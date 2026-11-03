using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FamilyLife.Domain;

namespace FamilyLife.Tests
{
    /// <summary>
    /// A headless stand-in for a colony: a few families with different personalities live for years, spend family time the way the
    /// game layer would (ranked companions, a daily budget, reconciling rifts first), and the opinion the game would supply is a
    /// simple model of compatibility. It cannot say how the real game feels. It can say whether the bond rules, left alone, make
    /// every family the same, so the rules can be tuned against that instead of against a screenshot.
    /// </summary>
    internal sealed class FamilySimulation
    {
        public sealed class Member
        {
            public int Id;
            public float[] Profile = new float[DimInfo.Count];
            public float Sociability = 1f;     // how much family time they seek compared with others
        }

        public const int Day = ActivityTuning.TicksPerDay;
        public const int Year = 60 * Day;

        private readonly Random rng;
        private readonly Dictionary<int, Member> members = new Dictionary<int, Member>();
        private readonly Dictionary<long, float> chemistry = new Dictionary<long, float>();
        private readonly Dictionary<long, float> opinion = new Dictionary<long, float>();
        public readonly FamilyRegistry Registry = new FamilyRegistry();
        public int Now;
        public int RiftsOpened;
        public int RiftsClosed;
        public int ReconcileTries;
        public int ReconcileSuccesses;
        public float FamilyHours;
        public int GatheringsHeld;
        public int BondAnnouncements;
        public int TraditionsAdopted;
        public int MaxOpenRequests;
        public readonly List<FamilyEvent> Announced = new List<FamilyEvent>();
        public readonly Dictionary<int, float> HoursBy = new Dictionary<int, float>();

        public FamilySimulation(int seed) { rng = new Random(seed); }

        private static long Key(int a, int b) => ((long)a << 32) | (uint)b;

        public Member Add(int id, float warmth, float steadiness, float devotion, float sociability = 1f)
        {
            var m = new Member { Id = id, Sociability = sociability };
            m.Profile[(int)Dim.Warmth] = warmth;
            m.Profile[(int)Dim.Steadiness] = steadiness;
            m.Profile[(int)Dim.Devotion] = devotion;
            members[id] = m;
            return m;
        }

        /// <summary>What the game would report as one person's opinion of another: it follows how well their characters fit.</summary>
        private float Compatibility(Member a, Member b)
        {
            float dw = Math.Abs(a.Profile[(int)Dim.Warmth] - b.Profile[(int)Dim.Warmth]);
            float ds = Math.Abs(a.Profile[(int)Dim.Steadiness] - b.Profile[(int)Dim.Steadiness]);
            float both = (a.Profile[(int)Dim.Warmth] + b.Profile[(int)Dim.Warmth]) / 2f;
            float calm = Math.Min(a.Profile[(int)Dim.Steadiness], b.Profile[(int)Dim.Steadiness]);
            if (!chemistry.TryGetValue(Key(Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id)), out var chem))
            {
                chem = (float)(rng.NextDouble() * 30 - 15);
                chemistry[Key(Math.Min(a.Id, b.Id), Math.Max(a.Id, b.Id))] = chem;
            }
            return 15f + both * 25f + calm * 10f - dw * 12f - ds * 10f + chem;
        }

        private int? OpinionOf(int from, int to)
        {
            return opinion.TryGetValue(Key(from, to), out var v) ? (int)Math.Round(v) : 0;
        }

        private void MoveOpinions(IEnumerable<PersonObservation> people, int hours, HashSet<long> together)
        {
            foreach (var a in members.Values)
                foreach (var b in members.Values)
                {
                    if (a.Id == b.Id) continue;
                    var key = Key(a.Id, b.Id);
                    opinion.TryGetValue(key, out var current);
                    float target = Compatibility(a, b);
                    // Living in the same house means constant chances to like or dislike each other.
                    opinion[key] = current + (target - current) * (1f - (float)Math.Pow(1f - 0.004f, hours));
                }
        }

        /// <summary>Two people who simply do not get along, whatever their characters.</summary>
        public void Clash(int a, int b, float strength)
        {
            chemistry[Key(Math.Min(a, b), Math.Max(a, b))] = strength;
        }

        public void Observe(List<PersonObservation> people) => Registry.Apply(people, Now);

        /// <summary>One day: hourly bond updates, then the evening's family time, then daily rift handling.</summary>
        public void RunDays(int days, Action<int> afterDay = null)
        {
            var ids = members.Keys.OrderBy(i => i).ToList();
            for (int d = 0; d < days; d++)
            {
                for (int h = 0; h < 24; h++)
                {
                    Now += BondTuning.TicksPerHour;
                    MoveOpinions(null, 1, null);
                    var events = new List<FamilyEvent>();
                    Registry.UpdateBonds(OpinionOf, Now, events, id => members.TryGetValue(id, out var mm) ? mm.Profile : null);
                    if (h % 6 == 0)
                    {
                        var rifts = Registry.UpdateRifts(Now, true, events);
                        RiftsOpened += rifts.Count(e => e.Kind == FamilyEventKind.RiftOpened);
                        RiftsClosed += rifts.Count(e => e.Kind == FamilyEventKind.RiftResolved);
                    }
                    if (h == 6) DailyPipeline();
                    if (h == 19) EveningFamilyTime(ids);
                }
                if (d % 5 == 0) Registry.UpdateCulture(pid => members.TryGetValue(pid, out var cm) ? cm.Profile : null, Now);
                if (d % 5 == 0) Registry.UpdateCulture(pid => members.TryGetValue(pid, out var cm) ? cm.Profile : null, Now);
                afterDay?.Invoke(d);
            }
        }

