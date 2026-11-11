using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class AssemblyEventTests
    {
        [Fact]
        public void The_assembly_is_a_gathering_of_its_own_kind_that_comes_due_at_once_and_is_not_planned_on_a_card()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1), T.P(2, parents: new[] { 1 }), T.P(3, parents: new[] { 1 }) }, 1000);
            int house = r.FamilyOf(1);
            var request = r.RequestCeremony(CeremonyKind.Assembly, house, 0, 0, 5000, 0, ActivityTuning.TicksPerDay);
            Assert.NotNull(request);
            Assert.Equal("assembly", request.Kind);
            Assert.Equal(CeremonyKind.Assembly, FamilyRegistry.CeremonyOf("assembly"));
            Assert.Contains(r.DueRequests(5000), q => q.Id == request.Id);
            Assert.Null(r.RequestCeremony(CeremonyKind.Assembly, house, 0, 0, 5001, 0, ActivityTuning.TicksPerDay));      // one at a time
        }

        [Fact]
        public void Completing_the_assembly_credits_the_host_house_and_is_remembered()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1), T.P(2, parents: new[] { 1 }) }, 1000);
            int house = r.FamilyOf(1);
            var request = r.RequestCeremony(CeremonyKind.Assembly, house, 0, 0, 5000, 0, ActivityTuning.TicksPerDay);
            r.MarkCeremonyStarted(request.Id, 1, 0, 5000, new[] { 1, 2 });
            var record = r.CompleteCeremony(request.Id, 6000, 2, 2, .8f);
            Assert.Equal(CeremonyKind.Assembly, record.Kind);
            Assert.True(r.GetLineage(house).Standing > 0f);
            Assert.DoesNotContain(r.GatheringRequests, q => q.Id == request.Id);
        }
    }
}
