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
    /// <summary>What a player (or the pawn they are playing) is shown of the offices' private business.</summary>
    internal static class OfficeCareView
    {
        /// <summary>The result of someone's check-up is theirs and the doctor's. Someone being played who is neither does not see it floating over a stranger's head.</summary>
        internal static bool MayKnowResult(Pawn patient, Pawn doctor)
        {
            int avatar = PerspectiveShiftBridge.AvatarId;
            return avatar == 0 || avatar == patient.thingIDNumber || avatar == doctor.thingIDNumber;
        }
    }

    /// <summary>
    /// A notice about a planned routine event (a check-up round, the annual assessment). It does not hold anything up: the event begins by itself at the planned time. The player
    /// may begin it now, put it off for a week, or skip it this time, if they are allowed to direct that post.
    /// </summary>
    public class ChoiceLetter_OfficeEvent : ChoiceLetter
    {
        public int kind;

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                var c = FamilyComponent.Current;
                var post = kind == PendingEvent.Testing ? OfficeKind.Teacher : OfficeKind.Healer;
                if (c != null && OfficeAuthority.MayDirect(c.Registry, PerspectiveShiftBridge.AvatarId, post))
                {
                    yield return new DiaOption("WFL_Event_Begin".Translate()) { action = () => { OfficeEvents.Begin(kind); Find.LetterStack.RemoveLetter(this); }, resolveTree = true };
                    yield return new DiaOption("WFL_Event_Reschedule".Translate()) { action = () => { OfficeEvents.Reschedule(kind); Find.LetterStack.RemoveLetter(this); }, resolveTree = true };
                    yield return new DiaOption("WFL_Event_Pass".Translate()) { action = () => { OfficeEvents.Pass(kind); Find.LetterStack.RemoveLetter(this); }, resolveTree = true };
                }
                yield return Option_Close;          // nothing needs answering: it goes ahead by itself
            }
        }

        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref kind, "wflEventKind"); }
    }

    /// <summary>
    /// The check-up round, the round called for an outbreak and the annual assessment. Routine ones are planned a day ahead and begin by themselves; an outbreak round begins at
    /// once (and never waits for anyone to answer anything). The state is kept on the post, so it is saved.
    /// </summary>
    internal static class OfficeEvents
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private const int PlanLead = Day, RescheduleBy = 7 * Day, IllnessGap = 10 * Day, MaxPupils = 24;
        private static LetterDef letterDef;
        private static LetterDef LetterKind => letterDef ?? (letterDef = DefDatabase<LetterDef>.GetNamedSilentFail("WFL_OfficeEvent") ?? LetterDefOf.NeutralEvent);

        // ------------------------------------------------------------------ reading the colony

        internal static float Longitude(Map m) => Find.WorldGrid.LongLatOf(m.Tile).x;
        internal static bool IsAprimay(Map m) => GenDate.Quadrum(Find.TickManager.TicksAbs, Longitude(m)) == Quadrum.Aprimay;
        internal static int Year(Map m) => GenDate.Year(Find.TickManager.TicksAbs, Longitude(m));

        /// <summary>Down with an illness (not an injury): something that gives them the sick thought.</summary>
        internal static bool IsSick(Pawn p) => p?.health?.hediffSet?.hediffs != null && p.health.hediffSet.hediffs.Any(h => h.def.makesSickThought && h.Visible);

        /// <summary>A quarter of the colony, and at least three, ill at once: an outbreak or a plague.</summary>
        internal static bool MassIllness(Map map)
        {
            if (map == null) return false;
            var people = map.mapPawns.FreeColonistsSpawned;
            int sick = people.Count(IsSick);
            return sick >= Math.Max(3, (int)Math.Ceiling(people.Count * .25));
        }

        private static int Interval()
        {
            var s = FamilyLifeMod.Settings;
            return 60 * Day / Math.Max(1, Math.Min(4, s?.checkupsPerYear ?? 2));
        }

        private static CommunityOffice Office(OfficeKind kind) => FamilyComponent.Current?.Registry.OfficeFor(kind);
        private static CommunityOffice OfficeFor(int eventKind) => Office(eventKind == PendingEvent.Testing ? OfficeKind.Teacher : OfficeKind.Healer);
        private static OfficeKind PostOf(int eventKind) => eventKind == PendingEvent.Testing ? OfficeKind.Teacher : OfficeKind.Healer;

        /// <summary>In an outbreak the sick are seen first.</summary>
        internal static Func<int, int> Priority(FamilyComponent c, CommunityOffice o) => o != null && o.IllnessRound ? (id => IsSick(c.TryGetPawn(id)) ? 0 : 1) : (Func<int, int>)null;

        private static int sickCheckedAt = -100000; private static bool sickRemaining;
        /// <summary>In an outbreak assessment the sick come before the rest: a well person is not seen while someone ill is still waiting. In any other round, everyone is on the list.</summary>
        internal static bool SickFirst(FamilyComponent c, FamilyRegistry r, CommunityOffice o, int id)
        {
            if (o == null || !o.IllnessRound || IsSick(c.TryGetPawn(id))) return true;
            int now = Activities.Now;
            if (now - sickCheckedAt > 600 || now < sickCheckedAt)
            {
                sickCheckedAt = now;
                sickRemaining = r.Persons.Any(p => p.InColony && p.Id != o.HolderId && !o.Examined.Contains(p.Id) && IsSick(c.TryGetPawn(p.Id)));
            }
            return !sickRemaining;
        }

        // ------------------------------------------------------------------ planning

        /// <summary>Plans the event for tomorrow and says so. Nothing waits on an answer.</summary>
        private static void Plan(int kind, Pawn holder, int now)
        {
            var o = OfficeFor(kind); if (o == null || !o.Held) return;
            o.PendingKind = kind; o.PendingTick = now + PlanLead;
            string key = kind == PendingEvent.Testing ? "Testing" : "Checkups";
            var letter = (ChoiceLetter_OfficeEvent)LetterMaker.MakeLetter(("WFL_Event_" + key + "_Title").Translate(), ("WFL_Event_" + key + "_Text").Translate(holder.LabelShort), LetterKind, holder);
            letter.kind = kind;
            Find.LetterStack.ReceiveLetter(letter);
        }

        internal static void Plan(int kind) { var c = FamilyComponent.Current; var o = OfficeFor(kind); var p = o != null && o.Held ? c?.TryGetPawn(o.HolderId) : null; if (p != null && p.Spawned) Plan(kind, p, Activities.Now); }

        private static void DropNotices(int kind)
        {
            foreach (var l in Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_OfficeEvent>().Where(x => x.kind == kind).ToList()) Find.LetterStack.RemoveLetter(l);
        }

        // ------------------------------------------------------------------ beginning, putting off and skipping

        internal static bool Begin(int kind)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; if (r == null) return false;
            int now = Activities.Now;
            var o = OfficeFor(kind);
            if (o == null || !o.Held) { Messages.Message(("WFL_Event_NoPost_" + (kind == PendingEvent.Testing ? "Teacher" : "Doctor")).Translate(), MessageTypeDefOf.RejectInput, false); return false; }
            var holder = c.TryGetPawn(o.HolderId);
            if (holder == null || !holder.Spawned) { o.PendingKind = 0; return false; }
            o.PendingKind = 0;
            DropNotices(kind);
            if (kind == PendingEvent.Testing) return BeginTesting(c, r, o, holder, now);
            if (kind == PendingEvent.Illness)
            {
                o.IllnessRound = true; o.LastIllnessTick = now;
                if (r.CheckupOpen(now))
                {
                    foreach (var id in o.Examined.ToList()) if (IsSick(c.TryGetPawn(id))) o.Examined.Remove(id);          // the sick are to be looked at again
                    o.RoundStartTick = now;
                    o.RoundWindow = CheckupWorkload.WindowDays(r.Persons.Count(p => p.InColony && p.Id != o.HolderId), true) * Day;
                }
                else { o.NextRoundTick = 0; r.BeginCheckupRound(now, Interval()); }
            }
            else if (!r.CheckupOpen(now)) { o.NextRoundTick = 0; r.BeginCheckupRound(now, Interval()); }
            AppointmentBook.Reset();
            Messages.Message((kind == PendingEvent.Illness ? "WFL_Checkup_Illness" : "WFL_Checkup_Round").Translate(holder.LabelShort), holder, MessageTypeDefOf.NeutralEvent, false);
            return true;
        }

        /// <summary>Put it off for a week. Checked again when it runs: only someone who may direct the post can.</summary>
        internal static void Reschedule(int kind)
        {
            var c = FamilyComponent.Current; var o = OfficeFor(kind); if (o == null || c == null) return;
            if (!OfficeAuthority.MayDirect(c.Registry, PerspectiveShiftBridge.AvatarId, PostOf(kind))) return;
            if (o.PendingKind == kind) o.PendingTick += RescheduleBy;
            DropNotices(kind);
        }

        internal static void Pass(int kind)
        {
            var o = OfficeFor(kind); var c = FamilyComponent.Current; if (o == null || c == null) return;
            if (!OfficeAuthority.MayDirect(c.Registry, PerspectiveShiftBridge.AvatarId, PostOf(kind))) return;
            int now = Activities.Now;
            o.PendingKind = 0;
            if (kind == PendingEvent.Testing) { var t = c.TryGetPawn(o.HolderId); if (t?.Map != null) o.LastTestingYear = Year(t.Map); }
            else if (kind == PendingEvent.Illness) o.LastIllnessTick = now - 5 * Day;
            else o.NextRoundTick = now + Interval() / 2;
            DropNotices(kind);
        }

        // ------------------------------------------------------------------ the doctor

        internal static void Healer(FamilyComponent c, FamilyRegistry r, CommunityOffice o, Pawn doctor, int now)
        {
            if (!r.CheckupOpen(now)) o.IllnessRound = false;
            if (o.PendingKind == PendingEvent.Checkups || o.PendingKind == PendingEvent.Illness)
            {
                if (now >= o.PendingTick || o.PendingTick - now > 40 * Day) Begin(o.PendingKind);          // the planned time has come (a saved plan from before simply begins)
                return;
            }
            if (o.PendingKind != 0) return;
            // An outbreak: the doctor changes priorities at once (the sick first, tending always before this) and the player is told. Nothing waits for an answer.
            if (!o.IllnessRound && now - o.LastIllnessTick > IllnessGap && MassIllness(doctor.Map)) { Begin(PendingEvent.Illness); return; }
            if (!r.CheckupOpen(now) && now >= o.NextRoundTick) Plan(PendingEvent.Checkups, doctor, now);
        }

        // ------------------------------------------------------------------ the teacher and the annual assessment

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
            if (CareDemand.StopReason(teacher, now) != null) return;          // the teacher must be fit to hold it; it waits
            o.TestingSlot++;
            bool room = ClassRooms.TeachingSpot(teacher, out var cell, out var area, out _);
            var seats = room ? ClassRooms.Seats(area, cell, o.TestIds.Count, teacher.Map) : new List<IntVec3>();
            bool makeUpOnly = o.TestingSlot > TestingMath.Offsets.Length;
            for (int i = 0; i < o.TestIds.Count; i++)
            {
                if (makeUpOnly && o.TestTry[i] >= 2) continue;
                var kid = c.TryGetPawn(o.TestIds[i]);
                if (kid == null || !kid.Spawned || kid.Dead) continue;
                string reason = CareDemand.StopReason(kid, now);
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
            int teacherLevel = CareRules.Level(teacher, SkillDefOf.Intellectual);
            for (int i = 0; i < o.TestIds.Count; i++)
            {
                var kid = c.TryGetPawn(o.TestIds[i]);
                if (kid == null || kid.Dead || kid.skills == null) continue;
                var recs = LessonRecord.For(o.LessonLog, kid.thingIDNumber).OrderByDescending(x => x.Xp).Take(3).ToList();
                if (recs.Count == 0) { lines.Add("WFL_Testing_LineNothing".Translate(kid.LabelShort)); continue; }          // nothing studied since the last assessment: nothing to assess
                float noise = o.TestTry[i] > 0 ? o.TestScore[i] / 100f / o.TestTry[i] : 0f;
                float support = Math.Min(1f, LessonMath.Delivery(teacherLevel, CareRules.Level(teacher, SkillDefOf.Social), 0f, 0f, 0f) / 1.2f);
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

        /// <summary>The player can begin an event whenever they like, from a medical bed and from a lectern, if they are allowed to direct that post.</summary>
        internal static IEnumerable<Gizmo> Gizmos(Building b)
        {
            if (b == null || !b.Spawned || b.Faction != Faction.OfPlayer) yield break;
            var room = b.GetRoom();
            if (room == null || room.PsychologicallyOutdoors) yield break;
            var reg = FamilyComponent.Current?.Registry; if (reg == null) yield break;
            int actor = PerspectiveShiftBridge.AvatarId;
            if (b is Building_Bed bed && (bed.Medical || room.Role == RoomRoleDefOf.Hospital) && Office(OfficeKind.Healer)?.Held == true && OfficeAuthority.MayDirect(reg, actor, OfficeKind.Healer))
            {
                bool outbreak = MassIllness(b.Map);
                yield return new Command_Action
                {
                    defaultLabel = (outbreak ? "WFL_Event_BeginIllness" : "WFL_Event_BeginCheckups").Translate(),
                    defaultDesc = "WFL_Event_BeginCheckupsDesc".Translate(),
                    icon = ContentFinder<Texture2D>.Get("WilsFamilyLife/Roles/healer", false) ?? BaseContent.BadTex,
                    action = () => { if (OfficeAuthority.MayDirect(reg, PerspectiveShiftBridge.AvatarId, OfficeKind.Healer)) Begin(outbreak ? PendingEvent.Illness : PendingEvent.Checkups); }
                };
            }
            if (ClassRooms.IsLectern(b.def) && !ClassRooms.IsOtherRoom(room) && Office(OfficeKind.Teacher)?.Held == true && Office(OfficeKind.Teacher).TestingStartTick == 0 && OfficeAuthority.MayDirect(reg, actor, OfficeKind.Teacher))
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
            this.FailOn(() => CareDemand.StopReason(pawn, Activities.Now) != null || FamilyComponent.Current?.Registry.OfficeFor(OfficeKind.Teacher)?.TestingStartTick == 0);
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var sit = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            sit.initAction = () => pawn.pather?.StopDead();
            sit.tickAction = () => { if (++sat >= TestingMath.SitTicks) { OfficeEvents.Sat(pawn); EndJobWith(JobCondition.Succeeded); } };
            yield return sit;
        }
    }
}
