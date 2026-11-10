using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // What the doctor's and teacher's posts remember beyond who holds them. Every field has a safe default, so an older save simply starts without it.

    public sealed partial class CommunityOffice
    {
        /// <summary>How long the current check-up round stays open, in ticks; 0 in an older save, which means the original fourteen days.</summary>
        public int RoundWindow;
        /// <summary>People the last round did not reach. They go first next time and are never marked as having failed.</summary>
        public List<int> Missed = new List<int>();
        /// <summary>People who declined this round, and when. A refusal is respected: they are not asked again for several days.</summary>
        public List<int> Refused = new List<int>(), RefusedTick = new List<int>();
        /// <summary>Routine visits made today (the daily budget) and when the last one was.</summary>
        public int RoutineDay, RoutineUsed, LastVisitTick;
        /// <summary>Professional contact today: how many times each person has dealt with the holder (limits what routine contact can do to a relationship).</summary>
        public int ContactDay;
        public List<int> ContactIds = new List<int>(), ContactCounts = new List<int>();
        /// <summary>The teacher's record of what each child studied since the last assessment; see <see cref="LessonRecord"/>.</summary>
        public List<string> LessonLog = new List<string>();
        /// <summary>Books already used for a pupil's lesson, so the same book cannot give the same child its experience over and over: "pupil|book|tick".</summary>
        public List<string> BookUse = new List<string>();
        /// <summary>The subjects of the last few classes, newest last, so the teacher varies them.</summary>
        public List<string> RecentSubjects = new List<string>();
        /// <summary>Each assessed child's accumulated sitting performance (hundredths), parallel to TestIds; and how many windows they were excused from.</summary>
        public List<int> TestScore = new List<int>(), TestExcused = new List<int>();
        /// <summary>The favouritism record: the sum of check-up quality for people of the holder's house and for everyone else, with counts.</summary>
        public float KinQuality, OtherQuality;
        public int KinSeen, OtherSeen;
    }

    public sealed partial class FamilyRegistry
    {
        // ---- routine budget ----

        public int RoutineVisitsToday(int now)
        {
            var o = OfficeFor(OfficeKind.Healer); if (o == null) return 0;
            int day = now / ActivityTuning.TicksPerDay;
            return o.RoutineDay == day ? o.RoutineUsed : 0;
        }

        public bool MayVisit(int now, bool outbreak)
        {
            var o = OfficeFor(OfficeKind.Healer); if (o == null || !o.Held) return false;
            int since = o.LastVisitTick == 0 || now < o.LastVisitTick ? int.MaxValue : now - o.LastVisitTick;
            return CheckupWorkload.MayStart(RoutineVisitsToday(now), outbreak, since);
        }

        public void NoteVisit(int now)
        {
            var o = OfficeFor(OfficeKind.Healer); if (o == null) return;
            int day = now / ActivityTuning.TicksPerDay;
            if (o.RoutineDay != day) { o.RoutineDay = day; o.RoutineUsed = 0; }
            o.RoutineUsed++; o.LastVisitTick = now;
        }

        // ---- refusals ----

        public void NoteRefusal(int person, int now)
        {
            var o = OfficeFor(OfficeKind.Healer); if (o == null) return;
            int i = o.Refused.IndexOf(person);
            if (i >= 0) { o.RefusedTick[i] = now; return; }
            o.Refused.Add(person); o.RefusedTick.Add(now);
            while (o.Refused.Count > 64) { o.Refused.RemoveAt(0); o.RefusedTick.RemoveAt(0); }
        }

        public bool HasRefused(int person, int now)
        {
            var o = OfficeFor(OfficeKind.Healer); if (o == null) return false;
            int i = o.Refused.IndexOf(person);
            return i >= 0 && now >= o.RefusedTick[i] && now - o.RefusedTick[i] < AppointmentBackoff.RefusalDays * ActivityTuning.TicksPerDay;
        }

        // ---- professional contact ----

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

        // ---- favouritism, from what was observed ----

        /// <summary>Records how a check-up went for someone of the holder's house or not. Shared ancestry alone proves nothing: only a gap in how people were treated does.</summary>
        public void NoteCareQuality(int patient, ResultQuality level)
        {
            var o = OfficeFor(OfficeKind.Healer); if (o == null) return;
            if (KinOfHolder(patient, OfficeKind.Healer)) { o.KinQuality += (int)level; o.KinSeen++; }
            else { o.OtherQuality += (int)level; o.OtherSeen++; }
        }

        /// <summary>A gap of a full level or more between how the holder's own house and everyone else was treated, once there is enough to compare. Positive favours the house.</summary>
        public float FavouritismGap(OfficeKind kind)
        {
            var o = OfficeFor(kind);
            if (o == null || o.KinSeen < 3 || o.OtherSeen < 3) return 0f;
            float gap = o.KinQuality / o.KinSeen - o.OtherQuality / o.OtherSeen;
            return Math.Abs(gap) >= 1f ? gap : 0f;
        }
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
