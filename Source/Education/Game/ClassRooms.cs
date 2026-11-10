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
    // Where a class is held. Owner: Education.

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
                if (FrameworkServices.LooksLikeSchool(b.def)) school++;
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
            if (front != null) return true;
            return PlainPlace(teacher, out cell, out room);          // no school furniture at all: teaching still happens
        }

        /// <summary>
        /// Teaching needs no furniture. Failing a lectern, board or desk, the lesson is given where people already sit (a room with seats), or, failing that, in the open
        /// beside the teacher. A tribal colony with a campfire and some logs has a school.
        /// </summary>
        internal static bool PlainPlace(Pawn teacher, out IntVec3 cell, out Room room)
        {
            cell = IntVec3.Invalid; room = null;
            var map = teacher?.Map; if (map == null) return false;
            Room bestRoom = null; int bestSeats = 1; Building anchor = null;
            foreach (var b in map.listerBuildings.allBuildingsColonist.Where(x => x.Spawned && x.def.building != null && x.def.building.isSittable && !(x is Building_Bed)).Take(120))
            {
                var r = b.GetRoom();
                if (r == null || r.PsychologicallyOutdoors || IsOtherRoom(r)) continue;
                int seats = r.ContainedAndAdjacentThings.Count(t => t is Building tb && tb.def.building != null && tb.def.building.isSittable);
                if (seats > bestSeats) { bestSeats = seats; bestRoom = r; anchor = b; }
            }
            if (bestRoom != null)
                foreach (var c in GenRadial.RadialCellsAround(anchor.Position, 6f, true))
                    if (UsableCell(c, map, bestRoom, teacher) && !c.GetThingList(map).Any(t => t is Building tb && tb.def.building != null && tb.def.building.isSittable)) { cell = c; room = bestRoom; return true; }
            // in the open, where the teacher is, if that is somewhere ordinary
            var here = teacher.Position;
            var hr = here.GetRoom(map);
            if (here.InBounds(map) && here.Standable(map) && (hr == null || hr.PsychologicallyOutdoors) && !here.GetThingList(map).Any(t => t is Building)) { cell = here; room = null; return true; }
            return false;
        }

        internal static bool IsLectern(ThingDef def) => IsFor(def, 0);

        /// <summary>How much the place helps the teacher, small: a lectern or board is a little, a plain place nothing, the open air a little less.</summary>
        internal static float Facility(Room room, Thing front)
        {
            if (front == null) return room == null ? -.1f : 0f;
            string n = ((front.def.defName ?? "") + " " + (front.def.label ?? "")).ToLowerInvariant();
            return n.Contains("lectern") ? .1f : BoardWords.Any(n.Contains) ? .08f : .05f;
        }

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
        internal static List<IntVec3> Seats(Room room, IntVec3 teacherCell, int count, Map openMap = null)
        {
            var seats = new List<IntVec3>();
            var map = room?.Map ?? openMap;
            if (map == null) return seats;
            if (room == null)          // in the open: free ground near the teacher
            {
                foreach (var c in GenRadial.RadialCellsAround(teacherCell, 7f, true))
                {
                    if (seats.Count >= count + 6) break;
                    if (c == teacherCell || c.DistanceTo(teacherCell) < 2f || !c.InBounds(map) || !c.Standable(map) || c.GetThingList(map).Any(t => t is Building)) continue;
                    seats.Add(c);
                }
                return seats;
            }
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
}
