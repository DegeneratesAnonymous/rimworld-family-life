using System;
using System.Collections.Generic;

namespace FamilyLife.Domain
{
    // Routine professional contact - a check-up, a lesson - is cooperative. Two things are decided separately:
    //   participation: is the person willing and able to take part at all (their trust, tension, mood, needs, history with the professional);
    //   delivery:      how well the professional does the work (their skill in the subject, their Social, preparation, the room, how tired they are).
    // The other person's Social skill does not make cooperation harder. It helps them put a question or describe a problem, which is worth a little to the result.
    // Opposed contests stay in GuidedExchanges.Resolve for genuine disagreement.

    public enum Willingness : byte
    {
        /// <summary>Able and willing now.</summary>
        Willing,
        /// <summary>Cannot just now (a need, a task, a danger): ask again later.</summary>
        Busy,
        /// <summary>Cannot travel or is not fit to join others: go to them instead of asking them to come.</summary>
        Unable,
        /// <summary>Does not want to. Respected: they are not asked again this round.</summary>
        Refuses
    }

    /// <summary>How urgent the medical need on the map is. Only Ongoing and Emergency hold back routine work.</summary>
    public enum CareLevel : byte { None, Routine, Outbreak, Ongoing, Emergency }

    /// <summary>
    /// The doctor's order of work: emergency treatment, ongoing required care, an outbreak assessment, then routine check-ups. The first two are done by the doctor's ordinary
    /// medical work; the other two give way to them. Nothing here ever cancels critical tending or rescue to begin a check-up.
    /// </summary>
    public static class CarePriority
    {
        public static bool RoutineMayProceed(CareLevel demand) => demand < CareLevel.Ongoing;
        public static bool OutbreakMayProceed(CareLevel demand) => demand < CareLevel.Ongoing;
        public static bool TendingFirst(CareLevel demand) => demand >= CareLevel.Ongoing;
    }

    public static class CareParticipation
    {
        public const float RefuseAbove = 40f;

        /// <summary>
        /// How reluctant this person is, 0 and up. Tension and unresolved grievance raise it; trust and affection lower it; kinship lowers it a little (more willing,
        /// never less). Mood matters at the edges: a person in a poor mood is a little less willing.
        /// </summary>
        public static float Reluctance(float trust, float tension, float attachment, float grievance, float mood01, bool kin, int personality)
        {
            float r = tension * .6f + grievance * .6f - trust * .35f - attachment * .15f - (kin ? 8f : 0f) - personality;
            if (mood01 < .35f) r += (.35f - mood01) * 30f;
            return Math.Max(0f, r);
        }

        /// <param name="roll">0-99: the chance of refusing above the threshold rises with how reluctant they are, so the same person is not always the same answer.</param>
        public static Willingness Decide(bool unableToTravel, bool needOrTaskUrgent, float reluctance, int roll)
        {
            if (unableToTravel) return Willingness.Unable;
            if (needOrTaskUrgent) return Willingness.Busy;
            if (reluctance <= RefuseAbove) return Willingness.Willing;
            return roll < Math.Min(85f, (reluctance - RefuseAbove) * 2.5f + 15f) ? Willingness.Refuses : Willingness.Willing;
        }
    }

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
