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
            r.MaintainOffices(2000);
            Assert.True(r.OfficeFor(OfficeKind.Healer).TermEndTick > 2000);       // the term runs to the next assembly
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
            Assert.DoesNotContain(3, r.OfficeFor(OfficeKind.Teacher).SessionQueue);
            Assert.True(r.OfficeFor(OfficeKind.Teacher).SessionQueue.Count <= FamilyRegistry.MaxClassSize);
            Assert.Equal(0, r.NextStudent(2100 + FamilyRegistry.ClassWindowTicks + 1));      // not finished within a day: over
            Assert.Empty(r.OfficeFor(OfficeKind.Teacher).SessionQueue);
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

        // ---- the yearly assembly ----

        private static OfficeContext TwoGood(int incumbent, int challenger, float inc = .6f, float chal = .7f)
            => new OfficeContext { Skill = (id, k) => k != OfficeKind.Healer ? 0f : id == incumbent ? inc : id == challenger ? chal : .1f };

        private static FamilyRegistry Contested(int worked, float quality, out int start)
        {
            var r = Colony();
            r.ElectOffices(2000, TwoGood(3, 4, .6f, .1f));                                     // only 3 is fit at first
            for (int i = 0; i < worked; i++) r.RecordOfficeWork(OfficeKind.Healer, 3, 500 + i, 2100 + (i / 3) * Day, quality);
            r.MaintainOffices(2100 + 10 * Day);
            start = r.NextAssemblyTick;
            return r;
        }

        [Fact]
        public void The_assembly_is_scheduled_once_the_colony_is_big_enough_and_is_held_a_year_apart()
        {
            var r = Colony();
            r.MaintainOffices(2000);
            Assert.Equal(2000 + FamilyRegistry.AssemblyYearTicks, r.NextAssemblyTick);
            Assert.Null(r.CallAssembly(2000 + 5, TwoGood(3, 4)));                                  // not yet
            var a = r.CallAssembly(r.NextAssemblyTick, TwoGood(3, 4));
            Assert.NotNull(a);
            Assert.Equal(AssemblyState.Campaign, a.State);
            Assert.Null(r.CallAssembly(r.NextAssemblyTick, TwoGood(3, 4)));                        // one at a time, and the next is a year off
            Assert.True(r.NextAssemblyTick > a.StartedTick + FamilyRegistry.AssemblyYearTicks - 10);
        }

        [Fact]
        public void A_good_year_makes_the_incumbent_hard_to_beat_even_by_a_somewhat_better_challenger()
        {
            var r = Contested(21, 1f, out int start);
            Assert.True(r.OfficeGrade(OfficeKind.Healer) > .9f);
            var ctx = TwoGood(3, 4, .6f, .7f);
            var a = r.CallAssembly(start, ctx);
            Assert.Equal(3, a.Races.Single().IncumbentId);
            r.AdvanceAssembly(a.VotingTick);
            foreach (int v in r.AssemblyVoters(a)) r.CastAssemblyBallot(a.Id, v, ctx, a.VotingTick + 1);
            r.AdvanceAssembly(a.VotingTick + 2);
            Assert.Equal(AssemblyState.Counted, a.State);
            Assert.Equal(3, r.OfficeFor(OfficeKind.Healer).HolderId);
            Assert.Equal(3, a.Races.Single().WinnerId);
        }

        [Fact]
        public void A_poor_year_leaves_the_incumbent_open_to_a_better_challenger()
        {
            var r = Contested(0, .2f, out int start);
            Assert.True(r.OfficeGrade(OfficeKind.Healer) < .4f);
            var ctx = TwoGood(3, 4, .6f, .7f);
            var a = r.CallAssembly(start, ctx);
            r.AdvanceAssembly(a.VotingTick);
            foreach (int v in r.AssemblyVoters(a)) r.CastAssemblyBallot(a.Id, v, ctx, a.VotingTick + 1);
            r.AdvanceAssembly(a.VotingTick + 2);
            Assert.Equal(4, r.OfficeFor(OfficeKind.Healer).HolderId);
            Assert.Equal(3, r.OfficeFor(OfficeKind.Healer).LastHolderId);
        }

        [Fact]
        public void The_bias_is_a_tilt_not_a_guarantee()
        {
            Assert.True(FamilyRegistry.IncumbentBias(1f) > FamilyRegistry.IncumbentBias(.5f));
            Assert.True(FamilyRegistry.IncumbentBias(.5f) > FamilyRegistry.IncumbentBias(0f));
            Assert.True(FamilyRegistry.IncumbentBias(1f) < .5f);                                  // a large skill gap still counts for more
            Assert.True(FamilyRegistry.IncumbentBias(0f) < 0f);
        }

        [Fact]
        public void Only_adults_vote_once_each_and_the_count_waits_for_the_day_or_for_everyone()
        {
            var r = Contested(21, 1f, out int start);
            var ctx = TwoGood(3, 4);
            var a = r.CallAssembly(start, ctx);
            Assert.False(r.CastAssemblyBallot(a.Id, 1, ctx, start + 10));                          // not open until voting begins
            r.AdvanceAssembly(a.VotingTick);
            Assert.True(r.CastAssemblyBallot(a.Id, 1, ctx, a.VotingTick + 1));
            Assert.False(r.CastAssemblyBallot(a.Id, 1, ctx, a.VotingTick + 2));                    // once
            Assert.False(r.CastAssemblyBallot(a.Id, 30, ctx, a.VotingTick + 3));                   // a child
            Assert.Null(r.AdvanceAssembly(a.VotingTick + 100));
            Assert.NotNull(r.AdvanceAssembly(a.VotingTick + FamilyRegistry.AssemblyVotingTicks));  // the day is up: counted with whoever came
        }

        [Fact]
        public void An_incumbent_nobody_voted_against_carries_on_and_one_person_holds_only_one_post()
        {
            var r = Colony();
            var ctx = new OfficeContext { Skill = (id, k) => id == 3 ? .9f : .1f };
            r.ElectOffices(2000, ctx);
            r.MaintainOffices(2000);
            var a = r.CallAssembly(r.NextAssemblyTick, ctx);
            r.AdvanceAssembly(a.VotingTick);
            r.AdvanceAssembly(a.VotingTick + FamilyRegistry.AssemblyVotingTicks);                  // nobody came
            Assert.Equal(AssemblyState.Counted, a.State);
            Assert.True(r.OfficesHeldBy(3).Count() == 1);                                           // one person, one post
        }

        [Fact]
        public void A_house_head_may_back_one_candidate_per_post_and_only_while_campaigning()
        {
            var r = Contested(21, 1f, out int start);
            var ctx = TwoGood(3, 4, .6f, .7f);
            var a = r.CallAssembly(start, ctx);
            int house = r.FamilyOf(1), head = r.HeadOf(house);
            Assert.False(r.EndorseAssembly(a.Id, house, 2, OfficeKind.Healer, 4));                  // not the head
            Assert.False(r.EndorseAssembly(a.Id, house, head, OfficeKind.Healer, 99));              // not standing
            Assert.True(r.EndorseAssembly(a.Id, house, head, OfficeKind.Healer, 4));
            Assert.False(r.EndorseAssembly(a.Id, house, head, OfficeKind.Healer, 3));               // once
            r.AdvanceAssembly(a.VotingTick);
            Assert.False(r.EndorseAssembly(a.Id, r.FamilyOf(20), r.HeadOf(r.FamilyOf(20)), OfficeKind.Healer, 4));   // voting has begun
        }

        // ---- the chaplain and the hall keeper ----

        [Fact]
        public void The_chaplain_follows_the_ideoligion_and_is_never_voted_for_or_relieved_for_neglect()
        {
            var r = Colony();
            Assert.True(r.AppointOffice(OfficeKind.Chaplain, 2, 2000));
            Assert.False(r.AppointOffice(OfficeKind.Chaplain, 2, 2100));                           // same person: nothing changes
            r.ElectOffices(3000, new OfficeContext { Skill = (id, k) => .9f });
            Assert.Equal(2, r.OfficeFor(OfficeKind.Chaplain).HolderId);                              // an election never touches it
            r.NoteOfficeNeed(OfficeKind.Chaplain, true, 2000);
            r.MaintainOffices(2000 + 100 * Day);
            Assert.Equal(2, r.OfficeFor(OfficeKind.Chaplain).HolderId);
            Assert.True(r.AppointOffice(OfficeKind.Chaplain, 4, 3000));                              // the game names someone else
            Assert.Equal(4, r.OfficeFor(OfficeKind.Chaplain).HolderId);
            Assert.True(r.AppointOffice(OfficeKind.Chaplain, 0, 3100));                              // no moral guide any more
            Assert.False(r.OfficeFor(OfficeKind.Chaplain).Held);
            Assert.False(r.AppointOffice(OfficeKind.Healer, 2, 3200));                               // only the chaplain is appointed
        }

        [Fact]
        public void The_hall_keeper_serves_a_meal_and_the_next_waits_its_interval()
        {
            var r = Colony();
            r.ElectOffices(2000, new OfficeContext { Skill = (id, k) => k == OfficeKind.HallKeeper && id == 3 ? .8f : 0f });
            Assert.Equal(3, r.OfficeFor(OfficeKind.HallKeeper).HolderId);
            Assert.False(r.BeginHallMeal(2100, 3 * Day, new int[0]));                                // nobody to feed: not used up
            Assert.True(r.BeginHallMeal(2100, 3 * Day, new[] { 1, 2, 4, 3 }));
            Assert.DoesNotContain(3, r.OfficeFor(OfficeKind.HallKeeper).SessionQueue);               // the keeper does not serve themselves
            Assert.Equal(1, r.NextDiner(2200));
            r.DinerServed(1);
            Assert.Equal(2, r.NextDiner(2300));
            Assert.False(r.BeginHallMeal(2100 + Day, 3 * Day, new[] { 1 }));
        }

        [Fact]
        public void Quality_counts_as_well_as_quantity_in_the_grade()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(3, OfficeKind.Teacher));
            for (int i = 0; i < 30; i++) r.RecordOfficeWork(OfficeKind.Teacher, 3, 700 + i, 2100 + (i / 3) * Day, .2f);
            float poor = r.OfficeGrade(OfficeKind.Teacher);
            var r2 = Colony();
            r2.ElectOffices(2000, Skilled(3, OfficeKind.Teacher));
            for (int i = 0; i < 30; i++) r2.RecordOfficeWork(OfficeKind.Teacher, 3, 700 + i, 2100 + (i / 3) * Day, 1f);
            Assert.True(r2.OfficeGrade(OfficeKind.Teacher) > poor + .25f);
        }
    }
}
