using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class HouseAmbitionTests
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private static FamilyRegistry World(out Lineage house)
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, Day);
            house = r.GetLineage(r.FamilyOf(1));
            return r;
        }

        private static HouseSituation Stable(float value = 70f) => new HouseSituation
        {
            Members = 2, Adults = 2, EligibleSingles = 1, SuitableBeds = 2, SpareBeds = 1,
            Sustenance = value, LivingSpace = value, Safety = value, Continuity = value, Capability = value,
            Cohesion = value, Influence = value, Independence = value, Legacy = value
        };

        [Fact]
        public void Food_crisis_becomes_the_primary_ambition()
        {
            var r = World(out var house); var s = Stable(); s.Sustenance = 8;
            var goal = r.UpdateHouseAmbition(house.Id, s, 10 * Day);
            Assert.Equal(HouseAmbitionKind.SecureProvisions, goal.Primary);
            Assert.Equal(HouseNeed.Sustenance, goal.PrimaryReason);
        }

        [Fact]
        public void Crowding_becomes_a_family_quarters_ambition()
        {
            var r = World(out var house); var s = Stable(); s.LivingSpace = 10; s.SuitableBeds = 0; s.SpareBeds = 0;
            Assert.Equal(HouseAmbitionKind.EstablishFamilyQuarters, r.UpdateHouseAmbition(house.Id, s, 10 * Day).Primary);
        }

        [Fact]
        public void Threatened_house_seeks_a_real_cooperative_protector()
        {
            var r = World(out var house); var s = Stable();
            s.Safety = 20; s.ThreateningFamilyId = 9; s.CooperativeFamilyId = 7;
            var goal = r.UpdateHouseAmbition(house.Id, s, 10 * Day);
            Assert.Equal(HouseAmbitionKind.SeekProtection, goal.Primary);
            Assert.Equal(7, goal.TargetFamilyId);
        }

        [Fact]
        public void Prosperous_house_with_an_eligible_single_can_expand_its_legacy()
        {
            var r = World(out var house); var s = Stable(82); s.Influence = 90; s.Continuity = 85; s.Capability = 85;
            Assert.Equal(HouseAmbitionKind.ExpandLegacy, r.UpdateHouseAmbition(house.Id, s, 10 * Day).Primary);
        }

        [Fact]
        public void Ordinary_changes_wait_for_the_three_day_lock()
        {
            var r = World(out var house); var food = Stable(); food.Sustenance = 25;
            Assert.Equal(HouseAmbitionKind.SecureProvisions, r.UpdateHouseAmbition(house.Id, food, 10 * Day).Primary);
            var rooms = Stable(); rooms.LivingSpace = 5;
            var held = r.UpdateHouseAmbition(house.Id, rooms, 11 * Day);
            Assert.Equal(HouseAmbitionKind.SecureProvisions, held.Primary);
            Assert.Equal(HouseAmbitionKind.EstablishFamilyQuarters, held.Secondary);
            Assert.InRange(held.Urgency, 29f, 31f);
            Assert.Equal(HouseAmbitionKind.EstablishFamilyQuarters, r.UpdateHouseAmbition(house.Id, rooms, 14 * Day).Primary);
        }

        [Fact]
        public void Emergency_safety_can_interrupt_a_locked_goal()
        {
            var r = World(out var house); var rooms = Stable(); rooms.LivingSpace = 20;
            Assert.Equal(HouseAmbitionKind.EstablishFamilyQuarters, r.UpdateHouseAmbition(house.Id, rooms, 10 * Day).Primary);
            var danger = Stable(); danger.Safety = 5;
            Assert.Equal(HouseAmbitionKind.ImproveSafety, r.UpdateHouseAmbition(house.Id, danger, 11 * Day).Primary);
        }

        [Fact]
        public void Stewardship_is_an_office_with_a_service_record()
        {
            var r = World(out var house);
            Assert.True(r.DelegateHouse(r.HeadOf(house.Id), 2, house.Id, true, 5 * Day));
            r.NoteStewardService(2, house.Id, StewardServiceKind.Ceremony, true, 6 * Day);
            var record = r.StewardshipOf(2, house.Id, true);
            Assert.NotNull(record); Assert.Equal(1, record.CeremoniesOrganized);
            Assert.True(r.DelegateHouse(r.HeadOf(house.Id), 2, house.Id, false, 7 * Day));
            Assert.False(record.Active);
        }
    }
}
