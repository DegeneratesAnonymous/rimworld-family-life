using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // The ledger of professional contact between an office holder and the people they serve, and who may direct a post. Owner: the framework.

    public sealed partial class CommunityOffice
    {
        /// <summary>Professional contact today: how many times each person has dealt with the holder (limits what routine contact can do to a relationship).</summary>
        public int ContactDay;
        public List<int> ContactIds = new List<int>(), ContactCounts = new List<int>();
    }

    public sealed partial class FamilyRegistry
    {
        /// <summary>
        /// The single place a doctor's or teacher's exchange changes a relationship. Contact is limited per pair per day with falling returns. Returns the scale applied (0 when the
        /// day's contact between them is used up), so the caller can tell whether anything changed.
        /// </summary>
        public float ProfessionalExchange(OfficeKind kind, int leader, int other, ExchangeTopic topic, EventTier tier, int now)
        {
            var o = OfficeFor(kind);
            if (o == null || o.HolderId != leader || leader == other || GetPerson(other) == null) return 0f;
            int day = now / ActivityTuning.TicksPerDay;
            if (o.ContactDay != day) { o.ContactDay = day; o.ContactIds.Clear(); o.ContactCounts.Clear(); }
            int idx = o.ContactIds.IndexOf(other);
            int today = idx >= 0 ? o.ContactCounts[idx] : 0;
            float scale = ProfessionalContact.DayScale(today);
            if (scale <= 0f) return 0f;
            if (idx >= 0) o.ContactCounts[idx]++; else if (o.ContactIds.Count < 96) { o.ContactIds.Add(other); o.ContactCounts.Add(1); }
            var bond = BondFrom(other, leader);
            var e = ProfessionalContact.Effect(topic, tier, bond?.Attachment ?? 0f);
            // closeness only from the first contact of the day: a second and third visit the same day add familiarity and trust, not affection
            ChangeBond(other, leader, new BondChange(today == 0 ? e.Attachment : 0f, e.Trust, e.Tension, e.Familiarity), now, scale);
            var side = ProfessionalContact.LeaderSide();
            ChangeBond(leader, other, side, now, scale);
            return scale;
        }

        private Bond BondFrom(int from, int to) => BondsFrom(from).FirstOrDefault(b => b.To == to);
    }

    /// <summary>Who may reschedule or decline a routine event. The colony overview and the post's holder may; so may anyone who manages the holder's house. Checked again when the action runs.</summary>
    public static class OfficeAuthority
    {
        public static bool MayDirect(FamilyRegistry r, int actor, OfficeKind kind)
        {
            if (actor == 0) return true;          // the overview, or no one being played
            var o = r?.OfficeFor(kind);
            if (o == null || !o.Held) return false;
            return actor == o.HolderId || (o.HolderFamily != 0 && r.CanManageHouse(actor, o.HolderFamily));
        }
    }
}
