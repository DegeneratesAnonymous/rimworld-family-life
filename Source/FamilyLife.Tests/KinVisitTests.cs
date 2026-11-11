using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class KinVisitTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        // A house in the colony and two of its relatives who live elsewhere.
        private static FamilyRegistry World(out int family, out GatheringRequest request)
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Elder"), T.P(2, "Two", parents: new[] { 1 }),
                T.P(10, "Far", parents: new[] { 1 }, presence: Presence.Away), T.P(11, "Farther", parents: new[] { 1 }, presence: Presence.Away)
            }, 1000);
            family = r.FamilyOf(1);
            request = r.RequestCeremony(CeremonyKind.GrandReunion, family, 0, 0, 2000, 2 * Day, 8 * Day);
            request.VisitorIds.AddRange(new[] { 10, 11 });
            return r;
        }

        [Fact]
        public void Only_relatives_the_game_confirmed_are_brought_and_the_rest_are_left_off_the_request()
        {
            var r = World(out _, out var request);
            var visit = r.PlanKinVisit(request.Id, new[] { 10, 99 }, 2000);          // 11 not confirmed; 99 was never invited
            Assert.Equal(new[] { 10 }, visit.KinIds);
            Assert.Equal(new[] { 10 }, request.VisitorIds);
            Assert.Equal(KinVisitState.Travelling, visit.State);
            Assert.True(visit.ArriveTick > 2000 && visit.ArriveTick < request.NotBefore);
        }

        [Fact]
        public void With_nobody_safe_to_bring_no_visit_is_made_and_the_reunion_is_unchanged_in_kind()
        {
            var r = World(out _, out var request);
            Assert.Null(r.PlanKinVisit(request.Id, new int[0], 2000));
            Assert.Empty(request.VisitorIds);
            Assert.Empty(r.KinVisits);
        }

        [Fact]
        public void One_request_has_one_visit()
        {
            var r = World(out _, out var request);
            Assert.NotNull(r.PlanKinVisit(request.Id, new[] { 10, 11 }, 2000));
            Assert.Null(r.PlanKinVisit(request.Id, new[] { 10, 11 }, 2100));
        }

        [Fact]
        public void The_reunion_waits_for_guests_on_the_road_and_goes_ahead_once_they_arrive()
        {
            var r = World(out _, out var request);
            var visit = r.PlanKinVisit(request.Id, new[] { 10, 11 }, 2000);
            visit.Complication = false;
            Assert.DoesNotContain(r.DueRequests(request.NotBefore + 10), x => x.Id == request.Id);
            Assert.False(r.MarkKinArrived(visit.Id, new[] { 10 }, visit.ArriveTick - 1));       // not yet
            Assert.True(r.MarkKinArrived(visit.Id, new[] { 10, 11 }, visit.ArriveTick));
            Assert.Contains(r.DueRequests(request.NotBefore + 10), x => x.Id == request.Id);
            Assert.Equal(KinVisitState.Arrived, visit.State);
        }

        [Fact]
        public void If_nobody_can_be_placed_the_visit_fails_and_the_reunion_continues_without_them()
        {
            var r = World(out _, out var request);
            var visit = r.PlanKinVisit(request.Id, new[] { 10, 11 }, 2000); visit.Complication = false;
            Assert.True(r.MarkKinArrived(visit.Id, new int[0], visit.ArriveTick));
            Assert.Equal(KinVisitState.Failed, visit.State);
            Assert.Empty(request.VisitorIds);
            Assert.Contains(r.DueRequests(request.NotBefore + 10), x => x.Id == request.Id);
        }

        [Fact]
        public void Only_people_who_really_arrived_stay_on_the_request()
        {
            var r = World(out _, out var request);
            var visit = r.PlanKinVisit(request.Id, new[] { 10, 11 }, 2000); visit.Complication = false;
            r.MarkKinArrived(visit.Id, new[] { 11, 77 }, visit.ArriveTick);
            Assert.Equal(new[] { 11 }, request.VisitorIds);
            Assert.Equal(new[] { 11 }, visit.ArrivedIds);
        }

        [Fact]
        public void Shuttle_passengers_do_not_unblock_the_reunion_until_they_are_really_on_the_map()
        {
            var r = World(out _, out var request);
            var visit = r.PlanKinVisit(request.Id, new[] { 10, 11 }, 2000); visit.Complication = false;
            Assert.True(r.MarkKinLanding(visit.Id, new[] { 10, 11 }, new[] { 10, 11 }, visit.ArriveTick));
            Assert.Equal(KinVisitState.Landing, visit.State);
            Assert.True(r.KinVisitBlocks(request.Id));
            Assert.DoesNotContain(r.DueRequests(request.NotBefore + 10), x => x.Id == request.Id);

            Assert.True(r.MarkKinArrived(visit.Id, new[] { 10, 11 }, visit.ArriveTick + 100));
            Assert.Equal(KinVisitState.Arrived, visit.State);
            Assert.Empty(visit.LandingIds);
            Assert.Contains(r.DueRequests(request.NotBefore + 10), x => x.Id == request.Id);
        }

        [Fact]
        public void A_held_up_trip_can_be_rescued_in_time_and_the_relatives_are_grateful()
        {
            var r = World(out _, out var request);
            var visit = r.PlanKinVisit(request.Id, new[] { 10, 11 }, 2000); visit.Complication = true;
            Assert.False(r.StrandKinVisit(visit.Id, 55, visit.ArriveTick - 1));                      // not before they would have arrived
            Assert.True(r.StrandKinVisit(visit.Id, 55, visit.ArriveTick));
            Assert.Equal(KinVisitState.Stranded, visit.State);
            Assert.True(r.KinVisitBlocks(request.Id));
            Assert.True(request.Expires >= visit.RescueDeadlineTick);                                  // the reunion does not lapse while they are in trouble
            float before = r.GetBond(10, 2)?.Trust ?? 0f;
            Assert.True(r.RescueKinVisit(visit.Id, new[] { 2 }, visit.ArriveTick + Day));
            Assert.Equal(KinVisitState.Travelling, visit.State);
            Assert.True((r.GetBond(10, 2)?.Trust ?? 0f) > before);
            Assert.False(r.RescueKinVisit(visit.Id, new[] { 2 }, visit.ArriveTick + Day + 5));       // rescuing twice does nothing
        }

        [Fact]
        public void An_unanswered_call_for_help_ends_safely_with_no_arrival_and_no_changes_to_the_relatives()
        {
            var r = World(out _, out var request);
            var visit = r.PlanKinVisit(request.Id, new[] { 10, 11 }, 2000); visit.Complication = true;
            r.StrandKinVisit(visit.Id, 55, visit.ArriveTick);
            r.MaintainKinVisits(visit.RescueDeadlineTick + 1);
            Assert.Equal(KinVisitState.Failed, visit.State);
            Assert.Empty(request.VisitorIds);
            Assert.All(new[] { 10, 11 }, id => Assert.Equal(Presence.Away, r.GetPerson(id).Presence));
            r.MaintainKinVisits(visit.RescueDeadlineTick + 2 * Day);                                  // idempotent
            Assert.Equal(KinVisitState.Failed, visit.State);
            Assert.False(r.RescueKinVisit(visit.Id, new[] { 2 }, visit.RescueDeadlineTick + 5));      // too late
        }

        [Fact]
        public void A_rescue_after_the_deadline_is_refused()
        {
            var r = World(out _, out var request);
            var visit = r.PlanKinVisit(request.Id, new[] { 10 }, 2000); visit.Complication = true;
            r.StrandKinVisit(visit.Id, 55, visit.ArriveTick);
            Assert.False(r.RescueKinVisit(visit.Id, new[] { 2 }, visit.RescueDeadlineTick + 1));
        }

        [Fact]
        public void A_cancelled_reunion_drops_a_visit_that_has_not_begun()
        {
            var r = World(out _, out var request);
            var visit = r.PlanKinVisit(request.Id, new[] { 10 }, 2000);
            r.GatheringRequests.Remove(request);
            r.MaintainKinVisits(2500);
            Assert.Equal(KinVisitState.Expired, visit.State);
        }

        [Fact]
        public void The_stay_ends_on_time_and_the_visit_completes_only_after_leaving_begins()
        {
            var r = World(out _, out var request);
            var visit = r.PlanKinVisit(request.Id, new[] { 10 }, 2000); visit.Complication = false;
            r.MarkKinArrived(visit.Id, new[] { 10 }, visit.ArriveTick);
            Assert.False(r.BeginKinLeaving(visit.Id, visit.ArriveTick + Day));                         // still welcome
            Assert.False(r.CompleteKinVisit(visit.Id, visit.ArriveTick + Day));                        // cannot finish before leaving starts
            Assert.True(r.BeginKinLeaving(visit.Id, visit.StayEndTick));
            Assert.True(r.CompleteKinVisit(visit.Id, visit.StayEndTick + 100));
            Assert.Equal(KinVisitState.Completed, visit.State);
            Assert.False(r.CompleteKinVisit(visit.Id, visit.StayEndTick + 200));                       // idempotent
        }
    }
}
