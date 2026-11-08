using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using KVP = System.Collections.Generic.KeyValuePair<string, System.Action>;

namespace FamilyLife
{
    /// <summary>
    /// Leadership, occasions, succession, institutions and house crime, as the person looking is able to know them. A selected pawn
    /// sees only what that pawn has been told; with no one selected the overview shows everything.
    /// </summary>
    public partial class Window_Families
    {
        /// <summary>
        /// Questions that wait for a pawn the player controls. The game never answers these on their behalf: an unanswered question
        /// simply stays open and counts for nothing.
        /// </summary>
        private void DrawAsks(Rect view, ref float yOut, FamilyComponent component, FamilyRegistry registry, int viewer)
        {
            var pawn = viewer == 0 ? null : component.TryGetPawn(viewer);
            if (pawn == null) return;
            int now = Activities.Now;
            float y = yOut;
            bool any = false;
            void Answer(ref float row, string text, params KeyValuePair<string, System.Action>[] buttons)
            {
                if (!any) { Line(view, ref y, "WFL_Ask_Title".Translate(), 0f, true); any = true; }
                Line(view, ref y, text, 10f);
                float x = 10f;
                foreach (var b in buttons) { if (Widgets.ButtonText(new Rect(x, y, 130f, 26f), b.Key)) b.Value(); x += 136f; }
                y += 30f;
            }

            var election = registry.Elections.FirstOrDefault(e => e.State == ElectionState.Voting);
            if (election != null && registry.NeedsToVote(viewer) && pawn.Spawned && GovernanceDriver.PollingPlace(component, out var map, out var cell) && map == pawn.Map)
            {
                int electionId = election.Id; var cellCopy = cell;
                Answer(ref y, "WFL_Ask_Vote".Translate(), new KeyValuePair<string, System.Action>("WFL_Ask_GoVote".Translate(), () =>
                {
                    var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_CastVote, cellCopy); job.count = electionId;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }));
            }
            if (registry.NeedsToVoteInAssembly(viewer) && pawn.Spawned && GovernanceDriver.PollingPlace(component, out var amap, out var acell) && amap == pawn.Map)
            {
                int assemblyId = registry.Assemblies.First(x => x.State == AssemblyState.Voting).Id; var acellCopy = acell;
                Answer(ref y, "WFL_Ask_AssemblyVote".Translate(), new KeyValuePair<string, System.Action>("WFL_Ask_GoVote".Translate(), () =>
                {
                    var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_CastAssemblyVote, acellCopy); job.count = assemblyId;
                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                }));
            }
            foreach (var coup in registry.Coups.Where(c => c.Open && c.Participants.Any(p => p.PersonId == viewer && p.Stance == CrimeStance.Unasked)).ToList())
            {
                int id = coup.Id;
                Answer(ref y, "WFL_Ask_Coup".Translate(FamilyUi.NameOf(registry, coup.OrganizerId), FamilyUi.NameOf(registry, coup.TargetLeaderId)),
                    new KeyValuePair<string, System.Action>("WFL_Ask_Join".Translate(), () => registry.AnswerCoup(id, viewer, RecruitAnswer.Join, now)),
                    new KeyValuePair<string, System.Action>("WFL_Ask_Refuse".Translate(), () => registry.AnswerCoup(id, viewer, RecruitAnswer.Refuse, now)),
                    new KeyValuePair<string, System.Action>("WFL_Ask_Expose".Translate(), () => registry.AnswerCoup(id, viewer, RecruitAnswer.Expose, now)));
            }
            foreach(var coup in registry.Coups.Where(c=>c.Open&&c.Stage==CoupStage.Executing&&c.TargetLeaderId==viewer).ToList())
            {
                int id=coup.Id;var organizer=component.TryGetPawn(coup.OrganizerId);
                Answer(ref y,"WFL_Ask_CoupConfront".Translate(FamilyUi.NameOf(registry,coup.OrganizerId)),
                    new KeyValuePair<string,System.Action>("WFL_Ask_Meet".Translate(),()=>
                    {
                        if(organizer==null||organizer.Map!=pawn.Map||!Activities.IsFree(organizer)||!GovernanceMeetings.Start(organizer,pawn,WFL_StoryDefOf.WFL_CoupConfront,id,recipientConsented:true))
                            Messages.Message("WFL_House_ActionUnavailable".Translate(),MessageTypeDefOf.NeutralEvent,false);
                    }),
                    new KeyValuePair<string,System.Action>("WFL_Ask_DeclineMeeting".Translate(),()=>registry.AvoidCoupConfrontation(id,now)));
            }
            foreach (var heist in registry.Heists.Where(h => h.State == HeistState.Planning && h.Participants.Any(p => p.PersonId == viewer && p.Stance == CrimeStance.Unasked)).ToList())
            {
                int id = heist.Id;
                Answer(ref y, "WFL_Ask_Heist".Translate(FamilyUi.NameOf(registry, heist.OrganizerId)),
                    new KeyValuePair<string, System.Action>("WFL_Ask_Join".Translate(), () => registry.AnswerHeist(id, viewer, RecruitAnswer.Join, now)),
                    new KeyValuePair<string, System.Action>("WFL_Ask_Refuse".Translate(), () => registry.AnswerHeist(id, viewer, RecruitAnswer.Refuse, now)),
                    new KeyValuePair<string, System.Action>("WFL_Ask_Expose".Translate(), () => registry.AnswerHeist(id, viewer, RecruitAnswer.Expose, now)));
            }
            foreach (var deal in registry.ProtectionArrangements.Where(a => a.AgreedTick == 0 && a.EndedTick == 0 && registry.HeadOf(a.ProtectedFamily) == viewer).ToList())
            {
                int id = deal.Id;
                Answer(ref y, "WFL_Ask_Protection".Translate(registry.LineageDisplayName(registry.GetLineage(deal.ProtectorFamily)), deal.TributeAmount),
                    new KeyValuePair<string, System.Action>("WFL_Ask_Agree".Translate(), () => registry.AgreeProtection(id, viewer, now)),
                    new KeyValuePair<string, System.Action>("WFL_Ask_Refuse".Translate(), () => registry.EndProtection(id, now)));
            }
            var movement = registry.ActiveInstitutionMovement; int viewerFamily = registry.FamilyOf(viewer);
            if (movement != null && registry.HeadOf(viewerFamily) == viewer && !movement.ContactedHouseIds.Contains(viewerFamily))
            {
                int id = movement.Id; string target = ("WFL_Institution_" + movement.Target).Translate();
                Answer(ref y, "WFL_Ask_Movement".Translate(FamilyUi.NameOf(registry, movement.ProposerId), target),
                    new KeyValuePair<string, System.Action>("WFL_Movement_Support".Translate(), () => registry.AnswerInstitutionMovement(id, viewerFamily, viewer, true, now)),
                    new KeyValuePair<string, System.Action>("WFL_Movement_Oppose".Translate(), () => registry.AnswerInstitutionMovement(id, viewerFamily, viewer, false, now)));
            }
            foreach (var matter in registry.CouncilMatters.Where(m => m.Open && registry.CouncilVotersFor(m).Contains(viewer)).ToList())
            {
                int id = matter.Id;
                string what = matter.Kind == CouncilMatterKind.Union
                    ? "WFL_Ask_CouncilUnion".Translate(registry.LineageDisplayName(registry.GetLineage(matter.FamilyA)), registry.LineageDisplayName(registry.GetLineage(matter.FamilyB))).ToString()
                    : "WFL_Ask_CouncilCensure".Translate(registry.LineageDisplayName(registry.GetLineage(matter.FamilyA))).ToString();
                Answer(ref y, what,
                    new KeyValuePair<string, System.Action>("WFL_Ask_Agree".Translate(), () => registry.AnswerCouncil(id, viewer, true, now)),
                    new KeyValuePair<string, System.Action>("WFL_Ask_Refuse".Translate(), () => registry.AnswerCouncil(id, viewer, false, now)));
            }
            foreach (var seat in registry.Elections.Where(e => e.Council && e.State == ElectionState.Voting && e.Pending.Contains(viewer)).ToList())
            {
                int electionId = seat.Id;
                var buttons = seat.Candidates.Take(4).Select(cand => { int id = cand; return new KeyValuePair<string, System.Action>(FamilyUi.NameOf(registry, id), () => registry.CastCouncilBallot(electionId, viewer, id, now)); }).ToList();
                buttons.Add(new KeyValuePair<string, System.Action>("WFL_Ask_Abstain".Translate(), () => registry.CastCouncilBallot(electionId, viewer, 0, now)));
                Answer(ref y, "WFL_Ask_CouncilLeader".Translate(), buttons.ToArray());
            }
            int myFamily = registry.FamilyOf(viewer);
            if (myFamily != 0 && registry.CanManageHouse(viewer, myFamily))
                foreach (var petition in registry.HousePetitions.Where(p => p.FamilyId == myFamily && p.State == PetitionState.Proposed && p.ReceivedBy == 0).Take(3).ToList())
                {
                    int id = petition.Id; var petitioner = component.TryGetPawn(petition.PersonId);
                    if (petitioner == null || !petitioner.Spawned) continue;
                    Answer(ref y, "WFL_Ask_Petition".Translate(FamilyUi.NameOf(registry, petition.PersonId), ("WFL_PetitionKind_" + petition.Kind).Translate()),
                        new KeyValuePair<string, System.Action>("WFL_Ask_Hear".Translate(), () =>
                        {
                            if (!GovernanceMeetings.Start(pawn, petitioner, WFL_StoryDefOf.WFL_StewardPetition, id, byPlayer: true))
                                Messages.Message("WFL_House_ActionUnavailable".Translate(), MessageTypeDefOf.NeutralEvent, false);
                        }));
                }
            // Questions about an estate wait for the pawn the player controls. An unanswered one lapses; it is never taken as a yes.
            foreach (var pair in registry.EstateAsksFor(viewer))
            {
                var estate = pair.Case; var ask = pair.Ask;
                int caseId = estate.Id, thing = ask.ItemThingId;
                string dead = FamilyUi.NameOf(registry, estate.DeceasedId);
                if (ask.Kind == EstateAskKind.Mediate)
                {
                    Answer(ref y, "WFL_Ask_EstateMediate".Translate(dead),
                        new KVP("WFL_Ask_ApproveSpread".Translate(), () => registry.AnswerMediation(caseId, viewer, MediationChoice.Approve, 0, 0, now)),
                        new KVP("WFL_Ask_LetSettle".Translate(), () => registry.AnswerMediation(caseId, viewer, MediationChoice.LetThemSettle, 0, 0, now)));
                    foreach (var contested in estate.Items.Where(i => i.Contested && !i.Terminal).Take(3).ToList())
                    {
                        int itemId = contested.ThingId;
                        var give = contested.Claims.Where(c => !c.Relinquished && !c.Dormant).Take(4)
                            .Select(c => { int heir = c.ClaimantId; return new KVP(FamilyUi.NameOf(registry, heir), () => registry.AnswerMediation(caseId, viewer, MediationChoice.Give, itemId, heir, now)); }).ToArray();
                        if (give.Length > 0) Answer(ref y, "WFL_Ask_GiveItem".Translate(contested.Label), give);
                    }
                    continue;
                }
                var item = estate.Items.FirstOrDefault(i => i.ThingId == thing);
                if (item == null || item.Terminal) continue;
                if (ask.Kind == EstateAskKind.Press)
                    Answer(ref y, "WFL_Ask_EstatePress".Translate(item.Label, dead),
                        new KVP("WFL_Ask_PressClaim".Translate(), () => registry.AnswerHeirAsk(caseId, thing, viewer, EstateAnswer.Contest, now)),
                        new KVP("WFL_Ask_Relinquish".Translate(), () => registry.AnswerHeirAsk(caseId, thing, viewer, EstateAnswer.Relinquish, now)),
                        new KVP("WFL_Ask_AcceptOutcome".Translate(), () => registry.AnswerHeirAsk(caseId, thing, viewer, EstateAnswer.Accept, now)));
                else
                    Answer(ref y, "WFL_Ask_EstateHeir".Translate(item.Label, dead),
                        new KVP("WFL_Ask_Accept".Translate(), () => registry.AnswerHeirAsk(caseId, thing, viewer, EstateAnswer.Accept, now)),
                        new KVP("WFL_Ask_Relinquish".Translate(), () => registry.AnswerHeirAsk(caseId, thing, viewer, EstateAnswer.Relinquish, now)));
            }
            foreach (var ready in registry.ReadyToCollect(viewer).Take(3))
            {
                var heirItem = ready.Item;
                Answer(ref y, "WFL_Ask_Collect".Translate(heirItem.Label, FamilyUi.NameOf(registry, ready.Case.DeceasedId)),
                    new KVP("WFL_Ask_CollectGo".Translate(), () =>
                    {
                        if (!EstateDriver.Collect(component, pawn, heirItem)) Messages.Message("WFL_House_ActionUnavailable".Translate(), MessageTypeDefOf.NeutralEvent, false);
                    }));
            }
            yOut = y;
        }
    }
}
