using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum StewardServiceKind : byte { Ceremony, Aid, Reconciliation, Negotiation, GuestCare, SupplyCrisis }

    /// <summary>A bounded record of work performed while holding office, rather than an unexplained status rank.</summary>
    public sealed partial class StewardRecord
    {
        public int PersonId, FamilyId, AppointedTick, EndedTick, LastServiceTick;
        public int CeremoniesOrganized, AidTransfers, Reconciliations, NegotiationsLed, GuestsHoused, ShortagesAverted, Failures;
        public bool Active => EndedTick == 0;
        public int Successes => CeremoniesOrganized + AidTransfers + Reconciliations + NegotiationsLed + GuestsHoused + ShortagesAverted;
    }

    public sealed partial class FamilyRegistry
    {
        public List<StewardRecord> StewardRecords = new List<StewardRecord>();

        public StewardRecord StewardshipOf(int person, int family, bool activeOnly = false)
            => StewardRecords.Where(s => s.PersonId == person && s.FamilyId == family && (!activeOnly || s.Active)).OrderByDescending(s => s.AppointedTick).FirstOrDefault();

        public void BeginStewardship(int person, int family, int now)
        {
            if (StewardshipOf(person, family, true) != null) return;
            StewardRecords.Add(new StewardRecord { PersonId = person, FamilyId = family, AppointedTick = now });
            if (StewardRecords.Count > 256)
            {
                var old = StewardRecords.Where(s => !s.Active).OrderBy(s => s.EndedTick).FirstOrDefault();
                if (old != null) StewardRecords.Remove(old);
            }
        }

        public void EndStewardship(int person, int family, int now)
        {
            var record = StewardshipOf(person, family, true);
            if (record != null) record.EndedTick = now > 0 ? now : record.AppointedTick + 1;
        }

        public void NoteStewardService(int person, int family, StewardServiceKind kind, bool success, int now)
        {
            if (RoleInHouse(person, family) != HouseRole.Steward) return;
            var record = StewardshipOf(person, family, true);
            if (record == null) { BeginStewardship(person, family, now); record = StewardshipOf(person, family, true); }
            if (record == null) return;
            record.LastServiceTick = now;
            if (!success) { record.Failures++; return; }
            switch (kind)
            {
                case StewardServiceKind.Ceremony: record.CeremoniesOrganized++; break;
                case StewardServiceKind.Aid: record.AidTransfers++; break;
                case StewardServiceKind.Reconciliation: record.Reconciliations++; break;
                case StewardServiceKind.Negotiation: record.NegotiationsLed++; break;
                case StewardServiceKind.GuestCare: record.GuestsHoused++; break;
                case StewardServiceKind.SupplyCrisis: record.ShortagesAverted++; break;
            }
        }
    }
}
