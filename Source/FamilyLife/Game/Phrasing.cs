using System;
using FamilyLife.Domain;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Several ways to say the same thing. A line is written as WFL_Something_0, WFL_Something_1 and so on (as many as the translators give), and one is
    /// chosen by a seed from who and when, so the same two people do not say the same words every time and a translation can add more without code.
    /// Moments are also given a lead-in that places them in the day and now and then a closing touch, so even a line with one wording reads differently
    /// across many moments.
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
            string key = n == 0 ? prefix : prefix + "_" + TextVariety.Index(n, seed);
            return key.Translate(a, b, c);
        }

        /// <summary>A plain line with no arguments, picked from its variants.</summary>
        public static string Say(string prefix, int seed) => Pick(prefix, seed).ToString();

        /// <summary>
        /// The moment's own wording (one of its variants if it has them), then, by chance, a lead-in for the time of day and a closing touch for how it went.
        /// </summary>
        public static string Moment(string baseKey, string tier, int seed, int hour, string a, string b, string c)
        {
            string text = Pick(baseKey + "_" + tier, seed, a, b, c ?? "").ToString();
            if (TextVariety.Chance(seed, 1, 40) && TextVariety.OpensWithName(text, a, b, c))
            {
                string lead = Pick("WFL_Lead_" + TextVariety.DayPart(hour), seed >> 3).ToString();
                if (!string.IsNullOrEmpty(lead) && !lead.StartsWith("WFL_")) text = lead + " " + text;
            }
            if (TextVariety.Chance(seed, 2, 35))
            {
                string tail = Pick("WFL_Tail_" + tier, seed >> 5).ToString();
                if (!string.IsNullOrEmpty(tail) && !tail.StartsWith("WFL_")) text = text + " " + tail;
            }
            return text;
        }
    }
}
