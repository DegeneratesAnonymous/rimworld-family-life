using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class GuidedExchangeTests
    {
        private static string Root()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "Source", "FamilyLife", "FamilyLife.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new System.IO.FileNotFoundException("repository root");
        }

        [Fact]
        public void A_skilled_leader_does_better_than_an_unskilled_one_against_the_same_person()
        {
            // Social 15 + Medicine 15 against Social 6, same dice.
            var skilled = GuidedExchanges.Resolve(GuidedExchanges.LeaderScore(15, 15), 6, 10, 10);
            var novice = GuidedExchanges.Resolve(GuidedExchanges.LeaderScore(2, 2), 6, 10, 10);
            Assert.True(skilled > novice);
            Assert.Equal(EventTier.Great, skilled);
            Assert.Equal(EventTier.Poor, GuidedExchanges.Resolve(GuidedExchanges.LeaderScore(0, 0), 20, 1, 20));
        }

        [Fact]
        public void A_persuasive_participant_is_harder_to_win_over()
        {
            var easy = GuidedExchanges.Resolve(15, 2, 10, 10);
            var hard = GuidedExchanges.Resolve(15, 18, 10, 10);
            Assert.True(easy > hard);
        }

        [Fact]
        public void A_disabled_skill_counts_as_nothing_and_never_helps()
        {
            Assert.Equal(8, GuidedExchanges.LeaderScore(8, -1));
            Assert.Equal(0, GuidedExchanges.LeaderScore(-3, -1));
        }

        [Fact]
        public void Good_talk_improves_how_people_feel_and_poor_talk_harms_it()
        {
            var good = GuidedExchanges.Effect(ExchangeTopic.Advice, EventTier.Good);
            var poor = GuidedExchanges.Effect(ExchangeTopic.Advice, EventTier.Poor);
            Assert.True(good.Trust > 0 && good.Tension < 0 && good.Quality > 0);
            Assert.True(poor.Trust < 0 && poor.Tension > 0 && poor.Quality < 0);
            Assert.True(GuidedExchanges.Effect(ExchangeTopic.Advice, EventTier.Great).Quality > good.Quality);
            Assert.True(GuidedExchanges.Effect(ExchangeTopic.CatchUp, EventTier.Good).Quality < good.Quality);       // small talk matters less than advice
        }

        [Fact]
        public void The_talk_changes_how_long_a_checkup_lasts_and_how_much_a_pupil_learns_within_limits()
        {
            Assert.True(GuidedExchanges.CheckupDurationFactor(4f) > 1f);
            Assert.True(GuidedExchanges.CheckupDurationFactor(-4f) < 1f);
            Assert.Equal(1.6f, GuidedExchanges.CheckupDurationFactor(100f));
            Assert.Equal(.7f, GuidedExchanges.CheckupDurationFactor(-100f));
            Assert.Equal(1.5f, GuidedExchanges.LessonFactor(100f));
            Assert.Equal(.6f, GuidedExchanges.LessonFactor(-100f));
            Assert.Equal(1, GuidedExchanges.Impression(2.5f)); Assert.Equal(-1, GuidedExchanges.Impression(-2f)); Assert.Equal(0, GuidedExchanges.Impression(1f));
        }

        [Fact]
        public void Topics_vary_through_a_visit_and_never_repeat_back_to_back()
        {
            for (int seed = 0; seed < 12; seed++)
            {
                var a = GuidedExchanges.PickTopic(GuidedExchanges.ClinicTopics, 0, seed);
                var b = GuidedExchanges.PickTopic(GuidedExchanges.ClinicTopics, 1, seed);
                var c = GuidedExchanges.PickTopic(GuidedExchanges.ClinicTopics, 2, seed);
                Assert.NotEqual(a, b); Assert.NotEqual(b, c);
            }
            Assert.Equal(5, GuidedExchanges.ClassTopics.Length);
        }

        [Fact]
        public void Every_line_a_topic_can_say_exists_in_the_string_file()
        {
            string xml = System.IO.File.ReadAllText(System.IO.Path.Combine(Root(), "Languages", "English", "Keyed", "FamilyExchanges.xml"));
            foreach (var topic in System.Enum.GetValues(typeof(ExchangeTopic)))
                foreach (var tier in new[] { "Poor", "Flat", "Good", "Great" })
                    foreach (var who in new[] { "Lead", "Reply" })
                        Assert.Contains("<WFL_Ex_" + topic + "_" + tier + "_" + who + "_0>", xml);
        }
    }
}
