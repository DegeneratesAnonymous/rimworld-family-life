using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;
using Xunit.Abstractions;

namespace FamilyLife.Tests
{
    public class LessonTests
    {
        private readonly ITestOutputHelper output;
        public LessonTests(ITestOutputHelper output) { this.output = output; }

        private static PupilSnapshot Kid(int id, int age, params (string skill, int level)[] levels)
        {
            var p = new PupilSnapshot { Id = id, Age = age };
            foreach (var l in levels) p.Levels[l.skill] = l.level;
            return p;
        }

        // ---------------------------------------------------------------- the shape of a class

        [Fact]
        public void A_class_is_four_short_blocks_with_breaks_and_stays_within_four_hours()
        {
            Assert.Equal(4, ClassSchedule.Blocks);
            Assert.True(ClassSchedule.TotalTicks <= 4 * 2500);
            Assert.Equal(ClassSchedule.Blocks * ClassSchedule.BlockTicks, ClassSchedule.InstructionTicks);
            Assert.False(ClassSchedule.InBreak(0));
            Assert.False(ClassSchedule.InBreak(ClassSchedule.BlockTicks - 1));
            Assert.True(ClassSchedule.InBreak(ClassSchedule.BlockTicks));
            Assert.False(ClassSchedule.InBreak(ClassSchedule.BlockTicks + ClassSchedule.BreakTicks));
            Assert.False(ClassSchedule.InBreak(ClassSchedule.TotalTicks - 1));          // no break after the last block
            Assert.True(ClassSchedule.Over(ClassSchedule.TotalTicks));
            // breaks do not count as instruction
            Assert.Equal(ClassSchedule.BlockTicks, ClassSchedule.InstructionDone(ClassSchedule.BlockTicks + ClassSchedule.BreakTicks - 1));
            Assert.Equal(ClassSchedule.InstructionTicks, ClassSchedule.InstructionDone(ClassSchedule.TotalTicks + 5000));
            Assert.Equal(0, ClassSchedule.BlockOf(10)); Assert.Equal(3, ClassSchedule.BlockOf(ClassSchedule.TotalTicks - 1));
        }

        // ---------------------------------------------------------------- subjects

        [Fact]
        public void A_lesson_has_one_subject_the_teacher_can_actually_explain()
        {
            var teacher = new Dictionary<string, int> { ["Cooking"] = 12, ["Plants"] = 3, ["Social"] = 9 };
            var kids = new List<PupilSnapshot> { Kid(1, 9, ("Cooking", 2), ("Plants", 0), ("Social", 8)), Kid(2, 11, ("Cooking", 3), ("Plants", 1), ("Social", 7)) };
            Assert.Equal("Cooking", LessonSubjects.Choose(teacher, null, kids, null, null, 1));          // Plants: below 4, not offered; Social: no gap
            // an advanced book on a subject the teacher cannot explain does not make it a subject
            Assert.Equal("Cooking", LessonSubjects.Choose(teacher, null, kids, "Plants", null, 1));
            Assert.Null(LessonSubjects.Choose(new Dictionary<string, int> { ["Cooking"] = 3 }, null, kids, null, null, 1));
        }

