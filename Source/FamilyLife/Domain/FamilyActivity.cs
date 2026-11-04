using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>A person's family time on one game day, and when they were last read to. Bounded: only colonists, only recent days.</summary>
    public sealed partial class TimeRecord
    {
        public int PersonId;
        public int Day;
        public int Ticks;
        public int LastStoryTick = -1000000;
    }

    /// <summary>The numbers behind family activities. Tuning values, not rules.</summary>
    public static class ActivityTuning
    {
        public const int TicksPerDay = 60000;
        public const int DefaultDailyTargetTicks = 5000;           // two in-game hours
        public const int StoryIntervalTicks = 45000;               // at most one story a night
        public const int StoryTicks = 1800;
        public const int ReunionAwayTicks = 5 * TicksPerDay;      // away this long and coming home counts as a reunion

        // Quality time (per in-game hour of being together on purpose)
        public const float QualityAttachmentPerHour = 1.5f;
        public const float QualityTrustPerHour = 0.8f;
        public const float QualityFamiliarityPerHour = 1.0f;
        public const float QualityTensionReliefPerHour = 2.0f;

        // Who wants to be with whom
        public const float MinDesire = 0.15f;
        public const float NeverTogetherHours = 72f;
        public const float StaleHoursFullDesire = 24f;

        // Bedtime stories
        public const int StoryMaxChildSkill = 8;                   // stories teach beginners; they do not make experts
        public const float BookEfficiency = 0.5f;                  // a story is a shortened, interrupted reading
        public const float NoBookBaseXp = 40f;

        // Eating together
        public const int SharedMealWindowTicks = 1500;
        public const float SharedMealTicks = 600f;
        public const float SharedMealMinAttachment = 15f;

        /// <summary>How well time together works for someone who holds this opinion of the other: from -0.6 (grating) to 1.2 (a joy).</summary>
        public static float TimeFit(float opinion)
        {
            return Math.Max(-0.6f, Math.Min(1.2f, 0.45f + opinion / 55f));
        }

        public static float SocialFactor(float social) => 0.5f + Math.Max(0f, Math.Min(20f, social)) / 20f;
    }

    public sealed class RankedCompanion
    {
        public int Id;
        public float Desire;
    }

    public sealed class Reader
    {
        public int Id;
        public float Social;
        public float BestSkillLevel;
    }

    /// <summary>Someone who just finished eating, as the game layer remembers it for a few minutes.</summary>
    public sealed class RecentMeal
    {
        public int PersonId;
        public int Tick;
        public int RoomKey;            // -1 when not in a room
    }

    public sealed partial class FamilyRegistry
    {
        public List<TimeRecord> TimeRecords = new List<TimeRecord>();
        private readonly Dictionary<int, TimeRecord> timeByPerson = new Dictionary<int, TimeRecord>();

        private void RebuildTimeIndex()
        {
            TimeRecords = TimeRecords ?? new List<TimeRecord>();
            TimeRecords.RemoveAll(t => t == null);
            timeByPerson.Clear();
            var unique = new List<TimeRecord>(TimeRecords.Count);
            foreach (var t in TimeRecords)
            {
                if (timeByPerson.ContainsKey(t.PersonId)) continue;
                t.Ticks = Math.Max(0, t.Ticks);
                timeByPerson[t.PersonId] = t;
                unique.Add(t);
            }
            TimeRecords = unique;
        }

        private TimeRecord TimeFor(int personId, bool create)
        {
            EnsureIndexes();
            if (timeByPerson.TryGetValue(personId, out var t)) return t;
            if (!create) return null;
            t = new TimeRecord { PersonId = personId, Day = int.MinValue };
            TimeRecords.Add(t);
            timeByPerson[personId] = t;
            return t;
        }

        private static int DayOf(int tick) => tick / ActivityTuning.TicksPerDay;

        // ------------------------------------------------------------------ the family-time budget

        /// <summary>How much family time this person has had today.</summary>
        public int FamilyTicksToday(int personId, int now)
        {
            var t = TimeFor(personId, false);
            return t != null && t.Day == DayOf(now) ? t.Ticks : 0;
        }

        /// <summary>
        /// The budget is soft: it never ends an activity in progress, it only keeps new voluntary ones from starting once the
        /// day's target has been reached.
        /// </summary>
        public bool HasFamilyTimeLeft(int personId, int now, int dailyTargetTicks)
        {
            return FamilyTicksToday(personId, now) < dailyTargetTicks;
        }

        public void AddFamilyTime(int personId, int ticks, int now)
        {
            if (ticks <= 0) return;
            var t = TimeFor(personId, true);
            int day = DayOf(now);
            if (t.Day != day) { t.Day = day; t.Ticks = 0; }
            t.Ticks += ticks;
        }

        // ------------------------------------------------------------------ quality time

        /// <summary>Two people spent time together on purpose. Both feel it; the longer, the more.</summary>
        public void RecordTogether(int a, int b, int ticks, int now, BondCause cause = BondCause.Together)
        {
            if (ticks <= 0 || a == b) return;
            KeepPromises(a, b, ticks, now);
            float hours = ticks / (float)BondTuning.TicksPerHour;
            foreach (var pair in new[] { new[] { a, b }, new[] { b, a } })
            {
                var bond = EnsureBond(pair[0], pair[1], now);
                if (bond == null) continue;
                // Time together helps in proportion to how the two feel about each other, and less the closer they already are.
                // With someone they cannot stand it does the opposite: forced company grates.
                float fit = ActivityTuning.TimeFit(bond.LastOpinion);
                float room = Math.Max(0.1f, 1f - bond.Attachment / 100f) * 1.6f;
                float helpful = Math.Max(0f, fit);
                bond.Attachment = BondTuning.Clamp(bond.Attachment + ActivityTuning.QualityAttachmentPerHour * hours * helpful * room);
                bond.Trust = BondTuning.Clamp(bond.Trust + ActivityTuning.QualityTrustPerHour * hours * fit * Math.Max(0.1f, 1f - bond.Trust / 100f) * 1.6f);
                bond.Familiarity = BondTuning.Clamp(bond.Familiarity + ActivityTuning.QualityFamiliarityPerHour * hours);
                float tensionBefore = bond.Tension;
                bond.Tension = BondTuning.Clamp(bond.Tension - ActivityTuning.QualityTensionReliefPerHour * hours * fit);
                if (fit < 0f) bond.Note(BondCause.Friction, bond.Tension - tensionBefore > 0f ? -(bond.Tension - tensionBefore) : -0.5f, now);
                else bond.Note(cause, ActivityTuning.QualityAttachmentPerHour * hours * helpful * room, now);
                bond.LastQualityTick = now;
                bond.QualityRecent += hours;
            }
        }

        // ------------------------------------------------------------------ who to be with

        private static float KindWeight(BondKind kind)
        {
            switch (kind)
            {
                case BondKind.Parent: return 1.3f;      // parents seek out their children
                case BondKind.Partner: return 1.2f;
                case BondKind.Child: return 1.0f;
                case BondKind.Sibling: return 0.9f;
                case BondKind.Housemate: return 0.5f;
                default: return 0f;                     // former partners are not sought out
            }
        }

        /// <summary>
        /// How much <paramref name="holderId"/> wants to spend time with each of the available people. Stronger attachment and
        /// longer since they were last together raise it; tension lowers it. Anyone below the minimum is left out.
        /// </summary>
        public List<RankedCompanion> RankCompanions(int holderId, IEnumerable<int> availableIds, int now, float appetite = 1f)
        {
            EnsureIndexes();
            var result = new List<RankedCompanion>();
            if (!personById.TryGetValue(holderId, out var holder)) return result;
            foreach (var id in availableIds.Distinct())
            {
                if (id == holderId || !personById.TryGetValue(id, out var other) || !other.InColony) continue;
                var kind = KindOf(holder, other);
                if (!kind.HasValue) continue;
                var bond = GetBond(holderId, id);
                float attachment = bond != null ? bond.Attachment : BondTuning.BaseAttachment(kind.Value);
                float tension = bond != null ? bond.Tension : 0f;
                float hoursSince = bond != null && bond.LastQualityTick > 0
                    ? Math.Max(0, now - bond.LastQualityTick) / (float)BondTuning.TicksPerHour
                    : ActivityTuning.NeverTogetherHours;
                float staleness = Math.Max(0.2f, Math.Min(3f, hoursSince / ActivityTuning.StaleHoursFullDesire));
                float desire = KindWeight(kind.Value) * (0.5f + attachment / 100f) * staleness * (1f - 0.8f * tension / 100f) * appetite;
                if (desire >= ActivityTuning.MinDesire) result.Add(new RankedCompanion { Id = id, Desire = desire });
            }
            return result.OrderByDescending(c => c.Desire).ThenBy(c => c.Id).ToList();
        }

        // ------------------------------------------------------------------ bedtime stories

        public bool StoryDue(int childId, int now)
        {
            var t = TimeFor(childId, false);
            return t == null || now - t.LastStoryTick >= ActivityTuning.StoryIntervalTicks;
        }

        public void RecordStory(int childId, int now)
        {
            TimeFor(childId, true).LastStoryTick = now;
        }

        /// <summary>The parent with the best Social skill reads; ties go to the lower id.</summary>
        public static Reader PickReader(IEnumerable<Reader> candidates)
        {
            return candidates.OrderByDescending(r => r.Social).ThenBy(r => r.Id).FirstOrDefault();
        }

        /// <summary>
        /// Skill experience a child gains from one story. With a book it follows the book's own rate for the story's length;
        /// without one the reader teaches something they know, at a lower rate. Both scale with the reader's Social skill
        /// (vanilla childcare uses Social; there is no separate childcare skill).
        /// </summary>
        public static float StoryXp(bool hasBook, float bookXpPerTick, int storyTicks, float readerSocial, float readerBestSkill)
        {
            float social = ActivityTuning.SocialFactor(readerSocial);
            if (hasBook)
                return Math.Max(0f, bookXpPerTick) * storyTicks * social * ActivityTuning.BookEfficiency;
            float knowledge = 0.5f + Math.Max(0f, Math.Min(20f, readerBestSkill)) / 20f;
            return ActivityTuning.NoBookBaseXp * social * knowledge;
        }

        // ------------------------------------------------------------------ eating together

        /// <summary>
        /// Relatives who finished a meal within the window in the same room as <paramref name="eaterId"/>, closest tie first.
        /// Only people with a real bond count, so strangers sharing a canteen are not "family dinners".
        /// </summary>
        public List<int> SharedMealPartners(int eaterId, int roomKey, int now, IEnumerable<RecentMeal> recent)
        {
            EnsureIndexes();
            var result = new List<int>();
            if (!personById.TryGetValue(eaterId, out var eater)) return result;
            foreach (var meal in recent.OrderByDescending(m => m.Tick))
            {
                if (meal.PersonId == eaterId || result.Contains(meal.PersonId)) continue;
                if (Math.Abs(now - meal.Tick) > ActivityTuning.SharedMealWindowTicks) continue;
                if (roomKey < 0 || meal.RoomKey != roomKey) continue;
                if (!personById.TryGetValue(meal.PersonId, out var other) || !other.InColony) continue;
                if (!KindOf(eater, other).HasValue) continue;
                var bond = GetBond(eaterId, meal.PersonId);
                float attachment = bond != null ? bond.Attachment : BondTuning.BaseAttachment(KindOf(eater, other).Value);
                if (attachment < ActivityTuning.SharedMealMinAttachment) continue;
                result.Add(meal.PersonId);
            }
            return result;
        }

        /// <summary>Keeps the budget table to current colonists and the last few days.</summary>
        public void PruneTimeRecords(int now)
        {
            EnsureIndexes();
            int today = DayOf(now);
            TimeRecords.RemoveAll(t => !personById.TryGetValue(t.PersonId, out var p) || !p.InColony
                                       || (t.Day < today - 2 && now - t.LastStoryTick > ActivityTuning.StoryIntervalTicks * 4));
            timeByPerson.Clear();
            foreach (var t in TimeRecords) timeByPerson[t.PersonId] = t;
        }
    }
}
