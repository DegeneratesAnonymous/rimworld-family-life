using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace FamilyLife
{
    // The teacher's side of the community posts. Owner: Education.

    internal static partial class OfficeDriver
    {

        /// <summary>The standing place at a lectern in an enclosed room (the classroom), so the teacher speaks from it when there is one.</summary>
        internal static bool LecternCell(Pawn teacher, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            var map = teacher?.Map;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail("Lectern");
            if (map == null || def == null) return false;
            foreach (var lectern in map.listerThings.ThingsOfDef(def).OfType<Building>().Where(b => b.Spawned && b.Faction == Faction.OfPlayer).OrderBy(b => b.Position.DistanceToSquared(teacher.Position)).Take(6))
            {
                var room = lectern.GetRoom();
                if (room == null || room.PsychologicallyOutdoors) continue;
                var spot = lectern.InteractionCell;
                if (spot.IsValid && spot.InBounds(map) && spot.Standable(map) && teacher.CanReach(spot, PathEndMode.OnCell, Danger.Some)) { cell = spot; return true; }
            }
            return false;
        }

        /// <summary>The skill the teacher could really teach this person: one the teacher is clearly better at and the student can learn. Null if none.</summary>
        internal static SkillDef LearnableSkill(Pawn teacher, Pawn student)
        {
            if (teacher?.skills == null || student?.skills == null) return null;
            SkillDef best = null; int gap = 2;
            foreach (var skill in DefDatabase<SkillDef>.AllDefsListForReading)
            {
                var t = teacher.skills.GetSkill(skill); var k = student.skills.GetSkill(skill);
                if (t == null || k == null || t.TotallyDisabled || k.TotallyDisabled || t.Level - k.Level <= gap) continue;
                gap = t.Level - k.Level; best = skill;
            }
            return best;
        }

        internal static void TeacherWork(FamilyComponent c, FamilyRegistry r, CommunityOffice office, Pawn holder, FamilySettings s, int now)
        {
            if (holder == null || holder.Map == null) return;
            int holderId = holder.thingIDNumber;
            bool adults = s != null && s.adultStudents;
            bool anyone = r.Persons.Any(p => p.InColony && p.Id != holderId && (p.Juvenile || adults));
            r.NoteOfficeNeed(OfficeKind.Teacher, anyone && (s == null || s.holdClasses), now);
            if (s != null && !s.holdClasses) return;

            OfficeEvents.Teacher(c, r, office, holder, now);
            if (HoldClass.IsQueued && (Due("classPending", now, 120) || Forced)) HoldClass.Tick(now);          // a class asked for earlier is held as soon as the teacher can          // the annual testing of the children, offered in Aprimay; classes go on whatever else is happening
            if (ClassSessions.Running(holder)) return;
            bool classroom = ClassroomKnown(holder, now);          // a lectern, a blackboard or a desk in a room: the class is held there, to everyone at once
            if (now >= office.NextSessionTick && office.SessionQueue.Count == 0)
            {
                var students = r.Persons.Where(p => p.InColony && p.Id != holderId && (p.Juvenile || adults))
                    .Where(p => Spawned(c, p.Id) is Pawn pawn && pawn.Map == holder.Map && FreeFor(c, p.Id) && (classroom || LearnableSkill(holder, pawn) != null))
                    .Select(p => p.Id).Take(24).ToList();
                int every = Math.Max(1, Math.Min(10, s?.classEveryDays ?? 3)) * Day;
                if (r.BeginClass(now, every, students))
                    Messages.Message("WFL_Class_Begins".Translate(holder.LabelShort), holder, MessageTypeDefOf.NeutralEvent, false);
            }
            if (classroom)
            {
                if (office.SessionQueue.Count > 0 && ClassSessions.MayTry(holder, now) && (Due("classroom", now, 600) || Forced && Due("classroomT", now, 200)))
                {
                    r.NextStudent(now);
                    if (office.SessionQueue.Count > 0)
                    {
                        var began = ClassSessions.TryBegin(c, holder, office.SessionQueue.ToList(), now);
                        if (began == BeginResult.Cannot) { office.SessionQueue.Clear(); office.NextSessionTick = Math.Max(office.NextSessionTick, now + Day / 2); }          // nothing to teach or nowhere to teach it: not tried again for half a day
                    }
                }
                return;
            }
            int next = r.NextStudent(now);
            if (next == 0) return;
            var student = Spawned(c, next);
            if (student == null || student.Map != holder.Map || !Governance.Free(c, next) || LearnableSkill(holder, student) == null) { r.ClassDone(next); return; }      // cannot come today
            if (Due("lesson", now, 600) && !GovernanceMeetings.Start(holder, student, WFL_StoryDefOf.WFL_OfficeLesson, 0)) r.SessionDefer(OfficeKind.Teacher, next);
        }

        private static int classroomChecked = -100000; private static bool classroomResult;
        private static bool ClassroomKnown(Pawn teacher, int now)
        {
            if (now - classroomChecked < 600 && now >= classroomChecked) return classroomResult;
            classroomChecked = now;
            return classroomResult = ClassRooms.TeachingSpot(teacher, out _, out _, out _);
        }

        /// <summary>
        /// A one-to-one lesson (when there is no classroom or the child cannot join a class): the same single path as a class gives experience, with the time actually spent.
        /// </summary>
        internal static void CompleteLesson(FamilyRegistry r, Pawn teacher, Pawn student, int now, int ticks = LessonTicks, float factor = 1f)
        {
            var office = r.OfficeFor(OfficeKind.Teacher);
            if (office == null || office.HolderId != teacher.thingIDNumber || !office.SessionQueue.Contains(student.thingIDNumber)) return;
            var skill = LearnableSkill(teacher, student);
            if (skill != null)
            {
                float delivery = LessonMath.Delivery(PawnReadiness.Level(teacher, skill), PawnReadiness.Level(teacher, SkillDefOf.Social), 0f, 0f, PawnReadiness.Fatigue(teacher));
                var talk = Conversations.Run(teacher, student, OfficeKind.Teacher, skill, ExchangeTopic.Explain, teacher.thingIDNumber + student.thingIDNumber + now);          // the one word in a one-to-one lesson
                LessonGrant.Apply(FamilyComponent.Current, r, teacher, student, skill, Math.Min(1f, ticks / (float)ClassSchedule.InstructionTicks), ticks, ticks, delivery, talk.Quality, 1, null, now);
            }
            r.ClassDone(student.thingIDNumber);
        }

        internal static List<string> DiagnoseTeacher(FamilyComponent c, int now)
        {
            var lines = new List<string>();
            var r = c.Registry;
            var teacher = r.OfficeFor(OfficeKind.Teacher);
            var settings = FamilyLifeMod.Settings;
            if (teacher == null || !teacher.Held) lines.Add("Teacher: nobody holds the post. Run 'Choose the colony's posts now' first.");
            else
            {
                var t = Spawned(c, teacher.HolderId);
                if (t == null) lines.Add("Teacher: the holder is not on the map.");
                else
                {
                    lines.Add("Teacher: " + t.LabelShort + (settings != null && !settings.holdClasses ? " (classes are switched off in the mod settings)" : ""));
                    if (ClassRooms.TeachingSpot(t, out var cellT, out var room, out var front)) lines.Add("  place to teach: " + (front != null ? front.LabelCap + " at " + front.Position : room == null ? "open ground" : "a room with seats") + ", the teacher stands at " + cellT + (room != null ? ", room of " + room.CellCount + " cells" : ""));
                    else lines.Add("  classroom: no lectern, blackboard or desk in an enclosed room the teacher can reach (lessons will be one to one instead)");
                    bool adults = settings != null && settings.adultStudents;
                    var kids = r.Persons.Where(p => p.InColony && p.Id != teacher.HolderId && (p.Juvenile || adults)).ToList();
                    int able = kids.Count(p => Spawned(c, p.Id) is Pawn pawn && pawn.Map == t.Map && FreeFor(c, p.Id) && Ready(pawn));
                    if (NeedsReason(t) != null) lines.Add("  the teacher should stop for: " + NeedsReason(t));
                    foreach (var k in kids) if (Spawned(c, k.Id) is Pawn kp && NeedsReason(kp) != null) lines.Add("  " + kp.LabelShort + " should stop for: " + NeedsReason(kp));
                    lines.Add("  children in the colony: " + kids.Count + ", able to come now: " + able + ", waiting in the class list: " + teacher.SessionQueue.Count + (ClassSessions.Running(t) ? ", class already running" : ""));
                }
            }
            return lines;
        }
    }
}
