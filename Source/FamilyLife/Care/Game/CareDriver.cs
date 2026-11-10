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
    // The doctor's side of the community posts. Owner: Community Care.

    [DefOf]
    public static class WFL_HediffDefOf
    {
        public static HediffDef WFL_CheckedUp;

        static WFL_HediffDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(WFL_HediffDefOf));
    }

    internal static partial class OfficeDriver
    {

        private static readonly Dictionary<int, int> skipUntil = new Dictionary<int, int>();

        private static bool Skipped(int person, int now) => skipUntil.TryGetValue(person, out int until) && now < until;
        private static void SkipFor(int person, int now, int ticks) { skipUntil[person] = now + ticks; if (skipUntil.Count > 200) skipUntil.Clear(); }

        internal static void HealerWork(FamilyComponent c, FamilyRegistry r, CommunityOffice office, Pawn holder, FamilySettings s, int now)
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
                OfficeEvents.Healer(c, r, office, holder, now);          // a round is planned and begins by itself; an outbreak round begins at once
                if (r.CheckupOpen(now)) SkipRecentlyChecked(c, r, office, now);
                if (r.CheckupOpen(now))
                {
                    needed = true;
                    // Work comes in this order: emergency treatment, ongoing care, an outbreak assessment, then routine check-ups. Anything that needs tending stops the
                    // routine work here (the clinic job also ends by itself), and the doctor's own work priorities take over until it is done.
                    if (CareDemand.DoctorShouldTend(holder, now)) { }
                    else if (ClinicVisits.Running(holder)) { }
                    else if (Due("checkup", now, 300) || Forced && Due("checkupT", now, 200)) RoutineRound(c, r, office, holder, now);
                }
            }
            r.NoteOfficeNeed(OfficeKind.Healer, needed, now);
        }

        /// <summary>
        /// Starts the next routine visit, if the day's hours and budget allow. Someone who cannot come is visited where they are; otherwise the doctor opens the clinic and asks
        /// people in one at a time. Nobody who declined, is busy or is waiting out a gap is asked.
        /// </summary>
        private static void RoutineRound(FamilyComponent c, FamilyRegistry r, CommunityOffice office, Pawn holder, int now)
        {
            if (!ClinicVisits.Allowed(r, office, holder, now)) return;
            var order = OfficeEvents.Priority(c, office);          // in an outbreak the sick come first
            // someone who cannot travel is visited, if they are willing
            int bedside = r.NextCheckupPatient(now, id => AppointmentBook.Bedside(id) && !AppointmentBook.Waiting(id, now) && !AppointmentBook.Deferred(id) && !r.HasRefused(id, now) && Spawned(c, id) != null && OfficeEvents.SickFirst(c, r, office, id), order);
            if (bedside != 0) { VisitInPlace(c, r, holder, bedside, now); return; }
            bool clinic = ClinicVisits.Spot(holder, out _, out _);
            int toAsk = r.NextCheckupPatient(now, id => CanBeSeen(c, holder, id) && ClinicVisits.Askable(r, id, now) && OfficeEvents.SickFirst(c, r, office, id), order);
            if (toAsk == 0) return;
            if (clinic) { ClinicVisits.TryOpen(holder); return; }
            VisitInPlace(c, r, holder, toAsk, now);          // no medical room: the doctor goes to them
        }

        /// <summary>A visit where the patient is. They are asked first; someone who declines is left alone, and someone busy is asked again later.</summary>
        private static void VisitInPlace(FamilyComponent c, FamilyRegistry r, Pawn holder, int patientId, int now)
        {
            var patient = Spawned(c, patientId); if (patient == null) return;
            var will = CareRules.Assess(c, holder, patient, now);
            if (will == PatientResponse.Refuses) { r.NoteRefusal(patientId, now); return; }
            if (will == PatientResponse.Busy) { AppointmentBook.Busy(patientId, now); return; }
            // 'Unable' here means unwell: the visit is exactly what they need, so it goes ahead
            if (PawnReadiness.StopReason(holder, now) != null) return;
            if (!GovernanceMeetings.Start(holder, patient, WFL_StoryDefOf.WFL_Checkup, 0)) AppointmentBook.Busy(patientId, now);
        }

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

        /// <summary>
        /// An examination done in person. The benefit is modest and bounded: a recent-check-up condition whose stage follows the quality, which never stacks, never lets a later poor
        /// visit reduce or refresh a better one, and lasts about twenty days. A conversation does not tend, cure or immunise anyone; if the patient needs tending, tending comes first
        /// and this waits. A visit cut short (<paramref name="partial"/>) is a Fair one at most with half the time.
        /// </summary>
        internal static void CompleteCheckup(FamilyRegistry r, Pawn doctor, Pawn patient, int now, float talkSum = 0f, bool partial = false, Building_Bed bed = null, bool inClinic = false)
        {
            if (!r.RecordCheckup(doctor.thingIDNumber, patient.thingIDNumber, now)) return;
            AppointmentBook.Seen(patient.thingIDNumber);
            r.NoteVisit(now);
            ResultQuality level = ResultQuality.Fair;
            try
            {
                if (!inClinic)          // a visit where the patient is: one word with them, in a room with less to work with
                    talkSum += Conversations.Run(doctor, patient, OfficeKind.Healer, SkillDefOf.Medicine, ExchangeTopic.Reassure, doctor.thingIDNumber + patient.thingIDNumber + now, CareRules.Modifiers(doctor, bed, false)).Quality;
                float mods = CareRules.Modifiers(doctor, bed, inClinic);
                level = CheckupQuality.Cap(CheckupQuality.Level(CheckupQuality.Score(PawnReadiness.Level(doctor, SkillDefOf.Medicine), PawnReadiness.Level(doctor, SkillDefOf.Social), mods, talkSum, Rand.RangeInclusive(-2, 2))), partial);
                if (WFL_HediffDefOf.WFL_CheckedUp != null && patient.health?.hediffSet != null)
                {
                    var existing = patient.health.hediffSet.GetFirstHediffOfDef(WFL_HediffDefOf.WFL_CheckedUp);
                    var existingComp = existing?.TryGetComp<HediffComp_Disappears>();
                    int baseTicks = 1200000;          // twenty days; the definition's own length
                    int ticks = (int)(baseTicks * CheckupQuality.DurationFactor(level) * (partial ? CheckupQuality.PartialDuration : 1f));
                    var update = CheckupQuality.Merge(existing != null, existing?.Severity ?? 0f, existingComp?.ticksToDisappear ?? 0, level, ticks);
                    if (update.Apply)
                    {
                        var made = existing ?? patient.health.AddHediff(WFL_HediffDefOf.WFL_CheckedUp);
                        if (made != null)
                        {
                            made.Severity = update.Severity;
                            var comp = made.TryGetComp<HediffComp_Disappears>();
                            if (comp != null) comp.ticksToDisappear = update.Remaining;
                        }
                    }
                }
                if (talkSum <= -1f) Activities.GainMemory(patient, "WFL_DismissiveCheckup", doctor);
                else if (talkSum >= 1f) Activities.GainMemory(patient, "WFL_ReassuringCheckup", doctor);
                if (OfficeCareView.MayKnowResult(patient, doctor))
                    MoteMaker.ThrowText(patient.DrawPos, patient.Map, "WFL_Checkup_Result".Translate(("WFL_Quality_" + level).Translate()), level == ResultQuality.Poor ? new UnityEngine.Color(1f, .78f, .62f) : level == ResultQuality.Excellent ? new UnityEngine.Color(.72f, 1f, .72f) : UnityEngine.Color.white);
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Check-up effects skipped: " + ex.Message); }
            r.NoteCareQuality(patient.thingIDNumber, level);
            Served(r, OfficeKind.Healer, doctor, patient, now);
            r.RecordOfficeWork(OfficeKind.Healer, doctor.thingIDNumber, GovernanceMeetings.NextToken(), now, .35f + .15f * SkillFor(doctor, OfficeKind.Healer) + .1f * (int)level);
        }


        internal static List<string> DiagnoseHealer(FamilyComponent c, int now)
        {
            var lines = new List<string>();
            var r = c.Registry;
            var healer = r.OfficeFor(OfficeKind.Healer);
            var settings = FamilyLifeMod.Settings;
            if (healer == null || !healer.Held) lines.Add("Doctor: nobody holds the post. Run 'Choose the colony's posts now' first.");
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
                    lines.Add("  medical need on the map: " + CareDemand.MedicalNeed(d.Map, now) + (CareDemand.DoctorShouldTend(d, now) ? " (routine visits wait)" : "") + "; visits today " + r.RoutineVisitsToday(now) + " of " + CheckupWorkload.VisitsPerDay(healer.IllnessRound) + "; declined this round: " + healer.Refused.Count + ", missed last round: " + healer.Missed.Count);
                    lines.Add("  patients able to come now: " + able + ", already seen this round: " + healer.Examined.Count);
                }
            }
            return lines;
        }
    }
}
