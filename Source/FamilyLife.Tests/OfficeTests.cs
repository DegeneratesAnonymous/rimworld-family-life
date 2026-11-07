using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class OfficeTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        // Two houses of adults so a judge can sit outside the accused's house.
        private static FamilyRegistry Colony()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Elder"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }), T.P(4, "Four", parents: new[] { 1 }),
                T.P(20, "Solo", last: "Other"), T.P(21, "Mate", last: "Other", parents: new[] { 20 }), T.Kid(30, new[] { 1 })
            }, 1000);
            return r;
        }

        private static OfficeContext Skilled(int who, OfficeKind kind, float level = .8f, int player = 0)
            => new OfficeContext { Skill = (id, k) => id == who && k == kind ? level : (id == who ? 0f : .1f), Player = id => id == player && player != 0 };

        [Fact]
        public void The_most_capable_person_the_voters_trust_is_chosen_and_the_others_do_not_get_the_post()
        {
            var r = Colony();
            r.ChangeBond(2, 3, new BondChange(40f, 40f, 0f, 0f), 1500); r.ChangeBond(1, 3, new BondChange(40f, 40f, 0f, 0f), 1500);
            r.ElectOffices(2000, Skilled(3, OfficeKind.Healer));
            Assert.Equal(3, r.OfficeFor(OfficeKind.Healer).HolderId);
            Assert.True(r.OfficeFor(OfficeKind.Healer).TermEndTick > 2000);
            Assert.Null(r.OfficesHeldBy(2).FirstOrDefault());
        }

        [Fact]
        public void A_post_nobody_is_fit_for_stays_empty_and_one_person_holds_one_post()
        {
            var r = Colony();
            r.ElectOffices(2000, new OfficeContext { Skill = (id, k) => .1f });
            Assert.All(r.Offices, o => Assert.False(o.Held));
            var ctx = new OfficeContext { Skill = (id, k) => id == 3 ? .9f : .1f };
            r.ElectOffices(2000 + 3 * Day, ctx);
            Assert.Single(r.Offices, o => o.Held);          // the one capable person fills one post, not five
        }

        [Fact]
        public void A_player_controlled_pawn_is_never_a_candidate_or_a_voter()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Teacher, player: 3));
            Assert.False(r.OfficeFor(OfficeKind.Teacher).Held);
        }

        [Fact]
        public void Work_counts_once_per_piece_of_evidence_and_is_capped_each_day()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Healer));
            Assert.False(r.RecordOfficeWork(OfficeKind.Healer, 3, 0, 2100));                  // no evidence
            Assert.False(r.RecordOfficeWork(OfficeKind.Healer, 2, 11, 2100));                 // not the holder
            Assert.True(r.RecordOfficeWork(OfficeKind.Healer, 3, 11, 2100));
            Assert.False(r.RecordOfficeWork(OfficeKind.Healer, 3, 11, 2200));                 // the same evidence again
            Assert.True(r.RecordOfficeWork(OfficeKind.Healer, 3, 12, 2200));
            Assert.True(r.RecordOfficeWork(OfficeKind.Healer, 3, 13, 2300));
            Assert.False(r.RecordOfficeWork(OfficeKind.Healer, 3, 14, 2400));                 // the day's limit
            Assert.True(r.RecordOfficeWork(OfficeKind.Healer, 3, 15, 2400 + Day));            // a new day
            Assert.Equal(4, r.OfficeFor(OfficeKind.Healer).Work);
            Assert.True((r.PrestigeOf(3)?.Value ?? 0f) > 0f);
        }

        [Fact]
        public void A_worked_post_weighs_more_than_one_that_is_only_held()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Healer));
            float idle = r.OfficeWeight(3);
            for (int i = 0; i < 6; i++) r.RecordOfficeWork(OfficeKind.Healer, 3, 100 + i, 2100 + i * Day);
            Assert.True(r.OfficeWeight(3) > idle);
            Assert.Equal(0f, r.OfficeWeight(2));
        }

        [Fact]
        public void Ignoring_work_that_is_needed_costs_the_post_but_a_quiet_stretch_does_not()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Healer));
            r.NoteOfficeNeed(OfficeKind.Healer, false, 2000);
            r.MaintainOffices(2000 + 20 * Day);
            Assert.Equal(3, r.OfficeFor(OfficeKind.Healer).HolderId);                          // nothing needed, nothing ignored
            r.NoteOfficeNeed(OfficeKind.Healer, true, 2000 + 20 * Day);
            r.MaintainOffices(2000 + 25 * Day);
            Assert.Equal(3, r.OfficeFor(OfficeKind.Healer).HolderId);                          // not long enough yet
            r.MaintainOffices(2000 + 40 * Day);
            Assert.False(r.OfficeFor(OfficeKind.Healer).Held);
            Assert.Equal(OfficeEnd.Neglected, r.OfficeFor(OfficeKind.Healer).EndReason);
        }

        [Fact]
        public void Doing_the_needed_work_keeps_the_post()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Healer));
            r.NoteOfficeNeed(OfficeKind.Healer, true, 2000);
            r.RecordOfficeWork(OfficeKind.Healer, 3, 5, 2000 + 10 * Day);
            r.MaintainOffices(2000 + 20 * Day);
            Assert.Equal(3, r.OfficeFor(OfficeKind.Healer).HolderId);
        }

        [Fact]
        public void A_holder_who_leaves_loses_the_post()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Healer));
            r.Apply(new[] { T.P(1, "Elder"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }, presence: Presence.Away), T.P(4, "Four", parents: new[] { 1 }), T.P(20, "Solo", last: "Other"), T.P(21, "Mate", last: "Other", parents: new[] { 20 }) }, 2500);
            r.MaintainOffices(2500);
            Assert.False(r.OfficeFor(OfficeKind.Healer).Held);
            Assert.Equal(OfficeEnd.Left, r.OfficeFor(OfficeKind.Healer).EndReason);
        }

        [Fact]
        public void A_term_ends_and_the_colony_chooses_again()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Healer));
            int end = r.OfficeFor(OfficeKind.Healer).TermEndTick;
            var changed = r.ElectOffices(end + 1, Skilled(4, OfficeKind.Healer));
            Assert.Equal(4, r.OfficeFor(OfficeKind.Healer).HolderId);
            Assert.Contains(changed, o => o.Kind == OfficeKind.Healer);
        }

        [Fact]
        public void A_neglected_holder_cannot_simply_be_chosen_again_at_once()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Healer));
            r.NoteOfficeNeed(OfficeKind.Healer, true, 2000);
            r.MaintainOffices(2000 + 40 * Day);
            r.ElectOffices(2000 + 43 * Day, Skilled(3, OfficeKind.Healer));
            Assert.False(r.OfficeFor(OfficeKind.Healer).Held);
        }

        // ---- classes ----

        private static FamilyRegistry Teacher()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Teacher));
            return r;
        }

        [Fact]
        public void A_class_opens_when_due_and_the_next_one_waits_the_chosen_interval()
        {
            var r = Teacher();
            int every = 3 * Day;
            Assert.True(r.BeginClass(2100, every, new[] { 30 }));
            Assert.Equal(30, r.NextStudent(2200));
            Assert.False(r.BeginClass(2100 + Day, every, new[] { 30 }));                      // one class at a time, and not before it is due
            r.ClassDone(30);
            Assert.Equal(0, r.NextStudent(2300));
            Assert.False(r.BeginClass(2100 + 2 * Day, every, new[] { 30 }));
            Assert.True(r.BeginClass(2100 + every, every, new[] { 30 }));
        }

        [Fact]
        public void With_nobody_to_teach_the_class_is_not_used_up_and_is_held_when_someone_comes()
        {
            var r = Teacher();
            Assert.False(r.BeginClass(2100, 3 * Day, new int[0]));
            Assert.False(r.BeginClass(2100 + 10 * Day, 3 * Day, new int[0]));               // still waiting, however long
            Assert.True(r.BeginClass(2100 + 10 * Day, 3 * Day, new[] { 30 }));              // held at once when a student is free
        }

        [Fact]
        public void A_class_is_small_never_includes_the_teacher_and_a_stalled_one_ends()
        {
            var r = Teacher();
            Assert.True(r.BeginClass(2100, 3 * Day, new[] { 3, 30, 4, 20, 21, 1, 2, 5 }));
            Assert.DoesNotContain(3, r.OfficeFor(OfficeKind.Teacher).ClassQueue);
            Assert.True(r.OfficeFor(OfficeKind.Teacher).ClassQueue.Count <= FamilyRegistry.MaxClassSize);
            Assert.Equal(0, r.NextStudent(2100 + FamilyRegistry.ClassWindowTicks + 1));      // not finished within a day: over
            Assert.Empty(r.OfficeFor(OfficeKind.Teacher).ClassQueue);
        }

        // ---- check-ups ----

        private static FamilyRegistry Doctor()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Healer));
            return r;
        }

        [Fact]
        public void A_round_of_checkups_opens_when_due_and_each_person_is_seen_once()
        {
            var r = Doctor();
            int start = r.OfficeFor(OfficeKind.Healer).NextRoundTick;
            Assert.False(r.BeginCheckupRound(start - 1, 30 * Day));
            Assert.True(r.BeginCheckupRound(start, 30 * Day));
            Assert.False(r.BeginCheckupRound(start + 10, 30 * Day));                          // already open
            int first = r.NextCheckupPatient(start + 10, id => true);
            Assert.NotEqual(0, first);
            Assert.NotEqual(3, first);                                                          // the doctor is not their own patient
            Assert.True(r.RecordCheckup(3, first, start + 20));
            Assert.False(r.RecordCheckup(3, first, start + 30));                               // once per round
            Assert.NotEqual(first, r.NextCheckupPatient(start + 40, id => true));
        }

        [Fact]
        public void Only_the_doctor_can_examine_and_only_people_who_are_free_are_called()
        {
            var r = Doctor();
            int start = r.OfficeFor(OfficeKind.Healer).NextRoundTick;
            r.BeginCheckupRound(start, 30 * Day);
            Assert.False(r.RecordCheckup(2, 1, start + 10));
            Assert.Equal(20, r.NextCheckupPatient(start + 10, id => id == 20));                // only the one who can come
            Assert.Equal(0, r.NextCheckupPatient(start + 10, id => false));                     // nobody free: nobody called, nothing forced
        }

        [Fact]
        public void The_round_ends_when_everyone_has_been_seen_or_the_window_passes_and_the_next_waits_its_interval()
        {
            var r = Doctor();
            var office = r.OfficeFor(OfficeKind.Healer);
            int start = office.NextRoundTick;
            r.BeginCheckupRound(start, 30 * Day);
            foreach (var p in r.Persons.Where(x => x.InColony && x.Id != 3).Select(x => x.Id).ToList()) r.RecordCheckup(3, p, start + 100);
            Assert.False(r.CheckupOpen(start + 200));
            Assert.False(r.BeginCheckupRound(start + 10 * Day, 30 * Day));
            Assert.True(r.BeginCheckupRound(start + 30 * Day, 30 * Day));
            Assert.False(r.CheckupOpen(start + 30 * Day + FamilyRegistry.CheckupWindowTicks + 1));    // an unfinished round lapses
        }

        [Fact]
        public void Without_a_doctor_there_are_no_checkups()
        {
            var r = Colony();
            Assert.False(r.BeginCheckupRound(10 * Day, 30 * Day));
            Assert.Equal(0, r.NextCheckupPatient(10 * Day, id => true));
        }
    }
}
