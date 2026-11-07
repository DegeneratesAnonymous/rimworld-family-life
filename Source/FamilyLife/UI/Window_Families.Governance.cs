using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

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
            yOut = y;
        }

        private void DrawGovernance(Rect view, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage lineage)
        {
            int viewer = component.PerspectivePawnId;
            Heading(view, ref y, "WFL_Gov_Title".Translate());
            DrawAsks(view, ref y, component, registry, viewer);

            // Upcoming occasions with a planning card.
            foreach (var plan in registry.CeremonyPlans.Where(p => p.FamilyId == lineage.Id && p.State != PlanState.Cancelled && registry.PlanVisibleTo(p, viewer)).Take(6))
            {
                var request = registry.GatheringRequests.FirstOrDefault(r => r.Id == plan.RequestId);
                if (request == null) continue;
                string kind = ("WFL_Gathering_Title_" + LordJob_FamilyGathering.ReportKey(request.Kind)).Translate();
                float rowY = y;
                Line(view, ref y, "WFL_Plan_Line".Translate(kind, FamilyUi.Date(registry.ScheduledStart(request)), ("WFL_PlanState_" + plan.State).Translate(),
                    FamilyUi.NameOf(registry, plan.OrganizerId)), 10f);
                var button = new Rect(view.width - 150f, rowY, 140f, 24f);
                if (Widgets.ButtonText(button, "WFL_Plan_Open".Translate())) Find.WindowStack.Add(new Window_CeremonyPlan(plan.Id));
            }

            // Stewardship work and the optional office.
            foreach (var duty in registry.StewardDuties.Where(d => d.FamilyId == lineage.Id && d.Open).Take(4))
                Line(view, ref y, "WFL_Duty_Line".Translate(FamilyUi.NameOf(registry, duty.HolderId), ("WFL_DutyKind_" + duty.Kind).Translate(), duty.HeadActed ? "WFL_Duty_HeadActing".Translate().ToString() : ""), 10f, true);
            var term = registry.StewardRecords.Where(s => s.FamilyId == lineage.Id && s.Active).OrderByDescending(s => s.AppointedTick).FirstOrDefault();
            if (term != null)
                Line(view, ref y, "WFL_Duty_Record".Translate(FamilyUi.NameOf(registry, term.PersonId), term.Successes, term.Failures, term.Interruptions, term.PetitionsHeard), 10f, true);
            var office = registry.OfficeOf(lineage.Id);
            Line(view, ref y, office == null ? "WFL_Office_None".Translate() : "WFL_Office_At".Translate(office.X, office.Z), 10f, true);
            var officeButton = new Rect(10f, y, 190f, 26f);
            if (HouseButton(officeButton, "WFL_Office_Set".Translate(), registry, lineage))
            {
                int family = lineage.Id;
                Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target =>
                {
                    var map=Find.CurrentMap;var house=registry.GetLineage(family);var area=FamilyZones.AreaFor(house);
                    if (target.Cell.IsValid && map != null && target.Cell.InBounds(map) && target.Cell.Standable(map) && area?.Map==map && area[target.Cell] &&
                        registry.SetFamilyOffice(family, map.uniqueID, target.Cell.x, target.Cell.z, Activities.Now))
                        Messages.Message("WFL_Office_Saved".Translate(), MessageTypeDefOf.TaskCompletion, false);
                    else Messages.Message("WFL_Office_Invalid".Translate(),MessageTypeDefOf.RejectInput,false);
                });
            }
            if (office != null && HouseButton(new Rect(210f, y, 150f, 26f), "WFL_Office_Clear".Translate(), registry, lineage)) registry.ClearFamilyOffice(lineage.Id);
            y += 32f;

            // Who leads, how this house chooses, and what is happening about it.
            var custom = registry.CustomOf(lineage.Id);
            Line(view, ref y, "WFL_Succession_Custom".Translate(("WFL_Custom_" + custom).Translate()), 10f);
            var house = lineage;
            var open = registry.OpenSuccession(lineage.Id);
            if (house.VacantHeadId != 0 && open == null) Line(view, ref y, "WFL_Succession_Waiting".Translate(FamilyUi.NameOf(registry, house.VacantHeadId)), 10f, true);
            if (open != null)
            {
                Line(view, ref y, "WFL_Succession_Case".Translate(FamilyUi.NameOf(registry, open.FormerHeadId), ("WFL_SuccessionStage_" + open.Stage).Translate(), FamilyUi.NameOf(registry, registry.HeadOf(lineage.Id))), 10f);
                foreach (var candidate in registry.SuccessionCandidates(lineage.Id, null, open.FormerHeadId).Take(4))
                {
                    var parts = registry.VisibleLegitimacy(candidate.PersonId, lineage.Id, viewer);
                    string why = string.Join(", ", parts.Take(4).Select(p => ("WFL_Legit_" + p.Factor).Translate().ToString() + " " + p.Value.ToString("+0.00;-0.00")));
                    LineTip(view, ref y, "WFL_Succession_Candidate".Translate(FamilyUi.NameOf(registry, candidate.PersonId), candidate.Score.ToString("0"),
                        candidate.Reluctant ? "WFL_Succession_Reluctant".Translate().ToString() : ""), why, 20f, true);
                }
            }
            else if (house.DesignatedHeirId != 0 && (viewer == 0 || house.DesignationKnownBy.Contains(viewer)))
                Line(view, ref y, "WFL_Succession_Heir".Translate(FamilyUi.NameOf(registry, house.DesignatedHeirId)), 10f, true);
            var closed = registry.SuccessionCases.Where(c => c.FamilyId == lineage.Id && !c.Open).OrderByDescending(c => c.StageTick).FirstOrDefault();
            if (closed != null)
                Line(view, ref y, "WFL_Succession_Last".Translate(FamilyUi.NameOf(registry, closed.ChosenId), ("WFL_SuccessionStage_" + closed.Stage).Translate(), closed.Dispute.ToString("0")), 10f, true);

            // The colony's institution.
            var inst = registry.Institution;
            Line(view, ref y, "WFL_Institution_Line".Translate(("WFL_Institution_" + inst.Kind).Translate(), FamilyUi.NameOf(registry, inst.LeaderId)), 10f);
            bool anyHead = viewer == 0 || registry.Lineages.Any(h => h.AbsorbedIntoId == 0 && registry.HeadOf(h.Id) == viewer);
            // Constitutional setup is an overseer control, not a power a role-played head exercises by clicking a panel.
            if (ActionButton(new Rect(10f, y, 250f, 26f), "WFL_Gov_ChangeLeadership".Translate(), viewer==0))
            {
                int fallbackLeader = registry.GetPerson(inst.LeaderId)?.InColony == true ? inst.LeaderId :
                    registry.Persons.Where(p => p.InColony && !p.Juvenile).Select(p => registry.AssessLegitimacy(p.Id, registry.FamilyOf(p.Id), 0)).OrderByDescending(a => a.Score).Select(a => a.PersonId).FirstOrDefault();
                var options = new List<FloatMenuOption>
                {
                    new FloatMenuOption(("WFL_Institution_" + InstitutionKind.Consensus).Translate(), () => registry.SetInstitution(InstitutionKind.Consensus, 0, Activities.Now)),
                    new FloatMenuOption(("WFL_Institution_" + InstitutionKind.Council).Translate(), () => registry.SetInstitution(InstitutionKind.Council, 0, Activities.Now)),
                    new FloatMenuOption(("WFL_Institution_" + InstitutionKind.Election).Translate(), () => registry.SetInstitution(InstitutionKind.Election, fallbackLeader, Activities.Now))
                };
                foreach (var h in registry.Lineages.Where(x => x.AbsorbedIntoId == 0).Select(x => registry.HeadOf(x.Id)).Where(id => id != 0).Distinct().Take(8))
                {
                    int head = h;
                    options.Add(new FloatMenuOption(("WFL_Institution_" + InstitutionKind.Authoritarian).Translate() + ": " + FamilyUi.NameOf(registry, head), () => registry.SetInstitution(InstitutionKind.Authoritarian, head, Activities.Now)));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            if (ActionButton(new Rect(270f, y, 200f, 26f), "WFL_Gov_CallElection".Translate(), anyHead && inst.Kind == InstitutionKind.Election))
            {
                var field = registry.Persons.Where(p => p.InColony && !p.Juvenile).Select(p => registry.AssessLegitimacy(p.Id, registry.FamilyOf(p.Id), 0))
                    .Where(a => a.Score >= 30f && !a.Reluctant).OrderByDescending(a => a.Score).ThenBy(a => a.PersonId).Take(4).Select(a => a.PersonId).ToList();
                Messages.Message(registry.CallElection(field, Activities.Now) != null ? "WFL_Gov_ElectionCalled".Translate() : "WFL_Gov_ElectionNotPossible".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }
            y += 32f;
            foreach (var matter in registry.CouncilMatters.Where(m => m.Open).Take(3))
                Line(view, ref y, matter.Kind == CouncilMatterKind.Union
                    ? "WFL_Ask_CouncilUnion".Translate(registry.LineageDisplayName(registry.GetLineage(matter.FamilyA)), registry.LineageDisplayName(registry.GetLineage(matter.FamilyB)))
                    : "WFL_Ask_CouncilCensure".Translate(registry.LineageDisplayName(registry.GetLineage(matter.FamilyA))), 20f, true);
            foreach (var election in registry.Elections.Where(e => e.Open).Take(2))
                Line(view, ref y, "WFL_Election_Line".Translate(("WFL_ElectionState_" + election.State).Translate(), FamilyUi.NameList(registry, election.Candidates)), 20f, true);
            foreach (var coup in registry.Coups.Where(c => c.Open && registry.CoupVisibleTo(c, viewer)).Take(2))
                Line(view, ref y, "WFL_Coup_Line".Translate(FamilyUi.NameOf(registry, coup.OrganizerId), ("WFL_CoupStage_" + coup.Stage).Translate()), 20f, true);

            // This house's own criminal direction, if it has one, and what the viewer knows of any job.
            var crime = lineage.Crime;
            bool member = viewer == 0 || registry.BelongsToFamily(registry.GetPerson(viewer), lineage.Id);
            if (member && crime != null && crime.Status != CrimeStatus.None)
                LineTip(view, ref y, "WFL_Crime_Line".Translate(("WFL_CrimeDirection_" + crime.Direction).Translate(), ("WFL_CrimeStatus_" + crime.Status).Translate()),
                    "WFL_Crime_Tip".Translate(crime.Heat.ToString("0"), crime.ReformProgress.ToString("0")), 10f, true);
            foreach (var favor in registry.Favors.Where(f => f.Open && f.CreditorFamily == lineage.Id && member).Take(3))
                Line(view, ref y, "WFL_Favor_Line".Translate(FamilyUi.NameOf(registry, favor.DebtorId), ("WFL_FavorState_" + favor.State).Translate()), 20f, true);
            foreach (var heist in registry.Heists.Where(h => h.FamilyId == lineage.Id || h.TargetFamilyId == lineage.Id))
            {
                var level = viewer == 0 ? CrimeKnowledgeLevel.Fact : registry.CrimeKnowledgeOf(viewer, heist.Id);
                if (level == CrimeKnowledgeLevel.None) continue;
                string who = level == CrimeKnowledgeLevel.Fact ? FamilyUi.NameOf(registry, heist.OrganizerId) : "WFL_Crime_Unknown".Translate().ToString();
                Line(view, ref y, "WFL_Heist_Line".Translate(("WFL_HeistState_" + heist.State).Translate(), who, ("WFL_CrimeKnowledge_" + level).Translate()), 20f, true);
            }

            // The colony's posts, any camping wish this house has, and relatives on the road.
            Line(view, ref y, "WFL_Offices_Title".Translate(), 0f, true);
            foreach (OfficeKind kind in System.Enum.GetValues(typeof(OfficeKind)))
            {
                var post = registry.OfficeFor(kind);
                string title = ("WFL_OfficeKind_" + kind).Translate();
                if (post == null || !post.Held) { Line(view, ref y, "WFL_Office_Vacant".Translate(title), 10f, true); continue; }
                string until = FamilyRegistry.IsAppointed(kind) ? "WFL_Office_Appointed".Translate().ToString() : "WFL_Office_Until".Translate(FamilyUi.Date(post.TermEndTick)).ToString();
                LineTip(view, ref y, "WFL_Office_Holder".Translate(title, FamilyUi.NameOf(registry, post.HolderId), until),
                    "WFL_Office_Tip".Translate(post.Work, post.Backers, post.Opponents, AssemblyDriver.GradeWord(registry.OfficeGrade(kind))).ToString() + "\n\n" + ("WFL_Perk_" + kind).Translate(), 10f, true);
            }
            foreach (OfficeKind kind in System.Enum.GetValues(typeof(OfficeKind)))
            {
                var wish = registry.HouseWishFor(lineage.Id, kind);
                if (wish == HouseWish.None) continue;
                string post = ("WFL_OfficeKind_" + kind).Translate();
                LineTip(view, ref y, ("WFL_HouseWish_" + wish).Translate(post), ("WFL_Perk_" + kind).Translate(), 20f, true);
            }
            Line(view, ref y, registry.HallNamed ? "WFL_Hall_Named".Translate(registry.HallX, registry.HallZ) : "WFL_Hall_Auto".Translate(), 10f, true);
            if (Widgets.ButtonText(new Rect(10f, y, 190f, 26f), "WFL_Hall_Set".Translate()))
                Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target =>
                {
                    var hallMap = Find.CurrentMap;
                    var room = hallMap == null || !target.Cell.IsValid ? null : target.Cell.GetRoom(hallMap);
                    if (room != null && !room.PsychologicallyOutdoors) { registry.SetHall(hallMap.uniqueID, target.Cell.x, target.Cell.z); Messages.Message("WFL_Hall_Saved".Translate(), MessageTypeDefOf.TaskCompletion, false); }
                    else Messages.Message("WFL_Hall_Invalid".Translate(), MessageTypeDefOf.RejectInput, false);
                });
            if (registry.HallNamed && Widgets.ButtonText(new Rect(210f, y, 150f, 26f), "WFL_Hall_Clear".Translate())) registry.ClearHall();
            y += 32f;
            if (registry.NextAssemblyTick > 0) Line(view, ref y, "WFL_Assembly_Next".Translate(FamilyUi.Date(registry.NextAssemblyTick)), 10f, true);
            var openAssembly = registry.OpenAssembly;
            if (openAssembly != null)
                foreach (var race in openAssembly.Races)
                    Line(view, ref y, "WFL_Assembly_RaceLine".Translate(("WFL_OfficeKind_" + race.Kind).Translate(), ("WFL_AssemblyState_" + openAssembly.State).Translate(), FamilyUi.NameList(registry, race.Candidates)), 20f, true);
            foreach (var trip in registry.CampTrips.Where(t => t.FamilyId == lineage.Id && t.Open).Take(1).ToList())
            {
                Line(view, ref y, "WFL_Camp_Line".Translate(("WFL_CampMotive_" + trip.Motive).Translate(), ("WFL_CampState_" + trip.State).Translate(), (trip.StayTicks / 2500).ToString(),
                    FamilyUi.NameList(registry, trip.Suggested)), 10f, true);
                float x = 10f;
                if (trip.State == CampState.Proposed)
                {
                    if (Widgets.ButtonText(new Rect(x, y, 150f, 26f), "WFL_Camp_Plan".Translate())) CampingDriver.Authorize(component, trip);
                    x += 156f;
                    if (Widgets.ButtonText(new Rect(x, y, 120f, 26f), "WFL_Camp_Decline".Translate())) registry.DeclineCamp(trip.Id, Activities.Now);
                    x += 126f;
                }
                if (trip.State == CampState.Authorized && Widgets.ButtonText(new Rect(x, y, 150f, 26f), "WFL_Camp_FormCaravan".Translate())) CampingDriver.OpenCaravanForm();
                if (trip.State == CampState.Authorized) x += 156f;
                if (Widgets.ButtonText(new Rect(x, y, 140f, 26f), "WFL_Camp_ShowPlace".Translate())) WorldTiles.Jump(trip.Tile);
                y += 32f;
            }
            foreach (var visit in registry.KinVisits.Where(v => v.FamilyId == lineage.Id && v.Open).Take(2).ToList())
            {
                Line(view, ref y, "WFL_Kin_Line".Translate(("WFL_KinState_" + visit.State).Translate(), FamilyUi.NameList(registry, visit.KinIds)), 10f, true);
                if (visit.State == KinVisitState.Stranded && Widgets.ButtonText(new Rect(10f, y, 150f, 26f), "WFL_Kin_ShowPlace".Translate())) { WorldTiles.Jump(visit.StrandTile); y += 32f; }
            }
        }
    }
}
