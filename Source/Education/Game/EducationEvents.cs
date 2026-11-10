using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    // The annual assessment. Owner: Education.

    internal static class EducationEvents
    {
        private const int Day = FamilyLife.Domain.ActivityTuning.TicksPerDay;
        private static int Year(Map m) => FrameworkServices.Year(m);
        private static bool IsAprimay(Map m) => FrameworkServices.IsAprimay(m);

        /// <summary>
        /// Every teacher's own assessments, run from the same pass that holds classes. In Aprimay a teacher who has not yet assessed this year begins their own, for the pupils whose
        /// work they own (see <see cref="AssessmentOwnership"/>). An assessment whose teacher has gone passes to another teacher; nothing depends on the old Teacher post.
        /// </summary>
        internal static void RunAssessments(FamilyComponent c, int now)
        {
            var r = c.Registry; r.MigrateLegacyAssessment();
            var ledger = r.Assessments(); var instructors = EducationCareers.Instructors(r);
            foreach (var orphan in ledger.Active.Where(x => !instructors.Contains(x.Owner)).ToList())
                if (instructors.Count > 0) ledger.Transfer(orphan.Owner, instructors.FirstOrDefault(i => ledger.Of(i) == null));
            var eligible = new List<int>();
            foreach (int id in instructors)
            {
                var teacher = c.TryGetPawn(id); if (teacher == null || !teacher.Spawned || teacher.Dead || teacher.Map == null) continue;
                var sitting = ledger.Of(id);
                if (sitting != null) { RunTesting(c, r, ledger, sitting, teacher, now); continue; }
                if (IsEligible(c, r, ledger, instructors, teacher, now)) eligible.Add(id);
            }
            // One consolidated notice for every teacher who has assessments due. It does not hold anything up: if it stands unanswered for a day the assessments begin. A postponed or skipped
            // assessment is not started again by itself: a postponement sends a new notice when it ends, and a skip lasts the year.
            if (eligible.Count == 0) { ledger.NoticeTick = 0; return; }
            int year = Year(c.TryGetPawn(eligible[0]).Map);
            if (ledger.NoticeTick == 0 || ledger.NoticeYear != year) { SendNotice(c, r, eligible); ledger.NoticeTick = Math.Max(1, now); ledger.NoticeYear = year; }
            else if (now < ledger.NoticeTick || now - ledger.NoticeTick >= Day) BeginAll(c, now);
        }

        /// <summary>This teacher has assessments of their own due: it is Aprimay, they have not held, skipped or postponed this year's, and they own pupils' work. A teacher with nothing to assess is passed over for the year.</summary>
        private static bool IsEligible(FamilyComponent c, FamilyRegistry r, AssessmentLedger ledger, IList<int> instructors, Pawn teacher, int now)
        {
            int id = teacher.thingIDNumber; int year = Year(teacher.Map);
            if (!IsAprimay(teacher.Map) || ledger.LastYearOf(id) == year || now < ledger.PostponedUntil(id)) return false;
            if (AssessmentOwnership.PupilsFor(id, r.EducationRecord().LessonLog, instructors, year, ledger).Count == 0) { ledger.SetLastYear(id, year); return false; }
            return true;
        }

        private static List<Pawn> EligibleNow(FamilyComponent c, int now)
        {
            var r = c.Registry; var ledger = r.Assessments(); var instructors = EducationCareers.Instructors(r);
            return instructors.Where(id => ledger.Of(id) == null).Select(id => c.TryGetPawn(id)).Where(t => t != null && t.Spawned && !t.Dead && t.Map != null && IsEligible(c, r, ledger, instructors, t, now)).ToList();
        }

        private static void ClearNotice(FamilyComponent c)
        {
            c.Registry.Assessments().NoticeTick = 0;
            foreach (var l in Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_Assessment>().ToList()) Find.LetterStack.RemoveLetter(l);
        }

        internal static void BeginAll(FamilyComponent c, int now) { foreach (var t in EligibleNow(c, now)) BeginTesting(c, c.Registry, t, now, false); ClearNotice(c); }
        internal static void PostponeAll(FamilyComponent c, int now) { var l = c.Registry.Assessments(); foreach (var t in EligibleNow(c, now)) l.Postpone(t.thingIDNumber, now + 7 * Day); ClearNotice(c); }
        internal static void SkipAll(FamilyComponent c, int now) { var l = c.Registry.Assessments(); foreach (var t in EligibleNow(c, now)) l.SetLastYear(t.thingIDNumber, Year(t.Map)); ClearNotice(c); }

        private static void SendNotice(FamilyComponent c, FamilyRegistry r, List<int> teachers)
        {
            var def = DefDatabase<LetterDef>.GetNamedSilentFail("WFL_Assessment") ?? LetterDefOf.NeutralEvent;
            var names = string.Join(", ", teachers.Select(id => c.TryGetPawn(id)?.LabelShort ?? "?"));
            var letter = LetterMaker.MakeLetter("WFL_Event_Testing_Title".Translate(), "WFL_Event_Testing_TextMany".Translate(names), def);
            Find.LetterStack.ReceiveLetter(letter);
        }

        /// <summary>Begins this teacher's assessment for the pupils they own. Returns false if there is nothing for them to assess.</summary>
        internal static bool BeginTesting(FamilyComponent c, FamilyRegistry r, Pawn teacher, int now, bool announceNothing)
        {
            var ledger = r.Assessments(); int id = teacher.thingIDNumber; int year = Year(teacher.Map);
            if (ledger.Of(id) != null) return false;
            var instructors = EducationCareers.Instructors(r);
            var ids = AssessmentOwnership.PupilsFor(id, r.EducationRecord().LessonLog, instructors, year, ledger);
            if (ids.Count == 0)
            {
                if (announceNothing) Messages.Message("WFL_Event_NoPupils".Translate(), MessageTypeDefOf.RejectInput, false);
                else ledger.SetLastYear(id, year);          // nothing of theirs to assess this year
                return false;
            }
            var s = ledger.Begin(id, year, now, ids); if (s == null) return false;
            Messages.Message("WFL_Testing_Begins".Translate(teacher.LabelShort, s.Ids.Count), teacher, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        private static bool SomeoneNeedsAMakeUp(AssessmentSitting a)
        {
            for (int i = 0; i < a.Ids.Count && i < a.Try.Count && i < a.Excused.Count; i++) if (a.Try[i] < 2 && a.Excused[i] > 0) return true;
            return false;
        }

        /// <summary>
        /// Two to four sittings over two days, in the daytime only: three planned, and one make-up for anyone who was ill or called away. Nobody is summoned in the night, nobody
        /// is hurried through, and an absence for a good reason is not a failure.
        /// </summary>
        private static void RunTesting(FamilyComponent c, FamilyRegistry r, AssessmentLedger ledger, AssessmentSitting a, Pawn teacher, int now)
        {
            if (now < a.StartTick) a.StartTick = now;
            a.Align();
            int elapsed = now - a.StartTick;
            bool makeUp = SomeoneNeedsAMakeUp(a);
            if (TestingMath.Over(a.Slot, elapsed, makeUp)) { FinishTesting(c, r, ledger, a, teacher, now); return; }
            bool due = TestingMath.Due(a.Slot, elapsed) || TestingMath.MakeUpDue(a.Slot, elapsed, makeUp);
            if (!due || !TestingMath.Daytime(GenLocalDate.HourOfDay(teacher))) return;
            if (PawnReadinessApi.StopReason(teacher, now) != null) return;          // the teacher must be fit to hold it; it waits
            a.Slot++;
            bool room = ClassRooms.TeachingSpot(teacher, out var cell, out var area, out _);
            var seats = room ? ClassRooms.Seats(area, cell, a.Ids.Count, teacher.Map) : new List<IntVec3>();
            bool makeUpOnly = a.Slot > TestingMath.Offsets.Length;
            for (int i = 0; i < a.Ids.Count; i++)
            {
                if (makeUpOnly && a.Try[i] >= 2) continue;
                var kid = c.TryGetPawn(a.Ids[i]);
                if (kid == null || !kid.Spawned || kid.Dead) continue;
                string reason = PawnReadinessApi.StopReason(kid, now);
                if (reason != null || !kid.Awake() || !FrameworkServices.Available(kid)) { a.Excused[i]++; continue; }          // ill, hungry, down or asleep: excused, and may sit the make-up
                if (!FrameworkServices.Ready(kid) || ClassSessions.Board.IsBusy(kid.thingIDNumber)) continue;          // busy at something else just now (or in a class): no sitting this time, and no mark
                if (seats.Count > 0)
                {
                    var seat = seats[i % seats.Count];
                    if (!kid.CanReach(seat, PathEndMode.OnCell, Danger.None)) continue;
                    var job = JobMaker.MakeJob(EducationDefOf.WFL_TestStudent, teacher, seat);
                    job.playerForced = true;
                    kid.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                }
                else Sat(kid);
            }
        }

        /// <summary>One completed sitting for this child. Its only effect on the result is a small bounded variation: the result comes from what they studied.</summary>
        internal static void Sat(Pawn kid)
        {
            var c = FamilyComponent.Current; if (c == null) return;
            var a = c.Registry.Assessments().SittingFor(kid.thingIDNumber); if (a == null) return;
            int i = a.Ids.IndexOf(kid.thingIDNumber); if (i < 0) return;
            a.Align();
            a.Try[i]++;
            a.Score[i] += Rand.RangeInclusive(-6, 6);          // hundredths: at most a few points either way, summed over the sittings
            MoteMaker.ThrowText(kid.DrawPos, kid.Map, "WFL_Testing_Sat".Translate(), Color.white);
        }

        private static string Support(string key, Pawn kid) => ("WFL_Testing_Support_" + key).Translate(kid.LabelShort).Resolve();

        private static void FinishTesting(FamilyComponent c, FamilyRegistry r, AssessmentLedger ledger, AssessmentSitting a, Pawn teacher, int now)
        {
            var lines = new List<string>(); int judged = 0; float score = 0f;
            a.Align();
            var education = r.EducationRecord(); var instructors = EducationCareers.Instructors(r);
            int teacherLevel = PawnReadinessApi.Level(teacher, SkillDefOf.Intellectual);
            var spent = new HashSet<string>();
            for (int i = 0; i < a.Ids.Count; i++)
            {
                var kid = c.TryGetPawn(a.Ids[i]);
                if (kid == null || kid.Dead || kid.skills == null) continue;
                var owned = AssessmentOwnership.RecordsOwnedBy(a.Owner, kid.thingIDNumber, education.LessonLog, instructors, a.Year, ledger);
                foreach (var rec in owned) spent.Add(kid.thingIDNumber + "|" + rec.Skill);
                var recs = owned.OrderByDescending(x => x.Xp).Take(3).ToList();
                if (recs.Count == 0) { lines.Add("WFL_Testing_LineNothing".Translate(kid.LabelShort)); continue; }          // nothing studied since the last assessment: nothing to assess
                float noise = a.Try[i] > 0 ? a.Score[i] / 100f / a.Try[i] : 0f;
                float support = Math.Min(1f, LessonMath.Delivery(teacherLevel, PawnReadinessApi.Level(teacher, SkillDefOf.Social), 0f, 0f, 0f) / 1.2f);
                var results = recs.Select(rec => new { rec, res = Assessments.Assess(rec, a.Try[i], TestingMath.Offsets.Length, a.Excused[i], kid.skills.GetSkill(DefDatabase<SkillDef>.GetNamedSilentFail(rec.Skill) ?? SkillDefOf.Intellectual)?.Level ?? 0, support, noise) }).ToList();
                var main = results[0];
                if (main.res.Completion == Completion.Incomplete)
                {
                    bool excused = a.Excused[i] > 0;
                    lines.Add("WFL_Testing_LineIncomplete".Translate(kid.LabelShort, (excused ? "WFL_Testing_Why_Excused" : "WFL_Testing_Why_Little").Translate().Resolve()));
                    if (excused) lines.Add("  " + Support("Recover", kid));
                    continue;
                }
                var parts = new List<string>();
                foreach (var x in results)
                {
                    if (x.res.Completion == Completion.Incomplete) continue;
                    var sk = kid.skills.GetSkill(DefDatabase<SkillDef>.GetNamedSilentFail(x.rec.Skill) ?? SkillDefOf.Intellectual);
                    float bonus = Assessments.Bonus(x.rec, x.res);
                    if (sk != null && !sk.TotallyDisabled && bonus > 0f) { sk.Learn(bonus, true); parts.Add(sk.def.skillLabel.CapitalizeFirst() + " +" + (int)bonus); }
                }
                var level = main.res.Level;
                lines.Add("WFL_Testing_LineDone".Translate(kid.LabelShort, ("WFL_Quality_" + level).Translate(), main.res.Provisional ? "WFL_Testing_Provisional".Translate().Resolve() : "", parts.Count > 0 ? string.Join(", ", parts) : "-"));
                // A weak result leads to support, not a penalty: one-to-one time, a different approach, shorter sittings.
                if (level == ResultQuality.Poor) { lines.Add("  " + Support("Tutor", kid)); if (main.res.Score < .3f) lines.Add("  " + Support("Approach", kid)); }
                else if (main.rec.Exchanges > 0 && main.rec.EngageSum / main.rec.Exchanges < -.2f) lines.Add("  " + Support("Approach", kid));
                // Feelings follow the result, gently: pride for a good one, a mild thought for a weak one, and none at all for a result that could not be judged.
                if (main.res.Completion == Completion.Complete)
                    FrameworkServices.GainMemory(kid, level >= ResultQuality.Good ? "WFL_TestingProud" : level == ResultQuality.Poor ? "WFL_TestingDisappointed" : "WFL_TestingDone", teacher);
                score += (int)level / 3f; judged++;
                CareerService.Report(c, teacher, EducationCareers.Career, recs[0].Skill, "teach.assessment", "assess|" + kid.thingIDNumber + "|" + a.Year, 6, now);
            }
            // what was assessed is spent: marked done for this year (so no other teacher assesses it again) and removed from the shared record, whoever taught it
            foreach (var key in spent)
            {
                int bar = key.IndexOf('|'); int pupil = int.Parse(key.Substring(0, bar)); string skill = key.Substring(bar + 1);
                ledger.MarkDone(pupil, skill, a.Year);
            }
            education.LessonLog = education.LessonLog.Where(x => { var rec = LessonRecord.Parse(x); return rec != null && !spent.Contains(rec.Pupil + "|" + rec.Skill); }).ToList();
            if (judged > 0) r.RecordOfficeWork(OfficeKind.Teacher, teacher.thingIDNumber, FrameworkServices.NextToken(), now, .4f + .6f * score / judged);
            ledger.End(a.Owner);
            if (lines.Count > 0)
                Find.LetterStack.ReceiveLetter("WFL_Testing_ResultTitle".Translate(), "WFL_Testing_ResultText".Translate(teacher.LabelShort, string.Join("\n", lines)), LetterDefOf.NeutralEvent, teacher);
        }

        // ------------------------------------------------------------------ buttons

        /// <summary>The "Begin annual assessment" button on a lectern: the nearest teacher free to do it begins their own.</summary>
        internal static IEnumerable<Gizmo> EducationGizmos(Building b)
        {
            var room = b.GetRoom();
            if (room == null || room.PsychologicallyOutdoors) yield break;
            var c = FamilyComponent.Current; var reg = c?.Registry; if (reg == null) yield break;
            if (!ClassRooms.IsLectern(b.def) || ClassRooms.IsOtherRoom(room) || !OfficeAuthority.MayDirect(reg, FrameworkServices.AvatarId, OfficeKind.Teacher)) yield break;
            var teacher = HoldClass.ChooseTeacher(c, reg, b);
            if (teacher == null || reg.Assessments().Of(teacher.thingIDNumber) != null) yield break;
            yield return new Command_Action
            {
                defaultLabel = "WFL_Event_BeginTesting".Translate(),
                defaultDesc = "WFL_Event_BeginTestingDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("WilsFamilyLife/Roles/teacher", false) ?? BaseContent.BadTex,
                action = () => { if (OfficeAuthority.MayDirect(reg, FrameworkServices.AvatarId, OfficeKind.Teacher)) BeginTesting(c, reg, teacher, FrameworkServices.Now, true); }
            };
        }
    }

    /// <summary>One sitting of the assessment: the child goes to a desk and sits for about ten game minutes. A need or an emergency ends it without any mark against them.</summary>
    public class JobDriver_TestStudent : JobDriver
    {
        private int sat;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref sat, "wflSat"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => PawnReadinessApi.StopReason(pawn, FrameworkServices.Now) != null || FamilyComponent.Current?.Registry.Assessments().SittingFor(pawn.thingIDNumber) == null);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var sit = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            sit.initAction = () => pawn.pather?.StopDead();
            sit.tickAction = () => { if (++sat >= TestingMath.SitTicks) { EducationEvents.Sat(pawn); EndJobWith(JobCondition.Succeeded); } };
            yield return sit;
        }
    }

    /// <summary>The one notice for all teachers' assessments: begin them, put them off a week, or skip this assessment period. It only carries the choice; each teacher's schedule and ownership are in the ledger.</summary>
    public class ChoiceLetter_Assessment : ChoiceLetter
    {
        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                var c = FamilyComponent.Current;
                if (c != null && OfficeAuthority.MayDirect(c.Registry, FrameworkServices.AvatarId, OfficeKind.Teacher))
                {
                    yield return new DiaOption("WFL_Event_Begin".Translate()) { action = () => EducationEvents.BeginAll(c, FrameworkServices.Now), resolveTree = true };
                    yield return new DiaOption("WFL_Event_Reschedule".Translate()) { action = () => EducationEvents.PostponeAll(c, FrameworkServices.Now), resolveTree = true };
                    yield return new DiaOption("WFL_Event_Pass".Translate()) { action = () => EducationEvents.SkipAll(c, FrameworkServices.Now), resolveTree = true };
                }
                yield return Option_Close;          // nothing needs answering: the assessments begin by themselves after a day
            }
        }
    }
}
