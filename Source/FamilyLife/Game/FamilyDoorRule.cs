using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    internal struct DoorWish { public Pawn Pawn; public Zone_Family Zone; public int Tick; }

    /// <summary>
    /// Someone from outside the family does not walk into its zone. To the game, a family zone is out of bounds for them, as a restricted area
    /// is, so the work they would pick and the places they would wander to are never inside it. When they would have picked something there, that
    /// is noted as a wish and <see cref="GuestPasses"/> sends them to knock. Family members, a guest who has been let in, and anyone who has a bed
    /// in the zone are not kept out. Orders the player gives, drafted colonists and anyone in a mental break or down are not affected.
    /// </summary>
    internal static class FamilyDoorRule
    {
        private const int WishLife = 120, MemberCacheTicks = 600;
        private static readonly Dictionary<int, DoorWish> wishes = new Dictionary<int, DoorWish>();
        private static readonly Dictionary<long, KeyValuePair<int, bool>> memberCache = new Dictionary<long, KeyValuePair<int, bool>>();
        private static long Key(Pawn p, Lineage l) => ((long)p.thingIDNumber << 32) | (uint)l.Id;

        public static bool Enabled => FamilyLifeMod.Settings == null || FamilyLifeMod.Settings.familyDoors;

        public static void Reset() { wishes.Clear(); memberCache.Clear(); }
        public static void ClearCaches() => memberCache.Clear();

        /// <summary>Whether this pawn must not enter this cell: it is in a family zone the pawn does not belong to, and they have not been let in.</summary>
        public static bool Blocks(IntVec3 cell, Pawn pawn, bool noteWish)
        {
            if (!Enabled || pawn == null || pawn.playerSettings == null || Current.ProgramState != ProgramState.Playing) return false;
            var map = pawn.MapHeld;
            if (map == null || !cell.InBounds(map)) return false;
            var zone = map.zoneManager.ZoneAt(cell) as Zone_Family;
            if (zone == null || zone.areaId == 0) return false;
            if (pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike || pawn.Drafted || pawn.Downed || pawn.InMentalState || pawn.IsPrisoner || pawn.IsSlave) return false;
            var component = FamilyComponent.Current;
            var lineage = component == null ? null : zone.Family(component.Registry);
            if (lineage == null) return false;
            if (IsMember(component, lineage, pawn)) return false;
            if (GuestPasses.HasPassFor(pawn, zone)) return false;
            var bed = pawn.ownership?.OwnedBed;
            if (bed != null && bed.Spawned && bed.Map == map && bed.OccupiedRect().Cells.Any(zone.ContainsCell)) return false;      // they sleep here

            if (noteWish) wishes[pawn.thingIDNumber] = new DoorWish { Pawn = pawn, Zone = zone, Tick = Find.TickManager.TicksGame };
            return true;
        }

        private static bool IsMember(FamilyComponent component, Lineage lineage, Pawn pawn)
        {
            int now = Find.TickManager.TicksGame;
            long key = Key(pawn, lineage);
            if (memberCache.TryGetValue(key, out var cached) && now - cached.Key < MemberCacheTicks) return cached.Value;
            bool member = GuestPasses.IsFamilyMember(component.Registry, lineage, pawn);
            memberCache[key] = new KeyValuePair<int, bool>(now, member);
            return member;
        }

        /// <summary>The recent wishes to enter a zone, each pawn once. They are cleared as they are taken.</summary>
        public static List<DoorWish> TakeWishes(int now)
        {
            if (wishes.Count == 0) return new List<DoorWish>();
            var list = wishes.Values.Where(w => now - w.Tick <= WishLife).ToList();
            wishes.Clear();
            return list;
        }
    }

    /// <summary>The game's own test of whether a pawn may be somewhere: used by the work they would pick and by places they would go.</summary>
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.InAllowedArea))]
    internal static class Patch_FamilyDoorAllowedArea
    {
        private static void Postfix(IntVec3 c, Pawn forPawn, ref bool __result)
        {
            if (!__result) return;
            try { if (FamilyDoorRule.Blocks(c, forPawn, true)) __result = false; }
            catch (System.Exception ex) { HookSafety.Report("family door rule", ex); }
        }
    }

    /// <summary>An idle stroll does not end in someone else's home.</summary>
    [HarmonyPatch(typeof(JobGiver_Wander), "TryGiveJob")]
    internal static class Patch_FamilyDoorWander
    {
        private static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result == null) return;
            try
            {
                var target = __result.targetA;
                if (target.IsValid && FamilyDoorRule.Blocks(target.Cell, pawn, false)) __result = null;
            }
            catch (System.Exception ex) { HookSafety.Report("family door wander", ex); }
        }
    }
}
