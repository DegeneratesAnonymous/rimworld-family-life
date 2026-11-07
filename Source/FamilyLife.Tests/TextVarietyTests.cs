using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class TextVarietyTests
    {
        [Fact]
        public void The_same_seed_always_gives_the_same_choice_and_different_seeds_spread_across_the_variants()
        {
            Assert.Equal(TextVariety.Index(5, 12345), TextVariety.Index(5, 12345));
            var seen = Enumerable.Range(0, 200).Select(s => TextVariety.Index(5, TextVariety.Seed("A1-01", s, 3, s))).Distinct().ToList();
            Assert.Equal(5, seen.Count);                                           // all five variants are used
            Assert.All(seen, i => Assert.InRange(i, 0, 4));
            Assert.Equal(0, TextVariety.Index(0, 99));
            Assert.InRange(TextVariety.Index(3, int.MinValue), 0, 2);
        }

        [Fact]
        public void A_lead_in_or_closing_touch_is_used_some_of_the_time_not_always_and_not_never()
        {
            int leads = Enumerable.Range(0, 1000).Count(s => TextVariety.Chance(s, 1, 40));
            int tails = Enumerable.Range(0, 1000).Count(s => TextVariety.Chance(s, 2, 35));
            Assert.InRange(leads, 300, 500);
            Assert.InRange(tails, 250, 450);
        }

        [Fact]
        public void The_day_is_split_into_five_parts()
        {
            Assert.Equal("Dawn", TextVariety.DayPart(6));
            Assert.Equal("Morning", TextVariety.DayPart(9));
            Assert.Equal("Afternoon", TextVariety.DayPart(14));
            Assert.Equal("Evening", TextVariety.DayPart(19));
            Assert.Equal("Night", TextVariety.DayPart(2));
            Assert.Equal("Night", TextVariety.DayPart(23));
        }

        [Fact]
        public void A_lead_in_is_only_for_lines_that_open_with_a_name()
        {
            Assert.True(TextVariety.OpensWithName("Ana and Bo talked.", "Ana", "Bo", ""));
            Assert.False(TextVariety.OpensWithName("The old argument returned.", "Ana", "Bo", ""));
            Assert.False(TextVariety.OpensWithName("", "Ana"));
        }
    }
}
