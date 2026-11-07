using System;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Several ways to say the same thing. A line is written as WFL_Something_0, WFL_Something_1 and so on (as many as the translators give), and one is
    /// chosen by a seed from who and when, so the same two people do not say the same words every time and a translation can add more without code.
    /// </summary>
    internal static class Phrasing
    {
        public static int Count(string prefix)
        {
            int n = 0;
            while ((prefix + "_" + n).CanTranslate()) n++;
            return n;
        }

        /// <summary>The seed-chosen variant of the line, or the plain key if there are no variants.</summary>
        public static TaggedString Pick(string prefix, int seed, string a = "", string b = "", string c = "")
        {
            int n = Count(prefix);
            string key = n == 0 ? prefix : prefix + "_" + (int)((uint)Math.Abs(seed == int.MinValue ? 0 : seed) % (uint)n);
            return key.Translate(a, b, c);
        }
    }
}