        /// <summary>The rest of what the game's hourly sweep does, once a day: milestones, traditions, memories, announcements, gatherings.</summary>
        private void DailyPipeline()
        {
            var ev = new List<FamilyEvent>();
            ev.AddRange(Registry.UpdateMilestones(Now));
            var trad = Registry.UpdateTraditions(Now);
            ev.AddRange(trad);
            TraditionsAdopted += trad.Count(e => e.Kind == FamilyEventKind.TraditionAdopted);
            var bondEvents = Registry.UpdateBondMilestones(Now);
            BondAnnouncements += bondEvents.Count;
            Announced.AddRange(bondEvents);
            ev.AddRange(bondEvents);
            Registry.RecordMemories(ev, null, Now);
            MaxOpenRequests = Math.Max(MaxOpenRequests, Registry.GatheringRequests.Count);
            var due = Registry.DueRequests(Now);
            if (due.Count > 0) { Registry.MarkGatheringHeld(due[0].Id, Now); GatheringsHeld++; }
        }

        private void EveningFamilyTime(List<int> ids)
        {
            var order = ids.OrderBy(_ => rng.Next()).ToList();
            var used = new HashSet<int>();
            foreach (var id in order)
            {
                if (used.Contains(id)) continue;
                var m = members[id];
                int budget = ActivityTuning.DefaultDailyTargetTicks;
                var record = Registry.GetPerson(id);
                float appetite = record == null ? 1f : Registry.FamilyTimeAppetite(Registry.LineageIdFor(record), m.Profile);
                if (rng.NextDouble() > 0.55 * m.Sociability * appetite) continue;      // some evenings are spent on other things
                var free = ids.Where(o => o != id && !used.Contains(o)).ToList();
                // Mending a rift comes first.
                var rift = Registry.OpenRiftsOf(id).FirstOrDefault(r => free.Contains(r.A == id ? r.B : r.A) && Registry.MayTryReconcile(r.Id, Now));
                if (rift != null)
                {
                    int other = rift.A == id ? rift.B : rift.A;
                    var ab = Registry.GetBond(id, other);
                    var ba = Registry.GetBond(other, id);
                    float forgiving = (members[id].Profile[(int)Dim.Warmth] + members[id].Profile[(int)Dim.Steadiness] + members[other].Profile[(int)Dim.Warmth] + members[other].Profile[(int)Dim.Steadiness]) / 2f;
                    bool ok = rng.NextDouble() < ConflictTuning.ReconcileChance(ab?.Trust ?? 25f, ba?.Trust ?? 25f, forgiving);
                    var closed = Registry.TryReconcile(rift.Id, ok, Now);
                    if (closed != null && closed.Kind == FamilyEventKind.RiftResolved) RiftsClosed++;
                    ReconcileTries++;
                    if (ok) ReconcileSuccesses++;
                    used.Add(id); used.Add(other);
                    continue;
                }
                var ranked = Registry.RankCompanions(id, free, Now, appetite);
                if (ranked.Count == 0) continue;
                var pick = ranked[0];
                Registry.RecordTogether(id, pick.Id, budget, Now);
                Registry.AddFamilyTime(id, budget, Now);
                Registry.AddFamilyTime(pick.Id, budget, Now);
                FamilyHours += budget / (float)BondTuning.TicksPerHour;
                HoursBy[id] = (HoursBy.TryGetValue(id, out var prior) ? prior : 0f) + budget / (float)BondTuning.TicksPerHour;
                used.Add(id); used.Add(pick.Id);
            }
        }

        // ------------------------------------------------------------------ reporting

        public string Report(string title, IEnumerable<int> ids)
        {
            var list = ids.ToList();
            var sb = new StringBuilder();
            var bonds = Registry.Bonds.Where(b => list.Contains(b.From) && list.Contains(b.To)).ToList();
            float att = bonds.Count == 0 ? 0 : bonds.Average(b => b.Attachment);
            float ten = bonds.Count == 0 ? 0 : bonds.Average(b => b.Tension);
            float tru = bonds.Count == 0 ? 0 : bonds.Average(b => b.Trust);
            int high = bonds.Count(b => b.Attachment >= 90f);
            int low = bonds.Count(b => b.Attachment < 35f);
            float spread = bonds.Count == 0 ? 0 : (float)Math.Sqrt(bonds.Average(b => (b.Attachment - att) * (b.Attachment - att)));
            sb.Append($"{title,-26} att {att,5:0.0} (sd {spread,4:0.0}) trust {tru,5:0.0} tension {ten,5:0.0} | >=90: {high,2}/{bonds.Count}  <35: {low,2}");
            return sb.ToString();
        }
    }
}
