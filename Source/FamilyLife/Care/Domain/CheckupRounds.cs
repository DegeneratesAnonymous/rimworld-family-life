using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // The doctor's check-up rounds on the shared office registry. Owner: Community Care.

    public sealed partial class FamilyRegistry
    {
        public const int CheckupWindowTicks = 14 * ActivityTuning.TicksPerDay;          // a round lasts until everyone has been seen; the doctor can be called away and come back

        // ---- the doctor's check-ups ----

        /// <summary>
        /// Opens a round of check-ups when one is due: every colonist is to be seen once. The interval is the player's choice. Nobody is booked in
        /// advance and nothing is forced.
        /// </summary>
        public bool BeginCheckupRound(int now, int intervalTicks)
        {
            var office = OfficeFor(OfficeKind.Healer);
            if (office == null || !office.Held || intervalTicks <= 0 || now < office.NextRoundTick || CheckupOpen(now)) return false;
            // Anyone the last round did not reach (and who did not decline) is first this time; nobody is marked as having failed for not being reached.
            var prior = office.Examined;
            office.Missed = RoundStartPeople(office).Where(id => !prior.Contains(id) && !office.Refused.Contains(id)).Take(96).ToList();
            office.RoundStartTick = now; office.Examined.Clear(); office.NextRoundTick = now + intervalTicks;
            office.RoundWindow = CheckupWorkload.WindowDays(Persons.Count(p => p.InColony && p.Id != office.HolderId), office.IllnessRound) * ActivityTuning.TicksPerDay;
            office.Refused.Clear(); office.RefusedTick.Clear();
            return true;
        }

        private IEnumerable<int> RoundStartPeople(CommunityOffice office) => Persons.Where(p => p.InColony && p.Id != office.HolderId).Select(p => p.Id);

        public bool CheckupOpen(int now)
        {
            var office = OfficeFor(OfficeKind.Healer);
            return office != null && office.Held && office.RoundStartTick != 0 && now - office.RoundStartTick < (office.RoundWindow > 0 ? office.RoundWindow : CheckupWindowTicks);
        }

        /// <summary>The next person still to be seen this round, chosen from those the game says are free to go. The doctor is not their own patient.</summary>
        public int NextCheckupPatient(int now, Func<int, bool> available, Func<int, int> priority = null)
        {
            var office = OfficeFor(OfficeKind.Healer);
            if (office == null || !CheckupOpen(now)) return 0;
            return Persons.Where(p => p.InColony && p.Id != office.HolderId && !office.Examined.Contains(p.Id) && (available == null || available(p.Id)))
                .OrderBy(p => priority == null ? 0 : priority(p.Id)).ThenBy(p => office.Missed.Contains(p.Id) ? 0 : 1).ThenBy(p => p.Id).Select(p => p.Id).FirstOrDefault();      // no house is seen first; those missed last time are
        }

        /// <summary>The doctor has really seen this person. Counted once per round; a round ends when everyone present has been seen.</summary>
        public bool RecordCheckup(int doctor, int patient, int now)
        {
            var office = OfficeFor(OfficeKind.Healer);
            if (office == null || office.HolderId != doctor || !CheckupOpen(now) || patient == doctor || GetPerson(patient)?.InColony != true || office.Examined.Contains(patient)) return false;
            office.Examined.Add(patient); office.Missed.Remove(patient);
            if (Persons.Where(p => p.InColony && p.Id != doctor).All(p => office.Examined.Contains(p.Id))) office.RoundStartTick = 0;
            return true;
        }
    }
}
