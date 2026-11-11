using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>What the framework saw when a vanilla job ended successfully. Plain data: no game types, so the rules can be tested.</summary>
    public sealed class JobFacts
    {
        public string JobDef, WorkType, RecipeSkill;
        public int JobId, ThingId;
        public bool HaulToStorage;
        public float WorkAmount;
    }

    /// <summary>
    /// Which finished vanilla work is paid, as which category, and how much of it counts. Only a job that ended <i>successfully</i> is ever presented (never one that merely started or was
    /// interrupted). Professional work (the modules' own jobs, medicine) is excluded so a profession is never paid twice for the same activity. Categories are the keys of the wage policy
    /// (<c>vanilla.&lt;category&gt;</c>).
    /// </summary>
    public static class Labour
    {
        public const string Prefix = "vanilla.";
        public const float ResearchPointsPerUnit = 400f;
        public static readonly string[] Categories = { "growing", "mining", "construction", "cooking", "cleaning", "hauling", "crafting", "animals", "research", "hunting" };

        private static readonly Dictionary<string, string> byWorkType = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "Growing", "growing" }, { "PlantCutting", "growing" }, { "Mining", "mining" }, { "Construction", "construction" }, { "Cooking", "cooking" }, { "Cleaning", "cleaning" },
            { "Hauling", "hauling" }, { "Crafting", "crafting" }, { "Smithing", "crafting" }, { "Tailoring", "crafting" }, { "Art", "crafting" }, { "Handling", "animals" }, { "Hunting", "hunting" }
        };

        public static string Role(string category) => Prefix + category;
        public static bool IsOrdinary(string role) => role != null && role.StartsWith(Prefix, StringComparison.Ordinal);

        /// <summary>The wage category of a finished job, or null if it is not paid (professional, medical, not productive, or hauling that is not into storage).</summary>
        public static string CategoryOf(JobFacts f)
        {
            if (f == null || string.IsNullOrEmpty(f.WorkType) || (f.JobDef ?? "").StartsWith("WFL_", StringComparison.Ordinal)) return null;
            if (!byWorkType.TryGetValue(f.WorkType, out var cat)) return null;
            if (cat == "hauling" && !f.HaulToStorage) return null;                     // moving things around is not productive; putting them away is
            if (cat == "crafting" && f.RecipeSkill == "Cooking") return "cooking";          // a recipe's own skill beats the bench's work type
            return cat;
        }

        /// <summary>How many units a finished job counts for: one, or for a worked recipe up to four by how much work it was, so a quick job and a long one are not paid alike.</summary>
        public static int Units(JobFacts f, string category) => category == "crafting" || category == "cooking" ? Math.Max(1, Math.Min(4, 1 + (int)(f.WorkAmount / 1500f))) : 1;

        /// <summary>The key that makes one piece of work pay once. Hauling is per thing per day (carrying the same stack back and forth earns nothing more); everything else is per job.</summary>
        public static string Key(JobFacts f, string category, int day) => category == "hauling" ? "h:" + f.ThingId + ":" + day : "j:" + f.JobId;

        /// <summary>Continuous work (research) pays one unit for every <see cref="ResearchPointsPerUnit"/> points actually performed. The remainder carries over; nothing is paid for a start.</summary>
        public static int ResearchUnits(ref float carried, float performed)
        {
            if (performed <= 0f) return 0;
            carried += performed; int units = (int)(carried / ResearchPointsPerUnit); carried -= units * ResearchPointsPerUnit;
            return units;
        }
    }
}
