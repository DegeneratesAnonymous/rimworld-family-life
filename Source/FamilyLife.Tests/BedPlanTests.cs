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
        private static BedSlot Single(int id, float x, float z = 0) => new BedSlot { Id = id, Capacity = 1, X = x, Z = z };
        private static BedSlot Double(int id, float x, float z = 0) => new BedSlot { Id = id, Capacity = 2, X = x, Z = z };
        private static BedSlot Crib(int id, float x, float z = 0) => new BedSlot { Id = id, Capacity = 1, Crib = true, X = x, Z = z };

        [Fact]
        public void A_couple_shares_the_double_bed_and_others_get_singles()
        {
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2, 1), Adult(3) }, new[] { Single(10, 0), Double(11, 5), Single(12, 9) });
            Assert.Equal(11, plan[1]);
            Assert.Equal(11, plan[2]);
            Assert.Equal(10, plan[3]);
        }

        [Fact]
        public void A_couple_without_a_double_gets_two_beds_side_by_side()
        {
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2, 1) }, new[] { Single(10, 0), Single(11, 30), Single(12, 1) });
            Assert.Equal(10, plan[1]);
            Assert.Equal(12, plan[2]);       // the one beside it, not the one across the room
        }

        [Fact]
        public void Partner_listed_by_one_side_only_still_counts()
        {
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2) }, new[] { Double(10, 0) });
            Assert.Equal(plan[1], plan[2]);
        }

        [Fact]
        public void A_single_adult_uses_a_double_only_when_no_single_is_left()
        {
            var one = BedPlan.Deal(new[] { Adult(1) }, new[] { Double(10, 0), Single(11, 5) });
            Assert.Equal(11, one[1]);
            var two = BedPlan.Deal(new[] { Adult(1) }, new[] { Double(10, 0) });
            Assert.Equal(10, two[1]);
        }

        [Fact]
        public void A_baby_gets_the_crib_nearest_a_parent_and_never_an_adult_bed_before_a_crib()
        {
            var plan = BedPlan.Deal(new[] { Adult(1), Kid(5, SleeperStage.Baby, 1) },
                new[] { Single(10, 0), Crib(20, 40), Crib(21, 2), Single(11, 1) });
            Assert.Equal(10, plan[1]);
            Assert.Equal(21, plan[5]);
        }

        [Fact]
        public void A_child_gets_the_bed_nearest_a_parent_and_never_a_crib()
        {
            var plan = BedPlan.Deal(new[] { Adult(1), Kid(6, SleeperStage.Child, 1) },
                new[] { Single(10, 0), Crib(20, 1), Single(11, 50), Single(12, 3) });
            Assert.Equal(12, plan[6]);
        }

        [Fact]
        public void Nobody_is_given_a_bed_that_another_has_and_the_surplus_go_without()
        {
            var sleepers = new[] { Adult(1), Adult(2), Adult(3) };
            var plan = BedPlan.Deal(sleepers, new[] { Single(10, 0), Single(11, 1) });
            Assert.Equal(2, plan.Count);
            Assert.Equal(2, plan.Values.Distinct().Count());
        }

        [Fact]
        public void A_double_holds_only_the_couple()
        {
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2, 1), Kid(6, SleeperStage.Child, 1, 2) }, new[] { Double(10, 0), Single(11, 4) });
            Assert.Equal(plan[1], plan[2]);
            Assert.Equal(11, plan[6]);
        }

        [Fact]
        public void A_bed_a_person_cannot_use_is_skipped()
        {
            var plan = BedPlan.Deal(new[] { Adult(1) }, new[] { Single(10, 0), Single(11, 1) }, (s, b) => b != 10);
            Assert.Equal(11, plan[1]);
        }

        [Fact]
        public void The_same_input_gives_the_same_answer()
        {
            var sleepers = new[] { Adult(3), Adult(1, 2), Adult(2, 1), Kid(8, SleeperStage.Child, 1) };
            var beds = new[] { Single(12, 4), Double(10, 0), Single(11, 2) };
            var a = BedPlan.Deal(sleepers, beds);
            var b = BedPlan.Deal(sleepers.Reverse().ToList(), beds.Reverse().ToList());
            Assert.Equal(a.OrderBy(k => k.Key), b.OrderBy(k => k.Key));
        }
    }
}
