using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class MarriageAllianceTests
    {
        private const int Day=ActivityTuning.TicksPerDay;
        private static FamilyRegistry World(out int a,out int b)
        {
            var r=new FamilyRegistry();r.Apply(new[]{T.P(1,last:"Smith"),T.P(2,last:"Jones")},Day);
            a=r.FamilyOf(1);b=r.FamilyOf(2);
            r.GetLineage(a).Situation=new HouseSituation{Continuity=10,LivingSpace=80,Sustenance=80};
            r.GetLineage(b).Situation=new HouseSituation{Continuity=10,LivingSpace=80,Sustenance=80};
            return r;
        }

        [Fact] public void House_interest_and_pawn_willingness_are_separate()
        {
            var r=World(out _,out _);var assessment=r.AssessMarriageAlliance(1,2,new MarriageCandidateContext{VanillaEligible=true,OpinionA=-80,OpinionB=35});
            Assert.True(assessment.WorthConsidering);Assert.True(assessment.Reasons.HasFlag(MarriageAllianceReason.Continuity));
            Assert.True(assessment.ObjectionsA.HasFlag(MarriageAllianceObjection.PawnUnwilling));Assert.False(assessment.ObjectionsB.HasFlag(MarriageAllianceObjection.PawnUnwilling));
        }

        [Fact] public void Vanilla_rejection_and_close_kin_are_hard_stops()
        {
            var r=World(out _,out _);Assert.True(r.AssessMarriageAlliance(1,2,new MarriageCandidateContext()).HardObjections.HasFlag(MarriageAllianceObjection.VanillaRejected));
            var kin=new FamilyRegistry();kin.Apply(new[]{T.P(10),T.P(11,parents:new[]{10})},Day);
            Assert.True(kin.AssessMarriageAlliance(10,11,new MarriageCandidateContext{VanillaEligible=true}).HardObjections.HasFlag(MarriageAllianceObjection.Kinship));
        }

        [Fact] public void Hunger_and_crowding_make_a_house_decline_expansion()
        {
            var r=World(out int a,out _);r.GetLineage(a).Situation.Sustenance=10;r.GetLineage(a).Situation.LivingSpace=20;
            var assessment=r.AssessMarriageAlliance(1,2,new MarriageCandidateContext{VanillaEligible=true});
            Assert.True(assessment.ObjectionsA.HasFlag(MarriageAllianceObjection.FoodInsecure));
            Assert.True(assessment.ObjectionsA.HasFlag(MarriageAllianceObjection.NoRoom));Assert.False(assessment.WorthConsidering);
        }

        [Fact] public void Opportunity_expires_without_creating_a_relationship()
        {
            var r=World(out _,out _);var assessment=r.AssessMarriageAlliance(1,2,new MarriageCandidateContext{VanillaEligible=true});
            var opportunity=r.RecordMarriageOpportunity(assessment,2*Day);Assert.NotNull(opportunity);Assert.Empty(r.GetPerson(1).PartnerIds());
            r.MaintainAgency(10*Day);Assert.Equal(MarriageAllianceState.Expired,opportunity.State);
        }

        [Fact] public void Only_an_actual_cross_house_marriage_authorizes_union()
        {
            var r=World(out int a,out int b);Assert.False(r.CanUniteThroughMarriage(a,b));
            r.Apply(new[]{T.P(1,last:"Smith",spouses:new[]{2}),T.P(2,last:"Jones",spouses:new[]{1})},2*Day);
            Assert.True(r.CanUniteThroughMarriage(a,b));
        }
    }
}