        [Fact]
        public void Subjects_follow_the_class_s_age_ability_interests_the_house_and_variety()
        {
            var teacher = new Dictionary<string, int> { ["Melee"] = 14, ["Cooking"] = 14, ["Medicine"] = 14 };
            var young = new List<PupilSnapshot> { Kid(1, 8, ("Melee", 0), ("Cooking", 0), ("Medicine", 0)), Kid(2, 9, ("Melee", 0), ("Cooking", 0), ("Medicine", 0)) };
            Assert.Equal("Cooking", LessonSubjects.Choose(teacher, null, young, null, null, 1));          // not combat, not medicine, for the very young
            Assert.False(LessonSubjects.AgeAppropriate("Shooting", 8)); Assert.True(LessonSubjects.AgeAppropriate("Shooting", 14));
            Assert.False(LessonSubjects.AgeAppropriate("Cooking", 5)); Assert.True(LessonSubjects.AgeAppropriate("Social", 5));
            // a subject the teacher is disabled in is never taught
            Assert.NotEqual("Cooking", LessonSubjects.Choose(teacher, new HashSet<string> { "Cooking" }, young, null, null, 1));
            // interest and the house's own gap each tip the choice, and so does variety
            var a = Kid(1, 14, ("Melee", 0), ("Cooking", 0)); var b = Kid(2, 14, ("Melee", 0), ("Cooking", 0));
            a.Passions.Add("Melee"); b.Passions.Add("Melee");
            Assert.Equal("Melee", LessonSubjects.Choose(teacher, null, new List<PupilSnapshot> { a, b }, null, null, 1));
            var c = Kid(1, 12, ("Medicine", 0), ("Cooking", 0)); var d = Kid(2, 12, ("Medicine", 0), ("Cooking", 0));
            c.HouseNeeds.Add("Medicine"); d.HouseNeeds.Add("Medicine");
            Assert.Equal("Medicine", LessonSubjects.Choose(teacher, null, new List<PupilSnapshot> { c, d }, null, null, 1));
            Assert.NotEqual("Medicine", LessonSubjects.Choose(teacher, null, new List<PupilSnapshot> { c, d }, null, new List<string> { "Medicine", "Medicine" }, 1));
            // most of the class must have something to learn
            var experts = new List<PupilSnapshot> { Kid(1, 12, ("Cooking", 14)), Kid(2, 12, ("Cooking", 14)), Kid(3, 12, ("Cooking", 0)) };
            Assert.NotEqual("Cooking", LessonSubjects.Choose(new Dictionary<string, int> { ["Cooking"] = 14 }, null, experts, null, null, 1) ?? "none");
        }

        [Fact]
        public void No_furniture_is_needed_to_have_a_subject_to_teach()
        {
            // the choice of subject takes no account of rooms or furniture at all
            var kids = new List<PupilSnapshot> { Kid(1, 9, ("Animals", 0)), Kid(2, 9, ("Animals", 1)) };
            Assert.Equal("Animals", LessonSubjects.Choose(new Dictionary<string, int> { ["Animals"] = 9 }, null, kids, null, null, 3));
        }

        // ---------------------------------------------------------------- experience

        [Fact]
        public void Experience_follows_the_time_actually_attended_delivery_and_what_there_was_to_learn()
        {
            float full = LessonMath.Xp(1f, 1f, 1f, 1f, 9);
            Assert.Equal(LessonMath.FullClassXp, full, 3);
            Assert.Equal(full / 2, LessonMath.Xp(.5f, 1f, 1f, 1f, 9), 2);          // half the class, half the experience
            Assert.Equal(0f, LessonMath.Xp(0f, 1.4f, 1.25f, 1f, 9));
            Assert.True(LessonMath.Xp(1f, 1.3f, 1f, 1f, 9) > LessonMath.Xp(1f, .7f, 1f, 1f, 9));          // a better teacher
            Assert.Equal(0f, LessonMath.Xp(1f, 1f, 1f, LessonSubjects.GapFactor(5, 4), 9));              // nothing the teacher can pass on
            Assert.True(LessonMath.Xp(1f, 1f, 1f, 1f, 9) > LessonMath.Xp(1f, 1f, 1f, 1f, 30));           // children take more in than adults
            Assert.Equal(0f, LessonSubjects.GapFactor(10, 9)); Assert.Equal(0f, LessonSubjects.GapFactor(10, 10)); Assert.Equal(0f, LessonSubjects.GapFactor(4, 12));
            Assert.True(LessonSubjects.GapFactor(14, 2) > LessonSubjects.GapFactor(6, 2));
            Assert.Equal(1f, LessonSubjects.GapFactor(20, 0));
            Assert.Equal(1f, LessonMath.AttendedShare(9000, 8000)); Assert.Equal(0f, LessonMath.AttendedShare(10, 0));
        }

