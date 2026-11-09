using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // The Families window's storytelling layer. Everything here is a presentation of records the registry already keeps: it ranks them, groups the
    // ones that belong together, and names the situations they add up to. It creates no event, changes no value and saves nothing. Text is never
    // built here: each line is a translation key plus its arguments (a Narrative), so the game layer translates it and a translator can reword it.
    // The window builds this once when its data changes, not while drawing.

    // ------------------------------------------------------------------ text as keys

    /// <summary>One line of the story, as a translation key and the names or numbers it needs. An empty argument stands for someone who is no longer known.
    /// An argument starting with "WFL_" is itself a key, "@d:" is a tick shown as a date and "@t:" is a tradition id; the game layer resolves them.</summary>
    public struct Narrative
    {
        public string Key;
        public string[] Args;

        public Narrative(string key, params string[] args) { Key = key; Args = args ?? new string[0]; }
        public bool Empty => string.IsNullOrEmpty(Key);

        /// <summary>The key to use when the person being played is the one the line is about, so the wording can say "you".</summary>
        public static Narrative About(int viewer, int subject, string key, params string[] args) =>
            new Narrative(IsPlayed(viewer) && viewer == subject ? key + "_You" : key, args);

        /// <summary>
        /// Whether the player has actually taken control of this person. Looking at someone's view of the family is not playing them: the story only says "you" for a
        /// person being played, and uses the name otherwise. The game layer supplies the answer; with no hook nobody is being played.
        /// </summary>
        public static Func<int, bool> PlayedHook;
        public static bool IsPlayed(int pawnId) => pawnId != 0 && PlayedHook != null && PlayedHook(pawnId);
    }

    // ------------------------------------------------------------------ how much a thing matters

    public enum StoryImportance : byte { Routine = 0, Meaningful = 1, Major = 2, Defining = 3 }

    public static class StoryRelevance
    {
        /// <summary>A moment between two people. The mod's own weight comes first; what the moment set going lifts it.</summary>
        public static StoryImportance Of(EventRecord e)
        {
            if (e == null) return StoryImportance.Routine;
            if (e.Weight == EventWeight.Defining) return StoryImportance.Defining;
            if (e.Weight == EventWeight.Ambient) return StoryImportance.Routine;
            var f = (AftermathFlags)e.Aftermath;
            if ((f & (AftermathFlags.RiftOpened | AftermathFlags.RiftResolved | AftermathFlags.TieChanged | AftermathFlags.PromiseKept | AftermathFlags.Memory)) != 0) return StoryImportance.Major;
            return StoryImportance.Meaningful;
        }

        public static StoryImportance Of(FamilyEvent e)
        {
            if (e == null) return StoryImportance.Routine;
            switch (e.Kind)
            {
                case FamilyEventKind.Birth: case FamilyEventKind.Died: case FamilyEventKind.MarriageFormed: case FamilyEventKind.LineageFounded:
                case FamilyEventKind.SocialBranch: case FamilyEventKind.FamiliesJoined: case FamilyEventKind.RiftResolved:
                    return StoryImportance.Defining;
                case FamilyEventKind.PartnershipFormed: case FamilyEventKind.PartnershipEnded: case FamilyEventKind.CameOfAge: case FamilyEventKind.Reunion:
                case FamilyEventKind.RiftOpened: case FamilyEventKind.TraditionAdopted: case FamilyEventKind.StepFamilyFormed: case FamilyEventKind.SocialMembership:
                    return StoryImportance.Major;
                case FamilyEventKind.Anniversary: case FamilyEventKind.TraditionHeld: case FamilyEventKind.TraditionAbandoned: case FamilyEventKind.LineageAffiliated:
                    return StoryImportance.Meaningful;
                default: return StoryImportance.Routine;      // joined, left, households, records starting, bond drift
            }
        }

        public static StoryImportance Of(CeremonyRecord c) =>
            c == null ? StoryImportance.Routine
            : c.Kind == CeremonyKind.Wedding || c.Kind == CeremonyKind.Funeral || c.Kind == CeremonyKind.GrandReunion || c.Kind == CeremonyKind.Reconciliation ? StoryImportance.Defining
            : StoryImportance.Major;

        public static StoryImportance Of(TimelineEntry t)
        {
            if (t == null) return StoryImportance.Routine;
            switch (t.Kind)
            {
                case TimelineKind.Moment: return Of(t.Moment);
                case TimelineKind.Ceremony: return Of(t.Ceremony);
                case TimelineKind.Chronicle: return Of(t.Chronicle);
                case TimelineKind.Incident: return StoryImportance.Major;
                default: return StoryImportance.Major;       // a plot
            }
        }
    }

    // ------------------------------------------------------------------ one story beat, possibly several moments

    /// <summary>One entry of the story: a single thing that happened, or several moments of the same kind that read better together.</summary>
    public sealed class StoryBeat
    {
        public TimelineEntry Entry;                           // the newest moment (or the only entry)
        public List<EventRecord> Parts = new List<EventRecord>();
        public StoryImportance Importance;
        public int Tick, FirstTick;
        public EventCategory Category;
        public int Initiator;
        public List<int> Others = new List<int>();
        public bool Grouped => Parts.Count > 1;
        /// <summary>A teaching moment: the event teaches a skill.</summary>
        public bool Teaching;
        /// <summary>Everyday matters folded into one line ("N small changes"); Entry is null.</summary>
        public int RoutineCount;
        public bool IsRoutineSummary => RoutineCount > 0;
    }

    public static class StoryConsolidation
    {
        /// <summary>Moments of one kind from one person, this close together, are told as one scene.</summary>
        public const int GroupWindow = 60000;

        /// <summary>Which way a moment went for the second person: -1 badly, 0 neutral, +1 well. Moments that went in different ways are never told as one.</summary>
        public static int Outcome(EventRecord e)
        {
            int tier = e.Tier == EventTier.Poor ? -1 : e.Tier == EventTier.Flat ? 0 : 1;
            int feel = e.FeelB >= 20 ? 1 : e.FeelB <= -20 ? -1 : 0;
            return Math.Sign(tier + feel * 2);
        }

        public static bool Compatible(EventRecord a, EventRecord b)
        {
            if (a.EventId != b.EventId || a.A != b.A || a.Third != b.Third) return false;
            if (Math.Abs(a.Tick - b.Tick) > GroupWindow) return false;
            if (Outcome(a) != Outcome(b)) return false;                 // a different response is a different story
            if ((a.Tier == EventTier.Poor) != (b.Tier == EventTier.Poor)) return false;
            var fa = (AftermathFlags)a.Aftermath; var fb = (AftermathFlags)b.Aftermath;
            const AftermathFlags consequential = AftermathFlags.RiftOpened | AftermathFlags.RiftResolved | AftermathFlags.PromiseKept | AftermathFlags.PromiseMade;
            return (fa & consequential) == (fb & consequential);       // one that opened a rift stays on its own
        }

        /// <summary>Folds compatible moments (newest first, as the timeline gives them) into beats. Order and everything else are kept.</summary>
        public static List<StoryBeat> Build(IList<TimelineEntry> newestFirst, bool includeRoutine)
        {
            var beats = new List<StoryBeat>();
            int routine = 0;
            foreach (var t in newestFirst)
            {
                var importance = StoryRelevance.Of(t);
                if (importance == StoryImportance.Routine && !includeRoutine) { routine++; continue; }
                if (t.Kind == TimelineKind.Moment)
                {
                    StoryBeat into = null;
                    for (int i = beats.Count - 1; i >= 0 && i >= beats.Count - 12; i--)
                    {
                        var b = beats[i];
                        if (b.Parts.Count == 0 || b.Entry.Kind != TimelineKind.Moment) continue;
                        if (Compatible(b.Parts[b.Parts.Count - 1], t.Moment)) { into = b; break; }
                    }
                    if (into != null)
                    {
                        into.Parts.Add(t.Moment); if (!into.Others.Contains(t.Moment.B)) into.Others.Add(t.Moment.B);
                        into.FirstTick = Math.Min(into.FirstTick, t.Tick);
                        if (importance > into.Importance) into.Importance = importance;
                        continue;
                    }
                    var def = EventCatalog.Get(t.Moment.EventId);
                    var beat = new StoryBeat { Entry = t, Importance = importance, Tick = t.Tick, FirstTick = t.Tick, Category = t.Category, Initiator = t.Moment.A, Teaching = def != null && !string.IsNullOrEmpty(def.TeachSkill) };
                    beat.Parts.Add(t.Moment); beat.Others.Add(t.Moment.B);
                    beats.Add(beat);
                }
                else beats.Add(new StoryBeat { Entry = t, Importance = importance, Tick = t.Tick, FirstTick = t.Tick, Category = t.Category });
            }
            if (routine > 0) beats.Add(new StoryBeat { RoutineCount = routine, Tick = 0, Importance = StoryImportance.Routine });
            return beats;
        }

        /// <summary>The few developments that matter most: defining before major before meaningful, newest first within each.</summary>
        public static List<StoryBeat> Highlights(IEnumerable<StoryBeat> beats, int take) =>
            beats.Where(b => !b.IsRoutineSummary && b.Importance >= StoryImportance.Meaningful)
                 .OrderByDescending(b => b.Importance).ThenByDescending(b => b.Tick).Take(take).ToList();
    }

    // ------------------------------------------------------------------ the story timeline for one house

    public static class StoryTimeline
    {
        public static List<StoryBeat> Recent(FamilyRegistry r, Lineage house, int viewer, int now, int mask, bool includeRoutine = false) =>
            StoryConsolidation.Build(FamilyTimeline.Recent(r, house, viewer, now, mask).Where(t => StoryVisibility.Visible(r, house, viewer, t)).ToList(), includeRoutine);

        /// <summary>Only what defines the family's history. Moments told together are still one entry.</summary>
        public static List<StoryBeat> Chronicle(FamilyRegistry r, Lineage house, int viewer, int mask)
        {
            var entries = FamilyTimeline.Chronicle(r, house, viewer, mask).Where(t => StoryRelevance.Of(t) == StoryImportance.Defining && StoryVisibility.Visible(r, house, viewer, t)).ToList();
            return StoryConsolidation.Build(entries, false);
        }
    }

    // ------------------------------------------------------------------ counts and names

    /// <summary>Family is kinship; House is membership and standing; a household is who shares a home. These are three different counts.</summary>
    public struct HouseCounts
    {
        public int HouseMembers, Residents, Households, ResidentsFromOtherHouses, MembersLivingElsewhere;
        public bool Differ => Residents != HouseMembers;
    }

    public static class HouseCountsOf
    {
        public static HouseCounts Of(FamilyRegistry r, Lineage house)
        {
            var c = new HouseCounts();
            if (r == null || house == null) return c;
            // A House member is anyone who belongs to the House (by descent, marriage or affiliation): the same test every action and the sidebar use.
            var members = new HashSet<int>(r.Persons.Where(p => p.InColony && r.BelongsToFamily(p, house.Id)).Select(p => p.Id));
            var residents = new HashSet<int>();
            var households = r.HouseholdsOfLineage(house.Id);
            foreach (var h in households)
                foreach (var id in h.ResidentIds)
                {
                    var p = r.GetPerson(id);
                    if (p != null && p.InColony) residents.Add(id);
                }
            c.HouseMembers = members.Count;
            c.Residents = residents.Count;
            c.Households = households.Count;
            c.ResidentsFromOtherHouses = residents.Count(id => !members.Contains(id));
            c.MembersLivingElsewhere = members.Count(id => !residents.Contains(id));
            return c;
        }
    }

    public static class HouseholdNames
    {
        /// <summary>A name a player would use: "Jessop Household", "Strick and Madeline's Household". Never an id.</summary>
        public static Narrative NameOf(FamilyRegistry r, Household h)
        {
            if (r == null || h == null) return new Narrative("WFL_FW_HH_Plain");
            var people = h.ResidentIds.Select(id => r.GetPerson(id)).Where(p => p != null && p.InColony).ToList();
            var adults = people.Where(p => !p.Juvenile).OrderBy(p => p.BirthAbsTick).ToList();
            if (adults.Count == 0) adults = people;
            if (adults.Count == 0) return new Narrative("WFL_FW_HH_Plain");
            var a = adults[0];
            if (adults.Count >= 2)
            {
                var b = adults[1];
                bool partners = r.KindOf(a, b) == BondKind.Partner;
                bool sameName = !string.IsNullOrEmpty(a.LastName) && string.Equals(a.LastName, b.LastName, StringComparison.OrdinalIgnoreCase);
                if (partners && !sameName) return new Narrative("WFL_FW_HH_Pair", Short(a), Short(b));
            }
            string surname = Surname(adults);
            return string.IsNullOrEmpty(surname) ? new Narrative("WFL_FW_HH_Of", Short(a)) : new Narrative("WFL_FW_HH_Name", surname);
        }

        private static string Surname(List<PersonRecord> adults) =>
            adults.Where(p => !string.IsNullOrEmpty(p.LastName)).GroupBy(p => p.LastName, StringComparer.OrdinalIgnoreCase)
                  .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";

        public static string Short(PersonRecord p) => p == null ? "" : !string.IsNullOrEmpty(p.FirstName) ? p.FirstName : p.Name ?? "";
    }

    // ------------------------------------------------------------------ story threads

    public enum ThreadKind : byte
    {
        Dealings, Feud, Rift, Plot, Ceremony, Visit, Camp, Ambition, Turning, Promise, Negotiation, Petition, Grievance,
        Food, Housing, Safety, Capability, Teaching, Tradition
    }

    /// <summary>
    /// An ongoing situation the records already hold, told as a story. It is read from the registry each time it is built and owns no state: closing
    /// it, or making it worse, happens only through the game's own systems.
    /// </summary>
    public sealed class StoryThread
    {
        public string Id = "";
        public ThreadKind Kind;
        /// <summary>1 (crisis) to 8 (ordinary life): the order in which situations lead the Current Chapter.</summary>
        public int Priority;
        public int Severity;
        public Narrative Title, Summary, Line, Why, Recent, State, Detail;
        /// <summary>More reasons that belong to the same situation, merged in from other sources.</summary>
        public List<Narrative> Facts = new List<Narrative>();
        /// <summary>Threads about the same underlying situation share a key and are shown once.</summary>
        public string MergeKey = "";
        public List<Narrative> Paths = new List<Narrative>();
        public List<Narrative> Actions = new List<Narrative>();
        public List<int> People = new List<int>();
        public FamilyTab Tab;
        public int Sub;
        public int PersonId;
    }

    public sealed class StoryChapter
    {
        public Narrative Title;
        public List<Narrative> Body = new List<Narrative>();
        public List<Narrative> Supporting = new List<Narrative>();
        /// <summary>Why the chapter reads as it does: one short line per thing that happened to the viewer.</summary>
        public List<Narrative> Evidence = new List<Narrative>();
        public StoryThread Lead;
        public bool Quiet, Unknown;
    }

    public sealed class StoryModel
    {
        public List<StoryThread> Threads = new List<StoryThread>();
        public StoryChapter Chapter = new StoryChapter();
        public List<StoryBeat> Recent = new List<StoryBeat>();
        public List<StoryBeat> Highlights = new List<StoryBeat>();
        public HouseCounts Counts;
        public Acquaintance Acquaintance;

        public static StoryModel Build(FamilyRegistry r, Lineage house, int viewer, int now, SkillProbe probe = null)
        {
            var m = new StoryModel();
            if (r == null || house == null) { m.Chapter.Unknown = true; m.Chapter.Title = new Narrative("WFL_FW_Ch_UnknownTitle"); return m; }
            m.Counts = HouseCountsOf.Of(r, house);
            bool insider = FamilyWindowFacts.IsInsider(r, house.Id, viewer);
            m.Acquaintance = Knowledge.AcquaintanceWith(r, viewer, house);
            if (!insider) { OutsiderStory.Fill(m, r, house, viewer, now, probe); return m; }
            m.Recent = StoryTimeline.Recent(r, house, viewer, now, 0);
            m.Highlights = StoryConsolidation.Highlights(m.Recent, 3);
            m.Threads = ThreadMerge.Merge(StoryThreads.Build(r, house, viewer, now, m.Recent));
            ActionAdvice.Apply(r, house, viewer, m.Threads, probe, Knowledge.HistoryWith(r, viewer, house));
            m.Chapter = StoryChapters.Build(r, house, viewer, m.Threads, m.Highlights);
            return m;
        }

        public static string PerspectiveName(FamilyRegistry r, int viewer) { var p = viewer == 0 ? null : r?.GetPerson(viewer); return HouseholdNames.Short(p); }
    }

    public static class StoryThreads
    {
        public const int RecentTurningWindow = 5 * 60000;

        private static string N(FamilyRegistry r, int id) => HouseholdNames.Short(r.GetPerson(id));
        private static string D(float v) => Math.Round(v).ToString("0");

        public static List<StoryThread> Build(FamilyRegistry r, Lineage house, int viewer, int now, List<StoryBeat> recent)
        {
            var list = new List<StoryThread>();
            if (r == null || house == null || !FamilyWindowFacts.IsInsider(r, house.Id, viewer)) return list;
            int id = house.Id;
            string houseName = r.LineageDisplayName(house);
            var colony = r.MembersOf(id).Where(p => p.InColony).ToList();
            var colonyIds = new HashSet<int>(colony.Select(p => p.Id));
            if (colony.Count == 0) return list;

            // ---- 1: crisis, feud, serious conflict
            foreach (var member in colony)
            {
                if (viewer != 0 && member.Id != viewer) continue;          // a rift is a private matter between its two people
                var rift = r.OpenRiftsOf(member.Id).FirstOrDefault();
                if (rift == null) continue;
                bool bad = rift.Severity >= 60f;
                list.Add(new StoryThread
                {
                    Id = "rift:" + rift.Id, Kind = ThreadKind.Rift, Priority = bad ? 1 : 5, Severity = bad ? 2 : 1,
                    Title = new Narrative("WFL_FW_Th_Rift_Title", N(r, rift.A), N(r, rift.B)),
                    Summary = Narrative.About(viewer, member.Id, "WFL_FW_Th_Rift_Sum", N(r, rift.A), N(r, rift.B)),
                    Line = new Narrative("WFL_FW_Th_Rift_Line", N(r, rift.A), N(r, rift.B)),
                    State = new Narrative(bad ? "WFL_FW_State_Serious" : "WFL_FW_State_Open"),
                    Why = new Narrative("WFL_FW_Th_Rift_Why", "@d:" + rift.StartTick),
                    Paths = { new Narrative("WFL_FW_Th_Rift_Path1"), new Narrative("WFL_FW_Th_Rift_Path2") },
                    Actions = { new Narrative("WFL_FW_Act_Apologize"), new Narrative("WFL_FW_Act_TimeTogether") },
                    People = { rift.A, rift.B }, Tab = FamilyTab.Relationships, PersonId = member.Id
                });
                break;
            }
            foreach (var tie in r.TiesOf(id).Where(t => t.State == FamilyTieState.Strained || t.State == FamilyTieState.Feuding).Take(2))
            {
                int otherId = tie.LineageA == id ? tie.LineageB : tie.LineageA;
                var other = r.GetLineage(otherId);
                if (other == null) continue;
                bool feud = tie.State == FamilyTieState.Feuding;
                list.Add(new StoryThread
                {
                    Id = "tie:" + otherId, Kind = ThreadKind.Feud, Priority = feud ? 1 : 5, Severity = feud ? 2 : 1,
                    Title = new Narrative(feud ? "WFL_FW_Th_Feud_Title" : "WFL_FW_Th_Strain_Title", r.LineageDisplayName(other)),
                    Summary = new Narrative(feud ? "WFL_FW_Th_Feud_Sum" : "WFL_FW_Th_Strain_Sum", houseName, r.LineageDisplayName(other)),
                    Line = new Narrative("WFL_FW_Th_Feud_Line", r.LineageDisplayName(other)),
                    State = new Narrative(feud ? "WFL_FW_State_Feuding" : "WFL_FW_State_Strained"),
                    Why = new Narrative("WFL_FW_Th_Tie_Why", D(tie.Grievance), D(tie.Goodwill)),
                    Paths = { new Narrative("WFL_FW_Th_Tie_Path1"), new Narrative("WFL_FW_Th_Tie_Path2") },
                    Actions = { new Narrative("WFL_FW_Act_Aid"), new Narrative("WFL_FW_Act_Apologize") },
                    Tab = FamilyTab.HouseAffairs
                });
            }
            foreach (var inc in r.Incidents.Where(i => !i.Resolved && i.VictimFamily == id && r.KnowsIncident(viewer, i)).OrderByDescending(i => i.Tick).Take(2))
                list.Add(new StoryThread
                {
                    Id = "incident:" + inc.Id, Kind = ThreadKind.Grievance, Priority = 5, Severity = 1,
                    Title = new Narrative("WFL_FW_Th_Incident_Title"),
                    Summary = new Narrative("WFL_FW_Th_Incident_Sum", inc.ActorId == 0 ? "" : N(r, inc.ActorId), N(r, inc.VictimId)),
                    Line = new Narrative("WFL_FW_Th_Incident_Line"),
                    State = new Narrative("WFL_FW_State_Unresolved"),
                    Why = new Narrative("WFL_FW_Th_Incident_Why"),
                    Paths = { new Narrative("WFL_FW_Th_Incident_Path1"), new Narrative("WFL_FW_Th_Incident_Path2") },
                    Actions = { new Narrative("WFL_FW_Act_Aid") },
                    People = { inc.VictimId }, Tab = FamilyTab.Story, Sub = (int)StorySub.Current
                });
            foreach (var plot in r.Plots.Where(p => p.Active && (colonyIds.Contains(p.OrganizerId) || colonyIds.Contains(p.TargetId)) && r.KnowsPlot(viewer, p)).Take(1))
                list.Add(new StoryThread
                {
                    Id = "plot:" + plot.Id, Kind = ThreadKind.Plot, Priority = 1, Severity = 2,
                    Title = new Narrative("WFL_FW_Th_Plot_Title"),
                    Summary = new Narrative("WFL_FW_Th_Plot_Sum", N(r, plot.OrganizerId), N(r, plot.TargetId)),
                    Line = new Narrative("WFL_FW_Th_Plot_Line"),
                    State = new Narrative("WFL_FW_State_Serious"),
                    Why = new Narrative("WFL_FW_Th_Plot_Why"),
                    Paths = { new Narrative("WFL_FW_Th_Plot_Path1") }, Actions = { new Narrative("WFL_FW_Act_Apologize") },
                    People = { plot.OrganizerId, plot.TargetId }, Tab = FamilyTab.Story, Sub = (int)StorySub.Current
                });

            // ---- 2: ceremony or major visitor event
            foreach (var plan in r.CeremonyPlans.Where(p => p.FamilyId == id && p.State == PlanState.Open && r.PlanVisibleTo(p, viewer)).Take(2))
            {
                var req = r.GatheringRequests.FirstOrDefault(q => q.Id == plan.RequestId);
                string kindKey = req != null ? "WFL_Ceremony_" + req.Ceremony : "WFL_Ceremony_None";
                list.Add(new StoryThread
                {
                    Id = "plan:" + plan.Id, Kind = ThreadKind.Ceremony, Priority = 2, Severity = 1,
                    Title = new Narrative("WFL_FW_Th_Ceremony_Title", kindKey),
                    Summary = new Narrative("WFL_FW_Th_Ceremony_Sum", kindKey),
                    Line = new Narrative("WFL_FW_Th_Ceremony_Line", kindKey),
                    State = new Narrative("WFL_FW_State_Preparing"),
                    Why = new Narrative("WFL_FW_Th_Ceremony_Why", kindKey),
                    Paths = { new Narrative("WFL_FW_Th_Ceremony_Path1"), new Narrative("WFL_FW_Th_Ceremony_Path2") },
                    Actions = { new Narrative("WFL_FW_Act_Attend"), new Narrative("WFL_FW_Act_Prepare") },
                    People = req != null && req.SubjectId != 0 ? new List<int> { req.SubjectId } : new List<int>(), Tab = FamilyTab.Story, Sub = (int)StorySub.Current
                });
            }
            foreach (var visit in r.KinVisits.Where(k => k.FamilyId == id && k.Open).Take(1))
                list.Add(new StoryThread
                {
                    Id = "visit:" + visit.Id, Kind = ThreadKind.Visit, Priority = 2, Severity = visit.State == KinVisitState.Stranded ? 2 : 0,
                    Title = new Narrative("WFL_FW_Th_Visit_Title"),
                    Summary = new Narrative("WFL_FW_Th_Visit_Sum", visit.KinIds.Count.ToString()),
                    Line = new Narrative("WFL_FW_Th_Visit_Line", visit.KinIds.Count.ToString()),
                    State = new Narrative("WFL_FW_KinState", "WFL_KinState_" + visit.State),
                    Why = new Narrative("WFL_FW_Th_Visit_Why"),
                    Paths = { new Narrative("WFL_FW_Th_Visit_Path1"), new Narrative("WFL_FW_Th_Visit_Path2") },
                    Actions = { new Narrative("WFL_FW_Act_Welcome"), new Narrative("WFL_FW_Act_TimeTogether") },
                    People = visit.KinIds.ToList(), Tab = FamilyTab.Story, Sub = (int)StorySub.Current
                });
            foreach (var trip in r.CampTrips.Where(t => t.FamilyId == id && t.Open).Take(1))
                list.Add(new StoryThread
                {
                    Id = "camp:" + trip.Id, Kind = ThreadKind.Camp, Priority = 3, Severity = 0,
                    Title = new Narrative("WFL_FW_Th_Camp_Title"),
                    Summary = new Narrative("WFL_FW_Th_Camp_Sum"),
                    Line = new Narrative("WFL_FW_Th_Camp_Line"),
                    State = new Narrative("WFL_FW_KinState", "WFL_CampState_" + trip.State),
                    Why = new Narrative("WFL_FW_Th_Camp_Why", "WFL_CampMotive_" + trip.Motive),
                    Paths = { new Narrative("WFL_FW_Th_Camp_Path1") }, Actions = { new Narrative("WFL_FW_Act_Attend") },
                    People = trip.Suggested.ToList(), Tab = FamilyTab.Story, Sub = (int)StorySub.Current
                });

            // ---- 3: the house's ambition
            var s = house.Situation;
            bool assessed = s != null && s.EvaluatedTick > 0;
            var amb = house.Ambition;
            if (assessed && amb != null && amb.Primary != HouseAmbitionKind.None)
            {
                string k = amb.Primary.ToString();
                var th = new StoryThread
                {
                    Id = "ambition:" + k, Kind = ThreadKind.Ambition, Priority = amb.Primary == HouseAmbitionKind.Contentment ? 8 : 3,
                    Severity = amb.Urgency >= 70f ? 1 : 0,
                    Title = new Narrative("WFL_FW_Amb_" + k + "_Title"),
                    Summary = new Narrative("WFL_FW_Amb_" + k + "_Sum", houseName),
                    Line = new Narrative("WFL_FW_Amb_" + k + "_Line"),
                    State = new Narrative("WFL_FW_State_Ambition", amb.Urgency >= 70f ? "WFL_FW_Urgency_High" : amb.Urgency >= 40f ? "WFL_FW_Urgency_Medium" : "WFL_FW_Urgency_Low"),
                    Why = new Narrative("WFL_FW_Why_" + amb.PrimaryReason), Detail = new Narrative("WFL_FW_Detail_Score", "WFL_FW_Need_" + amb.PrimaryReason, D(s.Score(amb.PrimaryReason))),
                    MergeKey = amb.Primary == HouseAmbitionKind.FillSkillGap && s.WeakestCapability != HouseCapability.None ? "capability:" + s.WeakestCapability : "",
                    Tab = FamilyTab.HouseAffairs
                };
                th.Paths.Add(new Narrative("WFL_FW_Amb_" + k + "_Path1")); th.Paths.Add(new Narrative("WFL_FW_Amb_" + k + "_Path2"));
                th.Actions.Add(new Narrative("WFL_FW_Amb_" + k + "_Act1")); th.Actions.Add(new Narrative("WFL_FW_Amb_" + k + "_Act2"));
                list.Add(th);
            }

            // ---- 5: promises, negotiations, petitions
            int due = r.Commitments.Count(c => c.IsOpen && c.DueTick - now <= FamilyAlerts.PromiseWindow
                && (colonyIds.Contains(c.FromId) || colonyIds.Contains(c.ToId)) && (viewer == 0 || c.FromId == viewer || c.ToId == viewer));
            if (due > 0)
            {
                var c0 = r.Commitments.First(c => c.IsOpen && (colonyIds.Contains(c.FromId) || colonyIds.Contains(c.ToId)) && (viewer == 0 || c.FromId == viewer || c.ToId == viewer));
                list.Add(new StoryThread
                {
                    Id = "promise", Kind = ThreadKind.Promise, Priority = 5, Severity = 1,
                    Title = new Narrative("WFL_FW_Th_Promise_Title"),
                    Summary = new Narrative("WFL_FW_Th_Promise_Sum", N(r, c0.FromId), N(r, c0.ToId), due.ToString()),
                    Line = new Narrative("WFL_FW_Th_Promise_Line", N(r, c0.FromId), N(r, c0.ToId)),
                    State = new Narrative("WFL_FW_State_Due"),
                    Why = new Narrative("WFL_FW_Th_Promise_Why", N(r, c0.FromId)),
                    Paths = { new Narrative("WFL_FW_Th_Promise_Path1"), new Narrative("WFL_FW_Th_Promise_Path2") },
                    Actions = { new Narrative("WFL_FW_Act_KeepPromise") },
                    People = { c0.FromId, c0.ToId }, Tab = FamilyTab.Story, Sub = (int)StorySub.Current
                });
            }
            foreach (var a in r.MarriageAlliances.Where(a => (a.FamilyA == id || a.FamilyB == id) && r.KnowsMarriageAlliance(viewer, a)
                && (a.State == MarriageAllianceState.Negotiating || a.State == MarriageAllianceState.Supported || a.State == MarriageAllianceState.Opposed || a.State == MarriageAllianceState.Consented)).Take(1))
                list.Add(new StoryThread
                {
                    Id = "alliance:" + a.Id, Kind = ThreadKind.Negotiation, Priority = 5, Severity = 0,
                    Title = new Narrative("WFL_FW_Th_Match_Title"),
                    Summary = new Narrative("WFL_FW_Th_Match_Sum", N(r, a.PawnA), N(r, a.PawnB)),
                    Line = new Narrative("WFL_FW_Th_Match_Line", N(r, a.PawnA), N(r, a.PawnB)),
                    State = new Narrative("WFL_FW_State_Negotiating"),
                    Why = new Narrative("WFL_FW_Th_Match_Why"),
                    Paths = { new Narrative("WFL_FW_Th_Match_Path1"), new Narrative("WFL_FW_Th_Match_Path2") },
                    Actions = { new Narrative("WFL_FW_Act_TalkHead"), new Narrative("WFL_FW_Act_TimeTogether") },
                    People = { a.PawnA, a.PawnB }, Tab = FamilyTab.HouseAffairs
                });
            bool manages = viewer == 0 || r.CanManageHouse(viewer, id);
            int petitions = manages ? r.HousePetitions.Count(p => p.FamilyId == id && p.State == PetitionState.Proposed) : 0;
            if (petitions > 0)
                list.Add(new StoryThread
                {
                    Id = "petition", Kind = ThreadKind.Petition, Priority = 5, Severity = 0,
                    Title = new Narrative("WFL_FW_Th_Petition_Title"),
                    Summary = new Narrative("WFL_FW_Th_Petition_Sum", petitions.ToString()),
                    Line = new Narrative("WFL_FW_Th_Petition_Line", petitions.ToString()),
                    State = new Narrative("WFL_FW_State_Awaiting"),
                    Why = new Narrative("WFL_FW_Th_Petition_Why"),
                    Paths = { new Narrative("WFL_FW_Th_Petition_Path1") }, Actions = { new Narrative("WFL_FW_Act_TalkHead") },
                    Tab = FamilyTab.HouseAffairs
                });

            // ---- 6: resource, housing and capability pressure, each with the reason it exists
            if (assessed)
            {
                if (s.Sustenance < FamilyAlerts.FoodLowAt)
                    list.Add(Pressure("food", ThreadKind.Food, "Food", s.Sustenance < FamilyAlerts.FoodCriticalAt ? 2 : 1, FamilyTab.Home, (int)HomeSub.Supplies, D(s.Sustenance)));
                if (s.LivingSpace < FamilyAlerts.SpaceLowAt)
                    list.Add(Pressure("housing", ThreadKind.Housing, "Housing", 1, FamilyTab.Home, (int)HomeSub.Households, D(s.LivingSpace)));
                if (s.Safety < FamilyAlerts.SafetyLowAt)
                    list.Add(Pressure("safety", ThreadKind.Safety, "Safety", 1, FamilyTab.HouseAffairs, 0, D(s.Safety)));
                if (s.Capability < FamilyAlerts.CapabilityLowAt && s.WeakestCapability != HouseCapability.None)
                {
                    var t = Pressure("capability", ThreadKind.Capability, "Capability", 0, FamilyTab.HouseAffairs, 0, D(s.Capability));
                    string capName = s.WeakestCapability.ToString();
                    t.Id = "capability_gap:" + capName.ToLowerInvariant(); t.MergeKey = "capability:" + capName;
                    t.Title = new Narrative("WFL_FW_CapTitle_" + capName, houseName);
                    t.Summary = new Narrative("WFL_FW_CapSum_" + capName, houseName);
                    t.Line = new Narrative("WFL_FW_CapLine_" + capName);
                    t.Why = new Narrative("WFL_FW_CapWhy", "WFL_Capability_" + capName);
                    t.Detail = new Narrative("WFL_FW_Detail_Score", "WFL_Capability_" + capName, D(s.Capability));
                    list.Add(t);
                }
            }

            // ---- 7: teaching and tradition, the recurring shape of family life
            var teaching = recent?.Where(b => b.Teaching && !b.IsRoutineSummary && now - b.Tick <= 3 * RecentTurningWindow).OrderByDescending(b => b.Tick).FirstOrDefault();
            if (teaching != null)
            {
                string teacher = N(r, teaching.Initiator);
                string pupils = string.Join(", ", teaching.Others.Select(o => N(r, o)));
                list.Add(new StoryThread
                {
                    Id = "teaching:" + teaching.Initiator, Kind = ThreadKind.Teaching, Priority = 7, Severity = 0,
                    Title = new Narrative("WFL_FW_Th_Teaching_Title", teacher),
                    Summary = Narrative.About(viewer, teaching.Initiator, "WFL_FW_Th_Teaching_Sum", teacher, pupils),
                    Line = Narrative.About(viewer, teaching.Initiator, "WFL_FW_Th_Teaching_Line", teacher, pupils),
                    State = new Narrative("WFL_FW_State_Ongoing"),
                    Why = new Narrative("WFL_FW_Th_Teaching_Why", teacher),
                    Recent = new Narrative("WFL_FW_Th_Teaching_Recent", teacher, pupils),
                    Paths = { new Narrative("WFL_FW_Th_Teaching_Path1"), new Narrative("WFL_FW_Th_Teaching_Path2") },
                    Actions = { new Narrative("WFL_FW_Act_Mentor"), new Narrative("WFL_FW_Act_TimeTogether") },
                    People = new[] { teaching.Initiator }.Concat(teaching.Others).ToList(), Tab = FamilyTab.Story, Sub = (int)StorySub.Timeline, PersonId = teaching.Initiator
                });
            }
            var tradition = house.Traditions.FirstOrDefault(t => !t.Muted && t.LastHeldTick > 0);
            if (tradition != null)
                list.Add(new StoryThread
                {
                    Id = "tradition:" + tradition.Id, Kind = ThreadKind.Tradition, Priority = 7, Severity = 0,
                    Title = new Narrative("WFL_FW_Th_Tradition_Title"),
                    Summary = new Narrative("WFL_FW_Th_Tradition_Sum", houseName, "@t:" + tradition.Id),
                    Line = new Narrative("WFL_FW_Th_Tradition_Line", "@t:" + tradition.Id),
                    State = new Narrative("WFL_FW_State_Ongoing"),
                    Why = new Narrative("WFL_FW_Th_Tradition_Why", tradition.TimesHeld.ToString()),
                    Paths = { new Narrative("WFL_FW_Th_Tradition_Path1") }, Actions = { new Narrative("WFL_FW_Act_Attend") },
                    Tab = FamilyTab.Story, Sub = (int)StorySub.Current
                });

            return list.OrderBy(t => t.Priority).ThenByDescending(t => t.Severity).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        }

        private static StoryThread Pressure(string id, ThreadKind kind, string key, int severity, FamilyTab tab, int sub, string score)
        {
            var t = new StoryThread
            {
                Id = id, Kind = kind, Priority = 6, Severity = severity,
                Title = new Narrative("WFL_FW_Th_" + key + "_Title"),
                Summary = new Narrative("WFL_FW_Th_" + key + "_Sum"),
                Line = new Narrative("WFL_FW_Th_" + key + "_Line"),
                State = new Narrative(severity >= 2 ? "WFL_FW_State_Serious" : "WFL_FW_State_Low"),
                Why = new Narrative("WFL_FW_Th_" + key + "_Why", score),
                Tab = tab, Sub = sub
            };
            t.Paths.Add(new Narrative("WFL_FW_Th_" + key + "_Path1")); t.Paths.Add(new Narrative("WFL_FW_Th_" + key + "_Path2"));
            t.Actions.Add(new Narrative("WFL_FW_Th_" + key + "_Act1"));
            return t;
        }
    }

    public static class StoryChapters
    {
        /// <summary>
        /// The one situation the family is mostly living through, in a few sentences: how the house is, what leads, what presses on it and what
        /// has lately happened. With nothing pressing it says the family is having a quiet time, which is a good thing to say.
        /// </summary>
        public static StoryChapter Build(FamilyRegistry r, Lineage house, int viewer, List<StoryThread> threads, List<StoryBeat> highlights)
        {
            var ch = new StoryChapter();
            string houseName = r.LineageDisplayName(house);
            var lead = threads.FirstOrDefault(t => t.Priority <= 7);
            var s = house.Situation;
            bool assessed = s != null && s.EvaluatedTick > 0;

            if (lead == null)
            {
                ch.Quiet = true;
                ch.Title = new Narrative("WFL_FW_Ch_QuietTitle");
                ch.Body.Add(new Narrative("WFL_FW_Ch_Quiet", houseName));
            }
            else
            {
                ch.Lead = lead;
                ch.Title = lead.Title;
                if (assessed) ch.Body.Add(new Narrative("WFL_FW_Ch_Is_" + BondTuning.CohesionTier(s.Cohesion), houseName));
                ch.Body.Add(lead.Summary);
                if (!lead.Why.Empty) ch.Body.Add(lead.Why);
            }

            // What has lately happened, from the most meaningful consolidated beat.
            var top = highlights?.FirstOrDefault();
            if (top != null)
            {
                string a = HouseholdNames.Short(r.GetPerson(top.Initiator != 0 ? top.Initiator : top.Entry?.Moment?.A ?? 0));
                string others = string.Join(", ", top.Others.Select(o => HouseholdNames.Short(r.GetPerson(o))));
                if (top.Teaching) ch.Body.Add(Narrative.About(viewer, top.Initiator, "WFL_FW_Clause_Teaching", a, others));
                else if (top.Entry != null && top.Entry.Kind == TimelineKind.Moment) ch.Body.Add(new Narrative("WFL_FW_Clause_Moment", a, others, "WFL_Ev_" + top.Entry.Moment.EventId.Replace('-', '_') + "_Title"));
            }

            // Supporting threads: the other situations as one short line each.
            foreach (var t in threads.Where(t => t != lead).Take(4)) ch.Supporting.Add(t.Line);
            if (lead != null) ch.Supporting.Insert(0, lead.Line);
            if (ch.Supporting.Count > 4) ch.Supporting.RemoveRange(4, ch.Supporting.Count - 4);
            return ch;
        }
    }
}

