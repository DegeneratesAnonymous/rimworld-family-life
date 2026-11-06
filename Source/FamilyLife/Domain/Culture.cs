using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>
    /// The dimensions a family's character is described in: seven interests, and four temperament axes where the sign says which
    /// end (for example Warmth above zero is warm, below zero guarded). Replaces a list of yes/no tags so "forgiving" and
    /// "volatile" are one scale instead of two boxes.
    /// </summary>
    public enum Dim : byte
    {
        Hearth = 0, Scholarly = 1, Martial = 2, Craft = 3, Revelry = 4, Faith = 5, Status = 6,
        Warmth = 7,        // + warm, - guarded
        Steadiness = 8,    // + even-tempered, - volatile
        Rootedness = 9,    // + rooted, - restless
        Devotion = 10      // + devoted to family, - independent
    }

    public static class DimInfo
    {
        public const int Count = 11;
        public static bool IsAxis(Dim d) => d >= Dim.Warmth;
    }

    /// <summary>
    /// What a trait says about a person's character, by trait name. Traits from the base game and from the trait mods installed
    /// with it are listed by name; a trait that is not listed contributes nothing (the game layer may add a conservative reading of
    /// its stat effects). Nothing here depends on any of those mods being present.
    /// </summary>
    public static class TraitTags
    {
        private static readonly Dictionary<string, KeyValuePair<Dim, float>[]> table = Build();

        private static KeyValuePair<Dim, float>[] T(params object[] pairs)
        {
            var list = new List<KeyValuePair<Dim, float>>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) list.Add(new KeyValuePair<Dim, float>((Dim)pairs[i], Convert.ToSingle(pairs[i + 1])));
            return list.ToArray();
        }

        private static Dictionary<string, KeyValuePair<Dim, float>[]> Build()
        {
            var d = new Dictionary<string, KeyValuePair<Dim, float>[]>(StringComparer.Ordinal);
            Func<object[], KeyValuePair<Dim, float>[]> t = a => T(a);
            // Base game. Degree-specific entries use "Name:degree".
            d["Kind"] = t(new object[] { Dim.Warmth, 1.0, Dim.Hearth, 0.5 });
            d["Abrasive"] = t(new object[] { Dim.Warmth, -1.0 });
            d["Jealous"] = t(new object[] { Dim.Devotion, 0.5, Dim.Steadiness, -0.7 });
            d["Greedy"] = t(new object[] { Dim.Status, 1.0 });
            d["Ascetic"] = t(new object[] { Dim.Status, -1.0 });
            d["Bloodlust"] = t(new object[] { Dim.Martial, 1.0, Dim.Steadiness, -0.5 });
            d["Brawler"] = t(new object[] { Dim.Martial, 0.7 });
            d["Psychopath"] = t(new object[] { Dim.Warmth, -1.0, Dim.Devotion, -0.5 });
            d["Cannibal"] = t(new object[] { Dim.Warmth, -0.4 });
            d["Pyromaniac"] = t(new object[] { Dim.Steadiness, -0.7 });
            d["Gourmand"] = t(new object[] { Dim.Hearth, 0.7 });
            d["FastLearner"] = t(new object[] { Dim.Scholarly, 0.7 });
            d["SlowLearner"] = t(new object[] { Dim.Scholarly, -0.4 });
            d["GreatMemory"] = t(new object[] { Dim.Scholarly, 0.5 });
            d["TorturedArtist"] = t(new object[] { Dim.Craft, 0.7, Dim.Steadiness, -0.5 });
            d["Recluse"] = t(new object[] { Dim.Warmth, -0.5, Dim.Rootedness, 0.5 });
            d["Wimp"] = t(new object[] { Dim.Martial, -0.5 });
            d["Nerves:2"] = t(new object[] { Dim.Steadiness, 1.0 });
            d["Nerves:1"] = t(new object[] { Dim.Steadiness, 0.5 });
            d["Nerves:-1"] = t(new object[] { Dim.Steadiness, -0.5 });
            d["Nerves:-2"] = t(new object[] { Dim.Steadiness, -1.0 });
            d["Neurotic:1"] = t(new object[] { Dim.Steadiness, -0.5 });
            d["Neurotic:2"] = t(new object[] { Dim.Steadiness, -1.0 });
            d["NaturalMood:2"] = t(new object[] { Dim.Warmth, 0.5, Dim.Steadiness, 0.5 });
            d["NaturalMood:1"] = t(new object[] { Dim.Warmth, 0.3 });
            d["NaturalMood:-1"] = t(new object[] { Dim.Warmth, -0.3 });
            d["NaturalMood:-2"] = t(new object[] { Dim.Warmth, -0.5, Dim.Steadiness, -0.5 });
            d["Industriousness:2"] = t(new object[] { Dim.Craft, 1.0 });
            d["Industriousness:1"] = t(new object[] { Dim.Craft, 0.5 });
            d["Industriousness:-1"] = t(new object[] { Dim.Craft, -0.4 });
            d["Industriousness:-2"] = t(new object[] { Dim.Craft, -0.8 });
            // Vanilla Traits Expanded
            d["VTE_Workaholic"] = t(new object[] { Dim.Craft, 0.7, Dim.Hearth, -0.5, Dim.Devotion, -0.3 });
            d["VTE_FunLoving"] = t(new object[] { Dim.Revelry, 1.0 });
            d["VTE_Perfectionist"] = t(new object[] { Dim.Craft, 0.7, Dim.Steadiness, -0.3 });
            d["VTE_Neat"] = t(new object[] { Dim.Hearth, 0.4 });
            d["VTE_Slob"] = t(new object[] { Dim.Hearth, -0.4 });
            d["VTE_Vengeful"] = t(new object[] { Dim.Steadiness, -1.0, Dim.Warmth, -0.5 });
            d["VTE_Rebel"] = t(new object[] { Dim.Devotion, -1.0, Dim.Steadiness, -0.4 });
            d["VTE_Submissive"] = t(new object[] { Dim.Devotion, 0.5 });
            d["VTE_Wanderlust"] = t(new object[] { Dim.Rootedness, -1.5 });
            d["VTE_Academian"] = t(new object[] { Dim.Scholarly, 1.5 });
            d["VTE_Prodigy"] = t(new object[] { Dim.Scholarly, 0.7 });
            d["VTE_Gastronomist"] = t(new object[] { Dim.Hearth, 1.2 });
            d["VTE_RefinedPalate"] = t(new object[] { Dim.Hearth, 0.5, Dim.Status, 0.5 });
            d["VTE_Tycoon"] = t(new object[] { Dim.Status, 1.2 });
            d["VTE_Snob"] = t(new object[] { Dim.Status, 1.0, Dim.Warmth, -0.5 });
            d["VTE_Brave"] = t(new object[] { Dim.Martial, 0.5 });
            d["VTE_Coward"] = t(new object[] { Dim.Martial, -0.5 });
            d["VTE_MartialArtist"] = t(new object[] { Dim.Martial, 1.0 });
            d["VTE_Anxious"] = t(new object[] { Dim.Steadiness, -0.8 });
            d["VTE_ThickSkinned"] = t(new object[] { Dim.Steadiness, 0.7 });
            d["VTE_ThinSkinned"] = t(new object[] { Dim.Steadiness, -0.7 });
            d["VTE_AnimalLover"] = t(new object[] { Dim.Hearth, 0.5, Dim.Warmth, 0.3 });
            d["VTE_Lush"] = t(new object[] { Dim.Revelry, 0.7 });
            d["VTE_DrunkenMaster"] = t(new object[] { Dim.Revelry, 0.7 });
            d["VTE_Stoner"] = t(new object[] { Dim.Revelry, 0.5 });
            d["VTE_CouchPotato"] = t(new object[] { Dim.Rootedness, 0.7 });
            d["VTE_Eccentric"] = t(new object[] { Dim.Rootedness, -0.3 });
            d["VTE_Schizoid"] = t(new object[] { Dim.Warmth, -0.7 });
            d["VTE_WorldWeary"] = t(new object[] { Dim.Warmth, -0.3, Dim.Rootedness, 0.3 });
            // RimTraits - General Traits
            d["RT_Valiant"] = t(new object[] { Dim.Martial, 0.7 });
            d["RT_Craven"] = t(new object[] { Dim.Martial, -0.5 });
            d["RT_Rowdy"] = t(new object[] { Dim.Revelry, 0.7, Dim.Steadiness, -0.5 });
            d["RT_Composed"] = t(new object[] { Dim.Steadiness, 0.8 });
            d["RT_Persuasive"] = t(new object[] { Dim.Warmth, 0.5, Dim.Status, 0.3 });
            d["RT_PatientCook"] = t(new object[] { Dim.Hearth, 1.0 });
            d["RT_ImpatientCook"] = t(new object[] { Dim.Hearth, -0.3 });
            d["RT_Restless"] = t(new object[] { Dim.Rootedness, -0.7 });
            d["RT_Restful"] = t(new object[] { Dim.Rootedness, 0.7 });
            d["RT_EasilyAmused"] = t(new object[] { Dim.Revelry, 0.5 });
            d["RT_Inquisitive"] = t(new object[] { Dim.Scholarly, 1.0 });
            d["RT_Ignorant"] = t(new object[] { Dim.Scholarly, -0.7 });
            d["RT_Tidy"] = t(new object[] { Dim.Hearth, 0.4 });
            d["RT_Messy"] = t(new object[] { Dim.Hearth, -0.4 });
            d["RT_Athletic"] = t(new object[] { Dim.Martial, 0.3 });
            // RimTraits - Medieval Talents
            foreach (var n in new[] { "Cook", "Chef", "Kitchenhand" }) d["RT_Talent" + n] = t(new object[] { Dim.Hearth, 0.8 });
            foreach (var n in new[] { "Herbalist", "Physician", "Surgeon", "Veterinarian" }) d["RT_Talent" + n] = t(new object[] { Dim.Hearth, 0.5, Dim.Warmth, 0.3 });
            foreach (var n in new[] { "Scholar", "Philosopher" }) d["RT_Talent" + n] = t(new object[] { Dim.Scholarly, 0.8 });
            foreach (var n in new[] { "Bowman", "Swordsman", "WeaponsMaster", "Hunter", "Armourer", "Fletcher", "Hero" }) d["RT_Talent" + n] = t(new object[] { Dim.Martial, 0.7 });
            foreach (var n in new[] { "Smith", "Builder", "Miner", "Tailor", "Artist", "Engineer", "Architect", "Farmer", "Herder", "Groundskeeper" }) d["RT_Talent" + n] = t(new object[] { Dim.Craft, 0.7 });
            foreach (var n in new[] { "Trader", "Recruiter", "Herald" }) d["RT_Talent" + n] = t(new object[] { Dim.Status, 0.5 });
            foreach (var n in new[] { "Adventurer", "Scout" }) d["RT_Talent" + n] = t(new object[] { Dim.Rootedness, -0.7 });
            // The Sims Traits
            d["ST_FamilyOriented"] = t(new object[] { Dim.Hearth, 1.0, Dim.Devotion, 1.0 });
            d["ST_Loyal"] = t(new object[] { Dim.Devotion, 1.5 });
            d["ST_NonCommital"] = t(new object[] { Dim.Devotion, -1.5, Dim.Rootedness, -0.5 });
            d["ST_Devout"] = t(new object[] { Dim.Faith, 1.5 });
            d["ST_PartyAnimal"] = t(new object[] { Dim.Revelry, 1.5 });
            d["ST_Bookworm"] = t(new object[] { Dim.Scholarly, 1.2 });
            d["ST_Proper"] = t(new object[] { Dim.Status, 0.8 });
            d["ST_Materialistic"] = t(new object[] { Dim.Status, 1.0 });
            d["ST_Narcissist"] = t(new object[] { Dim.Status, 0.7, Dim.Warmth, -0.5 });
            d["ST_Grumpy"] = t(new object[] { Dim.Warmth, -0.7, Dim.Steadiness, -0.5 });
            d["ST_Goofball"] = t(new object[] { Dim.Revelry, 1.0 });
            d["ST_Emotional"] = t(new object[] { Dim.Steadiness, -0.7, Dim.Warmth, 0.3 });
            d["ST_Shy"] = t(new object[] { Dim.Warmth, -0.5 });
            d["ST_Paranoid"] = t(new object[] { Dim.Warmth, -1.0 });
            d["ST_Zen"] = t(new object[] { Dim.Steadiness, 1.0 });
            d["ST_Chatterbox"] = t(new object[] { Dim.Warmth, 0.5 });
            d["ST_Childish"] = t(new object[] { Dim.Revelry, 0.5 });
            d["ST_Manipulative"] = t(new object[] { Dim.Warmth, -1.0 });
            d["ST_Naive"] = t(new object[] { Dim.Warmth, 0.3 });
            d["ST_HealthNut"] = t(new object[] { Dim.Hearth, 0.3 });
            d["ST_Handy"] = t(new object[] { Dim.Craft, 1.0 });
            d["ST_Virtuoso"] = t(new object[] { Dim.Craft, 0.7 });
            d["ST_TechWhiz"] = t(new object[] { Dim.Scholarly, 0.5, Dim.Craft, 0.5 });
            d["ST_Insane"] = t(new object[] { Dim.Steadiness, -1.0 });
            d["ST_HighMaintenance"] = t(new object[] { Dim.Status, 0.7 });
            d["ST_Gambler"] = t(new object[] { Dim.Revelry, 0.7 });
            d["ST_Nosy"] = t(new object[] { Dim.Warmth, -0.3 });
            d["ST_Observant"] = t(new object[] { Dim.Scholarly, 0.4 });
            d["ST_Daredevil"] = t(new object[] { Dim.Martial, 0.5, Dim.Steadiness, -0.3 });
            return d;
        }

        /// <summary>The reading for a trait, or null if it is not listed. A degree-specific entry wins over the general one.</summary>
        public static KeyValuePair<Dim, float>[] For(string defName, int degree)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            if (table.TryGetValue(defName + ":" + degree, out var specific)) return specific;
            return table.TryGetValue(defName, out var general) ? general : null;
        }

        public static bool IsKnown(string defName, int degree) => For(defName, degree) != null;

        /// <summary>The sum of a person's traits across the dimensions, each contribution limited to +-3 so one pile of traits cannot dominate.</summary>
        public static float[] ProfileOf(IEnumerable<KeyValuePair<Dim, float>> contributions)
        {
            var profile = new float[DimInfo.Count];
            foreach (var c in contributions) profile[(int)c.Key] += c.Value;
            for (int i = 0; i < profile.Length; i++) profile[i] = Math.Max(-3f, Math.Min(3f, profile[i]));
            return profile;
        }
    }

    /// <summary>A custom of a family: it comes from what the family values and is held on a rhythm.</summary>
    public sealed class TraditionDef
    {
        public string Id;
        public Dim Dim;
        public int IntervalDays;
        public int Priority;
    }

    public static class TraditionCatalog
    {
        public const float AdoptAt = 22f;          // culture value at which a family takes the tradition up
        public const float AbandonBelow = 8f;      // and the value below which it lets it go
        public const int MinPeopleInColony = 3;
        public const float MinCohesion = 25f;      // a fractured family does not celebrate together

        public static readonly TraditionDef[] All =
        {
            new TraditionDef { Id = "feast",     Dim = Dim.Hearth,   IntervalDays = 12 },
            new TraditionDef { Id = "reading",   Dim = Dim.Scholarly, IntervalDays = 15 },
            new TraditionDef { Id = "hunt",      Dim = Dim.Martial,  IntervalDays = 20 },
            new TraditionDef { Id = "craftfair", Dim = Dim.Craft,    IntervalDays = 20 },
            new TraditionDef { Id = "party",     Dim = Dim.Revelry,  IntervalDays = 10 },
            new TraditionDef { Id = "vigil",     Dim = Dim.Faith,    IntervalDays = 18 },
            new TraditionDef { Id = "foundersday", Dim = Dim.Status, IntervalDays = 30 },
            new TraditionDef { Id = "reunion",   Dim = Dim.Devotion, IntervalDays = 25 }
        };

        public static int IndexOf(string id)
        {
            for (int i = 0; i < All.Length; i++) if (All[i].Id == id) return i;
            return -1;
        }
    }

    /// <summary>A tradition a lineage has taken up.</summary>
    public sealed partial class TraditionRecord
    {
        public string Id = "";
        public int AdoptedTick;
        public int LastHeldTick;
        public int TimesHeld;
        public bool Muted;          // the player asked the family to skip it
    }

    /// <summary>A gathering that should happen: a tradition coming due, a coming of age, a memorial. The game layer carries it out.</summary>
    public sealed partial class GatheringRequest
    {
        public int Id;
        public string Kind = "";        // a tradition id, or "comingofage" / "memorial"
        public int LineageId;
        public int SubjectId;           // whom a coming of age or memorial is about, else 0
        public int NotBefore;
        public int Expires;
        public int Attempts;
        public CeremonyKind Ceremony;
        public CeremonyState State;
        public CeremonyInfluence Influences;
        public int SecondLineageId;
        public int SubjectBId;
        public int CreatedTick;
        public int StartedTick;
        public int OrganizerId;
        public int MapId = -1;
        public int Seed;
        public int MomentBudget;
        public int MomentsResolved;
        public List<int> InvitedIds = new List<int>();
        public List<int> VisitorIds = new List<int>();
    }

    public enum MemoryKind : byte { Birth = 0, Loss = 1, Wedding = 2, Parting = 3, CameOfAge = 4, Reunion = 5, Anniversary = 6, Tradition = 7, Reconciliation = 8, Joined = 9, StepFamily = 10, Bond = 11, Rupture = 12 }

    /// <summary>
    /// A defining moment in a lineage's history. Only moments weighty enough are kept, and only a bounded number: the rest of
    /// family life is counted, not stored. No text is stored; it is built from these fields when shown.
    /// </summary>
    public sealed partial class Memory
    {
        public int LineageId;
        public MemoryKind Kind;
        public int Tick;
        public int A;
        public int B;
        public float Weight;
    }

    public sealed partial class Lineage
    {
        public List<float> Culture = new List<float>(new float[DimInfo.Count]);
        public int LastCultureTick;
        public List<TraditionRecord> Traditions = new List<TraditionRecord>();
        public int StoriesTold;
        public int MealsShared;
        public int LastGrandReunionTick;
        public int NextCeremonyCheckTick;
        public float Standing;

        public float CultureOf(Dim d)
        {
            int i = (int)d;
            return Culture != null && i < Culture.Count ? Culture[i] : 0f;
        }
    }

    public static class CultureTuning
    {
        public const float ProfileScale = 55f;       // a family whose members average +1 on a dimension has a value of +55; real families average far less, so this is scaled to what they actually reach
        public const float DailyDrift = 0.04f;       // share of the gap to the inherited tendency closed per day
        public const float MaxDays = 10f;
        public const float AdultWeight = 1f;
        public const float JuvenileWeight = 0.4f;
        public const float MemoryThreshold = 1.0f;
        public const int MaxMemoriesPerLineage = 40;
        public const int LegacyMinMembers = 2;
        public const int LegacyMinLevel = 10;
        public const int YearTicks = 3600000;

        public static string LegacyGroup(string skill)
        {
            switch (skill)
            {
                case "Medicine": return "medicine";
                case "Melee": case "Shooting": return "warfare";
                case "Cooking": return "hospitality";
                case "Plants": case "Animals": return "the land";
                case "Crafting": case "Construction": case "Mining": case "Artistic": return "craftsmanship";
                case "Intellectual": return "learning";
                case "Social": return "diplomacy";
                default: return null;
            }
        }
    }

    public sealed partial class FamilyRegistry
    {
        public List<GatheringRequest> GatheringRequests = new List<GatheringRequest>();
        public List<Memory> Memories = new List<Memory>();
        public int NextRequestId = 1;
        public int LastAnniversaryTick;

        // ------------------------------------------------------------------ culture

        /// <summary>
        /// Moves each family's culture a little toward what its living members' traits point to. Changes only by drift and by
        /// <see cref="Reinforce"/>, so it moves over days and weeks, never jumps.
        /// </summary>
        public void UpdateCulture(Func<int, float[]> profileOf, int now)
        {
            EnsureIndexes();
            var membersByHouse=ActiveColonyMembers();
            foreach (var lineage in Lineages)
            {
                if(lineage.AbsorbedIntoId!=0) continue;
                if(lineage.LastCultureTick>0 && now-lineage.LastCultureTick<ActivityTuning.TicksPerDay) continue;
                var sum = new float[DimInfo.Count];
                float weight = 0f;
                if(!membersByHouse.TryGetValue(lineage.Id,out var members)) members=new List<PersonRecord>();
                foreach (var p in members)
                {
                    var profile = profileOf?.Invoke(p.Id);
                    if (profile == null) continue;
                    float w = p.Juvenile ? CultureTuning.JuvenileWeight : CultureTuning.AdultWeight;
                    for (int i = 0; i < sum.Length && i < profile.Length; i++) sum[i] += profile[i] * w;
                    weight += w;
                }
                if (weight <= 0f) { lineage.LastCultureTick = now; continue; }

                while (lineage.Culture.Count < DimInfo.Count) lineage.Culture.Add(0f);
                bool first = lineage.LastCultureTick <= 0;
                float days = first ? 0f : Math.Min(CultureTuning.MaxDays, Math.Max(0, now - lineage.LastCultureTick) / (float)ActivityTuning.TicksPerDay);
                float rate = 1f - (float)Math.Pow(1f - CultureTuning.DailyDrift, days);
                for (int i = 0; i < DimInfo.Count; i++)
                {
                    float target = Math.Max(-100f, Math.Min(100f, sum[i] / weight * CultureTuning.ProfileScale));
                    lineage.Culture[i] = first ? target : lineage.Culture[i] + (target - lineage.Culture[i]) * rate;
                }
                lineage.LastCultureTick = now;
            }
        }

        /// <summary>What the family does shapes what it is: a story told pushes toward learning, a shared meal toward the hearth, and so on.</summary>
        public void Reinforce(int lineageId, Dim dim, float amount)
        {
            var lineage = GetLineage(lineageId);
            if (lineage == null) return;
            while (lineage.Culture.Count < DimInfo.Count) lineage.Culture.Add(0f);
            int i = (int)dim;
            lineage.Culture[i] = Math.Max(-100f, Math.Min(100f, lineage.Culture[i] + amount));
        }

        /// <summary>The interests this family leans toward, strongest first.</summary>
        public List<Dim> Values(int lineageId, float threshold = 20f)
        {
            var lineage = GetLineage(lineageId);
            if (lineage == null) return new List<Dim>();
            return Enum.GetValues(typeof(Dim)).Cast<Dim>()
                .Where(d => !DimInfo.IsAxis(d) && lineage.CultureOf(d) >= threshold)
                .OrderByDescending(d => lineage.CultureOf(d)).ToList();
        }

        /// <summary>
        /// What the family is known for: a field where at least two of its people, living or dead, reached a high level.
        /// Behavior and standing only, never a stat bonus.
        /// </summary>
        public List<string> Legacies(int lineageId)
        {
            EnsureIndexes();
            var lineage = GetLineage(lineageId);
            var groups = new Dictionary<string, int>();
            foreach (var p in Persons)
            {
                if (!(BelongsToFamily(p,lineageId))) continue;
                if (p.TopSkillLevel < CultureTuning.LegacyMinLevel) continue;
                var group = CultureTuning.LegacyGroup(p.TopSkill);
                if (group == null) continue;
                groups[group] = groups.TryGetValue(group, out var n) ? n + 1 : 1;
            }
            return groups.Where(kv => kv.Value >= CultureTuning.LegacyMinMembers).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Select(kv => kv.Key).ToList();
        }

        // ------------------------------------------------------------------ traditions

        public int ColonyPeopleOf(int lineageId)
        {
            var lineage = GetLineage(lineageId);
            return Persons.Count(p => p.InColony && (BelongsToFamily(p,lineageId)));
        }

        /// <summary>
        /// Takes up and lets go of traditions as the culture changes, and returns the gatherings that have come due. Fractured
        /// families and tiny ones do not hold gatherings.
        /// </summary>
        public List<FamilyEvent> UpdateTraditions(int now)
        {
            EnsureIndexes();
            var membersByHouse=ActiveColonyMembers();
            var events = new List<FamilyEvent>();
            GatheringRequests.RemoveAll(r => r == null || r.State==CeremonyState.Planned && r.Expires < now);
            foreach (var lineage in Lineages)
            {
                if(lineage.AbsorbedIntoId!=0) continue;
                int people = membersByHouse.TryGetValue(lineage.Id,out var members)?members.Count:0;
                if (people == 0) continue;
                for (int i = 0; i < TraditionCatalog.All.Length; i++)
                {
                    var def = TraditionCatalog.All[i];
                    var held = lineage.Traditions.FirstOrDefault(t => t.Id == def.Id);
                    float value = lineage.CultureOf(def.Dim);
                    if (held == null && value >= TraditionCatalog.AdoptAt && people >= TraditionCatalog.MinPeopleInColony)
                    {
                        lineage.Traditions.Add(new TraditionRecord { Id = def.Id, AdoptedTick = now, LastHeldTick = now });
                        events.Add(new FamilyEvent(FamilyEventKind.TraditionAdopted, now, lineage.Id, i));
                    }
                    else if (held != null && value < TraditionCatalog.AbandonBelow)
                    {
                        lineage.Traditions.Remove(held);
                        GatheringRequests.RemoveAll(r => r.LineageId == lineage.Id && r.Kind == def.Id);
                        events.Add(new FamilyEvent(FamilyEventKind.TraditionAbandoned, now, lineage.Id, i));
                    }
                }

                float? cohesion = LineageCohesion(lineage.Id);
                if (people < TraditionCatalog.MinPeopleInColony || (cohesion.HasValue && cohesion.Value < TraditionCatalog.MinCohesion)) continue;
                foreach (var t in lineage.Traditions)
                {
                    var def = TraditionCatalog.All[Math.Max(0, TraditionCatalog.IndexOf(t.Id))];
                    if (t.Muted || now - Math.Max(t.AdoptedTick, t.LastHeldTick) < def.IntervalDays * ActivityTuning.TicksPerDay) continue;
                    if (GatheringRequests.Any(r => r.LineageId == lineage.Id && r.Kind == t.Id)) continue;
                    GatheringRequests.Add(new GatheringRequest { Id = NextRequestId++, Kind = t.Id, Ceremony=CeremonyKind.Tradition, State=CeremonyState.Planned,
                        LineageId = lineage.Id, CreatedTick=now, NotBefore = now, Expires = now + 6 * ActivityTuning.TicksPerDay,
                        Seed=unchecked(lineage.Id*397^now/ActivityTuning.TicksPerDay) });
                }
            }
            return events;
        }

        private Dictionary<int,List<PersonRecord>> ActiveColonyMembers()
        {
            var result=Lineages.Where(l=>l.AbsorbedIntoId==0).ToDictionary(l=>l.Id,l=>new List<PersonRecord>());
            foreach(var person in Persons.Where(p=>p.InColony))
            {
                int main=LineageIdFor(person);
                if(main>0 && result.TryGetValue(main,out var primary)) primary.Add(person);
            }
            foreach(var lineage in Lineages.Where(l=>l.AbsorbedIntoId==0))
                foreach(var affiliation in lineage.Affiliations)
                {
                    var person=GetPerson(affiliation.PersonId);
                    if(person?.InColony==true && person.SocialFamilyId==0 && LineageIdFor(person)!=lineage.Id && result.TryGetValue(lineage.Id,out var affiliated)) affiliated.Add(person);
                }
            return result;
        }

        /// <summary>Asks for a one-off gathering (a coming of age, a memorial). A second request for the same thing is ignored.</summary>
        public void RequestGathering(string kind, int lineageId, int subjectId, int now, int delayTicks, int lifetimeTicks)
        {
            var ceremony=CeremonyOf(kind);
            if(ceremony!=CeremonyKind.Tradition)
            {RequestCeremony(ceremony,lineageId,subjectId,0,now,delayTicks,lifetimeTicks,ceremony==CeremonyKind.Funeral?CeremonyInfluence.RecentDeath:CeremonyInfluence.None);return;}
            EnsureIndexes();if(!IsActiveHouse(lineageId))return;
            if(GatheringRequests.Any(r=>r.Kind==kind&&r.LineageId==lineageId&&r.SubjectId==subjectId&&
                (r.State==CeremonyState.Planned||r.State==CeremonyState.Active)))return;
            GatheringRequests.Add(new GatheringRequest{Id=NextRequestId++,Kind=kind,Ceremony=CeremonyKind.Tradition,State=CeremonyState.Planned,LineageId=lineageId,
                SubjectId=subjectId,CreatedTick=now,NotBefore=now+delayTicks,Expires=now+delayTicks+lifetimeTicks,Seed=unchecked(lineageId*397^subjectId^now/ActivityTuning.TicksPerDay)});
        }

        public List<GatheringRequest> DueRequests(int now)
        {
            return GatheringRequests.Where(r => r.State==CeremonyState.Planned && r.NotBefore <= now && r.Expires >= now).OrderBy(r => r.NotBefore).ThenBy(r => r.Id).ToList();
        }

        /// <summary>The gathering took place: the tradition's clock restarts, the culture it expresses grows, and it is remembered.</summary>
        public FamilyEvent MarkGatheringHeld(int requestId, int now)
        {
            var request = GatheringRequests.FirstOrDefault(r => r.Id == requestId);
            if (request == null) return null;
            var lineage = GetLineage(request.LineageId);
            if (lineage == null) return null;
            int index = TraditionCatalog.IndexOf(request.Kind);
            CompleteCeremony(requestId,now,request.InvitedIds.Count,request.MomentsResolved,0.5f);
            if (index < 0) return null;
            var record = lineage.Traditions.FirstOrDefault(t => t.Id == request.Kind);
            if (record != null) { record.LastHeldTick = now; record.TimesHeld++; }
            Reinforce(lineage.Id, TraditionCatalog.All[index].Dim, 2f);
            var e = new FamilyEvent(FamilyEventKind.TraditionHeld, now, lineage.Id, index);
            Chronicle.Add(e);
            if (Chronicle.Count > MaxChronicle) Chronicle.RemoveRange(0, Chronicle.Count - MaxChronicle);
            if (record != null && record.TimesHeld == 1) Remember(lineage.Id, MemoryKind.Tradition, now, 0, index, 1.0f);
            return e;
        }

        /// <summary>Retries later when the gathering could not start (no organizer free, no room).</summary>
        public void PostponeRequest(int requestId, int now, int delayTicks)
        {
            var r = GatheringRequests.FirstOrDefault(x => x.Id == requestId);
            if (r == null || r.State!=CeremonyState.Planned) return;
            r.Attempts++;
            r.NotBefore = now + delayTicks;
        }

        public void SetTraditionMuted(int lineageId, string traditionId, bool muted)
        {
            var record = GetLineage(lineageId)?.Traditions.FirstOrDefault(t => t.Id == traditionId);
            if (record != null) record.Muted = muted;
        }

        /// <summary>
        /// Who is invited: the family's own people in the colony, and anyone in the colony who thinks well of every host who is
        /// still here (typically the two parents).
        /// </summary>
        public List<int> Invitees(int lineageId, IList<int> hostIds, Func<int, int, int?> opinionOf, int minOpinion = 20)
        {
            EnsureIndexes();
            var lineage = GetLineage(lineageId);
            var hosts = hostIds.Where(h => personById.TryGetValue(h, out var hp) && hp.InColony).Distinct().ToList();
            var result = new List<int>();
            foreach (var p in Persons.Where(x => x.InColony).OrderBy(x => x.Id))
            {
                bool family = BelongsToFamily(p,lineageId);
                bool friend = hosts.Count > 0 && hosts.All(h => h == p.Id || (opinionOf?.Invoke(p.Id, h) ?? int.MinValue) >= minOpinion);
                var role=RoleInHouse(p.Id,lineageId);
                if (family || friend || role>=HouseRole.FamilyFriend && role<HouseRole.Member) result.Add(p.Id);
            }
            return result;
        }

        // ------------------------------------------------------------------ memories

        /// <summary>Keeps a defining moment if it is weighty enough; the least weighty and oldest are dropped when the family has too many.</summary>
        public bool Remember(int lineageId, MemoryKind kind, int now, int a, int b, float weight)
        {
            EnsureIndexes();
            if (weight < CultureTuning.MemoryThreshold || GetLineage(lineageId) == null) return false;
            Memories.Add(new Memory { LineageId = lineageId, Kind = kind, Tick = now, A = a, B = b, Weight = weight });
            var mine = Memories.Where(m => m.LineageId == lineageId).ToList();
            if (mine.Count > CultureTuning.MaxMemoriesPerLineage)
            {
                var drop = mine.OrderBy(m => m.Weight).ThenBy(m => m.Tick).First();
                Memories.Remove(drop);
            }
            ApplyMemoryToCulture(lineageId, kind, weight);
            return true;
        }

        private void ApplyMemoryToCulture(int lineageId, MemoryKind kind, float weight)
        {
            switch (kind)
            {
                case MemoryKind.Birth: Reinforce(lineageId, Dim.Hearth, weight); break;
                case MemoryKind.Loss: Reinforce(lineageId, Dim.Faith, weight); Reinforce(lineageId, Dim.Devotion, weight * 0.5f); break;
                case MemoryKind.Wedding: Reinforce(lineageId, Dim.Devotion, weight); break;
                case MemoryKind.Parting: Reinforce(lineageId, Dim.Devotion, -weight); break;
                case MemoryKind.Reunion: Reinforce(lineageId, Dim.Devotion, weight); break;
                case MemoryKind.Reconciliation: Reinforce(lineageId, Dim.Warmth, weight); break;
                case MemoryKind.CameOfAge: Reinforce(lineageId, Dim.Status, weight * 0.5f); break;
                case MemoryKind.Joined: Reinforce(lineageId, Dim.Devotion, weight * 0.5f); break;
                case MemoryKind.StepFamily: Reinforce(lineageId, Dim.Hearth, weight * 0.5f); break;
                case MemoryKind.Bond: Reinforce(lineageId, Dim.Warmth, weight * 0.5f); break;
                case MemoryKind.Rupture: Reinforce(lineageId, Dim.Warmth, -weight * 0.5f); break;
            }
        }

        public List<Memory> MemoriesOf(int lineageId)
        {
            return Memories.Where(m => m != null && m.LineageId == lineageId).OrderByDescending(m => m.Tick).ToList();
        }

        /// <summary>Turns the events of a sweep into the family's memories, with how much each mattered.</summary>
        public void RecordMemories(IEnumerable<FamilyEvent> events, IEnumerable<Grief> griefs, int now)
        {
            EnsureIndexes();
            var griefList = (griefs ?? Enumerable.Empty<Grief>()).ToList();
            foreach (var e in events ?? Enumerable.Empty<FamilyEvent>())
            {
                personById.TryGetValue(e.A, out var a);
                int lineage = a != null ? LineageIdFor(a) : 0;
                switch (e.Kind)
                {
                    case FamilyEventKind.Birth: Remember(lineage, MemoryKind.Birth, now, e.A, e.B, 1.0f); break;
                    case FamilyEventKind.CameOfAge: Remember(lineage, MemoryKind.CameOfAge, now, e.A, 0, 1.0f); break;
                    case FamilyEventKind.Reunion: Remember(lineage, MemoryKind.Reunion, now, e.A, 0, 1.0f); break;
                    case FamilyEventKind.PartnershipFormed:
                        if (a != null && a.SpouseIds.Contains(e.B)) Remember(lineage, MemoryKind.Wedding, now, e.A, e.B, 1.5f);
                        break;
                    case FamilyEventKind.MarriageFormed:
                        Remember(lineage,MemoryKind.Wedding,now,e.A,e.B,1.5f);
                        break;
                    case FamilyEventKind.PartnershipEnded: Remember(lineage, MemoryKind.Parting, now, e.A, e.B, 2.0f); break;
                    case FamilyEventKind.Died:
                        float grief = griefList.Where(g => g.LostId == e.A).Select(g => g.Attachment).DefaultIfEmpty(0f).Max();
                        Remember(lineage, MemoryKind.Loss, now, e.A, 0, grief / 50f);
                        break;
                    case FamilyEventKind.Anniversary:
                        if (a != null) Remember(lineage, MemoryKind.Anniversary, now, e.A, e.B, 1.0f);
                        break;
                    case FamilyEventKind.FamiliesJoined:
                    case FamilyEventKind.StepFamilyFormed:
                        {
                            var kind = e.Kind == FamilyEventKind.FamiliesJoined ? MemoryKind.Joined : MemoryKind.StepFamily;
                            personById.TryGetValue(e.B, out var other);
                            int lineageB = other != null ? LineageIdFor(other) : 0;
                            int lineageA = a != null ? LineageIdFor(a) : 0;
                            if (lineageA != 0) Remember(lineageA, kind, now, e.A, e.B, 1.5f);
                            if (lineageB != 0 && lineageB != lineageA) Remember(lineageB, kind, now, e.A, e.B, 1.5f);
                            break;
                        }
                }
            }
        }

        // ------------------------------------------------------------------ milestones

        /// <summary>Wedding anniversaries that have come round since the last check.</summary>
        public List<FamilyEvent> UpdateMilestones(int now)
        {
            EnsureIndexes();
            var events = new List<FamilyEvent>();
            int last = LastAnniversaryTick;
            LastAnniversaryTick = now;
            if (last <= 0 || now <= last) return events;
            foreach (var p in Persons.Where(x => x.InColony).OrderBy(x => x.Id))
            {
                for (int i = 0; i < p.SpouseIds.Count && i < p.SpouseSince.Count; i++)
                {
                    int since = p.SpouseSince[i];
                    int otherId = p.SpouseIds[i];
                    if (since <= 0 || p.Id >= otherId || !personById.TryGetValue(otherId, out var other) || !other.InColony) continue;
                    long before = (long)(last - since) / CultureTuning.YearTicks;
                    long after = (long)(now - since) / CultureTuning.YearTicks;
                    if (after >= 1 && after > before) events.Add(new FamilyEvent(FamilyEventKind.Anniversary, now, p.Id, otherId));
                }
            }
            return events;
        }

        // ------------------------------------------------------------------ distance

        /// <summary>People in the colony who are strongly attached to someone who has been away a long time.</summary>
        public List<KeyValuePair<int, int>> MissingPairs(int now)
        {
            EnsureIndexes();
            var result = new List<KeyValuePair<int, int>>();
            foreach (var bond in Bonds)
            {
                if (bond.Attachment < 50f) continue;
                if (!personById.TryGetValue(bond.From, out var holder) || !holder.InColony) continue;
                if (!personById.TryGetValue(bond.To, out var away) || away.Presence != Presence.Away || away.AwayTick < 0) continue;
                if (now - away.AwayTick >= ActivityTuning.ReunionAwayTicks) result.Add(new KeyValuePair<int, int>(holder.Id, away.Id));
            }
            return result;
        }
    }
}
