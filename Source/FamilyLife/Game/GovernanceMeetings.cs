using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// One pawn walking to another to do a piece of governance (hear a request, look after a guest, mediate, recruit, confront). Every meeting
    /// uses the same talk job as the other family conversations, so it is interrupted, drafted-over and reserved the same way.
    /// </summary>
    internal static class GovernanceMeetings
    {
        private static int tokenCounter;

        internal static void Reset() => tokenCounter = 0;

        internal static bool Handles(JobDef def) => def != null && (def == WFL_StoryDefOf.WFL_StewardPetition || def == WFL_StoryDefOf.WFL_StewardGuest ||
            def == WFL_StoryDefOf.WFL_StewardMediate || def == WFL_StoryDefOf.WFL_StewardNegotiate || def == WFL_StoryDefOf.WFL_CoupRecruit || def == WFL_StoryDefOf.WFL_CoupConfront || def == WFL_StoryDefOf.WFL_HeistRecruit ||
            def == WFL_StoryDefOf.WFL_ProtectionOffer || def == WFL_StoryDefOf.WFL_OfficeLesson || def == WFL_StoryDefOf.WFL_Checkup || def == WFL_StoryDefOf.WFL_InstitutionPetition);

        /// <summary>A number that has not been used before, handed over only when physical work was really finished.</summary>
        internal static int NextToken() => (int)(unchecked((uint)((Find.TickManager?.TicksGame ?? 0) * 7919 + (++tokenCounter) * 104729)) % 2000000000u) + 1;

        internal static bool Start(Pawn actor, Pawn other, JobDef def, int id, bool byPlayer = false, bool recipientConsented = false)
        {
            if (actor == null || other == null || def == null || actor == other || !actor.Spawned || !other.Spawned || actor.Map != other.Map) return false;
            if (!byPlayer && (!Activities.IsFree(actor) || actor.CurJob?.playerForced == true || Find.Selector.IsSelected(actor) || PerspectiveShiftBridge.IsAvatar(actor))) return false;
            if (!Activities.IsFree(other) || other.CurJob?.playerForced == true || (Find.Selector.IsSelected(other) || PerspectiveShiftBridge.IsAvatar(other)) && !recipientConsented) return false;
            if (!actor.CanReach(other, PathEndMode.Touch, Danger.None)) return false;
            var job = JobMaker.MakeJob(def, other); job.count = id;
            if(def==WFL_StoryDefOf.WFL_StewardPetition||def==WFL_StoryDefOf.WFL_StewardGuest||def==WFL_StoryDefOf.WFL_StewardMediate||def==WFL_StoryDefOf.WFL_StewardNegotiate||def==WFL_StoryDefOf.WFL_InstitutionPetition)
                PreferOffice(job,FamilyComponent.Current,FamilyComponent.Current?.Registry.FamilyOf(actor.thingIDNumber)??0,actor,other);
            if (def == WFL_StoryDefOf.WFL_Checkup && OfficeDriver.MedicalRoomCell(actor, out var room)) job.targetB = room;       // the examination is held in the medical room when there is one
            actor.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            return true;
        }

        internal static void PreferOffice(Job job,FamilyComponent c,int family,Pawn actor,Pawn other)
        {
            if(job!=null&&Governance.TryMeetingCell(c,family,actor,other,out var cell))job.targetB=cell;
        }

        internal static void Complete(Pawn speaker, Pawn other, JobDef def, int id)
        {
            var c = FamilyComponent.Current; var r = c?.Registry;
            if (r == null || speaker.Map != other.Map || speaker.Position.DistanceTo(other.Position) > 3) return;
            int now = Activities.Now, a = speaker.thingIDNumber, b = other.thingIDNumber;
            if (def == WFL_StoryDefOf.WFL_StewardPetition)
            {
                if (r.ReceivePetitionAsOffice(id, a, now)) FinishDuty(r, StewardDutyKind.ReceivePetition, id, 0, now);
            }
            else if (def == WFL_StoryDefOf.WFL_StewardGuest)
            {
                r.ChangeBond(b, a, new BondChange(1f, 1.5f, -1f, 1f), now);
                r.NoteHouseContact(b, r.FamilyOf(a), now);
                FinishDuty(r, StewardDutyKind.GuestCare, 0, NextToken(), now, a);
            }
            else if (def == WFL_StoryDefOf.WFL_StewardMediate)
            {
                var rift = r.Rifts.FirstOrDefault(x => x.Id == id && x.ResolvedTick == 0);
                if (rift != null)
                {
                    int partner = rift.A == b ? rift.B : rift.A;
                    r.ChangeBond(b, partner, new BondChange(1f, 2f, -3f, 1f), now);
                    r.ChangeBond(partner, b, new BondChange(0f, 0f, -1f, 0f), now);
                    rift.Severity = Math.Max(0f, rift.Severity - 15f);
                }
                FinishDuty(r, StewardDutyKind.Mediate, id, NextToken(), now, a);
            }
            else if (def == WFL_StoryDefOf.WFL_OfficeLesson)
            {
                if (r.GetPerson(b)?.Juvenile == true && r.OfficeFor(OfficeKind.Teacher)?.HolderId == a) OfficeDriver.CompleteLesson(r, speaker, other, now);
            }
            else if (def == WFL_StoryDefOf.WFL_Checkup)
            {
                if (r.OfficeFor(OfficeKind.Healer)?.HolderId == a) OfficeDriver.CompleteCheckup(r, speaker, other, now);
            }
            else if(def==WFL_StoryDefOf.WFL_StewardNegotiate)
                FinishDuty(r,StewardDutyKind.Negotiate,-id,NextToken(),now,a);
            else if (def == WFL_StoryDefOf.WFL_InstitutionPetition)
            {
                int family = r.FamilyOf(b);
                var movement = r.InstitutionMovements.FirstOrDefault(x => x.Id == id);
                if (r.AnswerInstitutionMovement(id, family, b, r.WouldSupportInstitutionMovement(id, family), now) && movement != null && !movement.Open)
                    Messages.Message((movement.State == InstitutionMovementState.Adopted ? "WFL_Movement_Adopted" : "WFL_Movement_Rejected").Translate(("WFL_Institution_" + movement.Target).Translate()), MessageTypeDefOf.NeutralEvent, false);
            }
            else if (def == WFL_StoryDefOf.WFL_CoupRecruit)
            {
                var coup = r.Coups.FirstOrDefault(x => x.Id == id && x.Open);
                if (coup == null) return;
                var toLeader = r.GetBond(b, coup.TargetLeaderId); var toOrganizer = r.GetBond(b, a);
                var answer = (toLeader?.Attachment ?? 0f) >= 50f ? RecruitAnswer.Expose
                    : (toOrganizer?.Trust ?? 0f) >= 25f && (toLeader?.Tension ?? 0f) >= 20f ? RecruitAnswer.Join : RecruitAnswer.Refuse;
                r.RecruitForCoup(id, b, answer, now);
            }
            else if (def == WFL_StoryDefOf.WFL_CoupConfront)
            {
                r.ResolveCoup(id, Political(c), now);
                Messages.Message("WFL_Coup_Confronted".Translate(speaker.LabelShort, other.LabelShort), speaker, MessageTypeDefOf.NeutralEvent, false);
            }
            else if (def == WFL_StoryDefOf.WFL_ProtectionOffer)
            {
                int protector = r.FamilyOf(a), protectedFamily = r.FamilyOf(b);
                if (protector == 0 || protectedFamily == 0 || protector == protectedFamily || r.HeadOf(protectedFamily) != b || r.HeadOf(protector) != a) return;
                var bond = r.GetBond(b, a); float trust = bond?.Trust ?? 0f, tension = bond?.Tension ?? 0f;
                bool needsIt = (r.GetLineage(protectedFamily)?.Situation?.Safety ?? 50f) < 50f && trust >= 20f, afraid = tension >= 40f && trust < 20f;
                var deal = r.ProposeProtection(protector, protectedFamily, 15, now, afraid && !needsIt);
                if (deal == null || PerspectiveShiftBridge.IsAvatar(other)) return;       // a player-controlled head answers for themselves
                if (needsIt || afraid)
                {
                    r.AgreeProtection(deal.Id, b, now);
                    if (deal.Coerced) r.RecordIncident(IncidentKind.Intimidation, a, b, speaker.Map.uniqueID, now, 18f, new[] { a, b });
                }
                else { r.EndProtection(deal.Id, now); r.ChangeBond(b, a, new BondChange(0f, -1f, 1f, 0f), now); }
            }
            else if (def == WFL_StoryDefOf.WFL_HeistRecruit)
            {
                var heist = r.Heists.FirstOrDefault(x => x.Id == id && x.State == HeistState.Planning);
                if (heist == null) return;
                var agency = r.AgencyOf(b);
                bool aggrieved = agency != null && agency.Excluded >= 2 && agency.Included < 3;
                float loyalty = r.GetBond(b, a)?.Attachment ?? 0f;
                var answer = loyalty >= 25f && (aggrieved || loyalty >= 55f) ? RecruitAnswer.Join
                    : (r.GetBond(b, r.HeadOf(heist.TargetFamilyId))?.Trust ?? 0f) >= 50f ? RecruitAnswer.Expose : RecruitAnswer.Refuse;
                r.RecruitForHeist(id, b, answer, now);
            }
        }

        private static void FinishDuty(FamilyRegistry r, StewardDutyKind kind, int target, int token, int now, int holder = 0)
        {
            var duty = r.StewardDuties.FirstOrDefault(d => d.Open && d.Kind == kind && (target == 0 || d.TargetId == target) && (holder == 0 || d.HolderId == holder));
            if (duty != null) r.CompleteStewardDuty(duty.Id, token, now);
        }

        // ---- what the political rules need to know about real pawns ----

        internal static PoliticalContext Political(FamilyComponent c) => new PoliticalContext
        {
            IdeologyFit = (v, cand) =>
            {
                if (!ModsConfig.IdeologyActive) return 0f;
                var pv = c.TryGetPawn(v); var pc = c.TryGetPawn(cand);
                if (pv?.Ideo == null || pc?.Ideo == null) return 0f;
                return pv.Ideo == pc.Ideo ? .6f : -.3f;
            },
            MaterialCapacity = id => Capacity(c.TryGetPawn(id)),
            DefenceCapacity = leader =>
            {
                var lp = c.TryGetPawn(leader);
                float total = Capacity(lp);
                int guards = 0;
                foreach (var m in c.Registry.Persons.Where(p => p.InColony && !p.Juvenile && p.Id != leader).Take(60))
                {
                    if (guards >= 4) break;
                    if ((c.Registry.GetBond(m.Id, leader)?.Attachment ?? 0f) < 50f) continue;
                    float cap = Capacity(c.TryGetPawn(m.Id));
                    if (cap > .2f) { total += cap * .5f; guards++; }
                }
                return Math.Min(3f, total);
            },
            Free = id => Governance.Free(c, id)
        };

        /// <summary>What a person could actually bring to a fight: trained, able, and armed. Not a head count.</summary>
        internal static float Capacity(Pawn p)
        {
            if (p == null || p.Dead || p.Downed) return 0f;
            float skill = ((p.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0) + (p.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0)) / 40f;
            float armed = p.equipment?.Primary != null ? .25f : 0f;
            float able = p.health?.capacities?.GetLevel(PawnCapacityDefOf.Moving) ?? 1f;
            return Math.Max(0f, Math.Min(1f, (Math.Min(.7f, skill) + armed) * Math.Min(1f, able)));
        }
    }

    /// <summary>A colonist walking to the polling place and casting their own ballot.</summary>
    public class JobDriver_CastVote : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Drafted || pawn.InMentalState);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Wait(180);
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    var c = FamilyComponent.Current;
                    c?.Registry.CastBallot(job.count, pawn.thingIDNumber, GovernanceMeetings.Political(c), Activities.Now);
                }
            };
        }
    }
}
