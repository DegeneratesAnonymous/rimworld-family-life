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
    // The doctor's check-ups and the teacher's classes happen in a place, with the people in it:
    //   a clinic - the doctor waits in the medical room; patients come in one at a time, lie on the bed, talk with the doctor, and leave;
    //   a class  - the teacher stands at the lectern (or in front of a blackboard or desk) and speaks for four hours to the colony's children, who sit in the room.
    // Both are ordinary jobs, so drafting, selecting, an emergency or a mental break ends them the same way it ends anything else. Nothing here is saved: a session
    // that was running when the game was loaded simply does not continue, and the office starts a new one when it is next due.

    internal static class ClassRooms
    {
        private static readonly string[] BoardWords = { "blackboard", "chalkboard", "whiteboard", "teachingboard" };

        private static bool Named(ThingDef def, string[] words)
        {
            string n = ((def?.defName ?? "") + " " + (def?.label ?? "")).ToLowerInvariant();
            return words.Any(n.Contains);
        }

        private static IEnumerable<IntVec3> FreeNeighbours(Building b, Map map, Room room)
        {
            var seen = new HashSet<IntVec3>();
            if (b.def.hasInteractionCell && b.InteractionCell.IsValid) { seen.Add(b.InteractionCell); yield return b.InteractionCell; }
            foreach (var c in GenAdj.CellsAdjacentCardinal(b)) if (seen.Add(c)) yield return c;
            foreach (var c in GenAdj.CellsAdjacent8Way(b)) if (seen.Add(c)) yield return c;
        }

        private static bool UsableCell(IntVec3 c, Map map, Room room, Pawn who) =>
            c.InBounds(map) && c.Standable(map) && c.GetRoom(map) == room && !c.GetThingList(map).Any(t => t is Building_Bed) && who.CanReach(c, PathEndMode.OnCell, Danger.Some);

        /// <summary>
        /// Where the teacher stands: behind a lectern in an enclosed room if there is one; otherwise in front of a blackboard; otherwise in front of a desk.
        /// The cell, the room, and the thing the teacher faces.
        /// </summary>
        internal static bool TeachingSpot(Pawn teacher, out IntVec3 cell, out Room room, out Thing front)
        {
            cell = IntVec3.Invalid; room = null; front = null;
            var map = teacher?.Map;
            if (map == null) return false;
            foreach (var pass in new[] { 0, 1, 2 })
            {
                var candidates = map.listerBuildings.allBuildingsColonist.Where(b => b.Spawned && !b.Destroyed && !b.IsForbidden(teacher) && IsFor(b.def, pass))
                    .OrderBy(b => b.Position.DistanceToSquared(teacher.Position)).Take(12);
                foreach (var b in candidates)
                {
                    var r = b.GetRoom();
                    if (r == null || r.PsychologicallyOutdoors) continue;
                    foreach (var c in FreeNeighbours(b, map, r))
                    {
                        if (!UsableCell(c, map, r, teacher)) continue;
                        cell = c; room = r; front = b;
                        return true;
                    }
                }
            }
            return false;
        }

        private static bool IsFor(ThingDef def, int pass)
        {
            if (pass == 0) return def.defName == "Lectern" || def.defName.EndsWith("Lectern", StringComparison.OrdinalIgnoreCase) || Named(def, new[] { "lectern" });
            if (pass == 1) return Named(def, BoardWords);
            return Named(def, new[] { "desk" }) && (def.building == null || !def.building.isSittable);
        }

        /// <summary>The places the children sit or stand: chairs in the classroom first, then free floor near the front. One per child, never the teacher's spot.</summary>
        internal static List<IntVec3> Seats(Room room, IntVec3 teacherCell, int count)
        {
            var seats = new List<IntVec3>();
            if (room == null) return seats;
            var map = room.Map;
            foreach (var chair in map.listerBuildings.allBuildingsColonist.Where(b => b.def.building != null && b.def.building.isSittable && b.GetRoom() == room).OrderBy(b => b.Position.DistanceToSquared(teacherCell)))
                if (chair.Position != teacherCell && !seats.Contains(chair.Position)) seats.Add(chair.Position);
            foreach (var c in room.Cells.OrderBy(c => c.DistanceToSquared(teacherCell)))
            {
                if (seats.Count >= count + 6) break;
                if (c == teacherCell || c.DistanceTo(teacherCell) < 2f || seats.Contains(c) || !c.Standable(map) || c.GetThingList(map).Any(t => t is Building_Bed)) continue;
                seats.Add(c);
            }
            return seats;
        }

        /// <summary>The book that would teach this class the most, if there is one the teacher can fetch. The class then reads from it together.</summary>
        internal static Thing BestBook(Pawn teacher, IEnumerable<Pawn> students)
        {
            Thing best = null; float bestValue = 0f;
            var list = students.ToList();
            var things = teacher.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < things.Count; i++)
            {
                var t = things[i];
                var comp = (t as ThingWithComps)?.GetComp<CompBook>();
                if (comp == null || !t.Spawned || t.IsForbidden(teacher) || t.Position.DistanceTo(teacher.Position) > 60f) continue;
                float value = 0f;
                foreach (var doer in comp.Doers.OfType<BookOutcomeDoerGainSkillExp>())
                    foreach (var pair in doer.Values)
                        foreach (var s in list)
                        {
                            var sk = s.skills?.GetSkill(pair.Key);
                            if (sk != null && !sk.TotallyDisabled) value += pair.Value;
                        }
                if (value <= bestValue || !teacher.CanReserve(t) || !teacher.CanReach(t, PathEndMode.ClosestTouch, Danger.None)) continue;
                best = t; bestValue = value;
            }
            return best;
        }
    }

    // ------------------------------------------------------------------ the class

    internal sealed class ClassSession
    {
        public int TeacherId, StartTick, StudyingTick, BookId;
        public IntVec3 TeacherCell;
        public Room Room;
        public List<IntVec3> Seats = new List<IntVec3>();
        public readonly Dictionary<int, int> Attended = new Dictionary<int, int>();
        public readonly HashSet<int> Called = new HashSet<int>();
        public bool Teaching, Finished;
        public List<int> Roster = new List<int>();
    }

    internal static class ClassSessions
    {
        /// <summary>Four game hours of teaching.</summary>
        internal const int ClassTicks = 4 * 2500;
        private static readonly Dictionary<int, ClassSession> byTeacher = new Dictionary<int, ClassSession>();

        internal static void Reset() => byTeacher.Clear();
        internal static ClassSession Of(int teacherId) => byTeacher.TryGetValue(teacherId, out var s) ? s : null;
        internal static bool Running(Pawn teacher) => teacher != null && byTeacher.ContainsKey(teacher.thingIDNumber);

        /// <summary>Starts a class: the teacher goes to the front of the classroom (fetching a book first if there is one) and calls the children when they arrive.</summary>
        internal static bool TryBegin(FamilyComponent c, Pawn teacher, List<int> roster, int now)
        {
            if (teacher == null || Running(teacher) || roster.Count == 0) return false;
            if (!ClassRooms.TeachingSpot(teacher, out var cell, out var room, out var front)) return false;
            if (!OfficeDriver.Ready(teacher) || teacher.CurJob?.playerForced == true || (Find.Selector.IsSelected(teacher) && !OfficeDriver.Forced) || PerspectiveShiftBridge.IsAvatar(teacher)) return true;       // there is a classroom: try again when the teacher is free
            var pupils = roster.Select(id => c.TryGetPawn(id)).Where(p => p != null && p.Spawned).ToList();
            var book = ClassRooms.BestBook(teacher, pupils);
            var s = new ClassSession { TeacherId = teacher.thingIDNumber, StartTick = now, TeacherCell = cell, Room = room, Roster = roster.ToList(), BookId = book?.thingIDNumber ?? 0 };
            s.Seats = ClassRooms.Seats(room, cell, roster.Count);
            byTeacher[teacher.thingIDNumber] = s;
            var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_ClassTeacher, front != null ? (LocalTargetInfo)front : (LocalTargetInfo)cell, cell);
            if (book != null) job.targetC = book;
            teacher.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            return true;
        }

        internal static void CallStudents(FamilyComponent c, ClassSession s, Pawn teacher)
        {
            int i = 0;
            foreach (int id in s.Roster)
            {
                var kid = c.TryGetPawn(id);
                int seatIndex = i++;
                if (kid == null || !kid.Spawned || kid.Map != teacher.Map || s.Attended.ContainsKey(id) && kid.CurJobDef == WFL_StoryDefOf.WFL_ClassStudent) continue;
                if (!OfficeDriver.Ready(kid) || !OfficeDriver.FreeFor(c, id) || kid.CurJob?.playerForced == true && !OfficeDriver.Forced) continue;
                var seat = s.Seats.Count > 0 ? s.Seats[seatIndex % s.Seats.Count] : s.TeacherCell;
                if (!kid.CanReach(seat, PathEndMode.OnCell, Danger.None)) continue;
                var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_ClassStudent, teacher, seat);
                kid.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                s.Called.Add(id);
                if (!s.Attended.ContainsKey(id)) s.Attended[id] = 0;
            }
        }

        internal static void End(Pawn teacher, bool finishedNormally)
        {
            if (teacher == null || !byTeacher.TryGetValue(teacher.thingIDNumber, out var s)) return;
            byTeacher.Remove(teacher.thingIDNumber);
            s.Finished = true;
            var c = FamilyComponent.Current; var r = c?.Registry;
            if (r == null) return;
            int now = Activities.Now;
            int learned = 0;
            var pupils = new List<Pawn>();
            if (finishedNormally || s.Teaching)
            {
                var book = s.BookId == 0 ? null : FindBook(teacher, s.BookId);
                foreach (var kv in s.Attended)
                {
                    var kid = c.TryGetPawn(kv.Key);
                    if (kid == null || kid.Dead || kv.Value < ClassTicks / 2) { r.ClassDone(kv.Key); continue; }
                    OfficeDriver.CompleteLesson(r, teacher, kid, now, kv.Value);
                    pupils.Add(kid); learned++;
                }
                if (book != null && pupils.Count > 0) GiveBookXp(book, pupils.Concat(new[] { teacher }));
                if (learned > 0)
                    Messages.Message((book != null ? "WFL_Class_EndedBook" : "WFL_Class_Ended").Translate(teacher.LabelShort, learned, book?.LabelNoCount ?? ""), teacher, MessageTypeDefOf.PositiveEvent, false);
            }
            foreach (int id in s.Roster) r.ClassDone(id);
        }

        private static Thing FindBook(Pawn teacher, int id)
        {
            if (teacher.carryTracker?.CarriedThing?.thingIDNumber == id) return teacher.carryTracker.CarriedThing;
            return teacher.Map?.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).FirstOrDefault(t => t.thingIDNumber == id);
        }

        /// <summary>Everyone who sat through the class reads the book together, and gets what reading it gives.</summary>
        private static void GiveBookXp(Thing book, IEnumerable<Pawn> readers)
        {
            var comp = (book as ThingWithComps)?.GetComp<CompBook>();
            if (comp == null) return;
            foreach (var p in readers)
                foreach (var doer in comp.Doers.OfType<BookOutcomeDoerGainSkillExp>())
                    foreach (var pair in doer.Values)
                    {
                        var skill = p.skills?.GetSkill(pair.Key);
                        if (skill != null && !skill.TotallyDisabled) skill.Learn(pair.Value, true);
                    }
        }
    }

    public class JobDriver_ClassTeacher : JobDriver
    {
        private int sinceCall;
        public override bool TryMakePreToilReservations(bool errorOnFailed) =>
            !job.targetC.HasThing || pawn.Reserve(job.targetC, job, 1, 1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Drafted || pawn.InMentalState || ClassSessions.Of(pawn.thingIDNumber) == null || !FamilyLifeMod.Settings.holdClasses);
            AddFinishAction(cond =>
            {
                if (pawn.carryTracker?.CarriedThing != null && pawn.Spawned) pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                ClassSessions.End(pawn, cond == JobCondition.Succeeded);
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
                var c = FamilyComponent.Current; if (c != null) ClassSessions.CallStudents(c, s, pawn);
                pawn.rotationTracker?.FaceCell(s.Room.Cells.Where(x => x != s.TeacherCell).OrderByDescending(x => x.DistanceToSquared(s.TeacherCell)).FirstOrDefault());
            };
            teach.tickAction = () =>
            {
                var s = ClassSessions.Of(pawn.thingIDNumber); if (s == null) return;
                int elapsed = Activities.Now - s.StudyingTick;
                if (pawn.Position != s.TeacherCell && pawn.pather != null && !pawn.pather.Moving) pawn.pather.StartPath(s.TeacherCell, PathEndMode.OnCell);
                if (elapsed % 480 == 0 && elapsed > 0) MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, Phrasing.Say("WFL_ClassTeacher", pawn.thingIDNumber + elapsed / 480), Color.white);
                if (elapsed < 3000 && ++sinceCall >= 300) { sinceCall = 0; var c = FamilyComponent.Current; if (c != null) ClassSessions.CallStudents(c, s, pawn); }      // late arrivals are called in
                if (elapsed > 3000 && s.Attended.Values.All(v => v == 0)) { EndJobWith(JobCondition.Incompletable); return; }          // nobody came
                if (elapsed >= ClassSessions.ClassTicks) EndJobWith(JobCondition.Succeeded);
            };
            yield return teach;
        }
    }

    public class JobDriver_ClassStudent : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        private Pawn Teacher => job.targetA.Thing as Pawn;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Drafted || pawn.InMentalState || Teacher == null || Teacher.Dead || Teacher.Downed || ClassSessions.Of(Teacher.thingIDNumber) == null || ClassSessions.Of(Teacher.thingIDNumber).Finished ||
                (pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < .12f) || (pawn.needs?.rest != null && pawn.needs.rest.CurLevelPercentage < .1f));
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var sit = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            sit.initAction = () => { pawn.pather?.StopDead(); };
            sit.tickAction = () =>
            {
                var s = ClassSessions.Of(Teacher.thingIDNumber); if (s == null) return;
                if (pawn.Position.GetRoom(pawn.Map) == s.Room) s.Attended[pawn.thingIDNumber] = (s.Attended.TryGetValue(pawn.thingIDNumber, out int n) ? n : 0) + 1;
                pawn.rotationTracker?.FaceCell(s.TeacherCell);
                if (Teacher.Spawned && (Activities.Now + pawn.thingIDNumber) % 900 == 0 && Rand.Chance(.3f)) MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, Phrasing.Say("WFL_ClassPupil", pawn.thingIDNumber + Activities.Now / 900), Color.white);
            };
            yield return sit;
        }
    }

    // ------------------------------------------------------------------ the clinic

    internal sealed class ClinicSession
    {
        public int DoctorId, PatientId, PhaseTick, Idle;
        public Building_Bed Bed;
        public IntVec3 DoctorCell;
        public bool PatientLying, Done;
        public int Talked;
    }

    internal static class ClinicVisits
    {
        /// <summary>The conversation between doctor and patient, in game ticks (about twenty-five seconds at normal speed).</summary>
        internal const int TalkTicks = 1500, ArriveTicks = 2400;
        private static readonly Dictionary<int, ClinicSession> byDoctor = new Dictionary<int, ClinicSession>();

        internal static void Reset() => byDoctor.Clear();
        internal static ClinicSession Of(int doctorId) => byDoctor.TryGetValue(doctorId, out var s) ? s : null;
        internal static ClinicSession ForPatient(int patientId) => byDoctor.Values.FirstOrDefault(s => s.PatientId == patientId && !s.Done);
        internal static bool Running(Pawn doctor) => doctor != null && byDoctor.ContainsKey(doctor.thingIDNumber);

        /// <summary>A free medical bed in an enclosed room and a place beside it for the doctor to wait.</summary>
        internal static bool Spot(Pawn doctor, out Building_Bed bed, out IntVec3 cell)
        {
            bed = null; cell = IntVec3.Invalid;
            var map = doctor?.Map; if (map == null) return false;
            foreach (var b in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>().Where(x => !x.Destroyed && !x.ForPrisoners && (x.Medical || x.GetRoom()?.Role == RoomRoleDefOf.Hospital))
                         .OrderBy(x => x.Medical ? 0 : 1).ThenBy(x => x.Position.DistanceToSquared(doctor.Position)).Take(10))
            {
                var room = b.GetRoom();
                if (room == null || room.PsychologicallyOutdoors || b.AnyOccupants) continue;
                foreach (var near in GenAdj.CellsAdjacent8Way(b).OrderBy(n => n.DistanceToSquared(b.Position)))
                {
                    if (!near.InBounds(map) || !near.Standable(map) || near.GetRoom(map) != room || near.GetThingList(map).Any(t => t is Building_Bed)) continue;
                    if (!doctor.CanReach(near, PathEndMode.OnCell, Danger.Some)) continue;
                    bed = b; cell = near;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Opens the clinic: the doctor goes to the medical room and waits for patients. False when there is no medical room, so the caller can use the older way.</summary>
        internal static bool TryOpen(Pawn doctor)
        {
            if (doctor == null || Running(doctor)) return true;
            if (!Spot(doctor, out var bed, out var cell)) return false;
            if (!OfficeDriver.Ready(doctor) || doctor.CurJob?.playerForced == true || (Find.Selector.IsSelected(doctor) && !OfficeDriver.Forced) || PerspectiveShiftBridge.IsAvatar(doctor)) return true;       // there is a clinic: wait for the doctor
            byDoctor[doctor.thingIDNumber] = new ClinicSession { DoctorId = doctor.thingIDNumber, Bed = bed, DoctorCell = cell };
            doctor.jobs.StartJob(JobMaker.MakeJob(WFL_StoryDefOf.WFL_ClinicDoctor, bed, cell), JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            return true;
        }

        internal static void Close(Pawn doctor)
        {
            if (doctor == null || !byDoctor.TryGetValue(doctor.thingIDNumber, out var s)) return;
            s.Done = true; byDoctor.Remove(doctor.thingIDNumber);
        }

        internal static void CallNext(FamilyComponent c, ClinicSession s, Pawn doctor, int now)
        {
            var r = c.Registry;
            int id = r.NextCheckupPatient(now, x => OfficeDriver.CanBeSeen(c, doctor, x) && ForPatient(x) == null);
            var patient = id == 0 ? null : c.TryGetPawn(id);
            if (patient == null || !patient.Spawned) { s.Idle++; return; }
            if (!patient.CanReach(s.Bed, PathEndMode.OnCell, Danger.None) || !patient.CanReserve(s.Bed, s.Bed.SleepingSlotsCount, 0)) { OfficeDriver.SkipPatient(id, now); return; }
            s.PatientId = id; s.PatientLying = false; s.Talked = 0; s.PhaseTick = 0; s.Idle = 0;
            patient.jobs.StartJob(JobMaker.MakeJob(WFL_StoryDefOf.WFL_ClinicPatient, s.Bed, doctor), JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
        }
    }

    public class JobDriver_ClinicDoctor : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Drafted || pawn.InMentalState || ClinicVisits.Of(pawn.thingIDNumber) == null || !FamilyLifeMod.Settings.healthChecks);
            AddFinishAction(cond => ClinicVisits.Close(pawn));
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var wait = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            wait.initAction = () => pawn.pather?.StopDead();
            wait.tickAction = () =>
            {
                var s = ClinicVisits.Of(pawn.thingIDNumber); var c = FamilyComponent.Current;
                if (s == null || c == null) return;
                int now = Activities.Now;
                if (s.Bed == null || s.Bed.Destroyed) { EndJobWith(JobCondition.Incompletable); return; }
                if (pawn.Position != s.DoctorCell && !pawn.pather.Moving) pawn.pather.StartPath(s.DoctorCell, PathEndMode.OnCell);
                pawn.rotationTracker?.FaceCell(s.Bed.Position);
                if (s.PatientId == 0)
                {
                    if (!c.Registry.CheckupOpen(now)) { EndJobWith(JobCondition.Succeeded); return; }
                    if (pawn.IsHashIntervalTick(60)) ClinicVisits.CallNext(c, s, pawn, now);
                    if (s.Idle > 6) EndJobWith(JobCondition.Succeeded);                          // nobody left who can come: the clinic closes
                    return;
                }
                var patient = c.TryGetPawn(s.PatientId);
                if (patient == null || patient.Dead || patient.CurJobDef != WFL_StoryDefOf.WFL_ClinicPatient)
                {
                    if (s.PhaseTick > 60 || patient == null) { OfficeDriver.SkipPatient(s.PatientId, now); s.PatientId = 0; s.PhaseTick = 0; return; }
                }
                s.PhaseTick++;
                if (!s.PatientLying)
                {
                    if (s.PhaseTick > ClinicVisits.ArriveTicks) { OfficeDriver.SkipPatient(s.PatientId, now); ReleasePatient(s); }          // did not make it to the bed
                    return;
                }
                s.Talked++;
                if (s.Talked % 300 == 1) MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, Phrasing.Say("WFL_ClinicDoctor", pawn.thingIDNumber + s.Talked / 300 + s.PatientId), Color.white);
                else if (s.Talked % 300 == 150 && patient != null) MoteMaker.ThrowText(patient.DrawPos, patient.Map, Phrasing.Say("WFL_ClinicPatient", s.PatientId + s.Talked / 300), Color.white);
                if (s.Talked >= ClinicVisits.TalkTicks)
                {
                    if (patient != null && !patient.Dead) OfficeDriver.CompleteCheckup(c.Registry, pawn, patient, now);
                    ReleasePatient(s);
                }
            };
            yield return wait;
        }

        /// <summary>The patient gets up and goes; the next can come in.</summary>
        private static void ReleasePatient(ClinicSession s) { s.PatientId = 0; s.PatientLying = false; s.PhaseTick = 0; s.Talked = 0; }
    }

    public class JobDriver_ClinicPatient : JobDriver
    {
        private Building_Bed Bed => job.targetA.Thing as Building_Bed;
        private Pawn Doctor => job.targetB.Thing as Pawn;
        public override bool TryMakePreToilReservations(bool errorOnFailed) => Bed != null && pawn.Reserve(Bed, job, Bed.SleepingSlotsCount, 0, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.AddEndCondition(() =>
            {
                var s = Doctor == null ? null : ClinicVisits.Of(Doctor.thingIDNumber);
                return s != null && s.PatientLying && s.PatientId != pawn.thingIDNumber ? JobCondition.Succeeded : JobCondition.Ongoing;          // the doctor has finished with this patient
            });
            this.FailOn(() => pawn.Drafted || pawn.InMentalState || Doctor == null || Doctor.Dead || ClinicVisits.Of(Doctor.thingIDNumber) == null);
            yield return Toils_Bed.GotoBed(TargetIndex.A);
            var arrived = new Toil { defaultCompleteMode = ToilCompleteMode.Instant };
            arrived.initAction = () => { var s = ClinicVisits.Of(Doctor.thingIDNumber); if (s != null) { s.PatientLying = true; s.PhaseTick = 0; } };
            yield return arrived;
            yield return Toils_LayDown.LayDown(TargetIndex.A, true, false, false, false);
        }
    }
}
