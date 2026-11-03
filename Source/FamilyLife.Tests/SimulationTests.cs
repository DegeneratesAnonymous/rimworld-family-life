using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Runs three very different families for five years and checks that they end up different. The assertions are deliberately
    /// loose (they pin a design goal, not a number); the report written next to the test run is what to read when tuning.
    /// </summary>
    public class SimulationTests
    {
        private static FamilySimulation Build(int seed, out int[] warm, out int[] mixed, out int[] cold)
        {
            var sim = new FamilySimulation(seed);
            // A warm, steady family; a family with a guarded parent and a volatile one; a guarded, restless family.
            sim.Add(1, 0.9f, 0.8f, 1.0f); sim.Add(2, 0.8f, 0.9f, 1.0f); sim.Add(3, 0.7f, 0.7f, 0.8f); sim.Add(4, 0.8f, 0.6f, 0.8f); sim.Add(5, 0.6f, 0.7f, 0.6f);
            sim.Add(11, -0.6f, 0.6f, 0.4f); sim.Add(12, 0.5f, -0.8f, 0.5f); sim.Add(13, 0.3f, -0.4f, 0.2f, 1.3f); sim.Add(14, -0.4f, 0.2f, 0.0f, 0.6f); sim.Add(15, 0.6f, -0.5f, 0.4f);
            sim.Add(21, -0.8f, 0.3f, -0.3f, 0.6f); sim.Add(22, -0.7f, 0.5f, -0.5f, 0.6f); sim.Add(23, -0.5f, 0.1f, -0.6f, 0.5f); sim.Add(24, -0.2f, 0.4f, -0.2f, 0.7f); sim.Add(25, -0.6f, -0.3f, -0.8f, 0.5f);
            sim.Clash(11, 14, -70f); sim.Clash(12, 13, -60f);
            warm = new[] { 1, 2, 3, 4, 5 }; mixed = new[] { 11, 12, 13, 14, 15 }; cold = new[] { 21, 22, 23, 24, 25 };

            var people = new List<PersonObservation>();
            foreach (var b in new[] { 1, 11, 21 })
            {
                people.Add(T.P(b, spouses: new[] { b + 1 }));
                people.Add(T.P(b + 1, spouses: new[] { b }));
                for (int k = 2; k < 5; k++) people.Add(T.P(b + k, parents: new[] { b, b + 1 }, juvenile: false));
            }
            sim.Observe(people);
            return sim;
        }

        [Fact]
        public void Every_system_together_stays_within_its_bounds_over_five_years()
        {
            for (int seed = 1; seed <= 3; seed++)
            {
                var sim = Build(seed, out var warm, out var mixed, out var cold);
                sim.RunDays(300);
                var r = sim.Registry;
                foreach (var b in r.Bonds)
                {
                    foreach (var v in new[] { b.Attachment, b.Trust, b.Tension, b.Familiarity, b.LastOpinion, b.QualityRecent })
                        Assert.True(!float.IsNaN(v) && !float.IsInfinity(v), "a bond value is not a number");
                    Assert.InRange(b.Attachment, 0f, 100f); Assert.InRange(b.Trust, 0f, 100f);
                    Assert.InRange(b.Tension, 0f, 100f); Assert.InRange(b.Familiarity, 0f, 100f);
                    Assert.True(b.Reasons.Count <= Bond.MaxReasons);
                }
                Assert.True(r.Chronicle.Count <= FamilyRegistry.MaxChronicle);
                foreach (var l in r.Lineages)
                {
                    Assert.True(r.Memories.Count(m => m.LineageId == l.Id) <= CultureTuning.MaxMemoriesPerLineage);
                    Assert.All(l.Culture, v => Assert.InRange(v, -100f, 100f));
                }
                Assert.True(r.Rifts.Count(x => !x.IsOpen) <= ConflictTuning.MaxClosedRifts);
                Assert.True(sim.MaxOpenRequests <= 12, "gathering requests pile up: " + sim.MaxOpenRequests);
                // Announcements must be rare enough to read: a few per family per year, never a stream.
                Assert.True(sim.BondAnnouncements <= 45, "too many bond announcements in five years: " + sim.BondAnnouncements);
                // The same pair is never announced twice in a row inside the cooldown.
                foreach (var g in sim.Announced.GroupBy(e => e.A * 1000 + e.B))
                {
                    var ticks = g.Select(e => e.Tick).OrderBy(t => t).ToList();
                    for (int i = 1; i < ticks.Count; i++)
                        Assert.True(ticks[i] - ticks[i - 1] >= InsightTuning.MilestoneCooldownTicks, "a bond was announced twice inside the cooldown");
                }
                Console.WriteLine($"seed {seed}: announcements {sim.BondAnnouncements}, traditions {sim.TraditionsAdopted}, gatherings {sim.GatheringsHeld}, memories {r.Memories.Count}, rifts {r.Rifts.Count}, requests max {sim.MaxOpenRequests}");
            }
        }

        [Fact]
        public void Three_families_end_up_different_after_five_years()
        {
            var sb = new StringBuilder();
            var warmAtt = new List<float>();
            var mixedAtt = new List<float>();
            var coldAtt = new List<float>();
            var warmHours = new List<float>();
            var coldHours = new List<float>();
            for (int seed = 1; seed <= 5; seed++)
            {
                var sim = Build(seed, out var warm, out var mixed, out var cold);
                sb.AppendLine($"seed {seed}");
                for (int year = 1; year <= 5; year++)
                {
                    sim.RunDays(60);
                    if (year == 1 || year == 5)
                    {
                        sb.AppendLine($"  year {year}:");
                        sb.AppendLine("    " + sim.Report("warm and steady", warm));
                        sb.AppendLine("    " + sim.Report("mixed, volatile", mixed));
                        sb.AppendLine("    " + sim.Report("guarded, restless", cold));
                    }
                }
                sb.AppendLine($"  rifts opened {sim.RiftsOpened}, closed {sim.RiftsClosed}, reconcile {sim.ReconcileSuccesses}/{sim.ReconcileTries}, family hours {sim.FamilyHours:0}");
                float Mean(int[] ids) => sim.Registry.Bonds.Where(b => ids.Contains(b.From) && ids.Contains(b.To)).Average(b => b.Attachment);
                warmHours.Add(warm.Sum(i => sim.HoursBy.TryGetValue(i, out var hw) ? hw : 0f));
                coldHours.Add(cold.Sum(i => sim.HoursBy.TryGetValue(i, out var hc) ? hc : 0f));
                warmAtt.Add(Mean(warm)); mixedAtt.Add(Mean(mixed)); coldAtt.Add(Mean(cold));
            }
            sb.AppendLine($"mean attachment over seeds: warm {warmAtt.Average():0.0}, mixed {mixedAtt.Average():0.0}, guarded {coldAtt.Average():0.0}");
            sb.AppendLine($"family-time hours over five years: warm {warmHours.Average():0}, guarded {coldHours.Average():0}");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "simulation-report.txt"), sb.ToString());
            Console.WriteLine(sb.ToString());

            // The design goal: the families do not converge. The gap is the whole point of having personalities.
            Assert.True(warmAtt.Average() - coldAtt.Average() >= 15f, "families with different characters should not end up equally close:\n" + sb);
            Assert.True(mixedAtt.Average() < warmAtt.Average(), "a family with a guarded and a volatile parent should be less close than a warm steady one:\n" + sb);
            Assert.True(warmHours.Average() > coldHours.Average() * 1.15f, "a devoted family should spend visibly more time together than an independent one:\n" + sb);
            Assert.True(coldAtt.Average() < 85f, "a guarded family should not reach maximum attachment just by living together:\n" + sb);
        }
    }
}