namespace FamilyLife.Domain
{
    /// <summary>What a relationship's four meters add up to, in a few sentences. Chosen by fixed thresholds from the bond's own values and recent movement.</summary>
    public sealed class RelationshipNarrative
    {
        public Narrative Tone, Trust, Trend, Concern, Idea;
        public List<Narrative> All => new[] { Tone, Trust, Trend, Concern, Idea }.Where(n => !n.Empty).ToList();
    }

    public static class RelationshipStory
    {
        public const float WarmAt = 60f, FondAt = 35f, TrustHigh = 60f, TrustLow = 30f, TensionAt = 50f, StrangersBelow = 25f;

        /// <param name="aIsYou">The first person is the one being played, so the lines can say "you".</param>
        public static RelationshipNarrative Of(RelRow row, string a, string b, bool aIsYou, string word = null)
        {
            var n = new RelationshipNarrative();
            string you = aIsYou ? "_You" : "";
            bool tense = row.Tension >= TensionAt;
            if (word == "Strained" || word == "Uneasy") tense = true;
            string tone = tense ? "Tense" : word == "Warm" || word == "Trusting" ? "Warm" : word == "Uncertain" ? "Cool" : row.Attachment >= WarmAt ? "Warm" : row.Attachment >= FondAt ? "Fond" : row.Familiarity < StrangersBelow ? "New" : "Cool";
            n.Tone = new Narrative("WFL_FW_RS_Tone_" + tone + you, a, b);
            if (row.Trust >= TrustHigh) n.Trust = new Narrative("WFL_FW_RS_Trust_High");
            else if (row.Trust <= TrustLow) n.Trust = new Narrative("WFL_FW_RS_Trust_Low");
            if (row.Trend > 0) n.Trend = new Narrative("WFL_FW_RS_Trend_Up");
            else if (row.Trend < 0) n.Trend = new Narrative("WFL_FW_RS_Trend_Down");
            n.Concern = new Narrative(tense ? "WFL_FW_RS_Concern_Tense" : "WFL_FW_RS_Concern_None");
            n.Idea = new Narrative("WFL_FW_RS_Idea_" + (tense ? "Tense" : tone == "New" ? "New" : tone == "Warm" || tone == "Fond" ? "Warm" : "Cool") + you, a, b);
            return n;
        }
    }
}

