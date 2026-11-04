using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class ObservationCacheTests
    {
        [Fact]
        public void Every_observed_fact_except_ordinary_aging_invalidates_the_cache()
        {
            foreach (var field in typeof(PersonObservation).GetFields().Where(f => f.Name != "AgeTicks"))
            {
                var cache = new ObservationCache(); var person = T.P(1);
                Assert.True(cache.Update(new[] { person }));
                if (field.FieldType == typeof(int)) field.SetValue(person, (int)field.GetValue(person) + 1);
                else if (field.FieldType == typeof(long)) field.SetValue(person, (long)field.GetValue(person) + 1);
                else if (field.FieldType == typeof(string)) field.SetValue(person, "changed");
                else if (field.FieldType == typeof(bool)) field.SetValue(person, !(bool)field.GetValue(person));
                else if (field.FieldType == typeof(Presence)) field.SetValue(person, Presence.Dead);
                else if (field.FieldType == typeof(List<int>)) ((List<int>)field.GetValue(person)).Add(99);
                else throw new Exception("Add coverage for " + field.Name);
                Assert.True(cache.Update(new[] { person }), field.Name + " was ignored");
            }
        }

        [Fact]
        public void Hourly_aging_does_not_repeat_structural_work_but_a_newborn_does()
        {
            var cache = new ObservationCache(); var adult = T.P(1);
            Assert.True(cache.Update(new[] { adult }));
            adult.AgeTicks += 2500;
            Assert.False(cache.Update(new[] { adult }));
            var newborn = T.Kid(2, new[] { 1 }); newborn.AgeTicks = 0;
            Assert.True(cache.Update(new[] { adult, newborn }));
        }

        [Fact]
        public void Snapshots_do_not_alias_normalized_input_and_missing_people_are_not_ignored()
        {
            var cache = new ObservationCache(); var a = T.P(1); var b = T.P(2);
            a.ParentIds.Add(8);
            Assert.True(cache.Update(new[] { a, b }));
            Assert.False(cache.Update(new[] { b, a })); // input order is immaterial
            a.ParentIds.Clear();
            Assert.True(cache.Update(new[] { a, b }));
            Assert.True(cache.Update(new[] { a }));
            cache.Clear();
            Assert.True(cache.Update(new[] { a }));
        }

        [Fact]
        public void Null_or_duplicate_input_never_suppresses_reconciliation()
        {
            var cache = new ObservationCache(); var person = T.P(1);
            Assert.True(cache.Update(new[] { person }));
            Assert.True(cache.Update(new[] { person, person }));
            Assert.True(cache.Update(new[] { person }));
            Assert.True(cache.Update(null));
            Assert.True(cache.Update(new PersonObservation[] { null }));
        }
    }
}
