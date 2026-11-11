using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class SuccessionTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        private static PersonObservation[] Members(Presence head = Presence.Colony, int[] absent = null)
        {
            var list = new System.Collections.Generic.List<PersonObservation>
            {
                T.P(1, "Elder", presence: head)
            };
            foreach (int id in new[] { 3, 4, 5, 6, 7, 8 })
                list.Add(T.P(id, "Kid" + id, parents: new[] { 1 }, presence: absent != null && absent.Contains(id) ? Presence.Away : Presence.Colony));
            return list.ToArray();
        }

        private static FamilyRegistry Home(out int family, SuccessionCustom custom)
        {
            var r = new FamilyRegistry();
            r.Apply(Members(), 1000);
            family = r.FamilyOf(1);
            var house = r.GetLineage(family);
            house.CustomSet = true; house.Custom = custom;
            r.HeadOf(family);
            return r;
        }

        private static void HeadDies(FamilyRegistry r, int now) { r.Apply(Members(Presence.Dead), now); }

        private static void Run(FamilyRegistry r, int from, int days, LegitimacyContext ctx = null)
        {
            for (int d = 0; d <= days; d++) r.MaintainSuccession(from + d * Day / 2, ctx);
        }

        [Fact]
        public void A_clearly_best_supported_heir_is_settled_in_an_orderly_way()
        {
            var r = Home(out int fam, SuccessionCustom.Merit);
            r.GetPerson(3).TopSkillLevel = 15;
            r.BeginStewardship(3, fam, 1000);
            for (int i = 0; i < 12; i++) r.NoteStewardService(3, fam, StewardServiceKind.Aid, true, 1500 + i);   // not a steward yet: no-ops
            HeadDies(r, 5000);
            r.MaintainSuccession(5000);
            var c = r.SuccessionCases.Single();
            Assert.Equal(SuccessionStage.Resolved, c.Stage);
            Assert.Equal(3, c.ChosenId);
            Assert.Equal(3, r.HeadOf(fam));
            Assert.Equal(0, r.GetLineage(fam).VacantHeadId);
        }

        [Fact]
        public void A_large_family_does_not_automatically_produce_a_legitimate_ruler()
        {
            var r = Home(out int fam, SuccessionCustom.Merit);        // a house that values proven service: nobody has any yet
            HeadDies(r, 5000);
            Run(r, 5000, 12);
            var c = r.SuccessionCases.Single();
            Assert.True(c.Open);
            Assert.Equal(0, c.ChosenId);
            Assert.NotEqual(1, r.HeadOf(fam));
            Assert.NotEqual(0, r.HeadOf(fam));                       // someone keeps the house running, but no one is named rightful
            Assert.Equal(SuccessionStage.Disputed, c.Stage);
        }

        [Fact]
        public void Different_customs_can_name_different_heirs_from_the_same_people()
        {
            int Winner(SuccessionCustom custom)
            {
                var r = Home(out int fam, custom);
                r.GetPerson(8).TopSkillLevel = 15;
                HeadDies(r, 5000);
                Run(r, 5000, 6);
                return r.SuccessionCases.Single().ChosenId;
            }
            Assert.Equal(3, Winner(SuccessionCustom.Seniority));     // the eldest
            Assert.Equal(8, Winner(SuccessionCustom.Merit));         // the capable one
        }

        [Fact]
        public void Members_back_different_people_for_their_own_reasons()
        {
            var r = Home(out int fam, SuccessionCustom.Seniority);
            r.ChangeBond(6, 3, new BondChange(80f, 40f, 0f, 0f), 2000);
            r.ChangeBond(7, 4, new BondChange(80f, 40f, 0f, 0f), 2000);
            var candidates = r.SuccessionCandidates(fam, null, 1);
            var reason6 = r.SupportFor(6, candidates, fam, out int pick6);
            var reason7 = r.SupportFor(7, candidates, fam, out int pick7);
            var reason8 = r.SupportFor(8, candidates, fam, out int pick8);
            Assert.Equal(3, pick6); Assert.Equal(4, pick7);
            Assert.Equal(SupportReason.Affection, reason6); Assert.Equal(SupportReason.Affection, reason7);
            Assert.NotEqual(SupportReason.Affection, reason8);       // someone with no personal tie weighs the person's merits instead
            Assert.NotEqual(SupportReason.None, reason8);
        }

        [Fact]
        public void A_reluctant_successor_may_decline_and_the_next_is_considered()
        {
            var r = Home(out int fam, SuccessionCustom.Seniority);
            r.GetPerson(4).TopSkillLevel = 6;
            r.Agency.Add(new AgencyRecord { PersonId = 3, FamilyId = fam, Goal = PersonalGoal.Independence, StartedTick = 1, LastEvidenceTick = 4000 });
            HeadDies(r, 5000);
            Run(r, 5000, 6);
            var c = r.SuccessionCases.Single();
            Assert.Contains(3, c.Declined);
            Assert.NotEqual(3, c.ChosenId);
            Assert.NotEqual(0, c.ChosenId);
        }

        [Fact]
        public void An_absent_head_who_returns_in_time_simply_resumes()
        {
            var r = Home(out int fam, SuccessionCustom.Seniority);
            r.Apply(Members(Presence.Away), 5000);
            r.MaintainSuccession(5000);
            Assert.Empty(r.SuccessionCases);                         // no case yet: a short absence is not a vacancy
            r.Apply(Members(), 5000 + Day / 3);
            r.MaintainSuccession(5000 + Day / 3);
            Assert.Equal(1, r.HeadOf(fam));
            Assert.Empty(r.SuccessionCases);
        }

        [Fact]
        public void A_long_absence_opens_a_case_and_a_late_return_does_not_undo_a_settled_one()
        {
            var r = Home(out int fam, SuccessionCustom.Merit);
            r.GetPerson(3).TopSkillLevel = 15; r.GetPerson(3).TopSkill = "Social";
            r.BeginStewardship(3, fam, 100); r.GetLineage(fam).Stewards.Add(3);
            for (int i = 0; i < 12; i++) r.NoteStewardService(3, fam, StewardServiceKind.Aid, true, 200 + i);
            r.Apply(Members(Presence.Away), 5000);
            r.MaintainSuccession(5000);
            r.MaintainSuccession(5000 + 2 * Day);
            Assert.Single(r.SuccessionCases);
            Assert.Equal(3, r.HeadOf(fam));
            r.Apply(Members(), 5000 + 4 * Day);
            r.MaintainSuccession(5000 + 4 * Day);
            Assert.Equal(3, r.HeadOf(fam));
        }

        [Fact]
        public void Equal_rivals_who_cannot_compromise_split_the_house()
        {
            var r = Home(out int fam, SuccessionCustom.Seniority);
            r.GetPerson(4).TopSkillLevel = 15;
            r.ChangeBond(3, 4, new BondChange(0f, 0f, 60f, 0f), 2000); r.ChangeBond(4, 3, new BondChange(0f, 0f, 60f, 0f), 2000);
            foreach (int m in new[] { 5, 6 }) r.ChangeBond(m, 3, new BondChange(90f, 40f, 0f, 0f), 2000);
            foreach (int m in new[] { 7, 8 }) r.ChangeBond(m, 4, new BondChange(90f, 40f, 0f, 0f), 2000);
            var ctx = new LegitimacyContext { Ideology = id => id == 3 ? .2f : .8f };
            HeadDies(r, 5000);
            for (int i = 0; i < 24; i++) r.MaintainSuccession(5000 + i * Day / 2, ctx);

            var c = Assert.Single(r.SuccessionCases);
            Assert.Equal(SuccessionStage.Branched, c.Stage);
            Assert.NotEqual(0, c.BranchHouseId);
            Assert.NotEqual(c.ChosenId, c.RunnerUpId);
            Assert.Equal(c.ChosenId, r.HeadOf(fam));
            Assert.Equal(c.RunnerUpId, r.HeadOf(c.BranchHouseId));
            Assert.Equal(c.BranchHouseId, r.FamilyOf(c.RunnerUpId));
            Assert.Equal(fam, r.FamilyOf(c.ChosenId));                                // the winner keeps the house
            Assert.Equal(c.BranchHouseId, r.FamilyOf(5));                              // those who backed the runner-up went with them
            Assert.Equal(fam, r.FamilyOf(7));
        }

        [Fact]
        public void The_overlooked_closest_kin_remembers_being_passed_over()
        {
            var r = Home(out int fam, SuccessionCustom.Merit);
            r.GetPerson(4).TopSkillLevel = 15;
            HeadDies(r, 5000);
            r.MaintainSuccession(5000);
            Assert.Equal(4, r.SuccessionCases.Single().ChosenId);
            Assert.True((r.GetBond(3, 4)?.Tension ?? 0f) > 0f);
        }

        [Fact]
        public void A_private_designation_stays_private()
        {
            var r = Home(out int fam, SuccessionCustom.Designation);
            Assert.True(r.DesignateHeir(fam, 1, 4, new[] { 5 }, 2000));
            bool Promise(int observer) => r.AssessLegitimacy(4, fam, observer).Factors.HasFlag(LegitimacyFactor.Promise);
            Assert.True(Promise(0)); Assert.True(Promise(4)); Assert.True(Promise(5)); Assert.True(Promise(1));
            Assert.False(Promise(6));
            Assert.False(r.DesignateHeir(fam, 5, 4, null, 2000));      // only the head can name an heir
            Assert.False(r.DesignateHeir(fam, 1, 1, null, 2000));
        }

        [Fact]
        public void Witnessed_misconduct_counts_only_for_those_who_know()
        {
            var r = Home(out int fam, SuccessionCustom.Seniority);
            r.Apply(Members().Concat(new[] { T.P(50, "Victim", last: "Other") }).ToArray(), 2000);
            var incident = r.RecordIncident(IncidentKind.Theft, 3, 50, 0, 2000, 60f, new[] { 5 });
            Assert.NotNull(incident);
            float Score(int observer) => r.AssessLegitimacy(3, fam, observer).Score;
            Assert.True(Score(5) < Score(6));
            Assert.Equal(Score(5), Score(0));
        }

        [Fact]
        public void Succession_never_touches_stock_or_property_records()
        {
            var r = Home(out int fam, SuccessionCustom.Merit);
            r.GetPerson(3).TopSkillLevel = 15;
            r.Claims.Add(new StockClaim { ThingId = 77, OwnerFamily = fam, HolderFamily = fam, MapId = 0 });
            HeadDies(r, 5000);
            Run(r, 5000, 6);
            var claim = Assert.Single(r.Claims);
            Assert.Equal(77, claim.ThingId); Assert.Equal(fam, claim.OwnerFamily); Assert.Equal(fam, claim.HolderFamily);
        }

        [Fact]
        public void A_house_with_a_living_present_head_does_no_succession_work()
        {
            var r = Home(out int fam, SuccessionCustom.Seniority);
            for (int i = 0; i < 50; i++) r.MaintainSuccession(5000 + i * 100);
            Assert.Empty(r.SuccessionCases);
            Assert.Equal(1, r.HeadOf(fam));
        }

        [Fact]
        public void Lived_precedent_shapes_the_custom_over_time()
        {
            var r = Home(out int fam, SuccessionCustom.Seniority);
            r.GetLineage(fam).CustomSet = false;
            r.GetLineage(fam).Precedent = new System.Collections.Generic.List<int> { 0, 0, 3, 0, 0 };
            Assert.Equal(SuccessionCustom.Merit, r.CustomOf(fam));
        }
    }
}