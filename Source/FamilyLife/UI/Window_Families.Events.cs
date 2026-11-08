using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>What an event card shows, worked out once when the timeline is built so drawing only reads it.</summary>
    internal sealed class EventCardData
    {
        public int Tick;
        public string Date = "", Type = "", Participants = "", Body = "", Consequence = "";
        public Color Color;
        public bool Defining;
        public int ForgivableIncident;
    }

    /// <summary>
    /// The Events tab: one family timeline in three parts. Upcoming (what is planned, promised or possible), Recent (what has just happened, with the
    /// matters still open) and Chronicle (what has become permanent history, plus the family's culture). Empty systems are one quiet line, not a heading each.
    /// </summary>
    public partial class Window_Families
    {
        private List<EventCardData> timelineCards;
        private int timelineRevision = -1, timelineViewer = -1, timelineLineage, timelineSub = -1, timelineMask = -1, timelineTick = -100000, timelineShown = 40;

        private static Color CategoryColor(EventCategory c)
        {
            if ((c & EventCategory.Conflict) != 0) return FamilyKit.Tension;
            if ((c & EventCategory.Ceremonies) != 0) return FamilyKit.Gold;
            if ((c & EventCategory.Traditions) != 0) return new Color(0.85f, 0.55f, 0.28f);
            if ((c & EventCategory.BirthsDeaths) != 0) return new Color(0.62f, 0.55f, 0.82f);
            if ((c & EventCategory.Leadership) != 0) return FamilyKit.Trust;
            if ((c & EventCategory.Relationships) != 0) return FamilyKit.Closeness;
            return new Color(0.45f, 0.70f, 0.72f);
        }

        private void DrawEvents(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            if (!view.Insider) { FamilyKit.EmptyState(v, ref y, "WFL_FW_NotKnownTo".Translate(view.NameOfPerspective(registry))); return; }
            switch ((EventsSub)State.EventsSub)
            {
                case EventsSub.Recent: DrawRecent(v, ref y, component, registry); break;
                case EventsSub.Chronicle: DrawChronicle(v, ref y, component, registry); break;
                default: DrawUpcoming(v, ref y, component, registry); break;
            }
        }

        // ------------------------------------------------------------------ upcoming

        private void DrawUpcoming(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            var col = new Rect(0f, 0f, v.width, 0f);
            var items = UpcomingFor(component, registry);

            var card = FamilyKit.BeginCard(col, ref y, "e.upcoming", "WFL_FW_Card_Upcoming".Translate());
            var cv = card.View;
            float cy = 0f;
            if (items.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NothingUpcoming".Translate());
            foreach (var u in items)
            {
                float top = cy;
                DrawUpcomingLine(cv, ref cy, registry, u);
                float bx = cv.width;
                if (u.Kind == UpcomingKind.Ceremony)
                {
                    var plan = registry.PlanFor(u.RefId);
                    if (plan != null)
                    {
                        int planId = plan.Id;
                        if (FamilyKit.Action(new Rect(cv.width - 118f, top + 1f, 116f, 22f), "WFL_Plan_Open".Translate(), true, null, "WFL_FW_PlanTip".Translate()) ) Find.WindowStack.Add(new Window_CeremonyPlan(planId));
                    }
                }
            }
            // Camping and visiting relatives keep their own buttons.
            foreach (var trip in registry.CampTrips.Where(t => t.FamilyId == house.Id && t.Open).Take(1).ToList())
            {
                cy += 4f;
                cy += FamilyKit.Label(cv, cy, "WFL_Camp_Line".Translate(("WFL_CampMotive_" + trip.Motive).Translate(), ("WFL_CampState_" + trip.State).Translate(), (trip.StayTicks / 2500).ToString(), FamilyUi.NameList(registry, trip.Suggested)), 0f, true);
                float x = 0f;
                if (trip.State == CampState.Proposed)
                {
                    if (FamilyKit.PrimaryButton(new Rect(x, cy, 150f, 26f), "WFL_Camp_Plan".Translate())) CampingDriver.Authorize(component, trip);
                    x += 156f;
                    if (FamilyKit.Action(new Rect(x, cy, 120f, 26f), "WFL_Camp_Decline".Translate(), true)) registry.DeclineCamp(trip.Id, Activities.Now);
                    x += 126f;
                }
                if (trip.State == CampState.Authorized) { if (FamilyKit.PrimaryButton(new Rect(x, cy, 150f, 26f), "WFL_Camp_FormCaravan".Translate())) CampingDriver.OpenCaravanForm(); x += 156f; }
                if (FamilyKit.Action(new Rect(x, cy, 140f, 26f), "WFL_Camp_ShowPlace".Translate(), true)) WorldTiles.Jump(trip.Tile);
                cy += 32f;
            }
            foreach (var visit in registry.KinVisits.Where(k => k.FamilyId == house.Id && k.Open).Take(2).ToList())
            {
                cy += FamilyKit.Label(cv, cy + 2f, "WFL_Kin_Line".Translate(("WFL_KinState_" + visit.State).Translate(), FamilyUi.NameList(registry, visit.KinIds)), 0f, true);
                if (visit.RescueOpen)
                    cy += FamilyKit.Label(cv, cy, "WFL_Rescue_Line".Translate(("WFL_RescueStage_" + visit.Rescue).Translate(), ("WFL_RescueTrouble_" + visit.Trouble).Translate(),
                        Mathf.Max(0, (visit.RescueDeadlineTick - Activities.Now) / ActivityTuning.TicksPerDay)), 8f, true);
                if (visit.State == KinVisitState.Stranded && visit.StrandTile >= 0)
                {
                    if (FamilyKit.Action(new Rect(0f, cy, 150f, 26f), "WFL_Kin_ShowPlace".Translate(), true)) WorldTiles.Jump(visit.StrandTile);
                    cy += 30f;
                }
            }
            FamilyKit.EndCard(card, cy, ref y);

            DrawTraditions(col, ref y, registry, house);
        }

        /// <summary>The house's traditions, with the same mute and hold-now controls as before, now in a card of their own.</summary>
        private void DrawTraditions(Rect col, ref float y, FamilyRegistry registry, Lineage house)
        {
            var card = FamilyKit.BeginCard(col, ref y, "e.traditions", "WFL_FW_Card_Traditions".Translate(), "WFL_TraditionsHelp".Translate());
            var cv = card.View;
            float cy = 0f;
            if (house.Traditions.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_NoTraditions".Translate());
            int now = Find.TickManager.TicksGame;
            foreach (var t in house.Traditions.ToList())
            {
                if (TraditionCatalog.All.FirstOrDefault(d => d.Id == t.Id) == null) continue;
                var row = new Rect(0f, cy, cv.width, 28f);
                Widgets.DrawHighlightIfMouseover(row);
                bool muted = t.Muted;
                Text.Anchor = TextAnchor.MiddleLeft;
                float btn = 232f;
                string last = t.LastHeldTick > 0 ? "WFL_TraditionLast".Translate(FamilyText.Date(t.LastHeldTick), t.TimesHeld).ToString() : "WFL_TraditionNever".Translate().ToString();
                GUI.color = muted ? FamilyKit.Dim : Color.white;
                Widgets.Label(new Rect(4f, row.y, Mathf.Max(80f, (cv.width - btn) * 0.45f), 28f), FamilyUi.TraditionNameById(t.Id).Truncate((cv.width - btn) * 0.45f));
                GUI.color = FamilyKit.Dim;
                Widgets.Label(new Rect(4f + (cv.width - btn) * 0.45f, row.y, (cv.width - btn) * 0.55f, 28f), (muted ? "WFL_FW_Muted".Translate().ToString() + " - " : "") + last.Truncate((cv.width - btn) * 0.55f));
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                if (HouseButton(new Rect(cv.width - btn, row.y + 1f, 108f, 26f), muted ? "WFL_Unmute".Translate() : "WFL_Mute".Translate(), registry, house)) registry.SetTraditionMuted(house.Id, t.Id, !muted);
                if (HouseButton(new Rect(cv.width - 120f, row.y + 1f, 120f, 26f), "WFL_HoldNow".Translate(), registry, house) && !muted)
                {
                    registry.RequestGathering(t.Id, house.Id, 0, now, 0, 3 * ActivityTuning.TicksPerDay);
                    Messages.Message("WFL_HoldNowQueued".Translate(FamilyUi.TraditionNameById(t.Id)), MessageTypeDefOf.NeutralEvent, false);
                }
                cy += 30f;
            }
            FamilyKit.EndCard(card, cy, ref y);
        }

        // ------------------------------------------------------------------ the filters shared by Recent and Chronicle

        private void DrawEventFilters(Rect v, ref float y)
        {
            float x = 0f, rowY = y;
            var all = new Rect(x, rowY, 56f, 24f);
            if (FamilyKit.ToggleChip(all, "WFL_FW_Cat_All".Translate(), State.EventFilter == 0, "WFL_FW_Cat_AllTip".Translate())) State.EventFilter = 0;
            x += 60f;
            foreach (var cat in EventFilters.All)
            {
                string label = FamilyText.CategoryLabel(cat);
                float w = FamilyKit.BadgeWidth(label) + 14f;
                if (x + w > v.width) { x = 0f; rowY += 28f; }
                if (FamilyKit.ToggleChip(new Rect(x, rowY, w, 24f), label, (State.EventFilter & (int)cat) != 0)) State.EventFilter = EventFilters.Toggle(State.EventFilter, cat);
                x += w + 4f;
            }
            y = rowY + 32f;
        }

        // ------------------------------------------------------------------ recent

        private void DrawRecent(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            int viewer = component.PerspectivePawnId;

            // Matters still open: one quiet line when there are none.
            var incidents = registry.Incidents.Where(i => !i.Resolved && (i.ActorFamily == house.Id || i.VictimFamily == house.Id) && registry.KnowsIncident(viewer, i)).OrderByDescending(i => i.Tick).Take(6).ToList();
            var plots = registry.Plots.Where(p => p.Active && (view.Relevant.Contains(p.OrganizerId) || view.Relevant.Contains(p.TargetId)) && registry.KnowsPlot(viewer, p)).Take(4).ToList();
            var promises = registry.Commitments.Where(c => c.IsOpen && (view.Relevant.Contains(c.FromId) || view.Relevant.Contains(c.ToId)) && (viewer == 0 || c.FromId == viewer || c.ToId == viewer) && c.DueTick - Activities.Now <= FamilyAlerts.PromiseWindow).Take(4).ToList();
            var col = new Rect(0f, 0f, v.width, 0f);
            if (incidents.Count == 0 && plots.Count == 0 && promises.Count == 0)
            {
                FamilyKit.EmptyState(v, ref y, "WFL_FW_NothingOpen".Translate());
            }
            else
            {
                var card = FamilyKit.BeginCard(col, ref y, "e.open", "WFL_FW_Card_Open".Translate(), null, true);
                var cv = card.View;
                float cy = 0f;
                foreach (var i in incidents)
                {
                    cy += FamilyKit.Label(cv, cy, FamilyText.Date(i.Tick) + " - " + FamilyText.IncidentBody(registry, i, viewer), 0f);
                    if (i.VictimFamily == house.Id && HouseButton(new Rect(8f, cy, 150f, 24f), "WFL_Stories_Forgive".Translate(), registry, house)) registry.ForgiveIncident(i.Id, house.Id, Activities.Now);
                    cy += 28f;
                }
                foreach (var p in plots) cy += FamilyKit.Label(cv, cy, FamilyText.PlotBody(registry, p), 0f);
                foreach (var c in promises) cy += FamilyKit.Label(cv, cy, "WFL_PromiseLine_0".Translate(FamilyUi.NameOf(registry, c.FromId), FamilyUi.NameOf(registry, c.ToId), FamilyText.Date(c.DueTick)), 0f);
                FamilyKit.EndCard(card, cy, ref y);
            }

            DrawEventFilters(v, ref y);
            DrawTimelineCards(v, ref y, component, registry, EventsSub.Recent);
            DrawSupplyRequests(v, ref y, registry);
        }

        /// <summary>Requests between households for goods, still shown, but only when there are any.</summary>
        private void DrawSupplyRequests(Rect v, ref float y, FamilyRegistry registry)
        {
            int viewer = FamilyComponent.Current?.PerspectivePawnId ?? 0;
            var requests = registry.Requests.Where(r => (view.Relevant.Contains(r.RequesterId) || view.Relevant.Contains(r.DonorId)) && (viewer == 0 || r.RequesterId == viewer || r.DonorId == viewer)).OrderByDescending(r => r.CreatedTick).Take(8).ToList();
            if (requests.Count == 0) return;
            var card = FamilyKit.BeginCard(new Rect(0f, 0f, v.width, 0f), ref y, "e.supply", "WFL_Stories_Supplies".Translate());
            var cv = card.View;
            float cy = 0f;
            cy += FamilyKit.Label(cv, cy, "WFL_Stories_Claims".Translate(registry.Claims.Count(c => c.HolderFamily == view.LineageId)), 0f, true);
            foreach (var supply in requests)
                cy += FamilyKit.Label(cv, cy, "WFL_Stories_Request".Translate(FamilyUi.NameOf(registry, supply.RequesterId), ("WFL_SupplyApproach_" + supply.Approach).Translate(), supply.Count, supply.Definition, ("WFL_SupplyState_" + supply.State).Translate()), 0f);
            FamilyKit.EndCard(card, cy, ref y);
        }

        // ------------------------------------------------------------------ chronicle

        private void DrawChronicle(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            var col = new Rect(0f, 0f, v.width, 0f);

            var culture = FamilyKit.BeginCard(col, ref y, "e.culture", "WFL_Culture".Translate());
            var cv = culture.View;
            float cy = 0f;
            var values = registry.Values(house.Id);
            if (values.Count == 0) cy += FamilyKit.Label(cv, cy, "WFL_NoValues".Translate(), 0f, true);
            else cy += FamilyKit.Label(cv, cy, "WFL_ValuesLine".Translate(string.Join(", ", values.Select(FamilyUi.DimName))));
            var legacies = registry.Legacies(house.Id);
            if (legacies.Count > 0) cy += FamilyKit.Label(cv, cy, "WFL_LegacyLine".Translate(string.Join(", ", legacies)));
            var memories = registry.MemoriesOf(house.Id);
            foreach (var m in memories.OrderByDescending(x => x.Weight).ThenByDescending(x => x.Tick).Take(5))
                cy += FamilyKit.Label(cv, cy, FamilyText.Date(m.Tick) + " - " + ("WFL_Mem_" + m.Kind).Translate(FamilyUi.NameOf(registry, m.A), FamilyUi.NameOf(registry, m.B)), 6f, true);
            FamilyKit.EndCard(culture, cy, ref y);

            DrawEventFilters(v, ref y);
            DrawTimelineCards(v, ref y, component, registry, EventsSub.Chronicle);
        }

        // ------------------------------------------------------------------ the shared list of cards

        private void DrawTimelineCards(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry, EventsSub sub)
        {
            int now = Find.TickManager.TicksGame;
            if (timelineSub != (int)sub || timelineMask != State.EventFilter) timelineShown = 40;
            if (timelineCards == null || timelineRevision != component.Revision || timelineViewer != component.PerspectivePawnId || timelineLineage != State.LineageId
                || timelineSub != (int)sub || timelineMask != State.EventFilter || now - timelineTick >= FamilyView.RefreshTicks)
            {
                timelineCards = BuildCards(component, registry, sub, now);
                timelineRevision = component.Revision; timelineViewer = component.PerspectivePawnId; timelineLineage = State.LineageId;
                timelineSub = (int)sub; timelineMask = State.EventFilter; timelineTick = now;
            }
            if (timelineCards.Count == 0)
            {
                FamilyKit.EmptyState(v, ref y, (sub == EventsSub.Recent ? "WFL_FW_NothingRecent" : "WFL_FW_NothingChronicle").Translate());
                return;
            }
            FamilyKit.Cull = true;
            int shown = Mathf.Min(timelineCards.Count, timelineShown);
            for (int i = 0; i < shown; i++)
            {
                var c = timelineCards[i];
                FamilyKit.EventCard(v, ref y, c.Date, c.Type, c.Color, c.Participants, c.Body, c.Consequence, c.Defining);
            }
            FamilyKit.Cull = false;
            if (timelineCards.Count > shown)
            {
                if (FamilyKit.Action(new Rect(0f, y + 2f, 180f, 26f), "WFL_FW_ShowMore".Translate(timelineCards.Count - shown), true)) timelineShown += 40;
                y += 32f;
            }
        }

        private List<EventCardData> BuildCards(FamilyComponent component, FamilyRegistry registry, EventsSub sub, int now)
        {
            var house = view.House;
            int viewer = component.PerspectivePawnId;
            var entries = sub == EventsSub.Recent ? FamilyTimeline.Recent(registry, house, viewer, now, State.EventFilter) : FamilyTimeline.Chronicle(registry, house, viewer, State.EventFilter);
            var cards = new List<EventCardData>(entries.Count);
            foreach (var t in entries)
            {
                var card = new EventCardData { Tick = t.Tick, Date = FamilyText.Date(t.Tick), Color = CategoryColor(t.Category), Defining = t.Defining, Participants = FamilyText.Participants(registry, t) };
                switch (t.Kind)
                {
                    case TimelineKind.Moment:
                        card.Body = FamilyText.MomentBody(registry, t.Moment);
                        if (card.Body == null) continue;
                        card.Type = FamilyText.MomentTitle(t.Moment);
                        card.Consequence = FamilyText.Aftermath(registry, t.Moment);
                        break;
                    case TimelineKind.Ceremony:
                        card.Type = ("WFL_Ceremony_" + t.Ceremony.Kind).Translate();
                        card.Body = "WFL_FW_CeremonyBody".Translate(t.Ceremony.Attendees, t.Ceremony.Moments);
                        card.Consequence = t.Ceremony.Outcome >= 0.5f ? "WFL_FW_CeremonyWell".Translate().ToString() : t.Ceremony.Outcome <= -0.5f ? "WFL_FW_CeremonyBadly".Translate().ToString() : "";
                        break;
                    case TimelineKind.Incident:
                        card.Type = ("WFL_Incident_" + t.Incident.Kind).Translate();
                        card.Body = FamilyText.IncidentBody(registry, t.Incident, viewer);
                        card.Consequence = t.Incident.Resolved ? "WFL_Stories_Resolved".Translate().ToString() : "WFL_Stories_Open".Translate().ToString();
                        break;
                    case TimelineKind.Plot:
                        card.Type = "WFL_FW_Cat_Plot".Translate();
                        card.Body = FamilyText.PlotBody(registry, t.Plot);
                        break;
                    default:
                        card.Type = FamilyText.CategoryLabel(EventFilters.Of(t.Chronicle.Kind) & ~(EventFilters.Of(t.Chronicle.Kind) - 1));
                        card.Body = FamilyUi.Describe(registry, t.Chronicle);
                        break;
                }
                cards.Add(card);
            }
            return cards;
        }
    }
}
