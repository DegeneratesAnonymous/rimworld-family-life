using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class OfficeEventTests
    {
        [Fact]
        public void A_better_doctor_gives_a_better_checkup_and_a_good_talk_adds_to_it()
        {
            var novice = CheckupQuality.Level(CheckupQuality.Score(2, 2, 0f, 10));
            var expert = CheckupQuality.Level(CheckupQuality.Score(16, 10, 0f, 10));
            Assert.True(expert > novice);
            Assert.Equal(ResultQuality.Poor, novice);
            Assert.True(CheckupQuality.Level(CheckupQuality.Score(8, 6, 3f, 10)) > CheckupQuality.Level(CheckupQuality.Score(8, 6, -3f, 10)));
        }

        [Fact]
        public void The_benefit_grows_with_the_quality_in_both_strength_and_length()
        {
            var levels = new[] { ResultQuality.Poor, ResultQuality.Fair, ResultQuality.Good, ResultQuality.Excellent };
            for (int i = 1; i < levels.Length; i++)
            {
                Assert.True(CheckupQuality.DurationFactor(levels[i]) > CheckupQuality.DurationFactor(levels[i - 1]));
                Assert.True(CheckupQuality.Severity(levels[i]) > CheckupQuality.Severity(levels[i - 1]));
            }
            // the stages of the condition begin at these severities, so each quality lands in its own stage
            var stages = new[] { 0f, .25f, .5f, .75f };
            for (int i = 0; i < levels.Length; i++)
                Assert.Equal(i, stages.Count(st => CheckupQuality.Severity(levels[i]) >= st) - 1);
        }

        [Fact]
        public void Testing_quality_follows_the_share_of_checks_passed_and_needs_enough_checks()
        {
            Assert.Equal(ResultQuality.Poor, TestingMath.Level(11, 11));          // too few sat to be judged
            Assert.Equal(ResultQuality.Poor, TestingMath.Level(4, 20));
            Assert.Equal(ResultQuality.Fair, TestingMath.Level(11, 20));
            Assert.Equal(ResultQuality.Good, TestingMath.Level(15, 20));
            Assert.Equal(ResultQuality.Excellent, TestingMath.Level(19, 20));
        }

        [Fact]
        public void Rewards_rise_with_quality_in_both_the_number_of_skills_and_the_experience_each()
        {
            var levels = new[] { ResultQuality.Poor, ResultQuality.Fair, ResultQuality.Good, ResultQuality.Excellent };
            for (int i = 1; i < levels.Length; i++)
            {
                Assert.True(TestingMath.SkillCount(levels[i]) > TestingMath.SkillCount(levels[i - 1]));
                Assert.True(TestingMath.XpEach(levels[i]) > TestingMath.XpEach(levels[i - 1]));
                Assert.True(TestingMath.TotalXp(levels[i]) > TestingMath.TotalXp(levels[i - 1]));
            }
        }

        [Fact]
        public void A_good_teacher_lowers_the_bar_and_a_sharper_child_clears_it_more_often()
        {
            Assert.True(TestingMath.Difficulty(24) < TestingMath.Difficulty(0));
            Assert.Equal(TestingMath.BaseDifficulty - 4, TestingMath.Difficulty(100));          // never more than four off
            int Passed(int intellect, int best) { int n = 0; for (int d = 1; d <= 20; d++) if (TestingMath.Passes(intellect, best, d, 16)) n++; return n; }
            Assert.True(Passed(10, 12) > Passed(2, 4));
        }

        [Fact]
        public void The_doctor_sees_the_sick_first_in_an_outbreak()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Doc"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }), T.P(4, "Four", parents: new[] { 1 }), T.P(5, "Five", parents: new[] { 1 }) }, 1000);
            var o = new CommunityOffice { Kind = OfficeKind.Healer, HolderId = 1, HolderFamily = r.FamilyOf(1), RoundStartTick = 1000 };
            r.Offices.Add(o);
            int first = r.NextCheckupPatient(1500, null, id => id == 4 ? 0 : 1);
            Assert.Equal(4, first);
            Assert.Equal(2, r.NextCheckupPatient(1500, null));          // without an outbreak, in the usual order
        }
    }
}
