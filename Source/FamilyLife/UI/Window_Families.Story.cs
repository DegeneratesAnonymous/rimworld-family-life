using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>The words of one story thread, translated once when the view is built so drawing only reads them.</summary>
    internal sealed class ThreadCard
    {
        public StoryThread T;
        public string Title = "", Summary = "", Line = "", State = "", Why = "", Recent = "", Detail = "";
        public string[] Facts = new string[0];
        public string[] Paths = new string[0], Actions = new string[0];
    }

    internal sealed class ChapterCard
    {
        public string Title = "";
        public string[] Paragraphs = new string[0], Supporting = new string[0], Evidence = new string[0];
        public bool Quiet, Unknown;
        public StoryThread Lead;
    }

    /// <summary>One entry of the Story as it is drawn: a single event, or several told together. Worked out when the timeline is built.</summary>
    internal sealed class BeatCard
    {
        public int Key, Tick;
        public string Date = "", Type = "", Body = "", Cause = "", Consequence = "", ImportanceWord = "";
        public Color Color;
        public StoryImportance Importance;
        public bool Grouped, RoutineSummary;
        public List<int> People = new List<int>();
        public List<string> Details = new List<string>();
        public int LinkA, LinkB;
        public int Outcome;                       // -1 harmful, 0 unchanged, 1 positive
        public bool Informational;                 // ongoing or informational: blue
        public string OneLine = "", LinkAName = "", LinkBName = "";
    }

    /// <summary>
    /// The Story tab, in three parts. Current: the situations the family is in and what is coming. Timeline: what has just happened, told as scenes,
    /// with everyday matters folded away. Chronicle: only what defines the family's history. The ranking and grouping are the story layer's
    /// (<see cref="StoryModel"/>); this file only draws them.
    /// </summary>
    public partial class Window_Families
    {
        private List<BeatCard> timelineCards;
        private int timelineRevision = -1, timelineViewer = -1, timelineLineage, timelineSub = -1, timelineMask = -1, timelinePerson = -1, timelineTick = -100000, timelineShown = 30;
        private bool timelineRoutine;
        private readonly HashSet<int> expandedBeats = new HashSet<int>();

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

        // ------------------------------------------------------------------ turning the story layer's keys into words

        internal static ChapterCard MakeChapter(FamilyRegistry r, StoryChapter c) => new ChapterCard
        {
            Title = FamilyText.Say(c.Title), Paragraphs = FamilyText.SayAll(c.Body), Supporting = FamilyText.SayAll(c.Supporting), Evidence = FamilyText.SayAll(c.Evidence), Quiet = c.Quiet, Unknown = c.Unknown, Lead = c.Lead
        };

        internal static ThreadCard MakeThread(FamilyRegistry r, StoryThread t) => new ThreadCard
        {
            T = t, Title = FamilyText.Say(t.Title), Summary = FamilyText.Say(t.Summary), Line = FamilyText.Say(t.Line), State = FamilyText.Say(t.State),
            Why = FamilyText.Say(t.Why), Recent = FamilyText.Say(t.Recent), Detail = FamilyText.Say(t.Detail), Facts = FamilyText.SayAll(t.Facts), Paths = FamilyText.SayAll(t.Paths), Actions = FamilyText.SayAll(t.Actions)
        };

        private static string FeelBucket(int v) => v >= 60 ? "Glad" : v >= 20 ? "Warm" : v <= -60 ? "Hurt" : v <= -20 ? "Uneasy" : null;

        internal static BeatCard MakeBeat(FamilyRegistry r, StoryBeat b, int viewer)
        {
            if (b.IsRoutineSummary)
                return new BeatCard { Key = -1, RoutineSummary = true, Body = "WFL_FW_RoutineSummary".Translate(b.RoutineCount).ToString(), Importance = StoryImportance.Routine };
            var t = b.Entry;
            var card = new BeatCard
            {
                Key = (t.Tick * 397) ^ (b.Parts.Count * 31) ^ b.Initiator, Tick = t.Tick, Date = FamilyText.Date(t.Tick), Color = CategoryColor(t.Category), Importance = b.Importance,
                ImportanceWord = ("WFL_FW_Imp_" + b.Importance).Translate().ToString()
            };
            switch (t.Kind)
            {
                case TimelineKind.Moment:
                    {
                        var e = t.Moment;
                        card.People.Add(e.A); foreach (var o in b.Others) if (!card.People.Contains(o)) card.People.Add(o);
                        if (e.Third != 0 && !card.People.Contains(e.Third)) card.People.Add(e.Third);
                        card.LinkA = e.A; card.LinkB = b.Others.Count == 1 ? b.Others[0] : 0;
                        if (e.Setting != 0 && ("WFL_FW_Cause_" + (ActivityKind)e.Setting).CanTranslate()) card.Cause = ("WFL_FW_Cause_" + (ActivityKind)e.Setting).Translate().ToString();
                        if (!b.Grouped)
                        {
                            card.Body = FamilyText.MomentBody(r, e);
                            if (card.Body == null)
                            {
                                // A moment whose definition is gone (an older save, a removed mod): shown plainly as a legacy entry, never dropped or treated as broken.
                                card.Type = "WFL_FW_Legacy_Title".Translate();
                                card.Body = "WFL_FW_Legacy_Body".Translate(FamilyUi.NameOf(r, e.A), FamilyUi.NameOf(r, e.B));
                                card.Consequence = FamilyText.TierWords(e.Tier);
                                break;
                            }
                            card.Type = FamilyText.MomentTitle(e);
                            card.Consequence = FamilyText.Aftermath(r, e);
                            break;
                        }
                        card.Grouped = true;
                        string initiator = FamilyUi.NameOf(r, b.Initiator);
                        string others = FamilyUi.NameList(r, b.Others);
                        card.Type = b.Teaching ? "WFL_FW_Group_Lessons".Translate(initiator).ToString() : FamilyText.MomentTitle(e);
                        card.Body = (viewer != 0 && viewer == b.Initiator ? "WFL_FW_Group_Body_You" : "WFL_FW_Group_Body").Translate(initiator, others, b.Parts.Count).ToString();
                        card.Consequence = GroupAftermath(r, b);
                        foreach (var part in b.Parts.OrderBy(p => p.Tick))
                            card.Details.Add(FamilyText.Date(part.Tick) + " - " + FamilyUi.NameOf(r, part.A) + " / " + FamilyUi.NameOf(r, part.B) + ": " + FamilyText.TierWords(part.Tier));
                        break;
                    }
                case TimelineKind.Ceremony:
                    card.Type = ("WFL_Ceremony_" + t.Ceremony.Kind).Translate();
                    card.Body = "WFL_FW_CeremonyBody".Translate(t.Ceremony.Attendees, t.Ceremony.Moments);
                    card.Consequence = t.Ceremony.Outcome >= 0.5f ? "WFL_FW_CeremonyWell".Translate().ToString() : t.Ceremony.Outcome <= -0.5f ? "WFL_FW_CeremonyBadly".Translate().ToString() : "";
                    if (t.Ceremony.SubjectId != 0) { card.People.Add(t.Ceremony.SubjectId); card.LinkA = t.Ceremony.SubjectId; }
                    break;
                case TimelineKind.Incident:
                    card.Type = ("WFL_Incident_" + t.Incident.Kind).Translate();
                    card.Body = FamilyText.IncidentBody(r, t.Incident, viewer);
                    card.Consequence = t.Incident.Resolved ? "WFL_Stories_Resolved".Translate().ToString() : "WFL_Stories_Open".Translate().ToString();
                    if (t.Incident.ActorId != 0) card.People.Add(t.Incident.ActorId);
                    card.People.Add(t.Incident.VictimId);
                    break;
                case TimelineKind.Plot:
                    card.Type = "WFL_FW_Cat_Plot".Translate();
                    card.Body = FamilyText.PlotBody(r, t.Plot);
                    card.People.Add(t.Plot.OrganizerId); card.People.Add(t.Plot.TargetId);
                    break;
                default:
                    card.Type = FamilyText.CategoryLabel(EventFilters.Of(t.Chronicle.Kind) & ~(EventFilters.Of(t.Chronicle.Kind) - 1));
                    card.Body = FamilyUi.Describe(r, t.Chronicle);
                    if (t.Chronicle.A != 0 && r.GetPerson(t.Chronicle.A) != null) { card.People.Add(t.Chronicle.A); card.LinkA = t.Chronicle.A; }
                    if (t.Chronicle.B != 0 && r.GetPerson(t.Chronicle.B) != null && t.Chronicle.Kind != FamilyEventKind.LineageFounded && t.Chronicle.Kind != FamilyEventKind.SocialBranch) card.People.Add(t.Chronicle.B);
                    break;
            }
            if (t.Kind == TimelineKind.Moment) card.Outcome = StoryValence.ForBeat(b.Parts, viewer);
            else if (t.Kind == TimelineKind.Ceremony) card.Outcome = t.Ceremony.Outcome >= 0.5f ? 1 : t.Ceremony.Outcome <= -0.5f ? -1 : 0;
            else if (t.Kind == TimelineKind.Incident) card.Outcome = t.Incident.Resolved ? 0 : -1;
            else if (t.Kind == TimelineKind.Plot) card.Outcome = -1;
            else card.Informational = true;
            if (card.LinkA != 0) card.LinkAName = FamilyUi.NameOf(r, card.LinkA);
            if (card.LinkB != 0) card.LinkBName = FamilyUi.NameOf(r, card.LinkB);
            card.OneLine = t.Kind == TimelineKind.Moment
                ? "WFL_FW_Line_Moment".Translate(FamilyUi.NameOf(r, b.Initiator), FamilyUi.NameList(r, b.Others), card.Type).ToString()
                : card.Type + ": " + card.Body;
            return card;
        }

        /// <summary>How a run of moments left the people they were with. People who walked away differently are named apart, never averaged together.</summary>
        private static string GroupAftermath(FamilyRegistry r, StoryBeat b)
        {
            var byBucket = new Dictionary<string, List<int>>();
            foreach (var part in b.Parts)
            {
                string bucket = FeelBucket(part.FeelB);
                if (bucket == null) continue;
                if (!byBucket.TryGetValue(bucket, out var who)) byBucket[bucket] = who = new List<int>();
                if (!who.Contains(part.B)) who.Add(part.B);
            }
            var sentences = new List<string>();
            foreach (var pair in byBucket) sentences.Add(("WFL_FW_FeelGroup_" + pair.Key).Translate(FamilyUi.NameList(r, pair.Value)).ToString());
            var f = AftermathFlags.None;
            foreach (var part in b.Parts) f |= (AftermathFlags)part.Aftermath;
            if ((f & AftermathFlags.PromiseKept) != 0) sentences.Add("WFL_FW_After_PromiseKeptGroup".Translate(FamilyUi.NameOf(r, b.Initiator)).ToString());
            if ((f & AftermathFlags.TieChanged) != 0) sentences.Add("WFL_FW_After_TieChanged".Translate().ToString());
            if ((f & AftermathFlags.Memory) != 0) sentences.Add("WFL_FW_After_Memory".Translate().ToString());
            return sentences.Count == 0 ? FamilyText.TierWords(b.Entry.Moment.Tier).ToString() : string.Join(" ", sentences);
        }

        // ------------------------------------------------------------------ shared drawing

        /// <summary>A row of small portraits with the people's first names under them. Returns its height.</summary>
        private float DrawPeopleStrip(float x, float y, IList<int> ids, FamilyComponent component, FamilyRegistry registry, int max = 5)
        {
            if (ids == null || ids.Count == 0) return 0f;
            float cx = x;
            int shown = 0;
            foreach (var id in ids)
            {
                if (shown >= max) break;
                var p = registry.GetPerson(id);
                if (p == null) continue;
                var r = new Rect(cx, y, 28f, 34f);
                DrawPortrait(r, p, component);
                TooltipHandler.TipRegion(r, p.FullName);
                if (Widgets.ButtonInvisible(r)) { State.SelectedPerson = p.Id; State.Navigate(FamilyTab.People); }
                cx += 32f; shown++;
            }
            if (ids.Count > shown && shown > 0) { GUI.color = FamilyKit.Dim; Widgets.Label(new Rect(cx, y + 8f, 36f, 22f), "+" + (ids.Count - shown)); GUI.color = Color.white; }
            return shown == 0 ? 0f : 38f;
        }

        private static string You(string s) => s;

        /// <summary>The Current Chapter: the situation the family is mostly living through, in a few sentences.</summary>
        private void DrawChapterCard(Rect col, ref float y, FamilyRegistry registry)
        {
            var ch = view.Chapter;
            if (ch == null) return;
            var card = FamilyKit.BeginCard(col, ref y, "s.chapter", "WFL_FW_Card_Chapter".Translate(), "WFL_FW_ChapterTip".Translate(), !ch.Quiet && !ch.Unknown);
            var cv = card.View;
            float cy = 0f;
            Text.Font = GameFont.Medium;
            float th = Text.CalcHeight(ch.Title, cv.width);
            Widgets.Label(new Rect(0f, cy, cv.width, th), ch.Title);
            Text.Font = GameFont.Small;
            cy += th + 4f;
            float prose = WindowLayout.ProseWidth(cv.width);
            var prv = new Rect(0f, 0f, prose, 0f);
            foreach (var p in ch.Paragraphs) cy += FamilyKit.Label(prv, cy, p) + 2f;
            if (ch.Evidence.Length > 0)
            {
                cy += 4f;
                cy += FamilyKit.Label(cv, cy, "WFL_FW_ChapterWhy".Translate(view.NameOfPerspective(registry).ToUpperInvariant()), 0f, true);
                foreach (var s in ch.Evidence) cy += FamilyKit.Label(prv, cy, "- " + s, 6f);
            }
            if (ch.Supporting.Length > 0)
            {
                cy += 4f;
                cy += FamilyKit.Label(cv, cy, "WFL_FW_ChapterThreads".Translate(), 0f, true);
                foreach (var s in ch.Supporting) cy += FamilyKit.Label(cv, cy, "- " + s, 6f);
            }
            if (ch.Lead != null && FamilyKit.Action(new Rect(0f, cy + 4f, 170f, 24f), "WFL_FW_OpenThread".Translate(), true)) State.Navigate(ch.Lead.Tab, ch.Lead.Sub);
            if (ch.Lead != null) cy += 32f;
            FamilyKit.EndCard(card, cy, ref y);
        }

        /// <summary>One situation as a card. Compact on the Summary; with its reasons, paths and what the acting pawn can do on the Story tab.</summary>
        private void DrawThreadCard(Rect col, ref float y, ThreadCard c, FamilyComponent component, FamilyRegistry registry, bool full)
        {
            var t = c.T;
            var card = FamilyKit.BeginCard(col, ref y, (full ? "tf." : "tc.") + t.Id, c.Title, c.Detail.Length > 0 ? c.Detail : null, t.Severity >= 2);
            var cv = card.View;
            float cy = 0f;
            cy += FamilyKit.Label(cv, cy, c.Summary);
            if (c.State.Length > 0)
            {
                float bw = FamilyKit.BadgeWidth(c.State);
                FamilyKit.Badge(new Rect(0f, cy + 2f, bw, 18f), c.State, t.Severity >= 2 ? FamilyKit.Urgent : t.Severity == 1 ? FamilyKit.Warn : FamilyKit.Trust);
                cy += 24f;
            }
            cy += DrawPeopleStrip(0f, cy, t.People, component, registry);
            if (full)
            {
                if (c.Why.Length > 0) { cy += 2f; cy += FamilyKit.Label(cv, cy, "WFL_FW_ThreadWhy".Translate(), 0f, true); cy += FamilyKit.Label(cv, cy, c.Why, 6f); foreach (var fact in c.Facts) cy += FamilyKit.Label(cv, cy, fact, 6f); }
                if (c.Recent.Length > 0) { cy += 2f; cy += FamilyKit.Label(cv, cy, "WFL_FW_ThreadRecent".Translate(), 0f, true); cy += FamilyKit.Label(cv, cy, c.Recent, 6f); }
                if (c.Paths.Length > 0)
                {
                    cy += 2f; cy += FamilyKit.Label(cv, cy, "WFL_FW_ThreadPaths".Translate(), 0f, true);
                    foreach (var p in c.Paths) cy += FamilyKit.Label(cv, cy, "- " + p, 6f);
                }
                if (c.Actions.Length > 0)
                {
                    int viewer = component.PerspectivePawnId;
                    string who = viewer == 0 ? "" : FamilyUi.NameOf(registry, viewer);
                    cy += 2f;
                    cy += FamilyKit.Label(cv, cy, (viewer == 0 ? "WFL_FW_ThreadActionsAny" : "WFL_FW_ThreadActions").Translate(who), 0f, true);
                    foreach (var a in c.Actions) cy += FamilyKit.Label(cv, cy, "- " + a, 6f);
                }
            }
            else if (c.Why.Length > 0) cy += FamilyKit.Label(cv, cy, c.Why, 0f, true, GameFont.Tiny);
            var open = new Rect(0f, cy + 4f, 130f, 24f);
            if (FamilyKit.Action(open, "WFL_FW_OpenThread".Translate(), true))
            {
                if (t.PersonId != 0) State.SelectedPerson = t.PersonId;
                State.Navigate(t.Tab, t.Sub);
            }
            cy += 32f;
            FamilyKit.EndCard(card, cy, ref y);
        }

        /// <summary>Lays cards into as many columns as fit, each no wider than a card should be, always adding to the shortest column.</summary>
        private void PlaceCards<T>(Rect v, ref float y, IList<T> items, Action<Rect, float[], int, T> draw)
        {
            int cols = WindowLayout.CardColumns(v.width);
            float w = WindowLayout.CardWidth(v.width, cols);
            var ys = new float[cols];
            for (int i = 0; i < cols; i++) ys[i] = y;
            foreach (var item in items)
            {
                int c = 0;
                for (int i = 1; i < cols; i++) if (ys[i] < ys[c]) c = i;
                draw(new Rect(c * (w + WindowLayout.Gap), 0f, w, 0f), ys, c, item);
            }
            y = ys.Max();
        }

        /// <summary>Gold for what defines the family, otherwise green for a good outcome, red for a harmful one, grey for nothing changed, blue for what is ongoing or only told.</summary>
        internal static Color OutcomeColor(BeatCard c) =>
            c.Importance == StoryImportance.Defining ? FamilyKit.Gold : c.Informational ? FamilyKit.Trust : c.Outcome > 0 ? FamilyKit.Closeness : c.Outcome < 0 ? FamilyKit.Tension : FamilyKit.Unknown;

        private void DrawBeatCard(Rect v, ref float y, BeatCard c, FamilyComponent component, FamilyRegistry registry, float width)
        {
            if (c.RoutineSummary)
            {
                y += FamilyKit.Label(new Rect(0f, 0f, width, 0f), y, c.Body, 4f, true) + 4f;
                return;
            }
            const float pad = 8f;
            float inner = width - 2f * pad;
            bool expanded = c.Grouped && expandedBeats.Contains(c.Key);
            // The title takes the card's width (less the importance tag) and wraps; it is never cut off.
            float tagW = c.ImportanceWord.Length > 0 ? FamilyKit.BadgeWidth(c.ImportanceWord) : 0f;
            Text.Font = GameFont.Small;
            float titleW = inner - 4f - (tagW > 0f ? tagW + 8f : 0f);
            float titleH = Text.CalcHeight(c.Type, titleW);
            float bodyH = string.IsNullOrEmpty(c.Body) ? 0f : Text.CalcHeight(c.Body, inner - 4f);
            Text.Font = GameFont.Tiny;
            float consH = string.IsNullOrEmpty(c.Consequence) ? 0f : Text.CalcHeight(c.Consequence, inner - 4f);
            float causeH = string.IsNullOrEmpty(c.Cause) ? 0f : 16f;
            Text.Font = GameFont.Small;
            bool anyPeople = c.People.Count > 0;
            bool canPair = c.LinkA != 0 && c.LinkB != 0;
            bool manyPeople = !canPair && c.People.Count >= 3;
            float footer = (c.Grouped || c.LinkA != 0 || manyPeople) ? 28f : 0f;
            float detailsH = expanded ? c.Details.Count * 16f + 4f : 0f;
            float h = pad + titleH + 18f + (anyPeople ? 38f : 0f) + bodyH + (consH > 0f ? consH + 2f : 0f) + causeH + detailsH + footer + pad;
            if (FamilyKit.Cull && (y + h < FamilyKit.ViewTop - 40f || y > FamilyKit.ViewBottom + 40f)) { y += h + 4f; return; }
            var rect = new Rect(0f, y, width, h);
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.14f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), OutcomeColor(c));
            float cy = y + pad;
            Widgets.Label(new Rect(pad + 4f, cy, titleW, titleH), c.Type);
            if (tagW > 0f) FamilyKit.Badge(new Rect(rect.xMax - pad - tagW, cy + 1f, tagW, 18f), c.ImportanceWord, c.Importance == StoryImportance.Defining ? FamilyKit.Gold : c.Importance == StoryImportance.Major ? FamilyKit.Warn : FamilyKit.Unknown);
            cy += titleH;
            Text.Font = GameFont.Tiny; GUI.color = FamilyKit.Dim;
            Widgets.Label(new Rect(pad + 4f, cy, inner - 4f, 16f), c.Date);
            GUI.color = Color.white; Text.Font = GameFont.Small;
            cy += 18f;
            if (anyPeople) { DrawPeopleStrip(pad + 4f, cy, c.People, component, registry); cy += 38f; }
            Text.Font = GameFont.Small;
            if (bodyH > 0f) { Widgets.Label(new Rect(pad + 4f, cy, inner - 4f, bodyH), c.Body); cy += bodyH; }
            if (consH > 0f)
            {
                Text.Font = GameFont.Tiny; GUI.color = new Color(0.72f, 0.78f, 0.62f);
                Widgets.Label(new Rect(pad + 4f, cy + 2f, inner - 4f, consH), c.Consequence);
                GUI.color = Color.white; Text.Font = GameFont.Small; cy += consH + 2f;
            }
            if (causeH > 0f)
            {
                Text.Font = GameFont.Tiny; GUI.color = FamilyKit.Dim;
                Widgets.Label(new Rect(pad + 4f, cy, inner - 4f, 16f), c.Cause);
                GUI.color = Color.white; Text.Font = GameFont.Small; cy += 16f;
            }
            if (expanded)
            {
                Text.Font = GameFont.Tiny; GUI.color = FamilyKit.Dim;
                foreach (var d in c.Details) { Widgets.Label(new Rect(pad + 12f, cy, inner - 12f, 16f), d.Truncate(inner - 12f)); cy += 16f; }
                GUI.color = Color.white; Text.Font = GameFont.Small; cy += 4f;
            }
            if (footer > 0f)
            {
                float fx = pad + 4f;
                if (c.Grouped)
                {
                    string label = expanded ? "WFL_FW_HideDetails".Translate().ToString() : "WFL_FW_ShowDetails".Translate(c.Details.Count).ToString();
                    if (FamilyKit.Action(new Rect(fx, cy, 130f, 22f), label, true)) { if (!expandedBeats.Add(c.Key)) expandedBeats.Remove(c.Key); }
                    fx += 136f;
                }
                if (canPair)
                {
                    string label = "WFL_FW_ViewPair".Translate(c.LinkAName, c.LinkBName).ToString();
                    float bw = Mathf.Min(inner - fx + pad, Text.CalcSize(label).x + 20f);
                    if (FamilyKit.Action(new Rect(fx, cy, bw, 22f), label.Truncate(bw - 8f), true, null, label)) { State.SelectedPerson = c.LinkA; State.SelectedRelation = c.LinkB; State.Navigate(FamilyTab.Relationships); }
                }
                else if (manyPeople)
                {
                    if (FamilyKit.Action(new Rect(fx, cy, 150f, 22f), "WFL_FW_ViewParticipants".Translate(), true))
                        Find.WindowStack.Add(new FloatMenu(c.People.Select(id => { int pid = id; return new FloatMenuOption(FamilyUi.NameOf(registry, pid), () => { State.SelectedPerson = pid; State.Navigate(FamilyTab.People); }); }).ToList()));
                }
                else if (c.LinkA != 0)
                {
                    string label = "WFL_FW_SelectPerson".Translate(c.LinkAName).ToString();
                    if (FamilyKit.Action(new Rect(fx, cy, Text.CalcSize(label).x + 20f, 22f), label, true)) { State.SelectedPerson = c.LinkA; State.Navigate(FamilyTab.People); }
                }
            }
            y += h + 4f;
        }


        // ------------------------------------------------------------------ the tab

        private void DrawStory(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            if (!view.Insider && (StorySub)State.EventsSub == StorySub.Current)
            {
                // A visitor's Current: the situations they are part of, then their own part in the House's story. Not the House's internal plans.
                if (view.Threads.Count > 0)
                {
                    FamilyKit.SectionHeader(v, ref y, "WFL_FW_ActiveThreads".Translate());
                    PlaceCards(v, ref y, view.Threads, (rect, ys, c, t) => { float cy = ys[c]; DrawThreadCard(rect, ref cy, t, component, registry, true); ys[c] = cy; });
                }
                if (view.Highlights.Count > 0)
                {
                    FamilyKit.SectionHeader(v, ref y, "WFL_FW_StoryOutsider".Translate());
                    foreach (var c in view.Highlights) DrawBeatCard(v, ref y, c, component, registry, Mathf.Min(v.width, 620f));
                }
                if (view.Threads.Count == 0 && view.Highlights.Count == 0) { var pub = new Rect(0f, 0f, Mathf.Min(v.width, 720f), 0f); DrawChapterCard(pub, ref y, registry); }
                return;
            }
            switch ((StorySub)State.EventsSub)
            {
                case StorySub.Timeline: DrawTimeline(v, ref y, component, registry); break;
                case StorySub.Chronicle: DrawChronicle(v, ref y, component, registry); break;
                default: DrawStoryCurrent(v, ref y, component, registry); break;
            }
        }

        // ------------------------------------------------------------------ current

        private void DrawStoryCurrent(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            int viewer = component.PerspectivePawnId;
            var items = UpcomingFor(component, registry);
            bool anyVisit = registry.CampTrips.Any(t => t.FamilyId == house.Id && t.Open) || registry.KinVisits.Any(k => k.FamilyId == house.Id && k.Open);
            var incidents = registry.Incidents.Where(i => !i.Resolved && (i.ActorFamily == house.Id || i.VictimFamily == house.Id) && registry.KnowsIncident(viewer, i)).OrderByDescending(i => i.Tick).Take(6).ToList();
            var plots = registry.Plots.Where(p => p.Active && (view.Relevant.Contains(p.OrganizerId) || view.Relevant.Contains(p.TargetId)) && registry.KnowsPlot(viewer, p)).Take(4).ToList();
            var promises = registry.Commitments.Where(c => c.IsOpen && (view.Relevant.Contains(c.FromId) || view.Relevant.Contains(c.ToId)) && (viewer == 0 || c.FromId == viewer || c.ToId == viewer) && c.DueTick - Activities.Now <= FamilyAlerts.PromiseWindow).Take(4).ToList();
            bool nothingPlanned = items.Count == 0 && !anyVisit && incidents.Count == 0 && plots.Count == 0 && promises.Count == 0;

            if (nothingPlanned) DrawNothingPlanned(v, ref y, component, registry);

            if (view.Threads.Count > 0)
            {
                FamilyKit.SectionHeader(v, ref y, "WFL_FW_ActiveThreads".Translate());
                PlaceCards(v, ref y, view.Threads, (rect, ys, c, t) => { float cy = ys[c]; DrawThreadCard(rect, ref cy, t, component, registry, true); ys[c] = cy; });
            }

            var col = new Rect(0f, 0f, Mathf.Min(v.width, 720f), 0f);
            if (!nothingPlanned)
            {
                DrawUpcomingCard(col, ref y, component, registry, items);
                if (incidents.Count > 0 || plots.Count > 0 || promises.Count > 0) DrawOpenMatters(col, ref y, registry, house, viewer, incidents, plots, promises);
            }
            DrawTraditions(col, ref y, registry, house);
            DrawSupplyRequests(v, ref y, registry);
        }

        /// <summary>No ceremonies, promises or visits: said plainly, with what the family is doing instead and what could start something.</summary>
        private void DrawNothingPlanned(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            var col = new Rect(0f, 0f, Mathf.Min(v.width, 720f), 0f);
            var card = FamilyKit.BeginCard(col, ref y, "e.empty", null);
            var cv = card.View;
            float cy = 0f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, cy, cv.width, 30f), "WFL_FW_NothingPlannedTitle".Translate().ToString().ToUpperInvariant());
            Text.Font = GameFont.Small;
            cy += 34f;
            var amb = house.Ambition;
            bool aims = amb != null && amb.Primary != HouseAmbitionKind.None && amb.Primary != HouseAmbitionKind.Contentment && house.Situation != null && house.Situation.EvaluatedTick > 0;
            cy += FamilyKit.Label(cv, cy, aims
                ? "WFL_FW_NothingPlannedAims".Translate(view.Name, ("WFL_Ambition_" + amb.Primary).Translate().ToString().ToLowerInvariant())
                : "WFL_FW_NothingPlanned".Translate(view.Name));
            cy += 6f;
            cy += FamilyKit.Label(cv, cy, "WFL_FW_ContextHeader".Translate(), 0f, true);
            if (aims) cy += FamilyKit.Label(cv, cy, "- " + "WFL_FW_ContextAmbition".Translate(("WFL_Ambition_" + amb.Primary).Translate()), 6f);
            var top = view.Highlights.FirstOrDefault();
            if (top != null && !top.RoutineSummary) cy += FamilyKit.Label(cv, cy, "- " + "WFL_FW_ContextRecent".Translate(top.Date, top.Type), 6f);
            var tradition = house.Traditions.FirstOrDefault(t => !t.Muted);
            if (tradition != null) cy += FamilyKit.Label(cv, cy, "- " + "WFL_FW_ContextTradition".Translate(FamilyUi.TraditionNameById(tradition.Id), tradition.TimesHeld), 6f);
            cy += FamilyKit.Label(cv, cy, "- " + "WFL_FW_ContextCauses".Translate(), 6f, true);
            FamilyKit.EndCard(card, cy, ref y);
        }

        private void DrawUpcomingCard(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, List<UpcomingItem> items)
        {
            var house = view.House;
            var card = FamilyKit.BeginCard(col, ref y, "e.upcoming", "WFL_FW_Card_Upcoming".Translate());
            var cv = card.View;
            float cy = 0f;
            if (items.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NothingUpcoming".Translate());
            foreach (var u in items)
            {
                float top = cy;
                DrawUpcomingLine(cv, ref cy, registry, u);
                if (u.Kind == UpcomingKind.Ceremony)
                {
                    var plan = registry.PlanFor(u.RefId);
                    if (plan != null)
                    {
                        int planId = plan.Id;
                        if (FamilyKit.Action(new Rect(cv.width - 118f, top + 1f, 116f, 22f), "WFL_Plan_Open".Translate(), true, null, "WFL_FW_PlanTip".Translate())) Find.WindowStack.Add(new Window_CeremonyPlan(planId));
                    }
                }
            }
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
        }

        private void DrawOpenMatters(Rect col, ref float y, FamilyRegistry registry, Lineage house, int viewer, List<FamilyIncident> incidents, List<RevengePlot> plots, List<Commitment> promises)
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

        /// <summary>The house's traditions, with the same mute and hold-now controls as before.</summary>
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

        /// <summary>Requests between households for goods, shown only when there are any.</summary>
        private void DrawSupplyRequests(Rect v, ref float y, FamilyRegistry registry)
        {
            int viewer = FamilyComponent.Current?.PerspectivePawnId ?? 0;
            var requests = registry.Requests.Where(r => (view.Relevant.Contains(r.RequesterId) || view.Relevant.Contains(r.DonorId)) && (viewer == 0 || r.RequesterId == viewer || r.DonorId == viewer)).OrderByDescending(r => r.CreatedTick).Take(8).ToList();
            if (requests.Count == 0) return;
            var card = FamilyKit.BeginCard(new Rect(0f, 0f, Mathf.Min(v.width, 720f), 0f), ref y, "e.supply", "WFL_Stories_Supplies".Translate());
            var cv = card.View;
            float cy = 0f;
            cy += FamilyKit.Label(cv, cy, "WFL_Stories_Claims".Translate(registry.Claims.Count(c => c.HolderFamily == view.LineageId)), 0f, true);
            foreach (var supply in requests)
                cy += FamilyKit.Label(cv, cy, "WFL_Stories_Request".Translate(FamilyUi.NameOf(registry, supply.RequesterId), ("WFL_SupplyApproach_" + supply.Approach).Translate(), supply.Count, supply.Definition, ("WFL_SupplyState_" + supply.State).Translate()), 0f);
            FamilyKit.EndCard(card, cy, ref y);
        }

        // ------------------------------------------------------------------ filters

        private void DrawEventFilters(Rect v, ref float y, bool timeline)
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
            rowY += 28f;
            if (timeline)
            {
                x = 0f;
                var registry = FamilyComponent.Current?.Registry;
                string who = State.StoryPerson == 0 || registry == null ? "WFL_FW_FilterEveryone".Translate().ToString() : "WFL_FW_FilterPerson".Translate(FamilyUi.NameOf(registry, State.StoryPerson)).ToString();
                float pw = FamilyKit.BadgeWidth(who) + 24f;
                if (FamilyKit.Action(new Rect(x, rowY, pw, 24f), who, true, null, "WFL_FW_FilterPersonTip".Translate()))
                {
                    var options = new List<FloatMenuOption> { new FloatMenuOption("WFL_FW_FilterEveryone".Translate(), () => State.StoryPerson = 0) };
                    foreach (var p in view.Members.Where(p => p.InColony).OrderBy(p => p.Name).Take(30)) { int id = p.Id; options.Add(new FloatMenuOption(p.FullName, () => State.StoryPerson = id)); }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                x += pw + 6f;
                if (FamilyKit.ToggleChip(new Rect(x, rowY, FamilyKit.BadgeWidth("WFL_FW_ShowRoutine".Translate()) + 14f, 24f), "WFL_FW_ShowRoutine".Translate(), State.ShowRoutine, "WFL_FW_ShowRoutineTip".Translate())) State.ShowRoutine = !State.ShowRoutine;
                rowY += 28f;
            }
            y = rowY + 4f;
        }

        // ------------------------------------------------------------------ timeline and chronicle

        private void DrawTimeline(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            DrawEventFilters(v, ref y, true);
            DrawTimelineCards(v, ref y, component, registry, StorySub.Timeline);
        }

        private void DrawChronicle(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            if (!view.Insider) { DrawEventFilters(v, ref y, false); DrawTimelineCards(v, ref y, component, registry, StorySub.Chronicle); return; }
            var house = view.House;
            var col = new Rect(0f, 0f, Mathf.Min(v.width, 720f), 0f);
            var culture = FamilyKit.BeginCard(col, ref y, "e.culture", "WFL_Culture".Translate());
            var cv = culture.View;
            float cy = 0f;
            var values = registry.Values(house.Id);
            if (values.Count == 0) cy += FamilyKit.Label(cv, cy, "WFL_NoValues".Translate(), 0f, true);
            else cy += FamilyKit.Label(cv, cy, "WFL_ValuesLine".Translate(string.Join(", ", values.Select(FamilyUi.DimName))));
            var legacies = registry.Legacies(house.Id);
            if (legacies.Count > 0) cy += FamilyKit.Label(cv, cy, "WFL_LegacyLine".Translate(string.Join(", ", legacies)));
            foreach (var m in registry.MemoriesOf(house.Id).OrderByDescending(x => x.Weight).ThenByDescending(x => x.Tick).Take(5))
                cy += FamilyKit.Label(cv, cy, FamilyText.Date(m.Tick) + " - " + ("WFL_Mem_" + m.Kind).Translate(FamilyUi.NameOf(registry, m.A), FamilyUi.NameOf(registry, m.B)), 6f, true);
            FamilyKit.EndCard(culture, cy, ref y);

            DrawEventFilters(v, ref y, false);
            DrawTimelineCards(v, ref y, component, registry, StorySub.Chronicle);
        }

        private void DrawTimelineCards(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry, StorySub sub)
        {
            int now = Find.TickManager.TicksGame;
            if (timelineSub != (int)sub || timelineMask != State.EventFilter || timelinePerson != State.StoryPerson || timelineRoutine != State.ShowRoutine) timelineShown = 30;
            if (timelineCards == null || timelineRevision != component.Revision || timelineViewer != component.PerspectivePawnId || timelineLineage != State.LineageId
                || timelineSub != (int)sub || timelineMask != State.EventFilter || timelinePerson != State.StoryPerson || timelineRoutine != State.ShowRoutine || now - timelineTick >= FamilyView.RefreshTicks)
            {
                timelineCards = BuildCards(component, registry, sub, now);
                timelineRevision = component.Revision; timelineViewer = component.PerspectivePawnId; timelineLineage = State.LineageId;
                timelineSub = (int)sub; timelineMask = State.EventFilter; timelinePerson = State.StoryPerson; timelineRoutine = State.ShowRoutine; timelineTick = now;
            }
            if (timelineCards.Count == 0)
            {
                FamilyKit.EmptyState(v, ref y, (sub == StorySub.Timeline ? "WFL_FW_NothingRecent" : "WFL_FW_NothingChronicle").Translate());
                return;
            }
            float width = Mathf.Min(v.width, 640f);
            FamilyKit.Cull = true;
            int shown = Mathf.Min(timelineCards.Count, timelineShown);
            for (int i = 0; i < shown; i++) DrawBeatCard(v, ref y, timelineCards[i], component, registry, width);
            FamilyKit.Cull = false;
            if (timelineCards.Count > shown)
            {
                if (FamilyKit.Action(new Rect(0f, y + 2f, 180f, 26f), "WFL_FW_ShowMore".Translate(timelineCards.Count - shown), true)) timelineShown += 30;
                y += 32f;
            }
        }

        private List<BeatCard> BuildCards(FamilyComponent component, FamilyRegistry registry, StorySub sub, int now)
        {
            var house = view.House;
            int viewer = component.PerspectivePawnId;
            var beats = sub == StorySub.Timeline ? StoryTimeline.Recent(registry, house, viewer, now, State.EventFilter, State.ShowRoutine) : StoryTimeline.Chronicle(registry, house, viewer, State.EventFilter);
            var cards = new List<BeatCard>(beats.Count);
            foreach (var b in beats)
            {
                var card = MakeBeat(registry, b, viewer);
                if (card == null) continue;
                if (State.StoryPerson != 0 && !card.RoutineSummary && !card.People.Contains(State.StoryPerson)) continue;
                cards.Add(card);
            }
            return cards;
        }
    }
}
