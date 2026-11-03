using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Vanilla clears every relation of a pawn it discards, so a dead relative can vanish from the game entirely.
    /// The records must keep what was already learned.
    /// </summary>
    public class ReconcileRobustnessTests
    {
        [Fact]
        public void Parent_links_survive_the_parent_being_discarded_by_the_game()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Grandpa", "Smith", presence: Presence.Dead),
                T.P(2, "Dad", "Smith", presence: Presence.Dead, parents: new[] { 1 }),
                T.P(3, "Kid", "Smith", parents: new[] { 2 })
            }, 100);
            Assert.Equal(2, r.GenerationIn(r.GetPerson(3)));

            // Later the game discards both dead pawns: the child no longer has a Parent relation, and they are not observed.
            r.Apply(new[] { T.P(3, "Kid", "Smith") }, 200);

            Assert.Equal(new[] { 2 }, r.GetPerson(3).ParentIds);
            Assert.Equal(new[] { 1 }, r.GetPerson(2).ParentIds);
            Assert.Equal(2, r.GenerationIn(r.GetPerson(3)));
            Assert.Single(r.Lineages);
        }

        [Fact]
        public void A_dead_partner_is_remembered_after_the_game_discards_them()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Widow", "Smith", spouses: new[] { 2 }),
                T.P(2, "Late", "Smith", presence: Presence.Dead, spouses: new[] { 1 })
            }, 100);

            var events = r.Apply(new[] { T.P(1, "Widow", "Smith") }, 200);

            Assert.Contains(2, r.GetPerson(1).SpouseIds);
            Assert.DoesNotContain(events, e => e.Kind == FamilyEventKind.PartnershipEnded);
        }

        [Fact]
        public void A_partner_who_simply_disappears_is_not_reported_as_a_breakup()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "A", "Smith", lovers: new[] { 9 }),
                T.P(9, "B", "Wolf", presence: Presence.Away, lovers: new[] { 1 })
            }, 100);

            var events = r.Apply(new[] { T.P(1, "A", "Smith") }, 200);

            Assert.DoesNotContain(events, e => e.Kind == FamilyEventKind.PartnershipEnded);
        }

        [Fact]
        public void A_divorce_moves_a_living_spouse_to_the_ex_list_and_is_reported_once()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, 100);

            var events = r.Apply(new[] { T.P(1, exes: new[] { 2 }), T.P(2, exes: new[] { 1 }) }, 200);

            Assert.DoesNotContain(2, r.GetPerson(1).SpouseIds);
            Assert.Contains(2, r.GetPerson(1).ExIds);
            Assert.Single(events, e => e.Kind == FamilyEventKind.PartnershipEnded);
        }

        [Fact]
        public void A_colonist_who_dies_with_no_living_relatives_is_recorded_as_dead_not_as_having_left()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Loner", "Smith") }, 100);

            // The game layer observes the known colonist directly even though nobody else mentions them.
            var events = r.Apply(new[] { T.P(1, "Loner", "Smith", presence: Presence.Dead) }, 200);

            Assert.True(events.Has(FamilyEventKind.Died));
            Assert.False(events.Has(FamilyEventKind.LeftColony));
            Assert.Empty(r.Households);
        }

        [Fact]
        public void Many_colonists_are_never_dropped_just_because_the_relative_walk_is_capped()
        {
            // The domain trusts its input; this documents the contract the game layer must meet: every colonist is observed.
            var r = new FamilyRegistry();
            var many = Enumerable.Range(1, 600).Select(i => T.P(i)).ToArray();
            r.Apply(many, 100);
            Assert.Equal(600, r.Persons.Count(p => p.InColony));
        }
    }
}
