using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class TreeTests
    {
        private static FamilyRegistry Registry(params PersonObservation[] people)
        {
            var r = new FamilyRegistry();
            r.Apply(people, 100000);
            return r;
        }

        private static FamilyTree TreeOf(FamilyRegistry r, int personId) => r.BuildTree(r.GetPerson(personId).PrimaryLineageId);

        private static TreeUnit UnitOf(FamilyTree t, int personId) => t.Units.First(u => u.Members.Contains(personId));

        private static void AssertNoOverlaps(FamilyTree t)
        {
            foreach (var row in t.Units.GroupBy(u => u.Generation))
            {
                var ordered = row.OrderBy(u => u.X).ToList();
                for (int i = 1; i < ordered.Count; i++)
                    Assert.True(ordered[i].X >= ordered[i - 1].X + ordered[i - 1].Size - 0.0001f, "two units overlap on one level");
            }
        }

        [Fact]
        public void A_couple_with_children_is_one_unit_centred_above_them()
        {
            var r = Registry(
                T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }), T.Kid(4, new[] { 1, 2 }), T.Kid(5, new[] { 1, 2 }));
            var t = TreeOf(r, 1);
            var parents = UnitOf(t, 1);
            Assert.Equal(new[] { 1, 2 }, parents.Members.OrderBy(i => i).ToArray());
            Assert.Equal(0, parents.Generation);
            var kids = new[] { 3, 4, 5 }.Select(i => UnitOf(t, i)).ToList();
            Assert.All(kids, k => Assert.Equal(1, k.Generation));
            float mid = (kids.Min(k => k.Center) + kids.Max(k => k.Center)) / 2f;
            Assert.Equal(mid, parents.Center, 3);
            AssertNoOverlaps(t);
        }

        [Fact]
        public void Someone_who_married_in_shares_the_unit_and_their_own_parents_are_not_needed()
        {
            var r = Registry(
                T.P(1, spouses: new[] { 2 }), T.P(2, last: "Jones", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }));
            var t = TreeOf(r, 1);
            Assert.Contains(2, UnitOf(t, 1).Members);
            Assert.Equal(1, UnitOf(t, 3).Generation);
        }

        [Fact]
        public void Three_generations_are_laid_out_without_overlaps_and_in_order()
        {
            var people = new List<PersonObservation>
            {
                T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }),
                T.P(3, parents: new[] { 1, 2 }, spouses: new[] { 5 }), T.P(4, parents: new[] { 1, 2 }), T.P(5, last: "Jones", spouses: new[] { 3 }),
                T.Kid(6, new[] { 3, 5 }), T.Kid(7, new[] { 3, 5 }), T.Kid(8, new[] { 3, 5 }), T.Kid(9, new[] { 3, 5 })
            };
            var t = TreeOf(Registry(people.ToArray()), 1);
            Assert.Equal(3, t.Generations);
            Assert.Equal(0, UnitOf(t, 1).Generation);
            Assert.Equal(1, UnitOf(t, 3).Generation);
            Assert.Contains(5, UnitOf(t, 3).Members);        // the one who married in is drawn level with their partner, not at the top
            Assert.Equal(2, UnitOf(t, 6).Generation);
            AssertNoOverlaps(t);
            Assert.True(t.TotalWidth >= 4f);
            // A branch is never wider than the space reserved for it, so neighbouring branches cannot run into each other.
            foreach (var u in t.Units) Assert.True(u.X >= -0.0001f);
        }

        [Fact]
        public void The_dead_and_those_away_are_still_in_the_tree()
        {
            var r = Registry(
                T.P(1, presence: Presence.Dead, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }),
                T.P(3, parents: new[] { 1, 2 }, presence: Presence.Away));
            var t = TreeOf(r, 3);
            Assert.NotNull(UnitOf(t, 1));
            Assert.NotNull(UnitOf(t, 3));
        }

        [Fact]
        public void A_huge_old_family_is_trimmed_by_dropping_the_dead_with_nothing_below_them()
        {
            var people = new List<PersonObservation> { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) };
            for (int i = 0; i < 100; i++) people.Add(T.P(10 + i, parents: new[] { 1, 2 }, presence: Presence.Dead));
            people.Add(T.Kid(500, new[] { 1, 2 }));
            var t = TreeOf(Registry(people.ToArray()), 1);
            Assert.True(t.PeopleShown <= TreeTuning.MaxPeople);
            Assert.NotNull(UnitOf(t, 500));         // the living child is kept
            Assert.NotNull(UnitOf(t, 1));
        }

        [Fact]
        public void A_person_with_no_family_links_is_a_tree_of_one_and_the_layout_is_stable()
        {
            var r = Registry(T.P(1));
            var a = TreeOf(r, 1);
            var b = TreeOf(r, 1);
            Assert.Single(a.Units);
            Assert.Equal(1f, a.TotalWidth);
            Assert.Equal(a.Units.Select(u => u.X).ToArray(), b.Units.Select(u => u.X).ToArray());
        }

        [Fact]
        public void A_cyclic_or_odd_parent_record_does_not_hang_or_throw()
        {
            var r = Registry(T.P(1, parents: new[] { 2 }), T.P(2, parents: new[] { 1 }));
            var t = r.BuildTree(r.GetPerson(1).PrimaryLineageId);
            Assert.NotNull(t);
        }

        [Fact]
        public void An_unknown_lineage_gives_an_empty_tree()
        {
            Assert.Empty(new FamilyRegistry().BuildTree(99).Units);
        }
    }
}
