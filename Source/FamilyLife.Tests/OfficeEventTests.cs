using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class OfficeEventTests
    {
        [Fact]
        public void A_better_doctor_gives_a_better_checkup_and_a_good_talk_adds_a_little_to_it()
        {
            var novice = CheckupQuality.Level(CheckupQuality.Score(3, 3, 0f, 0f, 0));
            var expert = CheckupQuality.Level(CheckupQuality.Score(16, 10, 0f, 0f, 0));
            Assert.True(expert > novice);
            Assert.Equal(ResultQuality.Poor, novice);
            Assert.True(CheckupQuality.Score(8, 6, 0f, 3f, 0) > CheckupQuality.Score(8, 6, 0f, -3f, 0));
            // but the talk is bounded: it moves the result by two points at most between the best and worst
            Assert.True(CheckupQuality.Score(8, 6, 0f, 100f, 0) - CheckupQuality.Score(8, 6, 0f, -100f, 0) <= 2.001f);
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
            // modest: the longest benefit is no more than a quarter longer than an ordinary one
            Assert.True(CheckupQuality.DurationFactor(ResultQuality.Excellent) <= 1.25f);
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

        [Fact]
        public void The_assessment_is_a_few_daytime_sittings_over_two_days_not_an_hourly_summons()
        {
            Assert.InRange(TestingMath.Offsets.Length, 2, 4);
            Assert.True(TestingMath.MaxSessions <= 4);
            Assert.Equal(600, TestingMath.SitTicks);
            // at most four sittings of ten game minutes: well under an hour of sitting, where it used to be more than eleven
            Assert.True(TestingMath.MaxSessions * TestingMath.SitTicks <= 2500);
            Assert.True(TestingMath.Daytime(12));
            Assert.False(TestingMath.Daytime(3));          // never in the night
            Assert.False(TestingMath.Daytime(22));
            Assert.All(TestingMath.Offsets, off => Assert.True(off < TestingMath.DurationTicks));
        }

        [Fact]
        public void The_make_up_sitting_comes_only_if_someone_needs_it_and_the_assessment_ends_by_itself()
        {
            Assert.True(TestingMath.Due(0, TestingMath.Offsets[0]));
            Assert.False(TestingMath.Due(0, TestingMath.Offsets[0] - 1));
            Assert.False(TestingMath.Due(1, TestingMath.Offsets[0] + 1));
            Assert.True(TestingMath.MakeUpDue(3, TestingMath.DurationTicks - 12000, true));
            Assert.False(TestingMath.MakeUpDue(3, TestingMath.DurationTicks - 12000, false));
            Assert.True(TestingMath.Over(3, TestingMath.DurationTicks, false));
            Assert.False(TestingMath.Over(3, TestingMath.DurationTicks, true));          // waits for the make-up
            Assert.True(TestingMath.Over(3, TestingMath.DurationTicks + TestingMath.MakeUpTicks, true));
            Assert.True(TestingMath.Over(4, 10, true));
        }
    }
}
