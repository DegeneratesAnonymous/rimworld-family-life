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

    internal static partial class OfficeEvents
    {
        private const int MaxPupils = 24;

        private static List<int> Children(FamilyComponent c, FamilyRegistry r, int teacherId)
        {
            bool adults = FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.adultStudents;
            return r.Persons.Where(p => p.InColony && p.Id != teacherId && (p.Juvenile || adults)).Select(p => p.Id).Take(MaxPupils).ToList();
        }

        internal static void Teacher(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn teacher, int now)
        {
            if (o.TestingStartTick != 0) { RunTesting(c, r, o, teacher, now); return; }
            if (o.PendingKind == PendingEvent.Testing)
            {
                if (now >= o.PendingTick || o.PendingTick - now > 40 * Day) Begin(PendingEvent.Testing);
                return;
            }
            if (o.PendingKind != 0) return;
            if (IsAprimay(teacher.Map) && o.LastTestingYear != Year(teacher.Map) && Children(c, r, o.HolderId).Count > 0) Plan(PendingEvent.Testing, teacher, now);
        }

        private static bool BeginTesting(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn teacher, int now)
        {
            if (o.TestingStartTick != 0) return false;
            var ids = Children(c, r, o.HolderId);
            if (ids.Count == 0) { Messages.Message("WFL_Event_NoPupils".Translate(), MessageTypeDefOf.RejectInput, false); return false; }
            o.TestingStartTick = now; o.TestingSlot = 0; o.LastTestingYear = Year(teacher.Map);
            o.TestIds = ids; o.TestPass = ids.Select(_ => 0).ToList(); o.TestTry = ids.Select(_ => 0).ToList(); o.TestScore = ids.Select(_ => 0).ToList(); o.TestExcused = ids.Select(_ => 0).ToList();
            Messages.Message("WFL_Testing_Begins".Translate(teacher.LabelShort, ids.Count), teacher, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        private static void EnsureLists(CommunityOffice o)
        {
            while (o.TestPass.Count < o.TestIds.Count) o.TestPass.Add(0);
            while (o.TestTry.Count < o.TestIds.Count) o.TestTry.Add(0);
            while (o.TestScore.Count < o.TestIds.Count) o.TestScore.Add(0);
            while (o.TestExcused.Count < o.TestIds.Count) o.TestExcused.Add(0);
        }

        private static bool SomeoneNeedsAMakeUp(CommunityOffice o)
        {
            for (int i = 0; i < o.TestIds.Count && i < o.TestTry.Count && i < o.TestExcused.Count; i++) if (o.TestTry[i] < 2 && o.TestExcused[i] > 0) return true;
            return false;
        }

        /// <summary>
        /// Two to four sittings over two days, in the daytime only: three planned, and one make-up for anyone who was ill or called away. Nobody is summoned in the night, nobody
        /// is hurried through, and an absence for a good reason is not a failure.
        /// </summary>
        private static void RunTesting(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn teacher, int now)
        {
            if (now < o.TestingStartTick) o.TestingStartTick = now;
            EnsureLists(o);
            int elapsed = now - o.TestingStartTick;
            bool makeUp = SomeoneNeedsAMakeUp(o);
            if (TestingMath.Over(o.TestingSlot, elapsed, makeUp)) { FinishTesting(c, r, o, teacher, now); return; }
            bool due = TestingMath.Due(o.TestingSlot, elapsed) || TestingMath.MakeUpDue(o.TestingSlot, elapsed, makeUp);
            if (!due || !TestingMath.Daytime(GenLocalDate.HourOfDay(teacher))) return;
            if (PawnReadiness.StopReason(teacher, now) != null) return;          // the teacher must be fit to hold it; it waits
            o.TestingSlot++;
            bool room = ClassRooms.TeachingSpot(teacher, out var cell, out var area, out _);
            var seats = room ? ClassRooms.Seats(area, cell, o.TestIds.Count, teacher.Map) : new List<IntVec3>();
            bool makeUpOnly = o.TestingSlot > TestingMath.Offsets.Length;
            for (int i = 0; i < o.TestIds.Count; i++)
            {
                if (makeUpOnly && o.TestTry[i] >= 2) continue;
                var kid = c.TryGetPawn(o.TestIds[i]);
                if (kid == null || !kid.Spawned || kid.Dead) continue;
                string reason = PawnReadiness.StopReason(kid, now);
                if (reason != null || !kid.Awake() || !OfficeDriver.Available(kid)) { o.TestExcused[i]++; continue; }          // ill, hungry, down or asleep: excused, and may sit the make-up
                if (!OfficeDriver.Ready(kid)) continue;          // busy at something else just now: no sitting this time, and no mark
                if (seats.Count > 0)
                {
                    var seat = seats[i % seats.Count];
                    if (!kid.CanReach(seat, PathEndMode.OnCell, Danger.None)) continue;
                    var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_TestStudent, teacher, seat);
                    job.playerForced = true;
                    kid.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                }
                else Sat(kid);
            }
        }

        /// <summary>One completed sitting for this child. Its only effect on the result is a small bounded variation: the result comes from what they studied.</summary>
        internal static void Sat(Pawn kid)
        {
            var c = FamilyComponent.Current; var o = Office(OfficeKind.Teacher); if (c == null || o == null || o.TestingStartTick == 0) return;
            int i = o.TestIds.IndexOf(kid.thingIDNumber); if (i < 0) return;
            EnsureLists(o);
            o.TestTry[i]++;
            o.TestScore[i] += Rand.RangeInclusive(-6, 6);          // hundredths: at most a few points either way, summed over the sittings
            MoteMaker.ThrowText(kid.DrawPos, kid.Map, "WFL_Testing_Sat".Translate(), Color.white);
        }

        private static string Support(string key, Pawn kid) => ("WFL_Testing_Support_" + key).Translate(kid.LabelShort).Resolve();

        private static void FinishTesting(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn teacher, int now)
        {
            var lines = new List<string>(); int judged = 0; float score = 0f;
            EnsureLists(o);
            int teacherLevel = PawnReadiness.Level(teacher, SkillDefOf.Intellectual);
            for (int i = 0; i < o.TestIds.Count; i++)
            {
                var kid = c.TryGetPawn(o.TestIds[i]);
                if (kid == null || kid.Dead || kid.skills == null) continue;
                var recs = LessonRecord.For(o.LessonLog, kid.thingIDNumber).OrderByDescending(x => x.Xp).Take(3).ToList();
                if (recs.Count == 0) { lines.Add("WFL_Testing_LineNothing".Translate(kid.LabelShort)); continue; }          // nothing studied since the last assessment: nothing to assess
                float noise = o.TestTry[i] > 0 ? o.TestScore[i] / 100f / o.TestTry[i] : 0f;
                float support = Math.Min(1f, LessonMath.Delivery(teacherLevel, PawnReadiness.Level(teacher, SkillDefOf.Social), 0f, 0f, 0f) / 1.2f);
                var results = recs.Select(rec => new { rec, res = Assessments.Assess(rec, o.TestTry[i], TestingMath.Offsets.Length, o.TestExcused[i], kid.skills.GetSkill(DefDatabase<SkillDef>.GetNamedSilentFail(rec.Skill) ?? SkillDefOf.Intellectual)?.Level ?? 0, support, noise) }).ToList();
                var main = results[0];
                if (main.res.Completion == Completion.Incomplete)
                {
                    bool excused = o.TestExcused[i] > 0;
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
                    Activities.GainMemory(kid, level >= ResultQuality.Good ? "WFL_TestingProud" : level == ResultQuality.Poor ? "WFL_TestingDisappointed" : "WFL_TestingDone", teacher);
                score += (int)level / 3f; judged++;
            }
            // the period's records for the children assessed are spent; the next assessment looks at what is studied from now on
            var assessed = new HashSet<int>(o.TestIds);
            o.LessonLog = o.LessonLog.Where(x => { var rec = LessonRecord.Parse(x); return rec != null && !assessed.Contains(rec.Pupil); }).ToList();
            if (judged > 0) r.RecordOfficeWork(OfficeKind.Teacher, teacher.thingIDNumber, GovernanceMeetings.NextToken(), now, .4f + .6f * score / judged);
            o.TestingStartTick = 0; o.TestingSlot = 0; o.TestIds = new List<int>(); o.TestPass = new List<int>(); o.TestTry = new List<int>(); o.TestScore = new List<int>(); o.TestExcused = new List<int>();
            if (lines.Count > 0)
                Find.LetterStack.ReceiveLetter("WFL_Testing_ResultTitle".Translate(), "WFL_Testing_ResultText".Translate(teacher.LabelShort, string.Join("\n", lines)), LetterDefOf.NeutralEvent, teacher);
        }

        // ------------------------------------------------------------------ buttons

        /// <summary>Skipping the assessment spends this year's.</summary>
        internal static void PassTesting()
        {
            var c = FamilyComponent.Current; var o = Office(OfficeKind.Teacher); if (o == null || c == null) return;
            var t = c.TryGetPawn(o.HolderId); if (t?.Map != null) o.LastTestingYear = Year(t.Map);
        }

        internal static bool BeginAssessment()
        {
            var c = FamilyComponent.Current; var r = c?.Registry; var o = Office(OfficeKind.Teacher); if (r == null || o == null || !o.Held) return false;
            var holder = c.TryGetPawn(o.HolderId); if (holder == null) return false;
            return BeginTesting(c, r, o, holder, Activities.Now);
        }

        /// <summary>The "Begin annual assessment" button on a lectern.</summary>
        internal static IEnumerable<Gizmo> EducationGizmos(Building b)
        {
            var room = b.GetRoom();
            if (room == null || room.PsychologicallyOutdoors) yield break;
            var reg = FamilyComponent.Current?.Registry; if (reg == null) yield break;
            if (ClassRooms.IsLectern(b.def) && !ClassRooms.IsOtherRoom(room) && Office(OfficeKind.Teacher)?.Held == true && Office(OfficeKind.Teacher).TestingStartTick == 0 && OfficeAuthority.MayDirect(reg, PerspectiveShiftBridge.AvatarId, OfficeKind.Teacher))
                yield return new Command_Action
                {
                    defaultLabel = "WFL_Event_BeginTesting".Translate(),
                    defaultDesc = "WFL_Event_BeginTestingDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("WilsFamilyLife/Roles/teacher", false) ?? BaseContent.BadTex,
                    action = () => { if (OfficeAuthority.MayDirect(reg, PerspectiveShiftBridge.AvatarId, OfficeKind.Teacher)) Begin(PendingEvent.Testing); }
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
            this.FailOn(() => PawnReadiness.StopReason(pawn, Activities.Now) != null || FamilyComponent.Current?.Registry.OfficeFor(OfficeKind.Teacher)?.TestingStartTick == 0);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var sit = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            sit.initAction = () => pawn.pather?.StopDead();
            sit.tickAction = () => { if (++sat >= TestingMath.SitTicks) { OfficeEvents.Sat(pawn); EndJobWith(JobCondition.Succeeded); } };
            yield return sit;
        }
    }
}
