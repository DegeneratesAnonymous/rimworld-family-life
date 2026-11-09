using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The People tab: everyone in the family as a portrait list grouped by household, or as the family tree. Choosing a person opens a detail panel
    /// beside the list; the window stays open. Genealogy lives here. How people feel about each other is the Relationships tab, never drawn on the tree.
    /// </summary>
    public partial class Window_Families
    {
        private const float NodeW0 = 148f, NodeH0 = 58f, GapX0 = 18f, RowGap0 = 38f, PortraitW0 = 40f, PortraitH0 = 48f;
        private const float RowHeightList = 54f;

        private float treeZoom = 1f;
        private Vector2 treeScroll;
        private bool treeRecenter;
        private bool peopleShowGone;
        private FamilyTree cachedTree;
        private int cachedTreeLineage;
        private int cachedTreeRevision = -1;
        private FamilyRegistry cachedTreeRegistry;
        private int lastClickedId;
        private float lastClickTime;

        private static bool portraitFailureReported;

        // A portrait the game has not drawn before is rendered on the spot, so a big tree opened all at once would stall for a moment.
        // A few new ones are drawn per frame and the rest show a placeholder until their turn.
        private const int NewPortraitsPerFrame = 4;
        private static int portraitBudget;
        private static readonly HashSet<int> portraitsDrawn = new HashSet<int>();

        private static Color HouseholdColor(int householdId)
        {
            return Color.HSVToRGB(((householdId * 0.381966f) % 1f + 1f) % 1f, 0.55f, 0.9f);
        }

        // ------------------------------------------------------------------ the tab

        private void DrawPeople(Rect viewRect, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            var model = People(component);

            // View switch and the tree's own controls.
            var labels = new[] { "WFL_FW_People_Portraits".Translate().ToString(), "WFL_FW_People_Tree".Translate().ToString() };
            int pick = FamilyKit.ChipRow(new Rect(0f, 0f, 300f, 0f), ref y, labels, (int)State.PeopleView, null, 26f);
            if (pick >= 0) State.PeopleView = (PeopleView)pick;
            float controlsY = y - 32f;
            float cx = viewRect.width - 4f;
            if (State.PeopleView == PeopleView.Tree)
            {
                cx -= 70f; if (FamilyKit.Action(new Rect(cx, controlsY, 66f, 26f), "WFL_FW_Recenter".Translate(), true, null, "WFL_FW_RecenterTip".Translate())) { treeZoom = 1f; treeRecenter = true; }
                cx -= 32f; if (FamilyKit.Action(new Rect(cx, controlsY, 28f, 26f), "+", treeZoom < 1.6f, null, "WFL_FW_ZoomIn".Translate())) treeZoom = Mathf.Min(1.6f, treeZoom + 0.2f);
                cx -= 32f; if (FamilyKit.Action(new Rect(cx, controlsY, 28f, 26f), "-", treeZoom > 0.6f, null, "WFL_FW_ZoomOut".Translate())) treeZoom = Mathf.Max(0.6f, treeZoom - 0.2f);
            }
            else
            {
                bool gone = peopleShowGone;
                cx -= 190f;
                if (FamilyKit.ToggleChip(new Rect(cx, controlsY, 186f, 26f), "WFL_FW_ShowGone".Translate(), gone, "WFL_FW_ShowGoneTip".Translate())) peopleShowGone = !peopleShowGone;
            }

            var selected = State.SelectedPerson != 0 && model.ById.ContainsKey(State.SelectedPerson) ? model.ById[State.SelectedPerson] : null;
            if (State.SelectedPerson != 0 && selected == null && registry.GetPerson(State.SelectedPerson) == null) State.SelectedPerson = 0;

            bool side = selected != null && viewRect.width >= 760f;
            float drawerW = WindowLayout.DrawerWidth(viewRect.width);
            float listW = side ? Mathf.Min(viewRect.width - drawerW - FamilyKit.Gap, 760f) : viewRect.width;
            var listView = new Rect(0f, 0f, listW, viewRect.height);

            float listY = y, detailY = y;
            if (selected != null && !side)
            {
                var col = new Rect(0f, 0f, viewRect.width, 0f);
                DrawPersonDetail(col, ref detailY, component, registry, selected);
                listY = detailY;
            }
            if (State.PeopleView == PeopleView.Tree) DrawTreeSection(listView, ref listY, component, registry, house);
            else DrawPortraitList(listView, ref listY, component, registry, model);

            if (selected != null && side)
            {
                var col = new Rect(listW + FamilyKit.Gap, 0f, drawerW, 0f);
                DrawPersonDetail(col, ref detailY, component, registry, selected);
            }
            y = Mathf.Max(listY, detailY);
        }

        // ------------------------------------------------------------------ portrait list

        private void DrawPortraitList(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry, PeopleModel model)
        {
            if (model.Rows.Count == 0) { FamilyKit.EmptyState(v, ref y, "WFL_NoHouseholds".Translate()); return; }
            DrawCategory(v, ref y, model, PersonCategory.Member, "WFL_FW_Group_Members", registry);
            DrawCategory(v, ref y, model, PersonCategory.Resident, "WFL_FW_Group_Residents", registry);
            DrawCategory(v, ref y, model, PersonCategory.Associate, "WFL_FW_Group_Associates", registry);
            int gone = model.Rows.Count(r => r.Category == PersonCategory.Elsewhere);
            if (gone > 0)
            {
                if (peopleShowGone) DrawCategory(v, ref y, model, PersonCategory.Elsewhere, "WFL_FW_Group_Elsewhere", registry);
                else { y += 4f; FamilyKit.EmptyState(v, ref y, "WFL_FW_GoneHidden".Translate(gone)); }
            }
        }

        private void DrawCategory(Rect v, ref float y, PeopleModel model, PersonCategory category, string headerKey, FamilyRegistry registry)
        {
            var rows = model.Rows.Where(r => r.Category == category).ToList();
            if (rows.Count == 0) return;
            FamilyKit.SectionHeader(v, ref y, headerKey.Translate(rows.Count));
            const float minW = 290f, maxW = 360f;
            int cols = Mathf.Max(1, Mathf.FloorToInt((v.width + WindowLayout.Gap) / (minW + WindowLayout.Gap)));
            float w = Mathf.Min(maxW, (v.width - WindowLayout.Gap * (cols - 1)) / cols);
            float rowY = y;
            for (int i = 0; i < rows.Count; i++)
            {
                int c = i % cols;
                if (c == 0 && i > 0) rowY += CardH + WindowLayout.Gap;
                DrawPersonCard(new Rect(c * (w + WindowLayout.Gap), rowY, w, CardH), rows[i], registry);
            }
            y = rowY + CardH + WindowLayout.Gap;
        }

        private const float CardH = 118f;

        /// <summary>One person as a card: a portrait, who they are to the House and to the viewer, what they are doing and the part of the story they are in.</summary>
        private void DrawPersonCard(Rect rect, PersonRow row, FamilyRegistry registry)
        {
            bool selected = State.SelectedPerson == row.Person.Id;
            Widgets.DrawBoxSolid(rect, selected ? new Color(0.22f, 0.20f, 0.12f) : new Color(0.13f, 0.13f, 0.14f));
            if (selected) Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), FamilyKit.Gold); else Widgets.DrawHighlightIfMouseover(rect);
            if (row.Person.InColony && row.HouseholdId != 0) Widgets.DrawBoxSolid(new Rect(rect.x + 3f, rect.y, 4f, rect.height), HouseholdColor(row.HouseholdId));
            DrawPortrait(new Rect(rect.x + 12f, rect.y + 8f, 56f, 68f), row.Person, FamilyComponent.Current);
            float roleW = Mathf.Min(row.Roles.Count, 2) * 24f;
            float tx = rect.x + 76f, tw = rect.width - 76f - 6f;
            bool dim = !row.Person.InColony;
            GUI.color = dim ? new Color(0.65f, 0.65f, 0.65f) : Color.white;
            Widgets.Label(new Rect(tx, rect.y + 3f, tw - roleW, 22f), HouseholdNames.Short(row.Person).Truncate(tw - roleW));
            Text.Font = GameFont.Tiny; GUI.color = FamilyKit.Dim;
            string houseLine = row.Age + (row.HouseNote.Length > 0 ? "" : row.Category == PersonCategory.Member ? " - " + "WFL_FW_CardMember".Translate(view.Name) : "");
            Widgets.Label(new Rect(tx, rect.y + 25f, tw, 16f), houseLine.Truncate(tw));
            string rel = row.Relation.Length > 0 ? row.Relation : "";
            string hh = row.Household.Length > 0 ? row.Household : "";
            Widgets.Label(new Rect(tx, rect.y + 41f, tw, 16f), (rel.Length > 0 && hh.Length > 0 ? rel + " - " + hh : rel + hh).Truncate(tw));
            Widgets.Label(new Rect(tx, rect.y + 57f, tw, 16f), (row.HouseNote.Length > 0 ? row.HouseNote : row.Status).Truncate(tw));
            if (row.Concern.Length > 0) { GUI.color = FamilyKit.Warn; Widgets.Label(new Rect(tx, rect.y + 73f, tw, 16f), row.Concern.Truncate(tw)); }
            else if (row.Thread.Length > 0) { GUI.color = FamilyKit.Trust; Widgets.Label(new Rect(tx, rect.y + 73f, tw, 16f), row.Thread.Truncate(tw)); }
            GUI.color = Color.white; Text.Font = GameFont.Small;
            for (int i = 0; i < row.Roles.Count && i < 2; i++)
            {
                var icon = new Rect(rect.xMax - roleW - 4f + i * 24f, rect.y + 4f, 20f, 20f);
                RoleIcons.Draw(icon, row.Roles[i].Role, true);
                TooltipHandler.TipRegion(icon, RoleBar.TitleOf(registry, row.Roles[i]).CapitalizeFirst() + "\n" + RoleIcons.Description(row.Roles[i].Role));
            }
            var shown = row;
            TooltipHandler.TipRegion(rect, new TipSignal(() => shown.Person.FullName + " (" + FamilyUi.StatusText(shown.Person) + ")\n" + "WFL_FW_ClickForDetails".Translate(), 7200000 + row.Person.Id));
            if (Widgets.ButtonInvisible(rect)) OnPersonClicked(row.Person);
        }

        private void DrawPersonRow(Rect v, ref float y, PersonRow row, FamilyRegistry registry)
        {
            var rect = new Rect(0f, y, v.width, RowHeightList - 2f);
            bool selected = State.SelectedPerson == row.Person.Id;
            Widgets.DrawBoxSolid(rect, selected ? new Color(0.22f, 0.20f, 0.12f) : new Color(0.13f, 0.13f, 0.14f));
            if (selected) Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), FamilyKit.Gold);
            else Widgets.DrawHighlightIfMouseover(rect);
            if (row.Person.InColony && row.HouseholdId != 0) Widgets.DrawBoxSolid(new Rect(rect.x + 3f, rect.y, 4f, rect.height), HouseholdColor(row.HouseholdId));
            DrawPortrait(new Rect(rect.x + 12f, rect.y + 3f, 38f, 46f), row.Person, FamilyComponent.Current);

            float roleW = Mathf.Min(row.Roles.Count, 3) * 24f;
            float textX = rect.x + 58f, textW = rect.width - 58f - roleW - 8f;
            bool dim = !row.Person.InColony;
            GUI.color = dim ? new Color(0.65f, 0.65f, 0.65f) : Color.white;
            Widgets.Label(new Rect(textX, rect.y + 2f, textW * 0.55f, 22f), row.Person.FullName.Truncate(textW * 0.55f));
            Text.Font = GameFont.Tiny;
            GUI.color = FamilyKit.Dim;
            Widgets.Label(new Rect(textX + textW * 0.55f, rect.y + 4f, textW * 0.45f, 18f), row.Relation.Truncate(textW * 0.45f));
            string line = row.Age + (row.Household.Length > 0 ? " - " + row.Household : "") + " - " + row.Status;
            Widgets.Label(new Rect(textX, rect.y + 22f, textW, 16f), line.Truncate(textW));
            if (row.Concern.Length > 0) { GUI.color = FamilyKit.Warn; Widgets.Label(new Rect(textX, rect.y + 36f, textW, 16f), row.Concern.Truncate(textW)); }
            else if (row.HouseNote.Length > 0 || row.Thread.Length > 0) { GUI.color = row.HouseNote.Length > 0 ? new Color(0.78f, 0.7f, 0.5f) : FamilyKit.Trust; Widgets.Label(new Rect(textX, rect.y + 36f, textW, 16f), (row.HouseNote.Length > 0 ? row.HouseNote : row.Thread).Truncate(textW)); }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Public role emblems.
            for (int i = 0; i < row.Roles.Count && i < 3; i++)
            {
                var icon = new Rect(rect.xMax - roleW - 4f + i * 24f, rect.y + 14f, 20f, 20f);
                RoleIcons.Draw(icon, row.Roles[i].Role, true);
                TooltipHandler.TipRegion(icon, RoleBar.TitleOf(registry, row.Roles[i]).CapitalizeFirst() + "\n" + RoleIcons.Description(row.Roles[i].Role));
            }
            var shownRow = row;
            TooltipHandler.TipRegion(rect, new TipSignal(() => shownRow.Person.FullName + " (" + FamilyUi.StatusText(shownRow.Person) + ")\n" + "WFL_FW_ClickForDetails".Translate(), 7200000 + row.Person.Id));
            if (Widgets.ButtonInvisible(rect)) OnPersonClicked(row.Person);
            y += RowHeightList;
        }

        /// <summary>A click opens the person's detail; a quick second click finds them in the colony.</summary>
        private void OnPersonClicked(PersonRecord person)
        {
            float now = Time.realtimeSinceStartup;
            bool doubleClick = lastClickedId == person.Id && now - lastClickTime < 0.4f;
            lastClickedId = person.Id; lastClickTime = now;
            if (doubleClick && person.InColony) { FamilyUi.Jump(person.Id); return; }
            State.SelectedPerson = State.SelectedPerson == person.Id && !doubleClick ? State.SelectedPerson : person.Id;
        }

        // ------------------------------------------------------------------ detail panel

        private void DrawPersonDetail(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, PersonRow row)
        {
            var p = row.Person;
            var card = FamilyKit.BeginCard(col, ref y, "person", p.FullName);
            var v = card.View;
            float cy = 0f;
            DrawPortrait(new Rect(0f, cy + 2f, 66f, 80f), p, component);
            float x = 76f, w = v.width - x;
            GUI.color = FamilyKit.Dim;
            Widgets.Label(new Rect(x, cy, w, 22f), row.Relation.Length > 0 ? row.Relation : FamilyUi.StatusText(p));
            Widgets.Label(new Rect(x, cy + 20f, w, 22f), row.Age + " - " + FamilyUi.StatusText(p));
            Widgets.Label(new Rect(x, cy + 40f, w, 22f), row.Household.Length > 0 ? row.Household : "WFL_FW_NoHousehold".Translate().ToString());
            GUI.color = Color.white;
            if (row.Status.Length > 0 && p.InColony) { GUI.color = new Color(0.75f, 0.85f, 0.75f); Widgets.Label(new Rect(x, cy + 60f, w, 22f), row.Status.Truncate(w)); GUI.color = Color.white; }
            cy += 88f;

            if (row.Concern.Length > 0) { var b = new Rect(0f, cy, v.width, 22f); FamilyKit.Badge(b, row.Concern, FamilyKit.Warn); cy += 28f; }
            if (row.HouseNote.Length > 0) cy += FamilyKit.Label(v, cy, row.HouseNote, 0f, true);
            if (row.Thread.Length > 0) { cy += FamilyKit.Label(v, cy, "WFL_FW_PersonThread".Translate(), 0f, true); cy += FamilyKit.Label(v, cy, row.Thread, 6f); }

            if (row.Roles.Count > 0)
            {
                FamilyKit.SectionHeader(v, ref cy, "WFL_FW_PublicRoles".Translate());
                foreach (var entry in row.Roles)
                {
                    var icon = new Rect(0f, cy, 22f, 22f);
                    RoleIcons.Draw(icon, entry.Role, true);
                    Widgets.Label(new Rect(30f, cy, v.width - 30f, 22f), RoleBar.TitleOf(registry, entry).CapitalizeFirst());
                    TooltipHandler.TipRegion(new Rect(0f, cy, v.width, 22f), RoleIcons.Description(entry.Role));
                    cy += 26f;
                }
            }

            // How they belong, as far as the person looking may know: only their own, or the overview's.
            int viewer = component.PerspectivePawnId;
            if (p.InColony && !p.Juvenile && (viewer == 0 || viewer == p.Id) && registry.BelongsToFamily(p, view.LineageId))
            {
                FamilyKit.SectionHeader(v, ref cy, "WFL_FW_Belonging".Translate());
                var belonging = registry.AssessBelonging(p.Id);
                var agency = registry.AgencyOf(p.Id);
                var detail = new List<string>();
                if (agency == null) detail.Add("WFL_Agency_NoEvidence".Translate().ToString());
                else
                {
                    detail.Add("WFL_Agency_Evidence".Translate(agency.Included, agency.Excluded, ("WFL_Ev_" + agency.LastEvent.Replace('-', '_') + "_Title").Translate(), FamilyUi.Date(agency.LastEvidenceTick)).ToString());
                    if (agency.Goal != PersonalGoal.None) detail.Add("WFL_Agency_Goal".Translate(("WFL_Goal_" + agency.Goal).Translate(), Mathf.RoundToInt(agency.Progress * 100), agency.Active ? "WFL_Agency_Active".Translate() : agency.Progress >= 1 ? "WFL_Agency_Achieved".Translate() : "WFL_Agency_Abandoned".Translate()).ToString());
                }
                var top = cy;
                cy += FamilyKit.Label(v, cy, "WFL_Agency_Belonging".Translate(p.Name, belonging.TrustedConnections, belonging.StrainedConnections));
                TooltipHandler.TipRegion(new Rect(0f, top, v.width, cy - top), string.Join("\n", detail));
            }
            else if (p.InColony && viewer != 0 && viewer != p.Id)
            {
                cy += 4f;
                cy += FamilyKit.Label(v, cy, "WFL_FW_NotKnownTo".Translate(FamilyUi.NameOf(registry, viewer)), 0f, true);
            }

            cy += 6f;
            float bx = 0f;
            if (p.InColony && FamilyKit.Action(new Rect(bx, cy, 112f, 26f), "WFL_FW_FindInColony".Translate(HouseholdNames.Short(p)), true, null, "WFL_FW_FindTip".Translate())) FamilyUi.Jump(p.Id);
            if (p.InColony) bx += 118f;
            if (FamilyKit.Action(new Rect(bx, cy, 130f, 26f), "WFL_FW_ViewRelationships".Translate(), p.InColony, "WFL_FW_NotInColony".Translate(), "WFL_FW_ViewRelationshipsTip".Translate()))
            { State.SelectedPerson = p.Id; State.SelectedRelation = 0; State.Navigate(FamilyTab.Relationships); }
            bx += 136f;
            if (FamilyKit.Action(new Rect(bx, cy, 60f, 26f), "WFL_FW_Close".Translate(), true)) State.SelectedPerson = 0;
            cy += 32f;
            FamilyKit.EndCard(card, cy, ref y);
        }

        // ------------------------------------------------------------------ the family tree

        private FamilyTree TreeFor(FamilyRegistry registry, int lineageId)
        {
            int revision = FamilyComponent.Current?.Revision ?? 0;
            if (cachedTree == null || cachedTreeRegistry != registry || cachedTreeLineage != lineageId || cachedTreeRevision != revision)
            {
                cachedTree = registry.BuildTree(lineageId);
                cachedTreeRegistry = registry;
                cachedTreeLineage = lineageId;
                cachedTreeRevision = revision;
            }
            return cachedTree;
        }

        private void DrawTreeSection(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage lineage)
        {
            var tree = TreeFor(registry, lineage.Id);
            if (tree.Units.Count == 0) { FamilyKit.EmptyState(v, ref y, "WFL_NoTree".Translate()); return; }
            var model = People(component);

            float z = treeZoom;
            float nodeW = NodeW0 * z, nodeH = NodeH0 * z, gapX = GapX0 * z, rowGap = RowGap0 * z, portW = PortraitW0 * z, portH = PortraitH0 * z;
            float slot = nodeW + gapX;
            float naturalW = tree.TotalWidth * slot - gapX;
            float contentW = Mathf.Max(v.width - 4f, naturalW + 8f);
            bool anyArcs = false;
            for (int i = 0; i < tree.Units.Count && !anyArcs; i++) anyArcs = tree.Units[i].Arcs.Count > 0;
            float arcRoom = anyArcs ? 32f * z : 0f;                 // arcs are drawn above the boxes
            float contentH = tree.Generations * (nodeH + rowGap) - rowGap + 8f + arcRoom;
            bool scrolls = contentW > v.width;
            float shownH = Mathf.Min(contentH + (scrolls ? 18f : 0f), 520f);
            var outRect = new Rect(0f, y, v.width, shownH);
            var viewRect = new Rect(0f, 0f, contentW, contentH);
            float ox = Mathf.Max(4f, (contentW - naturalW) / 2f);
            if (treeRecenter) { treeScroll = new Vector2(Mathf.Max(0f, (contentW - v.width) / 2f), 0f); treeRecenter = false; }

            Widgets.DrawBoxSolid(outRect, new Color(0.07f, 0.07f, 0.08f, 0.8f));
            Widgets.BeginScrollView(outRect, ref treeScroll, viewRect, true);
            float CenterOf(TreeUnit u) => ox + u.X * slot + (u.Size * slot - gapX) / 2f;
            float ChildX(TreeUnit u) => ox + (u.X + u.ChildMember) * slot + nodeW / 2f;
            float TopOf(TreeUnit u) => 4f + arcRoom + u.Generation * (nodeH + rowGap);
            var line = new Color(0.55f, 0.55f, 0.5f);

            // Parents to children: each child hangs from the point between its own two parents.
            foreach (var unit in tree.Units)
            {
                if (unit.ChildUnits.Count == 0) continue;
                float pBottom = TopOf(unit) + nodeH;
                float midY = pBottom + rowGap / 2f;
                foreach (var group in unit.ChildUnits.Select(i => tree.Units[i]).GroupBy(c => Mathf.Round(c.ParentAnchorLocal * 100f)))
                {
                    var children = group.ToList();
                    float anchorLocal = children[0].ParentAnchorLocal;
                    float px = ox + (unit.X + anchorLocal) * slot - gapX / 2f;
                    bool betweenBoxes = Mathf.Abs(anchorLocal - Mathf.Round(anchorLocal)) < 0.01f && unit.Size > 1f;
                    float startY = betweenBoxes ? TopOf(unit) + nodeH / 2f : pBottom;
                    float left = Mathf.Min(px, children.Min(c => ChildX(c)));
                    float right = Mathf.Max(px, children.Max(c => ChildX(c)));
                    Widgets.DrawBoxSolid(new Rect(px - 0.75f, startY, 1.5f, midY - startY), line);
                    Widgets.DrawBoxSolid(new Rect(left - 0.75f, midY - 0.75f, right - left + 1.5f, 1.5f), line);
                    foreach (var c in children)
                        Widgets.DrawBoxSolid(new Rect(ChildX(c) - 0.75f, midY, 1.5f, TopOf(c) - midY), line);
                    TooltipHandler.TipRegion(new Rect(Mathf.Min(left, px) - 2f, midY - 3f, right - left + 4f, 6f), "WFL_FW_TreeParentChild".Translate());
                }
            }

            // A parent who is drawn elsewhere than the unit the child hangs from.
            var faint = new Color(0.55f, 0.55f, 0.5f, 0.55f);
            foreach (var link in tree.CoParentLinks)
            {
                var child = tree.Units[link.ChildUnit];
                var parentUnit = tree.Units.FirstOrDefault(u => u.Members.Contains(link.ParentPerson));
                if (parentUnit == null) continue;
                float pcx = ox + (parentUnit.X + parentUnit.Members.IndexOf(link.ParentPerson)) * slot + nodeW / 2f;
                float ccx = ChildX(child);
                float from = TopOf(parentUnit) + nodeH;
                float midY = TopOf(child) - rowGap * 0.25f;
                Widgets.DrawBoxSolid(new Rect(pcx - 0.5f, from, 1f, midY - from), faint);
                Widgets.DrawBoxSolid(new Rect(Mathf.Min(pcx, ccx), midY - 0.5f, Mathf.Abs(ccx - pcx) + 1f, 1f), faint);
                Widgets.DrawBoxSolid(new Rect(ccx - 0.5f, midY, 1f, TopOf(child) - midY), faint);
            }

            // Partners who are not next to each other are joined by an arc over the people between them.
            foreach (var unit in tree.Units)
                foreach (var arc in unit.Arcs)
                {
                    float ax = ox + (unit.X + arc.A) * slot + nodeW / 2f;
                    float bx = ox + (unit.X + arc.B) * slot + nodeW / 2f;
                    float top = TopOf(unit);
                    float height = (6f + 6f * (arc.B - arc.A)) * z;
                    var color = arc.Kind == TreeLink.Partners ? new Color(0.9f, 0.55f, 0.6f) : new Color(0.8f, 0.35f, 0.3f);
                    var arcRect = new Rect(ax, top - height - 2f, bx - ax, height + 2f);
                    Widgets.DrawBoxSolid(new Rect(ax - 1f, top - height, 2f, height), color);
                    Widgets.DrawBoxSolid(new Rect(bx - 1f, top - height, 2f, height), color);
                    if (arc.Kind == TreeLink.Partners) Widgets.DrawBoxSolid(new Rect(ax, top - height - 1f, bx - ax, 2f), color);
                    else
                        for (float dx = ax; dx < bx; dx += 10f)
                            Widgets.DrawBoxSolid(new Rect(dx, top - height - 1f, Mathf.Min(6f, bx - dx), 2f), color);
                    TooltipHandler.TipRegion(arcRect, (arc.Kind == TreeLink.Partners ? "WFL_TreeLinkPartners" : "WFL_TreeLinkFormer").Translate());
                }

            var shown = new HashSet<int>();
            foreach (var unit in tree.Units) foreach (var id in unit.Members) shown.Add(id);
            var drawnHouseholds = new Dictionary<int, bool>();
            foreach (var unit in tree.Units)
            {
                for (int i = 0; i < unit.Members.Count; i++)
                {
                    var person = registry.GetPerson(unit.Members[i]);
                    if (person == null) continue;
                    var rect = new Rect(ox + (unit.X + i) * slot, TopOf(unit), nodeW, nodeH);
                    if (i > 0) DrawLink(new Rect(rect.x - gapX, rect.y, gapX, nodeH), unit.Links[i - 1], z);
                    DrawPersonNode(rect, registry, component, model, person, lineage, shown, z, portW, portH);
                    if (person.InColony && person.HouseholdId != 0) drawnHouseholds[person.HouseholdId] = true;
                }
            }
            Widgets.EndScrollView();
            y += outRect.height + 6f;

            DrawTreeLegend(v, ref y, registry, drawnHouseholds.Keys.OrderBy(k => k).ToList());
        }

        /// <summary>The short bar between two people drawn side by side: solid for partners, broken for a partnership that ended.</summary>
        private static void DrawLink(Rect gap, TreeLink kind, float z)
        {
            float midY = gap.y + gap.height / 2f - 1.5f;
            if (kind == TreeLink.Partners)
            {
                Widgets.DrawBoxSolid(new Rect(gap.x, midY, gap.width, 3f), new Color(0.9f, 0.55f, 0.6f));
                TooltipHandler.TipRegion(gap, "WFL_TreeLinkPartners".Translate());
            }
            else if (kind == TreeLink.Former)
            {
                var red = new Color(0.8f, 0.35f, 0.3f);
                float dash = (gap.width - 4f) / 3f;
                for (int i = 0; i < 3; i++)
                    Widgets.DrawBoxSolid(new Rect(gap.x + 2f + i * dash + 1f, midY, dash - 2f, 3f), red);
                TooltipHandler.TipRegion(gap, "WFL_TreeLinkFormer".Translate());
            }
        }

        internal static void DrawPortrait(Rect rect, PersonRecord person, FamilyComponent component)
        {
            var pawn = component?.TryGetPawn(person.Id);
            bool turn = pawn != null && (portraitsDrawn.Contains(person.Id) || portraitBudget > 0);
            if (pawn != null && turn)
            {
                if (portraitsDrawn.Add(person.Id)) portraitBudget--;
                try
                {
                    var texture = PortraitsCache.Get(pawn, new Vector2(rect.width, rect.height), Rot4.South, new Vector3(0f, 0f, 0.1f), 1.15f);
                    if (texture != null)
                    {
                        if (!person.InColony) GUI.color = new Color(1f, 1f, 1f, 0.6f);
                        GUI.DrawTexture(rect, texture);
                        GUI.color = Color.white;
                        return;
                    }
                }
                catch (Exception e)
                {
                    if (!portraitFailureReported) { portraitFailureReported = true; Log.Warning("[Wil's Family Life] Could not draw a portrait: " + e.Message); }
                }
            }
            // Someone the game no longer has a pawn for: a plain silhouette with their initial.
            Widgets.DrawBoxSolid(rect, new Color(0.18f, 0.18f, 0.18f));
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = new Color(0.55f, 0.55f, 0.55f);
            string initial = string.IsNullOrEmpty(person.Name) ? "?" : person.Name.Substring(0, 1).ToUpper();
            Widgets.Label(rect, initial);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
        }

        private void DrawPersonNode(Rect rect, FamilyRegistry registry, FamilyComponent component, PeopleModel model, PersonRecord person, Lineage lineage, HashSet<int> shown, float z, float portW, float portH)
        {
            Color fill = person.Presence == Presence.Colony ? new Color(0.22f, 0.27f, 0.22f)
                       : person.Presence == Presence.Away ? new Color(0.2f, 0.2f, 0.24f)
                       : new Color(0.14f, 0.14f, 0.14f);
            bool selected = State.SelectedPerson == person.Id;
            Widgets.DrawBoxSolid(rect, fill);
            if (Mouse.IsOver(rect)) Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.08f));
            GUI.color = selected ? FamilyKit.Gold : (person.Presence == Presence.Colony ? new Color(0.7f, 0.75f, 0.65f) : new Color(0.4f, 0.4f, 0.4f));
            Widgets.DrawBox(rect, selected ? 3 : 1);
            GUI.color = Color.white;
            if (person.InColony && person.HouseholdId != 0)
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 5f, rect.height), HouseholdColor(person.HouseholdId));

            DrawPortrait(new Rect(rect.x + 8f, rect.y + (rect.height - portH) / 2f, portW, portH), person, component);

            float textX = rect.x + 8f + portW + 4f;
            float textW = rect.xMax - textX - 3f;
            bool dim = !person.InColony;
            if (dim) GUI.color = new Color(0.65f, 0.65f, 0.65f);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = z < 0.85f ? GameFont.Tiny : GameFont.Small;
            Widgets.Label(new Rect(textX, rect.y + 3f, textW, 22f * z), person.Name.Truncate(textW));
            GUI.color = Color.white;

            var info = TreeInfo(registry, person, lineage, shown);
            var stepKids = info.StepKids; var stepParents = info.StepParents;
            string sub = info.Sub;
            Text.Font = GameFont.Tiny;
            GUI.color = stepKids.Count > 0 || stepParents.Count > 0 ? new Color(0.6f, 0.75f, 0.9f) : new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(textX, rect.y + 23f * z, textW, 32f * z), sub.Truncate(textW * 2f));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // The person's public role, small, in the corner.
            if (model.ById.TryGetValue(person.Id, out var row) && row.Roles.Count > 0)
            {
                var icon = new Rect(rect.xMax - 20f * z - 2f, rect.y + 2f, 18f * z, 18f * z);
                RoleIcons.Draw(icon, row.Roles[0].Role, true);
                TooltipHandler.TipRegion(icon, RoleBar.TitleOf(registry, row.Roles[0]).CapitalizeFirst());
            }

            TooltipHandler.TipRegion(rect, new TipSignal(() =>
            {
                string parents = person.ParentIds.Count > 0 ? "\n" + "WFL_ChildOf".Translate(FamilyUi.NameList(registry, person.ParentIds)) : "";
                string partners = person.PartnerIds().Any() ? "\n" + "WFL_PartnerOf".Translate(FamilyUi.NameList(registry, person.PartnerIds())) : "";
                string formers = person.ExIds.Count > 0 ? "\n" + "WFL_FormerPartnerOf".Translate(FamilyUi.NameList(registry, person.ExIds)) : "";
                string steps = (stepKids.Count > 0 ? "\n" + "WFL_Tree_StepParent".Translate(FamilyUi.NameList(registry, stepKids)) : "")
                             + (stepParents.Count > 0 ? "\n" + "WFL_Tree_StepChild".Translate(FamilyUi.NameList(registry, stepParents)) : "");
                string home = "";
                if (person.InColony && person.HouseholdId != 0)
                {
                    var household = registry.GetHousehold(person.HouseholdId);
                    float? cohesion = household == null ? null : registry.HouseholdCohesion(household);
                    if (cohesion.HasValue) home = "\n" + ("WFL_Tier_" + BondTuning.CohesionTier(cohesion.Value)).Translate();
                }
                return person.FullName + " (" + FamilyUi.StatusText(person) + ")" + parents + partners + formers + steps + home
                    + "\n" + "WFL_TreeClick".Translate().ToString() + (person.InColony ? " " + "WFL_TreeDoubleClick".Translate().ToString() : "");
            }, 7300000 + person.Id));
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(rect))
            {
                OnPersonClicked(person);
                Event.current.Use();
            }
        }

        private sealed class NodeInfo { public List<int> StepKids, StepParents; public string Sub; }
        private readonly Dictionary<int, NodeInfo> treeInfo = new Dictionary<int, NodeInfo>();
        private int treeInfoRevision = -1, treeInfoLineage;

        /// <summary>The words under a person in the tree, and their step-relations, worked out once per change in the records rather than every frame.</summary>
        private NodeInfo TreeInfo(FamilyRegistry registry, PersonRecord person, Lineage lineage, HashSet<int> shown)
        {
            int revision = FamilyComponent.Current?.Revision ?? 0;
            if (treeInfoRevision != revision || treeInfoLineage != lineage.Id) { treeInfo.Clear(); treeInfoRevision = revision; treeInfoLineage = lineage.Id; }
            if (treeInfo.TryGetValue(person.Id, out var hit)) return hit;
            var info = new NodeInfo
            {
                StepKids = registry.StepChildrenOf(person.Id).Where(shown.Contains).ToList(),
                StepParents = registry.StepParentsOf(person.Id).Where(shown.Contains).ToList()
            };
            info.Sub = person.Presence == Presence.Dead ? "† " + "WFL_Tree_Died".Translate()
                     : person.Presence == Presence.Away ? "WFL_Tree_Away".Translate().ToString()
                     : info.StepKids.Count > 0 ? "WFL_Tree_StepParent".Translate(FamilyUi.ShortNames(registry, info.StepKids)).ToString()
                     : info.StepParents.Count > 0 ? "WFL_Tree_StepChild".Translate(FamilyUi.ShortNames(registry, info.StepParents)).ToString()
                     : (person.SocialFamilyId == lineage.Id && person.PrimaryLineageId != lineage.Id ? "WFL_House_Chosen".Translate().ToString() : person.PrimaryLineageId == lineage.Id ? "" : "WFL_Tree_MarriedIn".Translate().ToString());
            return treeInfo[person.Id] = info;
        }

        private void DrawTreeLegend(Rect v, ref float y, FamilyRegistry registry, List<int> householdIds)
        {
            // What the lines mean, once, instead of in a paragraph.
            float x = 0f;
            float ly = y;
            void Key(Color c, string text, string tip, bool dashed = false)
            {
                var swatch = new Rect(x, ly + 8f, 22f, 3f);
                if (dashed) { Widgets.DrawBoxSolid(new Rect(x, ly + 8f, 6f, 3f), c); Widgets.DrawBoxSolid(new Rect(x + 12f, ly + 8f, 6f, 3f), c); }
                else Widgets.DrawBoxSolid(swatch, c);
                float w = Text.CalcSize(text).x;
                GUI.color = FamilyKit.Dim; Widgets.Label(new Rect(x + 28f, ly, w + 4f, 20f), text); GUI.color = Color.white;
                TooltipHandler.TipRegion(new Rect(x, ly, 28f + w, 20f), tip);
                x += 28f + w + 18f;
            }
            Key(new Color(0.55f, 0.55f, 0.5f), "WFL_FW_Legend_Descent".Translate(), "WFL_FW_TreeParentChild".Translate());
            Key(new Color(0.9f, 0.55f, 0.6f), "WFL_FW_Legend_Partners".Translate(), "WFL_TreeLinkPartners".Translate());
            Key(new Color(0.8f, 0.35f, 0.3f), "WFL_FW_Legend_Former".Translate(), "WFL_TreeLinkFormer".Translate(), true);
            Key(new Color(0.6f, 0.75f, 0.9f), "WFL_FW_Legend_Step".Translate(), "WFL_FW_Legend_StepTip".Translate());
            y += 24f;
            foreach (var id in householdIds)
            {
                var household = registry.GetHousehold(id);
                if (household == null) continue;
                var swatch = new Rect(10f, y + 4f, 12f, 12f);
                Widgets.DrawBoxSolid(swatch, HouseholdColor(id));
                string text = FamilyUi.NameList(registry, household.ResidentIds);
                float? cohesion = registry.HouseholdCohesion(household);
                if (cohesion.HasValue)
                    text += " - " + ("WFL_Tier_" + BondTuning.CohesionTier(cohesion.Value)).Translate() + " (" + Mathf.RoundToInt(cohesion.Value) + ")";
                Line(v, ref y, text, 28f);
                var leftOut = registry.LeftOutIn(household);
                if (leftOut.Count > 0) Line(v, ref y, "WFL_LeftOut".Translate(FamilyUi.NameList(registry, leftOut)), 28f, true);
            }
        }
    }
}
