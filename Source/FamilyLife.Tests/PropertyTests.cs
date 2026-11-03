using System;
using System.Diagnostics;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class PropertyTests
    {
        private static PersonObservation[] Colony()
        {
            return new[]
            {
                T.P(1, "Alice", "Smith", spouses: new[] { 2 }),
                T.P(2, "Bob", "Jones", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }),
                T.P(4, "Carl", "Smith", birth: -2_000_000, parents: new[] { 1, 2 }, lovers: new[] { 6 }),
                T.P(6, "Dana", "Lake", lovers: new[] { 4 }),
                T.Kid(7, new[] { 4, 6 }, "Smith"),
                T.P(8, "Ed", "Wolf", presence: Presence.Dead, parents: new[] { 99 }),
                T.P(9, "Fay", "Ash", presence: Presence.Away, exes: new[] { 1 })
            };
        }

        [Fact]
        public void Applying_the_same_observations_twice_changes_nothing_the_second_time()
        {
            var r = new FamilyRegistry();
            r.Apply(Colony(), 100);
            string first = T.Summary(r);
            int chronicle = r.Chronicle.Count;

            var again = r.Apply(Colony(), 200);

            Assert.Equal(first, T.Summary(r));
            Assert.Equal(chronicle, r.Chronicle.Count);
            Assert.All(again, e => Assert.NotEqual(FamilyEventKind.PersonRecorded, e.Kind));
            Assert.Empty(again);
        }

        [Fact]
        public void The_order_of_observations_does_not_matter()
        {
            var baseline = new FamilyRegistry();
            baseline.Apply(Colony(), 100);
            string expected = T.Summary(baseline);

            var rng = new Random(12345);
            for (int i = 0; i < 25; i++)
            {
                var shuffled = Colony().OrderBy(_ => rng.Next()).ToArray();
                var r = new FamilyRegistry();
                r.Apply(shuffled, 100);
                Assert.Equal(expected, T.Summary(r));
            }
        }

        [Fact]
        public void Records_that_begin_in_a_running_game_are_not_reported_as_births_or_arrivals()
        {
            var r = new FamilyRegistry();
            r.Apply(Colony(), 100);

            Assert.Contains(r.Chronicle, e => e.Kind == FamilyEventKind.RecordsBegan);
            Assert.DoesNotContain(r.Chronicle, e => e.Kind == FamilyEventKind.JoinedColony || e.Kind == FamilyEventKind.Birth);
        }

        [Fact]
        public void A_newborn_is_recorded_as_a_birth_and_joins_the_parents_household_and_lineage()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, 100);

            var events = r.Apply(new[]
            {
                T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }, age: 500)
            }, 200);

            Assert.True(events.Has(FamilyEventKind.Birth));
            Assert.Contains(r.Chronicle, e => e.Kind == FamilyEventKind.Birth && e.A == 3);
            Assert.True(T.SameHousehold(r, 1, 3));
            Assert.Equal(r.GetPerson(1).PrimaryLineageId, r.GetPerson(3).PrimaryLineageId);
        }

        [Fact]
        public void A_recruit_is_recorded_as_a_new_arrival()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1) }, 100);

            var events = r.Apply(new[] { T.P(1), T.P(2, "Newcomer") }, 200);

            Assert.True(events.Has(FamilyEventKind.JoinedColony));
            Assert.Contains(r.Chronicle, e => e.Kind == FamilyEventKind.JoinedColony && e.A == 2);
        }

        [Fact]
        public void The_chronicle_is_bounded()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1) }, 100);
            for (int i = 0; i < FamilyRegistry.MaxChronicle + 100; i++)
                r.Apply(new[] { T.P(1), T.P(1000 + i) }, 200 + i);

            Assert.True(r.Chronicle.Count <= FamilyRegistry.MaxChronicle);
            Assert.Equal(FamilyRegistry.MaxChronicle, r.Chronicle.Count);
        }

        [Fact]
        public void A_revived_person_is_alive_again()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1) }, 100);
            r.Apply(new[] { T.P(1, presence: Presence.Dead) }, 200);
            Assert.Equal(Presence.Dead, r.GetPerson(1).Presence);

            r.Apply(new[] { T.P(1) }, 300);

            Assert.Equal(Presence.Colony, r.GetPerson(1).Presence);
            Assert.Equal(-1, r.GetPerson(1).DeathTick);
            Assert.NotEqual(0, r.GetPerson(1).HouseholdId);
        }

        [Fact]
        public void Damaged_data_is_repaired_when_indexes_are_rebuilt()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, 100);

            // Simulate a damaged or hand-edited save.
            r.Persons.Add(new PersonRecord { Id = 1, Name = "Duplicate" });
            r.Persons.Add(null);
            r.Households[0].ResidentIds.Add(777);                       // a resident who does not exist
            r.GetPerson(2).HouseholdId = 4242;                          // a household that does not exist
            r.GetPerson(1).PrimaryLineageId = 999;                      // a lineage that does not exist
            r.Lineages[0].Affiliations.Add(null);

            r.RebuildIndexes();

            Assert.Equal(2, r.Persons.Count);
            Assert.DoesNotContain(777, r.Households[0].ResidentIds);
            Assert.NotEqual(999, r.GetPerson(1).PrimaryLineageId);
            Assert.NotEqual(4242, r.GetPerson(2).HouseholdId);

            // And the next sweep puts everything right again without throwing.
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, 200);
            Assert.True(T.SameHousehold(r, 1, 2));
            Assert.NotEqual(0, r.GetPerson(1).PrimaryLineageId);
        }

        [Fact]
        public void Null_and_duplicate_observations_are_tolerated()
        {
            var r = new FamilyRegistry();

            var events = r.Apply(new PersonObservation[] { null, T.P(1, "First"), T.P(1, "Second"), T.P(2, parents: new[] { 2, 2, 5, 5 }) }, 100);

            Assert.NotNull(events);
            Assert.Equal("Second", r.GetPerson(1).Name);
            Assert.Equal(new[] { 5 }, r.GetPerson(2).ParentIds);
            Assert.Empty(new FamilyRegistry().Apply(null, 100));
        }

        [Fact]
        public void A_large_colony_with_many_relatives_stays_fast_and_correct()
        {
            var obs = new System.Collections.Generic.List<PersonObservation>();
            int id = 1;
            // 80 families: two parents and three children each, 400 people in total.
            for (int f = 0; f < 80; f++)
            {
                int a = id++, b = id++;
                obs.Add(T.P(a, "A" + f, "Fam" + f, spouses: new[] { b }));
                obs.Add(T.P(b, "B" + f, "Fam" + f, spouses: new[] { a }));
                for (int k = 0; k < 3; k++) obs.Add(T.Kid(id++, new[] { a, b }, "Fam" + f));
            }

            var r = new FamilyRegistry();
            var clock = Stopwatch.StartNew();
            r.Apply(obs, 100);
            for (int i = 0; i < 30; i++) r.Apply(obs, 200 + i);       // 30 more sweeps with nothing changing
            clock.Stop();

            Assert.Equal(80, r.Lineages.Count);
            Assert.Equal(80, r.Households.Count);
            Assert.True(clock.ElapsedMilliseconds < 5000, "31 sweeps of 400 people took " + clock.ElapsedMilliseconds + " ms");
        }

        [Fact]
        public void Household_and_lineage_ids_are_never_reused()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1) }, 100);
            int firstHousehold = r.GetPerson(1).HouseholdId;
            r.Apply(new[] { T.P(1, presence: Presence.Away) }, 200);       // household dissolves
            r.Apply(new[] { T.P(1), T.P(2) }, 300);

            Assert.DoesNotContain(r.Households, h => h.Id == firstHousehold && h.ResidentIds.Contains(2));
            Assert.Equal(r.Households.Count, r.Households.Select(h => h.Id).Distinct().Count());
        }
    }
}
