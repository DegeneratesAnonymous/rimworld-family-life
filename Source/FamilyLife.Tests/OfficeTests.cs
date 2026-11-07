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
        public void A_judge_rules_only_on_what_they_know_first_hand_and_never_on_their_own_house()
        {
            var r = Colony();
            r.ElectOffices(2000, Skilled(20, OfficeKind.Judge));
            Assert.Equal(20, r.OfficeFor(OfficeKind.Judge).HolderId);
            var unknown = r.RecordIncident(IncidentKind.Theft, 2, 3, 0, 2100, 30f, new[] { 2, 3 });                       // the judge was not there
            Assert.Equal(Verdict.None, r.HearAccusation(20, unknown.Id, 2200));
            var seen = r.RecordIncident(IncidentKind.Theft, 2, 3, 0, 2100, 30f, new[] { 2, 3, 20 });
            Assert.Equal(seen.Id, r.NextAccusationFor(20).Id);
            float before = r.GetLineage(r.FamilyOf(2)).Standing;
            Assert.Equal(Verdict.Upheld, r.HearAccusation(20, seen.Id, 2200));
            Assert.True(r.GetLineage(r.FamilyOf(2)).Standing < before);
            Assert.Equal(Verdict.None, r.HearAccusation(20, seen.Id, 2300));                                              // once only
            var own = r.RecordIncident(IncidentKind.Theft, 21, 2, 0, 2400, 30f, new[] { 20, 21, 2 });
            Assert.Equal(Verdict.None, r.HearAccusation(20, own.Id, 2500));                                               // their own house
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
    }
}
