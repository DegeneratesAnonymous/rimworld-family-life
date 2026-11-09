using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Relationships as someone outside the House sees them: their own relationships with its people, the family ties that are public, and a plain
    /// statement that the House's private relationships are not theirs to see. The feelings shown are the viewer's own side of each bond.
    /// </summary>
    public partial class Window_Families
    {
        private List<RelRow> visitorRows;
        private HashSet<int> visitorStoried;
        private List<EventRecord> visitorHistory = new List<EventRecord>();
        private int visitorRevision = -1, visitorViewer = -1, visitorHouse;

        private void DrawVisitorRelationships(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage house)
        {
            int acting = component.PerspectivePawnId;
            var subject = registry.GetPerson(acting);
            string me = HouseholdNames.Short(subject);
            var col = new Rect(0f, 0f, Mathf.Min(v.width, 720f), 0f);
            var card = FamilyKit.BeginCard(col, ref y, "r.private", "WFL_FW_PrivateRelTitle".Translate(me, view.Name));
            float cy0 = 0f;
            cy0 += FamilyKit.Label(card.View, cy0, "WFL_FW_PrivateRelBody".Translate(me), 0f, true);
            FamilyKit.EndCard(card, cy0, ref y);
            if (subject == null) return;

            if (visitorRows == null || visitorRevision != component.Revision || visitorViewer != acting || visitorHouse != house.Id)
            {
                visitorRows = RelationshipView.Build(registry, acting, house.Id, RelFilter.Everyone, RelSort.Closest, null).Where(r => view.Relevant.Contains(r.OtherId)).ToList();
                visitorStoried = new HashSet<int>();
                visitorHistory = Knowledge.HistoryWith(registry, acting, house);
                foreach (var e in visitorHistory) { visitorStoried.Add(e.A == acting ? e.B : e.A); if (e.Third != 0 && e.Third != acting) visitorStoried.Add(e.Third); }
                visitorRevision = component.Revision; visitorViewer = acting; visitorHouse = house.Id;
            }
            FamilyKit.SectionHeader(v, ref y, "WFL_FW_RelYours".Translate());
            RelRow? selected = State.SelectedRelation != 0 ? visitorRows.Cast<RelRow?>().FirstOrDefault(r => r.Value.OtherId == State.SelectedRelation) : null;
            bool side = selected.HasValue && WindowLayout.DrawerBeside(v.width);
            float listW = side ? Mathf.Min(WindowLayout.ListWidth(v.width, true), v.width - WindowLayout.DrawerWidth(v.width) - FamilyKit.Gap) : Mathf.Min(v.width, 620f);
            float listY = y, detailY = y;
            if (selected.HasValue && !side) { DrawRelationDetail(new Rect(0f, 0f, Mathf.Min(v.width, 620f), 0f), ref detailY, component, registry, subject, selected.Value); listY = detailY; }
            if (visitorRows.Count == 0) { FamilyKit.EmptyState(new Rect(0f, 0f, listW, 0f), ref listY, "WFL_FW_RelNoYours".Translate()); }
            foreach (var row in visitorRows)
            {
                var other = registry.GetPerson(row.OtherId);
                if (other == null) continue;
                var r = new Rect(0f, listY, listW, 40f);
                bool on = State.SelectedRelation == row.OtherId;
                Widgets.DrawBoxSolid(r, on ? new Color(0.22f, 0.20f, 0.12f) : new Color(0.13f, 0.13f, 0.14f));
                if (on) Widgets.DrawBoxSolid(new Rect(r.x, r.y, 3f, r.height), FamilyKit.Gold); else Widgets.DrawHighlightIfMouseover(r);
                DrawPortrait(new Rect(r.x + 6f, r.y + 2f, 28f, 36f), other, component);
                Widgets.Label(new Rect(r.x + 40f, r.y + 2f, listW * 0.4f, 20f), HouseholdNames.Short(other).Truncate(listW * 0.4f));
                Text.Font = GameFont.Tiny; GUI.color = FamilyKit.Dim;
                Widgets.Label(new Rect(r.x + 40f, r.y + 21f, listW * 0.4f, 16f), ("WFL_BondKind_" + row.Kind).Translate().ToString());
                GUI.color = Color.white;
                string word = ("WFL_FW_Feeling_" + RelationshipWords.With(registry, acting, row.OtherId, row, visitorHistory)).Translate().ToString().CapitalizeFirst();
                Widgets.Label(new Rect(r.x + 40f + listW * 0.4f, r.y + 12f, listW * 0.4f - 30f, 18f), word);
                Text.Font = GameFont.Small;
                if (visitorStoried.Contains(row.OtherId)) { var dot = new Rect(r.xMax - 18f, r.y + 14f, 10f, 10f); Widgets.DrawBoxSolid(dot, FamilyKit.Gold); TooltipHandler.TipRegion(dot, "WFL_FW_StoryIndicatorTip".Translate()); }
                if (Widgets.ButtonInvisible(r)) State.SelectedRelation = row.OtherId;
                listY += 42f;
            }
            if (selected.HasValue && side) DrawRelationDetail(new Rect(listW + FamilyKit.Gap, 0f, WindowLayout.DrawerWidth(v.width), 0f), ref detailY, component, registry, subject, selected.Value);
            y = Mathf.Max(listY, detailY) + 6f;

            FamilyKit.SectionHeader(v, ref y, "WFL_FW_RelKnown".Translate());
            var ties = Knowledge.PublicTies(registry, house);
            var pub = new Rect(0f, 0f, Mathf.Min(v.width, 620f), 0f);
            if (ties.Count == 0) y += FamilyKit.Label(pub, y, "WFL_FW_RelNoKnown".Translate(), 4f, true);
            foreach (var tie in ties.Take(12))
            {
                var kind = registry.KindOf(registry.GetPerson(tie.Key), registry.GetPerson(tie.Value));
                y += FamilyKit.Label(pub, y, FamilyText.Say(RelationPhrase.Of(registry, tie.Key, tie.Value)), 6f);
            }
        }
    }
}
