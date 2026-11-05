using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>Preferred family homes, linked to map-scoped vanilla areas. Player movement limits remain independent.</summary>
    internal static class FamilyZones
    {
        public static Map HomeMap() => Find.AnyPlayerHomeMap ?? Find.CurrentMap;

        public static IEnumerable<Area> Areas(Map map) => map.areaManager.AllAreas.Where(a => a is Area_Allowed).OrderBy(a => a.Label);

        public static bool EnforcesAreas => false;
        public static Area FindArea(int areaId, int mapId = -1)
        {
            return FamilyAreaPolicy.Resolve(Find.Maps.SelectMany(m => m.areaManager.AllAreas), areaId, mapId, a => a.ID, a => a.Map.uniqueID);
        }
        public static Area AreaFor(Lineage lineage) => lineage == null ? null : FindArea(lineage.ZoneAreaId, lineage.ZoneMapId);

        /// <summary>Everyone in the family who has not been given the area yet gets it. An area that was deleted ends the assignment.</summary>
        public static void Apply(FamilyComponent component)
        {
            var registry = component.Registry;
            foreach (var lineage in registry.LineagesWithZones())
            {
                if (lineage.ZoneMapId < 0)
                {
                    var candidates = Find.Maps.SelectMany(m => m.zoneManager.AllZones.OfType<Zone_Family>())
                        .Where(z => z.areaId == lineage.ZoneAreaId).ToList();
                    if (candidates.Count == 1) lineage.ZoneMapId = candidates[0].Map.uniqueID;
                }
                var area = AreaFor(lineage);
                if (area == null) continue; // ambiguous legacy assignments are retained for explicit reassignment
                // One-time migration: only release the matching restriction the old mod recorded applying.
                foreach (int id in lineage.ZoneApplied.ToList())
                {
                    var pawn = component.TryGetPawn(id);
                    if (pawn?.playerSettings != null && pawn.Map == area.Map && pawn.playerSettings.AreaRestrictionInPawnCurrentMap == area)
                        pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                }
                lineage.ZoneApplied.Clear();
            }
            foreach (var map in Find.Maps) IncludeWholeBeds(map);
            FamilyHome.Ensure(component);
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

        /// <summary>Compatibility entry point. Preferred homes never change movement restrictions.</summary>
        public static void ManageWorkTime(FamilyComponent component) { } // Preferred homes never edit player movement schedules.

        public static void Assign(FamilyComponent component, Lineage lineage, Area area)
        {
            var previous = lineage.ZoneAreaId == 0 ? null : AreaFor(lineage);
            if (previous != null && previous != area) ReleasePawns(component, lineage, previous);
            component.Registry.SetZone(lineage.Id, area.ID, area.Map.uniqueID);
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
            foreach (var id in lineage.ZoneApplied.ToArray())
            {
                var pawn = component.TryGetPawn(id);
                if (pawn?.playerSettings != null && pawn.Map == area.Map && pawn.playerSettings.AreaRestrictionInPawnCurrentMap == area)
                    pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
            }
        }

        public static int RestrictedCount(FamilyComponent component, Lineage lineage, Area area)
        {
            var registry = component.Registry;
            int n = 0;
            foreach (var id in component.ZoneMemberIds(lineage))
            {
                if (registry.GetPerson(id)?.InColony != true) continue;
                var pawn = component.TryGetPawn(id);
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
                    if (zone is Zone_Family fz && fz.areaId != 0 && fz.areaId == lineage.ZoneAreaId && fz.Map.uniqueID == lineage.ZoneMapId) yield return fz;
        }
    }
}
