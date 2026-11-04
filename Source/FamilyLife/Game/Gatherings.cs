using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace FamilyLife
{
    [DefOf]
    public static class WFL_GatheringDefOf
    {
        public static GatheringDef WFL_FamilyGathering;

        static WFL_GatheringDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(WFL_GatheringDefOf));
    }

    /// <summary>
    /// A family gathering: a tradition, a coming of age, a memorial. It is the game's own party with a guest list: the family's people
    /// in the colony, and anyone who thinks well of the hosts. Everything else (where, how long, the mood afterwards) is the game's.
    /// </summary>
    public class LordJob_FamilyGathering : LordJob_Joinable_Party
    {
        private string kind = "";
        private int lineageId;
        private List<int> invited = new List<int>();

        public LordJob_FamilyGathering() { }

        public LordJob_FamilyGathering(IntVec3 spot, Pawn organizer, GatheringDef def, string kind, int lineageId, List<int> invited)
            : base(spot, organizer, def)
        {
            this.kind = kind;
            this.lineageId = lineageId;
            this.invited = invited ?? new List<int>();
        }

        private ThoughtDef Mood => DefDatabase<ThoughtDef>.GetNamedSilentFail(
            kind == "comingofage" ? "WFL_ComingOfAgeAttended" : (kind == "memorial" ? "WFL_MemorialAttended" : (kind == "reconciliation" ? "WFL_ReconciliationAttended" : "WFL_TraditionAttended"))) ?? ThoughtDefOf.AttendedParty;

        protected override ThoughtDef AttendeeThought => Mood;
        protected override ThoughtDef OrganizerThought => Mood;

        protected override bool IsInvited(Pawn p)
        {
            return base.IsInvited(p) && invited.Contains(p.thingIDNumber);
        }

        public override string GetReport(Pawn pawn)
        {
            return ("WFL_Gathering_Report_" + (kind == "comingofage" || kind == "memorial" || kind == "reconciliation" ? kind : "tradition")).Translate().ToString();
        }

        /// <summary>When the gathering ends, relatives who were there together grow closer.</summary>
        public override void Cleanup()
        {
            base.Cleanup();
            try { BondAttendees(); }
            catch (System.Exception e) { Log.Warning("[Wil's Family Life] Could not record who attended a gathering: " + e.Message); }
        }

        private void BondAttendees()
        {
            var component = FamilyComponent.Current;
            if (component == null || lord == null) return;
            var people = lord.ownedPawns.Where(p => p != null && !p.Dead).ToList();
            int now = Activities.Now;
            int together = System.Math.Min(durationTicks, 6000);
            for (int i = 0; i < people.Count; i++)
                for (int j = i + 1; j < people.Count; j++)
                {
                    var a = component.Registry.GetPerson(people[i].thingIDNumber);
                    var b = component.Registry.GetPerson(people[j].thingIDNumber);
                    if (a != null && b != null && component.Registry.KindOf(a, b).HasValue)
                        component.Registry.RecordTogether(a.Id, b.Id, together, now, BondCause.Gathering);
                }

            // A gathering is the setting for a few moments between the people who were there, whether or not they are family.
            SocialMoments.OnGathering(people, kind == "reconciliation" ? ActivityKind.Reconciliation : ActivityKind.Celebration);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref kind, "kind", "");
            Scribe_Values.Look(ref lineageId, "lineageId");
            Scribe_Collections.Look(ref invited, "invited", LookMode.Value);
            if (invited == null) invited = new List<int>();
        }
    }

    public class GatheringWorker_FamilyGathering : GatheringWorker_Party
    {
        protected override LordJob CreateLordJob(IntVec3 spot, Pawn organizer)
        {
            return FamilyGatherings.MakeLordJob(spot, organizer, def);
        }

        protected override bool TryFindGatherSpot(Pawn organizer, out IntVec3 spot)
        {
            return RCellFinder.TryFindGatheringSpot(organizer, def, ignoreRequiredColonistCount: true, out spot);
        }

        protected override void SendLetter(IntVec3 spot, Pawn organizer)
        {
            var request = FamilyGatherings.Active;
            string key = request != null && (request.Kind == "comingofage" || request.Kind == "memorial" || request.Kind == "reconciliation") ? request.Kind : "tradition";
            string subject = request != null && request.SubjectId != 0 ? FamilyUi.NameOf(FamilyComponent.Current.Registry, request.SubjectId) : "";
            Find.LetterStack.ReceiveLetter(("WFL_Gathering_Title_" + key).Translate(), ("WFL_Gathering_Text_" + key).Translate(organizer.LabelShort, subject),
                LetterDefOf.PositiveEvent, new TargetInfo(spot, organizer.Map));
        }
    }

    internal static class FamilyGatherings
    {
        /// <summary>The request being started, so the game's worker can read it while it builds the gathering.</summary>
        public static GatheringRequest Active;

        public static LordJob MakeLordJob(IntVec3 spot, Pawn organizer, GatheringDef def)
        {
            var component = FamilyComponent.Current;
            var request = Active;
            var invited = new List<int> { organizer.thingIDNumber };
            if (component != null && request != null)
            {
                var hosts = new List<int> { organizer.thingIDNumber };
                var organizerRecord = component.Registry.GetPerson(organizer.thingIDNumber);
                if (organizerRecord != null) hosts.AddRange(organizerRecord.PartnerIds());
                invited = component.Registry.Invitees(request.LineageId, hosts, component.OpinionBetween);
                if (request.Kind == "reconciliation")
                {
                    // The other family is asked as well: that is the whole point of it.
                    int guestFamily = component.Registry.LineageIdFor(component.Registry.GetPerson(request.SubjectId));
                    foreach (var p in component.Registry.Persons.Where(x => x.InColony && !invited.Contains(x.Id) && component.Registry.LineageIdFor(x) == guestFamily && guestFamily != 0))
                        invited.Add(p.Id);
                }
            }
            return new LordJob_FamilyGathering(spot, organizer, def, request?.Kind ?? "", request?.LineageId ?? 0, invited);
        }

        /// <summary>Starts at most one due gathering. A gathering that cannot start now is tried again a little later.</summary>
        public static void Process(FamilyComponent component, int now)
        {
            var settings = FamilyLifeMod.Settings;
            if (settings != null && !settings.traditions) return;
            var due = component.Registry.DueRequests(now);
            if (due.Count == 0) return;
            var map = Find.AnyPlayerHomeMap;
            var def = WFL_GatheringDefOf.WFL_FamilyGathering;
            if (map == null || def == null) return;
            if (GatheringsUtility.AnyLordJobPreventsNewGatherings(map) || !GatheringsUtility.AcceptableGameConditionsToStartGathering(map, def)) return;

            var request = due[0];
            var organizer = FindOrganizer(component, request, map);
            bool started = false;
            if (organizer != null && def.Worker.CanExecute(map, organizer))
            {
                Active = request;
                try { started = def.Worker.TryExecute(map, organizer); }
                finally { Active = null; }
            }
            if (started) component.Registry.MarkGatheringHeld(request.Id, now);
            else component.Registry.PostponeRequest(request.Id, now, 2500);
        }

        private static Pawn FindOrganizer(FamilyComponent component, GatheringRequest request, Map map)
        {
            var registry = component.Registry;
            var lineage = registry.GetLineage(request.LineageId);
            if (lineage == null) return null;
            Pawn best = null;
            int bestScore = int.MinValue;
            foreach (var record in registry.Persons.Where(p => p.InColony && !p.Juvenile && (p.PrimaryLineageId == lineage.Id || lineage.HasAffiliate(p.Id))))
            {
                var pawn = component.TryGetPawn(record.Id);
                if (pawn == null || pawn.Map != map || !pawn.Spawned || pawn.Drafted || !GatheringsUtility.PawnCanStartOrContinueGathering(pawn)) continue;
                if (!Activities.IsFree(pawn, checkCurrentJob: false)) continue;
                int score = Activities.SocialOf(pawn) + (request.SubjectId != 0 && record.ParentIds.Contains(request.SubjectId) ? 0 : 0)
                            + (registry.GetPerson(request.SubjectId)?.ParentIds.Contains(record.Id) == true ? 20 : 0);
                if (score > bestScore) { best = pawn; bestScore = score; }
            }
            return best;
        }
    }
}