        [Fact]
        public void Delivery_follows_the_teacher_s_subject_skill_more_than_anything_else_and_stays_bounded()
        {
            Assert.True(LessonMath.Delivery(16, 4, 0f, 0f, 0f) > LessonMath.Delivery(4, 16, 0f, 0f, 0f));
            Assert.True(LessonMath.Delivery(10, 10, 0f, 0f, 0f) > LessonMath.Delivery(10, 10, 0f, 0f, .25f));
            Assert.InRange(LessonMath.Delivery(0, 0, -5f, -5f, 5f), .5f, 1.4f);
            Assert.InRange(LessonMath.Delivery(20, 20, 5f, 5f, 0f), .5f, 1.4f);
            Assert.InRange(LessonMath.EngagementFactor(100f), .7f, 1.25f); Assert.InRange(LessonMath.EngagementFactor(-100f), .7f, 1.25f);
        }

        [Fact]
        public void A_book_gives_a_child_its_experience_once_in_twelve_days_and_only_as_far_as_the_teacher_can_explain_it()
        {
            Assert.Equal(0f, LessonMath.BookXp(1000f, 1f, 0f));          // no gap: the teacher cannot explain it
            Assert.Equal(500f, LessonMath.BookXp(1000f, 1f, 1f), 3);
            Assert.Equal(250f, LessonMath.BookXp(1000f, .5f, 1f), 3);
            var uses = new List<string> { "3|77|10000" };
            Assert.False(LessonMath.BookReady(uses, 3, 77, 10000 + 3 * 60000));
            Assert.True(LessonMath.BookReady(uses, 3, 77, 10000 + 13 * 60000));
            Assert.True(LessonMath.BookReady(uses, 4, 77, 10001));          // another child
            Assert.True(LessonMath.BookReady(uses, 3, 78, 10001));          // another book
            Assert.True(LessonMath.BookReady(null, 3, 77, 10001));
        }

        // ---------------------------------------------------------------- the record

        [Fact]
        public void The_lesson_record_is_compact_merges_by_pupil_and_subject_and_stays_bounded()
        {
            var log = new List<string>();
            LessonRecord.Add(log, new LessonRecord { Pupil = 5, Skill = "Cooking", Xp = 100f, Effect = 3000f, Ticks = 3000, Possible = 4000, Exchanges = 2, EngageSum = 2f, Classes = 1 });
            LessonRecord.Add(log, new LessonRecord { Pupil = 5, Skill = "Cooking", Xp = 50f, Effect = 1000f, Ticks = 1000, Possible = 4000, Exchanges = 1, EngageSum = 1f, Classes = 1 });
            LessonRecord.Add(log, new LessonRecord { Pupil = 5, Skill = "Plants", Xp = 10f, Ticks = 100, Possible = 100, Classes = 1 });
            Assert.Equal(2, log.Count);
            var cooking = LessonRecord.For(log, 5).First(r => r.Skill == "Cooking");
            Assert.Equal(150f, cooking.Xp); Assert.Equal(4000, cooking.Ticks); Assert.Equal(8000, cooking.Possible); Assert.Equal(2, cooking.Classes); Assert.Equal(3, cooking.Exchanges);
            Assert.Equal(cooking.Xp, LessonRecord.Parse(cooking.Encode()).Xp);
            Assert.Null(LessonRecord.Parse("nonsense")); Assert.Null(LessonRecord.Parse(null));
            for (int i = 0; i < 500; i++) LessonRecord.Add(log, new LessonRecord { Pupil = 1000 + i, Skill = "Cooking", Ticks = 1, Possible = 1, Classes = 1 });
            Assert.True(log.Count <= LessonRecord.MaxRecords);
            // a child who was away still has the time they could have attended recorded
            var away = LessonRecord.Absent(9, "Cooking", 4000);
            Assert.Equal(0, away.Ticks); Assert.Equal(4000, away.Possible);
        }

