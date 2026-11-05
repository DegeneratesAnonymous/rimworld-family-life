using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Beds in a family's zone (see <see cref="BedPlan"/> for who gets which):
    /// - Share out: when a family is given a zone, or on request, its beds are given out afresh. Beds the player set by hand since are left
    ///   alone on a later share-out (only beds this mod gave out are taken back), as are beds owned by people outside the family and beds
    ///   locked in Wil's Better Beds.
    /// - Upkeep: a newborn is put in its parents' room, a person with no bed gets one, and, without Wil's Better Beds, a couple who sleep
    ///   apart are housed together. Only the people affected are touched; everyone else keeps their bed.
    /// - Growing up: a child past the baby stage who is still in a parent's room moves to the most comfortable free bed in another room.
    /// Wil's Better Beds, when present, leaves the children and babies of a managed family to this (see <see cref="BedHousingApi"/>).
    /// </summary>
    internal static class ZoneBeds
    {
        private static Func<Building_Bed, Pawn, bool> lockCheck;
        private static bool lockLooked;
        private static readonly Dictionary<int, int> tried = new Dictionary<int, int>();      // pawn id -> tick of the last try; not saved
        private const int RetryTicks = 2 * GenDate.TicksPerDay;

        private static void LookForBetterBeds()
        {
            if (lockLooked) return;
            lockLooked = true;
            try
            {
                var type = GenTypes.GetTypeInAnyAssembly("WilsBetterBeds.BedLocks");
                var method = type?.GetMethod("IsLocked", new[] { typeof(Building_Bed), typeof(Pawn) });
                if (method != null) lockCheck = (Func<Building_Bed, Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Building_Bed, Pawn, bool>), method);
            }
            catch (Exception) { lockCheck = null; }
        }

        /// <summary>Wil's Better Beds is installed (it keeps couples together and has its own say on children).</summary>
        public static bool BetterBedsPresent { get { LookForBetterBeds(); return lockCheck != null; } }

        /// <summary>Wil's Better Beds lets the player lock a bed to its owner. Standalone, nothing is locked.</summary>
        private static bool IsLocked(Building_Bed bed, Pawn owner)
        {
            LookForBetterBeds();
            try { return lockCheck != null && lockCheck(bed, owner); }
            catch (Exception) { return false; }
        }

        private static int RoomOf(Building_Bed bed)
        {
            var room = bed.GetRoom();
            return room == null || room.PsychologicallyOutdoors ? -1 : room.ID;
        }

        /// <summary>How good it is to sleep in this bed: how impressive the room is, plus the bed's own comfort. A bed outdoors is the worst.</summary>
        private static float Comfort(Building_Bed bed)
        {
            var room = bed.GetRoom();
            float impressiveness = room == null || room.PsychologicallyOutdoors ? -50f : room.GetStat(RoomStatDefOf.Impressiveness);
            return impressiveness + 30f * bed.GetStatValue(StatDefOf.Comfort) + 10f * bed.GetStatValue(StatDefOf.BedRestEffectiveness);
        }

        private static bool Stays(Pawn pawn, Map map) =>
            pawn != null && pawn.Spawned && pawn.Map == map && !pawn.Dead && pawn.IsColonist && !pawn.IsPrisoner && !pawn.IsSlave
            && pawn.HostFaction == null && pawn.ownership != null && pawn.RaceProps.Humanlike;

        private static bool IsBaby(Pawn p) => p.DevelopmentalStage == DevelopmentalStage.Baby || p.DevelopmentalStage == DevelopmentalStage.Newborn;

        private static bool InZones(Building_Bed bed, List<Zone_Family> zones) => bed != null && bed.Spawned && zones.Any(z => z.Map == bed.Map && bed.OccupiedRect().Cells.Any(z.ContainsCell));

        // ---- what this mod gave out

        private static bool WasDealt(Lineage lineage, Pawn pawn, Building_Bed bed)
        {
            var d = lineage.BedDeals;
            for (int i = 0; i + 1 < d.Count; i += 2) if (d[i] == pawn.thingIDNumber && d[i + 1] == bed.thingIDNumber) return true;
            return false;
        }

        private static void Forget(Lineage lineage, int pawnId)
        {
            var d = lineage.BedDeals;
            for (int i = d.Count - 2; i >= 0; i -= 2) if (d[i] == pawnId) d.RemoveRange(i, 2);
        }

        private static void Remember(Lineage lineage, Pawn pawn, Building_Bed bed)
        {
            Forget(lineage, pawn.thingIDNumber);
            lineage.BedDeals.Add(pawn.thingIDNumber);
            lineage.BedDeals.Add(bed.thingIDNumber);
        }

        // ---- sharing out and upkeep

        /// <summary>Shares the zone's beds out among the whole family. Returns how many people were given a bed.</summary>
        public static int Deal(FamilyComponent component, Lineage lineage, Zone_Family zone) => Run(component, lineage, zone.Map, null, false);

        /// <summary>Puts right whoever needs it: a newborn, someone with no bed, and (without Better Beds) a couple who sleep apart.</summary>
        public static void Upkeep(FamilyComponent component)
        {
            int now = Find.TickManager.TicksGame;
            foreach (var lineage in component.Registry.LineagesWithZones())
            {
                try
                {
                    var zones = FamilyZones.ZonesOf(lineage).ToList();
                    if (zones.Count == 0) continue;
                    foreach (var map in zones.Select(z => z.Map).Distinct().ToList())
                    {
                        var members = new Dictionary<int, Pawn>();
                        foreach (int id in component.ZoneMemberIds(lineage))
                        {
                            var p = component.TryGetPawn(id);
                            if (Stays(p, map)) members[p.thingIDNumber] = p;
                        }
                        var affected = new HashSet<int>();
                        foreach (var p in members.Values)
                        {
                            if (tried.TryGetValue(p.thingIDNumber, out int at) && now - at < RetryTicks) continue;
                            var record = component.Registry.GetPerson(p.thingIDNumber);
                            if (record == null) continue;
                            var bed = p.ownership.OwnedBed;
                            if (bed == null) { if (!p.Dead) affected.Add(p.thingIDNumber); continue; }
                            if (IsBaby(p))
                            {
                                var parentBeds = record.ParentIds.Select(id => members.TryGetValue(id, out var par) ? par.ownership.OwnedBed : null).Where(b => b != null && InZones(b, zones)).ToList();
                                if (parentBeds.Count > 0 && !parentBeds.Any(b => RoomOf(b) >= 0 && RoomOf(b) == RoomOf(bed))) affected.Add(p.thingIDNumber);
                            }
                        }
                        if (!BetterBedsPresent)
                        {
                            foreach (var p in members.Values)
                            {
                                if (p.DevelopmentalStage != DevelopmentalStage.Adult || p.ownership.OwnedBed == null) continue;
                                var record = component.Registry.GetPerson(p.thingIDNumber);
                                if (record == null) continue;
                                foreach (int mateId in record.PartnerIds())
                                {
                                    if (mateId < p.thingIDNumber || !members.TryGetValue(mateId, out var mate) || mate.ownership.OwnedBed == null) continue;
                                    var a = p.ownership.OwnedBed; var b = mate.ownership.OwnedBed;
                                    if (a == b || (RoomOf(a) >= 0 && RoomOf(a) == RoomOf(b))) continue;
                                    if (tried.TryGetValue(p.thingIDNumber, out int at) && now - at < RetryTicks) continue;
                                    affected.Add(p.thingIDNumber); affected.Add(mateId);
                                }
                            }
                        }
                        if (affected.Count == 0) continue;
                        foreach (int id in affected) tried[id] = now;
                        int given = Run(component, lineage, map, affected, true);
                        if (given > 0 && FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.logEvents)
                            Log.Message("[Wil's Family Life] " + given + " people in " + component.Registry.LineageDisplayName(lineage) + " were given a better bed.");
                    }
                }
                catch (Exception ex) { Log.Warning("[Wil's Family Life] Keeping a family's beds in order failed: " + ex.Message); }
            }
        }

        /// <summary>
        /// One pass of the planner. With no <paramref name="only"/> the whole family is dealt afresh; with it, just those people, around everyone
        /// who keeps the bed they have. <paramref name="careful"/> only accepts a change that is clearly better for each person.
        /// </summary>
        private static int Run(FamilyComponent component, Lineage lineage, Map map, HashSet<int> only, bool careful)
        {
            var registry = component.Registry;
            var zones = FamilyZones.ZonesOf(lineage).Where(z => z.Map == map).ToList();
            if (zones.Count == 0) return 0;

            var members = new Dictionary<int, Pawn>();
            foreach (int id in component.ZoneMemberIds(lineage))
            {
                var pawn = component.TryGetPawn(id);
                if (Stays(pawn, map)) members[pawn.thingIDNumber] = pawn;
            }
            if (members.Count == 0) return 0;
            bool firstShare = lineage.BedDeals.Count == 0;

            // Who is dealt, who keeps what they have.
            var dealt = new Dictionary<int, Pawn>();
            var keeps = new Dictionary<int, int>();
            var beds = new Dictionary<int, Building_Bed>();
            foreach (var bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed.Medical || !bed.ForColonists || !bed.Spawned || !InZones(bed, zones)) continue;
                var owners = bed.OwnersForReading.ToList();
                if (owners.Any(o => !members.ContainsKey(o.thingIDNumber))) continue;                       // someone outside the family sleeps here
                bool released = owners.All(o => !IsLocked(bed, o) && (firstShare || WasDealt(lineage, o, bed))
                                              && (only == null || only.Contains(o.thingIDNumber)));
                if (owners.Count == 0 || released) beds[bed.thingIDNumber] = bed;
                else foreach (var o in owners) keeps[o.thingIDNumber] = bed.thingIDNumber;                 // theirs to keep
            }
            foreach (var p in members.Values)
            {
                if (keeps.ContainsKey(p.thingIDNumber)) continue;
                if (only != null && !only.Contains(p.thingIDNumber))
                {
                    var own = p.ownership.OwnedBed;
                    if (own != null && InZones(own, zones)) keeps[p.thingIDNumber] = own.thingIDNumber;
                    continue;
                }
                dealt[p.thingIDNumber] = p;
            }
            if (dealt.Count == 0 || beds.Count == 0) return 0;

            BedSleeper Describe(Pawn p)
            {
                var record = registry.GetPerson(p.thingIDNumber);
                return new BedSleeper
                {
                    Id = p.thingIDNumber,
                    Stage = IsBaby(p) ? SleeperStage.Baby : p.DevelopmentalStage == DevelopmentalStage.Child ? SleeperStage.Child : SleeperStage.Adult,
                    AgeYears = p.ageTracker.AgeBiologicalYearsFloat,
                    PartnerIds = record == null ? new List<int>() : record.PartnerIds().ToList(),
                    ParentIds = record == null ? new List<int>() : record.ParentIds.ToList()
                };
            }
            var sleepers = dealt.Values.Select(Describe).ToList();
            var slots = beds.Values.Select(b => new BedSlot
            {
                Id = b.thingIDNumber,
                Capacity = Math.Max(1, b.SleepingSlotsCount),
                RoomId = RoomOf(b),
                Comfort = Comfort(b),
                X = b.Position.x,
                Z = b.Position.z
            }).ToList();

            var plan = BedPlan.Deal(sleepers, slots, (personId, bedId) => RestUtility.CanUseBedEver(dealt[personId], beds[bedId].def), keeps);
            if (plan.Count == 0) return 0;

            // In a careful pass a person only moves for a clear gain: a baby into a parent's room, a couple together, someone with no bed.
            if (careful)
            {
                foreach (var pair in plan.ToList())
                {
                    var pawn = dealt[pair.Key];
                    var current = pawn.ownership.OwnedBed;
                    var target = beds[pair.Value];
                    if (current == null || current == target) continue;
                    var record = registry.GetPerson(pawn.thingIDNumber);
                    bool better = false;
                    if (IsBaby(pawn) && record != null)
                    {
                        foreach (int parentId in record.ParentIds)
                        {
                            var parentBed = members.TryGetValue(parentId, out var par) ? par.ownership.OwnedBed : null;
                            if (parentBed != null && RoomOf(parentBed) >= 0 && RoomOf(parentBed) == RoomOf(target)) better = true;
                        }
                    }
                    else if (record != null)
                    {
                        foreach (int mateId in record.PartnerIds())
                        {
                            if (!plan.TryGetValue(mateId, out int mateBedId)) continue;
                            var mateBed = beds[mateBedId];
                            if (mateBed == target || (RoomOf(mateBed) >= 0 && RoomOf(mateBed) == RoomOf(target))) better = true;
                        }
                    }
                    if (!better) plan.Remove(pair.Key);
                }
                if (plan.Count == 0) return 0;
            }

            // Whoever is dealt lets go of their bed in the zone; then each is given theirs.
            foreach (var bed in beds.Values)
                foreach (var owner in bed.OwnersForReading.ToList())
                    if (dealt.ContainsKey(owner.thingIDNumber) && (!careful || plan.ContainsKey(owner.thingIDNumber) || only == null)) { owner.ownership.UnclaimBed(); Forget(lineage, owner.thingIDNumber); }
            int given = 0;
            foreach (var pair in plan.OrderBy(p => p.Key))
            {
                var pawn = dealt[pair.Key];
                var bed = beds[pair.Value];
                if (pawn.ownership.OwnedBed != bed) pawn.ownership.UnclaimBed();
                if (pawn.ownership.ClaimBedIfNonMedical(bed)) { given++; Remember(lineage, pawn, bed); }
            }
            if (FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.logEvents && only == null)
                Log.Message("[Wil's Family Life] Beds shared out: " + given + " of " + members.Count + " people have one.");
            return given;
        }

        // ---- growing up

        /// <summary>
        /// A baby sleeps in its parents' room. Once it has grown past that stage, a child who is still in a parent's room moves to the most
        /// comfortable free bed in the family's zone that is in another room (a small child, one still within reach of a parent). Nothing
        /// moves if there is no such bed, or the child's bed is locked.
        /// </summary>
        public static void GrowOut(FamilyComponent component)
        {
            var registry = component.Registry;
            int moved = 0;
            foreach (var lineage in registry.LineagesWithZones())
            {
                var zones = FamilyZones.ZonesOf(lineage).ToList();
                foreach (int id in component.ZoneMemberIds(lineage))
                {
                    if (moved >= 2) return;
                    var child = component.TryGetPawn(id);
                    if (child == null || !child.Spawned || child.Dead || !child.IsColonist || child.ownership?.OwnedBed == null) continue;
                    if (child.DevelopmentalStage != DevelopmentalStage.Child) continue;
                    var bed = child.ownership.OwnedBed;
                    if (bed.Medical || IsLocked(bed, child)) continue;
                    var record = registry.GetPerson(id);
                    if (record == null) continue;
                    int room = RoomOf(bed);
                    if (room < 0) continue;
                    var parentBeds = new List<Building_Bed>();
                    foreach (int parentId in record.ParentIds)
                    {
                        var parentBed = component.TryGetPawn(parentId)?.ownership?.OwnedBed;
                        if (parentBed != null && parentBed.Spawned && parentBed.Map == bed.Map) parentBeds.Add(parentBed);
                    }
                    var parentRooms = new HashSet<int>(parentBeds.Select(RoomOf));
                    if (!parentRooms.Contains(room)) continue;           // already in a room of their own

                    bool small = child.ageTracker.AgeBiologicalYearsFloat < BedPlan.SmallChildAge;
                    float here = Comfort(bed);
                    Building_Bed best = null; float bestComfort = here;
                    foreach (var candidate in bed.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
                    {
                        if (candidate == bed || candidate.Medical || !candidate.ForColonists || !candidate.Spawned || candidate.OwnersForReading.Count > 0) continue;
                        if (!InZones(candidate, zones)) continue;
                        if (parentRooms.Contains(RoomOf(candidate)) || !RestUtility.CanUseBedEver(child, candidate.def)) continue;
                        if (small && parentBeds.Count > 0 && parentBeds.Min(p => (p.Position - candidate.Position).LengthHorizontal) > BedPlan.SmallChildReach) continue;
                        float comfort = Comfort(candidate);
                        if (comfort > bestComfort) { bestComfort = comfort; best = candidate; }
                    }
                    if (best == null) continue;
                    child.ownership.UnclaimBed();
                    if (child.ownership.ClaimBedIfNonMedical(best)) { moved++; Remember(lineage, child, best); }
                    else child.ownership.ClaimBedIfNonMedical(bed);       // exactly as they were
                }
            }
        }

        /// <summary>For <see cref="BedHousingApi"/>: whether this pawn's bed, or a parent's, is in a zone given to the pawn's family.</summary>
        public static bool IsManaged(Pawn pawn)
        {
            var component = FamilyComponent.Current;
            if (component == null || pawn == null) return false;
            foreach (var lineage in component.Registry.LineagesWithZones())
            {
                if (!component.ZoneMemberIds(lineage).Contains(pawn.thingIDNumber)) continue;
                var zones = FamilyZones.ZonesOf(lineage).ToList();
                if (zones.Count == 0) continue;
                if (InZones(pawn.ownership?.OwnedBed, zones)) return true;
                var record = component.Registry.GetPerson(pawn.thingIDNumber);
                if (record != null && record.ParentIds.Any(id => InZones(component.TryGetPawn(id)?.ownership?.OwnedBed, zones))) return true;
            }
            return false;
        }
    }

    /// <summary>For other mods (Wil's Better Beds looks this up by name): whether this mod is housing the pawn, so they can leave them be.</summary>
    public static class BedHousingApi
    {
        public static bool IsManaged(Pawn pawn)
        {
            try { return ZoneBeds.IsManaged(pawn); }
            catch (Exception) { return false; }
        }
    }
}
