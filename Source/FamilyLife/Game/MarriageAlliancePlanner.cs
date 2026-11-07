using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>Daily bounded discovery plus physical representative-to-representative negotiation.</summary>
    internal static class MarriageAlliancePlanner
    {
        private const int PairBudget=48;

        internal static void AssessHouse(FamilyComponent component,Lineage house,List<Lineage> active,int now)
        {
            var r=component.Registry;
            if(house?.Ambition==null||!WantsAlliance(house.Ambition.Primary)||r.MarriageAlliances.Any(a=>(a.FamilyA==house.Id||a.FamilyB==house.Id)&&
                (a.State==MarriageAllianceState.Opportunity||a.State==MarriageAllianceState.Negotiating||a.State==MarriageAllianceState.Supported||a.State==MarriageAllianceState.Consented)))return;
            var own=r.MembersOf(house.Id).Where(p=>p.InColony&&!p.Juvenile&&!p.PartnerIds().Any()).Take(6).ToList();
            if(own.Count==0)return;
            var pairs=(from other in active.Where(h=>h.Id!=house.Id).OrderBy(h=>h.Id)
                       from candidate in r.MembersOf(other.Id).Where(p=>p.InColony&&!p.Juvenile&&!p.PartnerIds().Any()).Take(6)
                       from person in own select new{Other=other,Candidate=candidate,Person=person}).ToList();
            if(pairs.Count==0)return;
            int start=(unchecked(house.Id*397+now/ActivityTuning.TicksPerDay)&int.MaxValue)%pairs.Count;
            MarriageAllianceAssessment best=null;
            for(int tried=0;tried<Math.Min(PairBudget,pairs.Count);tried++)
            {
                var pair=pairs[(start+tried)%pairs.Count];var a=component.TryGetPawn(pair.Person.Id);var b=component.TryGetPawn(pair.Candidate.Id);
                if(a==null||b==null||a.Map!=b.Map||!a.Spawned||!b.Spawned)continue;
                var context=Context(component,house,pair.Other,a,b);
                var assessment=r.AssessMarriageAlliance(a.thingIDNumber,b.thingIDNumber,context);
                if(!assessment.WorthConsidering)continue;
                if(best==null||assessment.HouseAInterest+assessment.HouseBInterest>best.HouseAInterest+best.HouseBInterest)best=assessment;
            }
            if(best!=null)r.RecordMarriageOpportunity(best,now);
        }

        private static bool WantsAlliance(HouseAmbitionKind kind)=>kind==HouseAmbitionKind.SecureSuccession||kind==HouseAmbitionKind.ExpandLegacy||kind==HouseAmbitionKind.FillSkillGap||kind==HouseAmbitionKind.SeekProtection;

        private static MarriageCandidateContext Context(FamilyComponent component,Lineage aHouse,Lineage bHouse,Pawn a,Pawn b)
        {
            bool eligible=false;try{eligible=RelationsUtility.RomanceEligiblePair(a,b,false).Accepted;}catch(Exception){eligible=false;}
            float ideology=0f;
            if(ModsConfig.IdeologyActive&&a.Ideo!=null&&b.Ideo!=null)ideology=a.Ideo==b.Ideo?1f:-.45f;
            var ab=component.Registry.GetBond(a.thingIDNumber,b.thingIDNumber);var ba=component.Registry.GetBond(b.thingIDNumber,a.thingIDNumber);
            return new MarriageCandidateContext{VanillaEligible=eligible,OpinionA=a.relations?.OpinionOf(b)??0,OpinionB=b.relations?.OpinionOf(a)??0,
                IdeologyCompatibility=ideology,Potential=((ab?.Attachment??0)+(ba?.Attachment??0))/200f,
                ContributionAToB=Contribution(a,bHouse),ContributionBToA=Contribution(b,aHouse)};
        }

        private static float Contribution(Pawn pawn,Lineage receiving)
        {
            var skill=HouseSituationPlanner.PreferredSkill(receiving?.Situation?.WeakestCapability??HouseCapability.None,pawn);
            var record=skill==null?null:pawn.skills?.GetSkill(skill);
            return record==null||record.TotallyDisabled?0f:Math.Min(1f,record.Level/12f);
        }

        internal static int Representative(FamilyRegistry r,int family)
        {
            int head=r.HeadOf(family);if(head!=0)return head;
            return r.GetLineage(family)?.Stewards.FirstOrDefault(id=>r.GetPerson(id)?.InColony==true)??0;
        }

        internal static int ConsentRepresentative(FamilyRegistry r,int family,int candidate)
        {
            int head=r.HeadOf(family);if(head!=0&&head!=candidate)return head;
            int steward=r.GetLineage(family)?.Stewards.FirstOrDefault(id=>id!=candidate&&r.GetPerson(id)?.InColony==true)??0;
            return steward!=0?steward:candidate;
        }

        internal static bool StartNegotiation(FamilyComponent component,MarriageAlliance alliance,Pawn actor,bool byPlayer=false)
        {
            if(component==null||alliance==null||alliance.State!=MarriageAllianceState.Opportunity||actor==null)return false;
            int family=component.Registry.FamilyOf(actor.thingIDNumber);if(family!=alliance.FamilyA&&family!=alliance.FamilyB||!component.Registry.CanManageHouse(actor.thingIDNumber,family))return false;
            int otherFamily=family==alliance.FamilyA?alliance.FamilyB:alliance.FamilyA;int otherId=Representative(component.Registry,otherFamily);
            var other=component.TryGetPawn(otherId);
            if(other==null||other==actor||!other.Spawned||other.Map!=actor.Map||!Activities.IsFree(other)||other.CurJob?.playerForced==true||
                (!byPlayer&&(Find.Selector.IsSelected(other)||PerspectiveShiftBridge.IsAvatar(other)))||!actor.CanReach(other,PathEndMode.Touch,Danger.None))return false;
            if(!byPlayer&&(!Activities.IsFree(actor)||actor.CurJob?.playerForced==true||PerspectiveShiftBridge.IsAvatar(actor)))return false;
            var job=JobMaker.MakeJob(WFL_StoryDefOf.WFL_MarriageNegotiation,other);job.count=alliance.Id;
            GovernanceMeetings.PreferOffice(job,component,family,actor,other);
            actor.jobs.StartJob(job,JobCondition.InterruptForced,null,false,tag:JobTag.Misc);return true;
        }

        internal static void CompleteNegotiation(Pawn speaker,Pawn other,int id)
        {
            var c=FamilyComponent.Current;var r=c?.Registry;var alliance=r?.MarriageAlliances.FirstOrDefault(a=>a.Id==id);
            if(alliance==null||speaker.Map!=other.Map||speaker.Position.DistanceTo(other.Position)>3)return;
            int speakerFamily=r.FamilyOf(speaker.thingIDNumber);
            int repA=speakerFamily==alliance.FamilyA?speaker.thingIDNumber:other.thingIDNumber;
            int repB=speakerFamily==alliance.FamilyA?other.thingIDNumber:speaker.thingIDNumber;
            if(!r.BeginMarriageNegotiation(id,repA,repB,Activities.Now))return;
            ResolveIfAutonomous(c,alliance,speaker);ResolveIfAutonomous(c,alliance,other);
            if(alliance.State==MarriageAllianceState.Negotiating)
                Messages.Message("WFL_MarriageAlliance_Waiting".Translate(speaker.LabelShort,other.LabelShort),speaker,MessageTypeDefOf.NeutralEvent,false);
        }

        private static void ResolveIfAutonomous(FamilyComponent c,MarriageAlliance alliance,Pawn representative)
        {
            if(Find.Selector.IsSelected(representative)||PerspectiveShiftBridge.IsAvatar(representative))return;
            int family=c.Registry.FamilyOf(representative.thingIDNumber);bool support=c.Registry.WouldHouseSupportMarriage(alliance,family);
            c.Registry.DecideMarriageNegotiation(alliance.Id,representative.thingIDNumber,support,Activities.Now);
            c.Registry.NoteStewardService(representative.thingIDNumber,family,StewardServiceKind.Negotiation,true,Activities.Now);
        }

        internal static bool DecideAs(FamilyComponent c,MarriageAlliance alliance,Pawn representative,bool support)
        {
            if(c==null||alliance==null||representative==null)return false;
            bool decided=c.Registry.DecideMarriageNegotiation(alliance.Id,representative.thingIDNumber,support,Activities.Now);
            if(decided)c.Registry.NoteStewardService(representative.thingIDNumber,c.Registry.FamilyOf(representative.thingIDNumber),StewardServiceKind.Negotiation,true,Activities.Now);
            return decided;
        }

        internal static bool StartConsent(FamilyComponent component,MarriageAlliance alliance,Pawn representative,bool byPlayer=false)
        {
            if(component==null||alliance==null||alliance.State!=MarriageAllianceState.Supported||representative==null)return false;
            int family=component.Registry.FamilyOf(representative.thingIDNumber);bool first=family==alliance.FamilyA;
            if(!first&&family!=alliance.FamilyB||!component.Registry.CanManageHouse(representative.thingIDNumber,family))return false;
            int pawnId=first?alliance.PawnA:alliance.PawnB;
            var target=component.TryGetPawn(pawnId);if(target==null||!target.Spawned)return false;
            var proposedOther=component.TryGetPawn(first?alliance.PawnB:alliance.PawnA);
            try{if(proposedOther==null||!RelationsUtility.RomanceEligiblePair(target,proposedOther,false).Accepted){alliance.State=MarriageAllianceState.Expired;alliance.LastChangedTick=Activities.Now;return false;}}
            catch(Exception){alliance.State=MarriageAllianceState.Expired;alliance.LastChangedTick=Activities.Now;return false;}
            if(target==representative)
            {
                if(!component.Registry.BeginMarriageConsent(alliance.Id,pawnId,representative.thingIDNumber,Activities.Now))return false;
                ResolveConsent(component,alliance,target);return true;
            }
            if(target.Map!=representative.Map||!Activities.IsFree(target)||target.CurJob?.playerForced==true||
                (!byPlayer&&(Find.Selector.IsSelected(target)||PerspectiveShiftBridge.IsAvatar(target)))||!representative.CanReach(target,PathEndMode.Touch,Danger.None))return false;
            if(!byPlayer&&(!Activities.IsFree(representative)||representative.CurJob?.playerForced==true||PerspectiveShiftBridge.IsAvatar(representative)))return false;
            var job=JobMaker.MakeJob(WFL_StoryDefOf.WFL_MarriageConsent,target);job.count=alliance.Id;
            GovernanceMeetings.PreferOffice(job,component,family,representative,target);
            representative.jobs.StartJob(job,JobCondition.InterruptForced,null,false,tag:JobTag.Misc);return true;
        }

        internal static void CompleteConsent(Pawn representative,Pawn proposed,int id)
        {
            var c=FamilyComponent.Current;var alliance=c?.Registry.MarriageAlliances.FirstOrDefault(a=>a.Id==id);
            if(alliance==null||representative.Map!=proposed.Map||representative.Position.DistanceTo(proposed.Position)>3||
                !c.Registry.BeginMarriageConsent(id,proposed.thingIDNumber,representative.thingIDNumber,Activities.Now))return;
            ResolveConsent(c,alliance,proposed);
        }

        private static void ResolveConsent(FamilyComponent c,MarriageAlliance alliance,Pawn proposed)
        {
            if(Find.Selector.IsSelected(proposed)||PerspectiveShiftBridge.IsAvatar(proposed))
            {Messages.Message("WFL_MarriageConsent_Waiting".Translate(proposed.LabelShort),proposed,MessageTypeDefOf.NeutralEvent,false);return;}
            bool first=proposed.thingIDNumber==alliance.PawnA;float willingness=first?alliance.PawnAWillingness:alliance.PawnBWillingness;
            c.Registry.DecideMarriageConsent(alliance.Id,proposed.thingIDNumber,willingness>=0f,Activities.Now);
        }

        internal static bool DecideConsentAs(FamilyComponent c,MarriageAlliance alliance,Pawn proposed,bool accept)
            =>c!=null&&alliance!=null&&proposed!=null&&c.Registry.DecideMarriageConsent(alliance.Id,proposed.thingIDNumber,accept,Activities.Now);

        internal static bool DecideSelfConsent(FamilyComponent c,MarriageAlliance alliance,Pawn proposed,bool accept)
        {
            if(c==null||alliance==null||proposed==null||!c.Registry.BeginMarriageConsent(alliance.Id,proposed.thingIDNumber,proposed.thingIDNumber,Activities.Now))return false;
            return c.Registry.DecideMarriageConsent(alliance.Id,proposed.thingIDNumber,accept,Activities.Now);
        }

        internal static bool StartUnion(FamilyComponent c,MarriageAlliance alliance,Pawn actor,bool byPlayer=false)
        {
            if(c==null||alliance==null||alliance.UnionState!=HouseUnionState.Eligible||actor==null)return false;
            int family=c.Registry.FamilyOf(actor.thingIDNumber);if(family!=alliance.FamilyA&&family!=alliance.FamilyB||
                !c.Registry.MayDecide(actor.thingIDNumber,family,HouseDecision.MarriageUnion))return false;
            int otherFamily=family==alliance.FamilyA?alliance.FamilyB:alliance.FamilyA;int otherId=Representative(c.Registry,otherFamily);var other=c.TryGetPawn(otherId);
            if(other==null||other.Map!=actor.Map||!other.Spawned||!Activities.IsFree(other)||other.CurJob?.playerForced==true||
                (!byPlayer&&(Find.Selector.IsSelected(other)||PerspectiveShiftBridge.IsAvatar(other)))||!actor.CanReach(other,PathEndMode.Touch,Danger.None))return false;
            if(!byPlayer&&(!Activities.IsFree(actor)||actor.CurJob?.playerForced==true||PerspectiveShiftBridge.IsAvatar(actor)))return false;
            var job=JobMaker.MakeJob(WFL_StoryDefOf.WFL_HouseUnionNegotiation,other);job.count=alliance.Id;
            GovernanceMeetings.PreferOffice(job,c,family,actor,other);actor.jobs.StartJob(job,JobCondition.InterruptForced,null,false,tag:JobTag.Misc);return true;
        }

        internal static void CompleteUnion(Pawn speaker,Pawn other,int id)
        {
            var c=FamilyComponent.Current;var a=c?.Registry.MarriageAlliances.FirstOrDefault(x=>x.Id==id);if(a==null||speaker.Map!=other.Map||speaker.Position.DistanceTo(other.Position)>3)return;
            int repA=c.Registry.FamilyOf(speaker.thingIDNumber)==a.FamilyA?speaker.thingIDNumber:other.thingIDNumber;
            int repB=repA==speaker.thingIDNumber?other.thingIDNumber:speaker.thingIDNumber;int target=c.Registry.PreferredMarriageUnionTarget(a);
            if(!c.Registry.BeginMarriageUnion(id,repA,repB,target,Activities.Now))return;
            ResolveUnionIfAutonomous(c,a,speaker);ResolveUnionIfAutonomous(c,a,other);
            if(a.UnionState==HouseUnionState.Negotiating)Messages.Message("WFL_HouseUnion_Waiting".Translate(),speaker,MessageTypeDefOf.NeutralEvent,false);
        }

        private static void ResolveUnionIfAutonomous(FamilyComponent c,MarriageAlliance a,Pawn representative)
        {
            if(Find.Selector.IsSelected(representative)||PerspectiveShiftBridge.IsAvatar(representative))return;
            int family=c.Registry.FamilyOf(representative.thingIDNumber);c.Registry.DecideMarriageUnion(a.Id,representative.thingIDNumber,c.Registry.WouldHouseSupportUnion(a,family),Activities.Now);
        }

        internal static bool DecideUnionAs(FamilyComponent c,MarriageAlliance a,Pawn representative,bool support)
            =>c!=null&&a!=null&&representative!=null&&c.Registry.DecideMarriageUnion(a.Id,representative.thingIDNumber,support,Activities.Now);
    }
}
