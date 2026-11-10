using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // What the doctor's post remembers beyond who holds it. Every field has a safe default, so an older save simply starts without it. Owner: Community Care.

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
        /// <summary>The favouritism record: the sum of check-up quality for people of the holder's house and for everyone else, with counts.</summary>
        public float KinQuality, OtherQuality;
        public int KinSeen, OtherSeen;
    }

    public sealed partial class FamilyRegistry
    {
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
}
