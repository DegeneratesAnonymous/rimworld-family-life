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

    internal struct ExchangeResult { public EventTier Tier; public float Quality, Scale; }

    /// <summary>
    /// One exchange between a doctor or teacher and the person they are talking to. It is cooperative: how it goes depends on how well the professional delivers it (their skill
    /// in the subject, their Social, the room, their tiredness), a small part from the other person being able to say what they need, and a little from the day. The
    /// relationship changes only through <see cref="FamilyRegistry.ProfessionalExchange"/>, which limits it per pair per day.
    /// </summary>
    internal static class Conversations
    {
        internal static ExchangeResult Run(Pawn leader, Pawn other, OfficeKind kind, SkillDef subject, ExchangeTopic topic, int seed, float facility = 0f)
        {
            var c = FamilyComponent.Current; var r = c?.Registry;
            float delivery = CooperativeExchange.Delivery(CareRules.Level(leader, subject), CareRules.Level(leader, SkillDefOf.Social), 0f, facility, CareRules.Fatigue(leader));
            var tier = CooperativeExchange.Tier(delivery, CooperativeExchange.Difficulty(topic), CooperativeExchange.Engagement(CareRules.Level(other, SkillDefOf.Social)), Rand.RangeInclusive(-2, 2));
            int now = Activities.Now;
            float scale = r != null ? r.ProfessionalExchange(kind, leader.thingIDNumber, other.thingIDNumber, topic, tier, now) : 1f;
            leader.skills?.Learn(SkillDefOf.Social, (3f + 4f * Math.Max(0, GuidedExchanges.TierValue(tier))) * scale, false);          // nothing once the day's contact with this person is used up
            var tint = tier == EventTier.Poor ? new Color(1f, .72f, .6f) : tier == EventTier.Great ? new Color(.72f, 1f, .72f) : Color.white;
            string stem = "WFL_Ex_" + topic + "_" + tier;
            MoteMaker.ThrowText(leader.DrawPos, leader.Map, Phrasing.Say(stem + "_Lead", seed), tint);
            MoteMaker.ThrowText(other.DrawPos + new Vector3(0f, 0f, -.35f), other.Map, Phrasing.Say(stem + "_Reply", seed + 7), tint);
            return new ExchangeResult { Tier = tier, Quality = GuidedExchanges.TierValue(tier) * GuidedExchanges.Weight(topic), Scale = scale };
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

    internal static class HoldClass
    {
        private static Texture2D icon;
        private static Texture2D Icon => icon ?? (icon = ContentFinder<Texture2D>.Get("WilsFamilyLife/Roles/teacher", false) ?? BaseContent.BadTex);

        /// <summary>The "Hold class" button for a lectern in a room that could be a classroom, or null.</summary>
        internal static Gizmo For(Building b)
        {
            if (b == null || !b.Spawned || b.Faction != Faction.OfPlayer || !ClassRooms.IsLectern(b.def)) return null;
            var reg = FamilyComponent.Current?.Registry;
            if (reg == null || !OfficeAuthority.MayDirect(reg, PerspectiveShiftBridge.AvatarId, OfficeKind.Teacher)) return null;
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

        // A class asked for while the teacher cannot start (hungry, tired, in the middle of something that matters) is held at the next opportunity, for up to a day.
        private static bool pending; private static Building pendingLectern; private static int pendingUntil;
        internal static bool IsQueued => pending;

        internal static void Queue(Building lectern, int now) { pending = true; pendingLectern = lectern; pendingUntil = now + ActivityTuning.TicksPerDay; }
        internal static void Cancel() => pending = false;

        /// <summary>Called as the post's work runs: tries the queued class again, and drops it once held, impossible or a day old.</summary>
        internal static void Tick(int now)
        {
            if (!pending) return;
            if (now > pendingUntil || now < pendingUntil - 2 * ActivityTuning.TicksPerDay) { pending = false; return; }
            if (pendingLectern != null && !pendingLectern.Spawned) pendingLectern = null;
            var result = Attempt(pendingLectern, now, false);
            if (result != BeginResult.Later) pending = false;
        }

        /// <summary>Starts the class if the teacher can. Later means the teacher is not able just now (the caller may queue it); Cannot means it is not possible at all.</summary>
        private static BeginResult Attempt(Building lectern, int now, bool announce)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; if (r == null) return BeginResult.Cannot;
            var office = r.OfficeFor(OfficeKind.Teacher);
            var teacher = office != null && office.Held ? c.TryGetPawn(office.HolderId) : null;
            if (teacher == null || !teacher.Spawned || lectern != null && teacher.Map != lectern.Map) { if (announce) Messages.Message("WFL_HoldClass_NoTeacher".Translate(), MessageTypeDefOf.RejectInput, false); return BeginResult.Cannot; }
            if (ClassSessions.Running(teacher)) { if (announce) Messages.Message("WFL_HoldClass_Running".Translate(teacher.LabelShort), MessageTypeDefOf.RejectInput, false); return BeginResult.Cannot; }
            bool adults = FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.adultStudents;
            var ids = r.Persons.Where(p => p.InColony && p.Id != office.HolderId && (p.Juvenile || adults))
                .Where(p => c.TryGetPawn(p.Id) is Pawn k && k.Spawned && k.Map == teacher.Map && OfficeDriver.Available(k)).Select(p => p.Id).Take(24).ToList();
            if (ids.Count == 0) { if (announce) Messages.Message("WFL_HoldClass_NoPupils".Translate(), MessageTypeDefOf.RejectInput, false); return BeginResult.Later; }          // nobody free yet: worth waiting for
            var saved = office.SessionQueue.ToList(); int savedStart = office.SessionStartTick;
            office.SessionQueue = ids; office.SessionStartTick = now;          // they are the class: lessons are credited the same way as a class held on the usual round
            var result = ClassSessions.TryBegin(c, teacher, ids, now, lectern, true);
            if (result != BeginResult.Started) { office.SessionQueue = saved; office.SessionStartTick = savedStart; return result; }
            Messages.Message("WFL_HoldClass_Started".Translate(teacher.LabelShort, ids.Count), teacher, MessageTypeDefOf.PositiveEvent, false);
            return result;
        }

        private static void Start(Building lectern)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; if (r == null) return;
            int now = Activities.Now;
            if (!OfficeAuthority.MayDirect(r, PerspectiveShiftBridge.AvatarId, OfficeKind.Teacher)) return;          // checked again here: the button may have been shown to someone else
            StartOrQueue(lectern, now);
        }

        /// <summary>Holds the class now, or at the next opportunity if the teacher (or the children) cannot just now.</summary>
        internal static void StartOrQueue(Building lectern, int now)
        {
            var result = Attempt(lectern, now, true);
            if (result == BeginResult.Later)
            {
                Queue(lectern, now);
                var c = FamilyComponent.Current; var o = c?.Registry.OfficeFor(OfficeKind.Teacher); var t = o != null ? c.TryGetPawn(o.HolderId) : null;
                Messages.Message("WFL_HoldClass_Queued".Translate(t?.LabelShort ?? "-"), t, MessageTypeDefOf.NeutralEvent, false);
            }
            else if (result == BeginResult.Cannot && lectern != null) { var c = FamilyComponent.Current; var o = c?.Registry.OfficeFor(OfficeKind.Teacher); var t = o != null ? c.TryGetPawn(o.HolderId) : null; if (t != null && !ClassSessions.Running(t)) Messages.Message("WFL_HoldClass_Cannot".Translate(t.LabelShort), MessageTypeDefOf.RejectInput, false); }
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

}
