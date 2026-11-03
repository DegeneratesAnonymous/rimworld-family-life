using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class ConflictTests
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private const int Hour = BondTuning.TicksPerHour;

        private static FamilyRegistry Home(out int now, int kids = 2)
        {
            var r = new FamilyRegistry();
            now = 5 * Day;
            var obs = new List<PersonObservation> { T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }) };
            for (int i = 0; i < kids; i++) obs.Add(T.Kid(10 + i, new[] { 1, 2 }));
            r.Apply(obs, now);
            r.UpdateBonds((a, b) => 0, now, null);
            return r;
        }

        // ---------------------------------------------------------------- pure rules

        [Fact]
        public void Couple_stages_follow_the_score_and_get_worse_as_it_falls()
        {
            Assert.Equal(CoupleStage.Steady, ConflictTuning.StageOf(80f));
            Assert.Equal(CoupleStage.Strained, ConflictTuning.StageOf(45f));
            Assert.Equal(CoupleStage.Distant, ConflictTuning.StageOf(30f));
            Assert.Equal(CoupleStage.Crisis, ConflictTuning.StageOf(5f));
        }

        [Fact]
        public void Breakups_become_likelier_as_the_relationship_worsens_and_strict_mode_allows_only_a_crisis()
        {
            float steady = ConflictTuning.BreakupMultiplier(CoupleStage.Steady, false);
            float strained = ConflictTuning.BreakupMultiplier(CoupleStage.Strained, false);
            float distant = ConflictTuning.BreakupMultiplier(CoupleStage.Distant, false);
            float crisis = ConflictTuning.BreakupMultiplier(CoupleStage.Crisis, false);
            Assert.True(steady < strained && strained < distant && distant < crisis);

            Assert.Equal(0f, ConflictTuning.BreakupMultiplier(CoupleStage.Steady, true));
            Assert.Equal(0f, ConflictTuning.BreakupMultiplier(CoupleStage.Distant, true));
            Assert.Equal(crisis, ConflictTuning.BreakupMultiplier(CoupleStage.Crisis, true));
        }

        [Fact]
        public void An_affair_needs_the_setting_a_drifting_couple_a_familiar_other_and_a_nature_that_allows_it()
        {
            Assert.True(ConflictTuning.AffairPossible(true, CoupleStage.Distant, 0f, 40f));
            Assert.False(ConflictTuning.AffairPossible(false, CoupleStage.Crisis, 0f, 90f));       // setting off
            Assert.False(ConflictTuning.AffairPossible(true, CoupleStage.Steady, 0f, 90f));        // happy couple
            Assert.False(ConflictTuning.AffairPossible(true, CoupleStage.Strained, 0f, 90f));      // not yet drifting
            Assert.False(ConflictTuning.AffairPossible(true, CoupleStage.Crisis, 0f, 5f));         // a stranger
            Assert.False(ConflictTuning.AffairPossible(true, CoupleStage.Crisis, 2f, 90f));        // devoted
        }

        [Fact]
        public void Mentoring_needs_a_real_gap_and_grows_with_the_gap_the_teachers_social_skill_and_familiarity()
        {
            Assert.Equal(0f, ConflictTuning.MentorXp(10, 9, 10f, 50f, 2000));
            Assert.Equal(0f, ConflictTuning.MentorXp(5, 12, 10f, 50f, 2000));
            Assert.Equal(0f, ConflictTuning.MentorXp(15, 3, 10f, 50f, 0));
            float small = ConflictTuning.MentorXp(8, 4, 10f, 50f, 2200);
            Assert.True(small > 0f);
            Assert.True(ConflictTuning.MentorXp(14, 4, 10f, 50f, 2200) > small);
            Assert.True(ConflictTuning.MentorXp(8, 4, 18f, 50f, 2200) > small);
            Assert.True(ConflictTuning.MentorXp(8, 4, 10f, 90f, 2200) > small);
            Assert.True(ConflictTuning.MentorXp(20, 0, 20f, 100f, 2200) < 600f);       // a session never makes a master
        }

        [Fact]
        public void The_chance_of_making_peace_rises_with_trust_and_a_forgiving_nature_and_is_always_between_limits()
        {
            Assert.True(ConflictTuning.ReconcileChance(80f, 80f, 0f) > ConflictTuning.ReconcileChance(10f, 10f, 0f));
            Assert.True(ConflictTuning.ReconcileChance(40f, 40f, 2f) > ConflictTuning.ReconcileChance(40f, 40f, -2f));
            Assert.InRange(ConflictTuning.ReconcileChance(-500f, -500f, -50f), 0.1f, 0.9f);
            Assert.InRange(ConflictTuning.ReconcileChance(500f, 500f, 50f), 0.1f, 0.9f);
        }

        // ---------------------------------------------------------------- couple health

        [Fact]
        public void A_warm_couple_scores_higher_than_a_cold_one_and_an_open_rift_lowers_the_score()
        {
            var warm = Home(out int now);
            foreach (var b in warm.Bonds.Where(b => (b.From == 1 && b.To == 2) || (b.From == 2 && b.To == 1))) { b.Attachment = 85f; b.Trust = 80f; b.Tension = 0f; b.LastQualityTick = now; }
            var cold = Home(out _);
            foreach (var b in cold.Bonds.Where(b => (b.From == 1 && b.To == 2) || (b.From == 2 && b.To == 1))) { b.Attachment = 15f; b.Trust = 15f; b.Tension = 60f; }

            Assert.True(warm.CoupleScore(1, 2, now) > cold.CoupleScore(1, 2, now));
            Assert.Equal(CoupleStage.Steady, warm.CoupleStageOf(1, 2, now));
            Assert.Equal(CoupleStage.Crisis, cold.CoupleStageOf(1, 2, now));

            float before = warm.CoupleScore(1, 2, now);
            warm.Rifts.Add(new Rift { Id = 1, A = 1, B = 2, Cause = RiftCause.Tension });
            Assert.True(warm.CoupleScore(1, 2, now) < before);
        }

        [Fact]
        public void A_couple_who_have_not_spent_time_together_for_a_long_while_drift()
        {
            var r = Home(out int now);
            foreach (var b in r.Bonds.Where(b => (b.From == 1 && b.To == 2) || (b.From == 2 && b.To == 1))) { b.LastQualityTick = now; }
            float recent = r.CoupleScore(1, 2, now + Hour);
            float months = r.CoupleScore(1, 2, now + 40 * Day);
            Assert.True(months < recent);
        }

        [Fact]
        public void A_couple_with_no_bonds_yet_is_simply_unknown_not_in_trouble()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, 100);
            Assert.Equal(50f, r.CoupleScore(1, 2, 200));
        }

        // ---------------------------------------------------------------- rifts

        [Fact]
        public void Ill_feeling_that_lasts_a_day_opens_a_rift_but_a_bad_hour_does_not()
        {
            var r = Home(out int now);
            r.GetBond(1, 2).Tension = 70f;

            Assert.Empty(r.UpdateRifts(now, true, null));
            Assert.Empty(r.UpdateRifts(now + Day - 100, true, null));
            Assert.Empty(r.Rifts);

            var events = r.UpdateRifts(now + Day + 10, true, null);
            Assert.Single(events, e => e.Kind == FamilyEventKind.RiftOpened);
            Assert.Equal(RiftCause.Tension, r.Rifts.Single().Cause);
        }

        [Fact]
        public void A_flash_of_anger_that_passes_never_becomes_a_rift()
        {
            var r = Home(out int now);
            r.GetBond(1, 2).Tension = 70f;
            r.UpdateRifts(now, true, null);
            r.GetBond(1, 2).Tension = 10f;
            r.UpdateRifts(now + Day / 2, true, null);
            r.GetBond(1, 2).Tension = 70f;
            r.UpdateRifts(now + Day / 2 + 100, true, null);

            Assert.Empty(r.UpdateRifts(now + Day + 100, true, null));      // the clock restarted when it calmed
        }

        [Fact]
        public void There_is_never_more_than_one_open_rift_per_pair()
        {
            var r = Home(out int now);
            r.GetBond(1, 2).Tension = 80f;
            r.GetBond(2, 1).Tension = 80f;
            r.UpdateRifts(now, true, null);                                   // starts the clock
            for (int i = 0; i < 10; i++) r.UpdateRifts(now + Day + i * Hour, true, null);

            Assert.Single(r.Rifts);
        }

        [Fact]
        public void A_breakup_opens_a_rift_with_that_cause()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, lovers: new[] { 2 }), T.P(2, lovers: new[] { 1 }) }, 5 * Day);
            r.UpdateBonds((a, b) => 0, 5 * Day, null);
            var ended = r.Apply(new[] { T.P(1, exes: new[] { 2 }), T.P(2, exes: new[] { 1 }) }, 6 * Day);
            r.UpdateBonds((a, b) => 0, 6 * Day, ended);

            r.UpdateRifts(6 * Day, true, ended);

            Assert.Equal(RiftCause.Breakup, r.Rifts.Single().Cause);
            Assert.Empty(new FamilyRegistry().UpdateRifts(1, true, null));
        }

        [Fact]
        public void A_young_rift_keeps_pulling_tension_back_up_so_it_cannot_just_be_waited_out()
        {
            var r = Home(out int now);
            var rift = OpenOne(r, now);
            r.GetBond(1, 2).Tension = 0f; r.GetBond(2, 1).Tension = 0f;

            for (int i = 1; i <= 30; i++) r.UpdateRifts(rift.StartTick + i * Hour, true, null);       // hourly sweeps

            Assert.True(r.GetBond(1, 2).Tension >= ConflictTuning.RiftCalmBelow);
            Assert.True(rift.IsOpen);
        }

        [Fact]
        public void An_old_rift_no_longer_holds_tension_up_and_closes_after_two_calm_days()
        {
            var r = Home(out int now);
            var rift = OpenOne(r, now);
            rift.StartTick = now - ConflictTuning.FloorTicks - Day;               // it has been open a long time
            r.GetBond(1, 2).Tension = 0f; r.GetBond(2, 1).Tension = 0f;

            Assert.Empty(r.UpdateRifts(now + 10, true, null).Where(e => e.Kind == FamilyEventKind.RiftResolved));
            Assert.Equal(0f, r.GetBond(1, 2).Tension, 3);                      // nothing pulled it back up
            Assert.True(rift.IsOpen);

            var events = r.UpdateRifts(now + 10 + ConflictTuning.CalmTicks + 100, true, null);
            Assert.False(rift.IsOpen);
            Assert.Single(events, e => e.Kind == FamilyEventKind.RiftResolved);
        }

        [Fact]
        public void A_rift_with_someone_who_has_left_closes_quietly()
        {
            var r = Home(out int now);
            r.GetBond(1, 2).Tension = 80f;
            r.UpdateRifts(now, true, null);
            r.UpdateRifts(now + Day + 10, true, null);
            Assert.Single(r.Rifts);

            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }, presence: Presence.Away), T.Kid(10, new[] { 1, 2 }), T.Kid(11, new[] { 1, 2 }) }, now + Day + 20);
            var events = r.UpdateRifts(now + Day + 30, true, null);

            Assert.False(r.Rifts.Single().IsOpen);
            Assert.Empty(events);
        }

        [Fact]
        public void Closed_rifts_are_kept_as_history_but_only_so_many()
        {
            var r = Home(out int now);
            for (int i = 0; i < ConflictTuning.MaxClosedRifts + 20; i++)
                r.Rifts.Add(new Rift { Id = 100 + i, A = 1, B = 2, ResolvedTick = 10 + i });
            r.UpdateRifts(now, true, null);
            Assert.Equal(ConflictTuning.MaxClosedRifts, r.Rifts.Count(x => !x.IsOpen));
            Assert.Equal(10 + 20, r.ClosedRifts().Last().ResolvedTick);       // the oldest were dropped
        }

        // ---------------------------------------------------------------- neglect and rivalry

        private static void Favour(FamilyRegistry r, float favoured, float neglected)
        {
            r.GetBond(1, 10).QualityRecent = favoured;
            r.GetBond(1, 11).QualityRecent = neglected;
        }

        [Fact]
        public void A_parent_who_gives_one_child_far_more_time_has_a_neglect_rift_with_the_other_child()
        {
            var r = Home(out int now);
            Favour(r, 10f, 1f);

            r.UpdateRifts(now, true, null);

            var rift = r.Rifts.Single();
            Assert.Equal(RiftCause.Neglect, rift.Cause);
            Assert.Equal(1, rift.A);
            Assert.Equal(11, rift.B);
        }

        [Fact]
        public void Even_attention_or_a_small_difference_or_little_attention_overall_causes_no_neglect()
        {
            var even = Home(out int now); Favour(even, 10f, 8f); even.UpdateRifts(now, true, null);
            var little = Home(out _); Favour(little, 2f, 0f); little.UpdateRifts(now, true, null);
            var onlyChild = Home(out _, kids: 1); onlyChild.GetBond(1, 10).QualityRecent = 20f; onlyChild.UpdateRifts(now, true, null);

            Assert.Empty(even.Rifts);
            Assert.Empty(little.Rifts);
            Assert.Empty(onlyChild.Rifts);
        }

        [Fact]
        public void Unanswered_neglect_grows_into_sibling_rivalry_only_when_that_is_switched_on()
        {
            var on = Home(out int now); Favour(on, 10f, 1f);
            on.UpdateRifts(now, true, null);
            on.UpdateRifts(now + ConflictTuning.RivalryAfterTicks + 10, true, null);
            Assert.Contains(on.Rifts, x => x.Cause == RiftCause.Rivalry && x.A == 11 && x.B == 10);

            var off = Home(out _); Favour(off, 10f, 1f);
            off.UpdateRifts(now, false, null);
            off.UpdateRifts(now + ConflictTuning.RivalryAfterTicks + 10, false, null);
            Assert.DoesNotContain(off.Rifts, x => x.Cause == RiftCause.Rivalry);
        }

        [Fact]
        public void Spending_time_with_the_neglected_child_ends_the_neglect_and_the_rivalry()
        {
            var r = Home(out int now); Favour(r, 10f, 1f);
            r.UpdateRifts(now, true, null);
            r.UpdateRifts(now + ConflictTuning.RivalryAfterTicks + 10, true, null);
            Assert.Equal(2, r.Rifts.Count(x => x.IsOpen));

            r.GetBond(1, 11).QualityRecent = 8f;
            var events = r.UpdateRifts(now + ConflictTuning.RivalryAfterTicks + 20, true, null);

            Assert.Empty(r.Rifts.Where(x => x.IsOpen));
            Assert.Equal(2, events.Count(e => e.Kind == FamilyEventKind.RiftResolved));
        }

        [Fact]
        public void Attention_fades_with_time_so_neglect_can_return()
        {
            var r = Home(out int now);
            r.GetBond(1, 10).QualityRecent = 10f;
            // A long gap counts as at most two days, so fading is seen over many ordinary updates.
            for (int d = 1; d <= 30; d++) r.UpdateBonds((a, b) => 0, now + d * Day, null);
            Assert.True(r.GetBond(1, 10).QualityRecent < 1f);
        }

        // ---------------------------------------------------------------- making peace

        private static Rift OpenOne(FamilyRegistry r, int now)
        {
            r.GetBond(1, 2).Tension = 80f; r.GetBond(2, 1).Tension = 80f;
            r.UpdateRifts(now, true, null);
            r.UpdateRifts(now + Day + 10, true, null);
            return r.Rifts.Single();
        }

        [Fact]
        public void An_accepted_apology_closes_the_rift_eases_tension_builds_trust_and_is_remembered()
        {
            var r = Home(out int now);
            var rift = OpenOne(r, now);
            float trust = r.GetBond(1, 2).Trust;

            var e = r.TryReconcile(rift.Id, true, now + 2 * Day);

            Assert.NotNull(e);
            Assert.False(rift.IsOpen);
            Assert.Equal(80f - ConflictTuning.ReconcileTensionRelief, r.GetBond(1, 2).Tension, 1);
            Assert.True(r.GetBond(1, 2).Trust > trust);
            Assert.Contains(r.Memories, m => m.Kind == MemoryKind.Reconciliation) ;
        }

        [Fact]
        public void A_refused_apology_leaves_the_rift_open_a_little_worse_and_waits_before_another_try()
        {
            var r = Home(out int now);
            var rift = OpenOne(r, now);
            float tension = r.GetBond(1, 2).Tension;

            Assert.Null(r.TryReconcile(rift.Id, false, now + 2 * Day));

            Assert.True(rift.IsOpen);
            Assert.True(r.GetBond(1, 2).Tension > tension);
            Assert.False(r.MayTryReconcile(rift.Id, now + 2 * Day + 100));
            Assert.True(r.MayTryReconcile(rift.Id, now + 3 * Day + 100));
        }

        [Fact]
        public void Making_peace_with_a_closed_or_unknown_rift_does_nothing()
        {
            var r = Home(out int now);
            Assert.Null(r.TryReconcile(999, true, now));
            var rift = OpenOne(r, now);
            r.TryReconcile(rift.Id, true, now + Day);
            Assert.Null(r.TryReconcile(rift.Id, true, now + 2 * Day));
            Assert.False(r.MayTryReconcile(rift.Id, now + 5 * Day));
        }

        [Fact]
        public void Rifts_for_a_large_colony_update_quickly()
        {
            var obs = new List<PersonObservation>();
            int id = 1;
            for (int f = 0; f < 80; f++)
            {
                int a = id++, b = id++;
                obs.Add(T.P(a, "A" + f, "Fam" + f, spouses: new[] { b }));
                obs.Add(T.P(b, "B" + f, "Fam" + f, spouses: new[] { a }));
                for (int k = 0; k < 3; k++) obs.Add(T.Kid(id++, new[] { a, b }, "Fam" + f));
            }
            var r = new FamilyRegistry();
            r.Apply(obs, 5 * Day);
            r.UpdateBonds((x, y) => 0, 5 * Day, null);
            var clock = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < 30; i++) { r.UpdateBonds((x, y) => 0, 5 * Day + i * Hour, null); r.UpdateRifts(5 * Day + i * Hour, true, null); r.UpdateTraditions(5 * Day + i * Hour); r.UpdateCulture(p => new float[DimInfo.Count], 5 * Day + i * Hour); }
            clock.Stop();
            Assert.True(clock.ElapsedMilliseconds < 8000, "30 full sweeps of 400 people took " + clock.ElapsedMilliseconds + " ms");
        }
    }
}
