using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Times the work the mod does on a large colony: the hourly sweep and the things the game calls constantly. The assertions are loose
    /// ceilings that catch an accidental quadratic loop; the printed numbers are what to read when tuning.
    /// </summary>
    public class PerformanceTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        /// <summary>A large colony: many families of colonists, plus relatives who are away or dead.</summary>
        private static List<PersonObservation> BigColony(int families, int relativesPerFamily, out List<int> colonists)
        {
            var rng = new Random(11);
            var people = new List<PersonObservation>();
            colonists = new List<int>();
            int id = 1;
            for (int f = 0; f < families; f++)
            {
                string last = "Fam" + f;
                int dad = id++, mum = id++;
                people.Add(T.P(dad, "Dad" + f, last, spouses: new[] { mum }));
                people.Add(T.P(mum, "Mum" + f, "Other" + f, spouses: new[] { dad }));
                colonists.Add(dad); colonists.Add(mum);
                for (int k = 0; k < 3; k++)
                {
                    int kid = id++;
                    people.Add(T.P(kid, "Kid" + f + "_" + k, last, parents: new[] { dad, mum }));
                    colonists.Add(kid);
                }
                int previous = dad;
                for (int r = 0; r < relativesPerFamily; r++)
                {
                    int rel = id++;
                    var presence = rng.Next(3) == 0 ? Presence.Dead : Presence.Away;
                    people.Add(T.P(rel, "Rel" + f + "_" + r, last, presence: presence, parents: new[] { r % 3 == 0 ? dad : previous }));
                    previous = rel;
                }
            }
            return people;
        }

        private static double TimeMs(int runs, Action action)
        {
            action();                                  // warm-up
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < runs; i++) action();
            sw.Stop();
            return sw.Elapsed.TotalMilliseconds / runs;
        }

        [Fact]
        public void The_hourly_sweep_and_the_constantly_called_parts_stay_cheap_in_a_large_colony()
        {
            var people = BigColony(24, 14, out var colonists);        // 120 colonists, about 340 relatives
            var r = new FamilyRegistry();
            int now = 5 * Day;
            r.Apply(people, now);
            Func<int, int, int?> opinion = (a, b) => (a * 31 + b * 17) % 120 - 40;
            Func<int, float[]> profile = id => { var p = new float[DimInfo.Count]; p[(int)Dim.Warmth] = (id % 7 - 3) / 3f; p[(int)Dim.Hearth] = (id % 5 - 2) / 2f; return p; };
            r.UpdateBonds(opinion, now, null, profile);
            for (int d = 0; d < 20; d++) { now += Day; r.UpdateBonds(opinion, now, null, profile); r.UpdateCulture(profile, now); }

            var report = new List<string>();
            double Measure(string name, int runs, Action a) { double ms = TimeMs(runs, a); report.Add($"{name,-42} {ms,8:0.000} ms"); return ms; }

            int tick = now;
            double apply = Measure("Apply (nothing changed)", 20, () => r.Apply(people, tick));
            var observations = new ObservationCache(); observations.Update(people);
            Measure("Compare unchanged observations", 200, () => observations.Update(people));
            double bonds = Measure("UpdateBonds (hourly)", 20, () => { tick += BondTuning.TicksPerHour; r.UpdateBonds(opinion, tick, new List<FamilyEvent>(), profile); });
            double culture = Measure("UpdateCulture", 20, () => r.UpdateCulture(profile, tick));
            int dailyCultureTick=tick;
            double dailyCulture=Measure("UpdateCulture (daily work)",20,()=>{dailyCultureTick+=Day;r.UpdateCulture(profile,dailyCultureTick);});
            double trad = Measure("UpdateTraditions", 20, () => r.UpdateTraditions(tick));
            double rifts = Measure("UpdateRifts", 20, () => r.UpdateRifts(tick, true, new List<FamilyEvent>()));
            double miles = Measure("UpdateBondMilestones", 20, () => r.UpdateBondMilestones(tick));
            double anniv = Measure("UpdateMilestones", 20, () => r.UpdateMilestones(tick));
            double mem = Measure("RecordMemories (no events)", 20, () => r.RecordMemories(new List<FamilyEvent>(), null, tick));
            double missing = Measure("MissingPairs", 20, () => r.MissingPairs(tick));
            double sweep = apply + bonds + culture + trad + rifts + miles + anniv + mem + missing;
            report.Add($"{"-- one full sweep",-42} {sweep,8:0.000} ms");

            var biggest = r.Lineages.OrderByDescending(l => r.MembersOf(l.Id).Count).First();
            double tree = Measure("BuildTree (largest family)", 20, () => r.BuildTree(biggest.Id));
            double listed = Measure("IsListedFamily for every family", 20, () => { foreach (var l in r.Lineages) r.IsListedFamily(l.Id); });
            double members = Measure("MembersOf (largest family)", 200, () => r.MembersOf(biggest.Id));

            // The things the game calls constantly while playing.
            int a = colonists[0], b = colonists[1];
            double stage = Measure("CoupleStageOf x1000 (breakup hook)", 20, () => { for (int i = 0; i < 1000; i++) r.CoupleStageOf(a, b, tick); });
            double getPerson = Measure("GetPerson x1000 (romance hook)", 20, () => { for (int i = 0; i < 1000; i++) r.GetPerson(a); });
            var available = r.BondsFrom(a).Select(x => x.To).ToList();
            double rank = Measure("RankCompanions (one pawn)", 200, () => r.RankCompanions(a, available, tick, 1f));
            double appetite = Measure("FamilyTimeAppetite x1000", 20, () => { for (int i = 0; i < 1000; i++) r.FamilyTimeAppetite(biggest.Id, null); });
            double together = Measure("RecordTogether x1000", 20, () => { for (int i = 0; i < 1000; i++) r.RecordTogether(a, b, 1500, tick); });

            report.Insert(0, $"{r.Persons.Count} people, {r.Lineages.Count} lineages, {r.Bonds.Count} bonds, {r.Households.Count} households");
            Console.WriteLine(string.Join("\n", report));

            // Loose ceilings: a sweep happens once an in-game hour, a hook call must be negligible.
            Assert.True(sweep < 100, "the hourly sweep is slow:\n" + string.Join("\n", report));
            Assert.True(culture < 5 && dailyCulture < 25, "culture aggregation is slow:\n" + string.Join("\n", report));
            Assert.True(tree < 25, "the tree takes too long to lay out:\n" + string.Join("\n", report));
            Assert.True(stage / 1000 < 0.01, "the breakup hook is too slow per call:\n" + string.Join("\n", report));
            Assert.True(getPerson / 1000 < 0.005, "the romance hook lookup is too slow per call:\n" + string.Join("\n", report));
            Assert.True(rank < 2, "choosing a companion is too slow:\n" + string.Join("\n", report));
        }

        [Fact]
        public void Repeated_house_unions_do_not_leave_active_simulation_work_behind()
        {
            var people=BigColony(24,4,out var colonists);var r=new FamilyRegistry();int now=5*Day;r.Apply(people,now);
            var active=r.Lineages.Where(l=>r.IsListedFamily(l.Id)).Select(l=>l.Id).ToList();int target=active[0];
            foreach(int source in active.Skip(1)) Assert.True(r.UniteFamilies(source,target,++now));
            Assert.Single(r.Lineages.Where(l=>r.IsListedFamily(l.Id)));
            Assert.All(active.Skip(1),id=>Assert.False(r.IsListedFamily(id)));
            Func<int,float[]> profile=id=>new float[DimInfo.Count];
            int cultureTick=now;Assert.True(TimeMs(20,()=>{cultureTick+=Day;r.UpdateCulture(profile,cultureTick);})<25);
        }
    }
}
