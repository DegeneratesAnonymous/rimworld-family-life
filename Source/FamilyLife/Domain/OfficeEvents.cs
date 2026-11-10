using System;
using System.Collections.Generic;

namespace FamilyLife.Domain
{
    // Two yearly events belong to the community posts: the doctor's check-up round and the teacher's annual assessment of the children. Both happen by themselves; the
    // player is told and may reschedule or decline. Each is measured: a check-up has a quality that decides how much it helps, and an assessment is derived from what
    // the child actually studied, how much they took part and how it went.

    public enum ResultQuality : byte { Poor, Fair, Good, Excellent }

    public static class PendingEvent
    {
        public const int None = 0, Checkups = 1, Illness = 2, Testing = 3;
    }

    /// <summary>
    /// The doctor's check. Medicine does most of the work; Social is how well the doctor gets the patient to say what is wrong; the room, the doctor's own state and how the
    /// talk went each move it a little; the day moves it a very little. A skilled doctor is dependable: ten levels of Medicine are worth 7.5 points and the day is worth
    /// at most 2.
    /// </summary>
    public static class CheckupQuality
    {
        public const float MedicineWeight = .75f, SocialWeight = .25f, MaxModifiers = 2f, MaxTalk = 1f, MaxNoise = 2f;
        public const float FairFrom = 5.5f, GoodFrom = 9.5f, ExcellentFrom = 14f;

        /// <param name="modifiers">The room and the doctor's preparation and fatigue, already summed; bounded here.</param>
        /// <param name="talkSum">The sum of how each exchange went (see <see cref="GuidedExchanges.TierValue"/>); bounded here.</param>
        /// <param name="noise">The day: whole points, -2 to +2.</param>
        public static float Score(int medicine, int social, float modifiers, float talkSum, int noise) =>
            Math.Max(0, medicine) * MedicineWeight + Math.Max(0, social) * SocialWeight
            + Math.Max(-MaxModifiers, Math.Min(MaxModifiers, modifiers))
            + TalkAdjust(talkSum)
            + Math.Max(-(int)MaxNoise, Math.Min((int)MaxNoise, noise));

        /// <summary>A good talk is worth up to one point and a poor one costs up to one. It is not multiplied.</summary>
        public static float TalkAdjust(float talkSum) => Math.Max(-MaxTalk, Math.Min(MaxTalk, talkSum * .4f));

        public static ResultQuality Level(float score) => score < FairFrom ? ResultQuality.Poor : score < GoodFrom ? ResultQuality.Fair : score < ExcellentFrom ? ResultQuality.Good : ResultQuality.Excellent;

        /// <summary>How long the benefit lasts, against a Fair check-up (twenty days).</summary>
        public static float DurationFactor(ResultQuality q) => q == ResultQuality.Poor ? .5f : q == ResultQuality.Fair ? 1f : q == ResultQuality.Good ? 1.1f : 1.25f;

        /// <summary>The severity the recent-check-up condition is given: its stages begin at 0, .25, .5 and .75, so the stage follows the quality.</summary>
        public static float Severity(ResultQuality q) => q == ResultQuality.Poor ? .1f : q == ResultQuality.Fair ? .35f : q == ResultQuality.Good ? .6f : .85f;

        /// <summary>An appointment cut short after most of it was done counts, but only as a Fair check-up with half the time. Anything less than that counts for nothing.</summary>
        public const float PartialFromShare = .6f;
        public static bool CountsAsPartial(int talked, int total) => total > 0 && talked * 5 >= total * 3 && talked < total;          // at least three fifths, in whole numbers
        public static ResultQuality Cap(ResultQuality q, bool partial) => partial && q > ResultQuality.Fair ? ResultQuality.Fair : q;
        public static float PartialDuration => .5f;

        /// <summary>
        /// A new check-up never stacks on an old one. If the person already has a better benefit it is left alone (a later poor visit cannot reduce it and cannot improve
        /// it); otherwise the benefit becomes the new one with the longer of the two remaining times.
        /// </summary>
        public static BenefitUpdate Merge(bool hasExisting, float existingSeverity, int existingRemaining, ResultQuality newLevel, int newTicks)
        {
            float sev = Severity(newLevel);
            if (!hasExisting) return new BenefitUpdate { Apply = true, Severity = sev, Remaining = newTicks };
            if (existingSeverity > sev + .001f) return new BenefitUpdate { Apply = false, Severity = existingSeverity, Remaining = existingRemaining };
            return new BenefitUpdate { Apply = true, Severity = Math.Max(existingSeverity, sev), Remaining = Math.Max(existingRemaining, newTicks) };
        }

