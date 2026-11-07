using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;
namespace FamilyLife
{
    internal static class HouseInteractions
    {
        internal static bool StartRiftRepair(Pawn actor,Pawn other)
        {
            var c=FamilyComponent.Current;var r=c?.Registry;
            var rift=r?.OpenRiftBetween(actor?.thingIDNumber??0,other?.thingIDNumber??0);
            if(rift==null||actor?.Map==null||other?.Map!=actor.Map||!FamilyLifeMod.Settings.conflicts||
                !r.MayTryReconcile(rift.Id,Activities.Now)||!Activities.IsFree(other)||other.CurJob?.playerForced==true||
                !actor.CanReach(other,PathEndMode.Touch,Danger.None))return false;
            actor.jobs.StartJob(JobMaker.MakeJob(WFL_FlavorDefOf.WFL_Reconcile,other),JobCondition.InterruptForced,null,false,tag:JobTag.Misc);
            return true;
        }

        internal static bool StartIncidentRepair(Pawn actor,Pawn other,int incidentId)
        {
            var c=FamilyComponent.Current;var incident=c?.Registry.Incidents.FirstOrDefault(i=>i.Id==incidentId&&!i.Resolved);
            if(incident==null||actor?.Map==null||other?.Map!=actor.Map||incident.ActorId!=other.thingIDNumber||
                !incident.KnownBy.Contains(actor.thingIDNumber)||!Activities.IsFree(other)||other.CurJob?.playerForced==true||
                !actor.CanReach(other,PathEndMode.Touch,Danger.None))return false;
            var job=JobMaker.MakeJob(WFL_StoryDefOf.WFL_ReconcileStory,other);job.count=incident.Id;
            actor.jobs.StartJob(job,JobCondition.InterruptForced,null,false,tag:JobTag.Misc);
            return true;
        }

        /// <summary>
        /// A pawn walks over and raises a petition (or, as head, an invitation). Pawns do this on their own (see <see cref="HouseAgency"/>);
        /// <paramref name="byPlayer"/> is the player choosing it for the pawn they have selected, which may interrupt what that pawn is doing.
        /// </summary>
        internal static bool StartPetition(Pawn actor,int family,PetitionKind kind,bool invitation=false,Pawn invitee=null,bool byPlayer=false)
        {
            var c=FamilyComponent.Current;if(c==null || !FamilyLifeMod.Settings.familyStories || actor==null) return false;
            if(!byPlayer && (!Activities.IsFree(actor) || actor.CurJob?.playerForced==true || !Activities.CanSpareAttention(actor))) return false;
            var r=c.Registry;int head=r.HeadOf(family);
            var other=invitation?invitee:c.TryGetPawn(head);
            if(other==null || other==actor || !other.Spawned || other.Map!=actor.Map || !Activities.IsFree(other) || other.CurJob?.playerForced==true ||
                (!byPlayer&&(Find.Selector.IsSelected(other)||PerspectiveShiftBridge.IsAvatar(other)))||!Activities.CanSpareAttention(other) || !actor.CanReach(other,PathEndMode.Touch,Danger.None)) return false;
            if(invitation && actor.thingIDNumber!=head) return false;
            int subject=invitation?other.thingIDNumber:actor.thingIDNumber;
            var q=r.HousePetitions.FirstOrDefault(p=>p.PersonId==subject && p.FamilyId==family && p.Kind==kind && p.State==PetitionState.Proposed) ?? r.ProposePetition(subject,family,kind,Activities.Now);if(q==null)return false;
            var job=JobMaker.MakeJob(invitation?WFL_StoryDefOf.WFL_HouseInvite:WFL_StoryDefOf.WFL_HousePetition,other);job.count=q.Id;
            GovernanceMeetings.PreferOffice(job,c,family,actor,other);
            actor.jobs.StartJob(job,JobCondition.InterruptForced,null,false,tag:JobTag.Misc);return true;
        }
        internal static void Complete(Pawn speaker,Pawn other,int petition,bool invitation)
        {
            var c=FamilyComponent.Current;if(c==null || speaker.Map!=other.Map || speaker.Position.DistanceTo(other.Position)>3) return;
            var r=c.Registry;var q=r.HousePetitions.FirstOrDefault(p=>p.Id==petition);
            if(q==null || q.PersonId!=(invitation?other.thingIDNumber:speaker.thingIDNumber)) return;
            int head=invitation?speaker.thingIDNumber:other.thingIDNumber;
            if(!r.HearPetition(q.Id,head,Activities.Now)) return;
            // The player is playing the head: the petition waits for them (select the head, right-click the petitioner) instead of being settled for them.
            if(!invitation && (Find.Selector.IsSelected(other)||PerspectiveShiftBridge.IsAvatar(other)))
            {
                Messages.Message("WFL_Petition_Waiting".Translate(speaker.LabelShort,other.LabelShort),speaker,MessageTypeDefOf.NeutralEvent,false);
                return;
            }
            bool accept;
            if(invitation && q.Kind==PetitionKind.Recognition) accept=r.WouldAcceptPetition(q,head);       // an honor from the head: the head's own test, not the invitee's
            else if(invitation) accept=HouseAgency.WouldAcceptInvitation(r,other.thingIDNumber,speaker.thingIDNumber,q.FamilyId);
            else accept=r.WouldAcceptPetition(q,head);
            Decide(c,q,head,accept,speaker,other);
        }
        internal static void Decide(FamilyComponent c,HousePetition q,int head,bool accept,Pawn speaker,Pawn other)
        {
            c.Registry.DecidePetition(q.Id,head,accept,Activities.Now);c.FamilyMembershipChanged();
            Messages.Message((q.State==PetitionState.Accepted?"WFL_Petition_Accepted":"WFL_Petition_Denied").Translate(speaker.LabelShort,other.LabelShort),speaker,MessageTypeDefOf.NeutralEvent,false);
        }
    }
}
