using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class CrestTests
    {
        private static int Primary(Lineage h) => CrestStyle.Index(h.CrestPrimary, h.Id, CrestStyle.Colors);
        private static int Secondary(Lineage h) => CrestStyle.Index(h.CrestSecondary, 7, CrestStyle.Colors);

        [Fact]
        public void A_new_house_gets_its_own_crest_and_houses_are_told_apart()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, last: "Ash"), T.P(2, last: "Birch"), T.P(3, last: "Cedar"), T.P(4, last: "Dune"), T.P(5, last: "Elm") }, 1000);
            var houses = r.Lineages.ToList();
            Assert.Equal(5, houses.Count);
            Assert.All(houses, h => { Assert.InRange(h.CrestPrimary, 0, CrestStyle.Colors - 1); Assert.NotEqual(Primary(h), Secondary(h)); });
            Assert.Equal(5, houses.Select(Primary).Distinct().Count());                               // no two share a main color while colors remain
        }

        [Fact]
        public void The_same_house_always_gets_the_same_crest_so_games_are_reproducible()
        {
            var a = new FamilyRegistry(); a.Apply(new[] { T.P(1, last: "Ash"), T.P(2, last: "Birch") }, 1000);
            var b = new FamilyRegistry(); b.Apply(new[] { T.P(1, last: "Ash"), T.P(2, last: "Birch") }, 1000);
            Assert.Equal(a.Lineages.Select(h => (h.CrestPrimary, h.CrestSecondary, h.CrestMark, h.CrestPattern)), b.Lineages.Select(h => (h.CrestPrimary, h.CrestSecondary, h.CrestMark, h.CrestPattern)));
        }

        [Fact]
        public void Many_houses_vary_in_more_than_color()
        {
            var r = new FamilyRegistry();
            r.Apply(Enumerable.Range(1, 16).Select(i => T.P(i, last: "House" + i)).ToArray(), 1000);
            Assert.True(r.Lineages.Select(h => h.CrestMark).Distinct().Count() > 2);
            Assert.True(r.Lineages.Select(h => h.CrestPattern).Distinct().Count() > 1);
        }

        [Fact]
        public void A_branch_keeps_the_main_color_and_mark_of_its_parent_but_not_everything()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, last: "Ash"), T.P(2, last: "Ash", parents: new[] { 1 }) }, 1000);
            var parent = r.Lineages.First();
            var branch = r.CreateFamilyBranch(2, parent.Id, "Ash of the Hill", 2000);
            Assert.NotNull(branch);
            Assert.Equal(Primary(parent), Primary(branch));
            Assert.Equal(parent.CrestMark, branch.CrestMark);
            Assert.True(Secondary(branch) != Secondary(parent) || branch.CrestPattern != parent.CrestPattern);
            Assert.NotEqual(Primary(branch), Secondary(branch));
        }
    }
}
