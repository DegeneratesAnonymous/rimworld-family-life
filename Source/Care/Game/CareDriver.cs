using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
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

    internal static class CareDriver
    {
        private const int Day = FamilyLife.Domain.ActivityTuning.TicksPerDay;
        private static readonly System.Collections.Generic.Dictionary<string, int> lastAct = new System.Collections.Generic.Dictionary<string, int>();
        private static bool Due(string key, int now, int gap) { if (lastAct.TryGetValue(key, out int last) && now >= last && now - last < gap) return false; lastAct[key] = now; return true; }
        private static Pawn Spawned(FamilyComponent c, int id) { var p = c.TryGetPawn(id); return p != null && p.Spawned && !p.Dead ? p : null; }
        private static bool Forced => FrameworkServices.Forced;
        private static bool Ready(Pawn p) => FrameworkServices.Ready(p);
        private static bool FreeFor(FamilyComponent c, int id) => FrameworkServices.FreeFor(c, id);
        private static string NeedsReason(Pawn p) => FrameworkServices.NeedsReason(p);
        private static void Served(FamilyRegistry r, OfficeKind kind, Pawn holder, Pawn served, int now) => FrameworkServices.Served(r, kind, holder, served, now);
        /// <summary>How fit a pawn is to be the doctor (0 to 1): skilled in Medicine, and not barred from doctoring.</summary>
        internal static float Fitness(Pawn p) => p == null || p.skills == null || p.Dead || p.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) ? 0f : FrameworkServices.SkillFraction(p, SkillDefOf.Medicine);
        /// <summary>A check-up lasts a few seconds at triple speed.</summary>
        internal const int CheckupTicks = 900;

        /// <summary>Who is seeing which patient and which bed is in use, so care workers never double up. Not saved.</summary>
        internal static readonly CareClaims Claims = new CareClaims();

        /// <summary>
        /// The benefit a visit gives: the recent-check-up condition at the quality's stage, never stacking and never reducing a better one (the same merge a routine check-up uses). Used by
        /// specialty visits, which are not part of the round. Nothing is tended, cured or immunised.
        /// </summary>
        internal static void GiveBenefit(Pawn patient, ResultQuality level, bool partial = false)
        {
            if (WFL_HediffDefOf.WFL_CheckedUp == null || patient?.health?.hediffSet == null) return;
            var existing = patient.health.hediffSet.GetFirstHediffOfDef(WFL_HediffDefOf.WFL_CheckedUp);
            var existingComp = existing?.TryGetComp<HediffComp_Disappears>();
            int ticks = (int)(1200000 * CheckupQuality.DurationFactor(level) * (partial ? CheckupQuality.PartialDuration : 1f));
            var update = CheckupQuality.Merge(existing != null, existing?.Severity ?? 0f, existingComp?.ticksToDisappear ?? 0, level, ticks);
            if (!update.Apply) return;
            var made = existing ?? patient.health.AddHediff(WFL_HediffDefOf.WFL_CheckedUp);
            if (made == null) return;
            made.Severity = update.Severity;
            var comp = made.TryGetComp<HediffComp_Disappears>(); if (comp != null) comp.ticksToDisappear = update.Remaining;
        }

        /// <summary>
        /// The routine work of a care worker who is not the one in the old working seat: the same order of priority (real medical work first) and the same shared round and daily
        /// budget, claiming each patient so nobody is seen twice.
        /// </summary>
        internal static void AssistantWork(FamilyComponent c, FamilyRegistry r, CommunityOffice office, Pawn worker, int now)
        {
            if (worker?.Map == null || !worker.Spawned || worker.Dead) return;
            var s = FamilyLifeMod.Settings; if (s != null && !s.healthChecks) return;
            if (!r.CheckupOpen(now) || CareDemand.DoctorShouldTend(worker, now) || ClinicVisits.Running(worker)) return;
            if (Due("checkup" + worker.thingIDNumber, now, 300) || Forced && Due("checkupT" + worker.thingIDNumber, now, 200)) RoutineRound(c, r, office, worker, now);
        }

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
                for (int i = 0; i < delta; i++) r.RecordOfficeWork(OfficeKind.Healer, holderId, FrameworkServices.NextToken(), now, .4f + .6f * Fitness(holder));
            }
            bool needed = holder.Map.mapPawns.FreeColonistsSpawned.Take(80).Any(p => HealthAIUtility.ShouldBeTendedNowByPlayer(p));
            if (s == null || s.healthChecks)
            {
                CareEvents.Healer(c, r, office, holder, now);          // a round is planned and begins by itself; an outbreak round begins at once
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
            var order = CareEvents.Priority(c, office);          // in an outbreak the sick come first
            // someone who cannot travel is visited, if they are willing
            int me = holder.thingIDNumber;
            int bedside = r.NextCheckupPatient(now, id => AppointmentBook.Bedside(id) && !AppointmentBook.Waiting(id, now) && !AppointmentBook.Deferred(id) && !r.HasRefused(id, now, me) && Spawned(c, id) != null && !Taken(id, me, now) && CareEvents.SickFirst(c, r, office, id), order, me);
            if (bedside != 0) { VisitInPlace(c, r, holder, bedside, now); return; }
            bool clinic = ClinicVisits.Spot(holder, out _, out _);
            int toAsk = r.NextCheckupPatient(now, id => CanBeSeen(c, holder, id) && !Taken(id, me, now) && ClinicVisits.Askable(r, id, now, me) && CareEvents.SickFirst(c, r, office, id), order, me);
            if (toAsk == 0) return;
            if (clinic) { ClinicVisits.TryOpen(holder); return; }
            VisitInPlace(c, r, holder, toAsk, now);          // no medical room: the doctor goes to them
        }

        /// <summary>A visit where the patient is. They are asked first; someone who declines is left alone, and someone busy is asked again later.</summary>
        private static void VisitInPlace(FamilyComponent c, FamilyRegistry r, Pawn holder, int patientId, int now)
        {
            var patient = Spawned(c, patientId); if (patient == null) return;
            var will = CareRules.Assess(c, holder, patient, now, out var why);
            if (will == PatientResponse.Refuses) { r.NoteRefusal(patientId, why.Kind, why.Reason, now, holder.thingIDNumber); return; }
            if (will == PatientResponse.Busy) { AppointmentBook.Busy(patientId, now); return; }
            // 'Unable' here means unwell: the visit is exactly what they need, so it goes ahead
            if (PawnReadinessApi.StopReason(holder, now) != null) return;
            if (!Claims.TryClaim(patientId, holder.thingIDNumber, now)) return;          // someone else has this patient
            CareerService.Event("care: " + holder.LabelShort + " claimed " + patient.LabelShort + " for a visit");
            if (!FrameworkServices.StartMeeting(holder, patient, CareDefOf.WFL_Checkup, 0)) { Claims.Release(patientId); AppointmentBook.Busy(patientId, now); }
        }

        /// <summary>This patient is already being seen (or about to be) by another care worker.</summary>
        internal static bool Taken(int patient, int me, int now) { int by = Claims.HeldBy(patient, now); return by != 0 && by != me; }

        /// <summary>A recent check-up is still doing them good, so they are not asked to come again yet.</summary>
        internal static bool RecentCheckup(Pawn p) => p?.health?.hediffSet != null && WFL_HediffDefOf.WFL_CheckedUp != null && p.health.hediffSet.HasHediff(WFL_HediffDefOf.WFL_CheckedUp);

        /// <summary>Takes anyone with a recent check-up off the doctor's list for this round, and closes the round if that leaves nobody.</summary>
        internal static void SkipRecentlyChecked(FamilyComponent c, FamilyRegistry r, CommunityOffice office, int now)
        {
            if (office == null || !r.CheckupOpen(now)) return;
            foreach (var person in r.Persons)
            {
                if (!person.InColony || person.Id == office.HolderId || office.Care().Examined.Contains(person.Id)) continue;
                var p = c.TryGetPawn(person.Id);
                if (p != null && RecentCheckup(p)) office.Care().Examined.Add(person.Id);
            }
            if (r.Persons.Where(x => x.InColony && x.Id != office.HolderId).All(x => office.Care().Examined.Contains(x.Id))) office.Care().RoundStartTick = 0;
        }

        /// <summary>
        /// The post changed hands. The outgoing doctor's clinic closes (the patient being seen is released), and they stop scheduling because the post no longer lists them. The community's
        /// care record (the round in progress, who was seen, who declined) is kept: the next doctor continues the round if there is one. Their own working state starts fresh.
        /// </summary>
        internal static void OnHealerChanged(FamilyComponent c, CommunityOffice office, int oldHolder, int newHolder)
        {
            c.Registry.LapseHealerRefusals(id => c.Registry.IsPractitioner(id) || id == newHolder);          // people who would not see the former doctor are asked afresh; those who declined check-ups themselves are still left alone
            if (oldHolder == 0) return;
            var old = c.TryGetPawn(oldHolder);
            Claims.ReleaseAll(oldHolder);
            if (old != null) { ClinicVisits.Close(old); if (old.CurJobDef == CareDefOf.WFL_ClinicDoctor) old.jobs.EndCurrentJob(JobCondition.InterruptForced, false); }
        }

        /// <summary>
        /// Once, when a game starts or loads with Community Care running: clinic sessions and appointment gaps are not saved, so they start empty; and the saved care record is checked
        /// against who is actually here, dropping people who are gone so nobody is waited for who cannot come.
        /// </summary>
        internal static void Reconcile(FamilyComponent c)
        {
            ClinicVisits.Reset(); CareDemand.Reset(); Claims.Clear(); PractitionerLoad.Clear();
            var office = c.Registry.OfficeFor(OfficeKind.Healer);
            if (office == null) return;
            var r = c.Registry; var care = office.CareRecord();
            bool Here(int id) => r.GetPerson(id) != null;
            care.Examined = care.Examined.Where(Here).ToList();
            care.Missed = care.Missed.Where(Here).ToList();
            r.ReconcileRefusals(FrameworkServices.Now, id => r.IsPractitioner(id) || id == office.HolderId);          // drops refusals about people or doctors who are gone, ones that have expired, and any damaged entry
        }

        internal static bool CanBeSeen(FamilyComponent c, Pawn doctor, int id) => !Skipped(id, FrameworkServices.Now) && PatientAvailable(c, doctor, id);
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
            Claims.Release(patient.thingIDNumber);          // whatever happens next (the round may have closed meanwhile), this visit no longer holds the patient or the worker
            CareerService.Event("care: " + doctor.LabelShort + " finished seeing " + patient.LabelShort);
            if (!r.RecordCheckup(doctor.thingIDNumber, patient.thingIDNumber, now)) return;
            AppointmentBook.Seen(patient.thingIDNumber);
            r.NoteVisit(now, doctor.thingIDNumber);
            ResultQuality level = ResultQuality.Fair;
            try
            {
                if (!inClinic)          // a visit where the patient is: one word with them, in a room with less to work with
                    talkSum += FrameworkServices.RunConversation(doctor, patient, OfficeKind.Healer, SkillDefOf.Medicine, ExchangeTopic.Reassure, doctor.thingIDNumber + patient.thingIDNumber + now, CareRules.Modifiers(doctor, bed, false)).Quality;
                float mods = CareRules.Modifiers(doctor, bed, inClinic);
                level = CheckupQuality.Cap(CheckupQuality.Level(CheckupQuality.Score(PawnReadinessApi.Level(doctor, SkillDefOf.Medicine), PawnReadinessApi.Level(doctor, SkillDefOf.Social), mods, talkSum, Rand.RangeInclusive(-2, 2))), partial);
                if (!r.Careers().IsQualified(doctor.thingIDNumber, CareRoles.Career) && level > ResultQuality.Fair) level = ResultQuality.Fair;          // entry-level care gives routine, not advanced, check-ups
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
                if (talkSum <= -1f) FrameworkServices.GainMemory(patient, "WFL_DismissiveCheckup", doctor);
                else if (talkSum >= 1f) FrameworkServices.GainMemory(patient, "WFL_ReassuringCheckup", doctor);
                if (OfficeCareView.MayKnowResult(patient, doctor))
                    MoteMaker.ThrowText(patient.DrawPos, patient.Map, "WFL_Checkup_Result".Translate(("WFL_Quality_" + level).Translate()), level == ResultQuality.Poor ? new UnityEngine.Color(1f, .78f, .62f) : level == ResultQuality.Excellent ? new UnityEngine.Color(.72f, 1f, .72f) : UnityEngine.Color.white);
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Check-up effects skipped: " + ex.Message); }
            Claims.Release(patient.thingIDNumber);
            CareCareers.Report(FamilyComponent.Current, doctor, patient, level, now);
            r.NoteCareQuality(patient.thingIDNumber, level);
            Served(r, OfficeKind.Healer, doctor, patient, now);
            r.RecordOfficeWork(OfficeKind.Healer, doctor.thingIDNumber, FrameworkServices.NextToken(), now, .35f + .15f * Fitness(doctor) + .1f * (int)level);
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
                    lines.Add("  medical need on the map: " + CareDemand.MedicalNeed(d.Map, now) + (CareDemand.DoctorShouldTend(d, now) ? " (routine visits wait)" : "") + "; visits today " + r.RoutineVisitsToday(now) + " of " + CheckupWorkload.VisitsPerDay(healer.Care().IllnessRound) + "; declined: " + healer.Care().Refusals.Count(x => x.Kind == CareRefusalKind.ServiceWide) + " check-ups, " + healer.Care().Refusals.Count(x => x.Kind == CareRefusalKind.HealerSpecific) + " this doctor; missed last round: " + healer.Care().Missed.Count);
                    lines.Add("  patients able to come now: " + able + ", already seen this round: " + healer.Care().Examined.Count);
                }
            }
            return lines;
        }
    }
}
