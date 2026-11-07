using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>The Families window: one list of lineages and, for the chosen one, its overview, members, relationships, households and history.
    /// Opened from the Family button on a colonist's bio card, the surname link, or the pawn's Family tab.</summary>
    public class Window_Families : Window
    {
        public static int PendingLineageId;

        private enum Tab { Overview, Relationships, Moments, Households, Manage, Zone, Traditions, History }

        private const float ListWidth = 230f;
        private const float RowHeight = 28f;

        private int selectedLineageId;
        private Tab tab = Tab.Overview;
        private string branchName = "";
        private Vector2 listScroll;
        private Vector2 bodyScroll;
        private float bodyHeight = 400f;
        private int relationsPersonId;
        private Vector2 treeScroll;
        private int focusPersonId;               // the person chosen in the tree; everyone else then shows how close they are to them
        private int shownFocusId;                // whom the meters are about this frame (the chosen person, or the first colonist until someone is chosen)
        private bool metersOff;                  // the player clicked the chosen person again to hide the meters
        private int lastClickedId;
        private float lastClickTime;
        private FamilyTree cachedTree;
        private int cachedTreeLineage;
        private int cachedTreeRevision = -1;
        private FamilyRegistry cachedTreeRegistry;
        private List<Lineage> cachedLineages;
        private FamilyRegistry listedRegistry;
        private int listedRevision = -1, listedSelection;

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

        public override Vector2 InitialSize => new Vector2(Mathf.Min(980f, UI.screenWidth - 40f), Mathf.Min(640f, UI.screenHeight - 120f));

        public override void PreOpen()
        {
            base.PreOpen();
            var c=FamilyComponent.Current;
            if(c!=null&&!PerspectiveShiftBridge.TrySync(c)&&!c.PerspectiveInitialized)
            {
                c.PerspectivePawnId=Find.Selector.SingleSelectedThing is Pawn selected && selected.IsColonist ? selected.thingIDNumber : c.Registry.Persons.FirstOrDefault(p=>p.InColony && !p.Juvenile)?.Id??0;
                c.PerspectiveInitialized=true;
            }
            if (PendingLineageId != 0)
            {
                selectedLineageId = PendingLineageId;
                PendingLineageId = 0;
                tab = Tab.Overview;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            var component = FamilyComponent.Current;
            if (component == null) return;
            using var measurement = component.Performance.Measure("Families window");
            var registry = component.Registry;
            portraitBudget = NewPortraitsPerFrame;

            // A click on a surname while this window is already open should still change the selection.
            if (PendingLineageId != 0)
            {
                selectedLineageId = PendingLineageId;
                PendingLineageId = 0;
                tab = Tab.Overview;
                bodyScroll = Vector2.zero;
            }

            // Only families with at least two people living in the colony, and more than one person by descent, are listed: a lone
            // colonist whose family is elsewhere, or a founder and the spouse they married, is not a family to look after. A family that was asked for directly (surname link, Family button) is still shown.
            if (cachedLineages == null || listedRegistry != registry || listedRevision != component.Revision || listedSelection != selectedLineageId)
            {
                cachedLineages = registry.Lineages.Where(l => l.Id == selectedLineageId || registry.IsListedFamily(l.Id))
                    .OrderByDescending(l => registry.LivingCount(l.Id, Presence.Colony)).ThenBy(l => l.Name).ToList();
                listedRegistry = registry; listedRevision = component.Revision; listedSelection = selectedLineageId;
            }
            var lineages = cachedLineages;

            if (lineages.Count == 0)
            {
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(inRect, "WFL_NoFamilies".Translate());
                Text.Anchor = TextAnchor.UpperLeft;
                return;
            }
            if (selectedLineageId == 0 || registry.GetLineage(selectedLineageId) == null) selectedLineageId = lineages[0].Id;

            var listRect = new Rect(0f, 0f, ListWidth, inRect.height);
            DrawLineageList(listRect, registry, lineages);

            var bodyRect = new Rect(ListWidth + 10f, 0f, inRect.width - ListWidth - 10f, inRect.height);
            DrawBody(bodyRect, component, registry, registry.GetLineage(selectedLineageId));
        }

        // ------------------------------------------------------------------ lineage list

        private void DrawLineageList(Rect rect, FamilyRegistry registry, List<Lineage> lineages)
        {
            Widgets.DrawMenuSection(rect);
            var outRect = rect.ContractedBy(4f);
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, lineages.Count * RowHeight);
            Widgets.BeginScrollView(outRect, ref listScroll, viewRect);
            float y = 0f;
            foreach (var lineage in lineages)
            {
                var row = new Rect(0f, y, viewRect.width, RowHeight);
                if (lineage.Id == selectedLineageId) Widgets.DrawHighlightSelected(row);
                else Widgets.DrawHighlightIfMouseover(row);

                int living = registry.LivingCount(lineage.Id, Presence.Colony);
                var nameRect = new Rect(row.x + 6f, row.y, row.width - 40f, row.height);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(nameRect, "WFL_FamilyOf".Translate(registry.LineageDisplayName(lineage)));
                Text.Anchor = TextAnchor.MiddleRight;
                Widgets.Label(new Rect(row.xMax - 36f, row.y, 30f, row.height), living.ToString());
                Text.Anchor = TextAnchor.UpperLeft;
                TooltipHandler.TipRegion(row, "WFL_ListTip".Translate(living));
                if (Widgets.ButtonInvisible(row))
                {
                    selectedLineageId = lineage.Id;
                    bodyScroll = Vector2.zero;
                }
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        // ------------------------------------------------------------------ body

        private void DrawBody(Rect rect, FamilyComponent component, FamilyRegistry registry, Lineage lineage)
        {
            if (lineage == null) return;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f), "WFL_FamilyOf".Translate(registry.LineageDisplayName(lineage)));
            Text.Font = GameFont.Small;
            var exportRect = new Rect(rect.xMax - 190f, rect.y + 2f, 180f, 26f);
            if (ActionButton(exportRect, "WFL_ExportRecords".Translate(),component.PerspectivePawnId==0))
            {
                try
                {
                    string path = FamilyDebug.Export(registry, lineage.Id);
                    Messages.Message("WFL_ExportedRecords".Translate(path), MessageTypeDefOf.NeutralEvent, false);
                }
                catch (Exception e) { Log.Warning("[Wil's Family Life] Could not write the records file: " + e.Message); }
            }
            TooltipHandler.TipRegion(exportRect, "WFL_ExportRecordsTip".Translate());

            float tabY = rect.y + 36f;
            if(!PerspectiveShiftBridge.TrySync(component)&&Find.Selector.SingleSelectedThing is Pawn picked&&picked.IsColonist)
                component.PerspectivePawnId=picked.thingIDNumber; // without a character mod, the pawn you selected is the acting perspective
            float tabWidth = Mathf.Min(108f, (rect.width - 4f * 7f) / 8f);
            var tabs = new[] { Tab.Overview, Tab.Relationships, Tab.Moments, Tab.Households, Tab.Manage, Tab.Zone, Tab.Traditions, Tab.History };
            for (int i = 0; i < tabs.Length; i++)
            {
                var r = new Rect(rect.x + i * (tabWidth + 4f), tabY, tabWidth, 28f);
                if (tab == tabs[i]) Widgets.DrawHighlightSelected(r);
                if (Widgets.ButtonText(r, ("WFL_Tab_" + tabs[i]).Translate())) { tab = tabs[i]; bodyScroll = Vector2.zero; }
            }

            var outRect = new Rect(rect.x, tabY + 34f, rect.width, rect.yMax - tabY - 34f);
            Widgets.DrawMenuSection(outRect);
            outRect = outRect.ContractedBy(6f);
            var viewRect = new Rect(0f, 0f, outRect.width - 16f, Mathf.Max(bodyHeight, outRect.height));
            Widgets.BeginScrollView(outRect, ref bodyScroll, viewRect);
            float y = 0f;
            switch (tab)
            {
                case Tab.Overview: DrawOverview(viewRect, ref y, registry, lineage); break;
                case Tab.Relationships: DrawRelationships(viewRect, ref y, registry, lineage); break;
                case Tab.Moments: DrawMoments(viewRect, ref y, component, registry, lineage); break;
                case Tab.Households: DrawHouseholds(viewRect, ref y, component, registry, lineage); break;
                case Tab.Manage: DrawManage(viewRect, ref y, component, registry, lineage); break;
                case Tab.Zone: DrawZone(viewRect, ref y, component, registry, lineage); break;
                case Tab.Traditions: DrawTraditions(viewRect, ref y, registry, lineage); break;
                case Tab.History: DrawHistory(viewRect, ref y, registry, lineage); break;
            }
            bodyHeight = y + 10f;
            Widgets.EndScrollView();
            DrawHelpIcon(new Rect(outRect.xMax - 18f, outRect.y, 0f, 0f), HelpFor(tab));
        }

        private static void Line(Rect view, ref float y, string text, float indent = 0f, bool dim = false)
        {
            float h = Text.CalcHeight(text, view.width - indent);
            if (dim) GUI.color = Color.gray;
            Widgets.Label(new Rect(indent, y, view.width - indent, h), text);
            GUI.color = Color.white;
            y += h + 2f;
        }

        /// <summary>Text that explains a tab, kept out of the way: it shows when the [?] in the corner is hovered.</summary>
        private static string HelpFor(Tab t)
        {
            string[] keys;
            switch (t)
            {
                case Tab.Overview: keys = new[] { "WFL_House_Ancestry", "WFL_TreeFocusHelp", "WFL_TreeLegend", "WFL_TreeLegendLinks", "WFL_CohesionHelp", "WFL_CultureHelp" }; break;
                case Tab.Relationships: keys = new[] { "WFL_RelHelp" }; break;
                case Tab.Moments: keys = new[] { "WFL_Moments_TieNote" }; break;
                case Tab.Households: keys = new[] { "WFL_HouseholdsHelp" }; break;
                case Tab.Manage: keys = new[] { "WFL_Manage_Help", "WFL_Associates_Help" }; break;
                case Tab.Zone: keys = new[] { "WFL_ZoneHelp" }; break;
                case Tab.Traditions: keys = new[] { "WFL_TraditionsHelp" }; break;
                default: return null;
            }
            return string.Join("\n\n", keys.Select(k => k.Translate().ToString()));
        }

        private static void DrawHelpIcon(Rect corner, string tip)
        {
            if (string.IsNullOrEmpty(tip)) return;
            var q = new Rect(corner.xMax - 22f, corner.y + 2f, 20f, 20f);
            Widgets.DrawBoxSolid(q, new Color(0.1f, 0.1f, 0.1f, 0.85f));
            GUI.color = Mouse.IsOver(q) ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            Widgets.DrawBox(q);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(q, "?");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(q, tip);
        }

        /// <summary>A line whose detail is in its tooltip, so the list stays short.</summary>
        private static void LineTip(Rect view, ref float y, string text, string tip, float indent = 0f, bool dim = false)
        {
            float top = y;
            Line(view, ref y, text, indent, dim);
            if (!string.IsNullOrEmpty(tip)) { var area = new Rect(indent, top, view.width - indent, y - top); Widgets.DrawHighlightIfMouseover(area); TooltipHandler.TipRegion(area, tip); }
        }

        private static void Heading(Rect view, ref float y, string text)
        {
            y += 6f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, view.width, 30f), text);
            Text.Font = GameFont.Small;
            y += 32f;
        }

        // ------------------------------------------------------------------ overview

        private static bool ActionButton(Rect rect,string label,bool allowed)
        {bool previous=GUI.enabled;GUI.enabled=previous&&allowed;bool clicked=Widgets.ButtonText(rect,label);GUI.enabled=previous;return clicked;}
        private static void ReportHouseAction(bool started)
        {if(!started) Messages.Message("WFL_House_ActionUnavailable".Translate(),MessageTypeDefOf.NeutralEvent,false);}

        private bool HouseButton(Rect rect,string label,FamilyRegistry registry,Lineage lineage,bool headOnly=false)
        {
            int actor=FamilyComponent.Current?.PerspectivePawnId??0;
            bool allowed=actor==0 || (headOnly?registry.HeadOf(lineage.Id)==actor:registry.CanManageHouse(actor,lineage.Id));
            return ActionButton(rect,label,allowed);
        }
        // ------------------------------------------------------------------ manage

        private void DrawManage(Rect view, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage lineage)
        {
            float w = Mathf.Min(view.width - 20f, 620f);

            // Requests and invitations this house is dealing with. People raise these themselves; nothing here needs pressing.
            Heading(view, ref y, "WFL_Manage_Petitions".Translate());
            var petitions = registry.HousePetitions.Where(q => q.FamilyId == lineage.Id || q.SourceFamilyId == lineage.Id).OrderByDescending(q => q.CreatedTick).Take(10).ToList();
            if (petitions.Count == 0) Line(view, ref y, "WFL_Manage_NoPetitions".Translate(), 0f, true);
            foreach (var q in petitions)
                Line(view, ref y, "WFL_Petition_Status".Translate(FamilyUi.NameOf(registry, q.PersonId), ("WFL_PetitionKind_" + q.Kind).Translate(), ("WFL_PetitionState_" + q.State).Translate(), registry.LineageDisplayName(registry.GetLineage(q.FamilyId))), 10f);

            Heading(view,ref y,"WFL_MarriageAlliance_Title".Translate());
            var alliances=registry.MarriageAlliances.Where(a=>a.FamilyA==lineage.Id||a.FamilyB==lineage.Id).OrderByDescending(a=>a.CreatedTick).Take(8).ToList();
            if(alliances.Count==0)Line(view,ref y,"WFL_MarriageAlliance_None".Translate(),10f,true);
            foreach(var a in alliances)
            {
                int otherFamily=a.FamilyA==lineage.Id?a.FamilyB:a.FamilyA;
                float interest=a.FamilyA==lineage.Id?a.HouseAInterest:a.HouseBInterest;
                var objections=a.FamilyA==lineage.Id?a.ObjectionsA:a.ObjectionsB;
                LineTip(view,ref y,"WFL_MarriageAlliance_Line".Translate(FamilyUi.NameOf(registry,a.PawnA),FamilyUi.NameOf(registry,a.PawnB),("WFL_MarriageAllianceState_"+a.State).Translate()),
                    "WFL_MarriageAlliance_Tip".Translate(registry.LineageDisplayName(registry.GetLineage(otherFamily)),interest.ToString("0"),
                        a.PawnAWillingness.ToString("0"),a.PawnBWillingness.ToString("0"),a.Reasons.ToString(),objections.ToString()),10f);
            }

            Heading(view, ref y, "WFL_Stewardship_Title".Translate());
            Line(view, ref y, "WFL_Stewardship_Head".Translate(FamilyUi.NameOf(registry, registry.HeadOf(lineage.Id))), 10f);
            var stewards = lineage.Stewards.Select(id => registry.GetPerson(id)).Where(p => p != null && p.InColony).ToList();
            if (stewards.Count == 0) Line(view, ref y, "WFL_Stewardship_None".Translate(), 10f, true);
            foreach (var steward in stewards)
            {
                var record = registry.StewardshipOf(steward.Id, lineage.Id, true);
                LineTip(view, ref y, "WFL_Stewardship_Line".Translate(FamilyUi.NameOf(registry, steward.Id), record?.Successes ?? 0, record?.Failures ?? 0),
                    "WFL_Stewardship_Tip".Translate(record?.CeremoniesOrganized ?? 0, record?.AidTransfers ?? 0, record?.Reconciliations ?? 0,
                        record?.NegotiationsLed ?? 0, record?.GuestsHoused ?? 0, record?.ShortagesAverted ?? 0), 10f);
            }

            // Friends and associates of the house.
            Heading(view, ref y, "WFL_Associates_Title".Translate());
            foreach (var c in registry.HouseContacts.Where(c => c.FamilyId == lineage.Id && !registry.BelongsToFamily(registry.GetPerson(c.PersonId), lineage.Id)).OrderByDescending(c => c.Respect).Take(12))
                Line(view, ref y, "WFL_Associates_Line".Translate(FamilyUi.NameOf(registry, c.PersonId), ("WFL_HouseRole_" + registry.RoleInHouse(c.PersonId, lineage.Id)).Translate(), c.Respect.ToString("0")), 10f, true);

            // The house's look and branches: the player's own to shape.
            Heading(view, ref y, "WFL_Manage_Crest".Translate());
            FamilyCrest.Draw(new Rect(10f, y + 4f, 64f, 76f), lineage);
            var crestLeft = new Rect(86f, 0f, 150f, 30f); var crestRight = new Rect(242f, 0f, 150f, 30f);
            crestLeft.y = crestRight.y = y + 4f;
            if (Widgets.ButtonText(crestLeft, "WFL_Crest_Primary".Translate())) lineage.CrestPrimary = (CrestStyle.Index(lineage.CrestPrimary, lineage.Id, CrestStyle.Colors) + 1) % CrestStyle.Colors;
            if (Widgets.ButtonText(crestRight, "WFL_Crest_Secondary".Translate())) lineage.CrestSecondary = (CrestStyle.Index(lineage.CrestSecondary, 7, CrestStyle.Colors) + 1) % CrestStyle.Colors;
            crestLeft.y = crestRight.y = y + 40f;
            if (Widgets.ButtonText(crestLeft, ("WFL_Crest_Mark_" + CrestStyle.Index(lineage.CrestMark, lineage.Id / CrestStyle.Colors, CrestStyle.Marks)).Translate())) lineage.CrestMark = (CrestStyle.Index(lineage.CrestMark, lineage.Id / CrestStyle.Colors, CrestStyle.Marks) + 1) % CrestStyle.Marks;
            if (Widgets.ButtonText(crestRight, ("WFL_Crest_Pattern_" + CrestStyle.Index(lineage.CrestPattern, 0, CrestStyle.Patterns)).Translate())) lineage.CrestPattern = (CrestStyle.Index(lineage.CrestPattern, 0, CrestStyle.Patterns) + 1) % CrestStyle.Patterns;
            y += 90f;
            float half = (w - 8f) / 2f;
            branchName = Widgets.TextField(new Rect(10f, y, half, 28f), branchName);
            if (Widgets.ButtonText(new Rect(10f + half + 8f, y, half, 28f), "WFL_House_Branch".Translate()))
            {
                var options = registry.MembersOf(lineage.Id).Where(p => p.InColony && !p.Juvenile).Take(100).Select(p => new FloatMenuOption(FamilyUi.NameOf(registry, p.Id), () => { var branch = registry.CreateFamilyBranch(p.Id, lineage.Id, branchName, Activities.Now); if (branch != null) { selectedLineageId = branch.Id; branchName = ""; component.FamilyMembershipChanged(); } })).ToList();
                if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
            }
            y += 38f;
        }

        // ------------------------------------------------------------------ overview

        private void DrawOverview(Rect view, ref float y, FamilyRegistry registry, Lineage lineage)
        {
            FamilyCrest.Draw(new Rect(0f, y + 4f, 48f, 58f), lineage);
            Line(view, ref y, "WFL_FoundedBy".Translate(FamilyUi.NameOf(registry, lineage.FounderId), FamilyUi.Date(lineage.FoundedTick)), 56f);
            Line(view, ref y, "WFL_Counts".Translate(
                registry.LivingCount(lineage.Id, Presence.Colony),
                registry.LivingCount(lineage.Id, Presence.Away),
                registry.LivingCount(lineage.Id, Presence.Dead)), 56f);
            y = Mathf.Max(y, 70f) + 4f;

            DrawHouseAmbition(view, ref y, registry, lineage);

            Heading(view, ref y, "WFL_Tree".Translate());
            DrawTree(view, ref y, registry, lineage);
            DrawCulture(view, ref y, registry, lineage);
        }

        private static void DrawHouseAmbition(Rect view, ref float y, FamilyRegistry registry, Lineage lineage)
        {
            Heading(view, ref y, "WFL_Ambition_Title".Translate());
            var situation = lineage.Situation; var ambition = lineage.Ambition;
            if (situation == null || situation.EvaluatedTick <= 0 || ambition == null)
            { Line(view, ref y, "WFL_Ambition_Assessing".Translate(), 10f, true); return; }
            string primary = ("WFL_Ambition_" + ambition.Primary).Translate();
            string reason = ("WFL_Need_" + ambition.PrimaryReason).Translate();
            string target = ambition.TargetFamilyId == 0 ? "" : " " + "WFL_Ambition_Target".Translate(registry.LineageDisplayName(registry.GetLineage(ambition.TargetFamilyId)));
            LineTip(view, ref y, "WFL_Ambition_Primary".Translate(primary, reason, ambition.Urgency.ToString("0")) + target,
                "WFL_Ambition_Metrics".Translate(situation.Sustenance.ToString("0"), situation.LivingSpace.ToString("0"), situation.Safety.ToString("0"),
                    situation.Continuity.ToString("0"), situation.Capability.ToString("0"), situation.Cohesion.ToString("0"),
                    situation.Influence.ToString("0"), situation.Independence.ToString("0")), 10f);
            if (ambition.Secondary != HouseAmbitionKind.None)
                Line(view, ref y, "WFL_Ambition_Secondary".Translate(("WFL_Ambition_" + ambition.Secondary).Translate(), ("WFL_Need_" + ambition.SecondaryReason).Translate()), 10f, true);
            if (ambition.TargetCapability != HouseCapability.None)
                Line(view, ref y, "WFL_Ambition_SkillGap".Translate(("WFL_Capability_" + ambition.TargetCapability).Translate()), 10f, true);
        }

        // ------------------------------------------------------------------ the family tree

        private const float NodeW = 148f;
        private const float NodeH = 58f;
        private const float GapX = 18f;
        private const float RowGap = 38f;
        private const float PortraitW = 40f;
        private const float PortraitH = 48f;

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

        private void DrawTree(Rect view, ref float y, FamilyRegistry registry, Lineage lineage)
        {
            var tree = TreeFor(registry, lineage.Id);
            if (tree.Units.Count == 0)
            {
                Line(view, ref y, "WFL_NoTree".Translate(), 10f, true);
                return;
            }
            var component = FamilyComponent.Current;
            var allShown = new HashSet<int>(tree.Units.SelectMany(u => u.Members));
            int focus = allShown.Contains(focusPersonId) ? focusPersonId : 0;
            if (focus == 0 && !metersOff)
            {
                // Until someone is chosen the meters are about the first colonist, so closeness is visible without having to find the click.
                var first = tree.Units.OrderBy(u => u.Generation).ThenBy(u => u.X).SelectMany(u => u.Members)
                    .Select(id => registry.GetPerson(id)).FirstOrDefault(p => p != null && p.InColony);
                if (first != null) focus = first.Id;
            }
            shownFocusId = focus;
            if (focus != 0) Line(view, ref y, "WFL_TreeFocusing".Translate(FamilyUi.NameOf(registry, focus)));
            float slot = NodeW + GapX;
            float naturalW = tree.TotalWidth * slot - GapX;
            float contentW = Mathf.Max(view.width - 4f, naturalW + 8f);
            bool anyArcs = tree.Units.Any(u => u.Arcs.Count > 0);
            float arcRoom = anyArcs ? 32f : 0f;      // arcs are drawn above the boxes
            float contentH = tree.Generations * (NodeH + RowGap) - RowGap + 8f + arcRoom;
            bool scrolls = contentW > view.width;
            var outRect = new Rect(0f, y, view.width, contentH + (scrolls ? 18f : 0f));
            var viewRect = new Rect(0f, 0f, contentW, contentH);
            float ox = Mathf.Max(4f, (contentW - naturalW) / 2f);

            Widgets.BeginScrollView(outRect, ref treeScroll, viewRect, true);
            Func<TreeUnit, float> centerOf = u => ox + u.X * slot + (u.Size * slot - GapX) / 2f;
            // A line down to a child comes to the child's own box, not to the middle of the couple they are part of.
            Func<TreeUnit, float> childX = u => ox + (u.X + u.ChildMember) * slot + NodeW / 2f;
            Func<TreeUnit, float> topOf = u => 4f + arcRoom + u.Generation * (NodeH + RowGap);
            var line = new Color(0.55f, 0.55f, 0.5f);

            // Parents to children: each child hangs from the point between its own two parents.
            foreach (var unit in tree.Units)
            {
                if (unit.ChildUnits.Count == 0) continue;
                float pBottom = topOf(unit) + NodeH;
                float midY = pBottom + RowGap / 2f;
                foreach (var group in unit.ChildUnits.Select(i => tree.Units[i]).GroupBy(c => Mathf.Round(c.ParentAnchorLocal * 100f)))
                {
                    var children = group.ToList();
                    float anchorLocal = children[0].ParentAnchorLocal;
                    float px = ox + (unit.X + anchorLocal) * slot - GapX / 2f;
                    bool betweenBoxes = Mathf.Abs(anchorLocal - Mathf.Round(anchorLocal)) < 0.01f && unit.Size > 1f;
                    float startY = betweenBoxes ? topOf(unit) + NodeH / 2f : pBottom;
                    float left = Mathf.Min(px, children.Min(c => childX(c)));
                    float right = Mathf.Max(px, children.Max(c => childX(c)));
                    Widgets.DrawBoxSolid(new Rect(px - 0.75f, startY, 1.5f, midY - startY), line);
                    Widgets.DrawBoxSolid(new Rect(left - 0.75f, midY - 0.75f, right - left + 1.5f, 1.5f), line);
                    foreach (var c in children)
                        Widgets.DrawBoxSolid(new Rect(childX(c) - 0.75f, midY, 1.5f, topOf(c) - midY), line);
                }
            }

            // A parent who is drawn elsewhere than the unit the child hangs from.
            var faint = new Color(0.55f, 0.55f, 0.5f, 0.55f);
            foreach (var link in tree.CoParentLinks)
            {
                var child = tree.Units[link.ChildUnit];
                var parentUnit = tree.Units.FirstOrDefault(u => u.Members.Contains(link.ParentPerson));
                if (parentUnit == null) continue;
                float pcx = ox + (parentUnit.X + parentUnit.Members.IndexOf(link.ParentPerson)) * slot + NodeW / 2f;
                float ccx = childX(child);
                float from = topOf(parentUnit) + NodeH;
                float midY = topOf(child) - RowGap * 0.25f;
                Widgets.DrawBoxSolid(new Rect(pcx - 0.5f, from, 1f, midY - from), faint);
                Widgets.DrawBoxSolid(new Rect(Mathf.Min(pcx, ccx), midY - 0.5f, Mathf.Abs(ccx - pcx) + 1f, 1f), faint);
                Widgets.DrawBoxSolid(new Rect(ccx - 0.5f, midY, 1f, topOf(child) - midY), faint);
            }

            // Partners who are not next to each other are joined by an arc over the people between them.
            foreach (var unit in tree.Units)
                foreach (var arc in unit.Arcs)
                {
                    float ax = ox + (unit.X + arc.A) * slot + NodeW / 2f;
                    float bx = ox + (unit.X + arc.B) * slot + NodeW / 2f;
                    float top = topOf(unit);
                    float height = 6f + 6f * (arc.B - arc.A);
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

            var shown = new HashSet<int>(tree.Units.SelectMany(u => u.Members));
            var drawnHouseholds = new Dictionary<int, bool>();
            foreach (var unit in tree.Units)
            {
                for (int i = 0; i < unit.Members.Count; i++)
                {
                    var person = registry.GetPerson(unit.Members[i]);
                    if (person == null) continue;
                    var rect = new Rect(ox + (unit.X + i) * slot, topOf(unit), NodeW, NodeH);
                    if (i > 0) DrawLink(new Rect(rect.x - GapX, rect.y, GapX, NodeH), unit.Links[i - 1]);
                    DrawPersonNode(rect, registry, component, person, lineage, shown, focus);
                    if (person.InColony && person.HouseholdId != 0) drawnHouseholds[person.HouseholdId] = true;
                }
            }
            Widgets.EndScrollView();
            y += outRect.height + 6f;

            DrawTreeLegend(view, ref y, registry, drawnHouseholds.Keys.OrderBy(k => k).ToList());
        }

        /// <summary>The short bar between two people drawn side by side: solid for partners, broken for a partnership that ended.</summary>
        private static void DrawLink(Rect gap, TreeLink kind)
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

        private static void DrawPortrait(Rect rect, PersonRecord person, FamilyComponent component)
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

        private void DrawPersonNode(Rect rect, FamilyRegistry registry, FamilyComponent component, PersonRecord person, Lineage lineage, HashSet<int> shown, int focus)
        {
            Color fill = person.Presence == Presence.Colony ? new Color(0.22f, 0.27f, 0.22f)
                       : person.Presence == Presence.Away ? new Color(0.2f, 0.2f, 0.24f)
                       : new Color(0.14f, 0.14f, 0.14f);
            bool isFocus = focus != 0 && person.Id == focus;
            PairCloseness pair = component.PerspectivePawnId==0 && focus != 0 && !isFocus ? registry.ClosenessBetween(focus, person.Id) : null;
            if (pair == null)
                Widgets.DrawBoxSolid(rect, fill);
            else
            {
                // While someone is chosen, the background of everyone else becomes a meter of how close they are to them.
                Widgets.DrawBoxSolid(rect, new Color(0.09f, 0.09f, 0.09f));
                if (pair.Value.HasValue)
                {
                    float share = Mathf.Clamp01(pair.Value.Value / 100f);
                    var inner = rect.ContractedBy(2f);
                    Widgets.DrawBoxSolid(new Rect(inner.x, inner.y, inner.width * share, inner.height), new Color(0.30f, 0.58f, 0.27f));
                }
            }
            if (Mouse.IsOver(rect)) Widgets.DrawBoxSolid(rect, new Color(1f, 1f, 1f, 0.08f));
            GUI.color = isFocus ? Color.white : (person.Presence == Presence.Colony ? new Color(0.7f, 0.75f, 0.65f) : new Color(0.4f, 0.4f, 0.4f));
            Widgets.DrawBox(rect, isFocus ? 3 : 1);
            GUI.color = Color.white;
            if (person.InColony && person.HouseholdId != 0)
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 5f, rect.height), HouseholdColor(person.HouseholdId));

            DrawPortrait(new Rect(rect.x + 8f, rect.y + (rect.height - PortraitH) / 2f, PortraitW, PortraitH), person, component);

            float textX = rect.x + 8f + PortraitW + 4f;
            float textW = rect.xMax - textX - 3f;
            bool dim = !person.InColony;
            if (dim) GUI.color = new Color(0.65f, 0.65f, 0.65f);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(textX, rect.y + 3f, textW, 22f), person.Name.Truncate(textW));
            GUI.color = Color.white;

            var stepKids = registry.StepChildrenOf(person.Id).Where(shown.Contains).ToList();
            var stepParents = registry.StepParentsOf(person.Id).Where(shown.Contains).ToList();
            string sub = person.Presence == Presence.Dead ? "\u2020 " + "WFL_Tree_Died".Translate()
                       : person.Presence == Presence.Away ? "WFL_Tree_Away".Translate().ToString()
                       : stepKids.Count > 0 ? "WFL_Tree_StepParent".Translate(FamilyUi.ShortNames(registry, stepKids)).ToString()
                       : stepParents.Count > 0 ? "WFL_Tree_StepChild".Translate(FamilyUi.ShortNames(registry, stepParents)).ToString()
                       : (person.SocialFamilyId==lineage.Id && person.PrimaryLineageId!=lineage.Id ? "WFL_House_Chosen".Translate().ToString() : person.PrimaryLineageId == lineage.Id ? "" : "WFL_Tree_MarriedIn".Translate().ToString());
            Text.Font = GameFont.Tiny;
            GUI.color = stepKids.Count > 0 || stepParents.Count > 0 ? new Color(0.6f, 0.75f, 0.9f) : new Color(0.6f, 0.6f, 0.6f);
            Widgets.Label(new Rect(textX, rect.y + 23f, textW, 32f), sub.Truncate(textW * 2f));
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

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
            string closeness = pair == null ? "" : "\n\n" + CloseText(registry, focus, person, pair);
            TooltipHandler.TipRegion(rect, person.FullName + " (" + FamilyUi.StatusText(person) + ")" + parents + partners + formers + steps + home + closeness
                + "\n" + "WFL_TreeClick".Translate().ToString() + (person.InColony ? " " + "WFL_TreeDoubleClick".Translate().ToString() : ""));
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && Mouse.IsOver(rect))
            {
                OnNodeClicked(person);
                Event.current.Use();
            }
        }

        /// <summary>A click chooses the person (and a second click on them clears it); a quick second click finds them in the colony.</summary>
        private void OnNodeClicked(PersonRecord person)
        {
            float now = Time.realtimeSinceStartup;
            bool doubleClick = lastClickedId == person.Id && now - lastClickTime < 0.4f;
            lastClickedId = person.Id;
            lastClickTime = now;
            if (doubleClick)
            {
                focusPersonId = person.Id;
                metersOff = false;
                if (person.InColony) FamilyUi.Jump(person.Id);
                return;
            }
            if (shownFocusId == person.Id)
            {
                // The person the meters are about was clicked again: hide the meters.
                focusPersonId = 0;
                metersOff = true;
            }
            else
            {
                focusPersonId = person.Id;
                metersOff = false;
            }
        }

        private static string CloseText(FamilyRegistry registry, int focus, PersonRecord other, PairCloseness pair)
        {
            string focusName = registry.GetPerson(focus)?.Name ?? "";
            if (!pair.Value.HasValue) return "WFL_TreeNoBond".Translate(focusName, other.Name).ToString();
            var lines = new List<string> { "WFL_TreeCloseness".Translate(focusName, Mathf.RoundToInt(pair.Value.Value)).ToString() };
            if (pair.FromFocus != null)
                lines.Add("WFL_TreeFeels".Translate(focusName, other.Name, Mathf.RoundToInt(pair.FromFocus.Attachment), Mathf.RoundToInt(pair.FromFocus.Trust), Mathf.RoundToInt(pair.FromFocus.Tension)).ToString());
            if (pair.ToFocus != null)
                lines.Add("WFL_TreeFeels".Translate(other.Name, focusName, Mathf.RoundToInt(pair.ToFocus.Attachment), Mathf.RoundToInt(pair.ToFocus.Trust), Mathf.RoundToInt(pair.ToFocus.Tension)).ToString());
            string lately = pair.ToFocus != null ? FamilyUi.ReasonsLine(pair.ToFocus) : (pair.FromFocus != null ? FamilyUi.ReasonsLine(pair.FromFocus) : "");
            if (lately.Length > 0) lines.Add(lately);
            return string.Join("\n", lines);
        }

        private void DrawTreeLegend(Rect view, ref float y, FamilyRegistry registry, List<int> householdIds)
        {
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
                Line(view, ref y, text, 28f);
                var leftOut = registry.LeftOutIn(household);
                if (leftOut.Count > 0) Line(view, ref y, "WFL_LeftOut".Translate(FamilyUi.NameList(registry, leftOut)), 28f, true);
            }
        }

        // ------------------------------------------------------------------ relationships

        private void DrawRelationships(Rect view, ref float y, FamilyRegistry registry, Lineage lineage)
        {
            var people = registry.MembersOf(lineage.Id).Concat(registry.AffiliatesOf(lineage.Id)).Where(p => p.InColony).Distinct().ToList();
            if (people.Count == 0)
            {
                Line(view, ref y, "WFL_NoHouseholds".Translate(), 0f, true);
                return;
            }
            int acting=FamilyComponent.Current?.PerspectivePawnId??0;
            var subject = acting>0?registry.GetPerson(acting):people.FirstOrDefault(p => p.Id == relationsPersonId) ?? people[0];
            if(subject==null) return;
            relationsPersonId = subject.Id;

            if (ActionButton(new Rect(0f, y, 220f, 26f), "WFL_RelPick".Translate(subject.FullName),acting==0))
                Find.WindowStack.Add(new FloatMenu(people.Select(p => { var id = p.Id; return new FloatMenuOption(p.FullName, () => relationsPersonId = id); }).ToList()));
            y += 32f;
            foreach (var rift in registry.OpenRiftsOf(subject.Id))
            {
                int otherId = rift.A == subject.Id ? rift.B : rift.A;
                GUI.color = new Color(0.9f, 0.55f, 0.45f);
                Line(view, ref y, "WFL_RiftLine".Translate(FamilyUi.NameOf(registry, otherId), ("WFL_RiftCause_" + rift.Cause).Translate(), FamilyUi.Date(rift.StartTick)), 0f);
                GUI.color = Color.white;
            }

            string[] headers = { "WFL_Bond_Attachment", "WFL_Bond_Trust", "WFL_Bond_Tension", "WFL_Bond_Familiarity" };
            float barsX = view.width - 4f * 78f;
            Text.Anchor = TextAnchor.MiddleLeft;
            for (int i = 0; i < headers.Length; i++)
                Widgets.Label(new Rect(barsX + i * 78f, y, 76f, 22f), headers[i].Translate());
            Text.Anchor = TextAnchor.UpperLeft;
            y += 24f;

            var bonds = registry.BondsFrom(subject.Id);
            if (bonds.Count == 0) Line(view, ref y, "WFL_NoBonds".Translate(), 0f, true);
            foreach (var bond in bonds)
            {
                var other = registry.GetPerson(bond.To);
                if (other == null) continue;
                var kind = registry.KindOf(subject, other) ?? BondKind.Acquaintance;
                var row = new Rect(0f, y, view.width, 24f);
                Widgets.DrawHighlightIfMouseover(row);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(row.x, row.y, barsX - 90f, row.height), other.FullName + (other.Presence == Presence.Dead ? " (" + "WFL_StatusDead".Translate() + ")" : ""));
                GUI.color = Color.gray;
                Widgets.Label(new Rect(barsX - 88f, row.y, 86f, row.height), ("WFL_BondKind_" + kind).Translate());
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                Bar(new Rect(barsX, row.y + 5f, 70f, 14f), bond.Attachment, new Color(0.35f, 0.7f, 0.45f));
                Bar(new Rect(barsX + 78f, row.y + 5f, 70f, 14f), bond.Trust, new Color(0.35f, 0.55f, 0.8f));
                Bar(new Rect(barsX + 156f, row.y + 5f, 70f, 14f), bond.Tension, new Color(0.8f, 0.4f, 0.35f));
                Bar(new Rect(barsX + 234f, row.y + 5f, 70f, 14f), bond.Familiarity, new Color(0.65f, 0.6f, 0.4f));
                TooltipHandler.TipRegion(row, "WFL_BondTip".Translate(subject.Name, other.Name,
                    Mathf.RoundToInt(bond.Attachment), Mathf.RoundToInt(bond.Trust), Mathf.RoundToInt(bond.Tension), Mathf.RoundToInt(bond.Familiarity)));
                y += 26f;
                string lately = FamilyUi.ReasonsLine(bond);
                if (lately.Length > 0)
                {
                    GUI.color = Color.gray;
                    Widgets.Label(new Rect(14f, y - 4f, view.width - 14f, 20f), lately);
                    GUI.color = Color.white;
                    y += 18f;
                }
            }
        }

        private static void Bar(Rect rect, float value, Color color)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.15f, 0.15f, 0.15f, 0.9f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value / 100f), rect.height), color);
        }

        // ------------------------------------------------------------------ households

        private void DrawHouseholds(Rect view, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage lineage)
        {
            var households = registry.HouseholdsOfLineage(lineage.Id);
            if (households.Count == 0) Line(view, ref y, "WFL_NoHouseholds".Translate(), 0f, true);
            foreach (var household in households)
            {
                Heading(view, ref y, "WFL_HouseholdN".Translate(household.Id));
                foreach (var residentId in household.ResidentIds.ToList())
                {
                    var p = registry.GetPerson(residentId);
                    if (p == null) continue;
                    var row = new Rect(10f, y, view.width - 10f, 26f);
                    Widgets.DrawHighlightIfMouseover(row);
                    Text.Anchor = TextAnchor.MiddleLeft;
                    Widgets.Label(new Rect(row.x, row.y, row.width - 110f, row.height), p.FullName + (p.Juvenile ? " (" + "WFL_Child".Translate() + ")" : ""));
                    Text.Anchor = TextAnchor.UpperLeft;
                    if (ActionButton(new Rect(row.xMax - 100f, row.y + 1f, 100f, 24f), "WFL_Move".Translate(),component.PerspectivePawnId==0 || component.PerspectivePawnId==p.Id || registry.CanManageHouse(component.PerspectivePawnId,lineage.Id)))
                        OpenMoveMenu(component, registry, p);
                    y += 28f;
                }
            }
        }

        private static void OpenMoveMenu(FamilyComponent component, FamilyRegistry registry, PersonRecord person)
        {
            var options = new List<FloatMenuOption>();
            foreach (var household in registry.Households.OrderBy(h => h.Id))
            {
                if (household.Id == person.HouseholdId) continue;
                var h = household;
                string label = "WFL_MoveTo".Translate(FamilyUi.NameList(registry, h.ResidentIds)).ToString();
                options.Add(new FloatMenuOption(label, () => component.MovePerson(person.Id, h.Id)));
            }
            options.Add(new FloatMenuOption("WFL_MoveToNew".Translate(), () => component.MovePerson(person.Id, 0)));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ------------------------------------------------------------------ the family's area

        private void DrawZone(Rect view, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage lineage)
        {
            bool savedEnabled=GUI.enabled;
            int acting=FamilyComponent.Current?.PerspectivePawnId??0;
            GUI.enabled=savedEnabled&&(acting==0 || registry.CanManageHouse(acting,lineage.Id));
            try
            {

            Heading(view, ref y, "WFL_Pantry_Title".Translate());
            Line(view, ref y, "WFL_Pantry_Food".Translate(lineage.FoodReserveDays.ToString("0.0")));
            lineage.FoodReserveDays = Widgets.HorizontalSlider(new Rect(10,y,view.width-20,24),lineage.FoodReserveDays,.5f,3f); y+=30;
            Line(view, ref y, "WFL_Pantry_Fuel".Translate(lineage.FuelReserveDays.ToString("0.0")));
            lineage.FuelReserveDays = Widgets.HorizontalSlider(new Rect(10,y,view.width-20,24),lineage.FuelReserveDays,.5f,3f); y+=30;
            Line(view, ref y, "WFL_Pantry_Medicine".Translate(lineage.MedicineReserve));
            lineage.MedicineReserve = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(10,y,view.width-20,24),lineage.MedicineReserve,0,10)); y+=30;
            Widgets.CheckboxLabeled(new Rect(10,y,view.width-20,30),"WFL_Pantry_Emergency".Translate(),ref lineage.EmergencySharing); y+=36;
            Widgets.CheckboxLabeled(new Rect(10,y,view.width-20,44),"WFL_Pantry_PublicFunds".Translate(),ref lineage.UsePublicBarterFunds); y+=50;
            var current = lineage.ZoneAreaId == 0 ? null : FamilyZones.AreaFor(lineage);
            y += 4f;
            if (current == null)
                Line(view, ref y, "WFL_ZoneNone".Translate(), 10f);
            else
            {
                Widgets.DrawBoxSolid(new Rect(10f, y + 4f, 14f, 14f), current.Color);
                Line(view, ref y, "WFL_ZoneCurrent".Translate(current.Label, FamilyZones.RestrictedCount(component, lineage, current), registry.LivingCount(lineage.Id, Presence.Colony)), 30f);
            }
            y += 6f;
            if (FamilyZones.HomeMap() == null) return;
            if (HouseButton(new Rect(10f, y, 300f, 28f), "WFL_ZoneCreate".Translate(),registry,lineage)) { FamilyZones.StartPainting(); Close(); }
            y += 34f;
            // A family zone already painted can be handed to this family from here as well as from the zone itself.
            var free = Find.Maps.SelectMany(m => m.zoneManager.AllZones).OfType<Zone_Family>().Where(z => z.areaId == 0 || z.areaId != lineage.ZoneAreaId).ToList();
            if (free.Count > 0 && HouseButton(new Rect(10f, y, 300f, 28f), "WFL_ZoneChoose".Translate(),registry,lineage))
            {
                var options = free.Select(z => { var zone = z; return new FloatMenuOption(zone.label, () => zone.SetFamily(component, lineage)); }).ToList();
                Find.WindowStack.Add(new FloatMenu(options));
            }
            if (free.Count > 0) y += 34f;
            if (current != null && HouseButton(new Rect(10f, y, 300f, 28f), "WFL_ZoneRemove".Translate(),registry,lineage))
            {
                foreach (var z in FamilyZones.ZonesOf(lineage).ToList()) z.areaId = 0;
                FamilyZones.Release(component, lineage, current);
            }
            if (current != null) y += 34f;
                    }
            finally {GUI.enabled=savedEnabled;}
        }

        // ------------------------------------------------------------------ culture and traditions

        private static void DrawCulture(Rect view, ref float y, FamilyRegistry registry, Lineage lineage)
        {
            Heading(view, ref y, "WFL_Culture".Translate());
            var values = registry.Values(lineage.Id);
            if (values.Count == 0) Line(view, ref y, "WFL_NoValues".Translate(), 10f, true);
            else Line(view, ref y, "WFL_ValuesLine".Translate(string.Join(", ", values.Select(FamilyUi.DimName))), 10f);

            var legacies = registry.Legacies(lineage.Id);
            if (legacies.Count > 0)
                Line(view, ref y, "WFL_LegacyLine".Translate(string.Join(", ", legacies)), 10f);

            var memories = registry.MemoriesOf(lineage.Id);
            if (memories.Count > 0)
            {
                Heading(view, ref y, "WFL_Memories".Translate());
                foreach (var m in memories.OrderByDescending(x => x.Weight).ThenByDescending(x => x.Tick).Take(8))
                    Line(view, ref y, FamilyUi.Date(m.Tick) + " - " + ("WFL_Mem_" + m.Kind).Translate(FamilyUi.NameOf(registry, m.A), FamilyUi.NameOf(registry, m.B)), 10f);
            }
        }

        private void DrawTraditions(Rect view, ref float y, FamilyRegistry registry, Lineage lineage)
        {
            bool savedEnabled=GUI.enabled;
            int acting=FamilyComponent.Current?.PerspectivePawnId??0;
            GUI.enabled=savedEnabled&&(acting==0 || registry.CanManageHouse(acting,lineage.Id));
            try
            {

            if (lineage.Traditions.Count == 0)
            {
                Line(view, ref y, "WFL_NoTraditions".Translate(), 0f, true);
                return;
            }
            int now = Find.TickManager.TicksGame;
            foreach (var t in lineage.Traditions.ToList())
            {
                var def = TraditionCatalog.All.FirstOrDefault(d => d.Id == t.Id);
                if (def == null) continue;
                var row = new Rect(0f, y, view.width, 30f);
                Widgets.DrawHighlightIfMouseover(row);
                Text.Anchor = TextAnchor.MiddleLeft;
                string last = t.LastHeldTick > 0 ? "WFL_TraditionLast".Translate(FamilyUi.Date(t.LastHeldTick), t.TimesHeld).ToString() : "WFL_TraditionNever".Translate().ToString();
                Widgets.Label(new Rect(row.x + 6f, row.y, 200f, row.height), FamilyUi.TraditionNameById(t.Id));
                GUI.color = Color.gray;
                Widgets.Label(new Rect(row.x + 210f, row.y, row.width - 210f - 250f, row.height), last);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                bool muted = t.Muted;
                if (HouseButton(new Rect(row.xMax - 240f, row.y + 2f, 110f, 26f), muted ? "WFL_Unmute".Translate() : "WFL_Mute".Translate(),registry,lineage))
                    registry.SetTraditionMuted(lineage.Id, t.Id, !muted);
                if (!muted && HouseButton(new Rect(row.xMax - 124f, row.y + 2f, 120f, 26f), "WFL_HoldNow".Translate(),registry,lineage))
                {
                    registry.RequestGathering(t.Id, lineage.Id, 0, now, 0, 3 * ActivityTuning.TicksPerDay);
                    Messages.Message("WFL_HoldNowQueued".Translate(FamilyUi.TraditionNameById(t.Id)), MessageTypeDefOf.NeutralEvent, false);
                }
                y += 32f;
            }
                    }
            finally {GUI.enabled=savedEnabled;}
        }

        // ------------------------------------------------------------------ moments and standing between families

        private void DrawMoments(Rect view, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage lineage)
        {
            int perspective=component.PerspectivePawnId;
            Heading(view, ref y, "WFL_Ceremonies_Title".Translate());
            var plans=registry.GatheringRequests.Where(r=>r.LineageId==lineage.Id&&r.Ceremony!=CeremonyKind.Tradition).OrderBy(r=>r.NotBefore).Take(6).ToList();
            foreach(var plan in plans)
                Line(view,ref y,"WFL_Ceremony_Planned".Translate(("WFL_Ceremony_"+plan.Ceremony).Translate(),FamilyUi.Date(plan.NotBefore),
                    CeremonyInfluences(plan.Influences)),0f,false);
            foreach(var ceremony in registry.Ceremonies.Where(c=>c.LineageId==lineage.Id&&c.Kind!=CeremonyKind.Tradition).OrderByDescending(c=>c.CompletedTick).Take(4))
                Line(view,ref y,"WFL_Ceremony_Recent".Translate(FamilyUi.Date(ceremony.CompletedTick),("WFL_Ceremony_"+ceremony.Kind).Translate(),
                    ceremony.Attendees,ceremony.Moments),0f,true);
            if(plans.Count==0&&!registry.Ceremonies.Any(c=>c.LineageId==lineage.Id&&c.Kind!=CeremonyKind.Tradition))
                Line(view,ref y,"WFL_Ceremony_None".Translate(),0f,true);
            var publicFigures=registry.Prestige.Where(p=>registry.FamilyOf(p.PersonId)==lineage.Id&&p.Value>0)
                .OrderByDescending(p=>p.Value).Take(5).ToList();
            if(publicFigures.Count>0)
                Line(view,ref y,"WFL_Ceremony_Prestige".Translate(string.Join(", ",publicFigures.Select(p=>FamilyUi.NameOf(registry,p.PersonId)+" "+p.Value.ToString("0.0")))),0f,true);
            Heading(view, ref y, "WFL_Agency_Title".Translate());
            foreach (var person in registry.MembersOf(lineage.Id).Where(p => p.InColony && !p.Juvenile && (perspective==0 || p.Id==perspective)).Take(24))
            {
                var belonging = registry.AssessBelonging(person.Id);
                var agency = registry.AgencyOf(person.Id);
                var detail = new List<string>();
                if (agency == null) detail.Add("WFL_Agency_NoEvidence".Translate().ToString());
                else
                {
                    detail.Add("WFL_Agency_Evidence".Translate(agency.Included, agency.Excluded, ("WFL_Ev_" + agency.LastEvent.Replace('-', '_') + "_Title").Translate(), FamilyUi.Date(agency.LastEvidenceTick)).ToString());
                    if (agency.Goal != PersonalGoal.None) detail.Add("WFL_Agency_Goal".Translate(("WFL_Goal_" + agency.Goal).Translate(), Mathf.RoundToInt(agency.Progress * 100), agency.Active ? "WFL_Agency_Active".Translate() : agency.Progress >= 1 ? "WFL_Agency_Achieved".Translate() : "WFL_Agency_Abandoned".Translate()).ToString());
                }
                LineTip(view, ref y, "WFL_Agency_Belonging".Translate(FamilyUi.NameOf(registry, person.Id), belonging.TrustedConnections, belonging.StrainedConnections), string.Join("\n", detail));
            }
            Heading(view, ref y, "WFL_Attention_Title".Translate());
            foreach (var child in registry.MembersOf(lineage.Id).Where(p => p.InColony && p.Juvenile).Take(24))
                foreach (var giver in child.ParentIds.Where(id=>perspective==0 || id==perspective).Take(4))
                {
                    var comparison = registry.CompareAttention(giver,child.Id,Activities.Now);
                    if (!comparison.Unequal) continue;
                    Line(view, ref y, "WFL_Attention_Comparison".Translate(FamilyUi.NameOf(registry,giver), FamilyUi.NameOf(registry,child.Id), (comparison.ChildTicks / 2500f).ToString("0.0"), FamilyUi.NameOf(registry,comparison.OtherChild), (comparison.OtherTicks / 2500f).ToString("0.0")),0f,true);
                }
            Heading(view, ref y, "WFL_Moments_Standing".Translate());
            var ties = registry.TiesOf(lineage.Id);
            if (ties.Count == 0) Line(view, ref y, "WFL_Moments_NoTies".Translate(), 0f, true);
            foreach (var tie in ties)
            {
                int otherId = tie.LineageA == lineage.Id ? tie.LineageB : tie.LineageA;
                string other = FamilyUi.LineageName(registry, otherId);
                var why = new List<string>();
                if (perspective > 0 && tie.Grievance >= 10f && !registry.Incidents.Any(i => i.ActorId == tie.WrongedBy && i.VictimId == tie.WrongedPerson && registry.KnowsIncident(perspective, i)))
                    why.Add("WFL_Perspective_UnknownCause".Translate().ToString());
                else if (tie.Grievance >= 10f && tie.WrongedBy != 0)
                    why.Add("WFL_TieCause".Translate(FamilyUi.NameOf(registry, tie.WrongedPerson), FamilyUi.NameOf(registry, tie.WrongedBy), FamilyUi.Date(tie.GrievanceTick)).ToString());
                else if (tie.Grievance >= 10f)
                    why.Add("WFL_TieCauseMany".Translate().ToString());
                if (tie.Grievance >= 20f && registry.GatheringRequests.Any(r => r.Kind == "reconciliation" && (r.LineageId == lineage.Id || r.LineageId == otherId)))
                    why.Add("WFL_TieMending".Translate().ToString());
                LineTip(view, ref y, "WFL_TieLine".Translate(other, ("WFL_TieState_" + tie.State).Translate(), Mathf.RoundToInt(tie.Goodwill), Mathf.RoundToInt(tie.Grievance)), string.Join("\n", why));
            }

            var relevant = new HashSet<int>(registry.MembersOf(lineage.Id).Select(p => p.Id));
            foreach (var a in lineage.Affiliations) relevant.Add(a.PersonId);

            Heading(view, ref y, "WFL_Stories_Incidents".Translate());
            var incidents = registry.Incidents.Where(i => (i.ActorFamily == lineage.Id || i.VictimFamily == lineage.Id) && registry.KnowsIncident(perspective,i)).OrderByDescending(i => i.Tick).Take(24).ToList();
            if (incidents.Count == 0) Line(view, ref y, "WFL_Stories_Empty".Translate(), 0f, true);
            foreach (var i in incidents)
            {
                string actor = i.ActorId == 0 ? "WFL_Stories_Unknown".Translate().ToString() : FamilyUi.NameOf(registry, i.ActorId);
                var more = new List<string>();
                if (perspective == 0) more.Add("WFL_Stories_Known".Translate(string.Join(", ", i.KnownBy.Take(16).Select(id => FamilyUi.NameOf(registry, id)))).ToString());
                if (i.RepairSteps > 0) more.Add("WFL_Reparation_Progress".Translate(i.RepairSteps).ToString());
                if (i.Context != IncidentContext.None) more.Add("WFL_Stories_Context".Translate(string.Join(", ", new[] { IncidentContext.Combat, IncidentContext.MentalBreak, IncidentContext.Accident, IncidentContext.Planned }.Where(flag => (i.Context & flag) != 0).Select(flag => ("WFL_Context_" + flag).Translate().ToString()))).ToString());
                string incidentLine = perspective == 0
                    ? "WFL_Stories_Incident".Translate(FamilyUi.Date(i.Tick), ("WFL_Incident_" + i.Kind).Translate(), actor, FamilyUi.NameOf(registry, i.VictimId), i.KnownBy.Count, i.Resolved ? "WFL_Stories_Resolved".Translate() : "WFL_Stories_Open".Translate(), i.Id).ToString()
                    : "WFL_Stories_PrivateIncident".Translate(i.Id, FamilyUi.Date(i.Tick), ("WFL_Incident_" + i.Kind).Translate(), actor, FamilyUi.NameOf(registry, i.VictimId), i.Resolved ? "WFL_Stories_Resolved".Translate() : "WFL_Stories_Open".Translate()).ToString();
                LineTip(view, ref y, incidentLine, string.Join("\n", more));
                if (!i.Resolved && i.VictimFamily == lineage.Id)
                {
                    if (HouseButton(new Rect(14, y, 180, 26), "WFL_Stories_Forgive".Translate(),registry,lineage)) registry.ForgiveIncident(i.Id, lineage.Id,Activities.Now);
                    y += 30;
                }
            }
            Heading(view, ref y, "WFL_Stories_Plots".Translate());
            foreach (var p in registry.Plots.Where(p => (relevant.Contains(p.OrganizerId) || relevant.Contains(p.TargetId)) && registry.KnowsPlot(perspective,p)).OrderByDescending(p => p.CreatedTick).Take(12))
                Line(view, ref y, "WFL_Stories_Plot".Translate(FamilyUi.NameOf(registry, p.OrganizerId), FamilyUi.NameOf(registry, p.TargetId), ("WFL_PlotState_" + p.State).Translate(), p.IncidentId, p.Recruits.Count));
            Heading(view, ref y, "WFL_Stories_Supplies".Translate());
            Line(view, ref y, "WFL_Stories_Claims".Translate(registry.Claims.Count(c => c.HolderFamily == lineage.Id)), 0f, true);
            foreach (var supply in registry.Requests.Where(r => (relevant.Contains(r.RequesterId) || relevant.Contains(r.DonorId)) && (perspective==0 || r.RequesterId==perspective || r.DonorId==perspective)).OrderByDescending(r => r.CreatedTick).Take(16))
                Line(view, ref y, "WFL_Stories_Request".Translate(FamilyUi.NameOf(registry, supply.RequesterId), ("WFL_SupplyApproach_" + supply.Approach).Translate(), supply.Count, supply.Definition, ("WFL_SupplyState_" + supply.State).Translate()));

            Heading(view, ref y, "WFL_Moments_Promises".Translate());
            var promises = registry.Commitments.Where(p => (relevant.Contains(p.FromId) || relevant.Contains(p.ToId)) && p.State != 3 && (perspective==0 || p.FromId==perspective || p.ToId==perspective)).OrderByDescending(p => p.CreatedTick).Take(12).ToList();
            if (promises.Count == 0) Line(view, ref y, "WFL_Moments_NoPromises".Translate(), 0f, true);
            foreach (var p in promises)
                Line(view, ref y, "WFL_PromiseLine_" + p.State, 0f, false, FamilyUi.NameOf(registry, p.FromId), FamilyUi.NameOf(registry, p.ToId), FamilyUi.Date(p.DueTick));

            Heading(view, ref y, "WFL_Moments_Recent".Translate());
            int shown = 0;
            for (int i = registry.EventLog.Count - 1; i >= 0 && shown < 60; i--)
            {
                var e = registry.EventLog[i];
                if (!relevant.Contains(e.A) && !relevant.Contains(e.B)) continue;
                if(perspective>0 && e.A!=perspective && e.B!=perspective && e.Third!=perspective) continue;
                var def = EventCatalog.Get(e.EventId) ?? TraditionMoments.Get(e.EventId) ?? CeremonyMoments.Get(e.EventId);
                if (def == null) continue;
                shown++;
                string marker = e.Weight == EventWeight.Defining ? "* " : (e.Weight == EventWeight.Meaningful ? "- " : "  ");
                string text = SocialMoments.Describe(def, e.Tier, FamilyUi.NameOf(registry, e.A), FamilyUi.NameOf(registry, e.B), e.Third != 0 ? FamilyUi.NameOf(registry, e.Third) : "");
                Line(view, ref y, marker + FamilyUi.Date(e.Tick) + " - " + ("WFL_Ev_" + e.EventId.Replace('-', '_') + "_Title").Translate() + ": " + text, 0f, e.Weight == EventWeight.Ambient);
            }
            if (shown == 0) Line(view, ref y, "WFL_Moments_None".Translate(), 0f, true);
        }

        private static string CeremonyInfluences(CeremonyInfluence influences)
        {
            if(influences==CeremonyInfluence.None)return "WFL_Ceremony_Influence_None".Translate();
            return string.Join(", ",Enum.GetValues(typeof(CeremonyInfluence)).Cast<CeremonyInfluence>()
                .Where(i=>i!=CeremonyInfluence.None&&influences.HasFlag(i)).Select(i=>("WFL_Ceremony_Influence_"+i).Translate().ToString()));
        }

        private static void Line(Rect view, ref float y, string key, float indent, bool dim, params string[] args)
        {
            Line(view, ref y, key.Translate(args.Select(a => (NamedArgument)a).ToArray()).ToString(), indent, dim);
        }

        // ------------------------------------------------------------------ history

        private void DrawHistory(Rect view, ref float y, FamilyRegistry registry, Lineage lineage)
        {
            var relevant = new HashSet<int>(registry.MembersOf(lineage.Id).Select(p => p.Id));
            foreach (var a in lineage.Affiliations) relevant.Add(a.PersonId);

            bool any = false;
            int shown = 0;
            for (int i = registry.Chronicle.Count - 1; i >= 0 && shown < 150; i--)
            {
                var e = registry.Chronicle[i];
                bool about = e.Kind == FamilyEventKind.RecordsBegan ? true
                    : e.Kind == FamilyEventKind.LineageFounded || e.Kind == FamilyEventKind.LineageAffiliated || e.Kind == FamilyEventKind.SocialBranch || e.Kind == FamilyEventKind.SocialMembership ? e.B == lineage.Id
                    : e.Kind == FamilyEventKind.TraditionAdopted || e.Kind == FamilyEventKind.TraditionAbandoned || e.Kind == FamilyEventKind.TraditionHeld ? e.A == lineage.Id
                    : relevant.Contains(e.A) || (e.Kind == FamilyEventKind.PartnershipFormed || e.Kind == FamilyEventKind.PartnershipEnded || e.Kind == FamilyEventKind.Birth || e.Kind == FamilyEventKind.Anniversary || e.Kind == FamilyEventKind.RiftOpened || e.Kind == FamilyEventKind.RiftResolved) && relevant.Contains(e.B);
                if (!about) continue;
                any = true;
                shown++;
                Line(view, ref y, FamilyUi.Date(e.Tick) + " - " + FamilyUi.Describe(registry, e));
            }
            if (!any) Line(view, ref y, "WFL_NoHistory".Translate(), 0f, true);
        }
    }
}
