using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class LineageTests
    {
        [Fact]
        public void Children_inherit_the_lineage_of_the_parent_who_shares_their_surname()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Alice", "Smith", spouses: new[] { 2 }),
                T.P(2, "Bob", "Jones", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }, "Smith"),
                T.Kid(4, new[] { 1, 2 }, "Jones")
            }, 100);

            Assert.Equal(r.GetPerson(1).PrimaryLineageId, r.GetPerson(3).PrimaryLineageId);
            Assert.Equal(r.GetPerson(2).PrimaryLineageId, r.GetPerson(4).PrimaryLineageId);
            Assert.NotEqual(r.GetPerson(1).PrimaryLineageId, r.GetPerson(2).PrimaryLineageId);
        }

        [Fact]
        public void With_no_matching_surname_the_lower_id_parent_decides()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(7, "Ann", "Ash", spouses: new[] { 5 }),
                T.P(5, "Ben", "Birch", spouses: new[] { 7 }),
                T.Kid(9, new[] { 5, 7 }, "Cedar")
            }, 100);

            Assert.Equal(r.GetPerson(5).PrimaryLineageId, r.GetPerson(9).PrimaryLineageId);
        }

        [Fact]
        public void A_spouse_from_another_lineage_is_affiliated_by_marriage_in_both_directions()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Alice", "Smith", spouses: new[] { 2 }),
                T.P(2, "Bob", "Jones", spouses: new[] { 1 })
            }, 100);

            var smith = r.GetLineage(r.GetPerson(1).PrimaryLineageId);
            var jones = r.GetLineage(r.GetPerson(2).PrimaryLineageId);
            Assert.True(smith.HasAffiliate(2));
            Assert.True(jones.HasAffiliate(1));
            Assert.Equal(AffiliationKind.Spouse, smith.Affiliations.Single().Kind);
        }

        [Fact]
        public void A_changed_surname_never_moves_anyone_between_lineages()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Alice", "Smith"), T.P(2, "Bob", "Jones") }, 100);
            int before = r.GetPerson(1).PrimaryLineageId;
            int lineages = r.Lineages.Count;

            r.Apply(new[] { T.P(1, "Alice", "Jones", spouses: new[] { 2 }), T.P(2, "Bob", "Jones", spouses: new[] { 1 }) }, 200);

            Assert.Equal(before, r.GetPerson(1).PrimaryLineageId);
            Assert.Equal(lineages, r.Lineages.Count);
        }

        [Fact]
        public void A_lineage_survives_the_death_of_its_founder()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Founder", "Smith", spouses: new[] { 2 }),
                T.P(2, "Spouse", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 })
            }, 100);
            int lineageId = r.GetPerson(1).PrimaryLineageId;

            r.Apply(new[]
            {
                T.P(1, "Founder", "Smith", presence: Presence.Dead, spouses: new[] { 2 }),
                T.P(2, "Spouse", "Smith", presence: Presence.Dead, spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 })
            }, 5000);

            Assert.NotNull(r.GetLineage(lineageId));
            Assert.Equal(1, r.LivingCount(lineageId, Presence.Colony));
            Assert.Equal(2, r.LivingCount(lineageId, Presence.Dead));
            Assert.Equal(1, r.GetLineage(lineageId).FounderId);
        }

        [Fact]
        public void Relatives_with_no_colonist_descendant_are_recorded_without_a_lineage()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Alice", "Smith", exes: new[] { 9 }),
                T.P(9, "Stranger", "Wolf", presence: Presence.Away, exes: new[] { 1 })
            }, 100);

            Assert.NotNull(r.GetPerson(9));
            Assert.Equal(0, r.GetPerson(9).PrimaryLineageId);
            Assert.Single(r.Lineages);
        }

        [Fact]
        public void A_parent_link_added_later_is_recognition_not_descent()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Parent", "Smith"), T.P(3, "Ward", "Lake", juvenile: true) }, 100);
            int wardLineage = r.GetPerson(3).PrimaryLineageId;
            int smithLineage = r.GetPerson(1).PrimaryLineageId;

            var events = r.Apply(new[] { T.P(1, "Parent", "Smith"), T.P(3, "Ward", "Lake", juvenile: true, parents: new[] { 1 }) }, 200);

            Assert.Equal(wardLineage, r.GetPerson(3).PrimaryLineageId);
            var smith = r.GetLineage(smithLineage);
            Assert.True(smith.HasAffiliate(3));
            Assert.Equal(AffiliationKind.Recognized, smith.Affiliations.Single(a => a.PersonId == 3).Kind);
            Assert.True(events.Has(FamilyEventKind.LineageAffiliated));
        }

        [Fact]
        public void Dead_ancestors_found_the_lineage_their_descendants_belong_to()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Grandpa", "Smith", presence: Presence.Dead),
                T.P(2, "Dad", "Smith", presence: Presence.Dead, parents: new[] { 1 }),
                T.P(3, "Kid", "Smith", parents: new[] { 2 })
            }, 100);

            var lineage = r.GetLineage(r.GetPerson(3).PrimaryLineageId);
            Assert.Equal(1, lineage.FounderId);
            Assert.Equal(3, r.MembersOf(lineage.Id).Count);
            Assert.Equal(2, r.GenerationIn(r.GetPerson(3)));
        }

        [Fact]
        public void A_cycle_in_damaged_parent_data_neither_hangs_nor_throws()
        {
            var r = new FamilyRegistry();
            var events = r.Apply(new[]
            {
                T.P(1, "A", "Smith", parents: new[] { 2 }),
                T.P(2, "B", "Smith", parents: new[] { 1 }),
                T.P(3, "Self", "Smith", parents: new[] { 3 })
            }, 100);

            Assert.NotNull(events);
            Assert.All(r.Persons, p => Assert.NotEqual(0, p.PrimaryLineageId));
            Assert.True(r.GenerationIn(r.GetPerson(1)) <= FamilyRegistry.AncestorDepthLimit);
        }

        [Fact]
        public void Unrelated_founders_with_the_same_surname_get_separate_lineages_with_distinct_names()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Ann", "Smith"), T.P(2, "Bea", "Smith") }, 100);

            Assert.Equal(2, r.Lineages.Count);
            var names = r.Lineages.Select(l => r.LineageDisplayName(l)).ToList();
            Assert.Equal(2, names.Distinct().Count());
        }

        [Fact]
        public void A_parent_who_is_not_observed_does_not_break_the_child()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.Kid(3, new[] { 404, 405 }) }, 100);

            Assert.NotEqual(0, r.GetPerson(3).PrimaryLineageId);
            Assert.Empty(r.SiblingsOf(3));
            Assert.Equal(new[] { 404, 405 }, r.GetPerson(3).ParentIds.OrderBy(x => x));
        }

        [Fact]
        public void Same_sex_parents_work_like_any_other()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Ann", "Smith", spouses: new[] { 2 }),
                T.P(2, "Bea", "Reed", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }, "Reed")
            }, 100);

            Assert.Equal(r.GetPerson(2).PrimaryLineageId, r.GetPerson(3).PrimaryLineageId);
            Assert.Equal(new[] { 1, 2 }, r.GetPerson(3).ParentIds.OrderBy(x => x));
        }

        [Fact]
        public void Half_siblings_are_siblings_and_stepchildren_are_not()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith"),
                T.P(2, "Dad1", "Smith"),
                T.P(3, "Dad2", "Lake"),
                T.Kid(10, new[] { 1, 2 }),
                T.Kid(11, new[] { 1, 3 }),
                T.Kid(12, new[] { 3 }, "Lake")
            }, 100);

            Assert.Contains(11, r.SiblingsOf(10));
            Assert.DoesNotContain(12, r.SiblingsOf(10));
            Assert.Contains(12, r.SiblingsOf(11));
        }

        [Fact]
        public void Two_founders_who_share_a_surname_and_marry_are_one_family_not_two()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Alice", "Smith", spouses: new[] { 2 }),
                T.P(2, "Bob", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 })
            }, 100);

            Assert.Single(r.Lineages);
            var smith = r.Lineages[0];
            Assert.Equal(1, smith.FounderId);
            Assert.Equal(0, r.GetPerson(2).PrimaryLineageId);
            Assert.True(smith.HasAffiliate(2));
            Assert.Equal(smith.Id, r.LineageIdFor(r.GetPerson(2)));
            Assert.Equal(smith.Id, r.GetPerson(3).PrimaryLineageId);
            Assert.Equal(3, r.LivingCount(smith.Id, Presence.Colony));     // the founder, the married-in spouse and the child
        }

        [Fact]
        public void Someone_who_marries_into_a_family_that_already_has_ancestry_yields_to_it_in_either_id_order()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Newcomer", "Smith", spouses: new[] { 5 }),
                T.P(4, "Grandpa", "Smith", presence: Presence.Dead),
                T.P(5, "Native", "Smith", parents: new[] { 4 }, spouses: new[] { 1 })
            }, 100);

            Assert.Single(r.Lineages);
            Assert.Equal(r.GetPerson(5).PrimaryLineageId, r.LineageIdFor(r.GetPerson(1)));
            Assert.Equal(0, r.GetPerson(1).PrimaryLineageId);
        }

        [Fact]
        public void A_married_in_spouse_does_not_found_a_new_family_after_a_divorce()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Alice", "Smith", spouses: new[] { 2 }), T.P(2, "Bob", "Smith", spouses: new[] { 1 }) }, 100);
            Assert.Single(r.Lineages);

            r.Apply(new[] { T.P(1, "Alice", "Smith", exes: new[] { 2 }), T.P(2, "Bob", "Smith", exes: new[] { 1 }) }, 200);

            Assert.Single(r.Lineages);
            Assert.Equal(0, r.GetPerson(2).PrimaryLineageId);
        }

        [Fact]
        public void A_child_of_a_married_in_parent_still_belongs_to_the_family_that_has_ancestry()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Alice", "Smith", spouses: new[] { 2 }),
                T.P(2, "Bob", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }),
                T.P(10, "Cara", "Lake", birth: -1_000_000, spouses: new[] { 3 }),
                T.Kid(11, new[] { 3, 10 }, "Smith")
            }, 100);

            Assert.Equal(r.GetPerson(3).PrimaryLineageId, r.GetPerson(11).PrimaryLineageId);
        }
    }
}
