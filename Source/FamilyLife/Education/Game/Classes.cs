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
    // The teacher's class. It is one subject, taught in four blocks of instruction with short breaks between (about 3.8 game hours in all). Pupils can leave for any real need
    // and are not called back until it is dealt with; a teacher who stops releases everyone. What a pupil takes away is worked out once, at the end, from how long they really
    // sat through the instruction. Nothing here is saved: a class under way when the game is loaded simply does not continue, and the office starts another when it is next due.

    internal sealed class ClassSession
    {
        public int TeacherId, StartTick, StudyingTick, BookId;
        public string Subject = "Intellectual";
        public float Delivery = 1f;
        public IntVec3 TeacherCell, FaceCell;
        public HashSet<IntVec3> RoomCells = new HashSet<IntVec3>();
        public List<IntVec3> Seats = new List<IntVec3>();
        public readonly Dictionary<int, int> Attended = new Dictionary<int, int>(), Excused = new Dictionary<int, int>(), Left = new Dictionary<int, int>(), Calls = new Dictionary<int, int>(), Exchanged = new Dictionary<int, int>();
        public readonly Dictionary<int, float> Tiers = new Dictionary<int, float>();
        public bool Teaching, Finished, ByPlayer;
        public int CallBlock = -1, Exchanges;
        public string EndReason;
        public List<int> Roster = new List<int>();
    }

    internal enum BeginResult : byte { Started, Later, Cannot }

    internal static class ClassSessions
    {
        private static readonly Dictionary<int, ClassSession> byTeacher = new Dictionary<int, ClassSession>();
        private static readonly Dictionary<int, int[]> backoff = new Dictionary<int, int[]>();          // teacher -> [failed tries, next try]

        internal static void Reset() { byTeacher.Clear(); backoff.Clear(); }
        internal static ClassSession Of(int teacherId) => byTeacher.TryGetValue(teacherId, out var s) ? s : null;
        internal static bool Running(Pawn teacher) => teacher != null && byTeacher.ContainsKey(teacher.thingIDNumber);

        /// <summary>Repeated failures to get a class going wait longer each time (ten seconds, thirty, ninety, then every few game hours), so nothing loops.</summary>
        internal static void ClearBackoff() => backoff.Clear();
        internal static bool MayTry(Pawn teacher, int now) => OfficeDriver.Forced || !backoff.TryGetValue(teacher.thingIDNumber, out var b) || now >= b[1] || now < b[1] - 200000;
        private static void Fail(Pawn teacher, int now)
        {
            backoff.TryGetValue(teacher.thingIDNumber, out var b); int fails = (b?[0] ?? 0) + 1;
            backoff[teacher.thingIDNumber] = new[] { fails, now + Math.Min(7500, 600 * (int)Math.Pow(3, Math.Min(fails - 1, 3))) };
        }

        private static PupilSnapshot Snapshot(FamilyComponent c, Pawn kid)
        {
            var snap = new PupilSnapshot { Id = kid.thingIDNumber, Age = kid.ageTracker?.AgeBiologicalYears ?? 12 };
            if (kid.skills != null)
                foreach (var sk in kid.skills.skills)
                {
                    snap.Levels[sk.def.defName] = sk.Level;
                    if (sk.TotallyDisabled) snap.Disabled.Add(sk.def.defName);
                    if (sk.passion != Passion.None) snap.Passions.Add(sk.def.defName);
                }
            try
            {
                var r = c.Registry; var house = r.GetLineage(r.FamilyOf(kid.thingIDNumber));
                var amb = house?.Ambition;
                if (amb != null && (amb.Primary == HouseAmbitionKind.FillSkillGap || amb.Secondary == HouseAmbitionKind.FillSkillGap)) { var skill = SkillOf(amb.TargetCapability); if (skill != null) snap.HouseNeeds.Add(skill); }
            }
            catch (Exception) { }
            return snap;
        }

        internal static string SkillOf(HouseCapability cap)
        {
            switch (cap)
            {
                case HouseCapability.Medical: return "Medicine";
                case HouseCapability.Cooking: return "Cooking";
                case HouseCapability.Growing: return "Plants";
                case HouseCapability.Construction: return "Construction";
                case HouseCapability.Crafting: return "Crafting";
                case HouseCapability.Intellectual: return "Intellectual";
                case HouseCapability.Social: return "Social";
                case HouseCapability.Animals: return "Animals";
                default: return null;
            }
        }

        /// <summary>The skill the book would teach this class the most of, for choosing the subject: the book is only a reason to pick a subject the teacher can explain.</summary>
        internal static string BookSkill(Thing book, IEnumerable<Pawn> pupils) => BookValues(book).OrderByDescending(kv => kv.Value).Select(kv => kv.Key.defName).FirstOrDefault();

        internal static Dictionary<SkillDef, float> BookValues(Thing book)
        {
            var d = new Dictionary<SkillDef, float>();
            var comp = (book as ThingWithComps)?.GetComp<CompBook>();
            if (comp == null) return d;
            foreach (var doer in comp.Doers.OfType<BookOutcomeDoerGainSkillExp>())
                foreach (var pair in doer.Values) d[pair.Key] = (d.TryGetValue(pair.Key, out var v) ? v : 0f) + pair.Value;
            return d;
        }

        /// <summary>Starts a class: the teacher goes to the front of the classroom (fetching a book first if one fits) and calls the children when they arrive.</summary>
        internal static BeginResult TryBegin(FamilyComponent c, Pawn teacher, List<int> roster, int now, Building lectern = null, bool byPlayer = false)
        {
            if (teacher == null || Running(teacher) || roster.Count == 0) return BeginResult.Cannot;
            IntVec3 cell = IntVec3.Invalid; Room room = null; Thing front = lectern;
            bool found = lectern != null ? ClassRooms.SpotAt(lectern, teacher, out cell, out room) : ClassRooms.TeachingSpot(teacher, out cell, out room, out front);
            if (!found) { OfficeDriver.Trace("class: nowhere to hold a class for " + teacher.LabelShort); return BeginResult.Cannot; }
            string stop = PawnReadiness.StopReason(teacher, now);
            if (byPlayer) { if (!OfficeDriver.Available(teacher) || stop != null) { OfficeDriver.Trace("class: " + teacher.LabelShort + " cannot start yet (" + (stop ?? "not available") + ")"); return BeginResult.Later; } }
            else if (!OfficeDriver.Ready(teacher) || stop != null || teacher.CurJob?.playerForced == true || (Find.Selector.IsSelected(teacher) && !OfficeDriver.Forced) || PerspectiveShiftBridge.IsAvatar(teacher))
            {
                OfficeDriver.Trace("class: " + teacher.LabelShort + " cannot start yet (" + (stop ?? (!OfficeDriver.Ready(teacher) ? "not free" : teacher.CurJob?.playerForced == true ? "following an order" : Find.Selector.IsSelected(teacher) ? "selected" : "being played")) + ")");
                Fail(teacher, now); return BeginResult.Later;
            }
            var pupils = roster.Select(id => c.TryGetPawn(id)).Where(p => p != null && p.Spawned).ToList();
            var book = ClassRooms.BestBook(teacher, pupils);
            var office = c.Registry.OfficeFor(OfficeKind.Teacher);
            var teacherLevels = new Dictionary<string, int>(); var disabled = new HashSet<string>();
            if (teacher.skills != null) foreach (var sk in teacher.skills.skills) { teacherLevels[sk.def.defName] = sk.Level; if (sk.TotallyDisabled) disabled.Add(sk.def.defName); }
            string subject = LessonSubjects.Choose(teacherLevels, disabled, pupils.Select(p => Snapshot(c, p)).ToList(), book == null ? null : BookSkill(book, pupils), office?.Education().RecentSubjects, teacher.thingIDNumber + now / 60000);
            if (subject == null) { OfficeDriver.Trace("class: " + teacher.LabelShort + " has nothing the children could learn from them"); return BeginResult.Cannot; }
            var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(subject);
            if (book != null && !(BookValues(book).Keys.Any(k => k.defName == subject))) book = null;          // a book that is not about this subject is left on the shelf
            float delivery = LessonMath.Delivery(PawnReadiness.Level(teacher, skillDef), PawnReadiness.Level(teacher, SkillDefOf.Social), 0f, ClassRooms.Facility(room, front), PawnReadiness.Fatigue(teacher));
            var s = new ClassSession { TeacherId = teacher.thingIDNumber, StartTick = now, TeacherCell = cell, Roster = roster.ToList(), BookId = book?.thingIDNumber ?? 0, ByPlayer = byPlayer, Subject = subject, Delivery = delivery };
            s.Seats = ClassRooms.Seats(room, cell, roster.Count, teacher.Map);
            if (room != null) foreach (var rc in room.Cells) s.RoomCells.Add(rc);
            else foreach (var rc in GenRadial.RadialCellsAround(cell, 8f, true)) s.RoomCells.Add(rc);
            s.FaceCell = s.Seats.Count > 0 ? s.Seats[s.Seats.Count / 2] : cell;
            OfficeDriver.Trace("class: " + teacher.LabelShort + " teaches " + subject + " at " + cell + (front != null ? " in front of " + front.LabelCap : room == null ? " in the open" : " in a room with seats") + ", delivery " + delivery.ToString("0.00") + ", " + s.Seats.Count + " seats, book: " + (book?.LabelNoCount ?? "none") + ", " + roster.Count + " children on the list");
            byTeacher[teacher.thingIDNumber] = s;
            backoff.Remove(teacher.thingIDNumber);
            var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_ClassTeacher, front != null ? (LocalTargetInfo)front : (LocalTargetInfo)cell, cell);
            if (book != null) job.targetC = book;
            job.playerForced = true;
            teacher.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            return BeginResult.Started;
        }

        /// <summary>
        /// Asks the children in. A child is not asked while the class is on a break, while they have left and are due back later, or more than three times in one block; and not at all
        /// while they have something that matters more to see to.
        /// </summary>
        internal static void CallStudents(FamilyComponent c, ClassSession s, Pawn teacher, int now, int block)
        {
            if (s.CallBlock != block) { s.CallBlock = block; s.Calls.Clear(); }
            int i = 0;
            foreach (int id in s.Roster)
            {
                int seatIndex = i++;
                var kid = c.TryGetPawn(id);
                if (kid == null || !kid.Spawned || kid.Map != teacher.Map || kid.CurJobDef == WFL_StoryDefOf.WFL_ClassStudent) continue;
                if (s.Left.TryGetValue(id, out int back) && now < back) continue;
                if (s.Calls.TryGetValue(id, out int n) && n >= 3) continue;
                bool able = s.ByPlayer ? OfficeDriver.Available(kid) : OfficeDriver.Ready(kid) && OfficeDriver.FreeFor(c, id) && !(kid.CurJob?.playerForced == true && !OfficeDriver.Forced);
                if (!able || PerspectiveShiftBridge.IsAvatar(kid) || PawnReadiness.StopReason(kid, now) != null) continue;          // the pawn being played is never sent to class
                var seat = s.Seats.Count > 0 ? s.Seats[seatIndex % s.Seats.Count] : s.TeacherCell;
                if (!kid.CanReach(seat, PathEndMode.OnCell, Danger.None)) { s.Calls[id] = 3; continue; }
                var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_ClassStudent, teacher, seat);
                job.playerForced = true;
                kid.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                s.Calls[id] = n + 1;
                if (!s.Attended.ContainsKey(id)) s.Attended[id] = 0;
                OfficeDriver.Trace("class: " + kid.LabelShort + " is called to " + seat + (kid.CurJobDef == WFL_StoryDefOf.WFL_ClassStudent ? "" : " but could not take the job"));
            }
        }

        /// <summary>The class is over for whatever reason: the pupils are released by their own jobs ending, and what each learned is worked out once.</summary>
        internal static void End(Pawn teacher, bool finishedNormally, string reason = null)
        {
            if (teacher == null || !byTeacher.TryGetValue(teacher.thingIDNumber, out var s)) return;
            byTeacher.Remove(teacher.thingIDNumber);
            s.Finished = true; s.EndReason = reason;
            var c = FamilyComponent.Current; var r = c?.Registry;
            if (r == null) return;
            int now = Activities.Now;
            int learned = 0;
            try
            {
                int elapsed = Math.Max(0, now - s.StudyingTick);
                int possibleBase = s.Teaching ? ClassSchedule.InstructionDone(elapsed) : 0;
                var skill = DefDatabase<SkillDef>.GetNamedSilentFail(s.Subject);
                var book = s.BookId == 0 ? null : FindBook(teacher, s.BookId);
                var office = r.OfficeFor(OfficeKind.Teacher);
                float gapSum = 0f;
                if (skill != null && possibleBase > 0)
                    foreach (int id in s.Roster)
                    {
                        var kid = c.TryGetPawn(id);
                        if (kid == null || kid.Dead || kid.skills == null) continue;
                        s.Attended.TryGetValue(id, out int attended); s.Excused.TryGetValue(id, out int excused);
                        int possible = Math.Max(0, possibleBase - excused);
                        if (possible <= 0) continue;
                        if (attended < LessonMath.MinShare * possible || attended < 600)
                        {
                            if (excused < possibleBase) LessonRecord.Add(office?.Education().LessonLog, LessonRecord.Absent(id, s.Subject, possible));          // they could have come and did not: it counts for the record, not for experience
                            continue;
                        }
                        s.Tiers.TryGetValue(id, out float tiers); s.Exchanged.TryGetValue(id, out int ex);
                        var grant = LessonGrant.Apply(c, r, teacher, kid, skill, attended / (float)ClassSchedule.InstructionTicks, attended, possible, s.Delivery, tiers, ex, book, now);
                        gapSum += grant.Gap; learned++;
                        if (ex >= 2) { if (tiers >= 2f) Activities.GainMemory(kid, "WFL_InspiringLesson", teacher); else if (tiers <= -2f) Activities.GainMemory(kid, "WFL_BoringLesson", teacher); }
                    }
                if (office != null) { office.Education().RecentSubjects.Add(s.Subject); while (office.Education().RecentSubjects.Count > 6) office.Education().RecentSubjects.RemoveAt(0); }
                if (learned > 0)
                {
                    r.RecordOfficeWork(OfficeKind.Teacher, teacher.thingIDNumber, GovernanceMeetings.NextToken(), now, Math.Min(1f, .35f + .45f * Math.Min(1f, s.Delivery / 1.2f) + .2f * gapSum / learned));
                    Messages.Message((book != null ? "WFL_Class_EndedBook" : "WFL_Class_Ended").Translate(teacher.LabelShort, learned, book?.LabelNoCount ?? "", (skill?.skillLabel ?? s.Subject)), teacher, MessageTypeDefOf.PositiveEvent, false);
                }
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] The class could not be settled: " + ex.Message); }
            foreach (int id in s.Roster) r.ClassDone(id);
        }

        /// <summary>The teacher says something to one pupil in the room, favouring whoever has had least attention: a question, an explanation, a quiz, a story, a word to settle them.</summary>
        internal static void Exchange(ClassSession s, Pawn teacher, int elapsed)
        {
            var c = FamilyComponent.Current; if (c == null) return;
            var present = s.Roster.Select(id => c.TryGetPawn(id)).Where(p => p != null && p.Spawned && p.CurJobDef == WFL_StoryDefOf.WFL_ClassStudent && s.RoomCells.Contains(p.Position)).ToList();
            if (present.Count == 0) return;
            int fewest = present.Min(p => s.Exchanged.TryGetValue(p.thingIDNumber, out int n) ? n : 0);
            var pool = present.Where(p => (s.Exchanged.TryGetValue(p.thingIDNumber, out int n) ? n : 0) == fewest).ToList();
            var pupil = pool[Rand.Range(0, pool.Count)];
            var topic = GuidedExchanges.PickTopic(GuidedExchanges.ClassTopics, s.Exchanges++, teacher.thingIDNumber);
            var skill = DefDatabase<SkillDef>.GetNamedSilentFail(s.Subject) ?? SkillDefOf.Intellectual;
            var res = Conversations.Run(teacher, pupil, OfficeKind.Teacher, skill, topic, teacher.thingIDNumber + elapsed);
            s.Tiers[pupil.thingIDNumber] = (s.Tiers.TryGetValue(pupil.thingIDNumber, out var old) ? old : 0f) + res.Quality;
            s.Exchanged[pupil.thingIDNumber] = (s.Exchanged.TryGetValue(pupil.thingIDNumber, out int e) ? e : 0) + 1;
        }

        private static Thing FindBook(Pawn teacher, int id)
        {
            if (teacher.carryTracker?.CarriedThing?.thingIDNumber == id) return teacher.carryTracker.CarriedThing;
            return teacher.Map?.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).FirstOrDefault(t => t.thingIDNumber == id);
        }
    }

    internal struct GrantResult { public float Xp, BookXp, Gap; }

    /// <summary>
    /// The one place lesson experience is given. It is called once for each pupil when a class ends (or a one-to-one lesson ends), with the time they really sat through, and nothing
    /// else gives lesson or book experience. The experience is given with direct = true, the same way a book gives the experience it carries when a colonist reads it, so the game's
    /// own learning-speed multipliers are not applied a second time on top of ours.
    /// </summary>
    internal static class LessonGrant
    {
        internal static GrantResult Apply(FamilyComponent c, FamilyRegistry r, Pawn teacher, Pawn pupil, SkillDef skill, float xpShare, int attended, int possible, float delivery, float tierSum, int exchanges, Thing book, int now)
        {
            var rec = new GrantResult();
            var tsk = teacher.skills?.GetSkill(skill); var psk = pupil.skills?.GetSkill(skill);
            if (tsk == null || psk == null || psk.TotallyDisabled) return rec;
            float gap = LessonSubjects.GapFactor(tsk.Level, psk.Level);
            float engage = LessonMath.EngagementFactor(tierSum);
            int age = pupil.ageTracker?.AgeBiologicalYears ?? 12;
            float xp = LessonMath.Xp(xpShare, delivery, engage, gap, age);
            if (xp > 0f) psk.Learn(xp, true);
            float bookXp = 0f;
            var office = r.OfficeFor(OfficeKind.Teacher);
            if (book != null && office != null)
            {
                var values = ClassSessions.BookValues(book);
                if (values.TryGetValue(skill, out float value) && LessonMath.BookReady(office.Education().BookUse, pupil.thingIDNumber, book.thingIDNumber, now))
                {
                    bookXp = LessonMath.BookXp(value, xpShare, gap);
                    if (bookXp > 0f) { psk.Learn(bookXp, true); office.Education().BookUse.Add(pupil.thingIDNumber + "|" + book.thingIDNumber + "|" + now); while (office.Education().BookUse.Count > 64) office.Education().BookUse.RemoveAt(0); }
                }
            }
            LessonRecord.Add(office?.Education().LessonLog, new LessonRecord { Pupil = pupil.thingIDNumber, Skill = skill.defName, Xp = xp + bookXp, Effect = delivery * engage * attended, EngageSum = tierSum, Ticks = attended, Possible = possible, Exchanges = exchanges, Classes = 1 });
            r.RecordOfficeWork(OfficeKind.Teacher, teacher.thingIDNumber, GovernanceMeetings.NextToken(), now, .4f);
            OfficeDriver.Served(r, OfficeKind.Teacher, teacher, pupil, now);
            // a lesson in the skill the pupil's house is trying to build moves that ambition a very little
            try
            {
                var amb = r.GetLineage(r.FamilyOf(pupil.thingIDNumber))?.Ambition;
                if (amb != null && amb.Primary == HouseAmbitionKind.FillSkillGap && ClassSessions.SkillOf(amb.TargetCapability) == skill.defName) amb.Progress = Math.Min(1f, amb.Progress + .01f * Math.Min(1f, xpShare * 2f));
            }
            catch (Exception) { }
            rec.Xp = xp; rec.BookXp = bookXp; rec.Gap = gap;
            return rec;
        }
    }

    public class JobDriver_ClassTeacher : JobDriver
    {
        private int sinceCall;
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            !job.targetC.HasThing || pawn.Reserve(job.targetC, job, 1, 1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => PawnReadiness.StopReason(pawn, Activities.Now) != null || ClassSessions.Of(pawn.thingIDNumber) == null || !FamilyLifeMod.Settings.holdClasses);
            AddFinishAction(cond =>
            {
                string why = PawnReadiness.StopReason(pawn, Activities.Now);
                OfficeDriver.Trace("class: " + pawn.LabelShort + " (teacher) finished (" + cond + "), reason: " + (why ?? "none") + ", session " + (ClassSessions.Of(pawn.thingIDNumber) != null));
                if (pawn.carryTracker?.CarriedThing != null && pawn.Spawned) pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                ClassSessions.End(pawn, cond == JobCondition.Succeeded, why ?? cond.ToString());
            });
            if (job.targetC.HasThing)
            {
                yield return Toils_Goto.GotoThing(TargetIndex.C, PathEndMode.ClosestTouch).FailOnDespawnedOrNull(TargetIndex.C);
                yield return Toils_Haul.StartCarryThing(TargetIndex.C);
            }
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var teach = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            teach.initAction = () =>
            {
                var s = ClassSessions.Of(pawn.thingIDNumber); if (s == null) return;
                s.Teaching = true; s.StudyingTick = Activities.Now;
                var c = FamilyComponent.Current; if (c != null) ClassSessions.CallStudents(c, s, pawn, s.StudyingTick, 0);
                pawn.rotationTracker?.FaceCell(s.FaceCell);
            };
            teach.tickAction = () =>
            {
                var s = ClassSessions.Of(pawn.thingIDNumber); if (s == null) return;
                int now = Activities.Now, elapsed = now - s.StudyingTick;
                if (ClassSchedule.Over(elapsed)) { EndJobWith(JobCondition.Succeeded); return; }
                bool onBreak = ClassSchedule.InBreak(elapsed);
                if (pawn.Position != s.TeacherCell && pawn.pather != null && !pawn.pather.Moving && !onBreak) pawn.pather.StartPath(s.TeacherCell, PathEndMode.OnCell);
                if (pawn.IsHashIntervalTick(60))
                {
                    var c = FamilyComponent.Current;
                    if (c != null && !onBreak) TrackAbsences(c, s, now);
                    if (elapsed > 3000 && s.Attended.Values.All(v => v == 0)) { EndJobWith(JobCondition.Incompletable); return; }          // nobody came
                }
                if (onBreak) return;
                int inBlock = elapsed % (ClassSchedule.BlockTicks + ClassSchedule.BreakTicks);
                if (inBlock == ClassSchedule.BlockTicks / 2) ClassSessions.Exchange(s, pawn, elapsed);          // one word with someone in each block, at the middle of it
                if (++sinceCall >= 300 || inBlock < 2) { sinceCall = 0; var c = FamilyComponent.Current; if (c != null) ClassSessions.CallStudents(c, s, pawn, now, ClassSchedule.BlockOf(elapsed)); }          // late arrivals and those who have been seen to are called in
            };
            yield return teach;
        }

        /// <summary>A pupil who is not in the class during instruction for a reason that is nobody's fault (a need, illness, danger, asleep, called away) is excused for that time.</summary>
        private static void TrackAbsences(FamilyComponent c, ClassSession s, int now)
        {
            foreach (int id in s.Roster)
            {
                var kid = c.TryGetPawn(id);
                if (kid != null && kid.Spawned && kid.CurJobDef == WFL_StoryDefOf.WFL_ClassStudent) continue;
                bool excuse = kid == null || !kid.Spawned || kid.Dead || !kid.Awake() || PawnReadiness.StopReason(kid, now) != null || (s.Left.TryGetValue(id, out int back) && now < back);
                if (excuse) s.Excused[id] = (s.Excused.TryGetValue(id, out int e) ? e : 0) + 60;
            }
        }
    }

    public class JobDriver_ClassStudent : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        private Pawn Teacher => job.targetA.Thing as Pawn;
        private bool TeacherGone() => Teacher == null || Teacher.Dead || Teacher.Destroyed || !Teacher.Spawned || Teacher.Downed || Teacher.CurJobDef != WFL_StoryDefOf.WFL_ClassTeacher || ClassSessions.Of(Teacher.thingIDNumber) == null || ClassSessions.Of(Teacher.thingIDNumber).Finished;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // Any real need or emergency, or the teacher stopping, ends it, and the pupil is then left alone: they are not sent straight back.
            this.FailOn(() => TeacherGone() || PawnReadiness.StopReason(pawn, Activities.Now) != null);
            AddFinishAction(cond =>
            {
                var s = Teacher == null ? null : ClassSessions.Of(Teacher.thingIDNumber);
                int now = Activities.Now;
                string why = PawnReadiness.StopReason(pawn, now);
                OfficeDriver.Trace("class: " + pawn.LabelShort + " (pupil) finished (" + cond + "), reason: " + (why ?? "none"));
                if (s == null || cond == JobCondition.Succeeded) return;
                // an order from the player, another job taking over, or a need being seen to: they are not recalled until the next block, or until they are fit again
                s.Left[pawn.thingIDNumber] = now + (why != null ? 1500 : ClassSchedule.BlockTicks);
            });
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var sit = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            sit.initAction = () => { pawn.pather?.StopDead(); };
            sit.tickAction = () =>
            {
                var s = ClassSessions.Of(Teacher.thingIDNumber); if (s == null) return;
                int elapsed = Activities.Now - s.StudyingTick;
                if (s.Teaching && ClassSchedule.InBreak(elapsed)) { EndJobWith(JobCondition.Succeeded); return; }          // a break: they are free until the next block
                if (s.RoomCells.Contains(pawn.Position)) s.Attended[pawn.thingIDNumber] = (s.Attended.TryGetValue(pawn.thingIDNumber, out int n) ? n : 0) + 1;
                pawn.rotationTracker?.FaceCell(s.TeacherCell);
            };
            yield return sit;
        }
    }
}
