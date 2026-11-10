using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // Whether a patient takes part in a routine appointment, and the doctor's order of work. Owner: Community Care.

    public enum PatientResponse : byte
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

    public readonly struct RefusalCause
    {
        public readonly CareRefusalKind Kind;
        public readonly string Reason;
        public RefusalCause(CareRefusalKind kind, string reason) { Kind = kind; Reason = reason; }
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

        /// <summary>
        /// Why someone who refuses does. How they feel about this doctor (tension and grievance against trust and attachment) is a refusal of that doctor, and lapses when someone else
        /// takes the post. A poor mood with nothing against the doctor is a refusal of check-ups for now, whoever is asking. Chosen here, by the rule that decides to refuse, and
        /// recorded as given.
        /// </summary>
        public static RefusalCause CauseOf(float trust, float tension, float attachment, float grievance, float mood01)
        {
            float toward = tension * .6f + grievance * .6f - trust * .35f - attachment * .15f;          // the part of the reluctance that is about this doctor
            float mood = mood01 < .35f ? (.35f - mood01) * 30f : 0f;
            return mood > 0f && mood >= toward
                ? new RefusalCause(CareRefusalKind.ServiceWide, CareRefusalReason.LowMood)
                : new RefusalCause(CareRefusalKind.HealerSpecific, CareRefusalReason.ReluctantWithHealer);
        }

        /// <param name="roll">0-99: the chance of refusing above the threshold rises with how reluctant they are, so the same person is not always the same answer.</param>
        public static PatientResponse Decide(bool unableToTravel, bool needOrTaskUrgent, float reluctance, int roll)
        {
            if (unableToTravel) return PatientResponse.Unable;
            if (needOrTaskUrgent) return PatientResponse.Busy;
            if (reluctance <= RefuseAbove) return PatientResponse.Willing;
            return roll < Math.Min(85f, (reluctance - RefuseAbove) * 2.5f + 15f) ? PatientResponse.Refuses : PatientResponse.Willing;
        }
    }
}
