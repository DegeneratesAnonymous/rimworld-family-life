using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>How two people drawn next to each other in a unit are connected.</summary>
    public enum TreeLink : byte { None = 0, Partners = 1, Former = 2 }

    /// <summary>A link between two people in a unit who are not drawn next to each other; drawn as an arc over the ones between.</summary>
    public sealed class UnitLink
    {
        public int A;          // index into the unit's Members
        public int B;
        public TreeLink Kind;
    }

    /// <summary>
    /// One row-group of the family tree: a person, a couple, or a chain of partners (an earlier partner, the person, and their
    /// present partner) drawn side by side. Children hang from the point between their two parents.
    /// </summary>
    public sealed class TreeUnit
    {
        public List<int> Members = new List<int>();     // in the order they are drawn, left to right
        public List<TreeLink> Links = new List<TreeLink>();   // Links[i] joins Members[i] and Members[i + 1]
        public List<UnitLink> Arcs = new List<UnitLink>();    // links between people who are not next to each other
        public int Generation;                          // 0 is the oldest generation shown
        public float X;                                 // left edge, in person-widths
        public float Width;                             // width of the whole branch below this unit, in person-widths
        public int ParentUnit = -1;                     // index into Units, -1 for a root
        public float ParentAnchorLocal;                 // where on the parent unit this unit hangs from (person-widths from its left edge)
        public int ChildMember;                         // which member of this unit is the child of that parent unit; the line comes down to them, not to their partner
        public List<int> ChildUnits = new List<int>();

        public float Size => Members.Count;
        public float Center => X + Size / 2f;
        public float AnchorX(TreeUnit parent) => parent.X + ParentAnchorLocal;
    }

    /// <summary>A parent who is drawn elsewhere than the unit the child hangs from (the parents were never recorded as partners).</summary>
    public sealed class CoParentLink
    {
        public int ChildUnit;
        public int ParentPerson;
    }

    public sealed class FamilyTree
    {
        public List<TreeUnit> Units = new List<TreeUnit>();
        public List<CoParentLink> CoParentLinks = new List<CoParentLink>();
        public int Generations;
        public float TotalWidth;
        public int PeopleShown;
    }

    public static class TreeTuning
    {
        public const int MaxPeople = 80;
        public const float SiblingGap = 0.25f;     // space between neighbouring branches, in person-widths
        public const float RootGap = 0.75f;
        public const int MaxUnitSize = 5;
    }

    public sealed partial class FamilyRegistry
    {
        private bool LinkedAsPartners(PersonRecord a, PersonRecord b) => a.HasPartner(b.Id) || b.HasPartner(a.Id);
        private bool LinkedAsFormer(PersonRecord a, PersonRecord b) => a.ExIds.Contains(b.Id) || b.ExIds.Contains(a.Id);

        /// <summary>
        /// Whether a lineage is a family worth listing: at least two people live in the colony, and the family is more than one person
        /// and their spouse. A founder whose only relative is the partner they married would otherwise be listed as a second "family"
        /// holding the same couple that is already part of the partner's own family.
        /// </summary>
        public bool IsListedFamily(int lineageId)
        {
            EnsureIndexes();
            if (GetLineage(lineageId) == null) return false;
            if((GetLineage(lineageId).ParentHouseId>0 || Persons.Any(p=>p.SocialFamilyId==lineageId)) && LivingCount(lineageId,Presence.Colony)>0) return true;
            if (LivingCount(lineageId, Presence.Colony) < 2) return false;
            int byDescent = 0;
            foreach (var p in Persons)
                if (p.PrimaryLineageId == lineageId && ++byDescent >= 2) return true;
            return false;
        }

        /// <summary>
        /// The family to open for this person: their own if it is listed, otherwise one they married into that is, otherwise their own.
        /// Someone whose own family is only themselves and a spouse is shown with the family the couple actually belongs to.
        /// </summary>
        public int FamilyToShow(PersonRecord person)
        {
            EnsureIndexes();
            if (person == null) return 0;
            int own = LineageIdFor(person);
            if(person.SocialFamilyId!=0) return own;
            if (own != 0 && IsListedFamily(own)) return own;
            foreach (var lineage in Lineages)
                if (lineage.HasAffiliate(person.Id) && IsListedFamily(lineage.Id)) return lineage.Id;
            return own;
        }

        /// <summary>
        /// The people who became this person's step-parents: the present partners of their parents who are not parents of theirs.
        /// </summary>
        public List<int> StepParentsOf(int personId)
        {
            EnsureIndexes();
            var result = new SortedSet<int>();
            if (!personById.TryGetValue(personId, out var person)) return new List<int>();
            foreach (var parentId in person.ParentIds)
            {
                if (!personById.TryGetValue(parentId, out var parent)) continue;
                foreach (var partnerId in parent.PartnerIds())
                    if (partnerId != personId && !person.ParentIds.Contains(partnerId) && personById.ContainsKey(partnerId)) result.Add(partnerId);
            }
            return result.ToList();
        }

        /// <summary>Children of this person's present partners who are not the person's own.</summary>
        public List<int> StepChildrenOf(int personId)
        {
            EnsureIndexes();
            var result = new SortedSet<int>();
            if (!personById.TryGetValue(personId, out var person)) return new List<int>();
            foreach (var partnerId in person.PartnerIds())
                foreach (var childId in ChildrenOf(partnerId))
                    if (childId != personId && !person.ParentIds.Contains(childId) && !ChildrenOf(personId).Contains(childId)) result.Add(childId);
            return result.ToList();
        }

        /// <summary>
        /// Lays out a lineage as a family tree: the people who belong to it, both of their parents, their partners and former partners,
        /// grouped into units and placed by generation with every unit centred above its children. A child hangs from the point
        /// between its own two parents, so a second marriage reads as a chain: an earlier partner, the person, the new partner, each
        /// with their own children. Nothing here knows about drawing; positions are in person-widths.
        /// </summary>
        public FamilyTree BuildTree(int lineageId)
        {
            EnsureIndexes();
            var tree = new FamilyTree();
            var lineage = GetLineage(lineageId);
            if (lineage == null) return tree;

            // Who is shown: the family itself, anyone who married in or was recognised...
            var shown = new HashSet<int>();
            foreach (var p in Persons)
                if (p.PrimaryLineageId == lineageId || lineage.HasAffiliate(p.Id)) shown.Add(p.Id);
            // ...both parents of everyone who belongs by descent (so a parent who left still appears)...
            foreach (var id in shown.ToList())
            {
                var p = personById[id];
                if (p.PrimaryLineageId != lineageId) continue;
                foreach (var parent in p.ParentIds) if (personById.ContainsKey(parent)) shown.Add(parent);
            }
            // ...and the partners and former partners of all of them.
            foreach (var id in shown.ToList())
            {
                var p = personById[id];
                foreach (var other in p.PartnerIds().Concat(p.ExIds)) if (personById.ContainsKey(other)) shown.Add(other);
            }

            // ...and the children of those added, so half-siblings and step-children are drawn with the parent they belong to.
            foreach (var id in shown.ToList())
                foreach (var child in ChildrenOf(id)) if (personById.ContainsKey(child)) shown.Add(child);

            // A very old family would fill the screen with the dead; the oldest dead with nothing below them are dropped first.
            if (shown.Count > TreeTuning.MaxPeople)
            {
                var droppable = shown.Select(id => personById[id])
                    .Where(p => p.Presence == Presence.Dead && !ChildrenOf(p.Id).Any(c => shown.Contains(c)))
                    .OrderBy(p => p.DeathTick < 0 ? int.MaxValue : p.DeathTick).ThenBy(p => p.Id).ToList();
                foreach (var p in droppable)
                {
                    if (shown.Count <= TreeTuning.MaxPeople) break;
                    shown.Remove(p.Id);
                }
            }
            if (shown.Count == 0) return tree;

            // Generations: below the parents, level with partners and former partners. Repeated until stable; the cap protects against bad data.
            var gen = shown.ToDictionary(id => id, id => 0);
            for (int pass = 0; pass < 12; pass++)
            {
                bool changed = false;
                foreach (var id in shown.OrderBy(i => i))
                {
                    var p = personById[id];
                    int want = gen[id];
                    foreach (var parent in p.ParentIds.Where(shown.Contains)) want = Math.Max(want, gen[parent] + 1);
                    foreach (var other in p.PartnerIds().Concat(p.ExIds).Where(shown.Contains)) want = Math.Max(want, gen[other]);
                    if (want != gen[id]) { gen[id] = want; changed = true; }
                }
                if (!changed) break;
            }

            // Units: people linked as partners or former partners on the same level are drawn together, in a chain.
            var unitOf = new Dictionary<int, TreeUnit>();
            foreach (var id in shown.OrderBy(i => personById[i].BirthAbsTick).ThenBy(i => i))
            {
                if (unitOf.ContainsKey(id)) continue;
                var component = new List<int> { id };
                var seen = new HashSet<int> { id };
                for (int i = 0; i < component.Count && component.Count < TreeTuning.MaxUnitSize; i++)
                {
                    var current = personById[component[i]];
                    foreach (var other in shown.Where(o => !seen.Contains(o) && !unitOf.ContainsKey(o) && gen[o] == gen[id]).OrderBy(o => personById[o].BirthAbsTick).ThenBy(o => o))
                    {
                        if (component.Count >= TreeTuning.MaxUnitSize) break;
                        var po = personById[other];
                        if (LinkedAsPartners(current, po) || LinkedAsFormer(current, po)) { component.Add(other); seen.Add(other); }
                    }
                }
                var unit = new TreeUnit { Generation = gen[id] };
                unit.Members = OrderMembers(component);
                for (int i = 0; i < unit.Members.Count; i++)
                    for (int j = i + 1; j < unit.Members.Count; j++)
                    {
                        var a = personById[unit.Members[i]];
                        var b = personById[unit.Members[j]];
                        var kind = LinkedAsPartners(a, b) ? TreeLink.Partners : (LinkedAsFormer(a, b) ? TreeLink.Former : TreeLink.None);
                        if (j == i + 1) unit.Links.Add(kind);
                        else if (kind != TreeLink.None) unit.Arcs.Add(new UnitLink { A = i, B = j, Kind = kind });
                    }
                foreach (var m in unit.Members) unitOf[m] = unit;
                tree.Units.Add(unit);
            }

            // Branches: a unit hangs below the unit of one of its members' parents, at the point between its parents there.
            foreach (var unit in tree.Units)
            {
                TreeUnit parentUnit = null;
                int childMember = 0;
                foreach (var member in unit.Members)
                {
                    foreach (var parent in personById[member].ParentIds.Where(shown.Contains).OrderBy(i => i))
                    {
                        var candidate = unitOf[parent];
                        if (candidate != unit && candidate.Generation < unit.Generation) { parentUnit = candidate; childMember = member; break; }
                    }
                    if (parentUnit != null) break;
                }
                if (parentUnit == null) continue;
                unit.ChildMember = Math.Max(0, unit.Members.IndexOf(childMember));
                int unitIndex = tree.Units.IndexOf(unit);
                unit.ParentUnit = tree.Units.IndexOf(parentUnit);
                parentUnit.ChildUnits.Add(unitIndex);

                var parents = unit.Members.SelectMany(m => personById[m].ParentIds).Where(shown.Contains).Distinct().ToList();
                var inside = parents.Where(p => parentUnit.Members.Contains(p)).ToList();
                unit.ParentAnchorLocal = inside.Count == 0 ? parentUnit.Size / 2f
                    : inside.Average(p => parentUnit.Members.IndexOf(p) + 0.5f);
                foreach (var outside in parents.Where(p => !parentUnit.Members.Contains(p) && unitOf[p].Generation < unit.Generation))
                    tree.CoParentLinks.Add(new CoParentLink { ChildUnit = unitIndex, ParentPerson = outside });
            }
            foreach (var unit in tree.Units)
                unit.ChildUnits = unit.ChildUnits
                    .OrderBy(i => tree.Units[i].ParentAnchorLocal)
                    .ThenBy(i => personById[tree.Units[i].Members[0]].BirthAbsTick)
                    .ThenBy(i => tree.Units[i].Members[0]).ToList();

            // Positions: leaves left to right, every parent centred over its children.
            float x = 0f;
            var roots = tree.Units.Where(u => u.ParentUnit < 0)
                .OrderBy(u => u.Generation).ThenBy(u => personById[u.Members[0]].BirthAbsTick).ThenBy(u => u.Members[0]).ToList();
            foreach (var root in roots)
            {
                Place(tree, root, x);
                x += root.Width + TreeTuning.RootGap;
            }

            tree.TotalWidth = tree.Units.Count == 0 ? 0f : tree.Units.Max(u => u.X + u.Size);
            tree.Generations = tree.Units.Count == 0 ? 0 : tree.Units.Max(u => u.Generation) + 1;
            tree.PeopleShown = shown.Count;
            return tree;
        }

        /// <summary>How much it matters that two people are drawn next to each other.</summary>
        private float PairWeight(int x, int y)
        {
            var a = personById[x];
            var b = personById[y];
            float w = LinkedAsPartners(a, b) ? 3f : (LinkedAsFormer(a, b) ? 1f : 0f);
            if (w > 0f && ChildrenOf(x).Any(c => ChildrenOf(y).Contains(c))) w += 2f;      // a couple with children together
            return w;
        }

        /// <summary>
        /// Orders a connected group so that the pairs who matter most (present partners, and couples with children) are next to each
        /// other, with earlier partners to the left and present partners to the right. A person with several partners cannot be next to
        /// all of them, so the rest are joined by an arc. Groups are at most five people, so every order is tried.
        /// </summary>
        private List<int> OrderMembers(List<int> component)
        {
            var baseOrder = component.OrderBy(i => personById[i].BirthAbsTick).ThenBy(i => i).ToList();
            if (baseOrder.Count <= 2) return baseOrder;
            var partnered = baseOrder.ToDictionary(i => i, i => baseOrder.Any(o => o != i && LinkedAsPartners(personById[i], personById[o])) ? 1 : 0);

            List<int> best = null;
            float bestWeight = -1f, bestSide = -1f;
            foreach (var order in Permutations(baseOrder))
            {
                float weight = 0f;
                for (int i = 0; i + 1 < order.Count; i++) weight += PairWeight(order[i], order[i + 1]);
                float side = 0f;                                         // present partners belong on the right
                for (int i = 0; i < order.Count; i++) side += i * partnered[order[i]];
                if (weight > bestWeight + 0.001f || (Math.Abs(weight - bestWeight) <= 0.001f && side > bestSide + 0.001f))
                {
                    best = order; bestWeight = weight; bestSide = side;
                }
            }
            return best ?? baseOrder;
        }

        private static IEnumerable<List<int>> Permutations(List<int> items)
        {
            var current = new List<int>();
            var used = new bool[items.Count];
            return Permute(items, used, current);
        }

        private static IEnumerable<List<int>> Permute(List<int> items, bool[] used, List<int> current)
        {
            if (current.Count == items.Count) { yield return new List<int>(current); yield break; }
            for (int i = 0; i < items.Count; i++)
            {
                if (used[i]) continue;
                used[i] = true;
                current.Add(items[i]);
                foreach (var p in Permute(items, used, current)) yield return p;
                current.RemoveAt(current.Count - 1);
                used[i] = false;
            }
        }

        private static void Place(FamilyTree tree, TreeUnit unit, float startX)
        {
            if (unit.ChildUnits.Count == 0)
            {
                unit.X = startX;
                unit.Width = unit.Size;
                return;
            }
            float cursor = 0f;
            var offsets = new List<float>();
            foreach (var index in unit.ChildUnits)
            {
                var child = tree.Units[index];
                Place(tree, child, 0f);                    // measured at zero, shifted into place below
                offsets.Add(cursor);
                cursor += child.Width + TreeTuning.SiblingGap;
            }
            float childrenWidth = cursor - TreeTuning.SiblingGap;
            float width = Math.Max(unit.Size, childrenWidth);
            float shift = startX + (width - childrenWidth) / 2f;
            for (int i = 0; i < unit.ChildUnits.Count; i++) Shift(tree, tree.Units[unit.ChildUnits[i]], shift + offsets[i]);

            var first = tree.Units[unit.ChildUnits[0]];
            var last = tree.Units[unit.ChildUnits[unit.ChildUnits.Count - 1]];
            float center = (first.Center + last.Center) / 2f;
            unit.X = Math.Max(startX, Math.Min(startX + width - unit.Size, center - unit.Size / 2f));
            unit.Width = width;
        }

        private static void Shift(FamilyTree tree, TreeUnit unit, float dx)
        {
            unit.X += dx;
            foreach (var index in unit.ChildUnits) Shift(tree, tree.Units[index], dx);
        }
    }
}
