using System;

namespace FamilyLife.Domain
{
    /// <summary>
    /// The choices behind varied wording, kept free of the game so they can be tested: which variant of a line to use, whether to add a lead-in or a
    /// closing touch, and which part of the day it is. Everything is a pure function of a seed, so the same moment always reads the same way but two
    /// different moments between the same people do not.
    /// </summary>
    public static class TextVariety
    {
        public static int Index(int count, int seed)
            => count <= 0 ? 0 : (int)((uint)Math.Abs(seed == int.MinValue ? 0 : seed) % (uint)count);

        /// <summary>True for roughly <paramref name="percent"/> out of 100 seeds, independently for each salt.</summary>
        public static bool Chance(int seed, int salt, int percent)
            => CrestStyle.Mix("chance", unchecked(seed * 31 + salt)) % 100 < percent;

        public static string DayPart(int hour)
            => hour >= 5 && hour < 8 ? "Dawn" : hour >= 8 && hour < 12 ? "Morning" : hour >= 12 && hour < 17 ? "Afternoon" : hour >= 17 && hour < 21 ? "Evening" : "Night";

        public static int Seed(string text, int a, int b, int hourOfTick) => CrestStyle.Mix(text ?? "", unchecked(a * 7919 + b * 104729 + hourOfTick));

        /// <summary>A lead-in only suits a line that opens with someone's name; any other opening would read wrongly after one.</summary>
        public static bool OpensWithName(string line, params string[] names)
        {
            if (string.IsNullOrEmpty(line)) return false;
            foreach (var n in names) if (!string.IsNullOrEmpty(n) && line.StartsWith(n, StringComparison.Ordinal)) return true;
            return false;
        }
    }
}
