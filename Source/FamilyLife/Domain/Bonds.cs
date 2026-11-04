using System;
using System.Collections.Generic;

namespace FamilyLife.Domain
{
    /// <summary>How one person is connected to another, from the first person's point of view.</summary>
    public enum BondKind : byte
    {
        Housemate = 0,
        Parent = 1,     // the holder is the other person's parent
        Child = 2,      // the holder is the other person's child
        Sibling = 3,
        Partner = 4,
        Ex = 5,
        Acquaintance = 6  // not family and not a housemate: someone the events have brought together
    }

    /// <summary>
    /// A directed relationship: how <see cref="From"/> feels about <see cref="To"/>. It complements the game's opinion and never
    /// replaces it. Each value has its own job later on: attachment drives desire to spend time together and how hard a loss
    /// lands; trust drives confiding, reconciliation and who is chosen to help; tension drives avoidance and conflict;
    /// familiarity drives how well teaching and storytelling work. All values are 0-100.
    /// </summary>
    public sealed partial class Bond
    {
        public int From;
        public int To;
        public float Attachment;
        public float Trust;
        public float Tension;
        public float Familiarity;
        public int LastTick;
        public int LastQualityTick;      // when they last spent time together on purpose; 0 = never
    }

    /// <summary>Someone who has lost someone they were attached to. The game layer turns this into a mood effect.</summary>
    public sealed class Grief
    {
        public int PersonId;
        public int LostId;
        public float Attachment;
    }

    /// <summary>
    /// Every number that shapes bonds, in one place. Rates are per in-game hour. These are tuning values, not rules: tests check
    /// directions and bounds, never the exact numbers.
    /// </summary>
    public static class BondTuning
    {
        public const float MaxHoursPerUpdate = 48f;      // a long gap is treated as at most two days
        public const int TicksPerHour = 2500;
        public const int MaxBondsPerPerson = 24;
        public const int MaxAcquaintances = 12;          // friends and others outside the family that keep a bond

        public const float FamiliarityPerHourHousehold = 0.05f;
        public const float FamiliarityPerHourTogether = 0.01f;
        public const float FamiliarityLossPerHourApart = 0.004f;

        public const float AttachmentRate = 0.012f;       // share of the gap to the target closed per hour
        public const float TrustRate = 0.008f;
        public const float TensionRate = 0.02f;
        public const float EmbitteredTension = 0.6f;      // tension target per point of negative opinion

        public const float BirthBump = 10f;
        public const float PartnerBump = 10f;
        public const float BreakupTension = 25f;
        public const float BreakupAttachmentLoss = 10f;
        public const float GriefMinAttachment = 25f;

        /// <summary>Where attachment settles for a kind of relationship before time and opinion are counted.</summary>
        public static float BaseAttachment(BondKind kind)
        {
            switch (kind)
            {
                case BondKind.Parent: return 50f;
                case BondKind.Child: return 40f;
                case BondKind.Partner: return 35f;
                case BondKind.Sibling: return 20f;
                case BondKind.Ex: return 10f;
                case BondKind.Acquaintance: return 3f;
                default: return 5f;
            }
        }

        /// <summary>
        /// Where attachment settles. Being related, and knowing someone well, sets a floor of sorts; how the two actually feel about
        /// each other decides the rest, so relatives who do not get along stay apart however long they share a house.
        /// </summary>
        public static float AttachmentTarget(BondKind kind, float familiarity, float opinion)
        {
            return BaseAttachment(kind) * 0.6f + 0.15f * familiarity + 0.5f * opinion;
        }

        public static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);

        public static string CohesionTier(float cohesion)
        {
            if (cohesion < 25f) return "Fractured";
            if (cohesion < 45f) return "Strained";
            if (cohesion < 65f) return "Steady";
            if (cohesion < 85f) return "Close";
            return "TightKnit";
        }
    }
}
