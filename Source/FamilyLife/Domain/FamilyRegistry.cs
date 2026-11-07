using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>
    /// The mod's durable family model: people, lineages, households and a bounded chronicle.
    /// It contains no game types. The game layer feeds it <see cref="PersonObservation"/>s and it works out,
    /// deterministically and idempotently, what changed (see FamilyRegistry.Apply.cs).
    /// </summary>
    public sealed partial class FamilyRegistry
    {
        public const int CurrentDataVersion = 3;
        public const int MaxChronicle = 400;
        public const int AncestorDepthLimit = 12;
        public const int NewbornAgeTicks = 60000;   // a person first seen younger than a day, with a parent, counts as born

        public int DataVersion = CurrentDataVersion;
        public int NextLineageId = 1;
        public int NextHouseholdId = 1;
        public List<PersonRecord> Persons = new List<PersonRecord>();
        public List<Lineage> Lineages = new List<Lineage>();
        public List<Household> Households = new List<Household>();
        public List<FamilyEvent> Chronicle = new List<FamilyEvent>();

        private readonly Dictionary<int, PersonRecord> personById = new Dictionary<int, PersonRecord>();
        private readonly Dictionary<int, Lineage> lineageById = new Dictionary<int, Lineage>();
        private readonly Dictionary<int, Household> householdById = new Dictionary<int, Household>();
        private readonly Dictionary<int, List<int>> childrenByParent = new Dictionary<int, List<int>>();
        private bool indexed;

        // ------------------------------------------------------------------ indexes

        /// <summary>Rebuilds lookups and repairs anything that could not have come from this code (a hand-edited or damaged save).</summary>
        public void RebuildIndexes()
        {
            Persons = Persons ?? new List<PersonRecord>();
            Lineages = Lineages ?? new List<Lineage>();
            Households = Households ?? new List<Household>();
            Chronicle = Chronicle ?? new List<FamilyEvent>();

            personById.Clear();
            lineageById.Clear();
            householdById.Clear();

            Persons.RemoveAll(p => p == null);
            var uniquePersons = new List<PersonRecord>(Persons.Count);
            foreach (var p in Persons)
            {
                if (personById.ContainsKey(p.Id)) continue;
                p.Name = p.Name ?? "";
                p.LastName = p.LastName ?? "";
                p.ParentIds = p.ParentIds ?? new List<int>();
                p.SpouseIds = p.SpouseIds ?? new List<int>();
                p.LoverIds = p.LoverIds ?? new List<int>();
                p.ExIds = p.ExIds ?? new List<int>();
                personById[p.Id] = p;
                uniquePersons.Add(p);
            }
            Persons = uniquePersons;

            Lineages.RemoveAll(l => l == null);
            var uniqueLineages = new List<Lineage>(Lineages.Count);
            foreach (var l in Lineages)
            {
                if (lineageById.ContainsKey(l.Id)) continue;
                l.Name = l.Name ?? "";
                l.Affiliations = l.Affiliations ?? new List<Affiliation>();
                l.Affiliations.RemoveAll(a => a == null);
                lineageById[l.Id] = l;
                uniqueLineages.Add(l);
                if (l.Id >= NextLineageId) NextLineageId = l.Id + 1;
            }
            Lineages = uniqueLineages;

            // Absorption links are historical pointers, not arbitrary graphs. Repair damaged saves before family queries run.
            foreach(var lineage in Lineages)
            {
                lineage.ZoneApplied=lineage.ZoneApplied??new List<int>();lineage.Stewards=lineage.Stewards??new List<int>();
                if(lineage.AbsorbedIntoId==lineage.Id || lineage.AbsorbedIntoId!=0 && !lineageById.ContainsKey(lineage.AbsorbedIntoId)) lineage.AbsorbedIntoId=0;
                var seen=new HashSet<int>{lineage.Id};int next=lineage.AbsorbedIntoId;
                while(next!=0 && lineageById.TryGetValue(next,out var house))
                {
                    if(!seen.Add(next)){lineage.AbsorbedIntoId=0;break;}
                    next=house.AbsorbedIntoId;
                }
                if(lineage.AbsorbedIntoId!=0)
                {
                    lineage.ZoneAreaId=0;lineage.ZoneMapId=-1;lineage.ZoneApplied.Clear();lineage.HeadId=0;lineage.Stewards.Clear();
                }
            }

            Households.RemoveAll(h => h == null);
            var uniqueHouseholds = new List<Household>(Households.Count);
            foreach (var h in Households)
            {
                if (householdById.ContainsKey(h.Id)) continue;
                h.ResidentIds = h.ResidentIds ?? new List<int>();
                householdById[h.Id] = h;
                uniqueHouseholds.Add(h);
                if (h.Id >= NextHouseholdId) NextHouseholdId = h.Id + 1;
            }
            Households = uniqueHouseholds;

            // A household only holds people who exist, are in the colony, and point back at it.
            foreach (var h in Households)
                h.ResidentIds.RemoveAll(id => !personById.TryGetValue(id, out var p) || p.HouseholdId != h.Id || !p.InColony);
            foreach (var p in Persons)
            {
                if (p.HouseholdId == 0) continue;
                if (!householdById.TryGetValue(p.HouseholdId, out var h)) { p.HouseholdId = 0; continue; }
                if (!p.InColony) { p.LastHouseholdId = p.HouseholdId; p.HouseholdId = 0; continue; }
                if (!h.ResidentIds.Contains(p.Id)) h.ResidentIds.Add(p.Id);
            }
            foreach (var p in Persons)
                if (p.PrimaryLineageId != 0 && !lineageById.ContainsKey(p.PrimaryLineageId)) p.PrimaryLineageId = 0;

            foreach (var l in Lineages)
            {
                l.Culture = l.Culture ?? new List<float>();
                while (l.Culture.Count < DimInfo.Count) l.Culture.Add(0f);
                l.Situation = l.Situation ?? new HouseSituation();
                l.Ambition = l.Ambition ?? new HouseAmbitionRecord();
                l.ZoneApplied = l.ZoneApplied ?? new List<int>();
                l.Traditions = l.Traditions ?? new List<TraditionRecord>();
                l.Traditions.RemoveAll(t => t == null || TraditionCatalog.IndexOf(t.Id) < 0);
            }
            foreach (var p in Persons)
            {
                p.SpouseSince = p.SpouseSince ?? new List<int>();
                p.TopSkill = p.TopSkill ?? "";
            }
            Rifts = Rifts ?? new List<Rift>();
            Rifts.RemoveAll(r => r == null || !personById.ContainsKey(r.A) || !personById.ContainsKey(r.B));
            Memories = Memories ?? new List<Memory>();
            Memories.RemoveAll(m => m == null || !lineageById.ContainsKey(m.LineageId));
            GatheringRequests = GatheringRequests ?? new List<GatheringRequest>();
            GatheringRequests.RemoveAll(r => r == null || !lineageById.ContainsKey(r.LineageId));
            StewardRecords = StewardRecords ?? new List<StewardRecord>();
            StewardRecords.RemoveAll(s => s == null || !personById.ContainsKey(s.PersonId) || !lineageById.ContainsKey(s.FamilyId));
            foreach (var house in Lineages)
                foreach (var steward in house.Stewards)
                    if (StewardshipOf(steward, house.Id, true) == null) BeginStewardship(steward, house.Id, 0);
            foreach (var service in StewardRecords.Where(s => s.Active).ToList())
                if (!lineageById.TryGetValue(service.FamilyId, out var served) || !served.Stewards.Contains(service.PersonId))
                    service.EndedTick = Math.Max(1, service.AppointedTick + 1);
            MarriageAlliances = MarriageAlliances ?? new List<MarriageAlliance>();
            MarriageAlliances.RemoveAll(a => a == null || !personById.ContainsKey(a.PawnA) || !personById.ContainsKey(a.PawnB));
            foreach(var alliance in MarriageAlliances){alliance.KnownBy=alliance.KnownBy??new List<int>();NextMarriageAllianceId=Math.Max(NextMarriageAllianceId,alliance.Id+1);}
            RebuildChildIndex();
            RebuildBondIndex();
            RebuildTimeIndex();
            RepairEvents();
            RepairStories();
            indexed = true;
        }

        private void EnsureIndexes()
        {
            if (!indexed) RebuildIndexes();
        }

        private void RebuildChildIndex()
        {
            childrenByParent.Clear();
            foreach (var p in Persons)
            {
                foreach (var parentId in p.ParentIds)
                {
                    if (!childrenByParent.TryGetValue(parentId, out var list))
                    {
                        list = new List<int>();
                        childrenByParent[parentId] = list;
                    }
                    if (!list.Contains(p.Id)) list.Add(p.Id);
                }
            }
            foreach (var list in childrenByParent.Values) list.Sort();
        }

        // ------------------------------------------------------------------ queries

        public PersonRecord GetPerson(int id)
        {
            EnsureIndexes();
            return personById.TryGetValue(id, out var p) ? p : null;
        }

        public Lineage GetLineage(int id)
        {
            EnsureIndexes();
            return lineageById.TryGetValue(id, out var l) ? l : null;
        }

        public Household GetHousehold(int id)
        {
            EnsureIndexes();
            return householdById.TryGetValue(id, out var h) ? h : null;
        }

        /// <summary>Everyone recorded as a child of this person, in id order.</summary>
        public IReadOnlyList<int> ChildrenOf(int id)
        {
            EnsureIndexes();
            return childrenByParent.TryGetValue(id, out var list) ? (IReadOnlyList<int>)list : Array.Empty<int>();
        }

        /// <summary>People who share at least one recorded parent with this person (half-siblings included).</summary>
        public List<int> SiblingsOf(int id)
        {
            EnsureIndexes();
            var result = new SortedSet<int>();
            if (!personById.TryGetValue(id, out var p)) return new List<int>();
            foreach (var parentId in p.ParentIds)
                foreach (var childId in ChildrenOf(parentId))
                    if (childId != id) result.Add(childId);
            return result.ToList();
        }

        public List<PersonRecord> MembersOf(int lineageId)
        {
            EnsureIndexes();
            if(!IsActiveHouse(lineageId)) return new List<PersonRecord>();
            return Persons.Where(p => p.SocialFamilyId>0
                    ? ActiveHouseIdIndexed(p.SocialFamilyId)==lineageId
                    : p.SocialFamilyId==0 && ActiveHouseIdIndexed(p.PrimaryLineageId)==lineageId)
                .OrderBy(p => p.BirthAbsTick).ThenBy(p => p.Id).ToList();
        }

        public List<PersonRecord> AffiliatesOf(int lineageId)
        {
            EnsureIndexes();
            var lineage = GetLineage(lineageId);
            var result = new List<PersonRecord>();
            if (lineage == null) return result;
            foreach (var a in lineage.Affiliations)
                if (personById.TryGetValue(a.PersonId, out var p) && p.SocialFamilyId==0) result.Add(p);
            return result.OrderBy(p => p.BirthAbsTick).ThenBy(p => p.Id).ToList();
        }

        /// <summary>Households that currently have at least one resident from this lineage (by descent or affiliation).</summary>
        public List<Household> HouseholdsOfLineage(int lineageId)
        {
            EnsureIndexes();
            var lineage = GetLineage(lineageId);
            var result = new List<Household>();
            foreach (var h in Households)
            {
                foreach (var rid in h.ResidentIds)
                {
                    if (!personById.TryGetValue(rid, out var p)) continue;
                    if (BelongsToFamily(p,lineageId))
                    {
                        result.Add(h);
                        break;
                    }
                }
            }
            return result.OrderBy(h => h.Id).ToList();
        }

        /// <summary>The lineage a person is shown under: their own by descent, else the first family they belong to by marriage or recognition.</summary>
        public int LineageIdFor(PersonRecord person)
        {
            EnsureIndexes();
            if (person == null) return 0;
            if (person.SocialFamilyId!=0) return person.SocialFamilyId>0 ? ActiveHouseId(person.SocialFamilyId) : 0;
            if (person.PrimaryLineageId != 0) return ActiveHouseId(person.PrimaryLineageId);
            foreach (var lineage in Lineages)
                if (lineage.AbsorbedIntoId==0 && lineage.HasAffiliate(person.Id)) return lineage.Id;
            return 0;
        }

        /// <summary>People in this lineage by descent or by affiliation, with the given presence.</summary>
        public int LivingCount(int lineageId, Presence presence)
        {
            EnsureIndexes();
            var lineage = GetLineage(lineageId);
            int n = 0;
            foreach (var p in Persons)
            {
                if (p.Presence != presence) continue;
                if (BelongsToFamily(p,lineageId)) n++;
            }
            return n;
        }

        /// <summary>How many generations below the lineage's founder this person is (0 for the founder, or when the chain is unknown).</summary>
        public int GenerationIn(PersonRecord person)
        {
            EnsureIndexes();
            int depth = 0;
            var current = person;
            var seen = new HashSet<int>();
            while (current != null && seen.Add(current.Id) && depth < AncestorDepthLimit)
            {
                PersonRecord next = null;
                foreach (var pid in current.ParentIds.OrderBy(x => x))
                {
                    if (personById.TryGetValue(pid, out var parent) && parent.PrimaryLineageId == person.PrimaryLineageId)
                    {
                        next = parent;
                        break;
                    }
                }
                if (next == null) break;
                depth++;
                current = next;
            }
            return depth;
        }

        public string LineageDisplayName(Lineage lineage)
        {
            if (lineage == null) return "";
            int sameName = Lineages.Count(l => string.Equals(l.Name, lineage.Name, StringComparison.OrdinalIgnoreCase));
            if (sameName <= 1) return lineage.Name;
            int ordinal = Lineages.Where(l => string.Equals(l.Name, lineage.Name, StringComparison.OrdinalIgnoreCase) && l.Id <= lineage.Id).Count();
            return lineage.Name + " (" + ToRoman(ordinal) + ")";
        }

        private static string ToRoman(int n)
        {
            string[] numerals = { "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X" };
            return n >= 1 && n <= numerals.Length ? numerals[n - 1] : n.ToString();
        }
    }
}
