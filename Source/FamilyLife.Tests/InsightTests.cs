using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class InsightTests
    {
        private const int Hour = BondTuning.TicksPerHour;
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry Family(out int now)
        {
            var r = new FamilyRegistry();
            now = 2 * Day;
            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }),
                T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }),
                T.Kid(4, new[] { 1, 2 })
            }, now);
            r.UpdateBonds((a, b) => 0, now, null);
            return r;
        }

        private static Lineage LineageOf(FamilyRegistry r) => r.GetLineage(r.GetPerson(1).PrimaryLineageId);

        // ---------------------------------------------------------------- reasons

        [Fact]
        public void The_same_cause_adds_up_and_only_a_few_causes_are_kept()
        {
            var bond = new Bond();
            bond.Note(BondCause.Story, 2f, 100);
            bond.Note(BondCause.Story, 3f, 200);
            Assert.Single(bond.Reasons);
            Assert.True(bond.Reasons[0].Delta > 4.5f);
            foreach (var c in new[] { BondCause.Meal, BondCause.Date, BondCause.Care, BondCause.Peace, BondCause.Gathering })
                bond.Note(c, 1f, 300);
            Assert.True(bond.Reasons.Count <= Bond.MaxReasons);
            Assert.Contains(bond.Reasons, r => r.Cause == BondCause.Story);       // the strongest cause survives the trimming
        }

        [Fact]
        public void Time_together_records_why_the_bond_changed()
        {
            var r = Family(out int now);
            r.GetBond(1, 3).LastOpinion = 40f;
            r.RecordTogether(1, 3, 2 * Hour, now, BondCause.Story);
            Assert.Equal(BondCause.Story, r.GetBond(1, 3).TopReason(true).Cause);
        }

        [Fact]
        public void Company_that_grates_is_recorded_as_friction_not_as_closeness()
        {
            var r = Family(out int now);
            r.GetBond(1, 3).LastOpinion = -50f;
            r.RecordTogether(1, 3, 2 * Hour, now, BondCause.Story);
            Assert.Equal(BondCause.Friction, r.GetBond(1, 3).TopReason(false).Cause);
            Assert.Null(r.GetBond(1, 3).TopReason(true));
        }

        // ---------------------------------------------------------------- milestones

        [Fact]
        public void The_first_look_at_a_bond_announces_nothing_even_if_it_is_already_close()
        {
            var r = Family(out int now);
            r.GetBond(1, 3).Attachment = 90f;
            r.GetBond(1, 3).Note(BondCause.Story, 8f, now);       // even with a reason on record
            Assert.Empty(r.UpdateBondMilestones(now));
        }

        [Fact]
        public void A_bond_that_becomes_close_for_a_reason_is_announced_once()
        {
            var r = Family(out int now);
            r.UpdateBondMilestones(now);
            var bond = r.GetBond(1, 3);
            bond.Attachment = 80f;
            bond.Note(BondCause.Story, 6f, now);
            var first = r.UpdateBondMilestones(now + Hour);
            Assert.Contains(first, e => e.Kind == FamilyEventKind.BondDeepened && e.A == 1 && e.B == 3);
            Assert.DoesNotContain(r.UpdateBondMilestones(now + 2 * Hour), e => e.A == 1 && e.B == 3);
        }

        [Fact]
        public void A_bond_that_was_always_low_is_not_announced_as_cooling()
        {
            var r = Family(out int now);
            r.GetBond(3, 4).Attachment = 60f;
            r.UpdateBondMilestones(now);
            r.GetBond(3, 4).Attachment = 10f;       // low, but nothing ever happened to make it so
            Assert.DoesNotContain(r.UpdateBondMilestones(now + Hour), e => e.Kind == FamilyEventKind.BondCooled && e.A == 3);
        }

        [Fact]
        public void A_bond_that_gets_close_with_no_known_cause_is_not_announced()
        {
            var r = Family(out int now);
            r.UpdateBondMilestones(now);
            r.GetBond(1, 3).Attachment = 85f;
            Assert.DoesNotContain(r.UpdateBondMilestones(now + Hour), e => e.A == 1 && e.B == 3);
        }

        [Fact]
        public void The_same_bond_is_not_announced_again_within_the_cooldown_but_is_afterwards()
        {
            var r = Family(out int now);
            r.UpdateBondMilestones(now);
            var bond = r.GetBond(1, 3);
            bond.Attachment = 80f; bond.Note(BondCause.Story, 6f, now);
            Assert.NotEmpty(r.UpdateBondMilestones(now + Hour));
            bond.Attachment = 50f;                                    // falls back out of "close"
            r.UpdateBondMilestones(now + 2 * Hour);
            bond.Attachment = 80f; bond.Note(BondCause.Story, 6f, now + 3 * Hour);
            Assert.DoesNotContain(r.UpdateBondMilestones(now + 3 * Hour), e => e.A == 1 && e.B == 3);
            Assert.Contains(r.UpdateBondMilestones(now + InsightTuning.MilestoneCooldownTicks + 4 * Hour), e => e.A == 1 && e.B == 3);
        }

        [Fact]
        public void A_bond_that_cools_after_a_falling_out_is_announced_and_a_burst_is_limited()
        {
            var r = Family(out int now);
            r.UpdateBondMilestones(now);
            foreach (var bond in r.Bonds.Where(b => b.From != b.To))
            {
                bond.Attachment = 10f;
                bond.Note(BondCause.Rift, -8f, now);
            }
            var events = r.UpdateBondMilestones(now + Hour);
            Assert.NotEmpty(events);
            Assert.True(events.Count <= InsightTuning.MaxMilestonesPerUpdate);
            Assert.All(events, e => Assert.Equal(FamilyEventKind.BondCooled, e.Kind));
        }

        // ---------------------------------------------------------------- culture shapes what a family does

        [Fact]
        public void A_learned_family_teaches_more_than_a_family_that_is_not()
        {
            var r = Family(out _);
            var lineage = LineageOf(r);
            lineage.Culture[(int)Dim.Scholarly] = 40f;
            float learned = r.TeachChance(lineage.Id);
            lineage.Culture[(int)Dim.Scholarly] = -30f;
            Assert.True(learned > r.TeachChance(lineage.Id));
        }

        [Fact]
        public void A_devoted_home_loving_family_goes_on_dates_more_than_an_independent_one()
        {
            var r = Family(out _);
            var lineage = LineageOf(r);
            lineage.Culture[(int)Dim.Devotion] = 35f; lineage.Culture[(int)Dim.Hearth] = 30f;
            float devoted = r.DateChance(lineage.Id);
            lineage.Culture[(int)Dim.Devotion] = -35f; lineage.Culture[(int)Dim.Hearth] = -30f;
            Assert.True(devoted > r.DateChance(lineage.Id));
        }

        [Fact]
        public void Family_time_appetite_follows_both_the_family_and_the_person()
        {
            var r = Family(out _);
            var lineage = LineageOf(r);
            var homebody = new float[DimInfo.Count]; homebody[(int)Dim.Devotion] = 1f; homebody[(int)Dim.Warmth] = 0.8f;
            var loner = new float[DimInfo.Count]; loner[(int)Dim.Devotion] = -1f; loner[(int)Dim.Warmth] = -0.8f;
            Assert.True(r.FamilyTimeAppetite(lineage.Id, homebody) > r.FamilyTimeAppetite(lineage.Id, loner));
            lineage.Culture[(int)Dim.Devotion] = 35f;
            float devotedFamily = r.FamilyTimeAppetite(lineage.Id, null);
            lineage.Culture[(int)Dim.Devotion] = -35f;
            Assert.True(devotedFamily > r.FamilyTimeAppetite(lineage.Id, null));
        }

        [Fact]
        public void Appetite_changes_who_is_sought_out()
        {
            var r = Family(out int now);
            var keen = r.RankCompanions(1, new[] { 3 }, now, 1.4f);
            var cool = r.RankCompanions(1, new[] { 3 }, now, 0.5f);
            Assert.True(keen.Count == 0 || cool.Count == 0 || keen[0].Desire > cool[0].Desire);
            Assert.True(keen.Count >= cool.Count);
        }

        [Fact]
        public void Opposed_characters_and_an_outlier_among_the_family_values_add_friction()
        {
            var r = Family(out _);
            var lineage = LineageOf(r);
            var warm = new float[DimInfo.Count]; warm[(int)Dim.Warmth] = 0.9f;
            var cold = new float[DimInfo.Count]; cold[(int)Dim.Warmth] = -0.9f;
            Assert.True(r.Friction(1, warm, cold) > r.Friction(1, warm, warm));
            Assert.Equal(0f, r.Friction(1, null, warm));

            var restless = new float[DimInfo.Count]; restless[(int)Dim.Rootedness] = -1f;
            float without = r.Friction(1, restless, restless);
            lineage.Culture[(int)Dim.Rootedness] = 35f;       // a family that prizes staying put
            Assert.True(r.Friction(1, restless, restless) > without);
        }

        [Fact]
        public void Friction_raises_the_tension_two_people_settle_at()
        {
            var r = Family(out int now);
            var warm = new float[DimInfo.Count]; warm[(int)Dim.Warmth] = 0.9f;
            var cold = new float[DimInfo.Count]; cold[(int)Dim.Warmth] = -0.9f;
            for (int h = 1; h <= 400; h++) r.UpdateBonds((a, b) => 0, now + h * Hour, null, id => id == 1 ? warm : cold);
            Assert.True(r.GetBond(1, 3).Tension > 3f);
        }
    }
}
