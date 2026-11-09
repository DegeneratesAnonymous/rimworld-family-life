using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The Relationships tab. One person's four feelings toward everyone they know, as bars you can scan, with search, filters and sorting. Choosing a
    /// row opens what lies behind it: the values, the recent causes, shared memories, grievances, promises and what the acting pawn can do about it.
    /// </summary>
    public partial class Window_Families
    {
        private List<RelRow> relRows;
        private int relSubject = -1, relRevision = -1, relLineage, relTick = -100000;
        private RelFilter relFilterBuilt; private RelSort relSortBuilt; private string relSearchBuilt;

        private const float RelRowH = 36f;

        private void DrawRelationships(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            if (!view.Insider) { FamilyKit.EmptyState(v, ref y, "WFL_FW_NotKnownTo".Translate(view.NameOfPerspective(registry))); return; }
            var people = registry.MembersOf(house.Id).Concat(registry.AffiliatesOf(house.Id)).Where(p => p.InColony).Distinct().ToList();
            if (people.Count == 0) { FamilyKit.EmptyState(v, ref y, "WFL_NoHouseholds".Translate()); return; }

            int acting = component.PerspectivePawnId;
            PersonRecord subject = acting > 0 ? registry.GetPerson(acting) : null;
            if (subject == null) subject = people.FirstOrDefault(p => p.Id == State.SelectedPerson) ?? people[0];
            if (acting == 0) State.SelectedPerson = subject.Id;

            // Whose feelings these are: a portrait and a name, a menu to change it when the overview is in use.
            var pick = new Rect(0f, y, Mathf.Min(260f, v.width), 40f);
            Widgets.DrawBoxSolid(pick, new Color(0.14f, 0.14f, 0.15f));
            DrawPortrait(new Rect(pick.x + 4f, pick.y + 2f, 30f, 36f), subject, component);
            GUI.color = FamilyKit.Dim; Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(pick.x + 42f, pick.y + 3f, pick.width - 48f, 16f), (acting > 0 ? "WFL_FW_RelOwn" : "WFL_FW_RelShowing").Translate());
            GUI.color = Color.white; Text.Font = GameFont.Small;
            Widgets.Label(new Rect(pick.x + 42f, pick.y + 18f, pick.width - 60f, 20f), subject.FullName.Truncate(pick.width - 60f));
            if (acting == 0)
            {
                Widgets.Label(new Rect(pick.xMax - 16f, pick.y + 10f, 14f, 20f), "v");
                TooltipHandler.TipRegion(pick, "WFL_FW_RelPickTip".Translate());
                if (Widgets.ButtonInvisible(pick))
                    Find.WindowStack.Add(new FloatMenu(people.Select(p => { var id = p.Id; return new FloatMenuOption(p.FullName, () => { State.SelectedPerson = id; State.SelectedRelation = 0; }); }).ToList()));
            }
            else TooltipHandler.TipRegion(pick, "WFL_FW_RelOwnTip".Translate(subject.Name));

            // Search, then filters, then sorting.
            var search = new Rect(pick.xMax + 8f, y + 8f, Mathf.Min(200f, v.width - pick.width - 16f), 24f);
            if (search.width > 80f)
            {
                State.RelSearch = Widgets.TextField(search, State.RelSearch ?? "");
                if (string.IsNullOrEmpty(State.RelSearch)) { GUI.color = new Color(1f, 1f, 1f, 0.35f); Widgets.Label(new Rect(search.x + 6f, search.y + 1f, search.width - 8f, 22f), "WFL_FW_Search".Translate()); GUI.color = Color.white; }
            }
            y += 46f;
            var filterLabels = new[] { "WFL_FW_RelFilter_Family".Translate().ToString(), "WFL_FW_RelFilter_Associates".Translate().ToString(), "WFL_FW_RelFilter_Everyone".Translate().ToString() };
            int f = FamilyKit.ChipRow(v, ref y, filterLabels, (int)State.RelFilter, new[] { null, null, "WFL_FW_RelFilter_EveryoneTip".Translate().ToString() });
            if (f >= 0) State.RelFilter = (RelFilter)f;
            var sortLabels = new[] { "WFL_FW_RelSort_Closest".Translate().ToString(), "WFL_FW_RelSort_LeastTrusted".Translate().ToString(), "WFL_FW_RelSort_MostTense".Translate().ToString(), "WFL_FW_RelSort_Recent".Translate().ToString() };
            int s = FamilyKit.ChipRow(v, ref y, sortLabels, (int)State.RelSort);
            if (s >= 0) State.RelSort = (RelSort)s;

            // Rows are rebuilt when anything they depend on changes, not each frame.
            int now = Find.TickManager.TicksGame;
            if (relRows == null || relSubject != subject.Id || relRevision != component.Revision || relLineage != house.Id || relFilterBuilt != State.RelFilter || relSortBuilt != State.RelSort || relSearchBuilt != State.RelSearch || now - relTick >= 240)
            {
                relRows = RelationshipView.Build(registry, subject.Id, house.Id, State.RelFilter, State.RelSort, State.RelSearch);
                relSubject = subject.Id; relRevision = component.Revision; relLineage = house.Id; relFilterBuilt = State.RelFilter; relSortBuilt = State.RelSort; relSearchBuilt = State.RelSearch; relTick = now;
            }

            // Strains first: they are what the family is most likely to want to act on.
            foreach (var rift in registry.OpenRiftsOf(subject.Id))
            {
                int otherId = rift.A == subject.Id ? rift.B : rift.A;
                GUI.color = new Color(0.9f, 0.55f, 0.45f);
                Line(v, ref y, "WFL_RiftLine".Translate(FamilyUi.NameOf(registry, otherId), ("WFL_RiftCause_" + rift.Cause).Translate(), FamilyUi.Date(rift.StartTick)), 0f);
                GUI.color = Color.white;
            }

            var other = State.SelectedRelation != 0 ? relRows.FirstOrDefault(r => r.OtherId == State.SelectedRelation) : (RelRow?)null;
            bool side = other.HasValue && v.width >= 800f;
            float listW = side ? Mathf.Min(WindowLayout.ListWidth(v.width, true), v.width - 330f - FamilyKit.Gap) : Mathf.Min(v.width, 620f);
            var list = new Rect(0f, 0f, listW, 0f);
            float listY = y, detailY = y;
            if (other.HasValue && !side)
            {
                DrawRelationDetail(new Rect(0f, 0f, v.width, 0f), ref detailY, component, registry, subject, other.Value);
                listY = detailY;
            }
            DrawRelationList(list, ref listY, component, registry, subject);
            if (other.HasValue && side) DrawRelationDetail(new Rect(listW + FamilyKit.Gap, 0f, Mathf.Min(WindowLayout.DrawerMax, v.width - listW - FamilyKit.Gap), 0f), ref detailY, component, registry, subject, other.Value);
            y = Mathf.Max(listY, detailY);
            DrawAttentionCard(new Rect(0f, 0f, v.width, 0f), ref y, component, registry, house);
        }

        private List<string> attentionLines;
        private int attentionRevision = -1, attentionViewer = -1, attentionLineage, attentionTick = -100000;

        /// <summary>Where one parent's time between their children is uneven. Only shown when there is something to say, and worked out sparingly.</summary>
        private void DrawAttentionCard(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage house)
        {
            int perspective = component.PerspectivePawnId, now = Find.TickManager.TicksGame;
            if (attentionLines == null || attentionRevision != component.Revision || attentionViewer != perspective || attentionLineage != house.Id || now - attentionTick >= FamilyView.RefreshTicks)
            {
                attentionLines = new List<string>();
                foreach (var child in registry.MembersOf(house.Id).Where(p => p.InColony && p.Juvenile).Take(24))
                    foreach (var giver in child.ParentIds.Where(id => perspective == 0 || id == perspective).Take(4))
                    {
                        var comparison = registry.CompareAttention(giver, child.Id, Activities.Now);
                        if (!comparison.Unequal) continue;
                        attentionLines.Add("WFL_Attention_Comparison".Translate(FamilyUi.NameOf(registry, giver), FamilyUi.NameOf(registry, child.Id), (comparison.ChildTicks / 2500f).ToString("0.0"), FamilyUi.NameOf(registry, comparison.OtherChild), (comparison.OtherTicks / 2500f).ToString("0.0")).ToString());
                    }
                attentionRevision = component.Revision; attentionViewer = perspective; attentionLineage = house.Id; attentionTick = now;
            }
            if (attentionLines.Count == 0) return;
            var card = FamilyKit.BeginCard(col, ref y, "r.attention", "WFL_Attention_Title".Translate());
            var cv = card.View;
            float cy = 0f;
            foreach (var line in attentionLines) cy += FamilyKit.Label(cv, cy, line, 0f, true);
            FamilyKit.EndCard(card, cy, ref y);
        }

        private void DrawRelationList(Rect list, ref float y, FamilyComponent component, FamilyRegistry registry, PersonRecord subject)
        {
            var v = new Rect(0f, 0f, list.width, 0f);
            if (relRows.Count == 0)
            {
                FamilyKit.EmptyState(v, ref y, (State.RelFilter == RelFilter.Everyone && string.IsNullOrWhiteSpace(State.RelSearch) ? "WFL_NoBonds" : "WFL_FW_RelNoMatch").Translate());
                return;
            }
            // Legend: the four colours, once.
            string[] names = { "WFL_Bond_Attachment", "WFL_Bond_Trust", "WFL_Bond_Tension", "WFL_Bond_Familiarity" };
            Color[] colors = { FamilyKit.Closeness, FamilyKit.Trust, FamilyKit.Tension, FamilyKit.Familiarity };
            float barW = Mathf.Clamp((v.width - 270f) / 4f - 4f, 38f, 90f);
            float barsX = v.width - 4f * (barW + 4f) - 24f;
            Text.Font = GameFont.Tiny;
            for (int i = 0; i < 4; i++)
            {
                var r = new Rect(barsX + i * (barW + 4f), y, barW, 16f);
                GUI.color = colors[i];
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(r, names[i].Translate());
                TooltipHandler.TipRegion(r, ("WFL_FW_BondTip_" + i).Translate());
            }
            GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
            y += 18f;

            foreach (var row in relRows)
            {
                var rect = new Rect(0f, y, v.width, RelRowH - 2f);
                bool selected = State.SelectedRelation == row.OtherId;
                Widgets.DrawBoxSolid(rect, selected ? new Color(0.22f, 0.20f, 0.12f) : new Color(0.13f, 0.13f, 0.14f));
                if (selected) Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), FamilyKit.Gold); else Widgets.DrawHighlightIfMouseover(rect);
                var other = registry.GetPerson(row.OtherId);
                if (other != null) DrawPortrait(new Rect(rect.x + 6f, rect.y + 2f, 24f, 30f), other, component);
                float nameW = barsX - 40f;
                GUI.color = row.Dead ? FamilyKit.Dim : Color.white;
                Widgets.Label(new Rect(rect.x + 36f, rect.y, nameW, 20f), (row.Name + (row.Dead ? " (" + "WFL_StatusDead".Translate() + ")" : "")).Truncate(nameW));
                Text.Font = GameFont.Tiny; GUI.color = FamilyKit.Dim;
                Widgets.Label(new Rect(rect.x + 36f, rect.y + 17f, nameW, 16f), ("WFL_BondKind_" + row.Kind).Translate().ToString());
                Text.Font = GameFont.Small; GUI.color = Color.white;

                float[] values = { row.Attachment, row.Trust, row.Tension, row.Familiarity };
                for (int i = 0; i < 4; i++)
                    FamilyKit.MetricBar(new Rect(barsX + i * (barW + 4f), rect.y + 11f, barW, 12f), values[i], colors[i], null);
                DrawTrend(new Rect(rect.xMax - 20f, rect.y + 6f, 18f, 22f), row.Trend);
                var captured = row; var who = subject;
                TooltipHandler.TipRegion(new Rect(rect.x, rect.y, barsX - 4f, rect.height), new TipSignal(() => "WFL_BondTip".Translate(who.Name, captured.Name, Mathf.RoundToInt(captured.Attachment), Mathf.RoundToInt(captured.Trust), Mathf.RoundToInt(captured.Tension), Mathf.RoundToInt(captured.Familiarity)).ToString(), 7100000 + row.OtherId));
                if (Widgets.ButtonInvisible(new Rect(rect.x, rect.y, barsX - 4f, rect.height))) State.SelectedRelation = selected ? 0 : row.OtherId;
                y += RelRowH;
            }
        }

        private static void DrawTrend(Rect r, int trend)
        {
            string glyph = trend > 0 ? "^" : trend < 0 ? "v" : "=";
            GUI.color = trend > 0 ? FamilyKit.Good : trend < 0 ? FamilyKit.Tension : FamilyKit.Dim;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, glyph);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(r, ("WFL_FW_Trend_" + (trend > 0 ? "Up" : trend < 0 ? "Down" : "Flat")).Translate());
        }

        // ------------------------------------------------------------------ one relationship

        private void DrawRelationDetail(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, PersonRecord subject, RelRow row)
        {
            var other = registry.GetPerson(row.OtherId);
            if (other == null) { State.SelectedRelation = 0; return; }
            var bond = registry.BondsFrom(subject.Id).FirstOrDefault(b => b.To == row.OtherId);
            var card = FamilyKit.BeginCard(col, ref y, "rel", subject.Name + " & " + other.Name);
            var cv = card.View;
            float cy = 0f;
            DrawPortrait(new Rect(0f, cy, 34f, 42f), subject, component);
            DrawPortrait(new Rect(38f, cy, 34f, 42f), other, component);
            GUI.color = FamilyKit.Dim;
            Widgets.Label(new Rect(80f, cy + 2f, cv.width - 80f, 20f), ("WFL_BondKind_" + row.Kind).Translate().ToString());
            Widgets.Label(new Rect(80f, cy + 20f, cv.width - 80f, 20f), ("WFL_FW_Trend_" + (row.Trend > 0 ? "Up" : row.Trend < 0 ? "Down" : "Flat")).Translate().ToString());
            GUI.color = Color.white;
            cy += 50f;

            // The story of it: what the four meters add up to, in a few lines.
            var tale = RelationshipStory.Of(row, FamilyUi.NameOf(registry, subject.Id), FamilyUi.NameOf(registry, other.Id), component.PerspectivePawnId == subject.Id);
            var prose = new Rect(0f, 0f, cv.width, 0f);
            foreach (var n in tale.All)
            {
                if (n.Key.StartsWith("WFL_FW_RS_Idea")) { cy += 2f; cy += FamilyKit.Label(prose, cy, "WFL_FW_RS_Possible".Translate(), 0f, true); cy += FamilyKit.Label(prose, cy, FamilyText.Say(n), 6f); }
                else cy += FamilyKit.Label(prose, cy, FamilyText.Say(n), 0f, n.Key.StartsWith("WFL_FW_RS_Concern"));
            }
            cy += 4f;

            string[] names = { "WFL_Bond_Attachment", "WFL_Bond_Trust", "WFL_Bond_Tension", "WFL_Bond_Familiarity" };
            Color[] colors = { FamilyKit.Closeness, FamilyKit.Trust, FamilyKit.Tension, FamilyKit.Familiarity };
            float[] values = { row.Attachment, row.Trust, row.Tension, row.Familiarity };
            for (int i = 0; i < 4; i++)
            {
                GUI.color = colors[i]; Widgets.Label(new Rect(0f, cy, 90f, 22f), names[i].Translate()); GUI.color = Color.white;
                FamilyKit.MetricBar(new Rect(94f, cy + 5f, cv.width - 94f - 36f, 12f), values[i], colors[i], ("WFL_FW_BondTip_" + i).Translate());
                Widgets.Label(new Rect(cv.width - 32f, cy, 32f, 22f), Mathf.RoundToInt(values[i]).ToString());
                cy += 24f;
            }

            // Why: the causes the bond has recently moved for.
            if (bond != null && bond.Reasons != null && bond.Reasons.Count > 0)
            {
                FamilyKit.SectionHeader(cv, ref cy, "WFL_FW_Rel_Causes".Translate());
                foreach (var reason in bond.Reasons.Where(rs => System.Math.Abs(rs.Delta) >= InsightTuning.ReasonsMinSize).OrderByDescending(rs => System.Math.Abs(rs.Delta)).Take(4))
                {
                    GUI.color = reason.Delta > 0f ? FamilyKit.Good : FamilyKit.Tension;
                    Widgets.Label(new Rect(0f, cy, 44f, 22f), (reason.Delta > 0f ? "+" : "") + System.Math.Round(reason.Delta));
                    GUI.color = Color.white;
                    Widgets.Label(new Rect(44f, cy, cv.width - 44f, 22f), FamilyUi.WhyText(reason.Cause, reason.Delta > 0f).CapitalizeFirst());
                    cy += 22f;
                }
            }
            if (bond != null && bond.LastQualityTick > 0)
                cy += FamilyKit.Label(cv, cy + 2f, "WFL_FW_Rel_LastTogether".Translate(FamilyText.Date(bond.LastQualityTick)), 0f, true);

            // Memories the two share, and the moments that made them.
            var memories = registry.Memories.Where(m => (m.A == subject.Id && m.B == other.Id) || (m.A == other.Id && m.B == subject.Id)).OrderByDescending(m => m.Weight).Take(3).ToList();
            var moments = new List<EventRecord>();
            for (int i = registry.EventLog.Count - 1; i >= 0 && moments.Count < 4; i--)
            {
                var e = registry.EventLog[i];
                if (e.Weight == EventWeight.Ambient) continue;
                if ((e.A == subject.Id && e.B == other.Id) || (e.A == other.Id && e.B == subject.Id)) moments.Add(e);
            }
            if (memories.Count > 0 || moments.Count > 0)
            {
                FamilyKit.SectionHeader(cv, ref cy, "WFL_FW_Rel_Memories".Translate());
                foreach (var m in memories) cy += FamilyKit.Label(cv, cy, FamilyText.Date(m.Tick) + " - " + ("WFL_Mem_" + m.Kind).Translate(FamilyUi.NameOf(registry, m.A), FamilyUi.NameOf(registry, m.B)), 4f);
                foreach (var e in moments)
                {
                    string body = FamilyText.MomentBody(registry, e);
                    if (body != null) cy += FamilyKit.Label(cv, cy, (e.Weight == EventWeight.Defining ? "* " : "- ") + FamilyText.Date(e.Tick) + " - " + FamilyText.MomentTitle(e) + ": " + body, 4f, e.Weight != EventWeight.Defining);
                }
            }

            // Grievances and promises.
            int viewer = component.PerspectivePawnId;
            var rift = registry.OpenRiftBetween(subject.Id, other.Id);
            var incidents = registry.Incidents.Where(i => !i.Resolved && ((i.ActorId == subject.Id && i.VictimId == other.Id) || (i.ActorId == other.Id && i.VictimId == subject.Id)) && registry.KnowsIncident(viewer, i)).Take(3).ToList();
            if (rift != null || incidents.Count > 0)
            {
                FamilyKit.SectionHeader(cv, ref cy, "WFL_FW_Rel_Grievances".Translate());
                if (rift != null) cy += FamilyKit.Label(cv, cy, "WFL_RiftLine".Translate(other.Name, ("WFL_RiftCause_" + rift.Cause).Translate(), FamilyUi.Date(rift.StartTick)), 4f);
                foreach (var i in incidents) cy += FamilyKit.Label(cv, cy, FamilyText.Date(i.Tick) + " - " + FamilyText.IncidentBody(registry, i, viewer), 4f);
            }
            var promises = registry.Commitments.Where(c => c.IsOpen && ((c.FromId == subject.Id && c.ToId == other.Id) || (c.FromId == other.Id && c.ToId == subject.Id)) && (viewer == 0 || c.FromId == viewer || c.ToId == viewer)).Take(3).ToList();
            if (promises.Count > 0)
            {
                FamilyKit.SectionHeader(cv, ref cy, "WFL_FW_Rel_Promises".Translate());
                foreach (var c in promises) cy += FamilyKit.Label(cv, cy, "WFL_PromiseLine_0".Translate(FamilyUi.NameOf(registry, c.FromId), FamilyUi.NameOf(registry, c.ToId), FamilyText.Date(c.DueTick)), 4f);
            }

            // What the acting pawn can do.
            FamilyKit.SectionHeader(cv, ref cy, "WFL_FW_Rel_Actions".Translate());
            DrawRelationActions(cv, ref cy, component, registry, other, rift, incidents);
            cy += 4f;
            if (FamilyKit.Action(new Rect(0f, cy, 80f, 24f), "WFL_FW_Close".Translate(), true)) State.SelectedRelation = 0;
            cy += 30f;
            FamilyKit.EndCard(card, cy, ref y);
        }

        private void DrawRelationActions(Rect cv, ref float cy, FamilyComponent component, FamilyRegistry registry, PersonRecord other, Rift rift, List<FamilyIncident> incidents)
        {
            int actingId = component.PerspectivePawnId;
            var actor = actingId == 0 ? null : component.TryGetPawn(actingId);
            var target = component.TryGetPawn(other.Id);
            string why = actingId == 0 ? "WFL_FW_Why_Overview".Translate().ToString()
                : actingId == other.Id ? "WFL_FW_Why_Self".Translate().ToString()
                : actor == null || !actor.Spawned || actor.Dead || actor.Downed ? "WFL_FW_Why_ActorBusy".Translate(FamilyUi.NameOf(registry, actingId)).ToString()
                : target == null || !target.Spawned || target.Map != actor.Map ? "WFL_FW_Why_NotNear".Translate(other.Name).ToString()
                : !PerspectiveShiftBridge.CanActAs(actor) ? "WFL_FW_Why_NotAvatar".Translate().ToString() : null;
            bool can = why == null;
            float x = 0f;
            float bw = Mathf.Min(170f, (cv.width - 8f) / 2f);
            if (FamilyKit.Action(new Rect(x, cy, bw, 26f), "WFL_FW_Act_Time".Translate(other.Name).ToString().Truncate(bw - 8f), can, why, "WFL_FW_Act_TimeTip".Translate()))
                ReportHouseAction(SocialMoments.TryPlayerMoment(actor, target));
            x += bw + 6f;
            if (rift != null)
            {
                bool ready = can && registry.MayTryReconcile(rift.Id, Activities.Now);
                if (FamilyKit.Action(new Rect(x, cy, bw, 26f), "WFL_FW_Act_Peace".Translate(), ready, can ? "WFL_FW_Why_TooSoon".Translate().ToString() : why, "WFL_FW_Act_PeaceTip".Translate()))
                    ReportHouseAction(HouseInteractions.StartRiftRepair(actor, target));
            }
            cy += 32f;
            foreach (var i in incidents.Where(i => i.ActorId == other.Id))
            {
                int incident = i.Id;
                bool known = can && i.KnownBy.Contains(actingId);
                if (FamilyKit.Action(new Rect(0f, cy, cv.width, 26f), "WFL_Act_DiscussIncident".Translate(other.Name), known, can ? "WFL_FW_Why_NotKnown".Translate(FamilyUi.NameOf(registry, actingId)).ToString() : why))
                    ReportHouseAction(HouseInteractions.StartIncidentRepair(actor, target, incident));
                cy += 30f;
            }
        }
    }
}
