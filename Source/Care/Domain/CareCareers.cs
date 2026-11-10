using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    // Community Care's career model, in plain data. Owner: Community Care.

    public static class CareRoles
    {
        public const string Career = "wils.care.practice";
        /// <summary>Entry level. Stable id; its title is Healer until the community has appointed a Doctor, then Nurse (CareTerms).</summary>
        public const string Healer = "wils.care.healer";
        public const string Doctor = "wils.care.doctor";
        /// <summary>Doctor: Medicine 6 and a hundred points of care experience (about a dozen check-ups). Preliminary balance values.</summary>
        public const int DoctorMedicine = 6, DoctorExperience = 100;
        public const int CheckupPoints = 8;
    }

    /// <summary>One medical specialty: its role id, the skills it needs and what its activity is. Preliminary balance values, all plain data.</summary>
    public sealed class CareSpecialty
    {
        public string Key, RoleId;
        public KeyValuePair<string, int>[] Skills;
        public int Index;
    }

    public static class CareSpecialties
    {
        /// <summary>
        /// The specialties that have a real activity in the game. Every one needs Doctor first. Geneticist is not here: the game has no gene procedure a community doctor could carry out, so
        /// nothing would be left of the role but its title (see docs/CAREERS.md).
        /// </summary>
        public static readonly CareSpecialty[] All =
        {
            Make(0, "Counselor", new KeyValuePair<string, int>("Social", 8)),
            Make(1, "Surgeon", new KeyValuePair<string, int>("Medicine", 10)),
            Make(2, "Midwife", new KeyValuePair<string, int>("Medicine", 8)),
            Make(3, "Veterinarian", new KeyValuePair<string, int>("Medicine", 6), new KeyValuePair<string, int>("Animals", 6)),
        };
        private static CareSpecialty Make(int i, string key, params KeyValuePair<string, int>[] skills) => new CareSpecialty { Index = i, Key = key, RoleId = "wils.care.specialist." + key.ToLowerInvariant(), Skills = skills };

        public static CareSpecialty ByIndex(int i) => i >= 0 && i < All.Length ? All[i] : null;
        public static CareSpecialty ByKey(string key) => All.FirstOrDefault(s => s.Key == key);
        public static IEnumerable<string> RoleIds => All.Select(s => s.RoleId);
        /// <summary>General care experience and specialty experience a specialist needs, on top of Doctor and the skills.</summary>
        public const int GeneralExperience = 150, SpecialtyExperience = 60, ActivityPoints = 10;

        public static CareerRule Rule(CareSpecialty s)
        {
            var rule = new CareerRule { GeneralXp = GeneralExperience, SpecialtyXp = SpecialtyExperience };
            foreach (var k in s.Skills) rule.Skill(k.Key, k.Value);
            return rule;
        }
        public static bool MeetsSkills(CareSpecialty s, Func<string, int> skill) => s.Skills.All(k => skill(k.Key) >= k.Value);
    }

    /// <summary>
    /// The community's professional terms for entry-level care workers. Before any Doctor has ever been appointed they are Healers; once the player appoints the first Doctor they are Nurses,
    /// and stay Nurses if the Doctor leaves or dies. It is the community's vocabulary, not anyone's promotion. Saved in a community-wide payload.
    /// </summary>
    public sealed class CareTerms : IModuleState
    {
        public const string Id = "wils.care.terms";
        public bool NurseTitles;
        public int FirstDoctorTick;

        /// <summary>Called with the number of Doctors the player has assigned. Returns true the one time the transition happens.</summary>
        public bool Observe(int assignedDoctors, int now)
        {
            if (NurseTitles || assignedDoctors <= 0) return false;
            NurseTitles = true; FirstDoctorTick = now; return true;
        }
        /// <summary>Only an explicit player-controlled reset may return to Healer (not wired to anything automatic).</summary>
        public void Reset() { NurseTitles = false; FirstDoctorTick = 0; }

        public string Encode() => new StateWriter().Bool("nurse", NurseTitles).Int("first", FirstDoctorTick).ToString();
        public static CareTerms Decode(StateReader r) => new CareTerms { NurseTitles = r.Bool("nurse"), FirstDoctorTick = r.Int("first") };
    }

    public sealed class CareTermsFactory : IStateFactory<CareTerms>
    {
        public static readonly CareTermsFactory Instance = new CareTermsFactory();
        public int Version => 1;
        public CareTerms Create() => new CareTerms();
        public CareTerms Decode(StateReader reader, int version) => CareTerms.Decode(reader);
        public CareTerms FromLegacy(StateReader legacy) => new CareTerms();
    }

    public static class CareTermsAccess
    {
        public static CareTerms CareTerms(this FamilyRegistry r) => r.Shared.Of(Domain.CareTerms.Id, CareTermsFactory.Instance);
        public static string EntryLevelTitleKey(FamilyRegistry r) => r.Shared.Has(Domain.CareTerms.Id) && r.CareTerms().NurseTitles ? "wils.care.nurse_label" : "wils.care.healer_label";
    }

    /// <summary>Each care worker's own visits today, so one worker cannot be given more than their share. In memory: after a load a worker may do a few more that day, which is harmless.</summary>
    public static class PractitionerLoad
    {
        private static readonly Dictionary<int, int[]> byPerson = new Dictionary<int, int[]>();          // person -> [day, used, lastVisitTick]
        public static int UsedToday(int person, int now) => byPerson.TryGetValue(person, out var l) && l[0] == now / ActivityTuning.TicksPerDay ? l[1] : 0;
        public static int SinceLast(int person, int now) => byPerson.TryGetValue(person, out var l) && l[2] != 0 && now >= l[2] ? now - l[2] : int.MaxValue;
        public static void Note(int person, int now)
        {
            int day = now / ActivityTuning.TicksPerDay;
            if (!byPerson.TryGetValue(person, out var l) || l[0] != day) l = byPerson[person] = new[] { day, 0, 0 };
            l[1]++; l[2] = now;
        }
        public static void Clear() { byPerson.Clear(); }
    }

    /// <summary>
    /// Who is seeing which patient. A patient is claimed by one practitioner at a time, so two care workers never examine the same person at once or duplicate a visit. A claim ends when the
    /// visit does, and in any case expires, so a visit that never finished cannot hold anyone forever. In memory only.
    /// </summary>
    public sealed class CareClaims
    {
        private readonly Dictionary<int, KeyValuePair<int, int>> byPatient = new Dictionary<int, KeyValuePair<int, int>>();          // patient -> (practitioner, expiry)
        private readonly Dictionary<string, KeyValuePair<int, int>> places = new Dictionary<string, KeyValuePair<int, int>>();

        public bool TryClaim(int patient, int practitioner, int now, int ttl = 6000)
        {
            if (patient <= 0 || practitioner <= 0 || patient == practitioner) return false;
            if (byPatient.TryGetValue(patient, out var held) && held.Value > now && held.Key != practitioner) return false;
            // a practitioner sees one patient at a time
            foreach (var kv in byPatient.ToList()) if (kv.Value.Key == practitioner && kv.Key != patient && kv.Value.Value > now) return false;
            byPatient[patient] = new KeyValuePair<int, int>(practitioner, now + ttl);
            return true;
        }
        public int HeldBy(int patient, int now) => byPatient.TryGetValue(patient, out var h) && h.Value > now ? h.Key : 0;
        public bool IsClaimed(int patient, int now) => HeldBy(patient, now) != 0;
        public void Release(int patient) { byPatient.Remove(patient); }
        public void ReleaseAll(int practitioner) { foreach (int p in byPatient.Where(kv => kv.Value.Key == practitioner).Select(kv => kv.Key).ToList()) byPatient.Remove(p); foreach (var k in places.Where(kv => kv.Value.Key == practitioner).Select(kv => kv.Key).ToList()) places.Remove(k); }

        /// <summary>A bed or room can host one practitioner at a time.</summary>
        public bool TryClaimPlace(string place, int practitioner, int now, int ttl = 12000)
        {
            if (string.IsNullOrEmpty(place)) return true;
            if (places.TryGetValue(place, out var held) && held.Value > now && held.Key != practitioner) return false;
            places[place] = new KeyValuePair<int, int>(practitioner, now + ttl); return true;
        }
        public bool PlaceTaken(string place, int practitioner, int now) => places.TryGetValue(place ?? "", out var h) && h.Value > now && h.Key != practitioner;
        public void ReleasePlace(string place) { if (place != null) places.Remove(place); }
        public void Clear() { byPatient.Clear(); places.Clear(); }
    }
}
