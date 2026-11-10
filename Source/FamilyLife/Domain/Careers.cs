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
    /// Experience and qualifications for every pawn. Experience is reported by the module that saw the activity, once per activity key. Repeating the same kind of activity on the
    /// same day earns diminishing returns (each module sets the points per activity, so there is no universal progression limit); <see cref="SafetyCapPerDay"/> is only a configurable
    /// backstop against a buggy reporter. Qualifications are only ever added, and the ledger keeps experience for careers whose module is not installed.
    ///
    /// Activity keys must be unique per real event: build them as module id + event kind + the event's own id (a class session id, a visit id), never from the pawn alone. The pawn,
    /// career and specialty are added here, so the same event credits each person once. Remembered keys are dated and forgotten after <see cref="RememberDays"/>, which is longer
    /// than any activity can be reported again.
    /// </summary>
    public sealed class CareerLedger : IModuleState
    {
        public const string Id = "wils.careers";
        public const int RememberDays = 60, RememberedActivities = 2000;
        /// <summary>Hard backstop on experience credited to one pawn in one day, for a reporter that misbehaves. 0 turns it off. Set from the module's settings.</summary>
        public static int SafetyCapPerDay = 400;
        /// <summary>Each repeat of the same kind of activity on one day earns this share of the previous one's points (1 = no diminishing).</summary>
        public static float RepeatFactor = 0.6f;

        private sealed class Entry { public Dictionary<string, int> Xp = new Dictionary<string, int>(); public HashSet<string> Quals = new HashSet<string>(); public int Day, DayXp; public Dictionary<string, int> KindCount = new Dictionary<string, int>(); public HashSet<string> Legacy = new HashSet<string>(); }
        private readonly Dictionary<int, Entry> people = new Dictionary<int, Entry>();
        private readonly List<string> seen = new List<string>();      // "key@day", oldest first
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
        /// Credits a finished activity. <paramref name="kind"/> groups activities of one sort for diminishing returns (e.g. "teach.class"); <paramref name="activityKey"/> identifies this one
        /// event so reporting it twice credits once. Specialty experience may be earned before qualifying. Returns the experience actually credited.
        /// </summary>
        public int Award(int person, string career, string specialty, string kind, string activityKey, int points, int day)
        {
            if (person <= 0 || points <= 0 || string.IsNullOrEmpty(career) || string.IsNullOrEmpty(activityKey)) return 0;
            Prune(day);
            string key = person + "|" + career + "|" + (specialty ?? "") + "|" + activityKey;
            if (seenSet.Contains(key)) return 0;
            var e = Of(person);
            if (e.Day != day) { e.Day = day; e.DayXp = 0; e.KindCount.Clear(); }
            string k = kind ?? "";
            e.KindCount.TryGetValue(k, out int n);
            int give = Math.Max(1, (int)Math.Round(points * Math.Pow(Math.Max(0f, Math.Min(1f, RepeatFactor)), n)));
            if (SafetyCapPerDay > 0) give = Math.Min(give, SafetyCapPerDay - e.DayXp);
            if (give <= 0) return 0;
            e.KindCount[k] = n + 1; e.DayXp += give;
            e.Xp[career] = GeneralXp(person, career) + give;
            if (!string.IsNullOrEmpty(specialty)) e.Xp[Special(career, specialty)] = SpecialtyXp(person, career, specialty) + give;
            seen.Add(key + "@" + day); seenSet.Add(key);
            while (seen.Count > RememberedActivities) Forgetting(0);
            return give;
        }

        private void Forgetting(int index) { string s = seen[index]; seenSet.Remove(s.Substring(0, s.LastIndexOf('@'))); seen.RemoveAt(index); }
        private void Prune(int day) { while (seen.Count > 0 && int.TryParse(seen[0].Substring(seen[0].LastIndexOf('@') + 1), out int d) && day - d > RememberDays) Forgetting(0); }

        /// <summary>
        /// A qualification given to someone who already held the old post, so their work carries on. It records no experience (none is known) and is marked legacy so the UI can say so.
        /// Skill and experience requirements are not checked: it is a recognition of the post they held, not a result of a test.
        /// </summary>
        public bool Grandfather(int person, string career)
        {
            var e = Of(person); bool added = e.Quals.Add(career); e.Legacy.Add(career); return added;
        }
        public bool IsLegacy(int person, string career) => people.TryGetValue(person, out var e) && e.Legacy.Contains(career);

        /// <summary>Careers the ledger holds data for that no loaded definition names: a module that is absent or was renamed. Kept untouched; for the status listing.</summary>
        public List<string> UnknownCareers(IEnumerable<CareerDefinition> defs)
        {
            var known = new HashSet<string>(defs.Select(d => d.Id));
            return people.Values.SelectMany(e => e.Xp.Keys.Select(k => k.Split(':')[0]).Concat(e.Quals.Select(q => q.Split(':')[0]))).Where(c => !known.Contains(c)).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToList();
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
                w.Strings("xk" + id, xk).Ints("xv" + id, xk.Select(k => e.Xp[k]).ToList()).Strings("q" + id, e.Quals.OrderBy(q => q, StringComparer.Ordinal).ToList()).Int("d" + id, e.Day).Int("dx" + id, e.DayXp)
                    .Strings("kc" + id, e.KindCount.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => x.Key + "=" + x.Value).ToList()).Strings("lg" + id, e.Legacy.OrderBy(x => x, StringComparer.Ordinal).ToList());
            }
            return w.Strings("seen", seen).ToString();
        }

        public static CareerLedger Decode(string text) => Decode(new StateReader(text));
        public static CareerLedger Decode(StateReader r)
        {
            var l = new CareerLedger();
            foreach (int id in r.Ints("p"))
            {
                var e = l.Of(id); var k = r.Strings("xk" + id); var v = r.Ints("xv" + id);
                for (int i = 0; i < k.Count && i < v.Count; i++) e.Xp[k[i]] = v[i];
                foreach (var q in r.Strings("q" + id)) e.Quals.Add(q);
                e.Day = r.Int("d" + id); e.DayXp = r.Int("dx" + id);
                foreach (var c in r.Strings("kc" + id)) { int i = c.LastIndexOf('='); if (i > 0 && int.TryParse(c.Substring(i + 1), out int cnt)) e.KindCount[c.Substring(0, i)] = cnt; }
                foreach (var c in r.Strings("lg" + id)) e.Legacy.Add(c);
            }
            foreach (var s in r.Strings("seen")) { int i = s.LastIndexOf('@'); if (i > 0 && l.seenSet.Add(s.Substring(0, i))) l.seen.Add(s); }
            return l;
        }

        /// <summary>A person who is gone keeps nothing; qualifications belong to the pawn, not the post.</summary>
        public void Forget(int person) { people.Remove(person); }
        public IEnumerable<int> People() => people.Keys;
    }
}

namespace FamilyLife.Framework
{
    using FamilyLife.Domain;
    public sealed class CareerLedgerFactory : IStateFactory<CareerLedger>
    {
        public static readonly CareerLedgerFactory Instance = new CareerLedgerFactory();
        public int Version => 1;
        public CareerLedger Create() => new CareerLedger();
        public CareerLedger Decode(StateReader reader, int version) => CareerLedger.Decode(reader);
        public CareerLedger FromLegacy(StateReader legacy) => new CareerLedger();
    }
    public static class CareerAccess
    {
        /// <summary>The community's career ledger, created empty the first time it is asked for.</summary>
        public static CareerLedger Careers(this FamilyRegistry r) => r.Shared.Of(CareerLedger.Id, CareerLedgerFactory.Instance);

        /// <summary>Drops career data for people the registry no longer knows at all (never for anyone merely absent or dead, whose record stays). Returns how many were dropped.</summary>
        public static int ReconcileCareers(this FamilyRegistry r)
        {
            var known = new System.Collections.Generic.HashSet<int>(r.Persons.Select(p => p.Id)); var l = r.Careers(); int n = 0;
            foreach (int id in l.People().ToList()) if (!known.Contains(id)) { l.Forget(id); n++; }
            return n;
        }
    }
}
