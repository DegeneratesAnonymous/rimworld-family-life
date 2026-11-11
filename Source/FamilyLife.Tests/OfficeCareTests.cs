using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FamilyLife.Domain;
using Xunit;
using Xunit.Abstractions;

namespace FamilyLife.Tests
{
    public class OfficeCareTests
    {
        private readonly ITestOutputHelper output;
        public OfficeCareTests(ITestOutputHelper output) { this.output = output; }
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry Colony(int doctor = 1)
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Doc"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }), T.P(4, "Four", parents: new[] { 1 }),
                T.P(20, "Solo", last: "Other"), T.P(21, "Mate", last: "Other", parents: new[] { 20 }), T.P(22, "Third", last: "Other")
            }, 1000);
            var post = new CommunityOffice { Id = 1, Kind = OfficeKind.Healer, HolderId = doctor, HolderFamily = r.FamilyOf(doctor) };
            post.Care().RoundStartTick = 1000;
            r.Offices.Add(post);
            return r;
        }

        // ---------------------------------------------------------------- cooperative exchanges

        [Fact]
        public void A_socially_skilled_recipient_is_never_harder_to_help_in_a_cooperative_exchange()
        {
            foreach (var topic in Enum.GetValues(typeof(ExchangeTopic)).Cast<ExchangeTopic>())
                for (int skill = 0; skill <= 20; skill += 4)
                    for (int noise = -2; noise <= 2; noise++)
                    {
                        float d = CooperativeExchange.Delivery(skill, 8, 0f, 0f, 0f);
                        var plain = CooperativeExchange.Tier(d, CooperativeExchange.Difficulty(topic), CooperativeExchange.Engagement(0), noise);
                        var social = CooperativeExchange.Tier(d, CooperativeExchange.Difficulty(topic), CooperativeExchange.Engagement(20), noise);
                        Assert.True(social >= plain, topic + " skill " + skill);
                    }
            // and the contest that does use the other person's Social is still there for genuine disagreement
            Assert.True(GuidedExchanges.Resolve(15, 2, 10, 10) > GuidedExchanges.Resolve(15, 18, 10, 10));
        }

        [Fact]
        public void Professional_skill_dominates_the_luck_of_the_day()
        {
            // the unluckiest and worst-prepared skilled doctor still beats the luckiest, best-prepared novice
            float worstExpert = CheckupQuality.Score(16, 10, -2f, -100f, -2);
            float bestNovice = CheckupQuality.Score(3, 3, 2f, 100f, 2);
            Assert.True(worstExpert > bestNovice);
            // the day (at most two points either way) is less than the gap between five levels of Medicine
            Assert.True(5 * CheckupQuality.MedicineWeight > CheckupQuality.MaxNoise);
        }

        [Fact]
        public void Representative_doctors_in_poor_ordinary_and_good_rooms_give_dependable_results()
        {
            var rng = new Random(2026);
            var table = new List<string>();
            var dist = new Dictionary<string, int[]>();
            foreach (var room in new[] { ("poor", -1f), ("ordinary", 0f), ("good", 1.5f) })
                foreach (var doc in new[] { (3, 3), (8, 5), (12, 8), (16, 10) })
                {
                    var counts = new int[4]; const int N = 20000;
                    for (int n = 0; n < N; n++)
                    {
                        float talk = 0f;
                        foreach (var topic in new[] { ExchangeTopic.Advice, ExchangeTopic.Reassure, ExchangeTopic.Habits })
                        {
                            float d = CooperativeExchange.Delivery(doc.Item1, doc.Item2, 0f, room.Item2, 0f);
                            var t = CooperativeExchange.Tier(d, CooperativeExchange.Difficulty(topic), CooperativeExchange.Engagement(rng.Next(0, 13)), rng.Next(-2, 3));
                            talk += GuidedExchanges.TierValue(t) * GuidedExchanges.Weight(topic);
                        }
                        counts[(int)CheckupQuality.Level(CheckupQuality.Score(doc.Item1, doc.Item2, room.Item2, talk, rng.Next(-2, 3)))]++;
                    }
                    dist[room.Item1 + " M" + doc.Item1] = counts;
                    table.Add(string.Format("{0,-9} Medicine {1,2} / Social {2,2}:  Poor {3,5:P1}  Fair {4,5:P1}  Good {5,5:P1}  Excellent {6,5:P1}", room.Item1, doc.Item1, doc.Item2,
                        counts[0] / (float)N, counts[1] / (float)N, counts[2] / (float)N, counts[3] / (float)N));
                }
            foreach (var line in table) output.WriteLine(line);
            float Share(string k, int level) => dist[k][level] / (float)dist[k].Sum();
            Assert.True(Share("ordinary M3", 0) > .9f);                                 // a novice is dependably poor
            Assert.True(Share("ordinary M16", 3) + Share("ordinary M16", 2) > .99f);   // a master is dependably good or better
            Assert.True(Share("ordinary M16", 0) + Share("ordinary M16", 1) < .01f);
            Assert.True(Share("ordinary M12", 2) + Share("ordinary M12", 3) > .9f);
            Assert.True(Share("ordinary M8", 1) > .6f);                                  // a middling doctor is mostly fair
            Assert.True(Share("good M8", 2) + Share("good M8", 3) > Share("poor M8", 2) + Share("poor M8", 3));          // the room still matters
            Assert.True(Share("ordinary M3", 3) < .001f);
        }

        // ---------------------------------------------------------------- relationships

        [Fact]
        public void Routine_professional_contact_makes_people_familiar_before_it_makes_them_close()
        {
            var r = Colony();
            r.ChangeBond(22, 1, new BondChange(0f, 0f, 0f, 0f), 1400);          // make the bond exist, so its starting values can be read
            var before = r.BondsFrom(22).First(x => x.To == 1);
            float a0 = before.Attachment, t0 = before.Tension, tr0 = before.Trust, f0 = before.Familiarity;
            r.ProfessionalExchange(OfficeKind.Healer, 1, 22, ExchangeTopic.Advice, EventTier.Flat, 1500);
            var b = r.BondsFrom(22).First(x => x.To == 1);
            Assert.True(b.Familiarity > f0);
            Assert.Equal(a0, b.Attachment);
            Assert.Equal(t0, b.Tension);
            // trust comes from competent care, not from turning up
            Assert.Equal(tr0, b.Trust);
            r.ChangeBond(21, 1, new BondChange(0f, 0f, 0f, 0f), 1400);
            float tr1 = r.BondsFrom(21).First(x => x.To == 1).Trust;
            r.ProfessionalExchange(OfficeKind.Healer, 1, 21, ExchangeTopic.Advice, EventTier.Good, 1500);
            Assert.True(r.BondsFrom(21).First(x => x.To == 1).Trust > tr1);
        }

        [Fact]
        public void A_pair_s_contact_in_a_day_has_falling_returns_and_then_nothing()
        {
            var r = Colony();
            var gains = new List<float>();
            for (int i = 0; i < 6; i++)
            {
                float before = r.BondsFrom(22).FirstOrDefault(x => x.To == 1)?.Familiarity ?? 0f;
                r.ProfessionalExchange(OfficeKind.Healer, 1, 22, ExchangeTopic.Reassure, EventTier.Great, 1500 + i);
                gains.Add((r.BondsFrom(22).First(x => x.To == 1).Familiarity) - before);
            }
            Assert.True(gains[0] > gains[1] && gains[1] > gains[2]);
            Assert.Equal(0f, gains[3], 4);
            Assert.Equal(0f, gains[5], 4);
            // the next day starts again
            float again = r.BondsFrom(22).First(x => x.To == 1).Familiarity;
            r.ProfessionalExchange(OfficeKind.Healer, 1, 22, ExchangeTopic.Reassure, EventTier.Great, 1500 + Day);
            Assert.True(r.BondsFrom(22).First(x => x.To == 1).Familiarity > again);
        }

        [Fact]
        public void Closeness_grows_only_from_a_great_personal_moment_and_less_the_closer_they_already_are()
        {
            Assert.Equal(0f, ProfessionalContact.Effect(ExchangeTopic.Advice, EventTier.Great, 0f).Attachment);          // a clinical topic: never
            Assert.Equal(0f, ProfessionalContact.Effect(ExchangeTopic.Reassure, EventTier.Good, 0f).Attachment);       // not great enough
            float far = ProfessionalContact.Effect(ExchangeTopic.Reassure, EventTier.Great, 0f).Attachment;
            float near = ProfessionalContact.Effect(ExchangeTopic.Reassure, EventTier.Great, 50f).Attachment;
            Assert.True(far > near && near >= 0f);
            Assert.Equal(0f, ProfessionalContact.Effect(ExchangeTopic.Reassure, EventTier.Great, 80f).Attachment);
        }

        [Fact]
        public void Tension_rises_only_from_a_poor_exchange_and_a_month_of_routine_visits_does_not_make_a_best_friend()
        {
            Assert.True(ProfessionalContact.Effect(ExchangeTopic.Advice, EventTier.Poor, 0f).Tension > 0f);
            Assert.Equal(0f, ProfessionalContact.Effect(ExchangeTopic.Advice, EventTier.Great, 0f).Tension);
            var r = Colony();
            r.ChangeBond(22, 1, new BondChange(0f, 0f, 0f, 0f), 1400);
            float a0 = r.BondsFrom(22).First(x => x.To == 1).Attachment;
            for (int day = 0; day < 30; day++)
                for (int i = 0; i < 3; i++) r.ProfessionalExchange(OfficeKind.Healer, 1, 22, ExchangeTopic.Reassure, EventTier.Great, 1500 + day * Day + i);
            var b = r.BondsFrom(22).First(x => x.To == 1);
            Assert.True(b.Attachment - a0 < 10f, "attachment grew by " + (b.Attachment - a0));          // thirty days of the best possible visits: some warmth, nowhere near best friends
            Assert.True(b.Familiarity > 0f);
        }

        [Fact]
        public void Only_the_current_holder_of_the_post_can_have_a_professional_exchange()
        {
            var r = Colony();
            Assert.Equal(0f, r.ProfessionalExchange(OfficeKind.Healer, 3, 2, ExchangeTopic.Advice, EventTier.Good, 1500));
            Assert.Equal(0f, r.ProfessionalExchange(OfficeKind.Teacher, 1, 2, ExchangeTopic.Advice, EventTier.Good, 1500));
        }

        // ---------------------------------------------------------------- order of work, appointments and refusals

        [Fact]
        public void Tending_comes_before_routine_work_and_an_outbreak_assessment()
        {
            Assert.True(CarePriority.RoutineMayProceed(CareLevel.None));
            Assert.True(CarePriority.RoutineMayProceed(CareLevel.Routine));
            Assert.False(CarePriority.RoutineMayProceed(CareLevel.Ongoing));
            Assert.False(CarePriority.RoutineMayProceed(CareLevel.Emergency));
            Assert.False(CarePriority.OutbreakMayProceed(CareLevel.Ongoing));
            Assert.False(CarePriority.OutbreakMayProceed(CareLevel.Emergency));
            Assert.True(CarePriority.TendingFirst(CareLevel.Ongoing) && CarePriority.TendingFirst(CareLevel.Emergency) && !CarePriority.TendingFirst(CareLevel.Outbreak));
        }

        [Fact]
        public void A_person_who_cannot_travel_is_visited_a_busy_one_is_asked_later_and_a_refusal_is_respected()
        {
            Assert.Equal(PatientResponse.Unable, CareParticipation.Decide(true, true, 90f, 0));          // too unwell to travel: visited, whatever else
            Assert.Equal(PatientResponse.Busy, CareParticipation.Decide(false, true, 90f, 0));
            Assert.Equal(PatientResponse.Willing, CareParticipation.Decide(false, false, 10f, 0));
            Assert.Equal(PatientResponse.Refuses, CareParticipation.Decide(false, false, 80f, 0));
            Assert.Equal(PatientResponse.Willing, CareParticipation.Decide(false, false, 80f, 99));          // not the same answer every time
            // A refusal is remembered for days, and the person is not asked again meanwhile.
            var r = Colony();
            r.NoteRefusal(2, CareRefusalKind.ServiceWide, CareRefusalReason.LowMood, 5000);
            Assert.True(r.HasRefused(2, 5000 + Day));
            Assert.False(r.HasRefused(2, 5000 + (AppointmentBackoff.RefusalDays + 1) * Day));
            Assert.False(r.HasRefused(3, 5000));
        }

        [Fact]
        public void Asking_again_backs_off_and_then_the_person_is_simply_carried_over_with_no_escort()
        {
            var gaps = Enumerable.Range(1, AppointmentBackoff.MaxTries).Select(AppointmentBackoff.RetryAfter).ToList();
            for (int i = 1; i < gaps.Count; i++) Assert.True(gaps[i] > gaps[i - 1]);
            Assert.False(AppointmentBackoff.GiveUp(AppointmentBackoff.MaxTries - 1));
            Assert.True(AppointmentBackoff.GiveUp(AppointmentBackoff.MaxTries));
            // there is no state or constant in the domain for fetching or escorting a refused patient
            Assert.DoesNotContain(typeof(AppointmentBackoff).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => f.Name), n => n.Contains("Fetch") || n.Contains("Escort"));
        }

        [Fact]
        public void Kinship_makes_a_person_a_little_more_willing_and_never_less_and_changes_no_duration()
        {
            for (float tension = 0; tension <= 80; tension += 20)
                for (float trust = 0; trust <= 80; trust += 20)
                    Assert.True(CareParticipation.Reluctance(trust, tension, 10f, 0f, .6f, true, 0) <= CareParticipation.Reluctance(trust, tension, 10f, 0f, .6f, false, 0));
            // the quality and length of a check-up take no information about relatives at all
            Assert.Single(typeof(CheckupQuality).GetMethod(nameof(CheckupQuality.DurationFactor)).GetParameters());
            Assert.Equal(typeof(ResultQuality), typeof(CheckupQuality).GetMethod(nameof(CheckupQuality.DurationFactor)).GetParameters()[0].ParameterType);
            Assert.DoesNotContain("kin", typeof(CheckupQuality).GetMethod(nameof(CheckupQuality.Score)).GetParameters().Select(p => p.Name.ToLowerInvariant()));
        }

        [Fact]
        public void No_house_is_seen_first_but_the_people_missed_last_round_are()
        {
            var r = Colony();
            var o = r.OfficeFor(OfficeKind.Healer);
            // the doctor's own house used to go first; now it is simply the order of the list
            int first = r.NextCheckupPatient(1500, id => id != 1);
            Assert.Equal(2, first);
            o.Care().Missed = new List<int> { 22 };
            Assert.Equal(22, r.NextCheckupPatient(1500, null));
            o.Care().Missed.Clear();
            Assert.NotEqual(22, r.NextCheckupPatient(1500, null));
        }

        [Fact]
        public void A_round_that_runs_out_carries_the_unseen_over_and_marks_nobody_as_failed()
        {
            var r = Colony();
            var o = r.OfficeFor(OfficeKind.Healer);
            o.Care().Examined = new List<int> { 2, 3 };
            o.Care().Refusals = new List<CareRefusal> { TestRefusals.Make(21, 1, CareRefusalKind.ServiceWide, 1500) };
            o.Care().RoundStartTick = 1000; o.Care().NextRoundTick = 0;
            Assert.True(r.BeginCheckupRound(1000 + 40 * Day, 30 * Day));
            Assert.Contains(4, o.Care().Missed); Assert.Contains(20, o.Care().Missed); Assert.Contains(22, o.Care().Missed);
            Assert.DoesNotContain(2, o.Care().Missed);           // they were seen
            Assert.DoesNotContain(21, o.Care().Missed);          // they declined: that is their right, not something to chase
            Assert.Empty(o.Care().Refusals);
        }

        // ---------------------------------------------------------------- the benefit

        [Fact]
        public void A_check_up_benefit_never_stacks_and_a_later_poor_visit_cannot_change_a_better_one()
        {
            const int ticks = 1000000;
            var first = CheckupQuality.Merge(false, 0f, 0, ResultQuality.Good, ticks);
            Assert.True(first.Apply);
            Assert.Equal(CheckupQuality.Severity(ResultQuality.Good), first.Severity);
            var poorLater = CheckupQuality.Merge(true, first.Severity, 400000, ResultQuality.Poor, 600000);
            Assert.False(poorLater.Apply);          // left exactly as it was: not reduced, and not refreshed to be longer either
            Assert.Equal(first.Severity, poorLater.Severity);
            Assert.Equal(400000, poorLater.Remaining);
            var same = CheckupQuality.Merge(true, first.Severity, 400000, ResultQuality.Good, ticks);
            Assert.Equal(first.Severity, same.Severity);          // the same level does not add to itself
            Assert.Equal(ticks, same.Remaining);                   // it only restores the time
            var better = CheckupQuality.Merge(true, first.Severity, 400000, ResultQuality.Excellent, 1200000);
            Assert.Equal(CheckupQuality.Severity(ResultQuality.Excellent), better.Severity);
            Assert.True(CheckupQuality.Severity(ResultQuality.Excellent) <= 1f);
        }

        [Fact]
        public void An_interrupted_appointment_gets_no_full_result()
        {
            Assert.False(CheckupQuality.CountsAsPartial(300, 1500));
            Assert.False(CheckupQuality.CountsAsPartial(899, 1500));
            Assert.True(CheckupQuality.CountsAsPartial(900, 1500));
            Assert.False(CheckupQuality.CountsAsPartial(1500, 1500));          // a finished one is not partial, it is whole
            Assert.Equal(ResultQuality.Fair, CheckupQuality.Cap(ResultQuality.Excellent, true));
            Assert.Equal(ResultQuality.Excellent, CheckupQuality.Cap(ResultQuality.Excellent, false));
            Assert.Equal(ResultQuality.Poor, CheckupQuality.Cap(ResultQuality.Poor, true));
            Assert.True(CheckupQuality.PartialDuration < 1f);
        }

        // ---------------------------------------------------------------- workload

        [Fact]
        public void A_round_is_sized_to_the_colony_and_takes_a_bounded_share_of_the_doctor_s_day()
        {
            output.WriteLine("colony  visits/day  days to see everyone  window");
            foreach (int n in new[] { 8, 20, 40, 80 })
                output.WriteLine(string.Format("{0,5}  {1,10}  {2,20:0.0}  {3,6}", n, CheckupWorkload.VisitsPerDay(false), CheckupWorkload.DaysToSee(n, false), CheckupWorkload.WindowDays(n, false)));
            Assert.True(CheckupWorkload.DayShare(false) <= .25f);          // never more than a quarter of the waking day
            Assert.Equal(14, CheckupWorkload.WindowDays(8, false));
            Assert.Equal(14, CheckupWorkload.WindowDays(40, false));
            Assert.True(CheckupWorkload.WindowDays(80, false) > 14);
            Assert.True(CheckupWorkload.WindowDays(1000, false) <= CheckupWorkload.MaxWindowDays);
            // a visit's own cost fits in the budget the right number of times
            Assert.Equal(4, CheckupWorkload.VisitsPerDay(false));
            Assert.True(CheckupWorkload.VisitsPerDay(true) > CheckupWorkload.VisitsPerDay(false));
            // staggered: no visit within an hour of the last, and no more than the day's share
            Assert.False(CheckupWorkload.MayStart(0, false, CheckupWorkload.MinGapTicks - 1));
            Assert.True(CheckupWorkload.MayStart(0, false, CheckupWorkload.MinGapTicks));
            Assert.False(CheckupWorkload.MayStart(4, false, 100000));
        }

        [Fact]
        public void The_visits_budget_counts_per_day_and_an_older_save_with_no_window_keeps_the_original_fourteen_days()
        {
            var r = Colony();
            for (int i = 0; i < 4; i++) { Assert.True(r.MayVisit(100000 + i * 3000, false)); r.NoteVisit(100000 + i * 3000); }
            Assert.False(r.MayVisit(100000 + 5 * 3000, false));
            Assert.True(r.MayVisit(100000 + Day, false));
            var o = r.OfficeFor(OfficeKind.Healer);
            o.Care().RoundWindow = 0; o.Care().RoundStartTick = 1000;
            Assert.True(r.CheckupOpen(1000 + 13 * Day));
            Assert.False(r.CheckupOpen(1000 + 15 * Day));
        }

        // ---------------------------------------------------------------- saving, replacing the holder, authority

        [Fact]
        public void What_the_posts_remember_survives_a_save_and_a_load_without_repeating_anything()
        {
            var r = Colony();
            var o = r.OfficeFor(OfficeKind.Healer);
            r.NoteRefusal(2, CareRefusalKind.ServiceWide, CareRefusalReason.LowMood, 3000); o.Care().Missed = new List<int> { 4 }; o.Care().RoundWindow = 20 * Day; r.NoteVisit(5000);
            r.ProfessionalExchange(OfficeKind.Healer, 1, 3, ExchangeTopic.Advice, EventTier.Good, 6000);
            r.NoteCareQuality(3, ResultQuality.Good);
            o.Education().LessonLog.Add(new LessonRecord { Pupil = 3, Skill = "Cooking", Xp = 100f, Ticks = 4000, Possible = 5000, Classes = 1 }.Encode());
            o.Education().BookUse.Add("3|77|5000");
            var back = SaveSim.Reload(r).OfficeFor(OfficeKind.Healer);
            Assert.Equal(new[] { 2 }, back.Care().Refusals.Select(x => x.Patient).ToArray());
            Assert.Equal(new[] { 4 }, back.Care().Missed.ToArray());
            Assert.Equal(20 * Day, back.Care().RoundWindow);
            Assert.Equal(1, back.Care().RoutineUsed);
            Assert.Equal(new[] { 3 }, back.ContactIds.ToArray());
            Assert.Single(back.Education().LessonLog); Assert.Single(back.Education().BookUse);
            Assert.Equal(1, back.Care().OtherSeen + back.Care().KinSeen);
            // contact limits survive a reload too: the day's allowance is not reset by loading
            var reloaded = SaveSim.Reload(r);
            Assert.Equal(.5f, reloaded.ProfessionalExchange(OfficeKind.Healer, 1, 3, ExchangeTopic.Advice, EventTier.Good, 6001));
        }

        [Fact]
        public void A_new_holder_keeps_the_communitys_care_record_but_starts_their_own_working_state_and_a_brand_new_office_starts_empty()
        {
            var fresh = new CommunityOffice();
            Assert.Empty(fresh.Care().Missed); Assert.Empty(fresh.Care().Refusals); Assert.Empty(fresh.Education().LessonLog); Assert.Equal(0, fresh.Care().RoundWindow);
            var r = Colony();
            var o = r.OfficeFor(OfficeKind.Healer);
            r.NoteRefusal(2, CareRefusalKind.ServiceWide, CareRefusalReason.LowMood, 3000); o.Care().Missed = new List<int> { 4 }; o.Care().RoundWindow = 20 * Day; o.Care().KinSeen = 3; o.Care().KinQuality = 9f;
            o.Education().LessonLog.Add(new LessonRecord { Pupil = 3, Skill = "Cooking", Xp = 100f, Ticks = 4000, Possible = 5000, Classes = 1 }.Encode());
            Assert.True(r.ResignOffice(OfficeKind.Healer, 1, 9000));
            Assert.Single(o.Care().Refusals); Assert.Equal(new[] { 4 }, o.Care().Missed); Assert.Equal(20 * Day, o.Care().RoundWindow);          // the community's care record outlives the doctor: declines are still respected, the missed still go first
            Assert.Equal(0, o.Care().KinSeen); Assert.Equal(0f, o.Care().KinQuality);          // the doctor's own record of how they treated people does not pass to the next
            Assert.Single(o.Education().LessonLog);          // what the children studied is theirs, not the holder's
        }

        [Fact]
        public void Favouritism_is_judged_from_how_people_were_treated_not_from_who_they_are_related_to()
        {
            var r = Colony();
            Assert.Equal(0f, r.FavouritismGap(OfficeKind.Healer));
            for (int i = 0; i < 4; i++) r.NoteCareQuality(2, ResultQuality.Good);           // the doctor's own house
            for (int i = 0; i < 4; i++) r.NoteCareQuality(20, ResultQuality.Good);          // the other house, treated the same
            Assert.Equal(0f, r.FavouritismGap(OfficeKind.Healer));
            for (int i = 0; i < 4; i++) { r.NoteCareQuality(2, ResultQuality.Excellent); r.NoteCareQuality(21, ResultQuality.Poor); }
            Assert.True(r.FavouritismGap(OfficeKind.Healer) >= 1f);          // a real gap in treatment
        }

        [Fact]
        public void Only_someone_who_may_direct_the_post_can_reschedule_or_decline_a_routine_event()
        {
            var r = Colony();
            Assert.True(OfficeAuthority.MayDirect(r, 0, OfficeKind.Healer));          // the overview, or no one being played
            Assert.True(OfficeAuthority.MayDirect(r, 1, OfficeKind.Healer));          // the holder
            Assert.False(OfficeAuthority.MayDirect(r, 22, OfficeKind.Healer));        // a stranger to that house
            Assert.False(OfficeAuthority.MayDirect(r, 1, OfficeKind.Teacher));        // nobody holds that post
        }
    }
}
