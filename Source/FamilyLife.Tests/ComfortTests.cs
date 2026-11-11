using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class ComfortTests
    {
        private static FamilyRegistry World()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Mum", spouses: new[] { 2 }), T.P(2, "Dad", spouses: new[] { 1 }), T.Kid(3, new[] { 1, 2 }), T.P(4, "Grown", parents: new[] { 1, 2 }), T.P(9, "Stranger", last: "Other")
            }, 1000);
            return r;
        }

        [Fact]
        public void Only_a_partner_or_a_parent_may_try_and_a_grown_child_must_be_close()
        {
            var r = World();
            Assert.True(r.CanComfort(2, 1));                                    // partner
            Assert.True(r.CanComfort(1, 3));                                    // parent of a child
            Assert.False(r.CanComfort(3, 1));                                   // a child does not comfort a parent
            Assert.False(r.CanComfort(9, 1));                                   // a stranger
            Assert.False(r.CanComfort(1, 4));                                   // parent of a grown child who is not close
            r.ChangeBond(4, 1, new BondChange(60f, 0f, 0f, 0f), 1500);
            Assert.True(r.CanComfort(1, 4));
        }

        [Fact]
        public void Closeness_and_skill_help_and_friction_hurts()
        {
            var r = World();
            float cold = r.ComfortQuality(2, 1, 0f);
            r.ChangeBond(1, 2, new BondChange(60f, 60f, 0f, 0f), 1500);
            float close = r.ComfortQuality(2, 1, 0f);
            Assert.True(close > cold);
            Assert.True(r.ComfortQuality(2, 1, 1f) > close);                  // someone good with people does better
            r.ChangeBond(1, 2, new BondChange(0f, 0f, 80f, 0f), 1600);
            Assert.True(r.ComfortQuality(2, 1, 0f) < close);                  // but not through a fight
        }

        [Fact]
        public void A_loving_partner_who_is_good_with_people_can_talk_someone_back_and_a_cold_one_cannot()
        {
            var r = World();
            r.ChangeBond(1, 2, new BondChange(80f, 80f, 0f, 0f), 1500);
            Assert.Equal(ComfortResult.Resolved, FamilyRegistry.ClassifyComfort(r.ComfortQuality(2, 1, .8f)));
            var cold = World();
            cold.ChangeBond(1, 2, new BondChange(-40f, -40f, 40f, 0f), 1500);
            Assert.Equal(ComfortResult.Failed, FamilyRegistry.ClassifyComfort(cold.ComfortQuality(2, 1, .3f)));
        }

        [Fact]
        public void A_good_talk_brings_them_closer_and_a_failed_one_leaves_it_rawer()
        {
            var r = World();
            float before = r.GetBond(1, 2)?.Attachment ?? 0f;
            r.RecordComfort(2, 1, ComfortResult.Resolved, 2000);
            Assert.True((r.GetBond(1, 2)?.Attachment ?? 0f) > before);
            var f = World();
            float tension = f.GetBond(1, 2)?.Tension ?? 0f;
            f.RecordComfort(2, 1, ComfortResult.Failed, 2000);
            Assert.True((f.GetBond(1, 2)?.Tension ?? 0f) > tension);
        }
    }
}
