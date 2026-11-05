using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class BedPlanTests
    {
        private static BedSleeper Adult(int id, params int[] partners) => new BedSleeper { Id = id, Stage = SleeperStage.Adult, PartnerIds = partners.ToList() };
        private static BedSleeper Kid(int id, SleeperStage stage, params int[] parents) => new BedSleeper { Id = id, Stage = stage, ParentIds = parents.ToList() };
        private static BedSlot Bed(int id, float comfort, int room = -1, int capacity = 1, float x = 0) => new BedSlot { Id = id, Comfort = comfort, RoomId = room, Capacity = capacity, X = x };

        [Fact]
        public void A_couple_takes_the_most_comfortable_place_to_sleep_together()
        {
            // A modest double, a fine double, and two singles in the best room of all.
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2, 1) },
                new[] { Bed(10, 20, 1, 2), Bed(11, 50, 2, 2), Bed(12, 80, 3), Bed(13, 80, 3) });
            Assert.Equal(12, plan[1]);
            Assert.Equal(13, plan[2]);
        }

        [Fact]
        public void A_double_bed_is_chosen_when_it_is_the_better_room()
        {
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2, 1) },
                new[] { Bed(10, 60, 1, 2), Bed(12, 40, 3), Bed(13, 40, 3) });
            Assert.Equal(10, plan[1]);
            Assert.Equal(10, plan[2]);
        }

        [Fact]
        public void Two_beds_in_different_rooms_do_not_count_as_together()
        {
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2, 1) }, new[] { Bed(10, 90, 1), Bed(11, 90, 2) });
            // No place to sleep together: they are not treated as a couple, and each simply gets a bed of their own.
            Assert.Equal(2, plan.Count);
            Assert.NotEqual(plan[1], plan[2]);
        }

        [Fact]
        public void Couples_choose_before_anyone_else()
        {
            // The single adult has the lower id and would otherwise take the best bed.
            var plan = BedPlan.Deal(new[] { Adult(1), Adult(2, 3), Adult(3, 2) }, new[] { Bed(10, 90, 1, 2), Bed(11, 10, 2) });
            Assert.Equal(10, plan[2]);
            Assert.Equal(10, plan[3]);
            Assert.Equal(11, plan[1]);
        }

        [Fact]
        public void A_child_takes_the_most_comfortable_bed_left_whatever_kind_it_is()
        {
            var plan = BedPlan.Deal(new[] { Adult(1), Kid(6, SleeperStage.Child, 1) },
                new[] { Bed(10, 30), Bed(11, 95, capacity: 2, room: 2), Bed(12, 60) });
            Assert.Equal(11, plan[6]);          // a double bed is fine for a child
            Assert.Equal(12, plan[1]);
        }

        [Fact]
        public void Equally_comfortable_beds_go_to_the_one_nearer_a_parent()
        {
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2, 1), Kid(6, SleeperStage.Child, 1, 2) },
                new[] { Bed(10, 50, 1, 2, 0), Bed(11, 50, x: 40), Bed(12, 50, x: 1) });
            Assert.Equal(10, plan[1]);
            Assert.Equal(12, plan[6]);
        }

        [Fact]
        public void A_bed_a_person_cannot_use_is_skipped_for_them()
        {
            var plan = BedPlan.Deal(new[] { Kid(5, SleeperStage.Baby, 1) }, new[] { Bed(10, 90), Bed(11, 10) }, (s, b) => b == 11);
            Assert.Equal(11, plan[5]);
        }

        [Fact]
        public void Nobody_is_given_a_bed_that_another_has_and_the_surplus_go_without()
        {
            var plan = BedPlan.Deal(new[] { Adult(1), Adult(2), Adult(3) }, new[] { Bed(10, 5), Bed(11, 6) });
            Assert.Equal(2, plan.Count);
            Assert.Equal(2, plan.Values.Distinct().Count());
        }

        [Fact]
        public void A_double_holds_only_the_couple()
        {
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2, 1), Kid(6, SleeperStage.Child, 1, 2) }, new[] { Bed(10, 90, 1, 2), Bed(11, 20, 2) });
            Assert.Equal(plan[1], plan[2]);
            Assert.Equal(11, plan[6]);
        }

        [Fact]
        public void The_same_input_gives_the_same_answer()
        {
            var sleepers = new[] { Adult(3), Adult(1, 2), Adult(2, 1), Kid(8, SleeperStage.Child, 1) };
            var beds = new[] { Bed(12, 50, x: 4), Bed(10, 70, 1, 2, 0), Bed(11, 50, x: 2) };
            var a = BedPlan.Deal(sleepers, beds);
            var b = BedPlan.Deal(sleepers.Reverse().ToList(), beds.Reverse().ToList());
            Assert.Equal(a.OrderBy(k => k.Key), b.OrderBy(k => k.Key));
        }
    }
}
