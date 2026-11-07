using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class FavorTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry World(out int giver, out int receiver)
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Giver"), T.P(2, "Kid", parents: new[] { 1 }), T.P(20, "Receiver", last: "Other") }, 1000);
            giver = r.FamilyOf(1); receiver = r.FamilyOf(20);
            return r;
        }

        [Fact]
        public void A_favor_exists_only_because_of_a_real_recorded_gift()
        {
            var r = World(out int giver, out _);
            Assert.Equal(0, r.RememberFavors(giver, 5 * Day));
            r.RecordIncident(IncidentKind.Gift, 1, 20, 0, 4 * Day, 10f, new[] { 1, 20 });
            Assert.Equal(1, r.RememberFavors(giver, 5 * Day));
            Assert.Equal(0, r.RememberFavors(giver, 5 * Day));                        // remembered once
            var favor = Assert.Single(r.Favors);
            Assert.Equal(20, favor.DebtorId);
        }

        [Fact]
        public void Small_gifts_and_gifts_within_a_house_create_nothing()
        {
            var r = World(out int giver, out _);
            r.RecordIncident(IncidentKind.Gift, 1, 20, 0, 4 * Day, 2f, new[] { 1, 20 });
            r.RecordIncident(IncidentKind.Gift, 1, 2, 0, 4 * Day, 20f, new[] { 1, 2 });
            Assert.Equal(0, r.RememberFavors(giver, 5 * Day));
        }

        [Fact]
        public void A_favor_is_settled_only_when_the_goods_really_arrive()
        {
            var r = World(out int giver, out int receiver);
            r.RecordIncident(IncidentKind.Gift, 1, 20, 0, 4 * Day, 10f, new[] { 1, 20 });
            r.RememberFavors(giver, 5 * Day);
            var favor = r.Favors.Single();
            var request = r.OpenSupplyRequest(1, 20, 50, 0, 2, SupplyKind.Food, SupplyApproach.Ask, 5 * Day);
            Assert.False(r.CallInFavor(favor.Id, 999, 5 * Day));
            Assert.True(r.CallInFavor(favor.Id, request.Id, 5 * Day));
            r.MaintainFavors(5 * Day + 10);
            Assert.Equal(FavorState.Called, favor.State);                            // still open
            request.State = SupplyState.Stored;
            float trust = r.GetBond(1, 20)?.Trust ?? 0f;
            r.MaintainFavors(5 * Day + 20);
            Assert.Equal(FavorState.Settled, favor.State);
            Assert.True((r.GetBond(1, 20)?.Trust ?? 0f) > trust);
        }

        [Fact]
        public void A_favor_that_is_refused_costs_the_debtor_regard()
        {
            var r = World(out int giver, out _);
            r.RecordIncident(IncidentKind.Gift, 1, 20, 0, 4 * Day, 10f, new[] { 1, 20 });
            r.RememberFavors(giver, 5 * Day);
            var favor = r.Favors.Single();
            var request = r.OpenSupplyRequest(1, 20, 50, 0, 2, SupplyKind.Food, SupplyApproach.Ask, 5 * Day);
            r.CallInFavor(favor.Id, request.Id, 5 * Day);
            request.State = SupplyState.Refused;
            r.MaintainFavors(5 * Day + 10);
            Assert.Equal(FavorState.Defaulted, favor.State);
            Assert.True((r.GetBond(1, 20)?.Tension ?? 0f) > 0f);
        }

        [Fact]
        public void An_uncalled_favor_lapses_and_a_request_from_the_wrong_houses_cannot_call_it()
        {
            var r = World(out int giver, out _);
            r.RecordIncident(IncidentKind.Gift, 1, 20, 0, 4 * Day, 10f, new[] { 1, 20 });
            r.RememberFavors(giver, 5 * Day);
            var favor = r.Favors.Single();
            var wrong = r.OpenSupplyRequest(20, 1, 50, 0, 2, SupplyKind.Food, SupplyApproach.Ask, 5 * Day);     // the debtor asking the creditor
            Assert.False(r.CallInFavor(favor.Id, wrong.Id, 5 * Day));
            r.MaintainFavors(5 * Day + FamilyRegistry.FavorLifeTicks + Day);
            Assert.Equal(FavorState.Lapsed, favor.State);
        }
    }
}
