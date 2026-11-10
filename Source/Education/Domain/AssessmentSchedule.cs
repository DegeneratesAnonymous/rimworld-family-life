using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // When the annual assessment sits. Owner: Education.

    /// <summary>The annual assessment: a few daytime sittings over two days, and a result worked out from what the child studied. See <see cref="Assessments"/>.</summary>
    public static class TestingMath
    {
        public const int DurationTicks = 2 * 60000, SitTicks = 600, MakeUpTicks = 30000;
        public const int MaxSessions = 4;
        /// <summary>When each of the three planned sittings comes due, counted from the start. The fourth is a make-up for anyone who missed.</summary>
        public static readonly int[] Offsets = { 18000, 60000, 96000 };

        public static bool Daytime(int hour) => WorkingHours.Daytime(hour);
        public static bool Due(int sessionsHeld, int elapsed) => sessionsHeld < Offsets.Length && elapsed >= Offsets[sessionsHeld];
        public static bool MakeUpDue(int sessionsHeld, int elapsed, bool someoneNeedsOne) => sessionsHeld == Offsets.Length && someoneNeedsOne && elapsed >= DurationTicks - 12000 && elapsed < DurationTicks + MakeUpTicks;
        /// <summary>The assessment is over after the planned two days, or once the one make-up sitting has been held or its time has passed.</summary>
        public static bool Over(int sessionsHeld, int elapsed, bool someoneNeedsOne) =>
            sessionsHeld >= MaxSessions || elapsed >= DurationTicks + MakeUpTicks || (sessionsHeld >= Offsets.Length && elapsed >= DurationTicks && !someoneNeedsOne);
    }
}