        public struct BenefitUpdate { public bool Apply; public float Severity; public int Remaining; }
    }

    /// <summary>
    /// How much of the doctor's time the optional rounds may take. A visit costs the conversation and a little walking; the doctor spends at most a fifth of the waking day
    /// on routine visits, spaced through the day, and only when no one needs tending. A big colony simply takes longer to get round, and the people not yet seen are first
    /// next time rather than marked as having failed.
    /// </summary>
    public static class CheckupWorkload
    {
        public const int VisitTicks = 1500, VisitCost = 2100, RoutineBudgetTicks = 8400, OutbreakBudgetTicks = 12600, MinGapTicks = 2500;
        public const int MinWindowDays = 14, MaxWindowDays = 40;

        public static int VisitsPerDay(bool outbreak) => (outbreak ? OutbreakBudgetTicks : RoutineBudgetTicks) / VisitCost;

        /// <summary>How long a round stays open for a colony of this size.</summary>
        public static int WindowDays(int patients, bool outbreak)
        {
            int days = (int)Math.Ceiling(Math.Max(0, patients) / (double)VisitsPerDay(outbreak)) + 3;
            return Math.Max(MinWindowDays, Math.Min(MaxWindowDays, days));
        }

        /// <summary>The doctor's time given to routine visits as a share of a 40,000-tick waking day, when the budget is used in full.</summary>
        public static float DayShare(bool outbreak) => VisitsPerDay(outbreak) * VisitCost / 40000f;

        public static bool MayStart(int visitsToday, bool outbreak, int sinceLastVisit) => visitsToday < VisitsPerDay(outbreak) && sinceLastVisit >= MinGapTicks;

        /// <summary>Days needed to see everyone at the daily budget.</summary>
        public static float DaysToSee(int patients, bool outbreak) => patients / (float)VisitsPerDay(outbreak);
    }

    /// <summary>When a patient who could not come is asked again. A refusal is not asked again this round; being busy is rescheduled with a growing gap, and after a few
    /// tries the person is simply carried to the next round without any mark against them.</summary>
    public static class AppointmentBackoff
    {
        public const int MaxTries = 6;
        public static int RetryAfter(int tries) => tries <= 1 ? 1500 : tries == 2 ? 4000 : tries == 3 ? 10000 : tries == 4 ? 20000 : tries == 5 ? 30000 : 45000;
        public static bool GiveUp(int tries) => tries >= MaxTries;
        public const int RefusalDays = 6;
    }

    /// <summary>The annual assessment: a few daytime sittings over two days, and a result worked out from what the child studied. See <see cref="Assessments"/>.</summary>
    public static class TestingMath
    {
        public const int DurationTicks = 2 * 60000, SitTicks = 600, MakeUpTicks = 30000;
        public const int FirstHour = 7, LastHour = 19, MaxSessions = 4;
        /// <summary>When each of the three planned sittings comes due, counted from the start. The fourth is a make-up for anyone who missed.</summary>
        public static readonly int[] Offsets = { 18000, 60000, 96000 };

        public static bool Daytime(int hour) => hour >= FirstHour && hour < LastHour;
        public static bool Due(int sessionsHeld, int elapsed) => sessionsHeld < Offsets.Length && elapsed >= Offsets[sessionsHeld];
        public static bool MakeUpDue(int sessionsHeld, int elapsed, bool someoneNeedsOne) => sessionsHeld == Offsets.Length && someoneNeedsOne && elapsed >= DurationTicks - 12000 && elapsed < DurationTicks + MakeUpTicks;
        /// <summary>The assessment is over after the planned two days, or once the one make-up sitting has been held or its time has passed.</summary>
        public static bool Over(int sessionsHeld, int elapsed, bool someoneNeedsOne) =>
            sessionsHeld >= MaxSessions || elapsed >= DurationTicks + MakeUpTicks || (sessionsHeld >= Offsets.Length && elapsed >= DurationTicks && !someoneNeedsOne);
    }
}
