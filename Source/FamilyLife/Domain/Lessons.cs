using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FamilyLife.Domain
{
    // A class teaches one subject. What a pupil takes away follows from how long they really sat in the lesson, how well the teacher put it across, and whether there was
    // anything the teacher could explain that the pupil did not already know. The annual assessment looks at that record; it does not give out experience of its own beyond
    // a small consolidation of what was studied.

    public sealed class PupilSnapshot
    {
        public int Id, Age;
        public Dictionary<string, int> Levels = new Dictionary<string, int>();
        public HashSet<string> Passions = new HashSet<string>(), Disabled = new HashSet<string>();
        /// <summary>Skills the pupil's house is trying to build (a capability gap in the house's ambition).</summary>
        public HashSet<string> HouseNeeds = new HashSet<string>();
        public int Level(string skill) => Levels.TryGetValue(skill, out int l) ? l : 0;
    }

    public static class LessonSubjects
    {
        /// <summary>Teaching combat to the very young is not part of school; the rest is open from seven.</summary>
        public static bool AgeAppropriate(string skill, int age)
        {
            bool combat = skill == "Shooting" || skill == "Melee";
            if (combat) return age >= 13;
            if (skill == "Medicine") return age >= 10;
            if (age < 7) return skill == "Social" || skill == "Artistic" || skill == "Animals" || skill == "Plants";
            return true;
        }

        /// <summary>How much of the teacher's skill can be passed on: nothing unless they are clearly ahead, rising to full when they are well ahead.</summary>
        public static float GapFactor(int teacherLevel, int pupilLevel)
        {
            int gap = teacherLevel - pupilLevel;
            if (gap < 2) return 0f;
            return Math.Min(1f, .35f + gap / 9f);
        }

        /// <summary>
        /// The subject with the most to give this class. The teacher must be able to explain it (level 4 or more), and most of the class must have something to learn.
        /// A pupil's interest, the house's own gap and the book on the shelf each make a subject a little more likely; a subject taught in the last two classes a little less.
        /// </summary>
        public static string Choose(IDictionary<string, int> teacher, ISet<string> teacherDisabled, IList<PupilSnapshot> pupils, string bookSkill, IList<string> recent, int seed)
        {
            if (teacher == null || pupils == null || pupils.Count == 0) return null;
            string best = null; float bestScore = 0f;
            foreach (var kv in teacher.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                string skill = kv.Key; int level = kv.Value;
                if (level < 4 || (teacherDisabled != null && teacherDisabled.Contains(skill))) continue;
                float sum = 0f; int learners = 0, fit = 0;
                foreach (var p in pupils)
                {
                    if (p.Disabled.Contains(skill) || !AgeAppropriate(skill, p.Age)) continue;
                    fit++;
                    float g = GapFactor(level, p.Level(skill));
                    if (g <= 0f) continue;
                    learners++;
                    sum += g * (p.Passions.Contains(skill) ? 1.35f : 1f) * (p.HouseNeeds.Contains(skill) ? 1.25f : 1f);
                }
                if (fit * 2 < pupils.Count || learners * 2 < pupils.Count) continue;          // most of the class must be able to take part and have something to learn
                float score = sum / pupils.Count;
                if (!string.IsNullOrEmpty(bookSkill) && bookSkill == skill) score *= 1.15f;
                if (recent != null) { int idx = recent.ToList().LastIndexOf(skill); if (idx >= 0 && recent.Count - idx <= 2) score *= .6f; }
                score += (Math.Abs(seed + skill.GetHashCode()) % 100) / 10000f;          // a tie goes the same way for the same class
                if (score > bestScore) { bestScore = score; best = skill; }
            }
            return best;
        }
    }

    public static class LessonMath
    {
        /// <summary>The experience a pupil gets in the subject from one full class (four hours of instruction) with an ordinary teacher.</summary>
        public const float FullClassXp = 480f;
        /// <summary>Of the book's experience, the share a pupil takes from hearing it read and explained rather than reading it themselves.</summary>
        public const float BookShare = .5f;
        public const int BookCooldownTicks = 12 * ActivityTuning.TicksPerDay;

        /// <summary>How well the teacher puts the subject across, about .5 to 1.4: the subject first, then Social, then how ready they were and how tired.</summary>
        public static float Delivery(int subjectSkill, int social, float preparation, float facility, float fatigue)
        {
            float v = .45f + Math.Max(0, subjectSkill) * .035f + Math.Max(0, social) * .015f + Math.Max(-.15f, Math.Min(.2f, preparation + facility)) - Math.Max(0f, Math.Min(.25f, fatigue));
            return Math.Max(.5f, Math.Min(1.4f, v));
        }

        /// <summary>Younger children soak more up from a lesson than adults.</summary>
        public static float AgeFactor(int age) => age < 13 ? 1f : age < 18 ? .85f : .65f;

        /// <summary>How the pupil took to the teacher over the class: from the sum of the exchanges' tiers, a little either way.</summary>
        public static float EngagementFactor(float tierSum) => Math.Max(.7f, Math.Min(1.25f, 1f + .06f * tierSum));

        /// <summary>The experience for the lesson: the share of the class they sat through, how it was delivered, how well they took to it, and what there was to learn.</summary>
        public static float Xp(float attendedShare, float delivery, float engagement, float gapFactor, int age) =>
            FullClassXp * Math.Max(0f, Math.Min(1f, attendedShare)) * delivery * engagement * gapFactor * AgeFactor(age);

        /// <summary>Learning a pupil would get from a book is limited by what the teacher can explain of it: nothing without a skill gap, full with a wide one.</summary>
        public static float BookXp(float bookExperience, float attendedShare, float gapFactor) =>
            Math.Max(0f, bookExperience) * BookShare * Math.Max(0f, Math.Min(1f, attendedShare)) * gapFactor;

        /// <summary>The same book gives the same child its experience once in twelve days, however many classes use it. <paramref name="uses"/> are "pupil|book|tick".</summary>
        public static bool BookReady(IEnumerable<string> uses, int pupil, int book, int now)
        {
            if (uses == null) return true;
            string prefix = pupil + "|" + book + "|";
            foreach (var u in uses)
            {
                if (u == null || !u.StartsWith(prefix, StringComparison.Ordinal)) continue;
                if (int.TryParse(u.Substring(prefix.Length), out int t) && now >= t && now - t < BookCooldownTicks) return false;
            }
            return true;
        }

        /// <summary>A pupil who sat through less than a fifth of the instruction did not really attend, and gets nothing.</summary>
        public const float MinShare = .2f;
        public static float AttendedShare(int attendedTicks, int instructionTicks) => instructionTicks <= 0 ? 0f : Math.Max(0f, Math.Min(1f, attendedTicks / (float)instructionTicks));
    }

    /// <summary>The shape of a class: shorter blocks of instruction with breaks between, up to four hours in all.</summary>
    public static class ClassSchedule
    {
        public const int BlockTicks = 2000, BreakTicks = 500, Blocks = 4;
        public const int InstructionTicks = Blocks * BlockTicks;
        public const int TotalTicks = Blocks * BlockTicks + (Blocks - 1) * BreakTicks;          // 9,500: about 3.8 game hours

        public static bool InBreak(int elapsed)
        {
            if (elapsed < 0) return false;
            int cycle = BlockTicks + BreakTicks;
            int block = elapsed / cycle;
            return block < Blocks - 1 && elapsed % cycle >= BlockTicks;
        }
        public static int BlockOf(int elapsed) => Math.Max(0, Math.Min(Blocks - 1, elapsed / (BlockTicks + BreakTicks)));
        public static bool Over(int elapsed) => elapsed >= TotalTicks;
        /// <summary>Instruction time that has passed (breaks do not count).</summary>
        public static int InstructionDone(int elapsed)
        {
            int cycle = BlockTicks + BreakTicks, full = Math.Min(elapsed / cycle, Blocks);
            int done = full * BlockTicks;
            if (full < Blocks) done += Math.Min(BlockTicks, elapsed % cycle);
            return Math.Min(InstructionTicks, done);
        }
    }

    /// <summary>One pupil's recorded learning in one subject over the assessment period. Kept as short text in the save and bounded in number.</summary>
    public sealed class LessonRecord
    {
        public int Pupil;
        public string Skill = "";
        public float Xp, Effect, EngageSum;
        public int Ticks, Possible, Exchanges, Classes;

        public string Encode() => string.Join("|", Pupil.ToString(CultureInfo.InvariantCulture), Skill, F(Xp), F(Effect), F(EngageSum), Ticks.ToString(CultureInfo.InvariantCulture), Possible.ToString(CultureInfo.InvariantCulture), Exchanges.ToString(CultureInfo.InvariantCulture), Classes.ToString(CultureInfo.InvariantCulture));
        private static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public static LessonRecord Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var p = text.Split('|');
            if (p.Length < 9) return null;
            try
            {
                var inv = CultureInfo.InvariantCulture;
                return new LessonRecord { Pupil = int.Parse(p[0], inv), Skill = p[1], Xp = float.Parse(p[2], inv), Effect = float.Parse(p[3], inv), EngageSum = float.Parse(p[4], inv), Ticks = int.Parse(p[5], inv), Possible = int.Parse(p[6], inv), Exchanges = int.Parse(p[7], inv), Classes = int.Parse(p[8], inv) };
            }
            catch (FormatException) { return null; }
        }

        public const int MaxRecords = 120;

        /// <summary>Adds one class's worth to the log (merging with the pupil's record in that subject) and keeps the log bounded by dropping the oldest.</summary>
        public static void Add(List<string> log, LessonRecord add)
        {
            if (log == null || add == null || add.Pupil == 0 || string.IsNullOrEmpty(add.Skill)) return;
            for (int i = 0; i < log.Count; i++)
            {
                var r = Parse(log[i]);
                if (r == null || r.Pupil != add.Pupil || r.Skill != add.Skill) continue;
                r.Xp += add.Xp; r.Effect += add.Effect; r.EngageSum += add.EngageSum; r.Ticks += add.Ticks; r.Possible += add.Possible; r.Exchanges += add.Exchanges; r.Classes += add.Classes;
                log[i] = r.Encode();
                return;
            }
            log.Add(add.Encode());
            while (log.Count > MaxRecords) log.RemoveAt(0);
        }

        public static List<LessonRecord> For(List<string> log, int pupil) => (log ?? new List<string>()).Select(Parse).Where(r => r != null && r.Pupil == pupil).ToList();

        /// <summary>Records for a pupil who was in a class but not present for it also count what they missed: Possible grows without Ticks.</summary>
        public static LessonRecord Absent(int pupil, string skill, int possible) => new LessonRecord { Pupil = pupil, Skill = skill, Possible = possible, Classes = 1 };
    }

    public enum Completion : byte { Complete, Partial, Incomplete }

    public struct AssessmentResult
    {
        public Completion Completion;
        public ResultQuality Level;
        /// <summary>How well they learned what they were taught, per hour they sat, regardless of where they started.</summary>
        public float Progress;
        /// <summary>How sure the result is: the share of the time they could have attended that they did, and the sittings completed.</summary>
        public float Confidence;
        /// <summary>The skill itself, for the record; it does not move the result.</summary>
        public int Attainment;
        public float Score;
        public bool Provisional;
    }

    public static class Assessments
    {
        public const float FairFrom = .45f, GoodFrom = .62f, ExcellentFrom = .8f;
        /// <summary>What the consolidation bonus is, as a share of the experience recorded from lessons: well under a fifth.</summary>
        public static float BonusShare(ResultQuality q) => q == ResultQuality.Poor ? .05f : q == ResultQuality.Fair ? .10f : q == ResultQuality.Good ? .15f : .20f;

        public static ResultQuality LevelOf(float score) => score < FairFrom ? ResultQuality.Poor : score < GoodFrom ? ResultQuality.Fair : score < ExcellentFrom ? ResultQuality.Good : ResultQuality.Excellent;

        /// <summary>
        /// Four things are kept apart. Attainment is the skill and is reported only. Progress is what they learned per attended hour. Confidence is how much evidence
        /// there is. Completion is whether enough was done to judge at all. A pupil who attended little is Incomplete, not Poor, and is not penalised twice for it.
        /// </summary>
        public static AssessmentResult Assess(LessonRecord rec, int sittingsDone, int sittingsPlanned, int excusedSittings, int attainment, float teacherDelivery, float sittingNoise)
        {
            var res = new AssessmentResult { Attainment = attainment };
            if (rec == null || rec.Ticks <= 0 || rec.Possible <= 0) { res.Completion = Completion.Incomplete; return res; }
            float attendance = Math.Min(1f, rec.Ticks / (float)rec.Possible);
            int possibleSittings = Math.Max(1, sittingsPlanned - Math.Max(0, excusedSittings));
            float sittingShare = Math.Min(1f, sittingsDone / (float)possibleSittings);
            res.Confidence = Math.Min(1f, attendance / .6f) * (.5f + .5f * sittingShare);
            res.Completion = sittingsDone >= 2 && attendance >= .45f ? Completion.Complete : sittingsDone >= 1 && attendance >= .2f ? Completion.Partial : Completion.Incomplete;
            if (res.Completion == Completion.Incomplete) return res;
            float progress = rec.Effect / rec.Ticks;                         // average delivery x how they took to it, per attended tick
            float engagement = rec.Exchanges > 0 ? Math.Max(0f, Math.Min(1f, (rec.EngageSum / rec.Exchanges + 1f) / 3f)) : .5f;
            float pn = Math.Max(0f, Math.Min(1f, progress / 1.2f));
            float support = Math.Max(0f, Math.Min(1f, teacherDelivery));
            res.Progress = progress;
            res.Score = Math.Max(0f, Math.Min(1f, pn * .6f + engagement * .25f + support * .15f + Math.Max(-.06f, Math.Min(.06f, sittingNoise))));
            res.Level = LevelOf(res.Score);
            if (res.Confidence < .5f && res.Level > ResultQuality.Good) { res.Level = ResultQuality.Good; res.Provisional = true; }
            if (res.Completion == Completion.Partial) res.Provisional = true;
            return res;
        }

        /// <summary>Consolidation experience: a share of what was recorded for this subject, less when the result is only partial.</summary>
        public static float Bonus(LessonRecord rec, AssessmentResult res)
        {
            if (rec == null || res.Completion == Completion.Incomplete) return 0f;
            float xp = rec.Xp * BonusShare(res.Level);
            return res.Completion == Completion.Partial ? xp * .5f : xp;
        }
    }
}
