using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class CampingTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry House(out int family)
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Elder"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }), T.Kid(4, new[] { 1 }) }, 1000);
            family = r.FamilyOf(1);
            return r;
        }

        private static CampContext Ctx(int avatar = 0) => new CampContext { IsAvatar = id => id == avatar && avatar != 0, Available = id => true, Skill = id => .1f };

        [Fact]
        public void A_house_with_nothing_drawing_it_together_does_not_want_a_trip()
        {
            var r = House(out int f);
            Assert.Null(r.AssessCamp(f, 5 * Day, Ctx()));
        }

        [Fact]
        public void A_quarrel_between_members_is_a_reason_to_go()
        {
            var r = House(out int f);
            r.Rifts.Add(new Rift { Id = 1, A = 2, B = 3, StartTick = 2000, Severity = 40f });
            Assert.Equal(CampMotive.Reconciliation, r.AssessCamp(f, 5 * Day, Ctx()));
        }

        [Fact]
        public void A_cohesive_family_may_simply_want_time_together()
        {
            var r = House(out int f);
            foreach (var pair in new[] { (1, 2), (2, 1), (1, 3), (3, 1), (2, 3), (3, 2) }) r.ChangeBond(pair.Item1, pair.Item2, new BondChange(40f, 40f, 0f, 0f), 2000);
            Assert.Equal(CampMotive.Recreation, r.AssessCamp(f, 5 * Day, Ctx()));
        }

        [Fact]
        public void A_proposal_never_starts_anything_and_the_player_must_authorize()
        {
            var r = House(out int f);
            var trip = r.ProposeCamp(f, CampMotive.Recreation, 77, 5 * Day, Ctx());
            Assert.Equal(CampState.Proposed, trip.State);
            Assert.False(r.BeginCampStay(trip.Id, new[] { 1, 2 }, 6 * Day));                // not authorized, so even people at the place do not start it
            Assert.True(r.AuthorizeCamp(trip.Id, 6 * Day));
            Assert.Equal(CampState.Authorized, trip.State);
        }

        [Fact]
        public void The_avatar_is_never_suggested_for_a_trip()
        {
            var r = House(out int f);
            var trip = r.ProposeCamp(f, CampMotive.Recreation, 77, 5 * Day, Ctx(avatar: 2));
            Assert.DoesNotContain(2, trip.Suggested);
        }

        [Fact]
        public void Declining_is_free_and_only_lengthens_the_wait()
        {
            var r = House(out int f);
            var trip = r.ProposeCamp(f, CampMotive.Recreation, 77, 5 * Day, Ctx());
            Assert.True(r.DeclineCamp(trip.Id, 5 * Day + 10));
            r.Rifts.Add(new Rift { Id = 1, A = 2, B = 3, StartTick = 2000, Severity = 40f });
            Assert.Null(r.AssessCamp(f, 20 * Day, Ctx()));                                  // still inside the longer wait
            Assert.Equal(CampMotive.Reconciliation, r.AssessCamp(f, 40 * Day, Ctx()));
        }

        [Fact]
        public void An_ignored_proposal_lapses_and_so_does_a_trip_nobody_set_out_on()
        {
            var r = House(out int f);
            var trip = r.ProposeCamp(f, CampMotive.Recreation, 77, 5 * Day, Ctx());
            r.MaintainCamps(5 * Day + 7 * Day);
            Assert.Equal(CampState.Expired, trip.State);
            var second = r.ProposeCamp(f, CampMotive.Recreation, 78, 20 * Day, Ctx());
            r.AuthorizeCamp(second.Id, 21 * Day);
            r.MaintainCamps(21 * Day + 11 * Day);
            Assert.Equal(CampState.Expired, second.State);
        }

        [Fact]
        public void The_stay_needs_two_of_the_house_really_at_the_place()
        {
            var r = House(out int f);
            var trip = r.ProposeCamp(f, CampMotive.Recreation, 77, 5 * Day, Ctx());
            r.AuthorizeCamp(trip.Id, 5 * Day);
            Assert.False(r.BeginCampStay(trip.Id, new[] { 1 }, 6 * Day));
            Assert.True(r.BeginCampStay(trip.Id, new[] { 1, 2 }, 6 * Day));
            Assert.Equal(CampState.Away, trip.State);
        }

        [Fact]
        public void Leaving_early_ends_the_trip_with_nothing_gained()
        {
            var r = House(out int f);
            var trip = r.ProposeCamp(f, CampMotive.Recreation, 77, 5 * Day, Ctx());
            r.AuthorizeCamp(trip.Id, 5 * Day); r.BeginCampStay(trip.Id, new[] { 1, 2, 3 }, 6 * Day);
            float before = r.GetBond(1, 2)?.Attachment ?? 0f;
            Assert.True(r.AdvanceCamp(trip.Id, new[] { 1 }, 6 * Day + Day / 4, Ctx()));
            Assert.Equal(CampState.Cut, trip.State);
            Assert.Equal(before, r.GetBond(1, 2)?.Attachment ?? 0f);
        }

        [Fact]
        public void A_full_stay_completes_once_and_a_good_one_deepens_bonds_and_eases_a_quarrel()
        {
            var r = House(out int f);
            r.Rifts.Add(new Rift { Id = 1, A = 2, B = 3, StartTick = 2000, Severity = 40f });
            var trip = r.ProposeCamp(f, CampMotive.Reconciliation, 77, 5 * Day, new CampContext { Available = id => true, IsAvatar = id => false, Skill = id => .8f, Weather = 1f });
            r.AuthorizeCamp(trip.Id, 5 * Day); r.BeginCampStay(trip.Id, new[] { 1, 2, 3 }, 6 * Day);
            var ctx = new CampContext { Skill = id => .8f, Weather = 1f };
            Assert.False(r.AdvanceCamp(trip.Id, new[] { 1, 2, 3 }, 6 * Day + trip.StayTicks - 5, ctx));            // not long enough
            float before = r.GetBond(1, 2)?.Attachment ?? 0f;
            Assert.True(r.AdvanceCamp(trip.Id, new[] { 1, 2, 3 }, 6 * Day + trip.StayTicks, ctx));
            Assert.Equal(CampState.Completed, trip.State);
            Assert.True(trip.Quality > 0f);
            Assert.True((r.GetBond(1, 2)?.Attachment ?? 0f) > before);
            Assert.True(r.Rifts[0].Severity < 40f);
            Assert.True(r.AdvanceCamp(trip.Id, new[] { 1, 2, 3 }, 7 * Day + trip.StayTicks, ctx));                // already over: nothing is applied again
        }

        [Fact]
        public void A_bad_stay_with_trouble_between_members_does_not_help_them()
        {
            var r = House(out int f);
            var trip = r.ProposeCamp(f, CampMotive.Recreation, 77, 5 * Day, Ctx());
            r.AuthorizeCamp(trip.Id, 5 * Day); r.BeginCampStay(trip.Id, new[] { 1, 2, 3 }, 6 * Day);
            r.RecordIncident(IncidentKind.Injury, 2, 3, 0, 6 * Day + 100, 40f, new[] { 1, 2, 3 });
            r.RecordIncident(IncidentKind.Theft, 3, 2, 0, 6 * Day + 200, 40f, new[] { 1, 2, 3 });
            r.ChangeBond(2, 3, new BondChange(-30f, -30f, 40f, 0f), 6 * Day + 300);
            float before = r.GetBond(2, 3)?.Attachment ?? 0f;
            r.AdvanceCamp(trip.Id, new[] { 1, 2, 3 }, 6 * Day + trip.StayTicks, new CampContext { Weather = -1f, Skill = id => 0f });
            Assert.True(trip.Quality < 0f);
            Assert.True((r.GetBond(2, 3)?.Attachment ?? 0f) <= before);
        }

        [Fact]
        public void A_house_has_one_open_trip_at_a_time()
        {
            var r = House(out int f);
            Assert.NotNull(r.ProposeCamp(f, CampMotive.Recreation, 77, 5 * Day, Ctx()));
            Assert.Null(r.ProposeCamp(f, CampMotive.Recreation, 78, 5 * Day, Ctx()));
        }
    }
}
