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
            Player = id => Governance.IsAvatar(c, id),
            Passion = (id, kind) => PassionFor(c.TryGetPawn(id), kind)
        };

        /// <summary>Whether the pawn burns for the skills the post uses: a calling, which is the clearest reason to want it.</summary>
        internal static bool PassionFor(Pawn p, OfficeKind kind)
        {
            if (p?.skills == null) return false;
            bool Burns(SkillDef def) { var s = p.skills.GetSkill(def); return s != null && !s.TotallyDisabled && s.passion != Passion.None; }
            switch (kind)
            {
                case OfficeKind.Healer: return Burns(SkillDefOf.Medicine);
                case OfficeKind.Teacher: return Burns(SkillDefOf.Intellectual) || Burns(SkillDefOf.Social);
                case OfficeKind.HallKeeper: return Burns(SkillDefOf.Cooking) || Burns(SkillDefOf.Social);
                default: return false;
            }
        }

        /// <summary>
        /// Once a day: the holder feels how the post is going (trusted when they do it well, let down when they do not), and the rest of their house feels
        /// pride in kin who holds a post worth having. These are the pawn's and the family's own stake in a post, not a penalty for an empty one.
        /// </summary>
        private static void Esteem(FamilyComponent c, FamilyRegistry r, int now)
        {
            if (!Due("esteem", now, Day)) return;
            foreach (var office in r.Offices.Where(o => o.Held).ToList())
            {
                var holder = Spawned(c, office.HolderId);
                if (holder == null) continue;
                float grade = r.OfficeGrade(office.Kind);
                bool appointed = FamilyRegistry.IsAppointed(office.Kind);
                bool neglecting = office.UnmetSinceTick != 0 && now - office.UnmetSinceTick > 3 * Day;       // work was needed and is not being done
                string mood = appointed || grade >= .65f ? "WFL_PostTrusted" : grade >= .35f || !neglecting && office.Work == 0 ? "WFL_PostAdequate" : "WFL_PostFailing";
                Activities.GainMemory(holder, mood, null);
                if (!appointed && grade < .5f && office.Work > 0) continue;
                foreach (var kin in r.MembersOf(office.HolderFamily).Where(m => m.InColony && m.Id != office.HolderId).Take(12))
                    if (Spawned(c, kin.Id) is Pawn k) Activities.GainMemory(k, "WFL_KinInPost", holder);
            }
        }

        /// <summary>The person served by one of their own house's post-holders feels it, and the holder earns the trust of anyone they serve.</summary>
        internal static void Served(FamilyRegistry r, OfficeKind kind, Pawn holder, Pawn served, int now)
        {
            r.ThankPost(kind, holder.thingIDNumber, served.thingIDNumber, now);
            if (r.KinOfHolder(served.thingIDNumber, kind)) Activities.GainMemory(served, "WFL_ServedByKin", holder);
        }

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
            Esteem(c, r, now);
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

        private static readonly Dictionary<int, int> skipUntil = new Dictionary<int, int>();

        private static bool Skipped(int person, int now) => skipUntil.TryGetValue(person, out int until) && now < until;
        private static void SkipFor(int person, int now, int ticks) { skipUntil[person] = now + ticks; if (skipUntil.Count > 200) skipUntil.Clear(); }

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
                OfficeEvents.Healer(c, r, office, holder, now);          // a round, or one for an outbreak, is offered to the player; it begins by itself if they leave it
                if (r.CheckupOpen(now)) SkipRecentlyChecked(c, r, office, now);
                if (r.CheckupOpen(now))
                {
                    needed = true;
                    // The round is a list that lasts until everyone has been seen. The doctor works down it whenever they are free: a meal, sleep, the toilet or a gathering
                    // pauses it, and it is taken up again afterwards.
                    if (ClinicVisits.Running(holder)) { }
                    else if (Due("checkup", now, 300) || Forced && Due("checkupT", now, 200))
                    {
                        bool clinic = ClinicVisits.Spot(holder, out _, out _);
                        // anyone still to be seen who can be asked to come to the clinic now (or, with no clinic, anyone at all)
                        var order = OfficeEvents.Priority(c, office);          // in an outbreak the sick come first
                        int toCome = r.NextCheckupPatient(now, id => CanBeSeen(c, holder, id) && !CheckupQueue.Waiting(id, now) && (!clinic || !CheckupQueue.OnTheSpot(id)), order);
                        if (clinic && toCome != 0) ClinicVisits.TryOpen(holder);
                        else
                        {
                            // Nobody can be brought to the clinic: the next person who could not be brought is seen where they are, as soon as they are free.
                            int where = clinic ? r.NextCheckupPatient(now, id => CanBeSeen(c, holder, id) && CheckupQueue.OnTheSpot(id) && !CheckupQueue.Waiting(id, now), order) : toCome;
                            var patient = where == 0 ? null : Spawned(c, where);
                            if (patient != null && !NeedsAttention(holder) && !GovernanceMeetings.Start(holder, patient, WFL_StoryDefOf.WFL_Checkup, 0)) { if (clinic) CheckupQueue.Failed(where, now); else SkipFor(where, now, 6000); }
                        }
                    }
                }
            }
            r.NoteOfficeNeed(OfficeKind.Healer, needed, now);
        }

        /// <summary>Set by the developer menu so a test is not held up by who happens to be busy: people only need to be undrafted and not in a mental break.</summary>
        internal static int ForceUntil;
        internal static bool Ready(Pawn p) => p != null && (Activities.IsFree(p) || (Activities.Now < ForceUntil && p.Spawned && !p.Dead && !p.Downed && !p.Drafted && !p.InMentalState && !p.IsPrisoner && p.RaceProps.Humanlike && !p.DevelopmentalStage.Baby()));

        internal static bool Forced => Activities.Now < ForceUntil;

        /// <summary>Awake enough and able: not drafted, hurt, in a break or a prisoner. Used when the player asks for something, which does not wait for the pawn to be idle.</summary>
        internal static bool Available(Pawn p) => p != null && p.Spawned && !p.Dead && !p.Downed && !p.Drafted && !p.InMentalState && !p.IsPrisoner && p.RaceProps.Humanlike && !p.DevelopmentalStage.Baby() && p.Awake();

        /// <summary>A recent check-up is still doing them good, so they are not asked to come again yet.</summary>
        internal static bool RecentCheckup(Pawn p) => p?.health?.hediffSet != null && WFL_HediffDefOf.WFL_CheckedUp != null && p.health.hediffSet.HasHediff(WFL_HediffDefOf.WFL_CheckedUp);

        /// <summary>Takes anyone with a recent check-up off the doctor's list for this round, and closes the round if that leaves nobody.</summary>
        internal static void SkipRecentlyChecked(FamilyComponent c, FamilyRegistry r, CommunityOffice office, int now)
        {
            if (office == null || !r.CheckupOpen(now)) return;
            foreach (var person in r.Persons)
            {
                if (!person.InColony || person.Id == office.HolderId || office.Examined.Contains(person.Id)) continue;
                var p = c.TryGetPawn(person.Id);
                if (p != null && RecentCheckup(p)) office.Examined.Add(person.Id);
            }
            if (r.Persons.Where(x => x.InColony && x.Id != office.HolderId).All(x => office.Examined.Contains(x.Id))) office.RoundStartTick = 0;
        }

        private static readonly string[] UrgentNeeds = { "Food", "Rest", "Bladder", "Hygiene" };

        /// <summary>
        /// Someone who should stop what they are doing: hungry, tired, in need of the toilet or a wash, or called away into a gathering. A check-up or a class stops for
        /// them and is taken up again afterwards, rather than holding them to it.
        /// </summary>
        internal static bool NeedsAttention(Pawn p) => NeedsReason(p) != null;

        /// <summary>Why this person should stop (a need, or an event they have been called to), or null.</summary>
        internal static string NeedsReason(Pawn p)
        {
            if (p == null || p.Dead) return null;
            try
            {
                var needs = p.needs?.AllNeeds;
                if (needs != null)
                    for (int i = 0; i < needs.Count; i++)
                    {
                        var n = needs[i];
                        if (n?.def != null && UrgentNeeds.Contains(n.def.defName) && n.CurLevelPercentage < .15f) return n.def.defName + " " + n.CurLevelPercentage.ToString("0.00");
                    }
                var lord = p.GetLord();
                if (lord?.LordJob is LordJob_VoluntarilyJoinable) return "called to an event";
            }
            catch (Exception) { }
            return null;
        }

        /// <summary>Written to the log during a developer test only, so a flow that does not start says why.</summary>
        internal static void Trace(string text) { if (Forced) Log.Message("[Wil's Family Life] test: " + text); }

        /// <summary>What stands between the doctor and a clinic, and the teacher and a class, in plain lines. Used by the developer menu.</summary>
        internal static List<string> Diagnose(FamilyComponent c, int now, OfficeKind? only = null)
        {
            var lines = new List<string>();
            var r = c.Registry;
            var healer = r.OfficeFor(OfficeKind.Healer); var teacher = r.OfficeFor(OfficeKind.Teacher);
            var settings = FamilyLifeMod.Settings;
            if (only == OfficeKind.Teacher) { }
            else if (healer == null || !healer.Held) lines.Add("Doctor: nobody holds the post. Run 'Choose the colony's posts now' first.");
            else
            {
                var d = Spawned(c, healer.HolderId);
                if (d == null) lines.Add("Doctor: the holder is not on the map.");
                else
                {
                    lines.Add("Doctor: " + d.LabelShort + (settings != null && !settings.healthChecks ? " (check-ups are switched off in the mod settings)" : ""));
                    lines.Add("  round open: " + r.CheckupOpen(now) + (ClinicVisits.Running(d) ? ", clinic already running" : ""));
                    if (ClinicVisits.Spot(d, out var bed, out var cell)) lines.Add("  clinic: " + bed.LabelCap + " at " + bed.Position + ", the doctor waits at " + cell);
                    else lines.Add("  clinic: no free medical bed in an enclosed room the doctor can reach (visits will be one to one instead)");
                    int able = r.Persons.Count(p => p.InColony && p.Id != healer.HolderId && PatientAvailable(c, d, p.Id));
                    if (NeedsReason(d) != null) lines.Add("  the doctor should stop for: " + NeedsReason(d));
                    lines.Add("  patients able to come now: " + able + ", already seen this round: " + healer.Examined.Count);
                }
            }
            if (only == OfficeKind.Healer) { }
            else if (teacher == null || !teacher.Held) lines.Add("Teacher: nobody holds the post. Run 'Choose the colony's posts now' first.");
            else
            {
                var t = Spawned(c, teacher.HolderId);
                if (t == null) lines.Add("Teacher: the holder is not on the map.");
                else
                {
                    lines.Add("Teacher: " + t.LabelShort + (settings != null && !settings.holdClasses ? " (classes are switched off in the mod settings)" : ""));
                    if (ClassRooms.TeachingSpot(t, out var cellT, out var room, out var front)) lines.Add("  classroom: " + front.LabelCap + " at " + front.Position + ", the teacher stands at " + cellT + ", room of " + room.CellCount + " cells");
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

        /// <summary>Runs the posts' work now, rather than at the next check.</summary>
        internal static void WorkNow(FamilyComponent c, int now, OfficeKind? only = null)
        {
            foreach (var office in c.Registry.Offices.Where(o => o.Held && (only == null || o.Kind == only)).ToList())
            {
                try { Work(c, c.Registry, office, now); }
                catch (Exception ex) { Log.Warning("[Wil's Family Life] test: post '" + office.Kind + "' failed: " + ex); }
            }
        }
        /// <summary>Not drafted, hurt or in a break; and, outside a developer test, not being played or selected.</summary>
        internal static bool FreeFor(FamilyComponent c, int id)
        {
            var p = c.TryGetPawn(id);
            if (Forced && p != null) return !p.Drafted && !p.Downed && !p.InMentalState && !p.IsPrisoner;
            return Governance.Free(c, id);
        }

        internal static bool CanBeSeen(FamilyComponent c, Pawn doctor, int id) => !Skipped(id, Activities.Now) && PatientAvailable(c, doctor, id);
        internal static void SkipPatient(int id, int now) => SkipFor(id, now, 6000);

        private static bool PatientAvailable(FamilyComponent c, Pawn doctor, int id)
        {
            var p = Spawned(c, id);
            return p != null && p.Map == doctor.Map && FreeFor(c, id) && Ready(p) && p.RaceProps.Humanlike;
        }

        /// <summary>A cell beside a medical bed in an enclosed room, so the examination happens in the medical room when there is one.</summary>
        internal static bool MedicalRoomCell(Pawn doctor, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            var map = doctor?.Map;
            if (map == null) return false;
            // The hospital: a room with a medical bed, or one the game itself calls a hospital. Dangerous-but-passable cells are fine; the doctor must simply be able to get there.
            foreach (var bed in map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>().Where(b => !b.Destroyed && !b.ForPrisoners && (b.Medical || b.GetRoom()?.Role == RoomRoleDefOf.Hospital))
                         .OrderBy(b => b.Position.DistanceToSquared(doctor.Position)).Take(8))
            {
                var room = bed.GetRoom();
                if (room == null || room.PsychologicallyOutdoors) continue;
                foreach (var near in GenAdj.CellsAdjacent8Way(bed))
                    if (near.InBounds(map) && near.Standable(map) && near.GetRoom(map) == room && doctor.CanReach(near, PathEndMode.OnCell, Danger.Some)) { cell = near; return true; }
            }
            return false;
        }

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

        /// <summary>An examination done in person: the doctor tends anything that needs it, and the patient gets a lasting boost to recovery and immunity.</summary>
        internal static void CompleteCheckup(FamilyRegistry r, Pawn doctor, Pawn patient, int now, float talkQuality = 0f)
        {
            if (!r.RecordCheckup(doctor.thingIDNumber, patient.thingIDNumber, now)) return;
            CheckupQueue.Seen(patient.thingIDNumber);
            try
            {
                if (patient.health?.hediffSet != null && patient.health.HasHediffsNeedingTend()) TendUtility.DoTend(doctor, patient, null);
                var existing = patient.health?.hediffSet?.GetFirstHediffOfDef(WFL_HediffDefOf.WFL_CheckedUp);
                if (existing != null) patient.health.RemoveHediff(existing);
                if (WFL_HediffDefOf.WFL_CheckedUp != null)
                {
                    // The doctor's check: Medicine and Social, the talk, and the day. The quality sets how strong the benefit is and how long it lasts.
                    int med = doctor.skills?.GetSkill(SkillDefOf.Medicine) is SkillRecord ms && !ms.TotallyDisabled ? ms.Level : 0;
                    int soc = doctor.skills?.GetSkill(SkillDefOf.Social) is SkillRecord ss && !ss.TotallyDisabled ? ss.Level : 0;
                    var level = CheckupQuality.Level(CheckupQuality.Score(med, soc, talkQuality, Rand.RangeInclusive(1, 20)));
                    var made = patient.health?.AddHediff(WFL_HediffDefOf.WFL_CheckedUp);
                    if (made != null) made.Severity = CheckupQuality.Severity(level);
                    var comp = made?.TryGetComp<HediffComp_Disappears>();
                    if (comp != null)
                    {
                        float duration = CheckupQuality.DurationFactor(level);
                        if (r.KinOfHolder(patient.thingIDNumber, OfficeKind.Healer)) duration *= 2f;          // the doctor looks after their own house better
                        comp.ticksToDisappear = (int)(comp.ticksToDisappear * duration);
                    }
                    if (level == ResultQuality.Poor) Activities.GainMemory(patient, "WFL_DismissiveCheckup", doctor);
                    else if (level >= ResultQuality.Good) Activities.GainMemory(patient, "WFL_ReassuringCheckup", doctor);
                    MoteMaker.ThrowText(patient.DrawPos, patient.Map, "WFL_Checkup_Result".Translate(("WFL_Quality_" + level).Translate()), level == ResultQuality.Poor ? new UnityEngine.Color(1f, .78f, .62f) : level == ResultQuality.Excellent ? new UnityEngine.Color(.72f, 1f, .72f) : UnityEngine.Color.white);
                }
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Check-up effects skipped: " + ex.Message); }
            r.RecordTogether(doctor.thingIDNumber, patient.thingIDNumber, 1200, now, BondCause.Care);
            Served(r, OfficeKind.Healer, doctor, patient, now);
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

            OfficeEvents.Teacher(c, r, office, holder, now);          // the annual testing of the children, offered in Aprimay; classes go on whatever else is happening
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
                if (office.SessionQueue.Count > 0 && (Due("classroom", now, 600) || Forced && Due("classroomT", now, 200))) { r.NextStudent(now); if (office.SessionQueue.Count > 0) ClassSessions.TryBegin(c, holder, office.SessionQueue.ToList(), now); }
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

        /// <summary>A lesson lasts two game hours; a check-up a few seconds at triple speed.</summary>
        internal const int LessonTicks = 5000, CheckupTicks = 900;

        /// <summary>A lesson from the colony's teacher: a real conversation that ends with the student learning something the teacher is better at.</summary>
        internal static void CompleteLesson(FamilyRegistry r, Pawn teacher, Pawn student, int now, int ticks = LessonTicks, float factor = 1f)
        {
            var office = r.OfficeFor(OfficeKind.Teacher);
            if (office == null || office.HolderId != teacher.thingIDNumber || !office.SessionQueue.Contains(student.thingIDNumber)) return;
            var skill = LearnableSkill(teacher, student) ?? Activities.BestSkillOf(teacher, student)?.def;
            float xp = student.ageTracker.AgeBiologicalYears >= 18 ? 450f : 700f;          // a lesson now lasts hours, not seconds
            if (r.KinOfHolder(student.thingIDNumber, OfficeKind.Teacher)) xp *= 1.5f;          // the teacher's own house learns more from them
            xp *= factor;          // how well the teacher got on with this pupil
            if (skill != null) student.skills.Learn(skill, xp, true);
            Served(r, OfficeKind.Teacher, teacher, student, now);
            r.ClassDone(student.thingIDNumber);
            r.RecordTogether(teacher.thingIDNumber, student.thingIDNumber, ticks, now, BondCause.Teaching);
            int gap = skill == null ? 0 : teacher.skills.GetSkill(skill).Level - student.skills.GetSkill(skill).Level;
            r.RecordOfficeWork(OfficeKind.Teacher, teacher.thingIDNumber, GovernanceMeetings.NextToken(), now, Math.Min(1f, .4f + gap / 10f));
        }
    }
}
