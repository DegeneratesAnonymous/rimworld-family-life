using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// When a family is given a zone, the beds in it are shared out among the family: couples to double beds, babies to cribs, children to the
    /// bed nearest a parent, everyone else to singles (see <see cref="BedPlan"/>). Beds owned by someone outside the family are left alone, and so
    /// are beds the player locked with Wil's Better Beds when that is installed. Anyone who ends up without a bed is left to the game.
    /// </summary>
    internal static class ZoneBeds
    {
        private static Func<Building_Bed, Pawn, bool> lockCheck;
        private static bool lockLooked;

        /// <summary>Wil's Better Beds lets the player lock a bed to its owner. Standalone, nothing is locked.</summary>
        private static bool IsLocked(Building_Bed bed, Pawn owner)
        {
            if (!lockLooked)
            {
                lockLooked = true;
                try
                {
                    var type = GenTypes.GetTypeInAnyAssembly("WilsBetterBeds.BedLocks");
                    var method = type?.GetMethod("IsLocked", new[] { typeof(Building_Bed), typeof(Pawn) });
                    if (method != null) lockCheck = (Func<Building_Bed, Pawn, bool>)Delegate.CreateDelegate(typeof(Func<Building_Bed, Pawn, bool>), method);
                }
                catch (Exception) { lockCheck = null; }
            }
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

        /// <returns>How many people were given a bed.</returns>
        public static int Deal(FamilyComponent component, Lineage lineage, Zone_Family zone)
        {
            var map = zone.Map;
            var registry = component.Registry;

            var pawns = new Dictionary<int, Pawn>();
            foreach (int id in component.ZoneMemberIds(lineage))
            {
                var pawn = component.TryGetPawn(id);
                if (pawn == null || !pawn.Spawned || pawn.Map != map || pawn.Dead || !pawn.IsColonist || pawn.IsPrisoner || pawn.IsSlave
                    || pawn.HostFaction != null || pawn.ownership == null || !pawn.RaceProps.Humanlike) continue;
                pawns[pawn.thingIDNumber] = pawn;
            }
            if (pawns.Count == 0) return 0;

            var beds = new Dictionary<int, Building_Bed>();
            foreach (var bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (bed.Medical || !bed.ForColonists || !bed.Spawned || !bed.OccupiedRect().Cells.Any(zone.ContainsCell)) continue;
                // Someone from outside the family sleeps here, or the player locked it to its owner: not ours to hand out.
                if (bed.OwnersForReading.Any(o => !pawns.ContainsKey(o.thingIDNumber) || IsLocked(bed, o))) continue;
                beds[bed.thingIDNumber] = bed;
            }
            if (beds.Count == 0) return 0;

            var sleepers = pawns.Values.Select(p =>
            {
                var record = registry.GetPerson(p.thingIDNumber);
                var stage = p.DevelopmentalStage == DevelopmentalStage.Baby || p.DevelopmentalStage == DevelopmentalStage.Newborn ? SleeperStage.Baby
                          : p.DevelopmentalStage == DevelopmentalStage.Child ? SleeperStage.Child : SleeperStage.Adult;
                return new BedSleeper
                {
                    Id = p.thingIDNumber,
                    Stage = stage,
                    PartnerIds = record == null ? new List<int>() : record.PartnerIds().ToList(),
                    ParentIds = record == null ? new List<int>() : record.ParentIds.ToList()
                };
            }).ToList();
            var slots = beds.Values.Select(b => new BedSlot
            {
                Id = b.thingIDNumber,
                Capacity = Math.Max(1, b.SleepingSlotsCount),
                RoomId = RoomOf(b),
                Comfort = Comfort(b),
                X = b.Position.x,
                Z = b.Position.z
            }).ToList();

            var plan = BedPlan.Deal(sleepers, slots, (personId, bedId) => RestUtility.CanUseBedEver(pawns[personId], beds[bedId].def));
            if (plan.Count == 0) return 0;

            // Everyone in the family lets go of the beds in the zone; then each is given theirs.
            foreach (var bed in beds.Values)
                foreach (var owner in bed.OwnersForReading.ToList()) owner.ownership.UnclaimBed();
            int given = 0;
            foreach (var pair in plan.OrderBy(p => p.Key))
            {
                var pawn = pawns[pair.Key];
                var bed = beds[pair.Value];
                if (pawn.ownership.OwnedBed != bed) pawn.ownership.UnclaimBed();
                if (pawn.ownership.ClaimBedIfNonMedical(bed)) given++;
            }
            if (FamilyLifeMod.Settings.logEvents)
                Log.Message("[Wil's Family Life] Beds in " + zone.label + " shared out: " + given + " of " + pawns.Count + " people have one.");
            return given;
        }
    }
}
