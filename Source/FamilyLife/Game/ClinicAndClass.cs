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

    /// <summary>
    /// One exchange between a doctor or teacher and the person they are talking to: a social contest (the leader's Social plus their second skill against the other
    /// person's Social), a line each said aloud, and what it did to how they feel about each other.
    /// </summary>
    internal static class Conversations
    {
        private static int Level(Pawn p, SkillDef def) { var s = p?.skills?.GetSkill(def); return s == null || s.TotallyDisabled ? 0 : s.Level; }

        internal static float Run(Pawn leader, Pawn other, SkillDef second, ExchangeTopic topic, int seed)
        {
            var c = FamilyComponent.Current; var r = c?.Registry;
            int score = GuidedExchanges.LeaderScore(Level(leader, SkillDefOf.Social), Level(leader, second));
            var tier = GuidedExchanges.Resolve(score, Level(other, SkillDefOf.Social), Rand.RangeInclusive(1, 20), Rand.RangeInclusive(1, 20));
            var e = GuidedExchanges.Effect(topic, tier);
            int now = Activities.Now;
            if (r != null)
            {
                r.ChangeBond(other.thingIDNumber, leader.thingIDNumber, new BondChange(e.Attachment, e.Trust, e.Tension, e.Familiarity), now);
                r.ChangeBond(leader.thingIDNumber, other.thingIDNumber, new BondChange(.2f, .2f, 0f, .5f), now);
            }
            float xp = 6f + 10f * Math.Max(0, GuidedExchanges.TierValue(tier));
            leader.skills?.Learn(SkillDefOf.Social, xp, false);
            var tint = tier == EventTier.Poor ? new Color(1f, .72f, .6f) : tier == EventTier.Great ? new Color(.72f, 1f, .72f) : Color.white;
            string stem = "WFL_Ex_" + topic + "_" + tier;
            MoteMaker.ThrowText(leader.DrawPos, leader.Map, Phrasing.Say(stem + "_Lead", seed), tint);
            MoteMaker.ThrowText(other.DrawPos + new Vector3(0f, 0f, -.35f), other.Map, Phrasing.Say(stem + "_Reply", seed + 7), tint);
            return e.Quality;
        }
    }

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

        private static readonly string[] NotAClassroom = { "temple", "chapel", "church", "shrine", "bedroom", "barracks", "hospital", "prison", "laboratory", "kitchen", "throne", "tomb" };
        private static readonly string[] Worship = { "altar", "ideogram", "pulpit", "reliquary", "animatree", "stele", "sarcophagus", "shrine", "relic" };

        /// <summary>A room that is for something else: a temple or church (which also has lecterns), a bedroom, a hospital, a kitchen.</summary>
        internal static bool IsOtherRoom(Room room)
        {
            string role = (room.Role?.defName ?? "").ToLowerInvariant();
            if (NotAClassroom.Any(role.Contains)) return true;
            foreach (var t in room.ContainedAndAdjacentThings)
            {
                if (!(t is Building)) continue;
                string n = ((t.def.defName ?? "") + " " + (t.def.label ?? "")).ToLowerInvariant();
                if (Worship.Any(n.Contains)) return true;
            }
            return false;
        }

        /// <summary>How much a room looks like a classroom: school furniture and places to sit, with the board or lectern itself counting for most.</summary>
        private static int ClassroomScore(Room room)
        {
            int school = 0, seats = 0;
            foreach (var t in room.ContainedAndAdjacentThings)
            {
                var b = t as Building; if (b == null) continue;
                if (HallDriver.LooksLikeSchool(b.def)) school++;
                if (b.def.building != null && b.def.building.isSittable) seats++;
            }
            return school * 6 + Math.Min(seats, 12);
        }

        /// <summary>
        /// Where the teacher stands: behind a lectern if there is one; otherwise in front of a blackboard; otherwise in front of a desk. Rooms for worship, sleep, healing or
        /// cooking are never chosen (a church has a lectern too), and of the rest the one that looks most like a classroom wins.
        /// The cell, the room, and the thing the teacher faces.
        /// </summary>
        internal static bool TeachingSpot(Pawn teacher, out IntVec3 cell, out Room room, out Thing front)
        {
            cell = IntVec3.Invalid; room = null; front = null;
            var map = teacher?.Map;
            if (map == null) return false;
            int bestScore = int.MinValue;
            foreach (var pass in new[] { 0, 1, 2 })
            {
                int baseScore = pass == 0 ? 30 : pass == 1 ? 20 : 10;
                foreach (var b in map.listerBuildings.allBuildingsColonist.Where(x => x.Spawned && !x.Destroyed && !x.IsForbidden(teacher) && IsFor(x.def, pass)).Take(60))
                {
                    var r = b.GetRoom();
                    if (r == null || r.PsychologicallyOutdoors || IsOtherRoom(r)) continue;
                    int score = baseScore + ClassroomScore(r) * 2 - (int)Math.Min(20f, b.Position.DistanceTo(teacher.Position) / 6f);
                    if (score <= bestScore) continue;
                    foreach (var c in FreeNeighbours(b, map, r))
                    {
                        if (!UsableCell(c, map, r, teacher)) continue;
                        cell = c; room = r; front = b; bestScore = score;
                        break;
                    }
                }
            }
            return front != null;
        }

        internal static bool IsLectern(ThingDef def) => IsFor(def, 0);

        /// <summary>The standing place for the teacher at this particular lectern.</summary>
        internal static bool SpotAt(Building b, Pawn teacher, out IntVec3 cell, out Room room)
        {
            cell = IntVec3.Invalid; room = b?.GetRoom();
            if (b == null || room == null || room.PsychologicallyOutdoors) return false;
            foreach (var c in FreeNeighbours(b, b.Map, room))
                if (UsableCell(c, b.Map, room, teacher)) { cell = c; return true; }
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
        public HashSet<IntVec3> RoomCells = new HashSet<IntVec3>();
        public List<IntVec3> Seats = new List<IntVec3>();
        public readonly Dictionary<int, int> Attended = new Dictionary<int, int>();
        public readonly HashSet<int> Called = new HashSet<int>();
        public bool Teaching, Finished, ByPlayer;
        public List<int> Roster = new List<int>();
        public readonly Dictionary<int, float> Rapport = new Dictionary<int, float>();          // how each pupil got on with the teacher, from the exchanges
        public int Exchanges;
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
        internal static bool TryBegin(FamilyComponent c, Pawn teacher, List<int> roster, int now, Building lectern = null, bool byPlayer = false)
        {
            if (teacher == null || Running(teacher) || roster.Count == 0) return false;
            IntVec3 cell = IntVec3.Invalid; Room room = null; Thing front = lectern;
            bool found = lectern != null ? ClassRooms.SpotAt(lectern, teacher, out cell, out room) : ClassRooms.TeachingSpot(teacher, out cell, out room, out front);
            if (!found) { OfficeDriver.Trace("class: no classroom furniture in an enclosed room for " + teacher.LabelShort); return false; }
            if (byPlayer) { if (!OfficeDriver.Available(teacher) || OfficeDriver.NeedsAttention(teacher)) return false; }
            else if (!OfficeDriver.Ready(teacher) || OfficeDriver.NeedsAttention(teacher) || teacher.CurJob?.playerForced == true || (Find.Selector.IsSelected(teacher) && !OfficeDriver.Forced) || PerspectiveShiftBridge.IsAvatar(teacher)) return true;       // there is a classroom: try again when the teacher is free
            var pupils = roster.Select(id => c.TryGetPawn(id)).Where(p => p != null && p.Spawned).ToList();
            var book = ClassRooms.BestBook(teacher, pupils);
            var s = new ClassSession { TeacherId = teacher.thingIDNumber, StartTick = now, TeacherCell = cell, Room = room, Roster = roster.ToList(), BookId = book?.thingIDNumber ?? 0, ByPlayer = byPlayer };
            s.Seats = ClassRooms.Seats(room, cell, roster.Count);
            foreach (var rc in room.Cells) s.RoomCells.Add(rc);
            OfficeDriver.Trace("class: " + teacher.LabelShort + " goes to " + cell + " in front of " + (front?.LabelCap ?? "the board") + ", " + s.Seats.Count + " seats, book: " + (book?.LabelNoCount ?? "none") + ", " + roster.Count + " children on the list");
            byTeacher[teacher.thingIDNumber] = s;
            var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_ClassTeacher, front != null ? (LocalTargetInfo)front : (LocalTargetInfo)cell, cell);
            if (book != null) job.targetC = book;
            job.playerForced = true;
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
                bool able = s.ByPlayer ? OfficeDriver.Available(kid) : OfficeDriver.Ready(kid) && OfficeDriver.FreeFor(c, id) && !(kid.CurJob?.playerForced == true && !OfficeDriver.Forced);
                if (!able || OfficeDriver.NeedsAttention(kid)) continue;
                var seat = s.Seats.Count > 0 ? s.Seats[seatIndex % s.Seats.Count] : s.TeacherCell;
                if (!kid.CanReach(seat, PathEndMode.OnCell, Danger.None)) continue;
                var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_ClassStudent, teacher, seat);
                job.playerForced = true;
                kid.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                OfficeDriver.Trace("class: " + kid.LabelShort + " is called to " + seat + (kid.CurJobDef == WFL_StoryDefOf.WFL_ClassStudent ? "" : " but could not take the job"));
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
            var factors = new Dictionary<Pawn, float>();
            if (finishedNormally || s.Teaching)
            {
                var book = s.BookId == 0 ? null : FindBook(teacher, s.BookId);
                foreach (var kv in s.Attended)
                {
                    var kid = c.TryGetPawn(kv.Key);
                    if (kid == null || kid.Dead || kv.Value < ClassTicks / 2) { r.ClassDone(kv.Key); continue; }
                    float rapport = s.Rapport.TryGetValue(kv.Key, out var q) ? q : 0f;
                    float factor = GuidedExchanges.LessonFactor(rapport);
                    OfficeDriver.CompleteLesson(r, teacher, kid, now, kv.Value, factor);
                    int feel = GuidedExchanges.Impression(rapport);
                    if (feel > 0) Activities.GainMemory(kid, "WFL_InspiringLesson", teacher); else if (feel < 0) Activities.GainMemory(kid, "WFL_BoringLesson", teacher);
                    pupils.Add(kid); learned++; factors[kid] = factor;
                }
                if (book != null && pupils.Count > 0) GiveBookXp(book, pupils, factors, teacher);
                if (learned > 0)
                    Messages.Message((book != null ? "WFL_Class_EndedBook" : "WFL_Class_Ended").Translate(teacher.LabelShort, learned, book?.LabelNoCount ?? ""), teacher, MessageTypeDefOf.PositiveEvent, false);
            }
            foreach (int id in s.Roster) r.ClassDone(id);
        }

        /// <summary>The teacher says something to one pupil who is in the room: a question, an explanation, a quiz, a story, a word to settle them. Roughly one every hour of the class.</summary>
        internal static void Exchange(ClassSession s, Pawn teacher, int elapsed)
        {
            var c = FamilyComponent.Current; if (c == null) return;
            var present = s.Attended.Keys.Select(id => c.TryGetPawn(id)).Where(p => p != null && p.Spawned && p.CurJobDef == WFL_StoryDefOf.WFL_ClassStudent).ToList();
            if (present.Count == 0) return;
            var pupil = present[Rand.Range(0, present.Count)];
            var topic = GuidedExchanges.PickTopic(GuidedExchanges.ClassTopics, s.Exchanges++, teacher.thingIDNumber);
            float q = Conversations.Run(teacher, pupil, SkillDefOf.Intellectual, topic, teacher.thingIDNumber + elapsed);
            s.Rapport[pupil.thingIDNumber] = (s.Rapport.TryGetValue(pupil.thingIDNumber, out var old) ? old : 0f) + q;
            foreach (var other in present) if (other != pupil) s.Rapport[other.thingIDNumber] = (s.Rapport.TryGetValue(other.thingIDNumber, out var o2) ? o2 : 0f) + q * .25f;          // the rest of the room is carried along, less
        }

        private static Thing FindBook(Pawn teacher, int id)
        {
            if (teacher.carryTracker?.CarriedThing?.thingIDNumber == id) return teacher.carryTracker.CarriedThing;
            return teacher.Map?.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).FirstOrDefault(t => t.thingIDNumber == id);
        }

        /// <summary>Everyone who sat through the class reads the book together, and gets what reading it gives.</summary>
        private static void GiveBookXp(Thing book, IEnumerable<Pawn> pupils, Dictionary<Pawn, float> factors, Pawn teacher)
        {
            var comp = (book as ThingWithComps)?.GetComp<CompBook>();
            if (comp == null) return;
            foreach (var p in pupils.Concat(new[] { teacher }))
                foreach (var doer in comp.Doers.OfType<BookOutcomeDoerGainSkillExp>())
                    foreach (var pair in doer.Values)
                    {
                        var skill = p.skills?.GetSkill(pair.Key);
                        if (skill != null && !skill.TotallyDisabled) skill.Learn(pair.Value * (factors.TryGetValue(p, out var f) ? f : 1f), true);          // a pupil who got on well with the teacher takes in more
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
            this.FailOn(() => pawn.Drafted || pawn.InMentalState || OfficeDriver.NeedsAttention(pawn) || ClassSessions.Of(pawn.thingIDNumber) == null || !FamilyLifeMod.Settings.holdClasses);
            AddFinishAction(cond =>
            {
                OfficeDriver.Trace("class: " + pawn.LabelShort + " (teacher) finished (" + cond + "), need: " + (OfficeDriver.NeedsReason(pawn) ?? "none") + ", session " + (ClassSessions.Of(pawn.thingIDNumber) != null));
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
                if (elapsed > 0 && elapsed % 1000 == 500) ClassSessions.Exchange(s, pawn, elapsed);
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
                OfficeDriver.NeedsAttention(pawn));
            AddFinishAction(cond => OfficeDriver.Trace("class: " + pawn.LabelShort + " (pupil) finished (" + cond + "), need: " + (OfficeDriver.NeedsReason(pawn) ?? "none")));
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var sit = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            sit.initAction = () => { pawn.pather?.StopDead(); };
            sit.tickAction = () =>
            {
                var s = ClassSessions.Of(Teacher.thingIDNumber); if (s == null) return;
                if (s.RoomCells.Contains(pawn.Position)) s.Attended[pawn.thingIDNumber] = (s.Attended.TryGetValue(pawn.thingIDNumber, out int n) ? n : 0) + 1;
                pawn.rotationTracker?.FaceCell(s.TeacherCell);
            };
            yield return sit;
        }
    }

    internal static class HoldClass
    {
        private static Texture2D icon;
        private static Texture2D Icon => icon ?? (icon = ContentFinder<Texture2D>.Get("WilsFamilyLife/Roles/teacher", false) ?? BaseContent.BadTex);

        /// <summary>The "Hold class" button for a lectern in a room that could be a classroom, or null.</summary>
        internal static Gizmo For(Building b)
        {
            if (b == null || !b.Spawned || b.Faction != Faction.OfPlayer || !ClassRooms.IsLectern(b.def)) return null;
            var room = b.GetRoom();
            if (room == null || room.PsychologicallyOutdoors || ClassRooms.IsOtherRoom(room)) return null;
            return new Command_Action
            {
                defaultLabel = "WFL_HoldClass_Label".Translate(),
                defaultDesc = "WFL_HoldClass_Desc".Translate(),
                icon = Icon,
                action = () => Start(b)
            };
        }

        private static void Start(Building lectern)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; if (r == null) return;
            int now = Activities.Now;
            var office = r.OfficeFor(OfficeKind.Teacher);
            var teacher = office != null && office.Held ? c.TryGetPawn(office.HolderId) : null;
            if (teacher == null || !teacher.Spawned || teacher.Map != lectern.Map) { Messages.Message("WFL_HoldClass_NoTeacher".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            if (ClassSessions.Running(teacher)) { Messages.Message("WFL_HoldClass_Running".Translate(teacher.LabelShort), MessageTypeDefOf.RejectInput, false); return; }
            bool adults = FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.adultStudents;
            var ids = r.Persons.Where(p => p.InColony && p.Id != office.HolderId && (p.Juvenile || adults))
                .Where(p => c.TryGetPawn(p.Id) is Pawn k && k.Spawned && k.Map == lectern.Map && OfficeDriver.Available(k)).Select(p => p.Id).Take(24).ToList();
            if (ids.Count == 0) { Messages.Message("WFL_HoldClass_NoPupils".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            office.SessionQueue = ids; office.SessionStartTick = now;          // they are the class: lessons are credited the same way as a class held on the usual round
            if (!ClassSessions.TryBegin(c, teacher, ids, now, lectern, true)) { office.SessionQueue.Clear(); Messages.Message("WFL_HoldClass_Cannot".Translate(teacher.LabelShort), MessageTypeDefOf.RejectInput, false); return; }
            Messages.Message("WFL_HoldClass_Started".Translate(teacher.LabelShort, ids.Count), teacher, MessageTypeDefOf.PositiveEvent, false);
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(Building), nameof(Building.GetGizmos))]
    internal static class Patch_LecternHoldClass
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building __instance)
        {
            foreach (var g in __result) yield return g;
            Gizmo extra = null;
            try { extra = HoldClass.For(__instance); } catch (Exception e) { Log.Warning("[Wil's Family Life] Hold class button skipped: " + e.Message); }
            if (extra != null) yield return extra;
            List<Gizmo> more = null;
            try { more = OfficeEvents.Gizmos(__instance).ToList(); } catch (Exception e) { Log.Warning("[Wil's Family Life] Event buttons skipped: " + e.Message); }
            if (more != null) foreach (var g in more) yield return g;
        }
    }

    // ------------------------------------------------------------------ the clinic

    internal enum ClinicPhase : byte { Waiting, Arriving, Fetching, Escorting, Examining }

    internal sealed class ClinicSession
    {
        public int DoctorId, PatientId, PhaseTick, Idle, NextWander;
        public Building_Bed Bed;
        public IntVec3 DoctorCell;
        public bool PatientLying, Done;
        public ClinicPhase Phase;
        public int Talked, Exchanges;
        public float Quality;
    }

    /// <summary>
    /// The doctor's list for the round, beyond who has been seen (that is in the registry and is saved): who is being given time to come, and who could not be
    /// brought to the clinic and is to be seen where they are the next time they can be. Not saved; after a load the doctor simply starts down the list again.
    /// </summary>
    internal static class CheckupQueue
    {
        private sealed class Entry { public int Fails, RetryAt; public bool OnTheSpot; }
        private static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();

        internal static void Reset() => entries.Clear();
        internal static bool Waiting(int id, int now) => entries.TryGetValue(id, out var e) && now < e.RetryAt;
        internal static bool OnTheSpot(int id) => entries.TryGetValue(id, out var e) && e.OnTheSpot;
        internal static void Seen(int id) => entries.Remove(id);

        /// <summary>This person did not come or could not be reached. The first time they are given another go at the clinic later; after that they are seen where they are.</summary>
        internal static void Failed(int id, int now)
        {
            if (!entries.TryGetValue(id, out var e)) entries[id] = e = new Entry();
            e.Fails++;
            if (e.Fails >= 2) e.OnTheSpot = true;
            e.RetryAt = now + (e.OnTheSpot ? 600 : 1500);
        }
    }

    internal static class ClinicVisits
    {
        /// <summary>The conversation between doctor and patient, in game ticks (about twenty-five seconds at normal speed). A patient is given a game hour to arrive before the doctor goes to find them.</summary>
        internal const int TalkTicks = 1500, ArriveTicks = 2500, FetchTicks = 4000, EscortTicks = 3000;
        private static readonly Dictionary<int, ClinicSession> byDoctor = new Dictionary<int, ClinicSession>();

        internal static void Reset() { byDoctor.Clear(); CheckupQueue.Reset(); }
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
            if (!Spot(doctor, out var bed, out var cell)) { OfficeDriver.Trace("clinic: no medical bed in an enclosed room for " + doctor.LabelShort); return false; }
            if (!OfficeDriver.Ready(doctor) || OfficeDriver.NeedsAttention(doctor) || doctor.CurJob?.playerForced == true || (Find.Selector.IsSelected(doctor) && !OfficeDriver.Forced) || PerspectiveShiftBridge.IsAvatar(doctor)) return true;       // there is a clinic: wait until the doctor is free and has seen to themselves
            OfficeDriver.Trace("clinic: " + doctor.LabelShort + " goes to " + cell + " beside " + bed.LabelCap);
            byDoctor[doctor.thingIDNumber] = new ClinicSession { DoctorId = doctor.thingIDNumber, Bed = bed, DoctorCell = cell };
            var open = JobMaker.MakeJob(WFL_StoryDefOf.WFL_ClinicDoctor, bed, cell);
            open.playerForced = true;
            doctor.jobs.StartJob(open, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            return true;
        }

        internal static void Close(Pawn doctor)
        {
            if (doctor == null || !byDoctor.TryGetValue(doctor.thingIDNumber, out var s)) return;
            s.Done = true; byDoctor.Remove(doctor.thingIDNumber);
        }

        /// <summary>The patient is sent to the bed. False if they could not take the job.</summary>
        internal static bool Send(Pawn patient, ClinicSession s, Pawn doctor)
        {
            var visit = JobMaker.MakeJob(WFL_StoryDefOf.WFL_ClinicPatient, s.Bed, doctor, s.Bed.GetSleepingSlotPos(0));
            visit.playerForced = true;          // not interrupted by the usual choosing of something else to do; it still ends if they are drafted, hurt, hungry or break
            patient.jobs.StartJob(visit, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            return patient.CurJobDef == WFL_StoryDefOf.WFL_ClinicPatient;
        }

        internal static void Release(ClinicSession s) { s.PatientId = 0; s.Phase = ClinicPhase.Waiting; s.PatientLying = false; s.PhaseTick = 0; s.Talked = 0; s.NextWander = 0; }

        /// <summary>The doctor takes the next person on the list who is able to come and has not just been tried.</summary>
        internal static void CallNext(FamilyComponent c, ClinicSession s, Pawn doctor, int now)
        {
            var r = c.Registry;
            int id = r.NextCheckupPatient(now, x => OfficeDriver.CanBeSeen(c, doctor, x) && ForPatient(x) == null && !CheckupQueue.Waiting(x, now) && !CheckupQueue.OnTheSpot(x), OfficeEvents.Priority(c, r.OfficeFor(OfficeKind.Healer)));
            var patient = id == 0 ? null : c.TryGetPawn(id);
            if (patient == null || !patient.Spawned) { s.Idle++; return; }
            s.Idle = 0;
            if (OfficeDriver.NeedsAttention(patient) || !patient.CanReach(s.Bed, PathEndMode.OnCell, Danger.None)) { CheckupQueue.Failed(id, now); OfficeDriver.Trace("clinic: " + patient.LabelShort + " cannot come just now"); return; }
            s.PatientId = id; s.PatientLying = false; s.Talked = 0; s.PhaseTick = 0; s.Quality = 0f; s.Exchanges = 0; s.Phase = ClinicPhase.Arriving;
            OfficeDriver.Trace("clinic: " + patient.LabelShort + " is called in");
            if (!Send(patient, s, doctor)) { OfficeDriver.Trace("clinic: " + patient.LabelShort + " could not take the job"); CheckupQueue.Failed(id, now); Release(s); }
        }

        private static IntVec3 WanderCell(Pawn doctor, ClinicSession s)
        {
            var room = s.Bed.GetRoom(); var map = s.Bed.Map;
            if (room == null) return IntVec3.Invalid;
            var options = GenRadial.RadialCellsAround(s.Bed.Position, 5f, true)
                .Where(c => c.InBounds(map) && c != s.Bed.Position && c.Standable(map) && c.GetRoom(map) == room && !c.GetThingList(map).Any(t => t is Building_Bed) && doctor.CanReach(c, PathEndMode.OnCell, Danger.None)).ToList();
            return options.Count == 0 ? IntVec3.Invalid : options[Rand.Range(0, options.Count)];
        }

        /// <summary>One tick of the doctor's clinic: waiting, calling, going to find someone who has not come, bringing them back, and the examination itself.</summary>
        internal static void Tick(Pawn doctor, ClinicSession s, FamilyComponent c, Action<JobCondition> end)
        {
            int now = Activities.Now;
            if (s.Bed == null || s.Bed.Destroyed) { end(JobCondition.Incompletable); return; }
            if (s.Phase == ClinicPhase.Waiting)
            {
                if (!c.Registry.CheckupOpen(now)) { end(JobCondition.Succeeded); return; }
                if (doctor.Position != s.DoctorCell && !doctor.pather.Moving) doctor.pather.StartPath(s.DoctorCell, PathEndMode.OnCell);
                else if (!doctor.pather.Moving) doctor.rotationTracker?.FaceCell(s.Bed.Position);
                if (doctor.IsHashIntervalTick(60)) CallNext(c, s, doctor, now);
                if (s.Idle > 6) end(JobCondition.Succeeded);          // nobody on the list can come just now: the clinic closes and opens again later
                return;
            }
            var patient = c.TryGetPawn(s.PatientId);
            if (patient == null || patient.Dead || !patient.Spawned) { Release(s); return; }
            s.PhaseTick++;
            switch (s.Phase)
            {
                case ClinicPhase.Arriving:
                    if (patient.CurJobDef != WFL_StoryDefOf.WFL_ClinicPatient && s.PhaseTick > 90) { CheckupQueue.Failed(s.PatientId, now); Release(s); return; }          // pulled away: try again later
                    if (s.PatientLying) { s.Phase = ClinicPhase.Examining; s.Talked = 0; s.NextWander = 0; return; }
                    if (s.PhaseTick > ArriveTicks)
                    {
                        OfficeDriver.Trace("clinic: " + patient.LabelShort + " has not come; " + doctor.LabelShort + " goes to find them");
                        s.Phase = ClinicPhase.Fetching; s.PhaseTick = 0;
                    }
                    return;
                case ClinicPhase.Fetching:
                    if (!OfficeDriver.Ready(patient) || OfficeDriver.NeedsAttention(patient) || !doctor.CanReach(patient, PathEndMode.Touch, Danger.Some) || s.PhaseTick > FetchTicks)
                    { CheckupQueue.Failed(s.PatientId, now); CheckupQueue.Failed(s.PatientId, now); Release(s); return; }          // cannot be brought: seen where they are, when they can be
                    if (doctor.Position.DistanceTo(patient.Position) > 2.5f)
                    {
                        if (!doctor.pather.Moving || s.PhaseTick % 120 == 1) doctor.pather.StartPath(patient, PathEndMode.Touch);
                        return;
                    }
                    MoteMaker.ThrowText(doctor.DrawPos, doctor.Map, Phrasing.Say("WFL_ClinicFetch", doctor.thingIDNumber + s.PatientId), Color.white);
                    if (!Send(patient, s, doctor)) { CheckupQueue.Failed(s.PatientId, now); Release(s); return; }
                    s.Phase = ClinicPhase.Escorting; s.PhaseTick = 0;
                    doctor.pather.StartPath(s.DoctorCell, PathEndMode.OnCell);
                    return;
                case ClinicPhase.Escorting:
                    if (s.PatientLying) { s.Phase = ClinicPhase.Examining; s.Talked = 0; s.NextWander = 0; return; }
                    if (!doctor.pather.Moving && doctor.Position != s.DoctorCell) doctor.pather.StartPath(s.DoctorCell, PathEndMode.OnCell);
                    if (s.PhaseTick > EscortTicks || patient.CurJobDef != WFL_StoryDefOf.WFL_ClinicPatient && s.PhaseTick > 90) { CheckupQueue.Failed(s.PatientId, now); Release(s); }
                    return;
                default:          // Examining
                    s.Talked++;
                    // The doctor does not stand still: they move about the room as they examine, and look to the patient when they stop.
                    if (!doctor.pather.Moving)
                    {
                        if (s.NextWander == 0) s.NextWander = s.Talked + 90;
                        if (s.Talked >= s.NextWander)
                        {
                            var cell = WanderCell(doctor, s);
                            if (cell.IsValid) doctor.pather.StartPath(cell, PathEndMode.OnCell);
                            s.NextWander = s.Talked + Rand.RangeInclusive(200, 420);
                        }
                        else doctor.rotationTracker?.FaceCell(s.Bed.Position);
                    }
                    if ((s.Talked == 150 || s.Talked == 700 || s.Talked == 1250) && !patient.Dead)          // three things raised, spread through the visit
                    {
                        var topic = GuidedExchanges.PickTopic(GuidedExchanges.ClinicTopics, s.Exchanges++, doctor.thingIDNumber + s.PatientId);
                        s.Quality += Conversations.Run(doctor, patient, SkillDefOf.Medicine, topic, doctor.thingIDNumber + s.PatientId + s.Talked);
                    }
                    if (s.Talked >= TalkTicks)
                    {
                        OfficeDriver.CompleteCheckup(c.Registry, doctor, patient, now, s.Quality);
                        Release(s);
                    }
                    return;
            }
        }
    }

    public class JobDriver_ClinicDoctor : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // The round is kept by the registry, so ending here for a meal, sleep, a visit to the toilet or a gathering only pauses it: the doctor takes it up again afterwards.
            this.FailOn(() => pawn.Drafted || pawn.InMentalState || OfficeDriver.NeedsAttention(pawn) || ClinicVisits.Of(pawn.thingIDNumber) == null || !FamilyLifeMod.Settings.healthChecks);
            AddFinishAction(cond => { OfficeDriver.Trace("clinic: " + pawn.LabelShort + " (doctor) finished (" + cond + "), need: " + (OfficeDriver.NeedsReason(pawn) ?? "none")); ClinicVisits.Close(pawn); });
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            var wait = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            wait.initAction = () => pawn.pather?.StopDead();
            wait.tickAction = () =>
            {
                var s = ClinicVisits.Of(pawn.thingIDNumber); var c = FamilyComponent.Current;
                if (s == null || c == null) return;
                ClinicVisits.Tick(pawn, s, c, cond => EndJobWith(cond));
            };
            yield return wait;
        }
    }

    public class JobDriver_ClinicPatient : JobDriver
    {
        private bool lying;
        private Building_Bed Bed => job.targetA.Thing as Building_Bed;
        private Pawn Doctor => job.targetB.Thing as Pawn;
        // The bed is not reserved: the game's own bed rules would refuse a healthy person a medical bed. The clinic sees one patient at a time.
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            AddFinishAction(cond => { OfficeDriver.Trace("clinic: " + pawn.LabelShort + " leaves the clinic (" + cond + "), need: " + (OfficeDriver.NeedsReason(pawn) ?? "none")); if (pawn.jobs != null) pawn.jobs.posture = PawnPosture.Standing; });
            this.AddEndCondition(() =>
            {
                var s = Doctor == null ? null : ClinicVisits.Of(Doctor.thingIDNumber);
                return s == null || s.PatientId != pawn.thingIDNumber ? JobCondition.Succeeded : JobCondition.Ongoing;          // the doctor has finished with this patient, or let them go
            });
            this.FailOn(() => pawn.Drafted || pawn.InMentalState || OfficeDriver.NeedsAttention(pawn) || Doctor == null || Doctor.Dead || ClinicVisits.Of(Doctor.thingIDNumber) == null);
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            var lie = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            lie.initAction = () =>
            {
                pawn.pather?.StopDead();
                lying = true;
                pawn.jobs.posture = PawnPosture.LayingInBed;          // on the bed, in the medical room
                var s = ClinicVisits.Of(Doctor.thingIDNumber);
                if (s != null) { s.PatientLying = true; s.PhaseTick = 0; }
            };
            yield return lie;
        }
    }
}
