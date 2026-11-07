using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    [DefOf]
    public static class WFL_HediffDefOf
    {
        public static HediffDef WFL_CheckedUp;

        static WFL_HediffDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(WFL_HediffDefOf));
    }

    /// <summary>
    /// The game side of the two community posts. The teacher holds a class every few days (the player chooses how often, and whether adults may
    /// join); the healer gives every colonist a check-up a couple of times a year in the medical room. Credit comes only from things the game saw:
    /// a lesson actually given, an examination actually done, a patient actually tended. Nothing here changes a work priority.
    /// </summary>
    internal static class OfficeDriver
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private static readonly Dictionary<string, int> lastAct = new Dictionary<string, int>();

        internal static OfficeContext Context(FamilyComponent c) => new OfficeContext
        {
            Skill = (id, kind) => SkillFor(c.TryGetPawn(id), kind),
            Free = id => Governance.Free(c, id),
            Player = id => Governance.IsAvatar(c, id)
        };

        /// <summary>0 to 1. A pawn we cannot see, or who cannot do the kind of work, is not fit.</summary>
        internal static float SkillFor(Pawn p, OfficeKind kind)
        {
            if (p == null || p.skills == null || p.Dead) return 0f;
            float S(SkillDef def) { var s = p.skills.GetSkill(def); return s == null || s.TotallyDisabled ? 0f : Math.Min(1f, s.Level / 20f); }
            if (kind == OfficeKind.Healer) return p.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) ? 0f : S(SkillDefOf.Medicine);
            return p.WorkTagIsDisabled(WorkTags.Social) ? 0f : .6f * S(SkillDefOf.Intellectual) + .4f * S(SkillDefOf.Social);
        }

        internal static void Run(FamilyComponent c, int now)
        {
            var r = c.Registry;
            foreach (var office in r.ElectOffices(now, Context(c)).Where(o => o.Held))
            {
                var pawn = c.TryGetPawn(office.HolderId);
                if (pawn != null) Messages.Message("WFL_Office_Elected".Translate(pawn.LabelShort, ("WFL_OfficeKind_" + office.Kind).Translate()), pawn, MessageTypeDefOf.PositiveEvent, false);
            }
            SyncChaplain(c, r, now);
            r.MaintainOffices(now);
            AssemblyDriver.Run(c, r, now);
            foreach (var office in r.Offices.Where(o => o.Held).ToList())
            {
                try { Work(c, r, office, now); }
                catch (Exception ex) { Log.Warning("[Wil's Family Life] Community post '" + office.Kind + "' skipped: " + ex.Message); }
            }
        }

        /// <summary>The chaplain is the ideoligion's moral guide, the one who converts and speaks for the faith. The post simply follows the game.</summary>
        private static void SyncChaplain(FamilyComponent c, FamilyRegistry r, int now)
        {
            int person = 0;
            try
            {
                if (ModsConfig.IdeologyActive)
                {
                    var ideo = Faction.OfPlayer.ideos?.PrimaryIdeo;
                    var guide = ideo?.RolesListForReading.FirstOrDefault(x => x.def == PreceptDefOf.IdeoRole_Moralist)?.ChosenPawnSingle();
                    if (guide != null && guide.Faction == Faction.OfPlayer && !guide.Dead) person = guide.thingIDNumber;
                }
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Could not read the moral guide: " + ex.Message); }
            if (r.AppointOffice(OfficeKind.Chaplain, person, now) && person != 0 && c.TryGetPawn(person) is Pawn p)
                Messages.Message("WFL_Chaplain_Named".Translate(p.LabelShort), p, MessageTypeDefOf.NeutralEvent, false);
        }

        private static bool Due(string key, int now, int gap)
        {
            if (lastAct.TryGetValue(key, out int last) && now >= last && now - last < gap) return false;
            lastAct[key] = now;
            return true;
        }

        private static Pawn Spawned(FamilyComponent c, int id) { var p = c.TryGetPawn(id); return p != null && p.Spawned && !p.Dead ? p : null; }

        private static void Work(FamilyComponent c, FamilyRegistry r, CommunityOffice office, int now)
        {
            var s = FamilyLifeMod.Settings;
            int holderId = office.HolderId;
            var holder = Spawned(c, holderId);
            if (office.Kind == OfficeKind.Healer) HealerWork(c, r, office, holder, s, now);
            else if (office.Kind == OfficeKind.Teacher) TeacherWork(c, r, office, holder, s, now);
            else if (office.Kind == OfficeKind.HallKeeper) HallDriver.Work(c, r, office, holder, s, now, (key, n, gap) => Due(key, n, gap));
        }

        // ------------------------------------------------------------------ the doctor

        private static void HealerWork(FamilyComponent c, FamilyRegistry r, CommunityOffice office, Pawn holder, FamilySettings s, int now)
        {
            if (holder?.records == null || holder.Map == null) return;
            int holderId = holder.thingIDNumber;
            int tended = (int)holder.records.GetValue(RecordDefOf.TimesTendedOther);
            if (!office.CounterReady) { office.Counter = tended; office.CounterReady = true; }
            else if (tended > office.Counter)
            {
                int delta = Math.Min(3, tended - office.Counter); office.Counter = tended;
                for (int i = 0; i < delta; i++) r.RecordOfficeWork(OfficeKind.Healer, holderId, GovernanceMeetings.NextToken(), now, .4f + .6f * SkillFor(holder, OfficeKind.Healer));
            }
            bool needed = holder.Map.mapPawns.FreeColonistsSpawned.Take(80).Any(p => HealthAIUtility.ShouldBeTendedNowByPlayer(p));
            if (s == null || s.healthChecks)
            {
                int perYear = Math.Max(1, Math.Min(4, s?.checkupsPerYear ?? 2));
                if (r.BeginCheckupRound(now, 60 * Day / perYear))
                    Messages.Message("WFL_Checkup_Round".Translate(holder.LabelShort), holder, MessageTypeDefOf.NeutralEvent, false);
                if (r.CheckupOpen(now))
                {
                    needed = true;
                    if (Due("checkup", now, 1500))
                    {
                        int patientId = r.NextCheckupPatient(now, id => PatientAvailable(c, holder, id));
                        var patient = patientId == 0 ? null : Spawned(c, patientId);
                        if (patient != null) GovernanceMeetings.Start(holder, patient, WFL_StoryDefOf.WFL_Checkup, 0);
                    }
                }
            }
            r.NoteOfficeNeed(OfficeKind.Healer, needed, now);
        }

        private static bool PatientAvailable(FamilyComponent c, Pawn doctor, int id)
        {
            var p = Spawned(c, id);
            return p != null && p.Map == doctor.Map && Governance.Free(c, id) && Activities.IsFree(p) && p.RaceProps.Humanlike;
        }

        /// <summary>A cell beside a medical bed in an enclosed room, so the examination happens in the medical room when there is one.</summary>
        internal static bool MedicalRoomCell(Pawn doctor, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            var map = doctor?.Map;
            if (map == null) return false;
            foreach (var bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>().Where(b => b.Medical && !b.Destroyed).OrderBy(b => b.Position.DistanceToSquared(doctor.Position)).Take(6))
            {
                var room = bed.GetRoom();
                if (room == null || room.PsychologicallyOutdoors) continue;
                foreach (var near in GenAdj.CellsAdjacent8Way(bed))
                    if (near.InBounds(map) && near.Standable(map) && near.GetRoom(map) == room && doctor.CanReach(near, PathEndMode.OnCell, Danger.None)) { cell = near; return true; }
            }
            return false;
        }

        /// <summary>An examination done in person: the doctor tends anything that needs it, and the patient gets a lasting boost to recovery and immunity.</summary>
        internal static void CompleteCheckup(FamilyRegistry r, Pawn doctor, Pawn patient, int now)
        {
            if (!r.RecordCheckup(doctor.thingIDNumber, patient.thingIDNumber, now)) return;
            try
            {
                if (patient.health?.hediffSet != null && patient.health.HasHediffsNeedingTend()) TendUtility.DoTend(doctor, patient, null);
                var existing = patient.health?.hediffSet?.GetFirstHediffOfDef(WFL_HediffDefOf.WFL_CheckedUp);
                if (existing != null) patient.health.RemoveHediff(existing);
                if (WFL_HediffDefOf.WFL_CheckedUp != null) patient.health?.AddHediff(WFL_HediffDefOf.WFL_CheckedUp);
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Check-up effects skipped: " + ex.Message); }
            r.RecordTogether(doctor.thingIDNumber, patient.thingIDNumber, 1200, now, BondCause.Care);
            bool inRoom = patient.Position.GetRoom(patient.Map)?.ContainedBeds?.Any(b => b.Medical) == true;
            r.RecordOfficeWork(OfficeKind.Healer, doctor.thingIDNumber, GovernanceMeetings.NextToken(), now, .4f + .4f * SkillFor(doctor, OfficeKind.Healer) + (inRoom ? .2f : 0f));
        }

        // ------------------------------------------------------------------ the teacher

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

        private static void TeacherWork(FamilyComponent c, FamilyRegistry r, CommunityOffice office, Pawn holder, FamilySettings s, int now)
        {
            if (holder == null || holder.Map == null) return;
            int holderId = holder.thingIDNumber;
            bool adults = s != null && s.adultStudents;
            bool anyone = r.Persons.Any(p => p.InColony && p.Id != holderId && (p.Juvenile || adults));
            r.NoteOfficeNeed(OfficeKind.Teacher, anyone && (s == null || s.holdClasses), now);
            if (s != null && !s.holdClasses) return;

            if (now >= office.NextSessionTick && office.SessionQueue.Count == 0)
            {
                var students = r.Persons.Where(p => p.InColony && p.Id != holderId && (p.Juvenile || adults))
                    .Where(p => Spawned(c, p.Id) is Pawn pawn && pawn.Map == holder.Map && Governance.Free(c, p.Id) && LearnableSkill(holder, pawn) != null)
                    .Select(p => p.Id).Take(24).ToList();
                int every = Math.Max(1, Math.Min(10, s?.classEveryDays ?? 3)) * Day;
                if (r.BeginClass(now, every, students))
                    Messages.Message("WFL_Class_Begins".Translate(holder.LabelShort), holder, MessageTypeDefOf.NeutralEvent, false);
            }
            int next = r.NextStudent(now);
            if (next == 0) return;
            var student = Spawned(c, next);
            if (student == null || student.Map != holder.Map || !Governance.Free(c, next) || LearnableSkill(holder, student) == null) { r.ClassDone(next); return; }      // cannot come today
            if (Due("lesson", now, 600)) GovernanceMeetings.Start(holder, student, WFL_StoryDefOf.WFL_OfficeLesson, 0);
        }

        /// <summary>A lesson from the colony's teacher: a real conversation that ends with the student learning something the teacher is better at.</summary>
        internal static void CompleteLesson(FamilyRegistry r, Pawn teacher, Pawn student, int now)
        {
            var office = r.OfficeFor(OfficeKind.Teacher);
            if (office == null || office.HolderId != teacher.thingIDNumber || !office.SessionQueue.Contains(student.thingIDNumber)) return;
            var skill = LearnableSkill(teacher, student);
            if (skill != null) student.skills.Learn(skill, student.ageTracker.AgeBiologicalYears >= 18 ? 150f : 250f, true);
            r.ClassDone(student.thingIDNumber);
            r.RecordTogether(teacher.thingIDNumber, student.thingIDNumber, 1800, now, BondCause.Teaching);
            int gap = skill == null ? 0 : teacher.skills.GetSkill(skill).Level - student.skills.GetSkill(skill).Level;
            r.RecordOfficeWork(OfficeKind.Teacher, teacher.thingIDNumber, GovernanceMeetings.NextToken(), now, Math.Min(1f, .4f + gap / 10f));
        }
    }
}
