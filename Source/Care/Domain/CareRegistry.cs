using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // The doctor's rounds, budget, refusals and record of how people were treated, as services over the shared registry (using only its public members). Owner: Community Care.

    public static class CareRegistry
    {
        /// <summary>A round lasts until everyone has been seen; the doctor can be called away and come back. The original fixed window, still read for a round started by an older save.</summary>
        public const int CheckupWindowTicks = 14 * ActivityTuning.TicksPerDay;

        private static CommunityOffice Post(FamilyRegistry r) => r.OfficeFor(OfficeKind.Healer);

        // ---- rounds ----

        /// <summary>
        /// Opens a round of check-ups when one is due: every colonist is to be seen once. The interval is the player's choice. Nobody is booked in
        /// advance and nothing is forced.
        /// </summary>
        public static bool BeginCheckupRound(this FamilyRegistry r, int now, int intervalTicks)
        {
            var office = Post(r);
            if (office == null || !office.Held) return false;
            var c = office.Care();
            if (intervalTicks <= 0 || now < c.NextRoundTick || r.CheckupOpen(now)) return false;
            // Anyone the last round did not reach (and who did not decline) is first this time; nobody is marked as having failed for not being reached.
            var prior = c.Examined;
            c.Missed = r.Persons.Where(p => p.InColony && p.Id != office.HolderId).Select(p => p.Id).Where(id => !prior.Contains(id) && !c.Refused.Contains(id)).Take(96).ToList();
            c.RoundStartTick = now; c.Examined = new List<int>(); c.NextRoundTick = now + intervalTicks;
            c.RoundWindow = CheckupWorkload.WindowDays(r.Persons.Count(p => p.InColony && p.Id != office.HolderId), c.IllnessRound) * ActivityTuning.TicksPerDay;
            c.Refused = new List<int>(); c.RefusedTick = new List<int>();
            return true;
        }

        public static bool CheckupOpen(this FamilyRegistry r, int now)
        {
            var office = Post(r);
            if (office == null || !office.Held) return false;
            var c = office.Care();
            return c.RoundStartTick != 0 && now - c.RoundStartTick < (c.RoundWindow > 0 ? c.RoundWindow : CheckupWindowTicks);
        }

        /// <summary>The next person still to be seen this round, chosen from those the game says are free to go. The doctor is not their own patient.</summary>
        public static int NextCheckupPatient(this FamilyRegistry r, int now, Func<int, bool> available, Func<int, int> priority = null)
        {
            var office = Post(r);
            if (office == null || !r.CheckupOpen(now)) return 0;
            var c = office.Care();
            return r.Persons.Where(p => p.InColony && p.Id != office.HolderId && !c.Examined.Contains(p.Id) && (available == null || available(p.Id)))
                .OrderBy(p => priority == null ? 0 : priority(p.Id)).ThenBy(p => c.Missed.Contains(p.Id) ? 0 : 1).ThenBy(p => p.Id).Select(p => p.Id).FirstOrDefault();      // no house is seen first; those missed last time are
        }

        /// <summary>The doctor has really seen this person. Counted once per round; a round ends when everyone present has been seen.</summary>
        public static bool RecordCheckup(this FamilyRegistry r, int doctor, int patient, int now)
        {
            var office = Post(r);
            if (office == null || office.HolderId != doctor || !r.CheckupOpen(now) || patient == doctor || r.GetPerson(patient)?.InColony != true) return false;
            var c = office.Care();
            if (c.Examined.Contains(patient)) return false;
            c.Examined.Add(patient); c.Missed.Remove(patient);
            if (r.Persons.Where(p => p.InColony && p.Id != doctor).All(p => c.Examined.Contains(p.Id))) c.RoundStartTick = 0;
            return true;
        }

        // ---- the day's budget ----

        public static int RoutineVisitsToday(this FamilyRegistry r, int now)
        {
            var o = Post(r); if (o == null) return 0;
            var c = o.Care();
            return c.RoutineDay == now / ActivityTuning.TicksPerDay ? c.RoutineUsed : 0;
        }

        public static bool MayVisit(this FamilyRegistry r, int now, bool outbreak)
        {
            var o = Post(r); if (o == null || !o.Held) return false;
            var c = o.Care();
            int since = c.LastVisitTick == 0 || now < c.LastVisitTick ? int.MaxValue : now - c.LastVisitTick;
            return CheckupWorkload.MayStart(r.RoutineVisitsToday(now), outbreak, since);
        }

        public static void NoteVisit(this FamilyRegistry r, int now)
        {
            var o = Post(r); if (o == null) return;
            var c = o.Care();
            int day = now / ActivityTuning.TicksPerDay;
            if (c.RoutineDay != day) { c.RoutineDay = day; c.RoutineUsed = 0; }
            c.RoutineUsed++; c.LastVisitTick = now;
        }

        // ---- refusals ----

        public static void NoteRefusal(this FamilyRegistry r, int person, int now)
        {
            var o = Post(r); if (o == null) return;
            var c = o.Care();
            int i = c.Refused.IndexOf(person);
            if (i >= 0) { c.RefusedTick[i] = now; return; }
            c.Refused.Add(person); c.RefusedTick.Add(now);
            while (c.Refused.Count > 64) { c.Refused.RemoveAt(0); c.RefusedTick.RemoveAt(0); }
        }

        public static bool HasRefused(this FamilyRegistry r, int person, int now)
        {
            var o = Post(r); if (o == null) return false;
            var c = o.Care();
            int i = c.Refused.IndexOf(person);
            return i >= 0 && now >= c.RefusedTick[i] && now - c.RefusedTick[i] < AppointmentBackoff.RefusalDays * ActivityTuning.TicksPerDay;
        }

        // ---- how people were treated ----

        /// <summary>Records how a check-up went for someone of the holder's house or not. Shared ancestry alone proves nothing: only a gap in how people were treated does.</summary>
        public static void NoteCareQuality(this FamilyRegistry r, int patient, ResultQuality level)
        {
            var o = Post(r); if (o == null) return;
            var c = o.Care();
            if (r.KinOfHolder(patient, OfficeKind.Healer)) { c.KinQuality += (int)level; c.KinSeen++; }
            else { c.OtherQuality += (int)level; c.OtherSeen++; }
        }

        /// <summary>A gap of a full level or more between how the holder's own house and everyone else was treated, once there is enough to compare. Positive favours the house.</summary>
        public static float FavouritismGap(this FamilyRegistry r, OfficeKind kind)
        {
            var o = r.OfficeFor(kind);
            if (o == null || kind != OfficeKind.Healer) return 0f;
            var c = o.Care();
            if (c.KinSeen < 3 || c.OtherSeen < 3) return 0f;
            float gap = c.KinQuality / c.KinSeen - c.OtherQuality / c.OtherSeen;
            return Math.Abs(gap) >= 1f ? gap : 0f;
        }

        // ---- leaving ----

        /// <summary>
        /// Ends everything the doctor has under way so the module can be taken away: the round is closed (the community's record of who was seen, missed and declined is kept), a waiting
        /// planned event is cleared, and the post is given up. Returns what was done, one line each.
        /// </summary>
        public static List<string> EndCareActivity(this FamilyRegistry r, int now, params int[] plannedKinds)
        {
            var done = new List<string>();
            var o = Post(r);
            if (o == null) return done;
            var c = o.Care();
            // The round is closed so it cannot resume by itself; who was seen, who was missed and who declined is the community's record and is kept as it is.
            if (c.RoundStartTick != 0 || c.IllnessRound) { c.RoundStartTick = 0; c.RoundWindow = 0; c.IllnessRound = false; done.Add("the check-up round was closed (" + c.Examined.Count + " people already seen stay on the record)"); }
            if (Array.IndexOf(plannedKinds, o.PendingKind) >= 0) { o.PendingKind = 0; done.Add("a planned event waiting to begin was cleared"); }
            if (o.Held && r.ResignOffice(OfficeKind.Healer, o.HolderId, now)) done.Add("the doctor's post was given up");
            return done;
        }
    }
}