        // ---------------------------------------------------------------- the assessment

        private static LessonRecord Rec(int ticks, int possible, float delivery, float engage, int exchanges = 4, float xp = 400f)
            => new LessonRecord { Pupil = 1, Skill = "Cooking", Xp = xp, Effect = delivery * engage * ticks, Ticks = ticks, Possible = possible, Exchanges = exchanges, EngageSum = exchanges * 1f, Classes = 2 };

        [Fact]
        public void Too_little_evidence_is_incomplete_not_poor_and_earns_no_bonus()
        {
            var none = Assessments.Assess(null, 3, 3, 0, 5, 1f, 0f);
            Assert.Equal(Completion.Incomplete, none.Completion);
            var few = Assessments.Assess(Rec(300, 8000, 1f, 1f), 3, 3, 0, 5, 1f, 0f);
            Assert.Equal(Completion.Incomplete, few.Completion);
            var noSittings = Assessments.Assess(Rec(7000, 8000, 1f, 1f), 0, 3, 0, 5, 1f, 0f);
            Assert.Equal(Completion.Incomplete, noSittings.Completion);
            Assert.Equal(0f, Assessments.Bonus(Rec(300, 8000, 1f, 1f), few));
            // a child excused from sittings for illness is judged only on the sittings they could have sat
            var excused = Assessments.Assess(Rec(5000, 6000, 1.1f, 1.1f), 2, 3, 1, 5, 1f, 0f);
            Assert.NotEqual(Completion.Incomplete, excused.Completion);
            Assert.True(excused.Confidence >= .95f);
        }

        [Fact]
        public void The_result_depends_on_how_well_they_learned_not_where_they_started()
        {
            var rec = Rec(7000, 8000, 1.2f, 1.1f);
            var beginner = Assessments.Assess(rec, 3, 3, 0, 1, .9f, 0f);
            var master = Assessments.Assess(rec, 3, 3, 0, 19, .9f, 0f);
            Assert.Equal(beginner.Level, master.Level);          // the skill itself does not move the result
            Assert.Equal(beginner.Score, master.Score);
            Assert.Equal(ResultQuality.Excellent, beginner.Level);          // a beginner who learned well does best
            Assert.Equal(1, beginner.Attainment); Assert.Equal(19, master.Attainment);          // it is reported all the same
            // a near-ceiling child is not marked down for being near it
            Assert.True(master.Level >= ResultQuality.Good);
            // poor delivery and poor engagement give a weaker result
            var weak = Assessments.Assess(Rec(7000, 8000, .6f, .7f, 4), 3, 3, 0, 5, .5f, 0f);
            Assert.True(weak.Level < beginner.Level);
            var unengaged = new LessonRecord { Pupil = 1, Skill = "Cooking", Xp = 400f, Effect = .8f * 7000, Ticks = 7000, Possible = 8000, Exchanges = 4, EngageSum = -4f };
            Assert.True(Assessments.Assess(unengaged, 3, 3, 0, 5, .8f, 0f).Score < Assessments.Assess(Rec(7000, 8000, .8f, 1f), 3, 3, 0, 5, .8f, 0f).Score);
        }

        [Fact]
        public void Attendance_sets_confidence_and_completion_without_being_charged_twice()
        {
            var good = Rec(7000, 8000, 1.1f, 1.05f);
            var partial = Assessments.Assess(Rec(2500, 8000, 1.1f, 1.05f), 1, 3, 0, 5, .9f, 0f);
            Assert.Equal(Completion.Partial, partial.Completion);
            Assert.True(partial.Provisional);
            var whole = Assessments.Assess(good, 3, 3, 0, 5, .9f, 0f);
            Assert.Equal(Completion.Complete, whole.Completion);
            Assert.True(whole.Confidence > partial.Confidence);
            // the same quality of learning gives the same score whether they attended a lot or a little: absence is shown by confidence and completion
            Assert.Equal(whole.Score, Assessments.Assess(Rec(3500, 8000, 1.1f, 1.05f), 2, 3, 0, 5, .9f, 0f).Score, 3);
            // low confidence can never claim the very top
            var thin = Assessments.Assess(Rec(1700, 8000, 1.4f, 1.25f), 1, 3, 0, 5, 1f, 0f);
            Assert.True(thin.Level <= ResultQuality.Good);
        }

