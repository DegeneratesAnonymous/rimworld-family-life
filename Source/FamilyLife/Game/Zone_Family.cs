using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// A family's zone, painted from Architect > Zone like a growing or stockpile zone. Pawns are kept to it through an allowed area that the
    /// zone owns and keeps identical to its own cells, so the game's own area restriction does the work. Choosing a family on the zone
    /// (its button, or the Families window) restricts every family member, including those born or married in later.
    /// </summary>
    public class Zone_Family : Zone
    {
        private static readonly Color[] Colors =
        {
            new Color(0.95f, 0.65f, 0.45f), new Color(0.55f, 0.8f, 0.95f), new Color(0.75f, 0.9f, 0.55f), new Color(0.9f, 0.6f, 0.85f), new Color(0.95f, 0.85f, 0.45f)
        };

        public int areaId;                 // the allowed area this zone owns; 0 until a family is chosen

        public Zone_Family() { }

        public Zone_Family(ZoneManager zoneManager) : base("WFL_FamilyZone".Translate(), zoneManager) { }

        protected override Color NextZoneColor => Colors[Find.CurrentMap == null ? 0 : (Find.CurrentMap.zoneManager.AllZones.Count(z => z is Zone_Family) % Colors.Length)];

        public Area LinkedArea => areaId == 0 ? null : FamilyZones.FindArea(areaId);

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref areaId, "wflAreaId");
        }

        public override void AddCell(IntVec3 c)
        {
            base.AddCell(c);
            var area = LinkedArea;
            if (area != null) area[c] = true;
        }

        public override void RemoveCell(IntVec3 c)
        {
            base.RemoveCell(c);
            var area = LinkedArea;
            if (area != null) area[c] = false;
        }

        public override void Delete(bool playSound)
        {
            var component = FamilyComponent.Current;
            var lineage = component?.Registry.Lineages.FirstOrDefault(l => l.ZoneAreaId != 0 && l.ZoneAreaId == areaId);
            var area = LinkedArea;
            if (component != null && lineage != null && area != null) FamilyZones.Release(component, lineage, area);
            if (area != null) area.Delete();
            areaId = 0;
            base.Delete(playSound);
        }

        /// <summary>Gives the zone to a family: a new linked area is made, painted with the zone's cells, and everyone in the family is restricted to it.</summary>
        public void SetFamily(FamilyComponent component, Lineage lineage)
        {
            var previous = component.Registry.Lineages.FirstOrDefault(l => l.ZoneAreaId != 0 && l.ZoneAreaId == areaId);
            var area = LinkedArea;
            if (area == null)
            {
                if (!Map.areaManager.TryMakeNewAllowed(out Area_Allowed made))
                {
                    Messages.Message("WFL_ZoneTooMany".Translate(), MessageTypeDefOf.RejectInput, false);
                    return;
                }
                area = made;
                areaId = made.ID;
                foreach (var c in cells) area[c] = true;
            }
            if (previous != null && previous != lineage) FamilyZones.Release(component, previous, area);
            (area as Area_Allowed)?.SetLabel("WFL_ZoneSuffix".Translate(component.Registry.LineageDisplayName(lineage)).ToString());
            label = area.Label;
            FamilyZones.Assign(component, lineage, area);
        }

        public override string GetInspectString()
        {
            var component = FamilyComponent.Current;
            var lineage = component?.Registry.Lineages.FirstOrDefault(l => l.ZoneAreaId != 0 && l.ZoneAreaId == areaId);
            if (lineage == null) return "WFL_ZoneNone".Translate();
            var area = LinkedArea;
            return "WFL_ZoneCurrent".Translate(component.Registry.LineageDisplayName(lineage),
                area == null ? 0 : FamilyZones.RestrictedCount(component, lineage, area), component.Registry.LivingCount(lineage.Id, Presence.Colony));
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            yield return new Command_Action
            {
                defaultLabel = "WFL_ZoneChooseFamily".Translate(),
                defaultDesc = "WFL_ZoneChooseFamilyDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/AssignOwner", false) ?? BaseContent.BadTex,
                action = () =>
                {
                    var component = FamilyComponent.Current;
                    if (component == null) return;
                    var options = component.Registry.Lineages.Where(l => component.Registry.IsListedFamily(l.Id))
                        .Select(l => { var lin = l; return new FloatMenuOption(component.Registry.LineageDisplayName(lin), () => SetFamily(component, lin)); }).ToList();
                    if (options.Count == 0) options.Add(new FloatMenuOption("WFL_ZoneNoFamilies".Translate(), null));
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            };
        }
    }

    /// <summary>The Architect > Zone tool that paints family zones. Shrinking and deleting use the game's own Delete Zone tool.</summary>
    public class Designator_ZoneAdd_Family : Designator_ZoneAdd
    {
        protected override string NewZoneLabel => "WFL_FamilyZone".Translate();

        public Designator_ZoneAdd_Family()
        {
            zoneTypeToPlace = typeof(Zone_Family);
            defaultLabel = "WFL_FamilyZone".Translate();
            defaultDesc = "WFL_FamilyZoneDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Stockpile", false) ?? BaseContent.BadTex;
            hotKey = null;
        }

        protected override Zone MakeNewZone() => new Zone_Family(Find.CurrentMap.zoneManager);
    }
}