namespace FamilyLife.Domain
{
    /// <summary>
    /// The story of a House as someone outside it can tell it: their own history with its people, then what is public, then (only then) that little is known.
    /// Nothing here reads the House's private state (ambition, supplies, private relationships, plots).
    /// </summary>
    public static class OutsiderStory
    {
        public static void Fill(StoryModel m, FamilyRegistry r, Lineage house, int viewer, int now, SkillProbe probe = null)
        {
            string houseName = r.LineageDisplayName(house);
            string me = StoryModel.PerspectiveName(r, viewer);
            var history = Knowledge.HistoryWith(r, viewer, house);
            var entries = history.Select(e => new TimelineEntry { Kind = TimelineKind.Moment, Tick = e.Tick, Category = EventCategory.Relationships, Moment = e }).ToList();
            var beats = StoryConsolidation.Build(entries, false).Where(b => !b.IsRoutineSummary).ToList();
            m.Recent = beats;
            m.Highlights = StoryConsolidation.Highlights(beats, 3);

            if (beats.Count > 0)
            {
                // The chapter is about how things stand lately, so the tone comes from the latest dealings and the reasons shown are the ones that explain it.
                var recent = history.Take(6).ToList();
                string tone = RelationshipWords.ToneOf(recent, viewer);
                int toneSign = tone == "Uneasy" ? -1 : tone == "Warm" ? 1 : 0;
                int bad = history.Count(e => StoryValence.Of(e, viewer).OnViewer < 0), good = history.Count(e => StoryValence.Of(e, viewer).OnViewer > 0);
                m.Chapter.Title = new Narrative("WFL_FW_Ch_Dealings_" + tone + "_Title", houseName);
                m.Chapter.Body.Add(new Narrative("WFL_FW_Ch_Dealings_" + tone, me, houseName));
                m.Chapter.Body.Add(new Narrative("WFL_FW_Ch_DealingsPrivate", houseName));
                var reasons = recent.Where(e => toneSign == 0 || Math.Sign(StoryValence.Of(e, viewer).OnViewer) == toneSign).Take(4).ToList();
                if (reasons.Count == 0) reasons = recent.Take(4).ToList();
                foreach (var e in reasons.AsEnumerable().Reverse()) m.Chapter.Evidence.Add(Evidence(r, e, viewer));
                m.Threads.Add(new StoryThread
                {
                    Id = "dealings:" + house.Id, Kind = ThreadKind.Dealings, Priority = 4, Severity = bad > good ? 1 : 0,
                    Title = new Narrative("WFL_FW_Ch_Dealings_" + tone + "_Title", houseName),
                    Summary = new Narrative("WFL_FW_Ch_Dealings_" + tone, me, houseName),
                    Line = new Narrative("WFL_FW_Th_Dealings_Line", houseName),
                    State = new Narrative("WFL_FW_State_Known"),
                    Why = new Narrative("WFL_FW_Th_Dealings_Why"),
                    Paths = { new Narrative("WFL_FW_Th_Dealings_Path1") }, Actions = { new Narrative("WFL_FW_Act_TimeTogether"), new Narrative("WFL_FW_Act_Apologize") },
                    People = history.Select(e => e.A == viewer ? e.B : e.A).Where(id => id != viewer).Distinct().Take(4).ToList(),
                    Tab = FamilyTab.Story, Sub = (int)StorySub.Timeline
                });
            }
            else if (m.Acquaintance == Acquaintance.Reputation)
            {
                int head = r.HeadOf(house.Id);
                m.Chapter.Title = new Narrative("WFL_FW_Ch_RepTitle");
                m.Chapter.Body.Add(new Narrative("WFL_FW_Ch_Rep", me, HouseholdNames.Short(r.GetPerson(head)), houseName));
            }
            else
            {
                m.Chapter.Title = new Narrative("WFL_FW_Ch_LittleTitle");
                m.Chapter.Body.Add(new Narrative("WFL_FW_Ch_Little", me, houseName));
            }
            m.Chapter.Unknown = beats.Count == 0;

            // Promises the viewer is part of with this House's people are theirs to know.
            var theirs = FamilyTimeline.Relevant(r, house);
            var promise = r.Commitments.FirstOrDefault(c => c.IsOpen && ((c.FromId == viewer && theirs.Contains(c.ToId)) || (c.ToId == viewer && theirs.Contains(c.FromId))));
            if (promise != null)
                m.Threads.Add(new StoryThread
                {
                    Id = "promise:" + promise.Id, Kind = ThreadKind.Promise, Priority = 5, Severity = 1,
                    Title = new Narrative("WFL_FW_Th_Promise_Title"),
                    Summary = new Narrative("WFL_FW_Th_Promise_Sum", HouseholdNames.Short(r.GetPerson(promise.FromId)), HouseholdNames.Short(r.GetPerson(promise.ToId)), "1"),
                    Line = new Narrative("WFL_FW_Th_Promise_Line", HouseholdNames.Short(r.GetPerson(promise.FromId)), HouseholdNames.Short(r.GetPerson(promise.ToId))),
                    State = new Narrative("WFL_FW_State_Due"), Why = new Narrative("WFL_FW_Th_Promise_Why", HouseholdNames.Short(r.GetPerson(promise.FromId))),
                    Paths = { new Narrative("WFL_FW_Th_Promise_Path1"), new Narrative("WFL_FW_Th_Promise_Path2") }, Actions = { new Narrative("WFL_FW_Act_KeepPromise") },
                    People = { promise.FromId, promise.ToId }, Tab = FamilyTab.Story, Sub = (int)StorySub.Current
                });
            ActionAdvice.Apply(r, house, viewer, m.Threads, probe, history);
            m.Threads = m.Threads.OrderBy(t => t.Priority).ToList();
        }

