using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>Time together is not simply good: it helps when people like each other, grates when they do not, and helps less the closer they already are.</summary>
    public class FeelingTests
    {
        private const int Hour = BondTuning.TicksPerHour;
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry Pair(out int now)
        {
            var r = new FamilyRegistry();
            now = 2 * Day;
            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }),
                T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 })
            }, now);
            r.UpdateBonds((a, b) => 0, now, null);
            return r;
        }

        [Fact]
        public void The_last_opinion_is_remembered_on_the_bond()
        {
            var r = Pair(out int now);
            r.UpdateBonds((a, b) => a == 1 && b == 3 ? -30 : 0, now + Hour, null);
            Assert.Equal(-30f, r.GetBond(1, 3).LastOpinion);
        }

        [Fact]
        public void Time_with_someone_disliked_adds_tension_and_no_attachment()
        {
            var r = Pair(out int now);
            var bond = r.GetBond(1, 3);
            bond.LastOpinion = -45f;
            bond.Tension = 10f;
            float attachment = bond.Attachment;
            r.RecordTogether(1, 3, 2 * Hour, now);
            Assert.True(bond.Tension > 10f, "forced company with someone you dislike should wear on you");
            Assert.True(bond.Attachment <= attachment + 0.001f);
        }

        [Fact]
        public void Time_with_someone_liked_helps_more_than_with_someone_disliked()
        {
            var r = Pair(out int now);
            var liked = r.GetBond(1, 3);
            var other = r.GetBond(2, 3);
            liked.LastOpinion = 45f; other.LastOpinion = -20f;
            liked.Attachment = other.Attachment = 40f;
            r.RecordTogether(1, 3, 2 * Hour, now);
            r.RecordTogether(2, 3, 2 * Hour, now);
            Assert.True(liked.Attachment - 40f > other.Attachment - 40f);
        }

        [Fact]
        public void The_closer_two_people_already_are_the_less_time_together_adds()
        {
            var r = Pair(out int now);
            var distant = r.GetBond(1, 3);
            var close = r.GetBond(2, 3);
            distant.LastOpinion = close.LastOpinion = 45f;
            distant.Attachment = 30f; close.Attachment = 90f;
            r.RecordTogether(1, 3, 2 * Hour, now);
            r.RecordTogether(2, 3, 2 * Hour, now);
            Assert.True(distant.Attachment - 30f > close.Attachment - 90f);
        }
    }
}
