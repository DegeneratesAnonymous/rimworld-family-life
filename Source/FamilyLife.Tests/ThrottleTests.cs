using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class ThrottleTests
    {
        [Fact]
        public void Something_just_tried_waits_for_its_cooldown_and_channels_are_separate()
        {
            var t = new AttemptThrottle();
            Assert.True(t.Allowed(1, 1000, 600));
            t.Note(1, 1000);
            Assert.False(t.Allowed(1, 1500, 600));
            Assert.True(t.Allowed(1, 1600, 600));
            Assert.True(t.Allowed(2, 1500, 600));                 // someone else
            Assert.True(t.Allowed(1, 1500, 600, channel: 5));     // another kind of attempt by the same pawn
        }

        [Fact]
        public void A_clock_that_went_backwards_does_not_block_forever()
        {
            var t = new AttemptThrottle();
            t.Note(1, 900000);
            Assert.True(t.Allowed(1, 100, 600));
        }

        [Fact]
        public void A_busy_colony_does_not_sweep_the_table_on_every_call()
        {
            var t = new AttemptThrottle();
            int now = 0;
            // 300 pawns, three kinds of attempt each, all noted over and over, all recent: nothing is stale, so nothing should be swept
            // more than once per interval no matter how often it is called.
            for (int round = 0; round < 50; round++)
            {
                now += 100;
                for (int pawn = 0; pawn < 300; pawn++)
                    for (int channel = 0; channel < 3; channel++) t.Note(pawn, now, channel);
            }
            Assert.True(t.Count > AttemptThrottle.PruneAbove);
            Assert.True(t.Sweeps <= 2, "the table was swept " + t.Sweeps + " times in 5000 ticks");
        }

        [Fact]
        public void Old_entries_are_eventually_cleared_so_the_table_stays_bounded()
        {
            var t = new AttemptThrottle();
            int now = 0;
            for (int i = 0; i < 5000; i++) { now += 50; t.Note(i, now); }       // every pawn id seen once, over 250000 ticks
            Assert.True(t.Count < 2500, "entries are never cleared: " + t.Count);
        }
    }
}
