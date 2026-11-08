using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The Leadership tab (the old House tab, reorganised into cards): the family's offices with their holders and why, its ambition, the alliances and
    /// requests it is dealing with, the institutions around it, its identity, and its heirlooms. A card with nothing in it is not shown.
    /// </summary>
    public partial class Window_Families
    {
        private void DrawLeadership(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var house = view.House;
            DrawOfficeCards(v, ref y, component, registry);
            if (!view.Insider)
            {
                FamilyKit.EmptyState(v, ref y, "WFL_FW_NotKnownTo".Translate(view.NameOfPerspective(registry)));
                return;
            }
            var col = new Rect(0f, 0f, v.width, 0f);
            DrawAmbitionCard(col, ref y, registry, house);
            DrawAlliancesCard(col, ref y, component, registry, house);
            DrawInstitutionsCard(col, ref y, component, registry, house);
            DrawIdentityCard(col, ref y, component, registry, house);
            DrawEstatesCard(col, ref y, component, registry, house);
        }

        // ------------------------------------------------------------------ offices

        private void DrawOfficeCards(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            FamilyKit.SectionHeader(v, ref y, "WFL_FW_Card_Offices".Translate());
            const float cardW = 300f;
            int perRow = Mathf.Max(1, Mathf.FloorToInt((v.width + FamilyKit.Gap) / (cardW + FamilyKit.Gap)));
            float w = Mathf.Floor((v.width - (perRow - 1) * FamilyKit.Gap) / perRow);
            float rowTop = y, rowMax = y;
            int i = 0;
            foreach (var data in view.Roles)
            {
                int c = i % perRow;
                if (c == 0 && i > 0) { rowTop = rowMax; }
                float cardY = rowTop;
                DrawOfficeCard(new Rect(c * (w + FamilyKit.Gap), 0f, w, 0f), ref cardY, component, registry, data);
                rowMax = Mathf.Max(rowMax, cardY);
                i++;
                if (c == perRow - 1) rowTop = rowMax;
            }
            y = rowMax;
        }

        private void DrawOfficeCard(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, RoleCardData data)
        {
            var house = view.House;
            string title = data.Role == FamilyRole.HouseHead || data.Role == FamilyRole.Steward || data.Role == FamilyRole.PublicHeir
                ? RoleBar.TitleOf(registry, new RoleEntry(data.Role, data.FamilyId)).CapitalizeFirst() : RoleIcons.Label(data.Role).CapitalizeFirst();
            var card = FamilyKit.BeginCard(col, ref y, "role." + data.Role, title, RoleIcons.Description(data.Role));
            var cv = card.View;
            float cy = 0f;
            RoleIcons.Draw(new Rect(cv.width - 40f, 0f, 38f, 38f), data.Role, true);
            float textW = cv.width - 46f;

            var holder = data.HolderId != 0 ? registry.GetPerson(data.HolderId) : null;
            if (data.Hidden)
            {
                GUI.color = FamilyKit.Dim;
                Widgets.Label(new Rect(0f, cy, textW, 22f), "WFL_FW_NotKnownTo".Translate(view.NameOfPerspective(registry)));
                GUI.color = Color.white;
            }
            else if (holder != null)
            {
                DrawPortrait(new Rect(0f, cy, 24f, 30f), holder, component);
                Widgets.Label(new Rect(30f, cy + 4f, textW - 30f, 22f), holder.FullName.Truncate(textW - 30f));
            }
            else
            {
                GUI.color = FamilyKit.Dim;
                Widgets.Label(new Rect(0f, cy + 4f, textW, 22f), (data.Role == FamilyRole.PublicHeir && house.DesignatedHeirId == 0 ? "WFL_FW_NoHeir" : "WFL_FW_Vacant").Translate());
                GUI.color = Color.white;
            }
            cy += 36f;

            Text.Font = GameFont.Tiny;
            GUI.color = FamilyKit.Dim;
            string description = RoleIcons.Description(data.Role);
            float dh = Text.CalcHeight(description, cv.width);
            Widgets.Label(new Rect(0f, cy, cv.width, dh), description);
            cy += dh + 4f;
            GUI.color = new Color(0.78f, 0.82f, 0.7f);
            string why = RoleWhy(registry, data, house);
            if (why.Length > 0)
            {
                float wh = Text.CalcHeight(why, cv.width);
                Widgets.Label(new Rect(0f, cy, cv.width, wh), why);
                cy += wh + 4f;
            }
            string status = RoleStatus(registry, data, house);
            if (status.Length > 0)
            {
                GUI.color = FamilyKit.Gold;
                float sh = Text.CalcHeight(status, cv.width);
                Widgets.Label(new Rect(0f, cy, cv.width, sh), status);
                cy += sh + 4f;
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Actions the acting pawn could take, each greyed out with its reason when not available.
            float bx = 0f;
            if (holder != null && holder.InColony && !data.Hidden && FamilyKit.Action(new Rect(bx, cy, 110f, 24f), "WFL_FW_FindInColony".Translate(), true, null, "WFL_FW_FindTip".Translate())) FamilyUi.Jump(holder.Id);
            if (holder != null && holder.InColony && !data.Hidden) bx += 116f;
            if (data.Role == FamilyRole.PublicHeir)
            {
                int actor = component.PerspectivePawnId;
                bool isHead = actor == 0 || registry.HeadOf(house.Id) == actor;
                if (FamilyKit.Action(new Rect(bx, cy, 130f, 24f), "WFL_FW_NameHeir".Translate(), isHead, WhyNot(registry, house, true), "WFL_FW_NameHeirTip".Translate())) OpenNameHeirMenu(component, registry, house);
                bx += 136f;
            }
            if (bx > 0f) cy += 28f;
            FamilyKit.EndCard(card, cy, ref y);
        }

        private string RoleWhy(FamilyRegistry registry, RoleCardData data, Lineage house)
        {
            if (data.Hidden || data.Vacant) return "";
            switch (data.Role)
            {
                case FamilyRole.HouseHead: return "WFL_FW_RoleWhy_Head".Translate(("WFL_Custom_" + registry.CustomOf(house.Id)).Translate()).ToString();
                case FamilyRole.Steward: return "WFL_FW_RoleWhy_Steward".Translate().ToString();
                case FamilyRole.PublicHeir: return "WFL_FW_RoleWhy_Heir".Translate().ToString();
                default:
                    return data.Why == "Appointed" ? "WFL_FW_RoleWhy_Named".Translate().ToString()
                        : data.TermEndTick > 0 ? "WFL_FW_RoleWhy_Elected".Translate(FamilyText.Date(data.TermEndTick)).ToString() : "WFL_FW_RoleWhy_ElectedOpen".Translate().ToString();
            }
        }

        private string RoleStatus(FamilyRegistry registry, RoleCardData data, Lineage house)
        {
            if (data.Role == FamilyRole.HouseHead)
            {
                var open = registry.OpenSuccession(house.Id);
                if (open != null) return "WFL_FW_Status_Succession".Translate(("WFL_SuccessionStage_" + open.Stage).Translate()).ToString();
                if (house.VacantHeadId != 0) return "WFL_Succession_Waiting".Translate(FamilyUi.NameOf(registry, house.VacantHeadId)).ToString();
                return "";
            }
            if (data.Vacant && data.Role == FamilyRole.Steward) return "WFL_FW_Status_NoSteward".Translate().ToString();
            if (data.Vacant && data.Role != FamilyRole.PublicHeir) return "WFL_FW_Status_Vacant".Translate().ToString();
            return "";
        }

        private void OpenNameHeirMenu(FamilyComponent component, FamilyRegistry registry, Lineage house)
        {
            int head = registry.HeadOf(house.Id);
            if (head == 0) return;
            var options = new List<FloatMenuOption>();
            var told = registry.MembersOf(house.Id).Where(p => p.InColony && !p.Juvenile).Select(p => p.Id).ToList();
            foreach (var p in registry.MembersOf(house.Id).Where(p => p.InColony && !p.Juvenile && p.Id != head).OrderBy(p => p.Name).Take(12))
            {
                int heir = p.Id;
                options.Add(new FloatMenuOption("WFL_Estate_NameAndTell".Translate(p.Name), () => registry.DesignateHeir(house.Id, head, heir, told, Activities.Now)));
                options.Add(new FloatMenuOption("WFL_Estate_NameSecretly".Translate(p.Name), () => registry.DesignateHeir(house.Id, head, heir, null, Activities.Now)));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption("WFL_Estate_NoKin".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ------------------------------------------------------------------ ambition

        private void DrawAmbitionCard(Rect col, ref float y, FamilyRegistry registry, Lineage house)
        {
            var card = FamilyKit.BeginCard(col, ref y, "l.ambition", "WFL_Ambition_Title".Translate());
            var cv = card.View;
            float cy = 0f;
            var s = house.Situation; var a = house.Ambition;
            if (s == null || s.EvaluatedTick <= 0 || a == null || a.Primary == HouseAmbitionKind.None)
            {
                FamilyKit.EmptyState(cv, ref cy, "WFL_Ambition_Assessing".Translate());
                FamilyKit.EndCard(card, cy, ref y);
                return;
            }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, cy, cv.width, 30f), ("WFL_Ambition_" + a.Primary).Translate());
            Text.Font = GameFont.Small;
            cy += 32f;
            string target = a.TargetFamilyId == 0 ? "" : " " + "WFL_Ambition_Target".Translate(registry.LineageDisplayName(registry.GetLineage(a.TargetFamilyId)));
            cy += FamilyKit.Label(cv, cy, ("WFL_Need_" + a.PrimaryReason).Translate() + target);
            if (a.TargetCapability != HouseCapability.None) cy += FamilyKit.Label(cv, cy, "WFL_Ambition_SkillGap".Translate(("WFL_Capability_" + a.TargetCapability).Translate()), 0f, true);
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_Urgency".Translate(), Mathf.RoundToInt(a.Urgency).ToString(), 80f);
            FamilyKit.SectionHeader(cv, ref cy, "WFL_FW_Approaches".Translate());
            foreach (var line in ("WFL_FW_Approach_" + a.Primary).Translate().ToString().Split('\n')) if (line.Trim().Length > 0) cy += FamilyKit.Label(cv, cy, "- " + line.Trim(), 6f);
            if (a.Secondary != HouseAmbitionKind.None)
                cy += FamilyKit.Label(cv, cy + 2f, "WFL_Ambition_Secondary".Translate(("WFL_Ambition_" + a.Secondary).Translate(), ("WFL_Need_" + a.SecondaryReason).Translate()), 0f, true);

            // How the house stands on each need, so the reason can be seen and not only read.
            FamilyKit.SectionHeader(cv, ref cy, "WFL_FW_Needs".Translate());
            var needs = new[] { HouseNeed.Sustenance, HouseNeed.LivingSpace, HouseNeed.Safety, HouseNeed.Continuity, HouseNeed.Capability, HouseNeed.Cohesion, HouseNeed.Influence, HouseNeed.Independence };
            foreach (var need in needs)
            {
                float score = s.Score(need);
                GUI.color = need == a.PrimaryReason ? FamilyKit.Gold : FamilyKit.Dim;
                Widgets.Label(new Rect(0f, cy, 110f, 20f), ("WFL_FW_Need_" + need).Translate());
                GUI.color = Color.white;
                FamilyKit.MetricBar(new Rect(114f, cy + 4f, cv.width - 114f - 36f, 11f), score, score < 35f ? FamilyKit.Urgent : score < 55f ? FamilyKit.Warn : FamilyKit.Good, ("WFL_FW_NeedTip_" + need).Translate());
                Widgets.Label(new Rect(cv.width - 32f, cy, 32f, 20f), Mathf.RoundToInt(score).ToString());
                cy += 20f;
            }
            FamilyKit.EndCard(card, cy, ref y);
        }

        // ------------------------------------------------------------------ alliances and requests

        private void DrawAlliancesCard(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage house)
        {
            int viewer = component.PerspectivePawnId;
            var petitions = registry.HousePetitions.Where(q => q.FamilyId == house.Id || q.SourceFamilyId == house.Id).OrderByDescending(q => q.CreatedTick).Take(8).ToList();
            var alliances = registry.MarriageAlliances.Where(a => (a.FamilyA == house.Id || a.FamilyB == house.Id) && registry.KnowsMarriageAlliance(viewer, a)).OrderByDescending(a => a.CreatedTick).Take(6).ToList();
            var ties = registry.TiesOf(house.Id);
            var threats = registry.Heists.Where(h => (h.FamilyId == house.Id || h.TargetFamilyId == house.Id) && (viewer == 0 || registry.CrimeKnowledgeOf(viewer, h.Id) != CrimeKnowledgeLevel.None)).ToList();
            var protection = registry.ProtectionArrangements.Where(p => (p.ProtectedFamily == house.Id || p.ProtectorFamily == house.Id) && p.EndedTick == 0).ToList();
            bool anyAsk = viewer != 0 && component.TryGetPawn(viewer) != null;

            if (petitions.Count == 0 && alliances.Count == 0 && ties.Count == 0 && threats.Count == 0 && protection.Count == 0 && !anyAsk) return;
            var card = FamilyKit.BeginCard(col, ref y, "l.alliances", "WFL_FW_Card_Alliances".Translate());
            var cv = card.View;
            float cy = 0f;
            DrawAsks(cv, ref cy, component, registry, viewer);

            if (petitions.Count > 0)
            {
                FamilyKit.SectionHeader(cv, ref cy, "WFL_Manage_Petitions".Translate());
                foreach (var q in petitions)
                    cy += FamilyKit.Label(cv, cy, "WFL_Petition_Status".Translate(FamilyUi.NameOf(registry, q.PersonId), ("WFL_PetitionKind_" + q.Kind).Translate(), ("WFL_PetitionState_" + q.State).Translate(), registry.LineageDisplayName(registry.GetLineage(q.FamilyId))), 6f);
            }
            if (alliances.Count > 0)
            {
                FamilyKit.SectionHeader(cv, ref cy, "WFL_MarriageAlliance_Title".Translate());
                foreach (var a in alliances)
                {
                    int otherFamily = a.FamilyA == house.Id ? a.FamilyB : a.FamilyA;
                    float interest = a.FamilyA == house.Id ? a.HouseAInterest : a.HouseBInterest;
                    var objections = a.FamilyA == house.Id ? a.ObjectionsA : a.ObjectionsB;
                    float top = cy;
                    cy += FamilyKit.Label(cv, cy, "WFL_MarriageAlliance_Line".Translate(FamilyUi.NameOf(registry, a.PawnA), FamilyUi.NameOf(registry, a.PawnB), ("WFL_MarriageAllianceState_" + a.State).Translate()), 6f);
                    var area = new Rect(0f, top, cv.width, cy - top);
                    Widgets.DrawHighlightIfMouseover(area);
                    TooltipHandler.TipRegion(area, "WFL_MarriageAlliance_Tip".Translate(registry.LineageDisplayName(registry.GetLineage(otherFamily)), interest.ToString("0"),
                        a.PawnAWillingness.ToString("0"), a.PawnBWillingness.ToString("0"), a.Reasons.ToString(), objections.ToString(),
                        ("WFL_MarriageConsent_" + a.ConsentA).Translate(), ("WFL_MarriageConsent_" + a.ConsentB).Translate(), ("WFL_HouseUnionState_" + a.UnionState).Translate()));
                }
            }
            if (ties.Count > 0)
            {
                FamilyKit.SectionHeader(cv, ref cy, "WFL_Moments_Standing".Translate());
                foreach (var tie in ties)
                {
                    int otherId = tie.LineageA == house.Id ? tie.LineageB : tie.LineageA;
                    string other = FamilyUi.LineageName(registry, otherId);
                    var why = new List<string>();
                    if (viewer > 0 && tie.Grievance >= 10f && !registry.Incidents.Any(i => i.ActorId == tie.WrongedBy && i.VictimId == tie.WrongedPerson && registry.KnowsIncident(viewer, i)))
                        why.Add("WFL_Perspective_UnknownCause".Translate().ToString());
                    else if (tie.Grievance >= 10f && tie.WrongedBy != 0)
                        why.Add("WFL_TieCause".Translate(FamilyUi.NameOf(registry, tie.WrongedPerson), FamilyUi.NameOf(registry, tie.WrongedBy), FamilyUi.Date(tie.GrievanceTick)).ToString());
                    else if (tie.Grievance >= 10f) why.Add("WFL_TieCauseMany".Translate().ToString());
                    if (tie.Grievance >= 20f && registry.GatheringRequests.Any(r => r.Kind == "reconciliation" && (r.LineageId == house.Id || r.LineageId == otherId))) why.Add("WFL_TieMending".Translate().ToString());
                    LineTip(cv, ref cy, "WFL_TieLine".Translate(other, ("WFL_TieState_" + tie.State).Translate(), Mathf.RoundToInt(tie.Goodwill), Mathf.RoundToInt(tie.Grievance)), string.Join("\n", why), 6f);
                }
            }
            if (threats.Count > 0 || protection.Count > 0)
            {
                FamilyKit.SectionHeader(cv, ref cy, "WFL_FW_Threats".Translate());
                foreach (var heist in threats)
                {
                    var level = viewer == 0 ? CrimeKnowledgeLevel.Fact : registry.CrimeKnowledgeOf(viewer, heist.Id);
                    if (level == CrimeKnowledgeLevel.None) continue;
                    string who = level == CrimeKnowledgeLevel.Fact ? FamilyUi.NameOf(registry, heist.OrganizerId) : "WFL_Crime_Unknown".Translate().ToString();
                    cy += FamilyKit.Label(cv, cy, "WFL_Heist_Line".Translate(("WFL_HeistState_" + heist.State).Translate(), who, ("WFL_CrimeKnowledge_" + level).Translate()), 6f, true);
                }
            }
            FamilyKit.EndCard(card, cy, ref y);
        }

        // ------------------------------------------------------------------ institutions

        private void DrawInstitutionsCard(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage house)
        {
            int viewer = component.PerspectivePawnId;
            var card = FamilyKit.BeginCard(col, ref y, "l.institutions", "WFL_FW_Card_Institutions".Translate());
            var cv = card.View;
            float cy = 0f;

            // How this house chooses, and what is happening about it.
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_SuccessionLabel".Translate(), ("WFL_Custom_" + registry.CustomOf(house.Id)).Translate(), 120f);
            var open = registry.OpenSuccession(house.Id);
            if (open != null)
            {
                cy += FamilyKit.Label(cv, cy, "WFL_Succession_Case".Translate(FamilyUi.NameOf(registry, open.FormerHeadId), ("WFL_SuccessionStage_" + open.Stage).Translate(), FamilyUi.NameOf(registry, registry.HeadOf(house.Id))), 0f);
                foreach (var candidate in registry.SuccessionCandidates(house.Id, null, open.FormerHeadId).Take(4))
                {
                    var parts = registry.VisibleLegitimacy(candidate.PersonId, house.Id, viewer);
                    string why = string.Join(", ", parts.Take(4).Select(p => ("WFL_Legit_" + p.Factor).Translate().ToString() + " " + p.Value.ToString("+0.00;-0.00")));
                    LineTip(cv, ref cy, "WFL_Succession_Candidate".Translate(FamilyUi.NameOf(registry, candidate.PersonId), candidate.Score.ToString("0"), candidate.Reluctant ? "WFL_Succession_Reluctant".Translate().ToString() : ""), why, 12f, true);
                }
            }
            else if (house.DesignatedHeirId != 0 && (viewer == 0 || house.DesignationKnownBy.Contains(viewer)))
                cy += FamilyKit.Label(cv, cy, "WFL_Succession_Heir".Translate(FamilyUi.NameOf(registry, house.DesignatedHeirId)), 0f, true);
            var closed = registry.SuccessionCases.Where(c => c.FamilyId == house.Id && !c.Open).OrderByDescending(c => c.StageTick).FirstOrDefault();
            if (closed != null) cy += FamilyKit.Label(cv, cy, "WFL_Succession_Last".Translate(FamilyUi.NameOf(registry, closed.ChosenId), ("WFL_SuccessionStage_" + closed.Stage).Translate(), closed.Dispute.ToString("0")), 0f, true);

            // Stewardship: the record of whoever holds the office, and work waiting for them.
            foreach (var duty in registry.StewardDuties.Where(d => d.FamilyId == house.Id && d.Open).Take(4))
                cy += FamilyKit.Label(cv, cy, "WFL_Duty_Line".Translate(FamilyUi.NameOf(registry, duty.HolderId), ("WFL_DutyKind_" + duty.Kind).Translate(), duty.HeadActed ? "WFL_Duty_HeadActing".Translate().ToString() : ""), 0f, true);
            var term = registry.StewardRecords.Where(s => s.FamilyId == house.Id && s.Active).OrderByDescending(s => s.AppointedTick).FirstOrDefault();
            if (term != null) cy += FamilyKit.Label(cv, cy, "WFL_Duty_Record".Translate(FamilyUi.NameOf(registry, term.PersonId), term.Successes, term.Failures, term.Interruptions, term.PetitionsHeard), 0f, true);

            // The meeting place.
            var office = registry.OfficeOf(house.Id);
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_MeetingPlace".Translate(), office == null ? "WFL_Office_None".Translate().ToString() : "WFL_Office_At".Translate(office.X, office.Z).ToString(), 120f);

            // The colony around it.
            FamilyKit.SectionHeader(cv, ref cy, "WFL_FW_ColonyLeadership".Translate());
            int leader = FamilyWindowFacts.RecognizedLeader(registry);
            var inst = registry.Institution;
            cy += FamilyKit.Label(cv, cy, "WFL_Institution_Line".Translate(("WFL_Institution_" + inst.Kind).Translate(), leader != 0 ? FamilyUi.NameOf(registry, leader) : "WFL_FW_NoLeader".Translate().ToString()));
            var movement = registry.ActiveInstitutionMovement;
            if (movement != null)
                cy += FamilyKit.Label(cv, cy, "WFL_Movement_Line".Translate(FamilyUi.NameOf(registry, movement.ProposerId), ("WFL_Institution_" + movement.Target).Translate(), movement.SupportHouseIds.Count, movement.OpposeHouseIds.Count), 8f, true);
            foreach (var matter in registry.CouncilMatters.Where(m => m.Open).Take(3))
                cy += FamilyKit.Label(cv, cy, matter.Kind == CouncilMatterKind.Union
                    ? "WFL_Ask_CouncilUnion".Translate(registry.LineageDisplayName(registry.GetLineage(matter.FamilyA)), registry.LineageDisplayName(registry.GetLineage(matter.FamilyB)))
                    : "WFL_Ask_CouncilCensure".Translate(registry.LineageDisplayName(registry.GetLineage(matter.FamilyA))), 8f, true);
            foreach (var election in registry.Elections.Where(e => e.Open).Take(2))
                cy += FamilyKit.Label(cv, cy, "WFL_Election_Line".Translate(("WFL_ElectionState_" + election.State).Translate(), FamilyUi.NameList(registry, election.Candidates)), 8f, true);
            foreach (var coup in registry.Coups.Where(c => c.Open && registry.CoupVisibleTo(c, viewer)).Take(2))
                cy += FamilyKit.Label(cv, cy, "WFL_Coup_Line".Translate(FamilyUi.NameOf(registry, coup.OrganizerId), ("WFL_CoupStage_" + coup.Stage).Translate()), 8f, true);

            // This house's own criminal direction, if it has one, as far as the viewer knows.
            var crime = house.Crime;
            bool member = viewer == 0 || registry.BelongsToFamily(registry.GetPerson(viewer), house.Id);
            if (member && crime != null && crime.Status != CrimeStatus.None)
                LineTip(cv, ref cy, "WFL_Crime_Line".Translate(("WFL_CrimeDirection_" + crime.Direction).Translate(), ("WFL_CrimeStatus_" + crime.Status).Translate()), "WFL_Crime_Tip".Translate(crime.Heat.ToString("0"), crime.ReformProgress.ToString("0")), 8f, true);
            foreach (var favor in registry.Favors.Where(f => f.Open && f.CreditorFamily == house.Id && member).Take(3))
                cy += FamilyKit.Label(cv, cy, "WFL_Favor_Line".Translate(FamilyUi.NameOf(registry, favor.DebtorId), ("WFL_FavorState_" + favor.State).Translate()), 8f, true);

            // The colony's posts and what the house wants of them.
            foreach (OfficeKind kind in System.Enum.GetValues(typeof(OfficeKind)))
            {
                var wish = registry.HouseWishFor(house.Id, kind);
                if (wish == HouseWish.None) continue;
                LineTip(cv, ref cy, ("WFL_HouseWish_" + wish).Translate(("WFL_OfficeKind_" + kind).Translate()), ("WFL_Perk_" + kind).Translate(), 8f, true);
            }
            cy += FamilyKit.Label(cv, cy + 2f, registry.HallNamed ? "WFL_Hall_Named".Translate(registry.HallX, registry.HallZ) : "WFL_Hall_Auto".Translate(), 0f, true);
            if (FamilyKit.Action(new Rect(0f, cy, 190f, 26f), "WFL_Hall_Set".Translate(), true))
                Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target =>
                {
                    var hallMap = Find.CurrentMap;
                    var room = hallMap == null || !target.Cell.IsValid ? null : target.Cell.GetRoom(hallMap);
                    if (room != null && !room.PsychologicallyOutdoors) { registry.SetHall(hallMap.uniqueID, target.Cell.x, target.Cell.z); Messages.Message("WFL_Hall_Saved".Translate(), MessageTypeDefOf.TaskCompletion, false); }
                    else Messages.Message("WFL_Hall_Invalid".Translate(), MessageTypeDefOf.RejectInput, false);
                });
            if (registry.HallNamed && FamilyKit.Action(new Rect(196f, cy, 150f, 26f), "WFL_Hall_Clear".Translate(), true)) registry.ClearHall();
            cy += 32f;
            if (registry.NextAssemblyTick > 0) cy += FamilyKit.Label(cv, cy, "WFL_Assembly_Next".Translate(FamilyUi.Date(registry.NextAssemblyTick)), 0f, true);
            var openAssembly = registry.OpenAssembly;
            if (openAssembly != null)
                foreach (var race in openAssembly.Races)
                    cy += FamilyKit.Label(cv, cy, "WFL_Assembly_RaceLine".Translate(("WFL_OfficeKind_" + race.Kind).Translate(), ("WFL_AssemblyState_" + openAssembly.State).Translate(), FamilyUi.NameList(registry, race.Candidates)), 8f, true);
            FamilyKit.EndCard(card, cy, ref y);
        }

        // ------------------------------------------------------------------ identity

        private void DrawIdentityCard(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage house)
        {
            var card = FamilyKit.BeginCard(col, ref y, "l.identity", "WFL_FW_Card_Identity".Translate());
            var cv = card.View;
            float cy = 0f;
            var associates = registry.HouseContacts.Where(c => c.FamilyId == house.Id && !registry.BelongsToFamily(registry.GetPerson(c.PersonId), house.Id)).OrderByDescending(c => c.Respect).Take(12).ToList();
            if (associates.Count > 0)
            {
                FamilyKit.SectionHeader(cv, ref cy, "WFL_Associates_Title".Translate());
                foreach (var c in associates)
                    cy += FamilyKit.Label(cv, cy, "WFL_Associates_Line".Translate(FamilyUi.NameOf(registry, c.PersonId), ("WFL_HouseRole_" + registry.RoleInHouse(c.PersonId, house.Id)).Translate(), c.Respect.ToString("0")), 6f, true);
            }

            bool may = MayManage(registry, house);
            FamilyKit.SectionHeader(cv, ref cy, "WFL_Manage_Crest".Translate());
            FamilyCrest.Draw(new Rect(0f, cy + 4f, 64f, 76f), house);
            var a = new Rect(76f, cy + 4f, 150f, 30f); var b = new Rect(232f, cy + 4f, 150f, 30f);
            if (FamilyKit.Action(a, "WFL_Crest_Primary".Translate(), may, WhyNot(registry, house))) house.CrestPrimary = (CrestStyle.Index(house.CrestPrimary, house.Id, CrestStyle.Colors) + 1) % CrestStyle.Colors;
            if (FamilyKit.Action(b, "WFL_Crest_Secondary".Translate(), may, WhyNot(registry, house))) house.CrestSecondary = (CrestStyle.Index(house.CrestSecondary, 7, CrestStyle.Colors) + 1) % CrestStyle.Colors;
            a.y = b.y = cy + 40f;
            if (FamilyKit.Action(a, ("WFL_Crest_Mark_" + CrestStyle.Index(house.CrestMark, house.Id / CrestStyle.Colors, CrestStyle.Marks)).Translate(), may, WhyNot(registry, house))) house.CrestMark = (CrestStyle.Index(house.CrestMark, house.Id / CrestStyle.Colors, CrestStyle.Marks) + 1) % CrestStyle.Marks;
            if (FamilyKit.Action(b, ("WFL_Crest_Pattern_" + CrestStyle.Index(house.CrestPattern, 0, CrestStyle.Patterns)).Translate(), may, WhyNot(registry, house))) house.CrestPattern = (CrestStyle.Index(house.CrestPattern, 0, CrestStyle.Patterns) + 1) % CrestStyle.Patterns;
            cy += 90f;
            float half = Mathf.Min(220f, (cv.width - 8f) / 2f);
            branchName = Widgets.TextField(new Rect(0f, cy, half, 28f), branchName);
            if (FamilyKit.Action(new Rect(half + 8f, cy, half, 28f), "WFL_House_Branch".Translate(), may, WhyNot(registry, house), "WFL_FW_BranchTip".Translate()))
            {
                var options = registry.MembersOf(house.Id).Where(p => p.InColony && !p.Juvenile).Take(100).Select(p => new FloatMenuOption(FamilyUi.NameOf(registry, p.Id), () =>
                {
                    var branch = registry.CreateFamilyBranch(p.Id, house.Id, branchName, Activities.Now);
                    if (branch != null) { State.Select(branch.Id); branchName = ""; component.FamilyMembershipChanged(); }
                })).ToList();
                if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
            }
            cy += 36f;
            FamilyKit.EndCard(card, cy, ref y);
        }

        // ------------------------------------------------------------------ heirlooms and estates

        private void DrawEstatesCard(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage house)
        {
            var card = FamilyKit.BeginCard(col, ref y, "l.estates", "WFL_Estate_Title".Translate());
            var cv = card.View;
            float cy = 0f;
            DrawInheritance(cv, ref cy, component, registry, house, false);
            DrawEstateHistory(cv, ref cy, registry, house, component.PerspectivePawnId);
            FamilyKit.EndCard(card, cy, ref y);
        }
    }
}
