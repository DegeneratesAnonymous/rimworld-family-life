using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class ZoneTests
    {
        private static FamilyRegistry Family(out int lineage)
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }), T.P(9, "Away", "Smith", presence: Presence.Away, parents: new[] { 1, 2 })
            }, 100000);
            lineage = r.GetPerson(1).PrimaryLineageId;
            return r;
        }

        [Fact]
        public void A_family_with_no_zone_has_nobody_waiting_for_one()
        {
            var r = Family(out int l);
            Assert.Empty(r.PendingZoneMembers(l));
            Assert.Empty(r.LineagesWithZones());
        }

        [Fact]
        public void Assigning_a_zone_puts_every_colony_member_in_line_for_it_and_marking_applied_takes_them_out()
        {
            var r = Family(out int l);
            r.SetZone(l, 7);
            var pending = r.PendingZoneMembers(l);
            Assert.Contains(1, pending); Assert.Contains(2, pending); Assert.Contains(3, pending);
            Assert.DoesNotContain(9, pending);               // someone who is away is not given an area
            r.MarkZoneApplied(l, pending);
            Assert.Empty(r.PendingZoneMembers(l));
            Assert.Single(r.LineagesWithZones());
        }

        [Fact]
        public void A_new_family_member_is_picked_up_but_someone_already_given_the_zone_is_not_overruled()
        {
            var r = Family(out int l);
            r.SetZone(l, 7);
            r.MarkZoneApplied(l, r.PendingZoneMembers(l));
            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }), T.Kid(4, new[] { 1, 2 }), T.P(9, "Away", "Smith", presence: Presence.Away, parents: new[] { 1, 2 })
            }, 200000);
            Assert.Equal(new[] { 4 }, r.PendingZoneMembers(l).ToArray());
        }

        [Fact]
        public void Choosing_another_zone_gives_everyone_the_new_one_and_clearing_removes_it()
        {
            var r = Family(out int l);
            r.SetZone(l, 7);
            r.MarkZoneApplied(l, r.PendingZoneMembers(l));
            r.SetZone(l, 8);
            Assert.Equal(3, r.PendingZoneMembers(l).Count);
            r.ClearZone(l);
            Assert.Empty(r.PendingZoneMembers(l));
            Assert.Equal(0, r.GetLineage(l).ZoneAreaId);
        }

        [Fact]
        public void Unknown_families_and_negative_ids_are_harmless()
        {
            var r = Family(out int l);
            r.SetZone(999, 3);
            r.MarkZoneApplied(999, new[] { 1 });
            Assert.Empty(r.PendingZoneMembers(999));
            r.SetZone(l, -4);
            Assert.Equal(0, r.GetLineage(l).ZoneAreaId);
        }
    }
}
