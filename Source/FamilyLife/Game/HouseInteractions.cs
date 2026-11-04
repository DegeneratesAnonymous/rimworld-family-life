using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;
namespace FamilyLife
{
    internal static class HouseInteractions
    {
        internal static bool StartPetition(Pawn actor,int family,PetitionKind kind,bool invitation=false,Pawn invitee=null)
        {
            var c=FamilyComponent.Current;if(c==null || !FamilyLifeMod.Settings.familyStories || actor==null || !Activities.IsFree(actor) || actor.CurJob?.playerForced==true || !Activities.CanSpareAttention(actor)) return false;
            var r=c.Registry;int head=r.HeadOf(family);
            var other=invitation?invitee:c.TryGetPawn(head);
            if(other==null || other==actor || !other.Spawned || other.Map!=actor.Map || !Activities.IsFree(other) || other.CurJob?.playerForced==true || !Activities.CanSpareAttention(other) || !actor.CanReach(other,PathEndMode.Touch,Danger.None)) return false;
            if(invitation && actor.thingIDNumber!=head) return false;
            int subject=invitation?other.thingIDNumber:actor.thingIDNumber;
            var q=r.HousePetitions.FirstOrDefault(p=>p.PersonId==subject && p.FamilyId==family && p.Kind==kind && p.State==PetitionState.Proposed) ?? r.ProposePetition(subject,family,kind,Activities.Now);if(q==null)return false;
            var job=JobMaker.MakeJob(invitation?WFL_StoryDefOf.WFL_HouseInvite:WFL_StoryDefOf.WFL_HousePetition,other);job.count=q.Id;
            actor.jobs.StartJob(job,JobCondition.InterruptForced,null,false,tag:JobTag.Misc);return true;
        }
        internal static void Complete(Pawn speaker,Pawn other,int petition,bool invitation)
        {
            var c=FamilyComponent.Current;if(c==null || speaker.Map!=other.Map || speaker.Position.DistanceTo(other.Position)>3) return;
            var r=c.Registry;var q=r.HousePetitions.FirstOrDefault(p=>p.Id==petition);
            if(q==null || q.PersonId!=(invitation?other.thingIDNumber:speaker.thingIDNumber)) return;
            int head=invitation?speaker.thingIDNumber:other.thingIDNumber;
            if(!r.HearPetition(q.Id,head,Activities.Now)) return;
            if(!invitation && c.PerspectivePawnId==head) return; // The player's representative decides through the panel.
            bool accept;
            if(invitation)
            {
                float trust=r.GetBond(other.thingIDNumber,speaker.thingIDNumber)?.Trust??0;
                float attachment=r.GetBond(other.thingIDNumber,speaker.thingIDNumber)?.Attachment??0;
                accept=(trust>=40 || (r.ContactOf(other.thingIDNumber,q.FamilyId)?.Respect??0)>=6) &&
                    (r.FamilyOf(other.thingIDNumber)==0 || r.AssessBelonging(other.thingIDNumber).Attachment<=attachment+15);
            }
            else accept=r.WouldAcceptPetition(q,head);
            r.DecidePetition(q.Id,head,accept,Activities.Now);c.FamilyMembershipChanged();
            Messages.Message((q.State==PetitionState.Accepted?"WFL_Petition_Accepted":"WFL_Petition_Denied").Translate(speaker.LabelShort,other.LabelShort),speaker,MessageTypeDefOf.NeutralEvent,false);
        }
    }
}
