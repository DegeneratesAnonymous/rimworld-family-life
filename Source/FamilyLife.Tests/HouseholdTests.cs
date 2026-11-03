using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class HouseholdTests
    {
        [Fact]
        public void A_lone_colonist_gets_their_own_household()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1) }, 100);

            Assert.Single(r.Households);
            Assert.Equal(new[] { 1 }, T.HouseholdOf(r, 1).ResidentIds);
        }

        [Fact]
        public void Two_parents_and_their_children_share_one_household()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, spouses: new[] { 2 }),
                T.P(2, spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }),
                T.Kid(4, new[] { 1, 2 })
            }, 100);

            Assert.Single(r.Households);
            Assert.Equal(4, r.Households[0].ResidentIds.Count);
        }

        [Fact]
        public void A_single_parent_and_children_share_a_household()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1), T.Kid(3, new[] { 1 }), T.Kid(4, new[] { 1 }) }, 100);

            Assert.Single(r.Households);
            Assert.Equal(3, r.Households[0].ResidentIds.Count);
        }

        [Fact]
        public void An_adult_child_stays_in_the_parents_household()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, spouses: new[] { 2 }),
                T.P(2, spouses: new[] { 1 }),
                T.P(3, birth: -2_000_000, parents: new[] { 1, 2 })
            }, 100);

            Assert.Single(r.Households);
            Assert.True(T.SameHousehold(r, 1, 3));
        }

        [Fact]
        public void Orphaned_children_stay_together()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, presence: Presence.Dead),
                T.Kid(3, new[] { 1 }),
                T.Kid(4, new[] { 1 })
            }, 100);

            Assert.True(T.SameHousehold(r, 3, 4));
        }

        [Fact]
        public void Adults_who_are_not_partners_do_not_share_a_household_by_default()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1), T.P(2) }, 100);

            Assert.Equal(2, r.Households.Count);
        }

        [Fact]
        public void Away_people_are_recorded_but_never_housed()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1), T.P(9, presence: Presence.Away, lovers: new[] { 1 }) }, 100);

            Assert.Equal(0, r.GetPerson(9).HouseholdId);
            Assert.Single(r.Households);
        }

        // ---------------------------------------------------------------- partnerships

        private static FamilyRegistry FamilyWithGrownChild(out PersonObservation[] baseline)
        {
            var r = new FamilyRegistry();
            baseline = new[]
            {
                T.P(1, spouses: new[] { 2 }),
                T.P(2, spouses: new[] { 1 }),
                T.P(3, birth: -2_000_000, parents: new[] { 1, 2 }),
                T.P(5)       // an unattached colonist living alone
            };
            r.Apply(baseline, 100);
            return r;
        }

        [Fact]
        public void An_adult_child_who_pairs_with_someone_living_alone_moves_in_with_them()
        {
            var r = FamilyWithGrownChild(out _);
            var events = r.Apply(new[]
            {
                T.P(1, spouses: new[] { 2 }),
                T.P(2, spouses: new[] { 1 }),
                T.P(3, birth: -2_000_000, parents: new[] { 1, 2 }, lovers: new[] { 5 }),
                T.P(5, lovers: new[] { 3 })
            }, 200);

            Assert.True(T.SameHousehold(r, 3, 5));
            Assert.True(T.SameHousehold(r, 1, 2));
            Assert.False(T.SameHousehold(r, 1, 3));
            Assert.True(events.Has(FamilyEventKind.PartnershipFormed));
            Assert.Equal(2, r.Households.Count);
        }

        [Fact]
        public void Two_people_who_each_live_with_others_start_a_new_household_together()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.P(3, birth: -2_000_000, parents: new[] { 1, 2 }),
                T.P(11, spouses: new[] { 12 }), T.P(12, spouses: new[] { 11 }), T.P(13, birth: -2_000_000, parents: new[] { 11, 12 })
            }, 100);
            Assert.Equal(2, r.Households.Count);

            r.Apply(new[]
            {
                T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.P(3, birth: -2_000_000, parents: new[] { 1, 2 }, lovers: new[] { 13 }),
                T.P(11, spouses: new[] { 12 }), T.P(12, spouses: new[] { 11 }), T.P(13, birth: -2_000_000, parents: new[] { 11, 12 }, lovers: new[] { 3 })
            }, 200);

            Assert.Equal(3, r.Households.Count);
            Assert.True(T.SameHousehold(r, 3, 13));
            Assert.True(T.SameHousehold(r, 1, 2));
            Assert.True(T.SameHousehold(r, 11, 12));
            Assert.False(T.SameHousehold(r, 1, 3));
        }

        [Fact]
        public void An_adult_who_moves_out_takes_their_dependent_children()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }),
                T.P(3, birth: -2_000_000, parents: new[] { 1, 2 }),
                T.Kid(20, new[] { 3 }),
                T.P(5)
            }, 100);
            Assert.True(T.SameHousehold(r, 3, 20));

            r.Apply(new[]
            {
                T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }),
                T.P(3, birth: -2_000_000, parents: new[] { 1, 2 }, lovers: new[] { 5 }),
                T.Kid(20, new[] { 3 }),
                T.P(5, lovers: new[] { 3 })
            }, 200);

            Assert.True(T.SameHousehold(r, 3, 5));
            Assert.True(T.SameHousehold(r, 3, 20));
            Assert.False(T.SameHousehold(r, 1, 20));
        }

        [Fact]
        public void A_single_parent_and_their_children_absorb_a_new_partner_and_the_partners_children()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1), T.Kid(10, new[] { 1 }),
                T.P(2), T.Kid(20, new[] { 2 })
            }, 100);
            Assert.Equal(2, r.Households.Count);

            r.Apply(new[]
            {
                T.P(1, lovers: new[] { 2 }), T.Kid(10, new[] { 1 }),
                T.P(2, lovers: new[] { 1 }), T.Kid(20, new[] { 2 })
            }, 200);

            Assert.Single(r.Households);
            Assert.True(T.SameHousehold(r, 1, 2));
            Assert.True(T.SameHousehold(r, 10, 20));
            Assert.True(T.SameHousehold(r, 1, 20));
        }

        [Fact]
        public void A_second_partner_joins_the_household_an_existing_partner_already_shares()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.P(3) }, 100);
            Assert.Equal(2, r.Households.Count);

            r.Apply(new[] { T.P(1, spouses: new[] { 2 }, lovers: new[] { 3 }), T.P(2, spouses: new[] { 1 }), T.P(3, lovers: new[] { 1 }) }, 200);

            Assert.True(T.SameHousehold(r, 1, 2));
            Assert.True(T.SameHousehold(r, 1, 3));
            Assert.Single(r.Households);
        }

        [Fact]
        public void A_breakup_keeps_both_people_where_they_live_and_is_recorded()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, 100);
            Assert.Single(r.Households);

            var events = r.Apply(new[] { T.P(1, exes: new[] { 2 }), T.P(2, exes: new[] { 1 }) }, 200);

            Assert.Single(r.Households);
            Assert.True(T.SameHousehold(r, 1, 2));
            Assert.Single(events, e => e.Kind == FamilyEventKind.PartnershipEnded);
        }

        // ---------------------------------------------------------------- leaving, dying, returning

        [Fact]
        public void Death_removes_a_resident_and_an_empty_household_is_dissolved()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, 100);

            var events = r.Apply(new[] { T.P(1, presence: Presence.Dead, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, 200);
            Assert.Equal(new[] { 2 }, r.Households.Single().ResidentIds);
            Assert.Equal(200, r.GetPerson(1).DeathTick);
            Assert.True(events.Has(FamilyEventKind.Died));

            events = r.Apply(new[] { T.P(1, presence: Presence.Dead), T.P(2, presence: Presence.Dead) }, 300);
            Assert.Empty(r.Households);
            Assert.True(events.Has(FamilyEventKind.HouseholdDissolved));
        }

        [Fact]
        public void Someone_who_leaves_and_comes_back_returns_to_their_household()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.Kid(3, new[] { 1, 2 }) }, 100);
            int home = r.GetPerson(2).HouseholdId;

            var left = r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, presence: Presence.Away, spouses: new[] { 1 }), T.Kid(3, new[] { 1, 2 }) }, 200);
            Assert.Equal(0, r.GetPerson(2).HouseholdId);
            Assert.True(left.Has(FamilyEventKind.LeftColony));

            var back = r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.Kid(3, new[] { 1, 2 }) }, 300);
            Assert.Equal(home, r.GetPerson(2).HouseholdId);
            Assert.True(back.Has(FamilyEventKind.JoinedColony));
        }

        [Fact]
        public void A_colonist_who_is_no_longer_observed_is_treated_as_having_left()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1), T.P(2) }, 100);

            r.Apply(new[] { T.P(1) }, 200);

            Assert.Equal(Presence.Away, r.GetPerson(2).Presence);
            Assert.Equal(0, r.GetPerson(2).HouseholdId);
            Assert.Single(r.Households);
        }

        [Fact]
        public void Someone_returning_to_a_dissolved_household_is_placed_again()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1) }, 100);
            r.Apply(new[] { T.P(1, presence: Presence.Away) }, 200);
            Assert.Empty(r.Households);

            r.Apply(new[] { T.P(1) }, 300);

            Assert.Single(r.Households);
            Assert.NotEqual(0, r.GetPerson(1).HouseholdId);
        }

        // ---------------------------------------------------------------- player-directed moves

        [Fact]
        public void Moving_to_a_new_household_takes_dependent_children_along()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.Kid(3, new[] { 1, 2 }), T.Kid(4, new[] { 1, 2 }) }, 100);

            r.MoveToHousehold(2, 0, 200);

            Assert.Equal(2, r.Households.Count);
            Assert.True(T.SameHousehold(r, 2, 3));
            Assert.True(T.SameHousehold(r, 2, 4));
            Assert.False(T.SameHousehold(r, 1, 2));
        }

        [Fact]
        public void Moving_without_dependents_leaves_the_children_behind()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.Kid(3, new[] { 1, 2 }) }, 100);

            r.MoveToHousehold(2, 0, 200, withDependents: false);

            Assert.True(T.SameHousehold(r, 1, 3));
            Assert.False(T.SameHousehold(r, 2, 3));
        }

        [Fact]
        public void Moving_to_an_existing_household_works_and_empty_ones_dissolve()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1), T.P(2) }, 100);
            int target = r.GetPerson(1).HouseholdId;

            r.MoveToHousehold(2, target, 200);

            Assert.Single(r.Households);
            Assert.True(T.SameHousehold(r, 1, 2));
        }

        [Fact]
        public void Pointless_moves_change_nothing()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1), T.P(2), T.P(9, presence: Presence.Away) }, 100);
            string before = T.Summary(r);

            Assert.Empty(r.MoveToHousehold(1, 0, 200));                              // already alone
            Assert.Empty(r.MoveToHousehold(1, r.GetPerson(1).HouseholdId, 200));     // already there
            Assert.Empty(r.MoveToHousehold(9, r.GetPerson(1).HouseholdId, 200));     // not in the colony
            Assert.Empty(r.MoveToHousehold(12345, 0, 200));                          // unknown person
            Assert.Empty(r.MoveToHousehold(1, 9999, 200));                           // unknown household

            Assert.Equal(before, T.Summary(r));
        }
    }
}
