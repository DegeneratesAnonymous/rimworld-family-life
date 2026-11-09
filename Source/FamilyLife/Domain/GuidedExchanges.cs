using System;
using System.Collections.Generic;

namespace FamilyLife.Domain
{
    // The talk during a check-up or a class is not one block of time: the doctor or teacher raises something, and how it goes depends on how well they can talk to
    // this person. The leader's skill is Social plus a second skill that fits the job (Medical for a doctor, Intellectual for a teacher); the other person only has
    // their Social. Each exchange is one such contest, and what it gives is small - the point is that people who are good at it make a difference over many of them.

    public enum ExchangeTopic : byte
    {
        // a check-up
        Advice, Reassure, Habits, CatchUp,
        // a class
        Question, Explain, Quiz, Story, Focus
    }

    public struct ExchangeEffect
    {
        public float Attachment, Trust, Tension, Familiarity;
        /// <summary>How much this exchange adds to (or takes from) the quality of the visit or of this pupil's lesson.</summary>
        public float Quality;
    }

    public static class GuidedExchanges
    {
        public static readonly ExchangeTopic[] ClinicTopics = { ExchangeTopic.Advice, ExchangeTopic.Reassure, ExchangeTopic.Habits, ExchangeTopic.CatchUp };
        public static readonly ExchangeTopic[] ClassTopics = { ExchangeTopic.Question, ExchangeTopic.Explain, ExchangeTopic.Quiz, ExchangeTopic.Story, ExchangeTopic.Focus };

        /// <summary>Levels of Social and the job's second skill, summed; 0 for a skill the person cannot use.</summary>
        public static int LeaderScore(int social, int second) => Math.Max(0, social) + Math.Max(0, second);

        /// <summary>
        /// The leader's skill and a d20 against the other person's Social and a d20, plus a little for how hard it is to move anyone. Close contests are flat; clear wins
        /// and clear losses are the good and poor ends.
        /// </summary>
        public static EventTier Resolve(int leaderScore, int participantSocial, int leaderRoll, int participantRoll)
        {
            int margin = (leaderScore + leaderRoll) - (Math.Max(0, participantSocial) + participantRoll + 5);
            if (margin <= -8) return EventTier.Poor;
            if (margin < 5) return EventTier.Flat;
            if (margin < 15) return EventTier.Good;
            return EventTier.Great;
        }

        public static int TierValue(EventTier t) => t == EventTier.Poor ? -1 : t == EventTier.Flat ? 0 : t == EventTier.Good ? 1 : 2;

        private static float Weight(ExchangeTopic t)
        {
            switch (t)
            {
                case ExchangeTopic.CatchUp: return .5f;
                case ExchangeTopic.Focus: return .6f;
                case ExchangeTopic.Reassure: return .8f;
                case ExchangeTopic.Story: return .8f;
                default: return 1f;
            }
        }

        /// <summary>What an exchange does, from the other person's side: how they feel about the leader afterwards, and what it adds to the quality of the visit.</summary>
        public static ExchangeEffect Effect(ExchangeTopic topic, EventTier tier)
        {
            float w = Weight(topic);
            ExchangeEffect e;
            switch (tier)
            {
                case EventTier.Poor: e = new ExchangeEffect { Attachment = -.5f, Trust = -1f, Tension = 1.5f, Familiarity = .3f }; break;
                case EventTier.Flat: e = new ExchangeEffect { Attachment = .2f, Trust = .2f, Tension = 0f, Familiarity = .5f }; break;
                case EventTier.Good: e = new ExchangeEffect { Attachment = 1f, Trust = 1f, Tension = -.5f, Familiarity = .8f }; break;
                default: e = new ExchangeEffect { Attachment = 2f, Trust = 2f, Tension = -1f, Familiarity = 1f }; break;
            }
            e.Quality = TierValue(tier) * w;
            return e;
        }

        /// <summary>The topic for the nth exchange: they run through the list in an order that depends on the seed, never the same one twice running.</summary>
        public static ExchangeTopic PickTopic(ExchangeTopic[] topics, int index, int seed)
        {
            if (topics == null || topics.Length == 0) return ExchangeTopic.CatchUp;
            int start = Math.Abs(seed) % topics.Length;
            return topics[(start + index) % topics.Length];
        }

        /// <summary>How long a check-up's benefit lasts, from how the talk went: a poor visit shortens it, a good one extends it.</summary>
        public static float CheckupDurationFactor(float qualitySum) => Math.Max(.7f, Math.Min(1.6f, 1f + .12f * qualitySum));

        /// <summary>How much of a lesson a pupil takes in, from how they got on with the teacher.</summary>
        public static float LessonFactor(float qualitySum) => Math.Max(.6f, Math.Min(1.5f, 1f + .08f * qualitySum));

        /// <summary>The feeling a person is left with: 1 good, -1 poor, 0 neither.</summary>
        public static int Impression(float qualitySum) => qualitySum >= 2f ? 1 : qualitySum <= -2f ? -1 : 0;
    }
}
