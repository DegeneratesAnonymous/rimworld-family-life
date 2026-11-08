using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The Summary tab: a dashboard of cards, not a document. Who the family is, what needs attention, what is coming, the latest of its story, and the
    /// people. Every concern is a link to where it can be dealt with.
    /// </summary>
    public partial class Window_Families
    {
        private void DrawSummary(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            FamilyKit.Columns(v, out var left, out var right, out bool wide, 0.52f);
            var ys = new[] { y, y };
            int rc = wide ? 1 : 0;
            var colL = wide ? left : new Rect(0f, 0f, v.width, 0f);
            var colR = wide ? right : colL;

            if (wide)
            {
                SummaryAttention(colL, ref ys[0], registry);
                SummaryUpcoming(colL, ref ys[0], component, registry);
                SummaryStatus(colR, ref ys[1], component, registry);
                SummaryStory(colR, ref ys[1], registry);
            }
            else
            {
                SummaryStatus(colL, ref ys[0], component, registry);
                SummaryAttention(colL, ref ys[0], registry);
                SummaryUpcoming(colL, ref ys[0], component, registry);
                SummaryStory(colL, ref ys[0], registry);
            }
            y = Mathf.Max(ys[0], ys[rc]);
            SummaryPeople(new Rect(0f, 0f, v.width, 0f), ref y, component, registry);
        }

        private void SummaryStatus(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            var card = FamilyKit.BeginCard(col, ref y, "s.status", "WFL_FW_Card_Status".Translate());
            var cv = card.View;
            float cy = 0f;
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_HouseHead".Translate(), view.Head != 0 ? FamilyUi.NameOf(registry, view.Head) : "WFL_FW_NoHead".Translate().ToString());
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_Steward".Translate(), view.Steward != 0 ? FamilyUi.NameOf(registry, view.Steward) : "WFL_FW_NoSteward".Translate().ToString());
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_FoundedLabel".Translate(), "WFL_FoundedBy".Translate(FamilyUi.NameOf(registry, house.FounderId), FamilyText.Date(house.FoundedTick)).ToString());
            if (!view.Insider)
            {
                cy += 4f;
                cy += FamilyKit.Label(cv, cy, "WFL_FW_NotKnownTo".Translate(view.NameOfPerspective(registry)), 0f, true);
                FamilyKit.EndCard(card, cy, ref y);
                return;
            }
            var home = HomeFor(component);
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_HomeArea".Translate(), home.AreaLabel.Length > 0 ? "WFL_FW_AreaCovers".Translate(home.AreaLabel, home.Covered, home.Total).ToString() : "WFL_ZoneNone".Translate().ToString());

            var s = house.Situation;
            bool assessed = s != null && s.EvaluatedTick > 0;
            cy += 4f;
            if (assessed)
            {
                string tier = BondTuning.CohesionTier(s.Cohesion);
                DrawStatusBar(cv, ref cy, "WFL_FW_Cohesion".Translate(), s.Cohesion, FamilyKit.Closeness, ("WFL_Tier_" + tier).Translate().ToString().CapitalizeFirst(), "WFL_CohesionHelp".Translate());
                DrawStatusBar(cv, ref cy, "WFL_FW_Standing".Translate(), s.Influence, FamilyKit.Familiarity, Mathf.RoundToInt(s.Influence).ToString(), "WFL_FW_StandingTip".Translate(Mathf.RoundToInt(s.Power)));
            }
            else cy += FamilyKit.Label(cv, cy, "WFL_Ambition_Assessing".Translate(), 0f, true);

            var ambition = house.Ambition;
            if (assessed && ambition != null && ambition.Primary != HouseAmbitionKind.None)
            {
                cy += 2f;
                var area = new Rect(0f, cy, cv.width, 44f);
                Widgets.DrawBoxSolid(area, new Color(0.18f, 0.16f, 0.09f));
                GUI.color = FamilyKit.Gold; Widgets.DrawBox(area, 1); GUI.color = Color.white;
                Widgets.Label(new Rect(8f, cy + 2f, cv.width - 16f, 22f), "WFL_FW_AmbitionIs".Translate(("WFL_Ambition_" + ambition.Primary).Translate()));
                GUI.color = FamilyKit.Dim; Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(8f, cy + 23f, cv.width - 16f, 18f), ("WFL_Need_" + ambition.PrimaryReason).Translate().ToString());
                Text.Font = GameFont.Small; GUI.color = Color.white;
                TooltipHandler.TipRegion(area, "WFL_Ambition_Metrics".Translate(s.Sustenance.ToString("0"), s.LivingSpace.ToString("0"), s.Safety.ToString("0"), s.Continuity.ToString("0"), s.Capability.ToString("0"), s.Cohesion.ToString("0"), s.Influence.ToString("0"), s.Independence.ToString("0")));
                if (Widgets.ButtonInvisible(area)) State.Navigate(FamilyTab.Leadership);
                cy += 48f;
            }
            FamilyKit.EndCard(card, cy, ref y);
        }

        private static void DrawStatusBar(Rect cv, ref float cy, string label, float value, Color color, string valueText, string tip)
        {
            GUI.color = FamilyKit.Dim;
            Widgets.Label(new Rect(0f, cy, 90f, 22f), label);
            GUI.color = Color.white;
            FamilyKit.MetricBar(new Rect(94f, cy + 5f, cv.width - 94f - 84f, 12f), value, color, tip);
            Widgets.Label(new Rect(cv.width - 80f, cy, 80f, 22f), valueText);
            cy += 24f;
        }

        private void SummaryAttention(Rect col, ref float y, FamilyRegistry registry)
        {
            string title = view.Alerts.Count > 0 ? "WFL_FW_Card_Attention".Translate() + "  (" + view.Alerts.Count + ")" : "WFL_FW_Card_Attention".Translate().ToString();
            var card = FamilyKit.BeginCard(col, ref y, "s.attention", title, null, view.Alerts.Any(a => a.Urgent));
            var cv = card.View;
            float cy = 0f;
            if (!view.Insider) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NotKnownTo".Translate(view.NameOfPerspective(registry)));
            else if (view.Alerts.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NothingAttention".Translate());
            foreach (var alert in view.Alerts.Take(8))
                if (FamilyKit.AlertRow(cv, ref cy, FamilyText.AlertText(registry, alert), alert.Severity, FamilyText.AlertTip(alert) + "\n\n" + "WFL_FW_ClickToOpen".Translate(("WFL_FW_Tab_" + alert.Tab).Translate())))
                    State.Go(alert);
            FamilyKit.EndCard(card, cy, ref y);
        }

        private void SummaryUpcoming(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var card = FamilyKit.BeginCard(col, ref y, "s.upcoming", "WFL_FW_Card_Upcoming".Translate());
            var cv = card.View;
            float cy = 0f;
            var items = UpcomingFor(component, registry);
            if (items.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NothingUpcoming".Translate());
            foreach (var u in items.Take(6)) DrawUpcomingLine(cv, ref cy, registry, u);
            if (items.Count > 0 && FamilyKit.Action(new Rect(0f, cy + 2f, 150f, 24f), "WFL_FW_SeeAllUpcoming".Translate(), true)) State.Navigate(FamilyTab.Events, (int)EventsSub.Upcoming);
            if (items.Count > 0) cy += 30f;
            FamilyKit.EndCard(card, cy, ref y);
        }

        private void DrawUpcomingLine(Rect cv, ref float cy, FamilyRegistry registry, UpcomingItem u)
        {
            var row = new Rect(0f, cy, cv.width, 24f);
            Widgets.DrawHighlightIfMouseover(row);
            string type = FamilyText.UpcomingType(u);
            float bw = FamilyKit.BadgeWidth(type);
            FamilyKit.Badge(new Rect(0f, cy + 3f, bw, 18f), type, u.Overdue ? FamilyKit.Warn : FamilyKit.Trust);
            float whenW = 118f;
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(bw + 8f, cy, cv.width - bw - 8f - whenW, 24f), FamilyText.UpcomingText(registry, u).Truncate(cv.width - bw - 8f - whenW));
            GUI.color = u.Overdue ? FamilyKit.Warn : FamilyKit.Dim;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(cv.width - whenW, cy, whenW, 24f), FamilyText.UpcomingWhen(u).Truncate(whenW));
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            cy += 26f;
        }

        private void SummaryStory(Rect col, ref float y, FamilyRegistry registry)
        {
            var card = FamilyKit.BeginCard(col, ref y, "s.story", "WFL_FW_Card_Story".Translate());
            var cv = card.View;
            float cy = 0f;
            if (!view.Insider) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NotKnownTo".Translate(view.NameOfPerspective(registry)));
            else if (view.Story.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NothingStory".Translate());
            foreach (var e in view.Story)
            {
                string body = FamilyText.MomentBody(registry, e);
                if (body == null) continue;
                FamilyKit.EventCard(cv, ref cy, FamilyText.Date(e.Tick), FamilyText.MomentTitle(e), FamilyKit.Trust, FamilyText.Participants(registry, new TimelineEntry { Kind = TimelineKind.Moment, Moment = e }), body, null, e.Weight == EventWeight.Defining);
            }
            if (FamilyKit.Action(new Rect(0f, cy + 2f, 170f, 24f), "WFL_FW_ViewTimeline".Translate(), true)) State.Navigate(FamilyTab.Events, (int)EventsSub.Recent);
            cy += 30f;
            FamilyKit.EndCard(card, cy, ref y);
        }

        private void SummaryPeople(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var card = FamilyKit.BeginCard(col, ref y, "s.people", "WFL_FW_Card_People".Translate());
            var cv = card.View;
            float cy = 0f;
            var model = People(component);
            var colony = model.Rows.Where(r => r.Person.InColony).ToList();
            if (colony.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_NoHouseholds".Translate());
            float x = 0f; const float slot = 54f;
            int fit = Mathf.Max(1, Mathf.FloorToInt(cv.width / slot));
            int shown = Mathf.Min(colony.Count, colony.Count > fit ? fit - 1 : fit);
            for (int i = 0; i < shown; i++)
            {
                var row = colony[i];
                var r = new Rect(x, cy, slot - 4f, 74f);
                Widgets.DrawHighlightIfMouseover(r);
                DrawPortrait(new Rect(r.x + 3f, r.y + 2f, 40f, 48f), row.Person, component);
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(r.x - 2f, r.y + 52f, r.width + 4f, 18f), row.Person.Name.Truncate(r.width + 4f));
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                if (row.Roles.Count > 0) RoleIcons.Draw(new Rect(r.xMax - 18f, r.y, 16f, 16f), row.Roles[0].Role, true);
                TooltipHandler.TipRegion(r, row.Person.FullName + (row.Relation.Length > 0 ? "\n" + row.Relation : ""));
                if (Widgets.ButtonInvisible(r)) { State.SelectedPerson = row.Person.Id; State.Navigate(FamilyTab.People); }
                x += slot;
            }
            if (colony.Count > shown)
            {
                var more = new Rect(x, cy + 10f, slot - 4f, 30f);
                if (FamilyKit.Action(more, "+" + (colony.Count - shown), true, null, "WFL_FW_SeeAllPeople".Translate())) State.Navigate(FamilyTab.People);
            }
            cy += 80f;
            FamilyKit.EndCard(card, cy, ref y);
        }

        private List<UpcomingItem> upcoming;
        private int upcomingRevision = -1, upcomingViewer = -1, upcomingLineage, upcomingTick = -100000;

        /// <summary>The upcoming list, worked out once per change in the records and kept for a little while.</summary>
        internal List<UpcomingItem> UpcomingFor(FamilyComponent component, FamilyRegistry registry)
        {
            int now = Find.TickManager.TicksGame;
            if (upcoming == null || upcomingRevision != component.Revision || upcomingViewer != component.PerspectivePawnId || upcomingLineage != State.LineageId || now - upcomingTick >= FamilyView.RefreshTicks)
            {
                upcoming = FamilyTimeline.Upcoming(registry, view.House, component.PerspectivePawnId, now);
                upcomingRevision = component.Revision; upcomingViewer = component.PerspectivePawnId; upcomingLineage = State.LineageId; upcomingTick = now;
            }
            return upcoming;
        }
    }
}
