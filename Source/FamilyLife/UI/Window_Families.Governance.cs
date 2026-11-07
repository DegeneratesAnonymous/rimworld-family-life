using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Leadership, occasions, succession, institutions and house crime, as the person looking is able to know them. A selected pawn
    /// sees only what that pawn has been told; with no one selected the overview shows everything.
    /// </summary>
    public partial class Window_Families
    {
        private void DrawGovernance(Rect view, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage lineage)
        {
            int viewer = component.PerspectivePawnId;
            Heading(view, ref y, "WFL_Gov_Title".Translate());

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
                    if (target.Cell.IsValid && Find.CurrentMap != null && registry.SetFamilyOffice(family, Find.CurrentMap.uniqueID, target.Cell.x, target.Cell.z, Activities.Now))
                        Messages.Message("WFL_Office_Saved".Translate(), MessageTypeDefOf.TaskCompletion, false);
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
            foreach (var heist in registry.Heists.Where(h => h.FamilyId == lineage.Id || h.TargetFamilyId == lineage.Id))
            {
                var level = viewer == 0 ? CrimeKnowledgeLevel.Fact : registry.CrimeKnowledgeOf(viewer, heist.Id);
                if (level == CrimeKnowledgeLevel.None) continue;
                string who = level == CrimeKnowledgeLevel.Fact ? FamilyUi.NameOf(registry, heist.OrganizerId) : "WFL_Crime_Unknown".Translate().ToString();
                Line(view, ref y, "WFL_Heist_Line".Translate(("WFL_HeistState_" + heist.State).Translate(), who, ("WFL_CrimeKnowledge_" + level).Translate()), 20f, true);
            }
        }
    }
}
