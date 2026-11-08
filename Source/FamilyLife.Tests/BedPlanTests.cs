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
        public void A_parent_chooses_first_and_the_child_takes_the_best_bed_nearest_them_whatever_kind_it_is()
        {
            // The parent is housed before the child (all beds are equally near here), so the child follows with the best that is left.
            var plan = BedPlan.Deal(new[] { Adult(1), Kid(6, SleeperStage.Child, 1) },
                new[] { Bed(10, 30), Bed(11, 95, capacity: 2, room: 2), Bed(12, 60) });
            Assert.Equal(11, plan[1]);
            Assert.Equal(12, plan[6]);
        }

        [Fact]
        public void A_child_takes_the_bed_nearest_a_parent_by_the_walk_not_in_a_straight_line()
        {
            var beds = new[] { Bed(10, 50, 1, x: 0), Bed(11, 90, 2, x: 3), Bed(12, 40, 3, x: 20) };
            // Straight lines say bed 11 is next to the parent's bed 10; the walk (round a wall) says bed 12 is the nearer.
            float Walk(int a, int b) => a == b ? 0 : (a == 10 && b == 11) || (a == 11 && b == 10) ? 60 : 8;
            var plan = BedPlan.Deal(new[] { Kid(6, SleeperStage.Child, 1) }, beds, null, new Dictionary<int, int> { { 1, 10 } }, Walk);
            Assert.Equal(12, plan[6]);
        }

        [Fact]
        public void A_child_is_placed_near_a_parent_who_keeps_their_own_bed()
        {
            // The parent is not part of the deal at all: the bed is theirs. The child still goes near it, not to the most comfortable bed in the house.
            var beds = new[] { Bed(10, 50, 1, x: 0), Bed(11, 40, 2, x: 4), Bed(12, 95, 3, x: 60) };
            var plan = BedPlan.Deal(new[] { Kid(6, SleeperStage.Child, 1) }, beds, null, new Dictionary<int, int> { { 1, 10 } });
            Assert.Equal(11, plan[6]);
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
        public void A_single_parent_is_housed_first_and_their_child_is_put_near_them()
        {
            // The parent picks first (the most comfortable); the child then goes to the nearest bed to the parent's.
            var plan = BedPlan.Deal(new[] { Kid(1, SleeperStage.Child, 5), Adult(5) },
                new[] { Bed(10, 70, 1, x: 0), Bed(11, 50, 2, x: 1), Bed(12, 40, 1, x: 30) });
            Assert.Equal(10, plan[5]);
            Assert.Equal(11, plan[1]);
        }

        [Fact]
        public void A_couple_picks_a_room_with_a_bed_for_their_baby_even_over_a_better_room_without_one()
        {
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2, 1), Kid(7, SleeperStage.Baby, 1, 2) },
                new[] { Bed(10, 90, 1, 2, 0), Bed(11, 60, 2, 2, 20), Bed(12, 10, 2, 1, 21), Bed(13, 95, 3, 1, 50) });
            Assert.Equal(11, plan[1]);
            Assert.Equal(12, plan[7]);      // in the room with them, though the other room is better
        }

        [Fact]
        public void A_baby_is_still_housed_as_near_as_possible_when_no_room_has_space()
        {
            var plan = BedPlan.Deal(new[] { Adult(1, 2), Adult(2, 1), Kid(7, SleeperStage.Baby, 1, 2) },
                new[] { Bed(10, 90, 1, 2, 0), Bed(11, 30, 2, 1, 5), Bed(12, 80, 3, 1, 60) });
            Assert.Equal(10, plan[1]);
            Assert.Equal(11, plan[7]);
        }

        [Fact]
        public void A_single_parent_and_their_baby_share_a_room_when_one_has_space_for_both()
        {
            var plan = BedPlan.Deal(new[] { Adult(1), Kid(7, SleeperStage.Baby, 1) },
                new[] { Bed(10, 90, 1, 1, 0), Bed(11, 50, 2, 1, 20), Bed(12, 20, 2, 1, 21) });
            Assert.Equal(11, plan[1]);
            Assert.Equal(12, plan[7]);
        }

        [Fact]
        public void A_child_past_the_baby_stage_takes_the_bed_nearest_their_parent()
        {
            // The parent takes the most comfortable bed (11); the child takes the nearest to it, 12, rather than the one in the parent's own room's far corner.
            var plan = BedPlan.Deal(new[] { Adult(1), Kid(7, SleeperStage.Child, 1) },
                new[] { Bed(10, 40, 1, 1, 0), Bed(11, 90, 2, 1, 20), Bed(12, 30, 1, 1, 19) });
            Assert.Equal(11, plan[1]);
            Assert.Equal(12, plan[7]);
        }

        private static BedSleeper Aged(BedSleeper s, float age) { s.AgeYears = age; return s; }

        [Fact]
        public void The_couple_with_a_baby_then_the_eldest_couple_choose_first()
        {
            var sleepers = new[] { Aged(Adult(1, 2), 25), Aged(Adult(2, 1), 25), Aged(Adult(3, 4), 70), Aged(Adult(4, 3), 70), Aged(Adult(5, 6), 30), Aged(Adult(6, 5), 30), Kid(9, SleeperStage.Baby, 5, 6) };
            var beds = new[] { Bed(10, 90, 1, 2, 0), Bed(11, 80, 2, 2, 10), Bed(12, 70, 3, 2, 20), Bed(13, 10, 1, 1, 1), Bed(14, 10, 4, 1, 99) };
            var plan = BedPlan.Deal(sleepers, beds);
            Assert.Equal(10, plan[5]);      // the couple with the baby
            Assert.Equal(11, plan[3]);      // then the eldest
            Assert.Equal(12, plan[1]);      // then the young couple
        }

        [Fact]
        public void An_adult_with_no_dependants_picks_before_a_child()
        {
            var plan = BedPlan.Deal(new[] { Aged(Adult(1), 60), Aged(Kid(6, SleeperStage.Child, 9), 10) }, new[] { Bed(10, 90), Bed(11, 40) });
            Assert.Equal(10, plan[1]);
            Assert.Equal(11, plan[6]);
        }

        [Fact]
        public void A_child_of_any_age_takes_the_bed_nearest_a_parent_and_not_the_most_comfortable_one_far_away()
        {
            var couple = new[] { Aged(Adult(1, 2), 30), Aged(Adult(2, 1), 30) };
            var beds = new[] { Bed(10, 50, 1, 2, 0), Bed(11, 40, 2, 1, 5), Bed(12, 95, 3, 1, 80) };
            var small = BedPlan.Deal(couple.Concat(new[] { Aged(Kid(6, SleeperStage.Child, 1, 2), 4) }).ToList(), beds);
            Assert.Equal(11, small[6]);
            var older = BedPlan.Deal(couple.Concat(new[] { Aged(Kid(6, SleeperStage.Child, 1, 2), 9) }).ToList(), beds);
            Assert.Equal(11, older[6]);
        }

        [Fact]
        public void Brothers_and_sisters_lean_towards_one_room()
        {
            var sleepers = new[] { Adult(1, 2), Adult(2, 1), Aged(Kid(6, SleeperStage.Child, 1, 2), 10), Aged(Kid(7, SleeperStage.Child, 1, 2), 11) };
            var beds = new[] { Bed(10, 90, 1, 2, 0), Bed(11, 60, 2, 1, 30), Bed(12, 58, 3, 1, 50), Bed(13, 50, 2, 1, 31) };
            var plan = BedPlan.Deal(sleepers, beds);
            Assert.Equal(11, plan[6]);
            Assert.Equal(13, plan[7]);      // the same room as their sister, though the other bed is a little more comfortable
        }

        private static int RoomOf(BedSlot[] beds, int id) => beds.First(b => b.Id == id).RoomId;

        [Fact]
        public void Someone_already_housed_is_not_moved_and_a_new_baby_joins_their_room()
        {
            var beds = new[] { Bed(10, 90, 1, 2, 0), Bed(11, 20, 1, 1, 1), Bed(12, 60, 2, 1, 40) };
            var fixedBeds = new Dictionary<int, int> { { 1, 10 }, { 2, 10 } };
            var plan = BedPlan.Deal(new[] { Kid(9, SleeperStage.Baby, 1, 2) }, beds, null, fixedBeds);
            Assert.Single(plan);
            Assert.Equal(11, plan[9]);
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
