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
    /// The Families window. A persistent header says who the family is and how it is doing; six tabs (Summary, People, Relationships, Events,
    /// Home, Leadership) say the rest. Each tab is in its own file (Window_Families.*.cs) and is built from the shared parts in
    /// <see cref="FamilyKit"/>. What the window remembers (family, tab, filters) lives in <see cref="State"/>, which outlives the window.
    /// Opened from the Family button on a colonist's bio card, the surname link, a role emblem, or the pawn's Family tab.
    /// </summary>
    public partial class Window_Families : Window
    {
        public static int PendingLineageId;

        internal static readonly FamilyWindowState State = new FamilyWindowState();

        private const float SidebarWidth = 226f, SidebarCollapsedWidth = 30f, HeaderHeight = 86f, SummaryBudgetPerFrame = 3f;

        private FamilyView view;
        private PeopleModel peopleModel;
        private readonly Dictionary<int, Vector2> scrolls = new Dictionary<int, Vector2>();
        private Vector2 sideScroll;
        private float bodyHeight = 400f;
        private List<Lineage> cachedLineages;
        private FamilyRegistry listedRegistry;
        private int listedRevision = -1, listedSelection;
        private readonly Dictionary<int, FamilySummary> summaries = new Dictionary<int, FamilySummary>();
        private int summaryRevision = -1, summaryViewer = -1, summaryTick;
        private int lastSelectedPawn;
        private bool collapseHandled;
        private string[] tabLabels, tabTips;
        private int[] tabCounts = new int[6];
        private bool[] tabUrgent = new bool[6];
        private float summaryBudget;
        private string branchName = "";

        public Window_Families()
        {
            doCloseX = true;
            draggable = true;
            resizeable = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = false;
            preventCameraMotion = false;
            forcePause = false;
        }

        public override Vector2 InitialSize => new Vector2(Mathf.Min(1060f, UI.screenWidth - 40f), Mathf.Min(700f, UI.screenHeight - 100f));

        public override void PreOpen()
        {
            base.PreOpen();
            var c = FamilyComponent.Current;
            if (c != null && !PerspectiveShiftBridge.TrySync(c) && !c.PerspectiveInitialized)
            {
                c.PerspectivePawnId = Find.Selector.SingleSelectedThing is Pawn selected && selected.IsColonist ? selected.thingIDNumber : c.Registry.Persons.FirstOrDefault(p => p.InColony && !p.Juvenile)?.Id ?? 0;
                c.PerspectiveInitialized = true;
            }
            lastSelectedPawn = Find.Selector.SingleSelectedThing is Pawn now && now.IsColonist ? now.thingIDNumber : 0;
            if (PendingLineageId != 0)
            {
                State.Select(PendingLineageId);
                State.Navigate(FamilyTab.Summary);
                PendingLineageId = 0;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            var component = FamilyComponent.Current;
            if (component == null) return;
            using var measurement = component.Performance.Measure("Families window");
            var registry = component.Registry;
            portraitBudget = NewPortraitsPerFrame;
            summaryBudget = SummaryBudgetPerFrame;
            int now = Find.TickManager.TicksGame;
            SyncPerspective(component);

            // A click on a surname while this window is already open should still change the selection.
            if (PendingLineageId != 0)
            {
                State.Select(PendingLineageId);
                State.Navigate(FamilyTab.Summary);
                PendingLineageId = 0;
            }

            // Only families with at least two people living in the colony, and more than one person by descent, are listed: a lone
            // colonist whose family is elsewhere, or a founder and the spouse they married, is not a family to look after. A family that was asked for directly (surname link, Family button) is still shown.
            if (cachedLineages == null || listedRegistry != registry || listedRevision != component.Revision || listedSelection != State.LineageId)
            {
                cachedLineages = registry.Lineages.Where(l => l.Id == State.LineageId || registry.IsListedFamily(l.Id))
                    .OrderByDescending(l => registry.LivingCount(l.Id, Presence.Colony)).ThenBy(l => l.Name).ToList();
                listedRegistry = registry; listedRevision = component.Revision; listedSelection = State.LineageId;
            }
            var lineages = cachedLineages;
            if (lineages.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inRect, "WFL_NoFamilies".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }
            if (State.LineageId == 0 || registry.GetLineage(State.LineageId) == null) State.Select(lineages[0].Id);
            if (!collapseHandled) { collapseHandled = true; if (lineages.Count == 1) State.SidebarCollapsed = true; }

            if (view == null || !view.Fresh(component, State.LineageId, now))
            {
                bool otherFamily = view == null || view.LineageId != State.LineageId;
                view = FamilyView.Build(component, State.LineageId, now);
                if (otherFamily) { FamilyKit.ForgetHeights(); peopleModel = null; }
                else if (peopleModel != null && !peopleModel.Fresh(component, State.LineageId, now)) peopleModel = null;
            }
            if (view.House == null) return;

            float sideW = State.SidebarCollapsed ? SidebarCollapsedWidth : WindowLayout.SidebarWidth(inRect.width);
            DrawSidebar(new Rect(0f, 0f, sideW, inRect.height), component, registry, lineages, now);
            var main = new Rect(sideW + 8f, 0f, inRect.width - sideW - 8f, inRect.height);
            DrawMain(main, component, registry);
        }

        /// <summary>Without a character mod the pawn you select becomes the acting perspective, but only when the selection changes, so the header's own choice sticks.</summary>
        private void SyncPerspective(FamilyComponent component)
        {
            if (PerspectiveShiftBridge.TrySync(component)) return;
            int id = Find.Selector.SingleSelectedThing is Pawn picked && picked.IsColonist ? picked.thingIDNumber : 0;
            if (id != 0 && id != lastSelectedPawn) component.PerspectivePawnId = id;
            lastSelectedPawn = id;
        }

        internal PeopleModel People(FamilyComponent component)
        {
            int now = Find.TickManager.TicksGame;
            if (peopleModel == null || !peopleModel.Fresh(component, State.LineageId, now)) peopleModel = PeopleModel.Build(component, view, now);
            return peopleModel;
        }

        // ------------------------------------------------------------------ the sidebar

        private FamilySummary SummaryOf(FamilyRegistry registry, FamilyComponent component, Lineage lineage, int now)
        {
            if (summaryRevision != component.Revision || summaryViewer != component.PerspectivePawnId || now - summaryTick >= 900)
            {
                summaries.Clear(); summaryRevision = component.Revision; summaryViewer = component.PerspectivePawnId; summaryTick = now;
            }
            if (summaries.TryGetValue(lineage.Id, out var hit)) return hit;
            if (summaryBudget < 1f) return null;                                    // the rest fill in over the next frames, never all at once
            summaryBudget -= 1f;
            return summaries[lineage.Id] = FamilySummaries.Of(registry, lineage, component.PerspectivePawnId, now);
        }

        private void DrawSidebar(Rect rect, FamilyComponent component, FamilyRegistry registry, List<Lineage> lineages, int now)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.10f, 0.10f, 0.11f, 0.95f));
            var toggle = new Rect(rect.xMax - 26f, rect.y + 2f, 24f, 24f);
            if (Widgets.ButtonText(toggle, State.SidebarCollapsed ? ">" : "<")) State.SidebarCollapsed = !State.SidebarCollapsed;
            TooltipHandler.TipRegion(toggle, (State.SidebarCollapsed ? "WFL_FW_SidebarShow" : "WFL_FW_SidebarHide").Translate());
            if (State.SidebarCollapsed)
            {
                // A collapsed list still shows who is selected: the crest, upright.
                var house = registry.GetLineage(State.LineageId);
                if (house != null) FamilyCrest.Draw(new Rect(rect.x + 5f, rect.y + 34f, 20f, 26f), house);
                return;
            }
            Text.Font = GameFont.Small;
            GUI.color = FamilyKit.Gold;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 3f, rect.width - 40f, 24f), "WFL_FW_Families".Translate());
            GUI.color = Color.white;

            float y = rect.y + 30f;
            State.Search = Widgets.TextField(new Rect(rect.x + 6f, y, rect.width - 12f, 24f), State.Search ?? "");
            if (string.IsNullOrEmpty(State.Search))
            {
                GUI.color = new Color(1f, 1f, 1f, 0.35f);
                Widgets.Label(new Rect(rect.x + 12f, y + 1f, rect.width - 24f, 22f), "WFL_FW_Search".Translate());
                GUI.color = Color.white;
            }
            y += 28f;
            var sortRect = new Rect(rect.x + 6f, y, rect.width - 12f, 22f);
            if (Widgets.ButtonText(sortRect, "WFL_FW_SortBy".Translate(("WFL_FW_Sort_" + State.SidebarSort).Translate())))
                Find.WindowStack.Add(new FloatMenu(Enum.GetValues(typeof(FamilySort)).Cast<FamilySort>().Select(s => { var mode = s; return new FloatMenuOption(("WFL_FW_Sort_" + mode).Translate(), () => State.SidebarSort = mode); }).ToList()));
            y += 28f;

            var rows = new List<FamilySummary>(lineages.Count);
            var pending = new List<Lineage>();
            foreach (var lineage in lineages)
            {
                var s = SummaryOf(registry, component, lineage, now);
                if (s == null) { pending.Add(lineage); s = new FamilySummary { LineageId = lineage.Id, Name = registry.LineageDisplayName(lineage), Members = registry.LivingCount(lineage.Id, Presence.Colony) }; }
                rows.Add(s);
            }
            var sorted = FamilySummaries.Sort(rows, State.SidebarSort, State.Search);

            var outRect = new Rect(rect.x + 2f, y, rect.width - 4f, rect.yMax - y - 4f);
            const float rowH = 46f;
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(sorted.Count * rowH, 10f));
            Widgets.BeginScrollView(outRect, ref sideScroll, viewRect);
            float ry = 0f;
            if (sorted.Count == 0) { GUI.color = FamilyKit.Dim; Widgets.Label(new Rect(6f, 4f, viewRect.width - 12f, 40f), "WFL_FW_NoMatch".Translate()); GUI.color = Color.white; }
            foreach (var s in sorted)
            {
                var row = new Rect(0f, ry, viewRect.width, rowH - 2f);
                bool selected = s.LineageId == State.LineageId;
                Widgets.DrawBoxSolid(row, selected ? new Color(0.22f, 0.20f, 0.12f) : new Color(0.14f, 0.14f, 0.15f));
                if (selected) Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), FamilyKit.Gold);
                else Widgets.DrawHighlightIfMouseover(row);
                var lineage = registry.GetLineage(s.LineageId);
                if (lineage != null) FamilyCrest.Draw(new Rect(row.x + 8f, row.y + 8f, 22f, 28f), lineage);
                Text.Anchor = TextAnchor.UpperLeft;
                Widgets.Label(new Rect(row.x + 38f, row.y + 3f, row.width - 62f, 22f), s.Name.Truncate(row.width - 62f));
                Text.Font = GameFont.Tiny;
                GUI.color = FamilyKit.Dim;
                string second = s.FirstAlert.HasValue ? ("WFL_FW_AlertShort_" + s.FirstAlert.Value).Translate().ToString() : s.Tier != null ? ("WFL_Tier_" + s.Tier).Translate().ToString().CapitalizeFirst() : "";
                Widgets.Label(new Rect(row.x + 38f, row.y + 23f, row.width - 44f, 18f), ("WFL_FW_MembersN".Translate(s.Members).ToString() + (second.Length > 0 ? " - " + second : "")).Truncate(row.width - 44f));
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                if (s.Alerts > 0) Widgets.DrawBoxSolid(new Rect(row.xMax - 14f, row.y + 6f, 8f, 8f), s.Urgent ? FamilyKit.Urgent : FamilyKit.Warn);
                TooltipHandler.TipRegion(row, "WFL_FW_FamilyRowTip".Translate(s.Name, s.Members, s.Households, s.Tier != null ? ("WFL_Tier_" + s.Tier).Translate().ToString() : "WFL_FW_NotAssessed".Translate().ToString(), s.Alerts));
                if (Widgets.ButtonInvisible(row) && !selected) { State.Select(s.LineageId); scrolls.Remove(ScrollKey()); }
                ry += rowH;
            }
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------ the header

        private static float Fact(float x, float y, string key, string value, float max)
        {
            Text.Font = GameFont.Small;
            string k = key + ": ";
            float kw = Text.CalcSize(k).x, vw = Mathf.Min(Text.CalcSize(value).x, max);
            GUI.color = FamilyKit.Dim;
            Widgets.Label(new Rect(x, y, kw, 22f), k);
            GUI.color = Color.white;
            Widgets.Label(new Rect(x + kw, y, vw + 2f, 22f), value);
            return kw + vw + 14f;
        }

        private static Color TierColor(string tier)
        {
            switch (tier)
            {
                case "Fractured": return FamilyKit.Urgent;
                case "Strained": return FamilyKit.Warn;
                case "Close": case "TightKnit": return FamilyKit.Good;
                default: return new Color(0.55f, 0.65f, 0.8f);
            }
        }

        private void DrawHeader(Rect rect, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.13f, 0.95f));
            FamilyCrest.Draw(new Rect(rect.x + 8f, rect.y + 8f, 46f, 56f), house);
            float x = rect.x + 64f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, rect.y + 4f, rect.width - 64f - 250f, 32f), "WFL_FamilyOf".Translate(view.Name));
            Text.Font = GameFont.Small;

            // Top right: whose eyes the window shows, and the small utility menu.
            var menu = new Rect(rect.xMax - 34f, rect.y + 6f, 28f, 26f);
            if (Widgets.ButtonText(menu, "...")) OpenUtilityMenu(component, registry);
            TooltipHandler.TipRegion(menu, "WFL_FW_MenuTip".Translate());
            var who = new Rect(menu.x - 206f, rect.y + 6f, 200f, 26f);
            DrawPerspective(who, component, registry);

            // Second line: the people and the numbers.
            float fx = x, fy = rect.y + 38f, max = 170f;
            fx += Fact(fx, fy, "WFL_FW_HouseHead".Translate(), view.Head != 0 ? FamilyUi.NameOf(registry, view.Head) : "WFL_FW_NoHead".Translate().ToString(), max);
            if (view.Steward != 0) fx += Fact(fx, fy, "WFL_FW_Steward".Translate(), FamilyUi.NameOf(registry, view.Steward), max);
            fx += Fact(fx, fy, "WFL_FW_HouseMembersLabel".Translate(), view.Counts.HouseMembers.ToString(), 60f);
            fx += Fact(fx, fy, "WFL_FW_ResidentsLabel".Translate(), "WFL_FW_ResidentsValue".Translate(view.Counts.Residents, view.Counts.Households).ToString(), 150f);
            string countsTip = "WFL_FW_CountsTip".Translate().ToString();
            if (view.Counts.Differ || view.Counts.ResidentsFromOtherHouses > 0) countsTip += System.Environment.NewLine + System.Environment.NewLine + CountsExplanation(view.Counts);
            TooltipHandler.TipRegion(new Rect(x, fy, fx - x, 22f), countsTip);

            // Third line: how the family is doing, what it wants, what needs attention.
            float bx = x, by = rect.y + 62f;
            if (!view.Insider)
            {
                GUI.color = FamilyKit.Dim;
                Widgets.Label(new Rect(bx, by - 2f, rect.width - 80f, 22f), "WFL_FW_NotKnownTo".Translate(view.NameOfPerspective(registry)));
                GUI.color = Color.white;
                return;
            }
            var s = house.Situation;
            bool assessed = s != null && s.EvaluatedTick > 0;
            string tier = assessed ? BondTuning.CohesionTier(s.Cohesion) : null;
            string cohText = tier != null ? "WFL_FW_CohesionIs".Translate(("WFL_Tier_" + tier).Translate()).ToString() : "WFL_FW_NotAssessed".Translate().ToString();
            float w = FamilyKit.BadgeWidth(cohText);
            var cohRect = new Rect(bx, by, w, 20f);
            FamilyKit.Badge(cohRect, cohText, tier != null ? TierColor(tier) : FamilyKit.Unknown);
            TooltipHandler.TipRegion(cohRect, "WFL_CohesionHelp".Translate());
            bx += w + 6f;
            var ambition = house.Ambition;
            string ambText = assessed && ambition != null && ambition.Primary != HouseAmbitionKind.None ? "WFL_FW_AmbitionIs".Translate(("WFL_Ambition_" + ambition.Primary).Translate()).ToString() : "WFL_FW_AmbitionNone".Translate().ToString();
            w = FamilyKit.BadgeWidth(ambText);
            var ambRect = new Rect(bx, by, w, 20f);
            FamilyKit.Badge(ambRect, ambText, FamilyKit.Gold);
            TooltipHandler.TipRegion(ambRect, "WFL_FW_AmbitionTip".Translate());
            if (Widgets.ButtonInvisible(ambRect)) State.Navigate(FamilyTab.HouseAffairs);
            bx += w + 6f;
            if (view.Alerts.Count > 0)
            {
                int urgent = view.Alerts.Count(a => a.Urgent);
                string alertText = urgent > 0 ? "WFL_FW_UrgentN".Translate(urgent).ToString() : "WFL_FW_AttentionN".Translate(view.Alerts.Count).ToString();
                w = FamilyKit.BadgeWidth(alertText);
                var alertRect = new Rect(bx, by, w, 20f);
                FamilyKit.Badge(alertRect, alertText, urgent > 0 ? FamilyKit.Urgent : FamilyKit.Warn);
                TooltipHandler.TipRegion(alertRect, "WFL_FW_AlertsTip".Translate());
                if (Widgets.ButtonInvisible(alertRect)) State.Navigate(FamilyTab.Summary);
            }
        }

        private void DrawPerspective(Rect rect, FamilyComponent component, FamilyRegistry registry)
        {
            bool shift = PerspectiveShiftBridge.Installed;
            string label = component.PerspectivePawnId == 0 ? "WFL_FW_ColonyOverview".Translate().ToString() : "WFL_FW_ViewingAs".Translate(FamilyUi.NameOf(registry, component.PerspectivePawnId)).ToString();
            Widgets.DrawBoxSolid(rect, new Color(0.17f, 0.17f, 0.18f));
            GUI.color = component.PerspectivePawnId == 0 ? FamilyKit.Gold : new Color(0.5f, 0.5f, 0.52f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label.Truncate(rect.width - 8f));
            Text.Anchor = TextAnchor.UpperLeft;
            string perspectiveTip = (component.PerspectivePawnId == 0 ? "WFL_FW_OverviewTip" : "WFL_FW_PerspectiveTip").Translate().ToString() + (shift ? Environment.NewLine + Environment.NewLine + "WFL_FW_PerspectiveShiftTip".Translate().ToString() : "");
            TooltipHandler.TipRegion(rect, perspectiveTip);
            if (!Widgets.ButtonInvisible(rect) || shift) return;
            var options = new List<FloatMenuOption> { new FloatMenuOption("WFL_FW_ColonyOverview".Translate(), () => component.PerspectivePawnId = 0) };
            foreach (var p in registry.Persons.Where(p => p.InColony && !p.Juvenile).OrderBy(p => p.Name).Take(40))
            {
                int id = p.Id;
                options.Add(new FloatMenuOption("WFL_FW_ViewingAs".Translate(p.FullName), () => { component.PerspectivePawnId = id; component.PerspectiveInitialized = true; }));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void OpenUtilityMenu(FamilyComponent component, FamilyRegistry registry)
        {
            var options = new List<FloatMenuOption>();
            bool overview = component.PerspectivePawnId == 0;
            options.Add(new FloatMenuOption(overview ? "WFL_ExportRecords".Translate().ToString() : "WFL_FW_ExportOverviewOnly".Translate().ToString(), overview ? (Action)(() =>
            {
                try
                {
                    string path = FamilyDebug.Export(registry, view.LineageId);
                    Messages.Message("WFL_ExportedRecords".Translate(path), MessageTypeDefOf.NeutralEvent, false);
                }
                catch (Exception e) { Log.Warning("[Wil's Family Life] Could not write the records file: " + e.Message); }
            }) : null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ------------------------------------------------------------------ main area

        private int ScrollKey() => (int)State.Tab * 10 + State.Sub(State.Tab);

        private void EnsureLabels()
        {
            if (tabLabels != null) return;
            tabLabels = new string[6]; tabTips = new string[6];
            for (int i = 0; i < 6; i++) { tabLabels[i] = ("WFL_FW_Tab_" + (FamilyTab)i).Translate(); tabTips[i] = ("WFL_FW_TabTip_" + (FamilyTab)i).Translate(); }
        }

        private void DrawMain(Rect main, FamilyComponent component, FamilyRegistry registry)
        {
            EnsureLabels();
            DrawHeader(new Rect(main.x, main.y, main.width, HeaderHeight), component, registry);
            float y = main.y + HeaderHeight + 4f;

            for (int i = 0; i < 6; i++) { tabCounts[i] = 0; tabUrgent[i] = false; }
            foreach (var a in view.Alerts) if (a.Severity >= 1) { tabCounts[(int)a.Tab]++; if (a.Urgent) tabUrgent[(int)a.Tab] = true; }
            int clicked = FamilyKit.TabStrip(new Rect(main.x, y, main.width, FamilyKit.TabH), tabLabels, (int)State.Tab, tabTips, tabCounts, tabUrgent);
            if (clicked >= 0) State.Navigate((FamilyTab)clicked, 0);
            y += FamilyKit.TabH + 4f;

            if (FamilyWindowLayout.SubCount(State.Tab) > 0)
            {
                DrawSubTabs(new Rect(main.x, y, main.width, 24f));
                y += 30f;
            }

            var outRect = new Rect(main.x, y, main.width, main.yMax - y);
            Widgets.DrawBoxSolid(outRect, new Color(0.08f, 0.08f, 0.09f, 0.6f));
            var inner = outRect.ContractedBy(6f);
            var viewRect = new Rect(0f, 0f, inner.width - 16f, Mathf.Max(bodyHeight, inner.height));
            int key = ScrollKey();
            scrolls.TryGetValue(key, out var scroll);
            Widgets.BeginScrollView(inner, ref scroll, viewRect);
            float cy = 0f;
            FamilyKit.ViewTop = scroll.y; FamilyKit.ViewBottom = scroll.y + inner.height;
            switch (State.Tab)
            {
                case FamilyTab.Summary: DrawSummary(viewRect, ref cy, component, registry); break;
                case FamilyTab.People: DrawPeople(viewRect, ref cy, component, registry); break;
                case FamilyTab.Relationships: DrawRelationships(viewRect, ref cy, component, registry); break;
                case FamilyTab.Story: DrawStory(viewRect, ref cy, component, registry); break;
                case FamilyTab.Home: DrawHome(viewRect, ref cy, component, registry); break;
                case FamilyTab.HouseAffairs: DrawLeadership(viewRect, ref cy, component, registry); break;
            }
            bodyHeight = cy + 10f;
            Widgets.EndScrollView();
            scrolls[key] = scroll;
            FamilyKit.Help(new Rect(inner.xMax - 18f, inner.y, 0f, 0f), HelpFor(State.Tab));
        }

        private void DrawSubTabs(Rect rect)
        {
            string[] labels;
            int selected;
            if (State.Tab == FamilyTab.Story)
            {
                labels = new[] { "WFL_FW_Ev_Current".Translate().ToString(), "WFL_FW_Ev_Timeline".Translate().ToString(), "WFL_FW_Ev_Chronicle".Translate().ToString() };
                selected = State.EventsSub;
            }
            else
            {
                labels = new[] { "WFL_FW_Home_Households".Translate().ToString(), "WFL_FW_Home_Supplies".Translate().ToString(), "WFL_FW_Home_Area".Translate().ToString() };
                selected = State.HomeSub;
            }
            float x = rect.x;
            for (int i = 0; i < labels.Length; i++)
            {
                float w = Text.CalcSize(labels[i]).x + 26f;
                var r = new Rect(x, rect.y, w, rect.height);
                bool on = i == selected;
                Widgets.DrawBoxSolid(r, on ? new Color(0.20f, 0.18f, 0.10f) : new Color(0.13f, 0.13f, 0.14f));
                if (on) Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 2f, r.width, 2f), FamilyKit.Gold);
                else Widgets.DrawHighlightIfMouseover(r);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = on ? Color.white : new Color(0.75f, 0.75f, 0.75f);
                Widgets.Label(r, labels[i]);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(r) && !on) { if (State.Tab == FamilyTab.Story) State.EventsSub = i; else State.HomeSub = i; }
                x += w + 4f;
            }
        }

        /// <summary>Text that explains a tab, kept out of the way: it shows when the [?] in the corner is hovered.</summary>
        private static string HelpFor(FamilyTab t)
        {
            string[] keys;
            switch (t)
            {
                case FamilyTab.Summary: keys = new[] { "WFL_FW_Help_Summary", "WFL_CohesionHelp" }; break;
                case FamilyTab.People: keys = new[] { "WFL_House_Ancestry", "WFL_TreeLegend", "WFL_TreeLegendLinks" }; break;
                case FamilyTab.Relationships: keys = new[] { "WFL_RelHelp" }; break;
                case FamilyTab.Story: keys = new[] { "WFL_Moments_TieNote", "WFL_TraditionsHelp" }; break;
                case FamilyTab.Home: keys = new[] { "WFL_HouseholdsHelp", "WFL_ZoneHelp" }; break;
                case FamilyTab.HouseAffairs: keys = new[] { "WFL_Manage_Help", "WFL_Associates_Help" }; break;
                default: return null;
            }
            return string.Join("\n\n", keys.Select(k => k.Translate().ToString()));
        }

        // ------------------------------------------------------------------ small helpers shared by the tabs

        private static void Line(Rect view, ref float y, string text, float indent = 0f, bool dim = false)
        {
            float h = Text.CalcHeight(text, view.width - indent);
            if (dim) GUI.color = Color.gray;
            Widgets.Label(new Rect(indent, y, view.width - indent, h), text);
            GUI.color = Color.white;
            y += h + 2f;
        }

        private static void Line(Rect view, ref float y, string key, float indent, bool dim, params string[] args)
        {
            Line(view, ref y, key.Translate(args.Select(a => (NamedArgument)a).ToArray()).ToString(), indent, dim);
        }

        /// <summary>A line whose detail is in its tooltip, so the list stays short.</summary>
        private static void LineTip(Rect view, ref float y, string text, string tip, float indent = 0f, bool dim = false)
        {
            float top = y;
            Line(view, ref y, text, indent, dim);
            if (!string.IsNullOrEmpty(tip)) { var area = new Rect(indent, top, view.width - indent, y - top); Widgets.DrawHighlightIfMouseover(area); TooltipHandler.TipRegion(area, tip); }
        }

        private static void Heading(Rect view, ref float y, string text) => FamilyKit.SectionHeader(view, ref y, text);

        private static bool ActionButton(Rect rect, string label, bool allowed)
        { bool previous = GUI.enabled; GUI.enabled = previous && allowed; bool clicked = Widgets.ButtonText(rect, label); GUI.enabled = previous; return clicked; }

        /// <summary>Whether the person being played may manage this house. The overview may, because it is the administrative view.</summary>
        private static bool MayManage(FamilyRegistry registry, Lineage lineage)
        {
            int actor = FamilyComponent.Current?.PerspectivePawnId ?? 0;
            return actor == 0 || registry.CanManageHouse(actor, lineage.Id);
        }

        private static bool MayManageHead(FamilyRegistry registry, Lineage lineage)
        {
            int actor = FamilyComponent.Current?.PerspectivePawnId ?? 0;
            return actor == 0 || registry.HeadOf(lineage.Id) == actor;
        }

        private static string WhyNot(FamilyRegistry registry, Lineage lineage, bool headOnly = false)
        {
            int actor = FamilyComponent.Current?.PerspectivePawnId ?? 0;
            return (headOnly ? "WFL_FW_WhyNot_Head" : "WFL_FW_WhyNot_Manage").Translate(FamilyUi.NameOf(registry, actor), registry.LineageDisplayName(lineage)).ToString();
        }

        private bool HouseButton(Rect rect, string label, FamilyRegistry registry, Lineage lineage, bool headOnly = false)
        {
            bool allowed = headOnly ? MayManageHead(registry, lineage) : MayManage(registry, lineage);
            return FamilyKit.Action(rect, label, allowed, WhyNot(registry, lineage, headOnly));
        }

        private static void ReportHouseAction(bool started)
        { if (!started) Messages.Message("WFL_House_ActionUnavailable".Translate(), MessageTypeDefOf.NeutralEvent, false); }

        private static string CeremonyInfluences(CeremonyInfluence influences)
        {
            if (influences == CeremonyInfluence.None) return "WFL_Ceremony_Influence_None".Translate();
            return string.Join(", ", Enum.GetValues(typeof(CeremonyInfluence)).Cast<CeremonyInfluence>()
                .Where(i => i != CeremonyInfluence.None && influences.HasFlag(i)).Select(i => ("WFL_Ceremony_Influence_" + i).Translate().ToString()));
        }
    }
}
