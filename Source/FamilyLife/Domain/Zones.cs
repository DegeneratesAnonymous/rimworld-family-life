using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public sealed partial class Lineage
    {
        public int ZoneAreaId;                                   // the game's allowed area assigned to this family; 0 = none
        public List<int> ZoneApplied = new List<int>();          // who has already been given it, so a person who later chose another area is not overruled
    }

    public sealed partial class FamilyRegistry
    {
        /// <summary>Assigns a family an allowed area (by the game's own area id). Everyone in the family is given it again.</summary>
        public void SetZone(int lineageId, int areaId)
        {
            var lineage = GetLineage(lineageId);
            if (lineage == null) return;
            lineage.ZoneAreaId = areaId < 0 ? 0 : areaId;
            lineage.ZoneApplied = new List<int>();
        }

        public void ClearZone(int lineageId) => SetZone(lineageId, 0);

        /// <summary>
        /// The people in the colony who belong to the family and have not yet been given its area. A new baby, or someone who marries
        /// in, shows up here; someone who was given it and later chose another area for themselves does not.
        /// </summary>
        public List<int> PendingZoneMembers(int lineageId)
        {
            EnsureIndexes();
            var lineage = GetLineage(lineageId);
            if (lineage == null || lineage.ZoneAreaId == 0) return new List<int>();
            lineage.ZoneApplied = lineage.ZoneApplied ?? new List<int>();
            return Persons.Where(p => p.InColony && (p.PrimaryLineageId == lineageId || lineage.HasAffiliate(p.Id)) && !lineage.ZoneApplied.Contains(p.Id))
                .Select(p => p.Id).OrderBy(i => i).ToList();
        }

        public void MarkZoneApplied(int lineageId, IEnumerable<int> personIds)
        {
            var lineage = GetLineage(lineageId);
            if (lineage == null) return;
            lineage.ZoneApplied = lineage.ZoneApplied ?? new List<int>();
            foreach (var id in personIds) if (!lineage.ZoneApplied.Contains(id)) lineage.ZoneApplied.Add(id);
        }

        /// <summary>The families that have an area assigned.</summary>
        public List<Lineage> LineagesWithZones() => Lineages.Where(l => l.ZoneAreaId != 0).ToList();
    }
}
