using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class StartingFamiliesTests
    {
        private static StartingPerson P(int id, string last, bool child, bool male) => new StartingPerson { Id = id, LastName = last, IsChild = child, IsMale = male };

        private static List<StartingPerson> ThreeFamilies()
        {
            var people = new List<StartingPerson>();
            int id = 1;
            foreach (var last in new[] { "Alder", "Brandt", "Carver" })
            {
                people.Add(P(id++, last, false, true));
                people.Add(P(id++, last, false, false));
                people.Add(P(id++, last, true, true));
                people.Add(P(id++, last, true, false));
            }
            return people;
        }

        [Fact]
        public void Three_families_of_four_give_three_marriages_and_twelve_parent_links()
        {
            var plan = StartingFamilies.Plan(ThreeFamilies());
            Assert.Equal(3, plan.Spouses.Count);
            Assert.Equal(12, plan.ParentOf.Count);          // two children x two parents x three families
            Assert.All(plan.Spouses, s => Assert.Equal((s.Key - 1) / 4, (s.Value - 1) / 4));    // each marriage is inside one family
            foreach (var link in plan.ParentOf) Assert.Equal((link.Key - 1) / 4, (link.Value - 1) / 4);
        }

        [Fact]
        public void Order_does_not_matter_so_a_rerolled_or_reordered_setup_still_gives_the_same_families()
        {
            var people = ThreeFamilies();
            var shuffled = people.OrderBy(p => (p.Id * 7919) % 13).ToList();
            var a = StartingFamilies.Plan(people);
            var b = StartingFamilies.Plan(shuffled);
            Assert.Equal(a.Spouses.OrderBy(s => s.Key).Select(s => s.Key + "-" + s.Value), b.Spouses.OrderBy(s => s.Key).Select(s => s.Key + "-" + s.Value));
            Assert.Equal(a.ParentOf.OrderBy(s => s.Key).ThenBy(s => s.Value).Select(s => s.Key + ">" + s.Value),
                         b.ParentOf.OrderBy(s => s.Key).ThenBy(s => s.Value).Select(s => s.Key + ">" + s.Value));
        }

        [Fact]
        public void A_family_missing_a_parent_still_links_the_children_to_the_one_who_is_there_and_marries_nobody()
        {
            var plan = StartingFamilies.Plan(new[] { P(1, "Alder", false, true), P(2, "Alder", true, false), P(3, "Alder", true, true) });
            Assert.Empty(plan.Spouses);
            Assert.Equal(2, plan.ParentOf.Count);
            Assert.All(plan.ParentOf, l => Assert.Equal(1, l.Key));
        }

        [Fact]
        public void Only_two_adults_are_married_if_a_family_somehow_has_more()
        {
            var plan = StartingFamilies.Plan(new[]
            {
                P(1, "Alder", false, true), P(2, "Alder", false, false), P(3, "Alder", false, true), P(4, "Alder", false, false), P(5, "Alder", true, true)
            });
            Assert.Single(plan.Spouses);
            Assert.Equal(2, plan.ParentOf.Count);
        }

        [Fact]
        public void People_with_no_surname_or_no_family_are_left_alone()
        {
            var plan = StartingFamilies.Plan(new[] { P(1, "", false, true), P(2, "Solo", false, false) });
            Assert.Empty(plan.Spouses);
            Assert.Empty(plan.ParentOf);
        }

        [Fact]
        public void Surnames_are_distinct_and_there_are_enough()
        {
            var names = StartingFamilies.PickSurnames(3, new Random(5));
            Assert.Equal(3, names.Distinct().Count());
            Assert.Equal(StartingFamilies.Surnames.Length, StartingFamilies.Surnames.Distinct().Count());
            Assert.True(StartingFamilies.Surnames.Length >= 30);
            Assert.Empty(StartingFamilies.PickSurnames(0, new Random(1)));
        }
    }
}
