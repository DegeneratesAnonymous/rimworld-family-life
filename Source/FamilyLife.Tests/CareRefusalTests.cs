using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>Refusing the doctor is not refusing check-ups: what each kind records, how long it lasts, what a change of doctor does to it, and how an old record is read.</summary>
    public class CareRefusalTests
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private const int Cool = AppointmentBackoff.RefusalDays * Day;

        private static FamilyRegistry Colony()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Doc"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }), T.P(4, "NewDoc", last: "Other"), T.P(20, "Solo", last: "Other"), T.P(21, "Solo2", last: "Other") }, 1000);
            return r;
        }

        private static CommunityOffice Healer(FamilyRegistry r, int holder = 1)
        {
            var o = new CommunityOffice { Id = 1, Kind = OfficeKind.Healer, HolderId = holder, HolderFamily = r.FamilyOf(holder), ElectedTick = 5000 };
            r.Offices.Add(o);
            return o;
        }

        private static void ChangeHealer(FamilyRegistry r, CommunityOffice o, int next) { o.HolderId = next; o.HolderFamily = r.FamilyOf(next); r.LapseHealerRefusals(next); }

        [Fact]
        public void A_refusal_of_the_doctor_records_who_was_refused_why_and_until_when()
        {
            var r = Colony(); var o = Healer(r);
            r.NoteRefusal(20, CareRefusalKind.HealerSpecific, CareRefusalReason.ReluctantWithHealer, 8000);
            var e = o.CareRecord().Refusals.Single();
            Assert.Equal(20, e.Patient); Assert.Equal(1, e.Healer); Assert.Equal(CareRefusalKind.HealerSpecific, e.Kind);
            Assert.Equal("reluctant_with_healer", e.Reason); Assert.Equal(8000, e.Tick); Assert.Equal(8000 + Cool, e.Until);
            Assert.True(r.HasRefused(20, 8000 + Day)); Assert.False(r.HasRefused(21, 8000 + Day));
        }

        [Fact]
        public void A_refusal_ends_at_its_expiry_and_asking_again_early_only_refreshes_it()
        {
            var r = Colony(); var o = Healer(r);
            r.NoteRefusal(20, CareRefusalKind.ServiceWide, CareRefusalReason.LowMood, 8000);
            Assert.True(r.HasRefused(20, 8000 + Cool - 1)); Assert.False(r.HasRefused(20, 8000 + Cool));
            r.NoteRefusal(20, CareRefusalKind.ServiceWide, CareRefusalReason.LowMood, 8000 + 2 * Day);
            Assert.Single(o.CareRecord().Refusals); Assert.Equal(8000 + 2 * Day + Cool, o.CareRecord().Refusals[0].Until);
        }

        [Fact]
        public void Replacing_the_doctor_lets_people_who_refused_that_doctor_be_asked_again_but_not_people_who_refused_check_ups()
        {
            var r = Colony(); var o = Healer(r);
            r.NoteRefusal(20, CareRefusalKind.HealerSpecific, CareRefusalReason.ReluctantWithHealer, 8000);
            r.NoteRefusal(21, CareRefusalKind.ServiceWide, CareRefusalReason.LowMood, 8000);
            Assert.True(r.HasRefused(20, 8100) && r.HasRefused(21, 8100));
            ChangeHealer(r, o, 4);
            Assert.False(r.HasRefused(20, 8100));          // would not see the old doctor; the new one may ask
            Assert.True(r.HasRefused(21, 8100));          // declined check-ups themselves: still left alone
            Assert.Equal(new[] { 21 }, o.CareRecord().Refusals.Select(x => x.Patient).ToArray());
            Assert.True(r.HasRefused(21, 8000 + Cool - 1)); Assert.False(r.HasRefused(21, 8000 + Cool));          // until their normal expiry
        }

        [Fact]
        public void A_refusal_of_the_old_doctor_does_not_apply_to_the_new_one_even_if_the_hand_over_hook_never_ran()
        {
            var r = Colony(); var o = Healer(r);
            r.NoteRefusal(20, CareRefusalKind.HealerSpecific, CareRefusalReason.ReluctantWithHealer, 8000);
            o.HolderId = 4;          // changed hands without the hook (a module that was not running)
            Assert.False(r.HasRefused(20, 8100));
            o.HolderId = 1;
            Assert.True(r.HasRefused(20, 8100));          // and the same doctor coming back is still refused until it expires
        }

        [Fact]
        public void Refusing_this_doctor_needs_a_doctor_and_an_unknown_kind_is_not_recorded()
        {
            var r = Colony(); var o = Healer(r); o.HolderId = 0;
            r.NoteRefusal(20, CareRefusalKind.HealerSpecific, CareRefusalReason.ReluctantWithHealer, 8000);
            r.NoteRefusal(20, (CareRefusalKind)9, "x", 8000);
            Assert.Empty(o.CareRecord().Refusals);
            r.NoteRefusal(20, CareRefusalKind.ServiceWide, CareRefusalReason.LowMood, 8000);          // declining check-ups needs no doctor
            Assert.Single(o.CareRecord().Refusals);
        }

        [Fact]
        public void The_reason_for_a_refusal_follows_what_is_behind_it()
        {
            var toward = CareParticipation.CauseOf(trust: 0f, tension: 70f, attachment: 0f, grievance: 0f, mood01: .7f);
            Assert.Equal(CareRefusalKind.HealerSpecific, toward.Kind); Assert.Equal(CareRefusalReason.ReluctantWithHealer, toward.Reason);
            var mood = CareParticipation.CauseOf(trust: 20f, tension: 0f, attachment: 10f, grievance: 0f, mood01: .05f);
            Assert.Equal(CareRefusalKind.ServiceWide, mood.Kind); Assert.Equal(CareRefusalReason.LowMood, mood.Reason);
            Assert.Equal(CareRefusalKind.HealerSpecific, CareParticipation.CauseOf(0f, 80f, 0f, 0f, .3f).Kind);          // mostly about the doctor, a little about mood
        }

        [Fact]
        public void A_save_that_recorded_only_the_patient_is_read_as_a_refusal_of_check_ups_with_the_same_cooldown_and_only_once()
        {
            string v1 = "roundStart=7000\nexamined=2,3\nrefused=20,21\nrefusedTick=61500,70000";
            var d = CareRecordData.Read(new StateReader(v1));
            Assert.Equal(2, d.Refusals.Count);
            Assert.All(d.Refusals, x => { Assert.Equal(CareRefusalKind.ServiceWide, x.Kind); Assert.Equal(CareRefusalReason.Legacy, x.Reason); Assert.Equal(0, x.Healer); });
            Assert.Equal(61500, d.Refusals[0].Tick); Assert.Equal(61500 + Cool, d.Refusals[0].Until);
            Assert.Equal(70000 + Cool, d.Refusals[1].Until);
            string v2 = d.Encode();          // saving writes the new form and never the old keys, so reading it again changes nothing
            Assert.DoesNotContain("refused=", v2);
            var again = CareRecordData.Read(new StateReader(v2));
            Assert.Equal(v2, again.Encode());
            Assert.Equal(2, again.Refusals.Count);
        }

        [Fact]
        public void A_legacy_refusal_without_a_tick_keeps_the_old_behaviour_of_expiring_within_the_cooldown_of_time_zero()
        {
            var e = CareRecordData.Read(new StateReader("refused=20")).Refusals.Single();
            Assert.Equal(0, e.Tick); Assert.True(e.ActiveAt(Day)); Assert.False(e.ActiveAt(Cool + 1));
        }

        [Fact]
        public void The_payload_round_trips_every_field_and_is_stable_when_written_again()
        {
            var d = new CareRecordData { RoundStartTick = 7000 };
            d.Refusals.Add(TestRefusals.Make(20, 1, CareRefusalKind.HealerSpecific, 6000, "reluctant_with_healer"));
            d.Refusals.Add(TestRefusals.Make(21, 0, CareRefusalKind.ServiceWide, 6500, "low,mood"));          // a comma in a reason must not break the lists
            string text = d.Encode();
            var back = CareRecordData.Read(new StateReader(text));
            Assert.Equal(2, back.Refusals.Count);
            Assert.Equal("low,mood", back.Refusals[1].Reason); Assert.Equal(CareRefusalKind.ServiceWide, back.Refusals[1].Kind); Assert.Equal(1, back.Refusals[0].Healer);
            Assert.Equal(text, back.Encode());
        }

        [Fact]
        public void Damaged_or_future_data_is_read_safely()
        {
            var d = CareRecordData.Read(new StateReader("refPatient=0,20,21\nrefHealer=1,1\nrefKind=1,1,7\nrefReason=a,b\nrefTick=5,6,7"));
            Assert.Equal(new[] { 20, 21 }, d.Refusals.Select(x => x.Patient).ToArray());
            Assert.Equal(CareRefusalKind.HealerSpecific, d.Refusals[0].Kind);
            Assert.Equal(CareRefusalKind.ServiceWide, d.Refusals[1].Kind);          // unknown kind: the cautious reading
            Assert.Equal(CareRefusalReason.Unspecified, d.Refusals[1].Reason);
            Assert.True(d.Refusals[1].Until > d.Refusals[1].Tick);
        }

        [Fact]
        public void Refusals_survive_a_save_and_the_payload_is_labelled_with_its_new_version()
        {
            var r = Colony(); var o = Healer(r);
            r.NoteRefusal(20, CareRefusalKind.HealerSpecific, CareRefusalReason.ReluctantWithHealer, 8000);
            r.NoteRefusal(21, CareRefusalKind.ServiceWide, CareRefusalReason.LowMood, 8100);
            var back = SaveSim.Reload(r).OfficeFor(OfficeKind.Healer);
            Assert.Equal(2, back.CareRecord().Refusals.Count);
            Assert.Equal(CareRefusalKind.HealerSpecific, back.CareRecord().Refusals.Single(x => x.Patient == 20).Kind);
            Assert.Equal(2, back.PayloadVersions[back.PayloadIds.IndexOf(CareRecordData.Id)]);
        }

        [Fact]
        public void An_old_record_is_stamped_version_two_the_first_time_the_game_saves_it()
        {
            var o = new CommunityOffice { Kind = OfficeKind.Healer, HolderId = 1 };
            o.PayloadIds.Add(CareRecordData.Id); o.PayloadData.Add("refused=20\nrefusedTick=500"); o.PayloadHolders.Add(0); o.PayloadVersions.Add(1);
            Assert.Single(o.CareRecord().Refusals);
            o.FlushState();
            int i = o.PayloadIds.IndexOf(CareRecordData.Id);
            Assert.Equal(2, o.PayloadVersions[i]); Assert.DoesNotContain("refused=", o.PayloadData[i]); Assert.Contains("refPatient=20", o.PayloadData[i]);
        }

        [Fact]
        public void Reconciling_drops_refusals_about_people_or_doctors_who_are_gone_expired_or_damaged_and_keeps_the_rest()
        {
            var r = Colony(); var o = Healer(r); var c = o.CareRecord();
            c.Refusals.Add(TestRefusals.Make(20, 1, CareRefusalKind.HealerSpecific, 8000));          // fine
            c.Refusals.Add(TestRefusals.Make(21, 0, CareRefusalKind.ServiceWide, 8000));          // fine
            c.Refusals.Add(TestRefusals.Make(999, 1, CareRefusalKind.ServiceWide, 8000));          // the patient is gone
            c.Refusals.Add(TestRefusals.Make(2, 777, CareRefusalKind.HealerSpecific, 8000));          // the doctor is gone
            c.Refusals.Add(TestRefusals.Make(3, 4, CareRefusalKind.HealerSpecific, 8000));          // a former doctor
            c.Refusals.Add(TestRefusals.Make(2, 0, CareRefusalKind.HealerSpecific, 8000));          // refuses "this doctor" with no doctor: damaged
            c.Refusals.Add(new CareRefusal { Patient = 0, Kind = CareRefusalKind.ServiceWide, Tick = 8000, Until = 9000 });
            Assert.Equal(5, r.ReconcileRefusals(8100));
            Assert.Equal(new[] { 20, 21 }, c.Refusals.Select(x => x.Patient).OrderBy(x => x).ToArray());
            Assert.Equal(2, r.ReconcileRefusals(8000 + Cool));          // both have now run out
        }

        [Fact]
        public void A_round_that_begins_asks_everyone_afresh_and_does_not_count_a_person_who_declined_as_missed()
        {
            var r = Colony(); var o = Healer(r);
            o.Care().NextRoundTick = 0;
            r.NoteRefusal(20, CareRefusalKind.ServiceWide, CareRefusalReason.LowMood, 900);
            Assert.True(r.BeginCheckupRound(1000, 30 * Day));
            Assert.DoesNotContain(20, o.Care().Missed);
            Assert.Empty(o.Care().Refusals);
        }
    }
}
