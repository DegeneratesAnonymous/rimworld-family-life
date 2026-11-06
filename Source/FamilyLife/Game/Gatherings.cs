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
        private int requestId;
        private List<int> invited = new List<int>();
        private List<int> visitors = new List<int>();

        public LordJob_FamilyGathering() { }

        public LordJob_FamilyGathering(IntVec3 spot, Pawn organizer, GatheringDef def, int requestId, string kind, int lineageId, List<int> invited, List<int> visitors)
            : base(spot, organizer, def)
        {
            this.requestId = requestId;
            this.kind = kind;
            this.lineageId = lineageId;
            this.invited = invited ?? new List<int>();
            this.visitors = visitors ?? new List<int>();
        }

        private ThoughtDef Mood => DefDatabase<ThoughtDef>.GetNamedSilentFail(MoodDefName(kind)) ?? ThoughtDefOf.AttendedParty;

        private static string MoodDefName(string occasion)
        {
            if(occasion=="comingofage")return "WFL_ComingOfAgeAttended";
            if(occasion=="funeral"||occasion=="memorial")return "WFL_FuneralAttended";
            if(occasion=="wedding")return "WFL_WeddingAttended";
            if(occasion=="grandreunion")return "WFL_GrandReunionAttended";
            if(occasion=="reconciliation")return "WFL_ReconciliationAttended";
            return "WFL_TraditionAttended";
        }

        protected override ThoughtDef AttendeeThought => Mood;
        protected override ThoughtDef OrganizerThought => Mood;

        protected override bool IsInvited(Pawn p)
        {
            return base.IsInvited(p) && invited.Contains(p.thingIDNumber);
        }

        public override string GetReport(Pawn pawn)
        {
            return ("WFL_Gathering_Report_" + ReportKey(kind)).Translate().ToString();
        }

        internal static string ReportKey(string value) => value=="comingofage"||value=="funeral"||value=="wedding"||value=="grandreunion"||value=="reconciliation"?value:"tradition";

        /// <summary>When the gathering ends, relatives who were there together grow closer.</summary>
        public override void Cleanup()
        {
            var people=lord?.ownedPawns.Where(p=>p!=null&&p.Spawned&&!p.Dead).ToList()??new List<Pawn>();
            int moments=0;
            try { moments=BondAttendees(people); }
            catch (System.Exception e) { Log.Warning("[Wil's Family Life] Could not record who attended a gathering: " + e.Message); }
            base.Cleanup();
            var component=FamilyComponent.Current;
            if(component!=null&&requestId!=0)
            {
                float attendance=invited.Count==0?0:people.Count/(float)invited.Count;
                var request=component.Registry.GatheringRequests.FirstOrDefault(r=>r.Id==requestId);
                if(request!=null)
                {
                    request.MomentsResolved+=moments;
                    float support=(request.Influences.HasFlag(CeremonyInfluence.SuitableVenue) ? .1f : 0f)
                        +(request.Influences.HasFlag(CeremonyInfluence.HeadSupport) ? .1f : 0f)
                        +(request.Influences.HasFlag(CeremonyInfluence.Hospitality) ? .1f : 0f);
                    float outcome=System.Math.Max(-1,System.Math.Min(1,attendance*1.2f-.5f+support));
                    if(TraditionCatalog.IndexOf(request.Kind)>=0)
                        component.Registry.MarkGatheringHeld(requestId,Activities.Now,people.Count,request.MomentsResolved,outcome);
                    else
                        component.Registry.CompleteCeremony(requestId,Activities.Now,people.Count,request.MomentsResolved,
                            outcome);
                }
            }
        }

        private int BondAttendees(List<Pawn> people)
        {
            var component = FamilyComponent.Current;
            if (component == null) return 0;
            int now = Activities.Now;
            int together = System.Math.Min(durationTicks, 6000);
            // A party does not give everyone a full private conversation with every other attendee.
            foreach (var pair in GatheringBudget.Pairs(people,people.Count*2,now))
            {
                var first=pair.Key; var second=pair.Value;
                if(first.Map!=second.Map || first.Position.DistanceTo(second.Position)>12 || !GenSight.LineOfSight(first.Position,second.Position,first.Map)) continue;
                var a=component.Registry.GetPerson(first.thingIDNumber); var b=component.Registry.GetPerson(second.thingIDNumber);
                if(a!=null && b!=null && component.Registry.KindOf(a,b).HasValue)
                    component.Registry.RecordTogether(a.Id,b.Id,together/System.Math.Min(8,System.Math.Max(1,people.Count-1)),now,BondCause.Gathering);
            }

            // A gathering is the setting for a few moments between the people who were there, whether or not they are family.
            string occasion=TraditionCatalog.IndexOf(kind)>=0||CeremonyMoments.Supports(kind)?kind:null;
            var request=requestId==0?null:component.Registry.GatheringRequests.FirstOrDefault(r=>r.Id==requestId);
            return SocialMoments.OnGathering(people,kind=="reconciliation"?ActivityKind.Reconciliation:ActivityKind.Celebration,occasion,
                request?.LineageId??lineageId,request?.SecondLineageId??0,request?.SubjectId??0,request?.SubjectBId??0,request?.VisitorIds??visitors);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref kind, "kind", "");
            Scribe_Values.Look(ref lineageId, "lineageId");
            Scribe_Values.Look(ref requestId,"requestId");
            Scribe_Collections.Look(ref invited, "invited", LookMode.Value);
            Scribe_Collections.Look(ref visitors,"visitors",LookMode.Value);
            if (invited == null) invited = new List<int>();
            if (visitors == null) visitors = new List<int>();
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
            string key = request==null?"tradition":LordJob_FamilyGathering.ReportKey(request.Kind);
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
                if(request.SecondLineageId!=0)
                    foreach(var member in component.Registry.MembersOf(request.SecondLineageId).Where(p=>p.InColony))
                        if(!invited.Contains(member.Id))invited.Add(member.Id);
                foreach(int id in request.VisitorIds)
                {
                    var visitor=component.TryGetPawn(id);
                    if(visitor!=null&&visitor.Spawned&&visitor.Map==organizer.Map&&!invited.Contains(id))invited.Add(id);
                }
                if (request.Kind == "reconciliation")
                {
                    // The other family is asked as well: that is the whole point of it.
                    int guestFamily = component.Registry.LineageIdFor(component.Registry.GetPerson(request.SubjectId));
                    foreach (var p in component.Registry.Persons.Where(x => x.InColony && !invited.Contains(x.Id) && component.Registry.LineageIdFor(x) == guestFamily && guestFamily != 0))
                        invited.Add(p.Id);
                }
            }
            if(request!=null)
            {
                // Attendance is judged only against people who could physically attend this map.
                invited=invited.Where(id=>id==organizer.thingIDNumber||
                    component.TryGetPawn(id) is Pawn pawn&&pawn.Spawned&&!pawn.Dead&&pawn.Map==organizer.Map).Distinct().Take(64).ToList();
                request.InvitedIds=invited;
                if(organizer.Map.zoneManager.ZoneAt(spot) is Zone_Family)request.Influences|=CeremonyInfluence.SuitableVenue;
            }
            return new LordJob_FamilyGathering(spot, organizer, def, request?.Id??0, request?.Kind ?? "", request?.LineageId ?? 0, invited, request?.VisitorIds);
        }

        /// <summary>Starts at most one due gathering. A gathering that cannot start now is tried again a little later.</summary>
        public static void Process(FamilyComponent component, int now)
        {
            var settings = FamilyLifeMod.Settings;
            var due = component.Registry.DueRequests(now).Where(r=>settings==null||
                (r.Ceremony==CeremonyKind.Wedding||r.Ceremony==CeremonyKind.Funeral||r.Ceremony==CeremonyKind.GrandReunion
                    ?settings.ceremonies:settings.traditions)).ToList();
            if (due.Count == 0) return;
            var requestedHouse=component.Registry.GetLineage(due[0].LineageId);
            var map = requestedHouse==null?Find.AnyPlayerHomeMap:Find.Maps.FirstOrDefault(m=>m.uniqueID==requestedHouse.ZoneMapId)??Find.AnyPlayerHomeMap;
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
            if (started)
            {
                var house=component.Registry.GetLineage(request.LineageId);
                if(house!=null&&(house.CultureOf(Dim.Hearth)>=20||house.CultureOf(Dim.Revelry)>=20))
                    request.Influences|=CeremonyInfluence.Hospitality;
                if(component.Registry.HeadOf(request.LineageId)==organizer.thingIDNumber||component.Registry.RoleInHouse(organizer.thingIDNumber,request.LineageId)==HouseRole.Steward)
                    request.Influences|=CeremonyInfluence.HeadSupport;
                component.Registry.MarkCeremonyStarted(request.Id,organizer.thingIDNumber,map.uniqueID,now,request.InvitedIds);
                // Relationship scenes are resolved from the lord's actual attendees in Cleanup, never
                // from every invited pawn who merely happens to be somewhere on the map.
            }
            else component.Registry.PostponeRequest(request.Id, now, 2500);
        }

        private static Pawn FindOrganizer(FamilyComponent component, GatheringRequest request, Map map)
        {
            var registry = component.Registry;
            var lineage = registry.GetLineage(request.LineageId);
            if (lineage == null) return null;
            Pawn best = null;
            int bestScore = int.MinValue;
            foreach (var record in registry.Persons.Where(p => p.InColony && !p.Juvenile && registry.BelongsToFamily(p,lineage.Id)))
            {
                var pawn = component.TryGetPawn(record.Id);
                if (pawn == null || pawn.Map != map || !pawn.Spawned || pawn.Drafted || !GatheringsUtility.PawnCanStartOrContinueGathering(pawn)) continue;
                if (!Activities.IsFree(pawn, checkCurrentJob: false)) continue;
                int score = Activities.SocialOf(pawn) + (registry.HeadOf(lineage.Id)==record.Id?30:registry.RoleInHouse(record.Id,lineage.Id)==HouseRole.Steward?15:0)
                            + (registry.GetPerson(request.SubjectId)?.ParentIds.Contains(record.Id) == true ? 20 : 0);
                if (score > bestScore) { best = pawn; bestScore = score; }
            }
            return best;
        }
    }
}
