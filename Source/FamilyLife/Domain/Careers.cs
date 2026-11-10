using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    // Professional careers: what a pawn is QUALIFIED to do. Separate from what the player ASSIGNS them to do, and from any community office (political standing). Qualification is earned
    // from vanilla skills plus accumulated professional experience, kept for good once earned, and never depends on an election. Modules define their careers; the framework only counts.

    /// <summary>What a level needs. Skills are minimum vanilla levels; Level 1 has no rule at all.</summary>
    public sealed class CareerRule
    {
        public Dictionary<string, int> Skills = new Dictionary<string, int>();
        public int GeneralXp, SpecialtyXp;
        public CareerRule Skill(string skill, int min) { Skills[skill] = min; return this; }
    }

    /// <summary>A career a module defines: level 2 (qualified professional) and any number of level 3 specialties. All thresholds are data, so they can be tuned or overridden.</summary>
    public sealed class CareerDefinition
    {
        public string Id, ModuleId;
        public CareerRule Level2 = new CareerRule();
        public Dictionary<string, CareerRule> Specialties = new Dictionary<string, CareerRule>();
    }

    /// <summary>
    /// Experience and qualifications for every pawn. Experience is reported by the module that saw the activity, once per activity key, with a daily cap per pawn so trivial
    /// repetition cannot be farmed. Qualifications are only ever added.
    /// </summary>
    public sealed class CareerLedger : IModuleState
    {
        public const string Id = "wils.careers";
        public const int DailyCap = 40, RememberedActivities = 400;

        private sealed class Entry { public Dictionary<string, int> Xp = new Dictionary<string, int>(); public HashSet<string> Quals = new HashSet<string>(); public int Day, DayXp; }
        private readonly Dictionary<int, Entry> people = new Dictionary<int, Entry>();
        private readonly List<string> seen = new List<string>();
        private readonly HashSet<string> seenSet = new HashSet<string>();

        private Entry Of(int person) { if (!people.TryGetValue(person, out var e)) people[person] = e = new Entry(); return e; }
        private static string Special(string career, string specialty) => career + ":" + specialty;

        public int GeneralXp(int person, string career) => people.TryGetValue(person, out var e) && e.Xp.TryGetValue(career, out var v) ? v : 0;
        public int SpecialtyXp(int person, string career, string specialty) => people.TryGetValue(person, out var e) && e.Xp.TryGetValue(Special(career, specialty), out var v) ? v : 0;
        public bool IsQualified(int person, string career) => people.TryGetValue(person, out var e) && e.Quals.Contains(career);
        public bool IsSpecialist(int person, string career, string specialty) => people.TryGetValue(person, out var e) && e.Quals.Contains(Special(career, specialty));
        public IEnumerable<string> Specialties(int person, string career) => people.TryGetValue(person, out var e)
            ? e.Quals.Where(q => q.StartsWith(career + ":", StringComparison.Ordinal)).Select(q => q.Substring(career.Length + 1)).OrderBy(s => s, StringComparer.Ordinal) : Enumerable.Empty<string>();

        /// <summary>Level 1 needs nothing: anyone the game lets do the work may be assigned. Levels 2 and 3 are qualifications.</summary>
        public int Level(int person, string career) => !IsQualified(person, career) ? 1 : Specialties(person, career).Any() ? 3 : 2;

        /// <summary>
        /// Credits a finished activity. <paramref name="activityKey"/> identifies it (module, kind, participants, day) so reporting it twice credits once. Specialty experience may be earned
        /// before qualifying. Returns the experience actually credited (0 for a repeat or when the day's cap is reached).
        /// </summary>
        public int Award(int person, string career, string specialty, string activityKey, int points, int day)
        {
            if (person <= 0 || points <= 0 || string.IsNullOrEmpty(career) || string.IsNullOrEmpty(activityKey)) return 0;
            string key = person + "|" + career + "|" + (specialty ?? "") + "|" + activityKey;
            if (seenSet.Contains(key)) return 0;
            var e = Of(person);
            if (e.Day != day) { e.Day = day; e.DayXp = 0; }
            int give = Math.Min(points, DailyCap - e.DayXp);
            if (give <= 0) return 0;
            e.DayXp += give;
            e.Xp[career] = GeneralXp(person, career) + give;
            if (!string.IsNullOrEmpty(specialty)) e.Xp[Special(career, specialty)] = SpecialtyXp(person, career, specialty) + give;
            seen.Add(key); seenSet.Add(key);
            while (seen.Count > RememberedActivities) { seenSet.Remove(seen[0]); seen.RemoveAt(0); }
            return give;
        }

        public static bool Meets(CareerRule rule, Func<string, int> skill) => rule.Skills.All(s => skill(s.Key) >= s.Value);

        /// <summary>Awards any qualification the pawn now meets. Returns the new ones as "career" or "career:specialty". Earned qualifications are never removed, so later skill decay costs nothing.</summary>
        public List<string> Evaluate(int person, CareerDefinition def, Func<string, int> skill)
        {
            var gained = new List<string>(); var e = Of(person);
            if (!e.Quals.Contains(def.Id) && GeneralXp(person, def.Id) >= def.Level2.GeneralXp && Meets(def.Level2, skill)) { e.Quals.Add(def.Id); gained.Add(def.Id); }
            if (e.Quals.Contains(def.Id))
                foreach (var s in def.Specialties.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    string q = Special(def.Id, s.Key);
                    if (!e.Quals.Contains(q) && GeneralXp(person, def.Id) >= s.Value.GeneralXp && SpecialtyXp(person, def.Id, s.Key) >= s.Value.SpecialtyXp && Meets(s.Value, skill)) { e.Quals.Add(q); gained.Add(q); }
                }
            return gained;
        }

        /// <summary>Which of a rule's requirements are still unmet, for showing progress. Empty when the rule is met.</summary>
        public List<string> Missing(int person, string career, string specialty, CareerRule rule, Func<string, int> skill)
        {
            var list = rule.Skills.Where(s => skill(s.Key) < s.Value).Select(s => s.Key + " " + s.Value).ToList();
            int g = GeneralXp(person, career); if (g < rule.GeneralXp) list.Add("experience " + g + "/" + rule.GeneralXp);
            if (specialty != null) { int x = SpecialtyXp(person, career, specialty); if (x < rule.SpecialtyXp) list.Add(specialty + " experience " + x + "/" + rule.SpecialtyXp); }
            return list;
        }

        public string Encode()
        {
            var w = new StateWriter();
            var ids = people.Keys.OrderBy(i => i).ToList();
            w.Ints("p", ids);
            foreach (int id in ids)
            {
                var e = people[id]; var xk = e.Xp.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
                w.Strings("xk" + id, xk).Ints("xv" + id, xk.Select(k => e.Xp[k]).ToList()).Strings("q" + id, e.Quals.OrderBy(q => q, StringComparer.Ordinal).ToList()).Int("d" + id, e.Day).Int("dx" + id, e.DayXp);
            }
            return w.Strings("seen", seen).ToString();
        }

        public static CareerLedger Decode(string text)
        {
            var r = new StateReader(text); var l = new CareerLedger();
            foreach (int id in r.Ints("p"))
            {
                var e = l.Of(id); var k = r.Strings("xk" + id); var v = r.Ints("xv" + id);
                for (int i = 0; i < k.Count && i < v.Count; i++) e.Xp[k[i]] = v[i];
                foreach (var q in r.Strings("q" + id)) e.Quals.Add(q);
                e.Day = r.Int("d" + id); e.DayXp = r.Int("dx" + id);
            }
            foreach (var s in r.Strings("seen")) if (l.seenSet.Add(s)) l.seen.Add(s);
            return l;
        }

        /// <summary>A person who is gone keeps nothing; qualifications belong to the pawn, not the post.</summary>
        public void Forget(int person) { people.Remove(person); }
    }
}
