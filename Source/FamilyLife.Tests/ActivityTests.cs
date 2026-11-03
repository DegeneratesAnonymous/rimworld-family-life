using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class ActivityTests
    {
        private const int Hour = BondTuning.TicksPerHour;
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry Household(out int now)
        {
            var r = new FamilyRegistry();
            now = 2 * Day;
            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }, exes: new[] { 4 }),
                T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }),
                T.P(4, "Gran", "Smith", exes: new[] { 1 }, birth: -20_000_000),
                T.P(9, "Stranger", "Wolf")
            }, now);
            r.UpdateBonds((a, b) => 0, now, null);
            return r;
        }

        // ---------------------------------------------------------------- the family-time budget

        [Fact]
        public void The_budget_counts_time_per_day_and_resets_with_the_next_day()
        {
            var r = Household(out int now);
            int target = ActivityTuning.DefaultDailyTargetTicks;
            Assert.True(r.HasFamilyTimeLeft(1, now, target));

            r.AddFamilyTime(1, 3000, now);
            Assert.Equal(3000, r.FamilyTicksToday(1, now));
            Assert.True(r.HasFamilyTimeLeft(1, now, target));

            r.AddFamilyTime(1, 3000, now + 100);
            Assert.False(r.HasFamilyTimeLeft(1, now + 100, target));
            Assert.Equal(6000, r.FamilyTicksToday(1, now + 100));

            Assert.True(r.HasFamilyTimeLeft(1, now + Day, target));
            Assert.Equal(0, r.FamilyTicksToday(1, now + Day));

            // Adding on the new day starts the count again rather than carrying yesterday's total.
            r.AddFamilyTime(1, 1000, now + Day);
            Assert.Equal(1000, r.FamilyTicksToday(1, now + Day));
            Assert.True(r.HasFamilyTimeLeft(1, now + Day, target));
        }

        [Fact]
        public void The_budget_is_soft_it_only_gates_starting_and_ignores_nonsense()
        {
            var r = Household(out int now);
            r.AddFamilyTime(1, 0, now);
            r.AddFamilyTime(1, -500, now);
            Assert.Equal(0, r.FamilyTicksToday(1, now));
            r.AddFamilyTime(1, 100000, now);                       // one very long activity is allowed to finish
            Assert.False(r.HasFamilyTimeLeft(1, now, ActivityTuning.DefaultDailyTargetTicks));
            Assert.Equal(0, r.FamilyTicksToday(999, now));         // an unknown person has used nothing
        }

        [Fact]
        public void Old_and_departed_budget_records_are_pruned()
        {
            var r = Household(out int now);
            r.AddFamilyTime(1, 100, now);
            r.AddFamilyTime(9, 100, now);
            r.AddFamilyTime(555, 100, now);          // nobody we know
            r.Apply(new[] { T.P(1), T.P(2), T.Kid(3, new[] { 1, 2 }), T.P(9, presence: Presence.Away) }, now + 5 * Day);

            r.PruneTimeRecords(now + 5 * Day);

            Assert.DoesNotContain(r.TimeRecords, t => t.PersonId == 555);
            Assert.DoesNotContain(r.TimeRecords, t => t.PersonId == 9);
            Assert.True(r.TimeRecords.Count <= 3);
        }

        // ---------------------------------------------------------------- quality time

        [Fact]
        public void Time_together_strengthens_both_directions_and_eases_tension()
        {
            var r = Household(out int now);
            r.GetBond(1, 3).Tension = 30f;
            r.GetBond(3, 1).Tension = 30f;
            float a1 = r.GetBond(1, 3).Attachment, a2 = r.GetBond(3, 1).Attachment;

            r.RecordTogether(1, 3, 2 * Hour, now);

            Assert.True(r.GetBond(1, 3).Attachment > a1);
            Assert.True(r.GetBond(3, 1).Attachment > a2);
            Assert.True(r.GetBond(1, 3).Tension < 30f);
            Assert.True(r.GetBond(3, 1).Trust > 25f);
            Assert.Equal(now, r.GetBond(1, 3).LastQualityTick);
        }

        [Fact]
        public void More_time_together_gives_more_and_none_gives_nothing()
        {
            var shortTime = Household(out int now);
            shortTime.RecordTogether(1, 3, 1 * Hour, now);
            var longTime = Household(out _);
            longTime.RecordTogether(1, 3, 3 * Hour, now);
            var none = Household(out _);
            none.RecordTogether(1, 3, 0, now);

            Assert.True(longTime.GetBond(1, 3).Attachment > shortTime.GetBond(1, 3).Attachment);
            Assert.Equal(none.GetBond(1, 3).Attachment, Household(out _).GetBond(1, 3).Attachment, 3);
        }

        [Fact]
        public void Time_together_can_never_push_values_past_the_limits()
        {
            var r = Household(out int now);
            for (int i = 0; i < 200; i++) r.RecordTogether(1, 3, 10 * Hour, now + i);
            Assert.All(r.Bonds, b => Assert.InRange(b.Attachment, 0f, 100f));
            Assert.InRange(r.GetBond(1, 3).Tension, 0f, 100f);
        }

        // ---------------------------------------------------------------- who wants to be with whom

        [Fact]
        public void Someone_not_seen_for_a_long_time_is_wanted_more_than_someone_just_seen()
        {
            var r = Household(out int now);
            r.RecordTogether(1, 2, Hour, now);        // just spent time with the partner
            var ranked = r.RankCompanions(1, new[] { 2, 3 }, now + Hour);

            Assert.Equal(3, ranked[0].Id);            // the child, never spent time with on purpose, comes first
            Assert.True(ranked[0].Desire > ranked.First(c => c.Id == 2).Desire);
        }

        [Fact]
        public void With_everything_else_equal_the_one_not_seen_for_longest_is_wanted_most()
        {
            var r = new FamilyRegistry();
            int now = 2 * Day;
            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith"), T.Kid(3, new[] { 1 }), T.Kid(5, new[] { 1 })
            }, now);
            r.UpdateBonds((a, b) => 0, now, null);

            r.RecordTogether(1, 3, Hour, now);              // the first child has just had time with Mum; the second has not

            var ranked = r.RankCompanions(1, new[] { 3, 5 }, now + Hour);
            Assert.Equal(5, ranked[0].Id);
            Assert.True(ranked[0].Desire > ranked[1].Desire);
        }

        [Fact]
        public void An_ex_is_a_real_relationship_that_is_still_never_chosen()
        {
            var r = Household(out int now);
            Assert.Equal(BondKind.Ex, r.KindOf(r.GetPerson(1), r.GetPerson(4)));
            Assert.Empty(r.RankCompanions(1, new[] { 4 }, now));
        }

        [Fact]
        public void Close_ties_and_kind_of_relationship_shape_the_ranking()
        {
            var r = Household(out int now);
            var ranked = r.RankCompanions(1, new[] { 2, 3 }, now);
            Assert.Equal(2, ranked.Count);
            Assert.All(ranked, c => Assert.True(c.Desire >= ActivityTuning.MinDesire));

            r.GetBond(1, 3).Attachment = 95f;
            r.GetBond(1, 2).Attachment = 10f;
            Assert.Equal(3, r.RankCompanions(1, new[] { 2, 3 }, now)[0].Id);
        }

        [Fact]
        public void Tension_makes_someone_less_sought_after()
        {
            var r = Household(out int now);
            float calm = r.RankCompanions(1, new[] { 2 }, now)[0].Desire;
            r.GetBond(1, 2).Tension = 90f;
            float tense = r.RankCompanions(1, new[] { 2 }, now)[0].Desire;
            Assert.True(tense < calm);
        }

        [Fact]
        public void Former_partners_strangers_the_dead_and_self_are_never_chosen()
        {
            var r = Household(out int now);
            var ranked = r.RankCompanions(1, new[] { 1, 4, 9, 404 }, now);
            Assert.Empty(ranked);                      // self, an ex, a stranger, an unknown id

            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }, presence: Presence.Dead),
                T.Kid(3, new[] { 1, 2 })
            }, now + 1);
            Assert.DoesNotContain(r.RankCompanions(1, new[] { 2 }, now + 1), c => c.Id == 2);
        }

        [Fact]
        public void Ranking_works_without_any_bonds_yet_using_the_relationship_alone()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, 100000);

            Assert.Single(r.RankCompanions(1, new[] { 2 }, 100000));
        }

        // ---------------------------------------------------------------- bedtime stories

        [Fact]
        public void A_child_is_read_to_at_most_once_a_night()
        {
            var r = Household(out int now);
            Assert.True(r.StoryDue(3, now));

            r.RecordStory(3, now);

            Assert.False(r.StoryDue(3, now + 1000));
            Assert.False(r.StoryDue(3, now + ActivityTuning.StoryIntervalTicks - 1));
            Assert.True(r.StoryDue(3, now + ActivityTuning.StoryIntervalTicks));
            Assert.True(r.StoryDue(9, now));           // another child is unaffected
        }

        [Fact]
        public void The_parent_with_the_best_social_skill_reads_and_ties_go_to_the_lower_id()
        {
            var pick = FamilyRegistry.PickReader(new[]
            {
                new Reader { Id = 1, Social = 6f }, new Reader { Id = 2, Social = 14f }, new Reader { Id = 7, Social = 14f }
            });
            Assert.Equal(2, pick.Id);
            Assert.Null(FamilyRegistry.PickReader(new Reader[0]));
        }

        [Fact]
        public void A_story_with_a_book_teaches_more_than_one_without()
        {
            float withBook = FamilyRegistry.StoryXp(true, 0.15f, ActivityTuning.StoryTicks, 10f, 12f);
            float withoutBook = FamilyRegistry.StoryXp(false, 0f, ActivityTuning.StoryTicks, 10f, 12f);
            Assert.True(withBook > withoutBook);
            Assert.True(withoutBook > 0f);
        }

        [Fact]
        public void A_better_storyteller_teaches_more_either_way()
        {
            Assert.True(FamilyRegistry.StoryXp(true, 0.15f, 1800, 18f, 5f) > FamilyRegistry.StoryXp(true, 0.15f, 1800, 2f, 5f));
            Assert.True(FamilyRegistry.StoryXp(false, 0f, 1800, 18f, 5f) > FamilyRegistry.StoryXp(false, 0f, 1800, 2f, 5f));
        }

        [Fact]
        public void Without_a_book_a_more_skilled_reader_teaches_more()
        {
            Assert.True(FamilyRegistry.StoryXp(false, 0f, 1800, 10f, 18f) > FamilyRegistry.StoryXp(false, 0f, 1800, 10f, 2f));
        }

        [Fact]
        public void Story_experience_is_never_negative_and_stays_modest()
        {
            Assert.Equal(0f, FamilyRegistry.StoryXp(true, -5f, 1800, 10f, 10f));
            Assert.True(FamilyRegistry.StoryXp(false, 0f, 1800, -50f, -50f) > 0f);       // nonsense input still gives a sane answer
            Assert.True(FamilyRegistry.StoryXp(false, 0f, 1800, 1000f, 1000f) < 200f);   // and cannot run away
            Assert.True(FamilyRegistry.StoryXp(true, 0.2f, 1800, 20f, 20f) < 1000f);
        }

        // ---------------------------------------------------------------- eating together

        [Fact]
        public void Relatives_who_ate_in_the_same_room_at_about_the_same_time_count_as_dining_together()
        {
            var r = Household(out int now);
            var recent = new[] { new RecentMeal { PersonId = 2, Tick = now - 300, RoomKey = 7 }, new RecentMeal { PersonId = 3, Tick = now - 100, RoomKey = 7 } };

            var partners = r.SharedMealPartners(1, 7, now, recent);

            Assert.Equal(new[] { 3, 2 }, partners);        // the most recent first
        }

        [Fact]
        public void Strangers_other_rooms_late_arrivals_and_unknown_rooms_do_not_count()
        {
            var r = Household(out int now);
            var recent = new[]
            {
                new RecentMeal { PersonId = 9, Tick = now - 100, RoomKey = 7 },                                   // a stranger
                new RecentMeal { PersonId = 2, Tick = now - 100, RoomKey = 8 },                                   // another room
                new RecentMeal { PersonId = 3, Tick = now - ActivityTuning.SharedMealWindowTicks - 1, RoomKey = 7 } // too long ago
            };

            Assert.Empty(r.SharedMealPartners(1, 7, now, recent));
            Assert.Empty(r.SharedMealPartners(1, -1, now, new[] { new RecentMeal { PersonId = 2, Tick = now, RoomKey = -1 } }));
            Assert.Empty(r.SharedMealPartners(1, 7, now, new[] { new RecentMeal { PersonId = 1, Tick = now, RoomKey = 7 } }));   // yourself
        }

        [Fact]
        public void Someone_you_barely_have_a_bond_with_does_not_count_as_a_family_meal()
        {
            var r = Household(out int now);
            r.GetBond(1, 3).Attachment = 5f;
            Assert.Empty(r.SharedMealPartners(1, 7, now, new[] { new RecentMeal { PersonId = 3, Tick = now, RoomKey = 7 } }));
        }
    }
}
