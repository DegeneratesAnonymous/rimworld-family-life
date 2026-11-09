using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The Summary tab: what the family is going through, told first. The Current Chapter leads, then the situations behind it, what needs doing, what
    /// is coming and the latest of its story. The raw status is one small card beside them, not the page. Columns follow the width: three on a wide
    /// window, two on a moderate one, one on a narrow one (<see cref="WindowLayout"/>).
    /// </summary>
    public partial class Window_Families
    {
        private void DrawSummary(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            int n = WindowLayout.DashboardColumns(v.width);
            float w = Mathf.Min(560f, (v.width - WindowLayout.Gap * (n - 1)) / n);
            var cols = new Rect[n];
            for (int i = 0; i < n; i++) cols[i] = new Rect(i * (w + WindowLayout.Gap), 0f, w, 0f);
            var ys = new float[n];
            for (int i = 0; i < n; i++) ys[i] = y;

            if (!view.Insider)
            {
                // A visitor's Summary: their own history with the House first, then what is public. Nothing private, nothing blank.
                int k = n > 1 ? 1 : 0;
                DrawChapterCard(cols[0], ref ys[0], registry);
                SummaryStatus(cols[n > 2 ? 2 : k], ref ys[n > 2 ? 2 : k], component, registry);
                SummaryThreads(cols[k], ref ys[k], component, registry);
                SummaryStory(cols[k], ref ys[k], component, registry);
                y = ys.Max();
                SummaryPeople(new Rect(0f, 0f, v.width, 0f), ref y, component, registry);
                return;
            }
            if (n >= 3)
            {
                DrawChapterCard(cols[0], ref ys[0], registry);
                SummaryAttention(cols[0], ref ys[0], component, registry);
                SummaryThreads(cols[1], ref ys[1], component, registry);
                SummaryUpcoming(cols[1], ref ys[1], component, registry);
                SummaryStatus(cols[2], ref ys[2], component, registry);
                SummaryStory(cols[2], ref ys[2], component, registry);
            }
            else if (n == 2)
            {
                DrawChapterCard(cols[0], ref ys[0], registry);
                SummaryAttention(cols[0], ref ys[0], component, registry);
                SummaryStatus(cols[0], ref ys[0], component, registry);
                SummaryThreads(cols[1], ref ys[1], component, registry);
                SummaryUpcoming(cols[1], ref ys[1], component, registry);
                SummaryStory(cols[1], ref ys[1], component, registry);
            }
            else
            {
                DrawChapterCard(cols[0], ref ys[0], registry);
                SummaryThreads(cols[0], ref ys[0], component, registry);
                SummaryAttention(cols[0], ref ys[0], component, registry);
                SummaryUpcoming(cols[0], ref ys[0], component, registry);
                SummaryStory(cols[0], ref ys[0], component, registry);
                SummaryStatus(cols[0], ref ys[0], component, registry);
            }
            y = ys.Max();
            SummaryPeople(new Rect(0f, 0f, v.width, 0f), ref y, component, registry);
            SummaryHouseholds(new Rect(0f, 0f, Mathf.Min(v.width, 720f), 0f), ref y, registry);
        }

        /// <summary>The situations behind the chapter. The one leading the chapter is not repeated; with nothing else going on the card says so.</summary>
        private void SummaryThreads(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var lead = view.Chapter?.Lead;
            var rest = view.Threads.Where(t => t.T != lead && t.T.Kind != ThreadKind.Dealings).Take(3).ToList();
            if (rest.Count == 0)
            {
                var card = FamilyKit.BeginCard(col, ref y, "s.threads", "WFL_FW_Card_Threads".Translate());
                float cy = 0f;
                FamilyKit.EmptyState(card.View, ref cy, "WFL_FW_NoOtherThreads".Translate());
                FamilyKit.EndCard(card, cy, ref y);
                return;
            }
            foreach (var t in rest) DrawThreadCard(col, ref y, t, component, registry, false);
        }

        /// <summary>A small card of facts. They support the story; they are not the story.</summary>
        private void SummaryStatus(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            var card = FamilyKit.BeginCard(col, ref y, "s.status", "WFL_FW_Card_Status".Translate());
            var cv = card.View;
            float cy = 0f;
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_HouseHead".Translate(), view.Head != 0 ? FamilyUi.NameOf(registry, view.Head) : "WFL_FW_NoHead".Translate().ToString());
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_Steward".Translate(), view.Steward != 0 ? FamilyUi.NameOf(registry, view.Steward) : "WFL_FW_NoSteward".Translate().ToString());
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_FoundedLabel".Translate(), "WFL_FoundedBy".Translate(FamilyUi.NameOf(registry, house.FounderId), FamilyText.Date(house.FoundedTick)).ToString());
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_CountsLabel".Translate(), CountsText(view.Counts));
            if (view.Counts.Differ || view.Counts.ResidentsFromOtherHouses > 0)
                cy += FamilyKit.Label(cv, cy, CountsExplanation(view.Counts), 0f, true, GameFont.Tiny);
            if (!view.Insider)
            {
                FamilyKit.EndCard(card, cy + 4f, ref y);
                return;
            }
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
            FamilyKit.EndCard(card, cy, ref y);
        }

        /// <summary>"3 House members, 4 household residents in 1 household": the three different counts, always named.</summary>
        internal static string CountsText(HouseCounts c) =>
            "WFL_FW_CountsLine".Translate(c.HouseMembers, c.Residents, c.Households).ToString();

        internal static string CountsExplanation(HouseCounts c)
        {
            var parts = new List<string>(2);
            if (c.ResidentsFromOtherHouses > 0) parts.Add("WFL_FW_CountsOthers".Translate(c.ResidentsFromOtherHouses).ToString());
            if (c.MembersLivingElsewhere > 0) parts.Add("WFL_FW_CountsElsewhere".Translate(c.MembersLivingElsewhere).ToString());
            return string.Join(" ", parts);
        }

        private static void DrawStatusBar(Rect cv, ref float cy, string label, float value, Color color, string valueText, string tip)
        {
            float bar = Mathf.Min(cv.width - 94f - 84f, 160f);
            GUI.color = FamilyKit.Dim;
            Widgets.Label(new Rect(0f, cy, 90f, 22f), label);
            GUI.color = Color.white;
            FamilyKit.MetricBar(new Rect(94f, cy + 5f, bar, 12f), value, color, tip);
            Widgets.Label(new Rect(94f + bar + 6f, cy, 80f, 22f), valueText);
            cy += 24f;
        }

        /// <summary>Matters needing attention, each saying what is wrong, by how much and what could be done, and each a link to where it is dealt with.</summary>
        private void SummaryAttention(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            string title = view.Alerts.Count > 0 ? "WFL_FW_Card_Attention".Translate() + "  (" + view.Alerts.Count + ")" : "WFL_FW_Card_Attention".Translate().ToString();
            var card = FamilyKit.BeginCard(col, ref y, "s.attention", title, null, view.Alerts.Any(a => a.Urgent));
            var cv = card.View;
            float cy = 0f;
            if (!view.Insider) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NotKnownTo".Translate(view.NameOfPerspective(registry)));
            else if (view.Alerts.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NothingAttention".Translate());
            foreach (var alert in view.Alerts.Take(6))
            {
                string head = ("WFL_FW_AlertHead_" + alert.Kind).Translate();
                string detail = AlertDetail(component, registry, alert);
                string hint = ("WFL_FW_AlertHint_" + alert.Kind).Translate();
                if (FamilyKit.InfoRow(cv, ref cy, head, detail, hint, alert.Severity, FamilyText.AlertTip(alert) + "\n\n" + "WFL_FW_ClickToOpen".Translate(("WFL_FW_Tab_" + alert.Tab).Translate())))
                    State.Go(alert);
            }
            FamilyKit.EndCard(card, cy, ref y);
        }

        /// <summary>The numbers behind an alert where the records have them (food held against the wanted days), otherwise its plain sentence.</summary>
        private string AlertDetail(FamilyComponent component, FamilyRegistry registry, FamilyAlert alert)
        {
            if (alert.Kind == AlertKind.FoodLow)
            {
                var food = HomeFor(component).Supplies.FirstOrDefault(s => s.Kind == SupplyKind.Food);
                if (food != null && food.Known && food.Target > 0f) return "WFL_FW_FoodDetail".Translate(food.Current.ToString("0.0"), food.Target.ToString("0.0")).ToString();
            }
            return FamilyText.AlertText(registry, alert);
        }

        private void SummaryUpcoming(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var card = FamilyKit.BeginCard(col, ref y, "s.upcoming", "WFL_FW_Card_Upcoming".Translate());
            var cv = card.View;
            float cy = 0f;
            var items = UpcomingFor(component, registry);
            if (items.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NothingUpcoming".Translate());
            foreach (var u in items.Take(5)) DrawUpcomingLine(cv, ref cy, registry, u);
            if (FamilyKit.Action(new Rect(0f, cy + 2f, 150f, 24f), "WFL_FW_SeeAllUpcoming".Translate(), true)) State.Navigate(FamilyTab.Story, (int)StorySub.Current);
            cy += 30f;
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

        /// <summary>The three most meaningful recent developments, each told as a scene. Several teaching moments are one development, not several.</summary>
        private void SummaryStory(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var card = FamilyKit.BeginCard(col, ref y, "s.story", "WFL_FW_Card_Story".Translate());
            var cv = card.View;
            float cy = 0f;
            // An outsider only knows what they were part of; say so only when there is nothing of that.
            if (view.Highlights.Count == 0)
                FamilyKit.EmptyState(cv, ref cy, view.Insider ? "WFL_FW_NothingStory".Translate() : "WFL_FW_NotKnownTo".Translate(view.NameOfPerspective(registry)));
            else if (!view.Insider)
                cy += FamilyKit.Label(cv, cy, "WFL_FW_StoryOutsider".Translate(), 0f, true);
            foreach (var c in view.Highlights.Take(2)) { cy += FamilyKit.Label(cv, cy, "- " + c.OneLine); }
            if (view.Highlights.Count > 0) cy += 2f;
            if (FamilyKit.Action(new Rect(0f, cy + 2f, 170f, 24f), "WFL_FW_ViewFullStory".Translate(), true)) State.Navigate(FamilyTab.Story, (int)StorySub.Timeline);
            cy += 30f;
            FamilyKit.EndCard(card, cy, ref y);
        }

        /// <summary>The people the current story is about first: the House's head and steward, then whoever the situations name, then the rest.</summary>
        private void SummaryPeople(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var card = FamilyKit.BeginCard(col, ref y, "s.people", "WFL_FW_Card_People".Translate());
            var cv = card.View;
            float cy = 0f;
            var model = People(component);
            var named = new HashSet<int>();
            if (view.Head != 0) named.Add(view.Head);
            if (view.Steward != 0) named.Add(view.Steward);
            foreach (var t in view.Threads.Take(4)) foreach (var id in t.T.People) named.Add(id);
            var colony = model.Rows.Where(r => r.Person.InColony).OrderByDescending(r => named.Contains(r.Person.Id)).ToList();
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
                Widgets.Label(new Rect(r.x - 2f, r.y + 52f, r.width + 4f, 18f), HouseholdNames.Short(row.Person).Truncate(r.width + 4f));
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

        /// <summary>Who lives where, in the household's own name, and how many residents belong to another House.</summary>
        private void SummaryHouseholds(Rect col, ref float y, FamilyRegistry registry)
        {
            var card = FamilyKit.BeginCard(col, ref y, "s.households", "WFL_FW_Card_Households".Translate());
            var cv = card.View;
            float cy = 0f;
            var households = registry.HouseholdsOfLineage(view.LineageId);
            if (households.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_NoHouseholds".Translate());
            foreach (var h in households.Take(5))
            {
                int residents = h.ResidentIds.Count(id => registry.GetPerson(id)?.InColony == true);
                int others = h.ResidentIds.Count(id => { var p = registry.GetPerson(id); return p != null && p.InColony && !view.Relevant.Contains(id); });
                string name = view.HouseholdLabels.TryGetValue(h.Id, out var label) ? label : "WFL_FW_HH_Plain".Translate().ToString();
                var row = new Rect(0f, cy, cv.width, 24f);
                Widgets.DrawHighlightIfMouseover(row);
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(4f, cy, cv.width - 8f, 24f), ("WFL_FW_HouseholdSummary".Translate(name, residents).ToString() + (others > 0 ? "  -  " + "WFL_FW_HouseholdOthers".Translate(others).ToString() : "")).Truncate(cv.width - 8f));
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(row)) State.Navigate(FamilyTab.Home, (int)HomeSub.Households);
                cy += 26f;
            }
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
