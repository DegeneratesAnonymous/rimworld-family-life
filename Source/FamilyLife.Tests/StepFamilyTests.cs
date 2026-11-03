using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class StepFamilyTests
    {
        private static FamilyRegistry Registry(params PersonObservation[] people)
        {
            var r = new FamilyRegistry();
            r.Apply(people, 100000);
            return r;
        }

        private static FamilyTree TreeOf(FamilyRegistry r, int personId) => r.BuildTree(r.GetPerson(personId).PrimaryLineageId);
        private static TreeUnit UnitOf(FamilyTree t, int personId) => t.Units.First(u => u.Members.Contains(personId));

        // ---------------------------------------------------------------- the tree

        [Fact]
        public void Divorced_parents_are_both_still_shown_joined_as_former_partners_with_the_children_between_them()
        {
            var r = Registry(
                T.P(1, "Dad", "Smith", exes: new[] { 2 }), T.P(2, "Mum", "Jones", exes: new[] { 1 }),
                T.P(3, "Kid", "Smith", parents: new[] { 1, 2 }), T.P(4, "Kid2", "Smith", parents: new[] { 1, 2 }));
            var t = TreeOf(r, 3);
            var parents = UnitOf(t, 1);
            Assert.Contains(2, parents.Members);
            Assert.Equal(TreeLink.Former, parents.Links.Single());
            var kid = UnitOf(t, 3);
            Assert.Equal(parents, t.Units[kid.ParentUnit]);
            Assert.Equal(1f, kid.ParentAnchorLocal, 3);       // exactly between the two parents
        }

        [Fact]
        public void A_second_marriage_reads_as_a_chain_with_each_set_of_children_under_its_own_parents()
        {
            var r = Registry(
                T.P(1, "Dad", "Smith", exes: new[] { 2 }),
                T.P(2, "Mum", "Jones", exes: new[] { 1 }, spouses: new[] { 5 }),
                T.P(5, "Step", "Jones", spouses: new[] { 2 }),
                T.P(3, "First", "Smith", parents: new[] { 1, 2 }),
                T.P(6, "Second", "Jones", parents: new[] { 2, 5 }));
            var t = TreeOf(r, 3);
            var chain = UnitOf(t, 1);
            Assert.Equal(new[] { 1, 2, 5 }, chain.Members.ToArray());                 // earlier partner, the person, the present partner
            Assert.Equal(new[] { TreeLink.Former, TreeLink.Partners }, chain.Links.ToArray());
            Assert.Equal(1f, UnitOf(t, 3).ParentAnchorLocal, 3);                      // first child between Dad and Mum
            Assert.Equal(2f, UnitOf(t, 6).ParentAnchorLocal, 3);                      // second child between Mum and the stepparent
            Assert.True(UnitOf(t, 3).Center < UnitOf(t, 6).Center);
        }

        [Fact]
        public void A_person_with_two_exes_and_a_present_partner_keeps_the_present_couple_together_and_arcs_the_other_ex()
        {
            // Capello (1) has two former partners and a present partner with whom he has two children.
            var r = Registry(
                T.P(1, "Capello", "Capello", exes: new[] { 2, 3 }, spouses: new[] { 4 }),
                T.P(2, "Birch", "Capello", exes: new[] { 1 }),
                T.P(3, "Estelle", "Shubin", exes: new[] { 1 }),
                T.P(4, "Ruspy", "Ruspy", spouses: new[] { 1 }),
                T.P(5, "Grunt", "Capello", parents: new[] { 1, 4 }),
                T.P(6, "Prissy", "Capello", parents: new[] { 1, 4 }));
            var t = TreeOf(r, 1);
            var unit = UnitOf(t, 1);
            Assert.Equal(4, unit.Members.Count);
            int c = unit.Members.IndexOf(1), ruspy = unit.Members.IndexOf(4);
            Assert.Equal(1, System.Math.Abs(c - ruspy));                          // the present couple are side by side
            Assert.Equal(ruspy > c ? c + 1f : ruspy + 1f, UnitOf(t, 5).ParentAnchorLocal, 3);   // their children hang from between them
            Assert.Equal(UnitOf(t, 5).ParentAnchorLocal, UnitOf(t, 6).ParentAnchorLocal, 3);
            Assert.Equal(1, unit.Arcs.Count);                                      // one ex cannot be next to Capello as well
            Assert.True(ruspy > c, "present partners are drawn to the right of the person");
        }

        [Fact]
        public void The_former_partner_the_person_had_children_with_is_the_one_kept_beside_them()
        {
            // Three former partners, only one of whom (Mum, drawn last by age) had children with him.
            var r = Registry(
                T.P(1, "Dad", "Smith", exes: new[] { 2, 3, 4 }),
                T.P(2, "ExA", "Jones", exes: new[] { 1 }),
                T.P(3, "ExB", "Brown", exes: new[] { 1 }),
                T.P(4, "Mum", "Green", exes: new[] { 1 }),
                T.P(5, "Kid", "Smith", parents: new[] { 1, 4 }));
            var unit = UnitOf(TreeOf(r, 1), 1);
            Assert.Equal(1, System.Math.Abs(unit.Members.IndexOf(1) - unit.Members.IndexOf(4)));
            Assert.Equal(1f + System.Math.Min(unit.Members.IndexOf(1), unit.Members.IndexOf(4)), UnitOf(TreeOf(r, 1), 5).ParentAnchorLocal, 3);
        }

        [Fact]
        public void A_present_partner_is_drawn_to_the_right_even_when_they_are_the_older_of_the_two()
        {
            var r = Registry(
                T.P(1, "Ruspy", "Ruspy", spouses: new[] { 2 }),
                T.P(2, "Capello", "Capello", spouses: new[] { 1 }, exes: new[] { 3 }),
                T.P(3, "Birch", "Capello", exes: new[] { 2 }),
                T.P(4, "Kid", "Capello", parents: new[] { 1, 2 }));
            var unit = UnitOf(TreeOf(r, 2), 2);
            Assert.True(unit.Members.IndexOf(1) > unit.Members.IndexOf(2), "the present partner should be on the right of the person with an earlier partner");
            Assert.Equal(TreeLink.Former, unit.Links[0]);
        }

        [Fact]
        public void A_founder_and_the_spouse_they_married_are_not_a_second_family_for_the_same_couple()
        {
            // Lorall belongs to the Gamare family (with a sibling who is away); Nila's own family is only Nila.
            var r = Registry(
                T.P(1, "Gamare", "Gamare", presence: Presence.Away),
                T.P(2, "Trev", "Gamare", parents: new[] { 1 }, presence: Presence.Away),
                T.P(3, "Lorall", "Gamare", parents: new[] { 1 }, spouses: new[] { 4 }),
                T.P(4, "Nila", "Reyan", spouses: new[] { 3 }));
            Assert.True(r.IsListedFamily(r.GetPerson(3).PrimaryLineageId));      // the Gamare family
            Assert.False(r.IsListedFamily(r.GetPerson(4).PrimaryLineageId));     // "the Reyan family" is just Nila and her husband
        }

        [Fact]
        public void Someone_whose_own_family_is_just_a_couple_is_shown_the_family_they_married_into()
        {
            var r = Registry(
                T.P(1, "Gamare", "Gamare", presence: Presence.Away),
                T.P(2, "Trev", "Gamare", parents: new[] { 1 }, presence: Presence.Away),
                T.P(3, "Lorall", "Gamare", parents: new[] { 1 }, spouses: new[] { 4 }),
                T.P(4, "Nila", "Reyan", spouses: new[] { 3 }));
            Assert.Equal(r.GetPerson(3).PrimaryLineageId, r.FamilyToShow(r.GetPerson(4)));   // Nila sees the Gamare family
            Assert.Equal(r.GetPerson(3).PrimaryLineageId, r.FamilyToShow(r.GetPerson(3)));
            Assert.Equal(0, r.FamilyToShow(null));
        }

        [Fact]
        public void The_line_to_a_child_who_has_a_partner_comes_to_the_child_not_to_the_couple()
        {
            var r = Registry(
                T.P(1, "Gamare", "Gamare"),
                T.P(2, "Nila", "Reyan", spouses: new[] { 4 }),                      // older, so drawn first in the couple
                T.P(3, "Trev", "Gamare", parents: new[] { 1 }),
                T.P(4, "Lorall", "Gamare", parents: new[] { 1 }, spouses: new[] { 2 }));
            var t = TreeOf(r, 4);
            var couple = UnitOf(t, 4);
            Assert.Equal(1, couple.Members.IndexOf(4));
            Assert.Equal(couple.Members.IndexOf(4), couple.ChildMember);          // Lorall is the child, Nila is not
            Assert.NotEqual(couple.Members.IndexOf(2), couple.ChildMember);
        }

        [Fact]
        public void A_family_with_only_one_person_in_the_colony_is_not_listed()
        {
            var r = Registry(
                T.P(1, "Dad", "Smith", presence: Presence.Away), T.P(2, "Kid", "Smith", parents: new[] { 1 }, presence: Presence.Away),
                T.P(3, "Kid2", "Smith", parents: new[] { 1 }));
            Assert.False(r.IsListedFamily(r.GetPerson(3).PrimaryLineageId));
            Assert.False(r.IsListedFamily(99));
        }

        [Fact]
        public void A_former_partner_with_no_children_together_is_still_drawn_beside_the_person()
        {
            var r = Registry(T.P(1, "Dad", "Smith", exes: new[] { 2 }), T.P(2, "Ex", "Jones", exes: new[] { 1 }));
            var t = TreeOf(r, 1);
            var unit = UnitOf(t, 1);
            Assert.Contains(2, unit.Members);
            Assert.Equal(TreeLink.Former, unit.Links.Single());
        }

        [Fact]
        public void Step_relationships_are_worked_out_from_the_parents_present_partners()
        {
            var r = Registry(
                T.P(1, "Dad", "Smith", exes: new[] { 2 }),
                T.P(2, "Mum", "Jones", exes: new[] { 1 }, spouses: new[] { 5 }),
                T.P(5, "Step", "Jones", spouses: new[] { 2 }),
                T.P(3, "First", "Smith", parents: new[] { 1, 2 }),
                T.P(6, "Second", "Jones", parents: new[] { 2, 5 }));
            Assert.Equal(new[] { 5 }, r.StepParentsOf(3).ToArray());
            Assert.Empty(r.StepParentsOf(6));                              // both of their parents are their own
            Assert.Contains(3, r.StepChildrenOf(5));
            Assert.DoesNotContain(6, r.StepChildrenOf(5));
        }

        [Fact]
        public void Parents_who_were_never_recorded_as_partners_are_still_both_drawn_with_a_link_to_the_child()
        {
            var r = Registry(
                T.P(1, "Dad", "Smith"), T.P(2, "Mum", "Jones"),
                T.P(3, "Kid", "Smith", parents: new[] { 1, 2 }));
            var t = TreeOf(r, 3);
            Assert.NotNull(UnitOf(t, 1));
            Assert.NotNull(UnitOf(t, 2));
            Assert.Single(t.CoParentLinks);
        }

        [Fact]
        public void A_half_sibling_from_the_other_parents_new_family_is_drawn_under_that_parent()
        {
            var r = Registry(
                T.P(1, "Dad", "Smith", exes: new[] { 2 }), T.P(2, "Mum", "Jones", exes: new[] { 1 }, spouses: new[] { 5 }),
                T.P(5, "Step", "Jones", spouses: new[] { 2 }),
                T.P(3, "First", "Smith", parents: new[] { 1, 2 }), T.P(6, "Half", "Jones", parents: new[] { 2, 5 }));
            var t = TreeOf(r, 3);
            Assert.NotNull(UnitOf(t, 6));
            Assert.Equal(UnitOf(t, 1), t.Units[UnitOf(t, 6).ParentUnit]);
        }

        // ---------------------------------------------------------------- the joining event

        [Fact]
        public void Two_families_joining_by_a_partnership_is_an_event()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "A", "Smith"), T.P(2, "B", "Jones") }, 1000);
            var events = r.Apply(new[] { T.P(1, "A", "Smith", spouses: new[] { 2 }), T.P(2, "B", "Jones", spouses: new[] { 1 }) }, 2000);
            Assert.True(events.Has(FamilyEventKind.PartnershipFormed));
            Assert.True(events.Has(FamilyEventKind.FamiliesJoined));
            Assert.True(r.Chronicle.Any(e => e.Kind == FamilyEventKind.FamiliesJoined));
        }

        [Fact]
        public void A_partnership_within_one_family_does_not_join_two_families()
        {
            var r = new FamilyRegistry();
            var before = new[]
            {
                T.P(1, "Gran", "Smith"), T.P(2, "Aunt", "Smith", parents: new[] { 1 }), T.P(3, "Uncle", "Smith", parents: new[] { 1 }),
                T.P(4, "CousinA", "Smith", parents: new[] { 2 }), T.P(5, "CousinB", "Smith", parents: new[] { 3 })
            };
            r.Apply(before, 1000);
            var events = r.Apply(new[]
            {
                T.P(1, "Gran", "Smith"), T.P(2, "Aunt", "Smith", parents: new[] { 1 }), T.P(3, "Uncle", "Smith", parents: new[] { 1 }),
                T.P(4, "CousinA", "Smith", parents: new[] { 2 }, spouses: new[] { 5 }), T.P(5, "CousinB", "Smith", parents: new[] { 3 }, spouses: new[] { 4 })
            }, 2000);
            Assert.True(events.Has(FamilyEventKind.PartnershipFormed));
            Assert.False(events.Has(FamilyEventKind.FamiliesJoined));
        }
        [Fact]
        public void Becoming_partners_with_someone_who_has_children_forms_a_step_family()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "A", "Smith"), T.P(2, "B", "Jones"), T.P(3, "Kid", "Smith", parents: new[] { 1 }) }, 1000);
            var events = r.Apply(new[]
            {
                T.P(1, "A", "Smith", spouses: new[] { 2 }), T.P(2, "B", "Jones", spouses: new[] { 1 }), T.P(3, "Kid", "Smith", parents: new[] { 1 })
            }, 2000);
            Assert.True(events.Has(FamilyEventKind.StepFamilyFormed));
            Assert.Contains(3, r.StepChildrenOf(2));
        }

        [Fact]
        public void A_couple_with_only_shared_children_is_not_a_step_family()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "A", "Smith"), T.P(2, "B", "Jones") }, 1000);
            var events = r.Apply(new[]
            {
                T.P(1, "A", "Smith", spouses: new[] { 2 }), T.P(2, "B", "Jones", spouses: new[] { 1 }), T.P(3, "Kid", "Smith", parents: new[] { 1, 2 })
            }, 2000);
            Assert.False(events.Has(FamilyEventKind.StepFamilyFormed));
        }

        [Fact]
        public void Records_that_are_only_beginning_do_not_report_old_partnerships_as_joinings()
        {
            var r = new FamilyRegistry();
            var events = r.Apply(new[]
            {
                T.P(1, "A", "Smith", spouses: new[] { 2 }), T.P(2, "B", "Jones", spouses: new[] { 1 }), T.P(3, "Kid", "Smith", parents: new[] { 1 })
            }, 1000);
            Assert.False(events.Has(FamilyEventKind.FamiliesJoined));
            Assert.False(events.Has(FamilyEventKind.StepFamilyFormed));
        }

        [Fact]
        public void A_joining_becomes_a_memory_of_both_families()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "A", "Smith"), T.P(2, "B", "Jones") }, 1000);
            var events = r.Apply(new[] { T.P(1, "A", "Smith", spouses: new[] { 2 }), T.P(2, "B", "Jones", spouses: new[] { 1 }) }, 2000);
            r.RecordMemories(events, null, 2000);
            int smith = r.GetPerson(1).PrimaryLineageId, jones = r.GetPerson(2).PrimaryLineageId;
            Assert.Contains(r.MemoriesOf(smith), m => m.Kind == MemoryKind.Joined);
            Assert.Contains(r.MemoriesOf(jones), m => m.Kind == MemoryKind.Joined);
        }
    }
}