        /// <summary>One short line about what a moment was for the viewer: it went badly, changed nothing, went well, or someone stood against them.</summary>
        public static Narrative Evidence(FamilyRegistry r, EventRecord e, int viewer)
        {
            var v = StoryValence.Of(e, viewer);
            string title = "WFL_Ev_" + e.EventId.Replace('-', '_') + "_Title";
            string other = HouseholdNames.Short(r.GetPerson(e.A == viewer ? e.B : e.A));
            var def = EventCatalog.Get(e.EventId);
            if (viewer == e.Third && def != null && (def.Effects & EventEffect.Confronts) != 0)
                return new Narrative(Narrative.IsPlayed(viewer) ? "WFL_FW_Evid_Against_You" : "WFL_FW_Evid_Against", HouseholdNames.Short(r.GetPerson(e.A)), HouseholdNames.Short(r.GetPerson(e.B)), title, StoryModel.PerspectiveName(r, viewer));
            return new Narrative(v.OnViewer < 0 ? "WFL_FW_Evid_Bad" : v.OnViewer > 0 ? "WFL_FW_Evid_Good" : "WFL_FW_Evid_Neutral", other, title);
        }

        public static Narrative MomentLine(FamilyRegistry r, StoryBeat b)
        {
            string a = HouseholdNames.Short(r.GetPerson(b.Initiator));
            string others = string.Join(", ", b.Others.Select(o => HouseholdNames.Short(r.GetPerson(o))));
            string title = b.Entry?.Moment == null ? "" : "WFL_Ev_" + b.Entry.Moment.EventId.Replace('-', '_') + "_Title";
            return new Narrative("WFL_FW_Clause_Moment", a, others, title);
        }
    }
}

