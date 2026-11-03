using System.Collections.Generic;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Turns a pawn's traits into a character profile. Traits the mod knows by name use that reading. A trait it does not know (from
    /// some other mod) gets a conservative reading from the structured stat effects the trait itself declares, small in size, and
    /// is never read from its description text. Anything that says nothing useful counts as neutral.
    /// </summary>
    internal static class TraitReader
    {
        private static readonly Dictionary<string, KeyValuePair<Dim, float>[]> cache = new Dictionary<string, KeyValuePair<Dim, float>[]>();
        private static readonly KeyValuePair<Dim, float>[] none = new KeyValuePair<Dim, float>[0];

        public static float[] ProfileOf(Pawn pawn)
        {
            var contributions = new List<KeyValuePair<Dim, float>>();
            var traits = pawn?.story?.traits?.allTraits;
            if (traits != null)
                foreach (var trait in traits)
                    if (trait?.def != null) contributions.AddRange(ReadingFor(trait));
            return TraitTags.ProfileOf(contributions);
        }

        private static KeyValuePair<Dim, float>[] ReadingFor(Trait trait)
        {
            string key = trait.def.defName + ":" + trait.Degree;
            if (cache.TryGetValue(key, out var cached)) return cached;
            var reading = TraitTags.For(trait.def.defName, trait.Degree) ?? FromStructuredEffects(trait);
            cache[key] = reading;
            return reading;
        }

        private static KeyValuePair<Dim, float>[] FromStructuredEffects(Trait trait)
        {
            var result = new List<KeyValuePair<Dim, float>>();
            var data = trait.CurrentData;
            if (data?.statOffsets == null) return none;
            foreach (var offset in data.statOffsets)
            {
                if (offset?.stat == null) continue;
                float sign = offset.value > 0f ? 1f : (offset.value < 0f ? -1f : 0f);
                if (sign == 0f) continue;
                switch (offset.stat.defName)
                {
                    case "SocialImpact":
                    case "NegotiationAbility":
                    case "TradePriceImprovement":
                        result.Add(new KeyValuePair<Dim, float>(Dim.Warmth, 0.3f * sign)); break;
                    case "MentalBreakThreshold":
                        result.Add(new KeyValuePair<Dim, float>(Dim.Steadiness, -0.4f * sign)); break;      // a higher threshold means breaking more easily
                    case "ShootingAccuracyPawn":
                    case "MeleeHitChance":
                        result.Add(new KeyValuePair<Dim, float>(Dim.Martial, 0.3f * sign)); break;
                    case "WorkSpeedGlobal":
                        result.Add(new KeyValuePair<Dim, float>(Dim.Craft, 0.3f * sign)); break;
                    case "GlobalLearningFactor":
                    case "ResearchSpeed":
                        result.Add(new KeyValuePair<Dim, float>(Dim.Scholarly, 0.3f * sign)); break;
                }
            }
            return result.Count == 0 ? none : result.ToArray();
        }

        public static SkillRecord BestSkill(Pawn pawn)
        {
            SkillRecord best = null;
            var skills = pawn?.skills?.skills;
            if (skills == null) return null;
            foreach (var s in skills)
                if (!s.TotallyDisabled && (best == null || s.Level > best.Level)) best = s;
            return best;
        }
    }
}
