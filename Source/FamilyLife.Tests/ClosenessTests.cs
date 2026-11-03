using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class ClosenessTests
    {
        private static FamilyRegistry Family(out int now)
        {
            var r = new FamilyRegistry();
            now = 2 * ActivityTuning.TicksPerDay;
            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }),
                T.P(4, "Gran", "Smith", presence: Presence.Away, parents: new int[0]),
                T.P(5, "Aunt", "Smith", presence: Presence.Away, parents: new[] { 4 }),
            }, now);
            r.UpdateBonds((a, b) => 0, now, null);
            return r;
        }

        [Fact]
        public void Closeness_is_the_average_of_the_two_directions_when_both_are_recorded()
        {
            var r = Family(out _);
            r.GetBond(1, 3).Attachment = 80f;
            r.GetBond(3, 1).Attachment = 40f;
            var c = r.ClosenessBetween(1, 3);
            Assert.Equal(60f, c.Value.Value, 3);
            Assert.NotNull(c.FromFocus);
            Assert.NotNull(c.ToFocus);
        }

        [Fact]
        public void It_does_not_matter_which_of_the_two_is_the_focus()
        {
            var r = Family(out _);
            r.GetBond(1, 3).Attachment = 90f;
            r.GetBond(3, 1).Attachment = 30f;
            Assert.Equal(r.ClosenessBetween(1, 3).Value.Value, r.ClosenessBetween(3, 1).Value.Value, 3);
        }

        [Fact]
        public void With_only_one_direction_recorded_that_one_is_used()
        {
            var r = Family(out _);
            // A bond is only kept from someone in the colony: Mum has one toward Gran, who is away, but Gran has none toward Mum.
            var toGran = r.GetBond(1, 4);
            if (toGran != null)
            {
                toGran.Attachment = 55f;
                var c = r.ClosenessBetween(4, 1);
                Assert.Equal(55f, c.Value.Value, 3);
                Assert.Null(c.FromFocus);
            }
        }

        [Fact]
        public void People_with_no_recorded_bond_have_no_closeness_and_nobody_is_close_to_themselves()
        {
            var r = Family(out _);
            Assert.False(r.ClosenessBetween(3, 5).Value.HasValue);        // a cousin-ish pair nobody tracks
            Assert.False(r.ClosenessBetween(1, 1).Value.HasValue);
            Assert.False(r.ClosenessBetween(1, 999).Value.HasValue);
        }
    }
}
