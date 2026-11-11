using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // Routine professional contact (a check-up, a lesson) is cooperative, and changes a relationship only gradually and in one limited place. Both Education and Community
    // Care use it; the framework owns it. See docs/OFFICES.md.

    public static class CooperativeExchange
    {
        /// <summary>The professional's delivery: the relevant skill most, Social next, then what they prepared and the room, less any tiredness.</summary>
        public static float Delivery(int subjectSkill, int social, float preparation, float facility, float fatigue) =>
            Math.Max(0, subjectSkill) * .7f + Math.Max(0, social) * .3f + Math.Max(-1.5f, Math.Min(1.5f, preparation + facility)) - Math.Max(0f, Math.Min(2f, fatigue));

        public const float BaseDifficulty = 6f;

        /// <summary>Some things are harder to get across: pulling someone's attention back, working through habits.</summary>
        public static float Difficulty(ExchangeTopic topic)
        {
            switch (topic)
            {
                case ExchangeTopic.Habits: case ExchangeTopic.Quiz: case ExchangeTopic.Focus: return BaseDifficulty + 1f;
                case ExchangeTopic.CatchUp: case ExchangeTopic.Story: return BaseDifficulty - 1f;
                default: return BaseDifficulty;
            }
        }

        /// <summary>What the other person's own ability to say what they need is worth: at most one point, from their Social.</summary>
        public static float Engagement(int participantSocial) => Math.Max(0, Math.Min(20, participantSocial)) / 20f;

        /// <summary>The result of one cooperative exchange. <paramref name="noise"/> is a whole number from -2 to +2.</summary>
        public static EventTier Tier(float delivery, float difficulty, float engagement, int noise)
        {
            float margin = delivery + engagement + Math.Max(-2, Math.Min(2, noise)) - difficulty;
            if (margin <= -3f) return EventTier.Poor;
            if (margin < 2f) return EventTier.Flat;
            if (margin < 6f) return EventTier.Good;
            return EventTier.Great;
        }
    }

    /// <summary>
    /// What routine professional contact does to a relationship. Contact mostly makes the two more familiar. Trust grows when the work is competent. Closeness grows only
    /// from a meaningful personal moment done very well. Tension rises only when it went badly. A pair's contact is limited per day with falling returns, and the gain in
    /// closeness falls as they get closer, so a doctor or teacher cannot become everyone's best friend by turning up.
    /// </summary>
    public static class ProfessionalContact
    {
        public const int MaxPerPairPerDay = 3;

        /// <summary>1 for the first contact of the day between the two, then less, then nothing.</summary>
        public static float DayScale(int contactsToday) => contactsToday <= 0 ? 1f : contactsToday == 1 ? .5f : contactsToday == 2 ? .25f : 0f;

        public static bool Personal(ExchangeTopic t) => t == ExchangeTopic.Reassure || t == ExchangeTopic.CatchUp || t == ExchangeTopic.Story;

        /// <summary>The effect of one exchange, from the other person's side. Only Familiarity, Trust, Attachment and Tension are used.</summary>
        public static ExchangeEffect Effect(ExchangeTopic topic, EventTier tier, float currentAttachment)
        {
            var e = new ExchangeEffect();
            switch (tier)
            {
                case EventTier.Poor: e.Familiarity = .3f; e.Tension = .8f; e.Trust = -.3f; break;
                case EventTier.Flat: e.Familiarity = .6f; break;
                case EventTier.Good: e.Familiarity = .8f; e.Trust = .35f; break;
                default: e.Familiarity = 1f; e.Trust = .7f; break;
            }
            // closeness: only a Great personal moment, and less the closer they already are
            if (tier == EventTier.Great && Personal(topic)) e.Attachment = .3f * Math.Max(0f, 1f - currentAttachment / 40f);
            e.Quality = GuidedExchanges.TierValue(tier) * GuidedExchanges.Weight(topic);
            return e;
        }

        /// <summary>The professional's own feeling about the person is only familiarity, and a little of it.</summary>
        public static BondChange LeaderSide() => new BondChange(0f, 0f, 0f, .3f);
    }
}