namespace FamilyLife.Domain
{
    public static class StoryVisibility
    {
        /// <summary>The chronicle kinds anyone may know about another House: its births, deaths, marriages, foundation and comings of age. Rifts, bond drift and the like are private.</summary>
        public static bool PublicKind(FamilyEventKind k) =>
            k == FamilyEventKind.Birth || k == FamilyEventKind.Died || k == FamilyEventKind.MarriageFormed || k == FamilyEventKind.LineageFounded
            || k == FamilyEventKind.SocialBranch || k == FamilyEventKind.CameOfAge || k == FamilyEventKind.PartnershipFormed;

        public static bool Visible(FamilyRegistry r, Lineage house, int viewer, TimelineEntry t) =>
            FamilyWindowFacts.IsInsider(r, house.Id, viewer) || t.Kind != TimelineKind.Chronicle || PublicKind(t.Chronicle.Kind);
    }
}

namespace FamilyLife.Domain
{
    public static class ThreadMerge
    {
        /// <summary>Two sources describing one situation (a capability gap and an ambition to fill it) become one thread that carries both reasons.</summary>
        public static List<StoryThread> Merge(List<StoryThread> threads)
        {
            var result = new List<StoryThread>();
            foreach (var group in threads.GroupBy(t => string.IsNullOrEmpty(t.MergeKey) ? t.Id : t.MergeKey))
            {
                var all = group.ToList();
                if (all.Count == 1) { result.Add(all[0]); continue; }
                var primary = all.FirstOrDefault(t => t.Kind == ThreadKind.Capability) ?? all.OrderBy(t => t.Priority).First();
                foreach (var other in all.Where(t => t != primary))
                {
                    primary.Priority = Math.Min(primary.Priority, other.Priority);
                    primary.Severity = Math.Max(primary.Severity, other.Severity);
                    if (other.Kind == ThreadKind.Ambition) primary.Facts.Add(new Narrative("WFL_FW_Fact_AmbitionGap"));
                    foreach (var p in other.Paths) if (!primary.Paths.Any(x => x.Key == p.Key)) primary.Paths.Add(p);
                    foreach (var a in other.Actions) primary.Actions.Add(a);
                    foreach (var f in other.Facts) primary.Facts.Add(f);
                    foreach (var pid in other.People) if (!primary.People.Contains(pid)) primary.People.Add(pid);
                }
                result.Add(primary);
            }
            return result.OrderBy(t => t.Priority).ThenByDescending(t => t.Severity).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        }
    }
}
