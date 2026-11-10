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
    // The doctor's routine check-ups. Medical work comes first: emergency treatment, then ongoing care, then an outbreak assessment, then a routine round - the clinic closes the
    // moment someone needs tending and opens again afterwards. A patient is asked, never made to come: someone busy is asked again later (a growing gap), someone who declines is
    // left alone for several days, and someone too ill or hurt to come is visited where they are. Nothing here is saved except what the post's record keeps (who has been seen, who
    // declined, who was missed); after a load the doctor simply starts down the list again.

    internal enum ClinicPhase : byte { Waiting, Arriving, Examining }

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

    /// <summary>When each person on the list is next asked, and who is to be visited where they are. Not saved.</summary>
    internal static class AppointmentBook
    {
        private sealed class Entry { public int Tries, RetryAt; public bool Bedside, Deferred; }
        private static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();

        internal static void Reset() => entries.Clear();
        private static Entry Get(int id) { if (!entries.TryGetValue(id, out var e)) entries[id] = e = new Entry(); return e; }
        internal static bool Waiting(int id, int now) => entries.TryGetValue(id, out var e) && now < e.RetryAt;
        internal static bool Bedside(int id) => entries.TryGetValue(id, out var e) && e.Bedside;
        internal static bool Deferred(int id) => entries.TryGetValue(id, out var e) && e.Deferred;
        internal static void Seen(int id) => entries.Remove(id);

        /// <summary>Busy or unable to arrive: asked again later, with a longer gap each time; after a few tries they are simply carried to the next round, with no mark against them.</summary>
        internal static void Busy(int id, int now)
        {
            var e = Get(id); e.Tries++;
            e.RetryAt = now + AppointmentBackoff.RetryAfter(e.Tries);
            if (AppointmentBackoff.GiveUp(e.Tries)) e.Deferred = true;
        }

        /// <summary>Too unwell or hurt to come: visited where they are, when the doctor is free and they are willing.</summary>
        internal static void NeedsVisit(int id, int now) { var e = Get(id); e.Bedside = true; e.RetryAt = Math.Max(e.RetryAt, now + 600); }
        internal static void Reschedule(int id, int now, int gap) { var e = Get(id); e.RetryAt = now + gap; }
    }

    internal static class ClinicVisits
    {
        /// <summary>The conversation between doctor and patient, in game ticks (about twenty-five seconds at normal speed). A patient is given a game hour to arrive.</summary>
        internal const int TalkTicks = CheckupWorkload.VisitTicks, ArriveTicks = 2500;
        private static readonly Dictionary<int, ClinicSession> byDoctor = new Dictionary<int, ClinicSession>();

        internal static void Reset() { byDoctor.Clear(); AppointmentBook.Reset(); }
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

        /// <summary>Opens the clinic: the doctor goes to the medical room and waits for patients. False when there is no medical room, so the caller can visit people instead.</summary>
        internal static bool TryOpen(Pawn doctor)
        {
            if (doctor == null || Running(doctor)) return true;
            if (!Spot(doctor, out var bed, out var cell)) { FrameworkServices.Trace("clinic: no medical bed in an enclosed room for " + doctor.LabelShort); return false; }
            int now = FrameworkServices.Now;
            if (CareDemand.DoctorShouldTend(doctor, now) || PawnReadinessApi.StopReason(doctor, now) != null) return true;          // tending and the doctor's own needs come first
            if (!FrameworkServices.Ready(doctor) || doctor.CurJob?.playerForced == true || (Find.Selector.IsSelected(doctor) && !FrameworkServices.Forced) || FrameworkServices.IsAvatar(doctor)) return true;       // there is a clinic: wait until the doctor is free
            FrameworkServices.Trace("clinic: " + doctor.LabelShort + " goes to " + cell + " beside " + bed.LabelCap);
            byDoctor[doctor.thingIDNumber] = new ClinicSession { DoctorId = doctor.thingIDNumber, Bed = bed, DoctorCell = cell };
            var open = JobMaker.MakeJob(CareDefOf.WFL_ClinicDoctor, bed, cell);
            open.playerForced = true;
            doctor.jobs.StartJob(open, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            return true;
        }

        /// <summary>The doctor's job is over (finished, or interrupted by tending, a need or an order). A visit cut short counts for something only if most of it was done.</summary>
        internal static void Close(Pawn doctor)
        {
            if (doctor == null || !byDoctor.TryGetValue(doctor.thingIDNumber, out var s)) return;
            if (s.PatientId != 0 && s.Phase == ClinicPhase.Examining)
            {
                var c = FamilyComponent.Current; var patient = c?.TryGetPawn(s.PatientId);
                if (patient != null && !patient.Dead) Interrupted(c, s, doctor, patient, FrameworkServices.Now);
            }
            s.Done = true; byDoctor.Remove(doctor.thingIDNumber);
        }

        /// <summary>
        /// A visit stopped partway. If at least 60% of it was done the check-up counts, as a Fair one at most and with half the time; otherwise it counts for nothing, and the
        /// patient is asked again soon. Neither is a failure for the patient.
        /// </summary>
        private static void Interrupted(FamilyComponent c, ClinicSession s, Pawn doctor, Pawn patient, int now)
        {
            if (CheckupQuality.CountsAsPartial(s.Talked, TalkTicks)) CareDriver.CompleteCheckup(c.Registry, doctor, patient, now, s.Quality, true, s.Bed, true);
            else AppointmentBook.Reschedule(patient.thingIDNumber, now, 1500);
        }

        /// <summary>The patient is sent to the bed. False if they could not take the job.</summary>
        internal static bool Send(Pawn patient, ClinicSession s, Pawn doctor)
        {
            var visit = JobMaker.MakeJob(CareDefOf.WFL_ClinicPatient, s.Bed, doctor, s.Bed.GetSleepingSlotPos(0));
            visit.playerForced = true;          // not interrupted by the usual choosing of something else to do; it still ends if they are drafted, hurt, hungry or break
            patient.jobs.StartJob(visit, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            return patient.CurJobDef == CareDefOf.WFL_ClinicPatient;
        }

        internal static void Release(ClinicSession s) { s.PatientId = 0; s.Phase = ClinicPhase.Waiting; s.PatientLying = false; s.PhaseTick = 0; s.Talked = 0; s.NextWander = 0; s.Quality = 0f; s.Exchanges = 0; }

        /// <summary>
        /// The next person on the list who is able to come, unless it is outside the day's hours or the day's budget. A person who is busy is asked again later, one who declines is left
        /// alone, and one who cannot travel is put down for a visit instead. None of these is held against anyone.
        /// </summary>
        internal static void CallNext(FamilyComponent c, ClinicSession s, Pawn doctor, int now)
        {
            var r = c.Registry; var office = r.OfficeFor(OfficeKind.Healer);
            if (office == null || !Allowed(r, office, doctor, now)) { s.Idle += 2; return; }
            int id = r.NextCheckupPatient(now, x => CareDriver.CanBeSeen(c, doctor, x) && ForPatient(x) == null && Askable(r, x, now) && CareEvents.SickFirst(c, r, office, x), CareEvents.Priority(c, office));
            var patient = id == 0 ? null : c.TryGetPawn(id);
            if (patient == null || !patient.Spawned) { s.Idle++; return; }
            switch (CareRules.Assess(c, doctor, patient, now))
            {
                case PatientResponse.Unable: AppointmentBook.NeedsVisit(id, now); FrameworkServices.Trace("clinic: " + patient.LabelShort + " is not fit to come; they will be visited"); return;
                case PatientResponse.Busy: AppointmentBook.Busy(id, now); FrameworkServices.Trace("clinic: " + patient.LabelShort + " is busy just now"); return;
                case PatientResponse.Refuses: r.NoteRefusal(id, now); FrameworkServices.Trace("clinic: " + patient.LabelShort + " would rather not; they are left alone"); return;
            }
            if (!patient.CanReach(s.Bed, PathEndMode.OnCell, Danger.None)) { AppointmentBook.NeedsVisit(id, now); return; }
            s.Idle = 0;
            s.PatientId = id; s.PatientLying = false; s.Talked = 0; s.PhaseTick = 0; s.Quality = 0f; s.Exchanges = 0; s.Phase = ClinicPhase.Arriving;
            FrameworkServices.Trace("clinic: " + patient.LabelShort + " is asked in");
            if (!Send(patient, s, doctor)) { FrameworkServices.Trace("clinic: " + patient.LabelShort + " could not take the job"); AppointmentBook.Busy(id, now); Release(s); }
        }

        /// <summary>Whether a person is still on the list to be asked: not waiting out a gap, not carried over, not to be visited instead, not declined.</summary>
        internal static bool Askable(FamilyRegistry r, int id, int now) => !AppointmentBook.Waiting(id, now) && !AppointmentBook.Deferred(id) && !AppointmentBook.Bedside(id) && !r.HasRefused(id, now);

        /// <summary>Daytime only, within the day's budget, spaced through the day. A developer test ignores all three.</summary>
        internal static bool Allowed(FamilyRegistry r, CommunityOffice office, Pawn doctor, int now) =>
            FrameworkServices.Forced || WorkingHours.Daytime(GenLocalDate.HourOfDay(doctor)) && r.MayVisit(now, office.Care().IllnessRound);

        private static IntVec3 WanderCell(Pawn doctor, ClinicSession s)
        {
            var room = s.Bed.GetRoom(); var map = s.Bed.Map;
            if (room == null) return IntVec3.Invalid;
            var options = GenRadial.RadialCellsAround(s.Bed.Position, 5f, true)
                .Where(c => c.InBounds(map) && c != s.Bed.Position && c.Standable(map) && c.GetRoom(map) == room && !c.GetThingList(map).Any(t => t is Building_Bed) && doctor.CanReach(c, PathEndMode.OnCell, Danger.None)).ToList();
            return options.Count == 0 ? IntVec3.Invalid : options[Rand.Range(0, options.Count)];
        }

        /// <summary>One tick of the doctor's clinic: waiting, asking the next person in, and the examination itself.</summary>
        internal static void Tick(Pawn doctor, ClinicSession s, FamilyComponent c, Action<JobCondition> end)
        {
            int now = FrameworkServices.Now;
            if (s.Bed == null || s.Bed.Destroyed) { end(JobCondition.Incompletable); return; }
            if (s.Phase == ClinicPhase.Waiting)
            {
                if (!c.Registry.CheckupOpen(now)) { end(JobCondition.Succeeded); return; }
                if (doctor.Position != s.DoctorCell && !doctor.pather.Moving) doctor.pather.StartPath(s.DoctorCell, PathEndMode.OnCell);
                else if (!doctor.pather.Moving) doctor.rotationTracker?.FaceCell(s.Bed.Position);
                if (doctor.IsHashIntervalTick(120)) CallNext(c, s, doctor, now);
                if (s.Idle > 6) end(JobCondition.Succeeded);          // nobody on the list can come just now: the clinic closes and opens again when someone can
                return;
            }
            var patient = c.TryGetPawn(s.PatientId);
            if (patient == null || patient.Dead || !patient.Spawned) { Release(s); return; }
            s.PhaseTick++;
            switch (s.Phase)
            {
                case ClinicPhase.Arriving:
                    if (patient.CurJobDef != CareDefOf.WFL_ClinicPatient && s.PhaseTick > 90) { AppointmentBook.Busy(s.PatientId, now); Release(s); return; }          // pulled away: asked again later
                    if (s.PatientLying) { s.Phase = ClinicPhase.Examining; s.Talked = 0; s.NextWander = 0; return; }
                    if (s.PhaseTick > ArriveTicks) { FrameworkServices.Trace("clinic: " + patient.LabelShort + " has not come; asked again later"); AppointmentBook.Busy(s.PatientId, now); Release(s); }          // no one is sent for or brought in
                    return;
                default:          // Examining
                    if (patient.CurJobDef != CareDefOf.WFL_ClinicPatient && s.PhaseTick > 90) { Interrupted(c, s, doctor, patient, now); Release(s); return; }
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
                        s.Quality += FrameworkServices.RunConversation(doctor, patient, OfficeKind.Healer, SkillDefOf.Medicine, topic, doctor.thingIDNumber + s.PatientId + s.Talked, CareRules.Modifiers(doctor, s.Bed, true)).Quality;
                    }
                    if (s.Talked >= TalkTicks)
                    {
                        CareDriver.CompleteCheckup(c.Registry, doctor, patient, now, s.Quality, false, s.Bed, true);
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
            // The round is kept by the post's record, so ending here for tending, a meal, sleep, a visit to the toilet or a gathering only pauses it: the doctor takes it up again afterwards.
            this.FailOn(() => ClinicVisits.Of(pawn.thingIDNumber) == null || !FamilyLifeMod.Settings.healthChecks || PawnReadinessApi.StopReason(pawn, FrameworkServices.Now) != null || CareDemand.DoctorShouldTend(pawn, FrameworkServices.Now));
            AddFinishAction(cond => { FrameworkServices.Trace("clinic: " + pawn.LabelShort + " (doctor) finished (" + cond + "), reason: " + (PawnReadinessApi.StopReason(pawn, FrameworkServices.Now) ?? (CareDemand.DoctorShouldTend(pawn, FrameworkServices.Now) ? "someone needs tending" : "none"))); ClinicVisits.Close(pawn); });
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
        private Building_Bed Bed => job.targetA.Thing as Building_Bed;
        private Pawn Doctor => job.targetB.Thing as Pawn;
        // The bed is not reserved: the game's own bed rules would refuse a healthy person a medical bed. The clinic sees one patient at a time.
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            AddFinishAction(cond => { FrameworkServices.Trace("clinic: " + pawn.LabelShort + " leaves the clinic (" + cond + "), reason: " + (PawnReadinessApi.StopReason(pawn, FrameworkServices.Now) ?? "none")); if (pawn.jobs != null) pawn.jobs.posture = PawnPosture.Standing; });
            this.AddEndCondition(() =>
            {
                var s = Doctor == null ? null : ClinicVisits.Of(Doctor.thingIDNumber);
                return s == null || s.PatientId != pawn.thingIDNumber ? JobCondition.Succeeded : JobCondition.Ongoing;          // the doctor has finished with this patient, or let them go
            });
            this.FailOn(() => Doctor == null || Doctor.Dead || ClinicVisits.Of(Doctor.thingIDNumber) == null || PawnReadinessApi.StopReason(pawn, FrameworkServices.Now) != null);
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            var lie = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            lie.initAction = () =>
            {
                pawn.pather?.StopDead();
                pawn.jobs.posture = PawnPosture.LayingInBed;          // on the bed, in the medical room
                var s = ClinicVisits.Of(Doctor.thingIDNumber);
                if (s != null) { s.PatientLying = true; s.PhaseTick = 0; }
            };
            yield return lie;
        }
    }
}