        [Fact]
        public void The_assessment_is_a_small_consolidation_of_what_was_studied_never_the_main_source()
        {
            var rec = Rec(7000, 8000, 1.2f, 1.1f, xp: 1000f);
            foreach (var q in new[] { ResultQuality.Poor, ResultQuality.Fair, ResultQuality.Good, ResultQuality.Excellent })
                Assert.True(Assessments.BonusShare(q) <= .2f);
            Assert.True(Assessments.BonusShare(ResultQuality.Excellent) > Assessments.BonusShare(ResultQuality.Poor));
            var res = Assessments.Assess(rec, 3, 3, 0, 5, .9f, 0f);
            float bonus = Assessments.Bonus(rec, res);
            Assert.InRange(bonus, 1f, 200.01f);
            Assert.True(bonus <= .2f * rec.Xp);
            // no studied subject, no reward: the bonus is a function of the record alone, so an unrelated skill can never receive one
            Assert.Equal(0f, Assessments.Bonus(null, res));
            var partial = Assessments.Assess(Rec(2500, 8000, 1.2f, 1.1f, xp: 1000f), 1, 3, 0, 5, .9f, 0f);
            Assert.True(Assessments.Bonus(Rec(2500, 8000, 1.2f, 1.1f, xp: 1000f), partial) < Assessments.BonusShare(partial.Level) * 1000f);
            output.WriteLine("full class XP " + LessonMath.FullClassXp + "; consolidation at Poor/Fair/Good/Excellent: " + string.Join("/", new[] { ResultQuality.Poor, ResultQuality.Fair, ResultQuality.Good, ResultQuality.Excellent }.Select(q => (Assessments.BonusShare(q) * 100) + "%")));
        }

        [Fact]
        public void Representative_assessment_results_are_spread_by_learning_not_by_luck()
        {
            var rng = new Random(11);
            var lines = new List<string>();
            foreach (var profile in new[] { ("weak teacher, unengaged", .65f, .75f, -.5f), ("below average", .85f, .9f, 0f), ("ordinary", 1f, 1f, .5f), ("good teacher, engaged", 1.25f, 1.1f, 1.2f) })
            {
                var counts = new int[4]; int incomplete = 0; const int N = 5000;
                for (int n = 0; n < N; n++)
                {
                    int attended = rng.Next(1500, 8001);
                    var rec = new LessonRecord { Pupil = 1, Skill = "Cooking", Xp = 300f, Effect = profile.Item2 * profile.Item3 * attended, Ticks = attended, Possible = 8000, Exchanges = 4, EngageSum = 4 * profile.Item4, Classes = 2 };
                    var res = Assessments.Assess(rec, rng.Next(0, 4), 3, 0, rng.Next(0, 20), Math.Min(1f, profile.Item2 / 1.2f), (float)(rng.NextDouble() * .12 - .06));
                    if (res.Completion == Completion.Incomplete) incomplete++; else counts[(int)res.Level]++;
                }
                lines.Add(string.Format("{0,-26} incomplete {1,5:P1}  Poor {2,5:P1}  Fair {3,5:P1}  Good {4,5:P1}  Excellent {5,5:P1}", profile.Item1, incomplete / (float)N, counts[0] / (float)N, counts[1] / (float)N, counts[2] / (float)N, counts[3] / (float)N));
                Assert.True(counts.Sum() + incomplete == N);
            }
            foreach (var l in lines) output.WriteLine(l);
        }
    }
}
