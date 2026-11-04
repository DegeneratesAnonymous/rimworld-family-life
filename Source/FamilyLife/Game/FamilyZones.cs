using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// A family's area: one of the game's own allowed areas, assigned to everyone in the family. Nothing new is invented here: the area is a
    /// normal area (it shows under Areas and can be painted, renamed and deleted there) and the restriction is the game's own area restriction
    /// on each pawn. What the mod adds is the link from a family to an area, so that new members get it too.
    /// </summary>
    internal static class FamilyZones
    {
        public static Map HomeMap() => Find.AnyPlayerHomeMap ?? Find.CurrentMap;

        public static IEnumerable<Area> Areas(Map map) => map.areaManager.AllAreas.Where(a => a is Area_Allowed).OrderBy(a => a.Label);

        public static Area FindArea(int areaId)
        {
            foreach (var map in Find.Maps)
                foreach (var area in map.areaManager.AllAreas)
                    if (area.ID == areaId) return area;
            return null;
        }

        /// <summary>Everyone in the family who has not been given the area yet gets it. An area that was deleted ends the assignment.</summary>
        public static void Apply(FamilyComponent component)
        {
            var registry = component.Registry;
            foreach (var lineage in registry.LineagesWithZones())
            {
                var area = FindArea(lineage.ZoneAreaId);
                if (area == null) { registry.ClearZone(lineage.Id); continue; }
                var done = new List<int>();
                foreach (var id in registry.PendingZoneMembers(lineage.Id))
                {
                    var pawn = component.TryGetPawn(id);
                    if (pawn == null || pawn.Dead || pawn.playerSettings == null || pawn.Map != area.Map) continue;
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                    done.Add(id);
                }
                registry.MarkZoneApplied(lineage.Id, done);
            }
            foreach (var map in Find.Maps) IncludeWholeBeds(map);
        }

        /// <summary>
        /// A bed that is partly inside a family zone belongs to the home, so all of it is: the cells it covers and the cells beside it a
        /// sleeper stands on. A pawn cannot claim or walk to a bed that is outside its area, and a zone painted around a bed by eye often
        /// misses a corner. Cells that belong to no other zone join the zone itself; the rest join only the family's area.
        /// </summary>
        public static void IncludeWholeBeds(Map map)
        {
            var zones = map.zoneManager.AllZones.OfType<Zone_Family>().Where(z => z.areaId != 0).ToList();
            if (zones.Count == 0) return;
            foreach (var bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>().ToList())
            {
                var cells = bed.OccupiedRect().Cells.Concat(bed.InteractionCells).Distinct().Where(c => c.InBounds(map)).ToList();
                var zone = zones.FirstOrDefault(z => cells.Any(z.ContainsCell));
                if (zone == null) continue;
                var area = zone.LinkedArea;
                foreach (var c in cells)
                {
                    if (zone.ContainsCell(c) || c.Impassable(map)) continue;
                    if (map.zoneManager.ZoneAt(c) == null) zone.AddCell(c);
                    else if (area != null) area[c] = true;
                }
            }
        }

        /// <summary>
        /// A family zone is where the family rests, eats and plays, not a cage for their work. During work hours (and 'anything') the
        /// restriction is lifted so crops, mining, hauling and building get done; when the schedule moves to sleep or recreation it is put back.
        /// Only members who hold the family's area, or none, are touched, and never someone in the middle of a visit through another family's door.
        /// </summary>
        public static void ManageWorkTime(FamilyComponent component)
        {
            var settings = FamilyLifeMod.Settings;
            if (settings != null && !settings.zonesAllowWork) return;
            var registry = component.Registry;
            foreach (var lineage in registry.LineagesWithZones())
            {
                var area = FindArea(lineage.ZoneAreaId);
                if (area == null) continue;
                foreach (var person in registry.MembersOf(lineage.Id).Concat(registry.AffiliatesOf(lineage.Id)).Distinct())
                {
                    var pawn = component.TryGetPawn(person.Id);
                    if (pawn?.playerSettings == null || pawn.Dead || !pawn.Spawned || pawn.Map != area.Map || GuestPasses.IsActive(pawn)) continue;
                    var held = pawn.playerSettings.AreaRestrictionInPawnCurrentMap;
                    if (held != null && held != area) continue;                      // the player gave them something else
                    var now = pawn.timetable?.CurrentAssignment;
                    bool working = now == TimeAssignmentDefOf.Work || now == TimeAssignmentDefOf.Anything;
                    if (working && held == area) pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                    else if (!working && held == null && now != null) pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
                }
            }
        }

        public static void Assign(FamilyComponent component, Lineage lineage, Area area)
        {
            var previous = lineage.ZoneAreaId == 0 ? null : FindArea(lineage.ZoneAreaId);
            if (previous != null && previous != area) ReleasePawns(component, lineage, previous);
            component.Registry.SetZone(lineage.Id, area.ID);
            Apply(component);
        }

        public static void Release(FamilyComponent component, Lineage lineage, Area area)
        {
            ReleasePawns(component, lineage, area);
            component.Registry.ClearZone(lineage.Id);
        }

        /// <summary>Family members who are restricted to this area go back to no restriction; anyone set to something else is left alone.</summary>
        private static void ReleasePawns(FamilyComponent component, Lineage lineage, Area area)
        {
            var registry = component.Registry;
            foreach (var person in registry.MembersOf(lineage.Id).Concat(registry.AffiliatesOf(lineage.Id)).Distinct())
            {
                var pawn = component.TryGetPawn(person.Id);
                if (pawn?.playerSettings != null && pawn.playerSettings.AreaRestrictionInPawnCurrentMap == area)
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
            }
        }

        public static int RestrictedCount(FamilyComponent component, Lineage lineage, Area area)
        {
            var registry = component.Registry;
            int n = 0;
            foreach (var person in registry.MembersOf(lineage.Id).Concat(registry.AffiliatesOf(lineage.Id)).Distinct().Where(p => p.InColony))
            {
                var pawn = component.TryGetPawn(person.Id);
                if (pawn?.playerSettings != null && pawn.playerSettings.AreaRestrictionInPawnCurrentMap == area) n++;
            }
            return n;
        }

        /// <summary>Selects the Architect > Zone family-zone tool, so a zone can be painted on the map straight away.</summary>
        public static void StartPainting()
        {
            Find.DesignatorManager.Select(new Designator_ZoneAdd_Family());
        }

        /// <summary>The family zones on the map that belong to this family.</summary>
        public static IEnumerable<Zone_Family> ZonesOf(Lineage lineage)
        {
            foreach (var map in Find.Maps)
                foreach (var zone in map.zoneManager.AllZones)
                    if (zone is Zone_Family fz && fz.areaId != 0 && fz.areaId == lineage.ZoneAreaId) yield return fz;
        }
    }
}
