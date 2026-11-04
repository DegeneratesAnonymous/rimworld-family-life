using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>Questions the event conditions ask of the records. Each answers from what was really recorded, never from a guess.</summary>
    public sealed partial class FamilyRegistry
    {
        public bool IsParentOf(int parentId, int childId)
        {
            EnsureIndexes();
            return personById.TryGetValue(childId, out var child) && child.ParentIds.Contains(parentId);
        }

        private List<PersonRecord> JuvenileChildrenInColony(int parentId)
        {
            return ChildrenOf(parentId).Select(id => personById.TryGetValue(id, out var k) ? k : null).Where(k => k != null && k.InColony && k.Juvenile).ToList();
        }

        /// <summary>A parent's recent time with this child is well below what a sibling has had. The same measure that opens a neglect rift, but milder.</summary>
        public bool IsOverlooked(int parentId, int childId)
        {
            EnsureIndexes();
            var kids = JuvenileChildrenInColony(parentId);
            if (kids.Count < 2 || kids.All(k => k.Id != childId)) return false;
            float mine = GetBond(parentId, childId)?.QualityRecent ?? 0f;
            float best = kids.Where(k => k.Id != childId).Select(k => GetBond(parentId, k.Id)?.QualityRecent ?? 0f).DefaultIfEmpty(0f).Max();
            return best >= 2f && mine <= best * 0.4f;
        }

        /// <summary>One sibling has had little time with the others while the rest have been together.</summary>
        public bool IsLeftOutSibling(int childId)
        {
            EnsureIndexes();
            if (!personById.TryGetValue(childId, out var child) || !child.Juvenile) return false;
            var sibs = SiblingsOf(childId).Select(id => personById.TryGetValue(id, out var s) ? s : null).Where(s => s != null && s.InColony && s.Juvenile).ToList();
            if (sibs.Count < 2) return false;
            float mine = sibs.Select(s => Math.Max(GetBond(childId, s.Id)?.QualityRecent ?? 0f, GetBond(s.Id, childId)?.QualityRecent ?? 0f)).DefaultIfEmpty(0f).Max();
            float others = 0f;
            for (int i = 0; i < sibs.Count; i++)
                for (int j = i + 1; j < sibs.Count; j++)
                    others = Math.Max(others, Math.Max(GetBond(sibs[i].Id, sibs[j].Id)?.QualityRecent ?? 0f, GetBond(sibs[j].Id, sibs[i].Id)?.QualityRecent ?? 0f));
            return others >= 1.5f && mine <= others * 0.25f;
        }

        /// <summary>The adult helped, taught or comforted this child lately, and it went well. That is what a parent's thanks answers.</summary>
        public bool HelpedChildRecently(int adultId, int childId, int now)
        {
            string[] help = { "B2-01", "B2-02", "B2-08", "B2-09", "C2-01", "C2-03", "C2-05", "C2-09" };
            return EventLog.Any(e => e.A == adultId && e.B == childId && e.Tier >= EventTier.Good && Array.IndexOf(help, e.EventId) >= 0 && now - e.Tick <= 5 * ActivityTuning.TicksPerDay);
        }

        /// <summary>How many good moments children of the two families have had with each other lately.</summary>
        public int GoodChildMomentsBetween(int lineageA, int lineageB, int sinceTick)
        {
            EnsureIndexes();
            int n = 0;
            foreach (var e in EventLog)
            {
                if (e.Tick < sinceTick || e.Tier < EventTier.Good) continue;
                if (!personById.TryGetValue(e.A, out var a) || !personById.TryGetValue(e.B, out var b) || !a.Juvenile || !b.Juvenile) continue;
                int la = LineageIdFor(a), lb = LineageIdFor(b);
                if ((la == lineageA && lb == lineageB) || (la == lineageB && lb == lineageA)) n++;
            }
            return n;
        }

        public bool LineageHasMemoryOf(int lineageId, int personA, int personB)
        {
            return Memories.Any(m => m.LineageId == lineageId && (m.A == personA || m.A == personB || m.B == personA || m.B == personB)
                                     && m.Kind != MemoryKind.Rupture);
        }

        /// <summary>Sets the standing between two families directly. For the developer tools and tests; play moves it only through events.</summary>
        public FamilyTie SetTie(int lineageA, int lineageB, float familiarity, float goodwill, float grievance, int shared, int tick)
        {
            EnsureIndexes();
            var tie = EnsureTie(lineageA, lineageB);
            if (tie == null) return null;
            tie.Familiarity = BondTuning.Clamp(familiarity); tie.Goodwill = BondTuning.Clamp(goodwill); tie.Grievance = BondTuning.Clamp(grievance);
            tie.Shared = shared; tie.LastTick = tick;
            return tie;
        }

        /// <summary>
        /// Two families with a real grievance between them, both here in the colony, are brought together over a meal to mend it. The
        /// family that gave the offence holds it when that is known. At most one per pair every twelve days, and it is only a request:
        /// whether it happens depends on someone being free and a place being found.
        /// </summary>
        public List<GatheringRequest> RequestReconciliations(int now, float minGrievance = 20f)
        {
            EnsureIndexes();
            var asked = new List<GatheringRequest>();
            foreach (var tie in Ties.Where(t => t.Grievance >= minGrievance && (t.LastGatheringTick == 0 || now - t.LastGatheringTick >= 12 * ActivityTuning.TicksPerDay)).ToList())
            {
                var a = Persons.Where(p => p.InColony && !p.Juvenile && LineageIdFor(p) == tie.LineageA).OrderBy(p => p.Id).ToList();
                var b = Persons.Where(p => p.InColony && !p.Juvenile && LineageIdFor(p) == tie.LineageB).OrderBy(p => p.Id).ToList();
                if (a.Count == 0 || b.Count == 0) continue;
                int host = tie.LineageA;
                if (tie.WrongedBy != 0 && personById.TryGetValue(tie.WrongedBy, out var offender))
                {
                    int offenderFamily = LineageIdFor(offender);
                    if (offenderFamily == tie.LineageA || offenderFamily == tie.LineageB) host = offenderFamily;
                }
                var guests = host == tie.LineageA ? b : a;
                int before = GatheringRequests.Count;
                RequestGathering("reconciliation", host, guests[0].Id, now, ActivityTuning.TicksPerDay / 4, 3 * ActivityTuning.TicksPerDay);
                tie.LastGatheringTick = now;
                if (GatheringRequests.Count > before) asked.Add(GatheringRequests[GatheringRequests.Count - 1]);
            }
            return asked;
        }

        public bool LineageHasRupture(int lineageId) => Memories.Any(m => m.LineageId == lineageId && m.Kind == MemoryKind.Rupture);

        /// <summary>Who in a household is a child that an adult could be asked about for a promise. Used by tests and the history view.</summary>
        public List<EventRecord> RecentEventsOf(int personId, int count)
        {
            return EventLog.Where(e => e.A == personId || e.B == personId).OrderByDescending(e => e.Tick).Take(count).ToList();
        }

        public List<EventRecord> RecentEvents(int count) => EventLog.OrderByDescending(e => e.Tick).Take(count).ToList();
    }
}
