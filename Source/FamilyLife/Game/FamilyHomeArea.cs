using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// "Family zone" in the Restrict tab: one choice among the allowed areas, the same for everyone. Whoever is set to it stays in their own
    /// home: a family member in their family's zone, anyone else in their bedroom. The area itself has no cells; the game is told the pawn's real
    /// area whenever it asks where they may go (see <see cref="Patch_FamilyHomeEffectiveArea"/>), so it follows people as they move and never
    /// needs painting. The areas the family zones use internally are kept out of the list.
    /// </summary>
    public class Area_FamilyHome : Area_Allowed
    {
        public Area_FamilyHome() { }
        public Area_FamilyHome(AreaManager areaManager) : base(areaManager) { }

        public override bool Mutable => false;
        public override string Label => "WFL_FamilyHomeArea".Translate();
        public override Color Color => new Color(0.95f, 0.8f, 0.4f);
    }

    internal static class FamilyHome
    {
        private sealed class Cached { public Area Area; public int Tick, BedId, RoomId, Cells; }
        private static readonly Dictionary<int, Cached> pawnAreas = new Dictionary<int, Cached>();
        private static readonly Dictionary<int, HashSet<int>> linkedAreas = new Dictionary<int, HashSet<int>>();    // map id -> area ids a family zone uses
        private static int linkedTick = -1000;
        private const int CacheTicks = 250;

        public static void Reset() { pawnAreas.Clear(); linkedAreas.Clear(); linkedTick = -1000; }

        /// <summary>Makes sure each home map offers the "Family zone" choice, and moves anyone who was restricted to a family zone's own area onto it.</summary>
        public static void Ensure(FamilyComponent component)
        {
            foreach (var map in Find.Maps)
            {
                if (!map.IsPlayerHome) continue;
                var home = map.areaManager.AllAreas.OfType<Area_FamilyHome>().FirstOrDefault();
                if (home == null)
                {
                    home = new Area_FamilyHome(map.areaManager);
                    map.areaManager.AllAreas.Add(home);
                }
                var linked = LinkedAreaIds(map, true);
                if (linked.Count == 0) continue;
                foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    var held = pawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
                    if (held != null && !(held is Area_FamilyHome) && linked.Contains(held.ID)) pawn.playerSettings.AreaRestrictionInPawnCurrentMap = home;
                }
            }
        }

        /// <summary>The ids of the areas family zones keep for themselves.</summary>
        public static HashSet<int> LinkedAreaIds(Map map, bool force = false)
        {
            int now = Find.TickManager?.TicksGame ?? 0;
            if (force || now - linkedTick > 60 || now < linkedTick) { linkedAreas.Clear(); linkedTick = now; }
            if (!linkedAreas.TryGetValue(map.uniqueID, out var set))
            {
                set = new HashSet<int>(map.zoneManager.AllZones.OfType<Zone_Family>().Where(z => z.areaId != 0).Select(z => z.areaId));
                linkedAreas[map.uniqueID] = set;
            }
            return set;
        }

        /// <summary>Where this pawn stays when set to the family zone: their family's zone, else their bedroom, else nowhere in particular.</summary>
        public static Area AreaFor(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned) return null;
            var map = pawn.Map;
            var component = FamilyComponent.Current;
            if (component != null)
            {
                foreach (var lineage in component.Registry.LineagesWithZones())
                {
                    if (lineage.ZoneMapId != map.uniqueID || !GuestPasses.IsFamilyMember(component.Registry, lineage, pawn)) continue;
                    var area = FamilyZones.AreaFor(lineage);
                    if (area != null && area.Map == map && area.TrueCount > 0) return area;
                }
            }
            return Bedroom(pawn, map);
        }

        private static Area Bedroom(Pawn pawn, Map map)
        {
            var bed = pawn.ownership?.OwnedBed;
            if (bed == null || !bed.Spawned || bed.Map != map) return null;
            var room = bed.GetRoom();
            if (room == null || room.PsychologicallyOutdoors || room.CellCount > 600) return null;
            int now = Find.TickManager.TicksGame;
            if (pawnAreas.TryGetValue(pawn.thingIDNumber, out var c) && c.Area.Map == map && c.BedId == bed.thingIDNumber && c.RoomId == room.ID && c.Cells == room.CellCount && now - c.Tick < CacheTicks)
                return c.Area;
            var area = new Area_Allowed(map.areaManager);
            foreach (var cell in room.Cells) area[cell] = true;
            // A bed in a doorway or open room still has somewhere to stand: take the cells beside it too.
            foreach (var cell in bed.OccupiedRect().Cells.Concat(bed.InteractionCells)) if (cell.InBounds(map)) area[cell] = true;
            pawnAreas[pawn.thingIDNumber] = new Cached { Area = area, Tick = now, BedId = bed.thingIDNumber, RoomId = room.ID, Cells = room.CellCount };
            return area;
        }
    }

    /// <summary>Whoever is set to "Family zone" is given their real home as the area they are held to.</summary>
    [HarmonyPatch(typeof(Pawn_PlayerSettings), "get_EffectiveAreaRestrictionInPawnCurrentMap")]
    internal static class Patch_FamilyHomeEffectiveArea
    {
        private static void Postfix(ref Area __result, Pawn ___pawn)
        {
            if (!(__result is Area_FamilyHome)) return;
            try { __result = FamilyHome.AreaFor(___pawn); }
            catch (System.Exception ex) { __result = null; HookSafety.Report("family zone area", ex); }
        }
    }

    /// <summary>The areas a family zone uses for itself are not choices in the Restrict tab, and cannot be edited or deleted as areas.</summary>
    [HarmonyPatch(typeof(Area_Allowed), nameof(Area_Allowed.AssignableAsAllowed))]
    internal static class Patch_FamilyHomeHideAssignable
    {
        private static void Postfix(Area_Allowed __instance, ref bool __result)
        {
            if (!__result || __instance.Map == null || __instance is Area_FamilyHome) return;
            try { if (FamilyHome.LinkedAreaIds(__instance.Map).Contains(__instance.ID)) __result = false; }
            catch (System.Exception ex) { HookSafety.Report("family zone area list", ex); }
        }
    }

    [HarmonyPatch(typeof(Area_Allowed), "get_Mutable")]
    internal static class Patch_FamilyHomeHideMutable
    {
        private static void Postfix(Area_Allowed __instance, ref bool __result)
        {
            if (!__result || __instance.Map == null || __instance is Area_FamilyHome) return;
            try { if (FamilyHome.LinkedAreaIds(__instance.Map).Contains(__instance.ID)) __result = false; }
            catch (System.Exception ex) { HookSafety.Report("family zone area edit", ex); }
        }
    }
}
