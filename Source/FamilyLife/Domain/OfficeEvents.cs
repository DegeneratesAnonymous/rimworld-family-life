using System;
using System.Collections.Generic;

namespace FamilyLife.Domain
{
    // Two yearly events belong to the community posts: the doctor's check-up round and the teacher's annual testing of the children. Each is offered to the player as a
    // neutral event they can begin or pass on, and each is measured: a check-up has a quality that decides how much it helps, and a testing has a quality that decides
    // what the children are rewarded with.

    public enum ResultQuality : byte { Poor, Fair, Good, Excellent }

    public static class PendingEvent
    {
        public const int None = 0, Checkups = 1, Illness = 2, Testing = 3;
    }

    public static class CheckupQuality
    {
        /// <summary>
        /// The doctor's check: Medicine and Social (the doctor has to find the trouble and also get the patient to say what is wrong), how the talk went, and a d20 for the day.
        /// </summary>
        public static float Score(int medicine, int social, float talkQuality, int d20) =>
            Math.Max(0, medicine) * .6f + Math.Max(0, social) * .4f + talkQuality * 1.5f + (d20 - 10.5f);

        public static ResultQuality Level(float score) => score < 4f ? ResultQuality.Poor : score < 9f ? ResultQuality.Fair : score < 14f ? ResultQuality.Good : ResultQuality.Excellent;

        /// <summary>How long the benefit lasts, against a Fair check-up.</summary>
        public static float DurationFactor(ResultQuality q) => q == ResultQuality.Poor ? .7f : q == ResultQuality.Fair ? 1f : q == ResultQuality.Good ? 1.25f : 1.6f;

        /// <summary>The severity the recent-check-up condition is given: its stages begin at 0, .25, .5 and .75, so the benefit it gives follows the quality.</summary>
        public static float Severity(ResultQuality q) => q == ResultQuality.Poor ? .1f : q == ResultQuality.Fair ? .35f : q == ResultQuality.Good ? .6f : .85f;
    }

    public static class TestingMath
    {
        /// <summary>The testing lasts two days, with a check every game hour.</summary>
        public const int DurationTicks = 2 * 60000, SlotTicks = 2500, MinAttempts = 12;
        public const int BaseDifficulty = 16;

        /// <summary>The teacher makes it easier: the better they are at teaching and at reaching a child, the lower the bar.</summary>
        public static int Difficulty(int teacherScore) => BaseDifficulty - Math.Min(4, Math.Max(0, teacherScore) / 8);

        /// <summary>One hourly check: a d20 and the child's Intellectual and half their best skill against the difficulty.</summary>
        public static bool Passes(int intellectual, int bestSkill, int d20, int difficulty) => d20 + Math.Max(0, intellectual) + Math.Max(0, bestSkill) / 2 >= difficulty;

        /// <summary>Quality from how many of the hourly checks were passed. A child who attempted too few to be judged is Poor.</summary>
        public static ResultQuality Level(int passes, int attempts)
        {
            if (attempts < MinAttempts) return ResultQuality.Poor;
            float rate = passes / (float)attempts;
            return rate < .45f ? ResultQuality.Poor : rate < .65f ? ResultQuality.Fair : rate < .82f ? ResultQuality.Good : ResultQuality.Excellent;
        }

        /// <summary>How many skills are rewarded, and how much experience each gets: both rise with the quality.</summary>
        public static int SkillCount(ResultQuality q) => q == ResultQuality.Poor ? 1 : q == ResultQuality.Fair ? 2 : q == ResultQuality.Good ? 3 : 4;
        public static float XpEach(ResultQuality q) => q == ResultQuality.Poor ? 400f : q == ResultQuality.Fair ? 700f : q == ResultQuality.Good ? 1100f : 1600f;
        public static float TotalXp(ResultQuality q) => SkillCount(q) * XpEach(q);
    }
}
