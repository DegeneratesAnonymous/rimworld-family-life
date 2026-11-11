using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// What each Community Care specialty actually does, using only things the game already has:
    ///   Counselor    - a supportive conversation with someone whose mood is low (grief first); it leaves a good or a modest memory, never removes a mental state or a thought.
    ///   Surgeon      - a follow-up visit to someone with an implant or a missing part, giving the same bounded recent-check-up benefit a check-up gives. It performs no operation:
    ///                  surgery stays the game's own bills and work.
    ///   Midwife      - a visit to someone who is pregnant or recovering from childbirth (Biotech only; with Biotech absent the specialty simply has nobody to visit).
    ///   Veterinarian - an examination of a tamed animal, with the same bounded benefit.
    /// A Doctor who has the skills but is not yet a specialist may practise a specialty (to earn its experience), with the quality capped at Fair; a qualified specialist is not capped.
    /// Real medical work always comes first. Geneticist is not implemented (docs/CAREERS.md). Owner: Community Care.
    /// </summary>
    internal static class CareSpecialistWork
    {
        private const int Every = 5000, Cooldown = 3 * 60000, VisitTicks = 1500, ExamTicks = 600;
        private static readonly Dictionary<string, int> last = new Dictionary<string, int>();

        public static void Register(string moduleId)
        {
            FamilyFramework.Meetings.Register(new MeetingDefinition
            {
                Id = "wils.care.specialty", ModuleId = moduleId, JobDefName = "WFL_SpecialistVisit", Ticks = VisitTicks, ListenerSlackTicks = 600,
                Complete = (speaker, other, id, now) => { var c = FamilyComponent.Current; if (c != null) CompleteVisit(c, speaker, other, CareSpecialties.ByIndex(id), now); }
            });
            FamilyFramework.Roster.Register(new WorkItem { Id = moduleId + ".specialists", ModuleId = moduleId, Interval = 900, Run = now => { var c = FamilyComponent.Current; if (c != null) Work(c, now); } });
        }

        internal static void Reset() { last.Clear(); }

        private static bool Recent(string key, int now) => last.TryGetValue(key, out int t) && now >= t && now - t < Cooldown;

        private static void Work(FamilyComponent c, int now)
        {
            var r = c.Registry; var s = FamilyLifeMod.Settings; if (s != null && !s.healthChecks) return;
            foreach (int id in CareCareers.Practitioners(r))
            {
                if (r.GetPerson(id)?.InColony != true || !(c.TryGetPawn(id) is Pawn doc) || !doc.Spawned || doc.Dead) continue;
                if (!FrameworkServices.Ready(doc) || FrameworkServices.NeedsReason(doc) != null || ClinicVisits.Running(doc) || CareDemand.DoctorShouldTend(doc, now)) continue;
                if (TryAttendBirth(c, r, doc, id, now)) continue;
                var spec = ChooseSpecialty(r, doc, id);
                if (spec == null || last.TryGetValue("w" + id, out int w) && now >= w && now - w < Every) continue;
                last["w" + id] = now;
                var target = FindTarget(c, doc, spec, now);
                if (target == null || !CareDriver.Claims.TryClaim(target.thingIDNumber, id, now)) continue;
                bool started = spec.Key == "Veterinarian" ? StartExam(doc, target) : FrameworkServices.StartMeeting(doc, target, CareDefOf.WFL_SpecialistVisit, spec.Index);
                if (!started) CareDriver.Claims.Release(target.thingIDNumber); else last["t" + target.thingIDNumber + "|" + spec.Key] = now;
            }
        }

        private static readonly string[] LaborHediffs = { "Labor", "LaborPushing" };

        private static bool InLabor(Pawn p)
        {
            if (!ModsConfig.BiotechActive || p?.health?.hediffSet == null) return false;
            foreach (var n in LaborHediffs) { var d = DefDatabase<HediffDef>.GetNamedSilentFail(n); if (d != null && p.health.hediffSet.HasHediff(d)) return true; }
            return false;
        }

        /// <summary>
        /// A midwife (or a Doctor with the skills, who may practise) goes to someone in labour and stays with them. This changes nothing about the birth itself, which stays the game's own: the
        /// mother is attended, which she remembers, and the midwife earns the specialty's experience. Biotech only.
        /// </summary>
        private static bool TryAttendBirth(FamilyComponent c, FamilyRegistry r, Pawn doc, int id, int now)
        {
            if (!ModsConfig.BiotechActive) return false;
            var mid = CareSpecialties.ByKey("Midwife");
            bool qualified = r.Assignments().IsAssigned(id, mid.RoleId), practising = r.Assignments().IsAssigned(id, CareRoles.Doctor) && CareSpecialties.MeetsSkills(mid, CareerService.SkillsOf(doc));
            if (!qualified && !practising) return false;
            var mother = doc.Map?.mapPawns.FreeColonistsSpawned.Where(p => p != doc && InLabor(p) && !CareDriver.Taken(p.thingIDNumber, id, now) && doc.CanReach(p, PathEndMode.Touch, Danger.None)).OrderBy(p => p.thingIDNumber).FirstOrDefault();
            if (mother == null || !CareDriver.Claims.TryClaim(mother.thingIDNumber, id, now, 30000)) return false;
            var job = JobMaker.MakeJob(CareDefOf.WFL_AttendBirth, mother); job.playerForced = true;
            doc.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            if (doc.CurJobDef == CareDefOf.WFL_AttendBirth) return true;
            CareDriver.Claims.Release(mother.thingIDNumber); return false;
        }

        internal static bool StillInLabor(Pawn mother) => InLabor(mother);

        internal static void BirthAttended(FamilyComponent c, Pawn doc, Pawn mother, int attendedTicks, int now)
        {
            var r = c.Registry; if (attendedTicks < 600) return;
            var mid = CareSpecialties.ByKey("Midwife");
            FrameworkServices.GainMemory(mother, "WFL_BirthAttended", doc);
            CareerService.Report(c, doc, CareRoles.Career, mid.Key, "care.birth", "birth|" + mother.thingIDNumber + "|" + now / 60000, 15, now);
        }

        // ---- vanilla work that counts: real operations and real tending

        private static bool Running(out FamilyComponent c) { c = FamilyComponent.Current; return c != null && FamilyFramework.Catalog.IsEnabled(CareModule.Id) && c.Registry.Lifecycle.MayRun(CareModule.Id); }

        /// <summary>A vanilla operation was completed by <paramref name="doer"/>. Counts for the surgeon specialty (people) or the veterinarian's (animals); the operation and its outcome are the game's own.</summary>
        internal static void OnSurgery(Pawn doer, Pawn patient, int billId)
        {
            if (doer == null || patient == null || doer == patient || !doer.RaceProps.Humanlike || !Running(out var c)) return;
            int now = FrameworkServices.Now;
            if (CareerService.Report(c, doer, CareRoles.Career, patient.RaceProps.Animal ? "Veterinarian" : "Surgeon", patient.RaceProps.Animal ? "care.surgery.animal" : "care.surgery", "surgery|" + billId + "|" + doer.thingIDNumber + "|" + now, 12, now) > 0) MedicalWorkHits.OperationsCredited++;
        }

        /// <summary>A vanilla tend was done. Tending animals counts for the veterinarian specialty; tending people counts a little toward general care experience.</summary>
        internal static void OnTend(Pawn doctor, Pawn patient)
        {
            if (doctor == null || patient == null || doctor == patient || !doctor.RaceProps.Humanlike || !Running(out var c)) return;
            int now = FrameworkServices.Now;
            if (patient.RaceProps.Animal) { if (CareerService.Report(c, doctor, CareRoles.Career, "Veterinarian", "care.tend.animal", "tend|" + doctor.thingIDNumber + "|" + patient.thingIDNumber + "|" + now, 4, now) > 0) MedicalWorkHits.AnimalTendsCredited++; }
            else if (patient.RaceProps.Humanlike) CareerService.Report(c, doctor, CareRoles.Career, null, "care.tend", "tend|" + doctor.thingIDNumber + "|" + patient.thingIDNumber + "|" + now, 2, now);
        }

        /// <summary>The specialty this care worker does now: a specialist's own first; otherwise a Doctor who has the skills practises one, in a fixed order.</summary>
        private static CareSpecialty ChooseSpecialty(FamilyRegistry r, Pawn doc, int id)
        {
            var book = r.Assignments();
            var own = CareSpecialties.All.FirstOrDefault(sp => book.IsAssigned(id, sp.RoleId));
            if (own != null) return own;
            if (!book.IsAssigned(id, CareRoles.Doctor)) return null;
            var skills = CareerService.SkillsOf(doc);
            return CareSpecialties.All.FirstOrDefault(sp => CareSpecialties.MeetsSkills(sp, skills));
        }

        private static bool IsSpecialist(FamilyRegistry r, int id, CareSpecialty spec) => r.Assignments().IsAssigned(id, spec.RoleId);

        private static Pawn FindTarget(FamilyComponent c, Pawn doc, CareSpecialty spec, int now)
        {
            var map = doc.Map; if (map == null) return null;
            IEnumerable<Pawn> pool = spec.Key == "Veterinarian" ? map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Where(p => p.RaceProps.Animal) : map.mapPawns.FreeColonistsSpawned;
            return pool.Where(p => p != doc && !p.Dead && !p.Downed && !Recent("t" + p.thingIDNumber + "|" + spec.Key, now) && !CareDriver.Taken(p.thingIDNumber, doc.thingIDNumber, now) && Eligible(p, spec) && doc.CanReach(p, PathEndMode.Touch, Danger.None)
                && (spec.Key == "Veterinarian" || FrameworkServices.FreeFor(c, p.thingIDNumber) && FrameworkServices.Ready(p)))
                .OrderByDescending(p => Urgency(p, spec)).ThenBy(p => p.thingIDNumber).FirstOrDefault();
        }

        private static bool Eligible(Pawn p, CareSpecialty spec)
        {
            switch (spec.Key)
            {
                case "Counselor": return p.RaceProps.Humanlike && p.needs?.mood != null && (p.needs.mood.CurLevel < .4f || HasOpenQuarrel(p)) && !HasMemory(p, "WFL_CounsellingHelped") && !HasMemory(p, "WFL_CounsellingListened");
                case "Surgeon": return p.RaceProps.Humanlike && !CareDriver.RecentCheckup(p) && p.health?.hediffSet != null && p.health.hediffSet.hediffs.Any(h => h is Hediff_AddedPart || h is Hediff_MissingPart);
                case "Midwife": return p.RaceProps.Humanlike && !CareDriver.RecentCheckup(p) && Expecting(p);
                case "Veterinarian": return p.RaceProps.Animal && !CareDriver.RecentCheckup(p);
                default: return false;
            }
        }

        /// <summary>Pregnant or recovering from childbirth. Only Biotech has these conditions, so without it nobody is eligible.</summary>
        private static bool Expecting(Pawn p)
        {
            if (!ModsConfig.BiotechActive || p.health?.hediffSet == null) return false;
            foreach (var name in new[] { "PregnantHuman", "PostpartumExhaustion" })
            {
                var def = DefDatabase<HediffDef>.GetNamedSilentFail(name);
                if (def != null && p.health.hediffSet.HasHediff(def)) return true;
            }
            return false;
        }

        private static bool HasOpenQuarrel(Pawn p) { var r = FamilyComponent.Current?.Registry; return r != null && r.OpenRiftsOf(p.thingIDNumber).Any(x => r.MayTryReconcile(x.Id, FrameworkServices.Now)); }

        private static bool HasMemory(Pawn p, string def) => p.needs?.mood?.thoughts?.memories?.Memories?.Any(m => m.def.defName == def) == true;

        /// <summary>Someone grieving comes first for a counselor; otherwise the lowest mood.</summary>
        private static float Urgency(Pawn p, CareSpecialty spec)
        {
            if (spec.Key != "Counselor") return 0f;
            float grief = p.needs?.mood?.thoughts?.memories?.Memories?.Any(m => (m.def.defName.IndexOf("Died", StringComparison.OrdinalIgnoreCase) >= 0 || m.def.defName.IndexOf("Death", StringComparison.OrdinalIgnoreCase) >= 0) && m.MoodOffset() < 0f) == true ? 1f : 0f;
            return grief + (1f - (p.needs?.mood?.CurLevel ?? 1f));
        }

        private static bool StartExam(Pawn vet, Pawn animal)
        {
            var job = JobMaker.MakeJob(CareDefOf.WFL_VetExam, animal); job.playerForced = true;
            vet.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            return vet.CurJobDef == CareDefOf.WFL_VetExam;
        }

        /// <summary>The visit finished (a conversation for people, the examination job for animals): the effect, then the experience.</summary>
        internal static void CompleteVisit(FamilyComponent c, Pawn doc, Pawn target, CareSpecialty spec, int now)
        {
            var r = c.Registry; CareDriver.Claims.Release(target.thingIDNumber);
            if (spec == null || doc == null || target == null || target.Dead || !CareSpecialistEligibleAtEnd(target, spec)) return;
            bool full = IsSpecialist(r, doc.thingIDNumber, spec);
            try
            {
                if (spec.Key == "Counselor") Counsel(c, r, doc, target, full, now);
                else
                {
                    int medicine = PawnReadinessApi.Level(doc, SkillDefOf.Medicine), second = spec.Key == "Veterinarian" ? PawnReadinessApi.Level(doc, SkillDefOf.Animals) : PawnReadinessApi.Level(doc, SkillDefOf.Social);
                    var level = CheckupQuality.Level(CheckupQuality.Score(medicine, second, 1f, 0f, Rand.RangeInclusive(-2, 2)));
                    if (!full && level > ResultQuality.Fair) level = ResultQuality.Fair;
                    CareDriver.GiveBenefit(target, level);
                    if (target.RaceProps.Humanlike && OfficeCareView.MayKnowResult(target, doc)) MoteMaker.ThrowText(target.DrawPos, target.Map, "WFL_Checkup_Result".Translate(("WFL_Quality_" + level).Translate()), Color.white);
                }
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] A specialty visit's effect was skipped: " + ex.Message); }
            CareerService.Report(c, doc, CareRoles.Career, spec.Key, "care.specialty." + spec.Key, "spec|" + spec.Key + "|" + target.thingIDNumber + "|" + now, CareSpecialties.ActivityPoints, now);
        }

        /// <summary>
        /// A supportive talk, shaped by what Family Life knows. How well it goes depends on the counselor's Social skill, how much the person already trusts them, and whether they are
        /// grieving. It leaves a good or a modest memory and moves the bond between them a little. If the person has an open quarrel and the talk helped, it can encourage them to try to make
        /// peace, using the quarrel's own reconciliation rules; a failed encouragement changes nothing. Nothing removes a mental state or a thought.
        /// </summary>
        private static void Counsel(FamilyComponent c, FamilyRegistry r, Pawn doc, Pawn target, bool full, int now)
        {
            int social = PawnReadinessApi.Level(doc, SkillDefOf.Social);
            float trust = r.GetBond(target.thingIDNumber, doc.thingIDNumber)?.Trust ?? 0f;
            bool grieving = Grieving(target);
            bool helped = Rand.Chance(CounselingMath.HelpChance(social, trust, grieving, full));
            FrameworkServices.GainMemory(target, helped ? "WFL_CounsellingHelped" : "WFL_CounsellingListened", doc);
            r.ChangeBond(target.thingIDNumber, doc.thingIDNumber, helped ? new BondChange(1.5f, 1f, -.5f, 1f) : new BondChange(.5f, 0f, 0f, .5f), now);
            if (!helped) return;
            var rift = r.OpenRiftsOf(target.thingIDNumber).FirstOrDefault(x => r.MayTryReconcile(x.Id, now));
            if (rift == null) return;
            int other = rift.A == target.thingIDNumber ? rift.B : rift.A;
            float chance = Math.Min(.95f, ConflictTuning.ReconcileChance(r.GetBond(target.thingIDNumber, other)?.Trust ?? 0f, r.GetBond(other, target.thingIDNumber)?.Trust ?? 0f, .5f) + CounselingMath.ReconcileBoost(social));
            if (Rand.Chance(chance)) r.TryReconcile(rift.Id, true, now);          // only a success is recorded: encouragement that does not take costs them nothing
        }

        private static bool Grieving(Pawn p) => p.needs?.mood?.thoughts?.memories?.Memories?.Any(m => (m.def.defName.IndexOf("Died", StringComparison.OrdinalIgnoreCase) >= 0 || m.def.defName.IndexOf("Death", StringComparison.OrdinalIgnoreCase) >= 0) && m.MoodOffset() < 0f) == true;

        private static bool CareSpecialistEligibleAtEnd(Pawn target, CareSpecialty spec) => spec.Key == "Veterinarian" ? target.RaceProps.Animal : target.RaceProps.Humanlike;
    }

    /// <summary>A veterinarian examines a tamed animal: they go to it and spend a short while with it. A need or an emergency ends it without effect.</summary>
    public class JobDriver_VetExam : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => PawnReadinessApi.StopReason(pawn, FrameworkServices.Now) != null || CareDemand.DoctorShouldTend(pawn, FrameworkServices.Now));          // real medical work comes first
            AddFinishAction(cond => { if (job.targetA.Thing != null) CareDriver.Claims.Release(job.targetA.Thing.thingIDNumber); });
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(600).FailOnDespawnedOrNull(TargetIndex.A).WithProgressBarToilDelay(TargetIndex.A);
            yield return new Toil
            {
                initAction = () =>
                {
                    var c = FamilyComponent.Current; var animal = job.targetA.Thing as Pawn;
                    if (c != null && animal != null) CareSpecialistWork.CompleteVisit(c, pawn, animal, CareSpecialties.ByKey("Veterinarian"), FrameworkServices.Now);
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    /// <summary>A midwife stays with someone in labour until it ends (or a couple of game hours pass). A need or an emergency releases them.</summary>
    public class JobDriver_AttendBirth : JobDriver
    {
        private int attended;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref attended, "wflAttended"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => PawnReadinessApi.StopReason(pawn, FrameworkServices.Now) != null || CareDemand.DoctorShouldTend(pawn, FrameworkServices.Now));          // an emergency releases the midwife
            AddFinishAction(cond =>
            {
                var mother = job.targetA.Thing as Pawn; if (mother == null) return;
                CareDriver.Claims.Release(mother.thingIDNumber);
                var c = FamilyComponent.Current; if (c != null) CareSpecialistWork.BirthAttended(c, pawn, mother, attended, FrameworkServices.Now);
            });
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var stay = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            stay.initAction = () => pawn.pather?.StopDead();
            stay.tickAction = () =>
            {
                var mother = job.targetA.Thing as Pawn;
                if (mother == null || !CareSpecialistWork.StillInLabor(mother) || attended > 5000) { EndJobWith(JobCondition.Succeeded); return; }
                attended++;
                pawn.rotationTracker?.FaceTarget(mother);
            };
            yield return stay;
        }
    }
}
