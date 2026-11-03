using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Checks that culture can actually be reached by ordinary families. Pawns are given two or three traits at random from the known
    /// trait table (the way the game hands them out); a rule that only a family of near-identical people could satisfy would make
    /// traditions and culture-driven behaviour look implemented while never appearing in play.
    /// </summary>
    public class CultureReachTests
    {
        private static List<KeyValuePair<Dim, float>[]> AllReadings()
        {
            var field = typeof(TraitTags).GetField("table", BindingFlags.NonPublic | BindingFlags.Static);
            var table = (Dictionary<string, KeyValuePair<Dim, float>[]>)field.GetValue(null);
            return table.Values.ToList();
        }

        private static float[] RandomProfile(Random rng, List<KeyValuePair<Dim, float>[]> readings)
        {
            var picks = new List<KeyValuePair<Dim, float>>();
            int count = rng.Next(2, 4);
            for (int i = 0; i < count; i++) picks.AddRange(readings[rng.Next(readings.Count)]);
            return TraitTags.ProfileOf(picks);
        }

        [Fact]
        public void Ordinary_random_families_reach_values_and_some_take_up_traditions()
        {
            var readings = AllReadings();
            var rng = new Random(7);
            int families = 400, withValue = 0, withTradition = 0;
            var maxes = new List<float>();
            for (int f = 0; f < families; f++)
            {
                var r = new FamilyRegistry();
                var obs = new List<PersonObservation>
                {
                    T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }),
                    T.P(3, parents: new[] { 1, 2 }), T.P(4, parents: new[] { 1, 2 }), T.P(5, parents: new[] { 1, 2 })
                };
                r.Apply(obs, 1000);
                var profiles = obs.ToDictionary(o => o.Id, _ => RandomProfile(rng, readings));
                int now = 1000;
                for (int day = 0; day < 40; day++)
                {
                    now += ActivityTuning.TicksPerDay;
                    r.UpdateCulture(id => profiles[id], now);
                    r.UpdateTraditions(now);
                }
                var lineage = r.Lineages.First();
                float max = lineage.Culture.Take(7).Max();
                maxes.Add(max);
                if (r.Values(lineage.Id).Count > 0) withValue++;
                if (lineage.Traditions.Count > 0) withTradition++;
            }
            maxes.Sort();
            Console.WriteLine($"families with a clear value: {withValue}/{families}; with a tradition: {withTradition}/{families}; median top value {maxes[maxes.Count / 2]:0.0}, 90th {maxes[(int)(maxes.Count * 0.9)]:0.0}");
            // About a third to two thirds of ordinary families should have something they value, and a meaningful minority a tradition.
            Assert.True(withValue >= families * 0.25, $"too few families have any clear value ({withValue}/{families})");
            Assert.True(withTradition >= families * 0.12, $"too few families ever take up a tradition ({withTradition}/{families})");
            Assert.True(withTradition <= families * 0.8, $"almost every family takes up a tradition ({withTradition}/{families})");
        }
    }
}
