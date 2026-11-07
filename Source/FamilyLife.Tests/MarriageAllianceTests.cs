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

        [Fact] public void Both_house_representatives_must_support_the_negotiation()
        {
            var r=World(out int a,out int b);var assessment=r.AssessMarriageAlliance(1,2,new MarriageCandidateContext{VanillaEligible=true});
            var opportunity=r.RecordMarriageOpportunity(assessment,2*Day);
            Assert.True(r.BeginMarriageNegotiation(opportunity.Id,r.HeadOf(a),r.HeadOf(b),2*Day));
            Assert.True(r.DecideMarriageNegotiation(opportunity.Id,r.HeadOf(a),true,2*Day));Assert.Equal(MarriageAllianceState.Negotiating,opportunity.State);
            Assert.True(r.DecideMarriageNegotiation(opportunity.Id,r.HeadOf(b),true,2*Day));Assert.Equal(MarriageAllianceState.Supported,opportunity.State);
        }

        [Fact] public void Each_pawn_decides_separately_and_refusal_is_final()
        {
            var r=World(out int a,out int b);var x=r.RecordMarriageOpportunity(r.AssessMarriageAlliance(1,2,new MarriageCandidateContext{VanillaEligible=true}),2*Day);
            r.BeginMarriageNegotiation(x.Id,r.HeadOf(a),r.HeadOf(b),2*Day);r.DecideMarriageNegotiation(x.Id,r.HeadOf(a),true,2*Day);r.DecideMarriageNegotiation(x.Id,r.HeadOf(b),true,2*Day);
            Assert.True(r.BeginMarriageConsent(x.Id,1,r.HeadOf(a),2*Day));Assert.True(r.DecideMarriageConsent(x.Id,1,true,2*Day));Assert.Equal(MarriageAllianceState.Supported,x.State);
            Assert.True(r.BeginMarriageConsent(x.Id,2,r.HeadOf(b),2*Day));Assert.True(r.DecideMarriageConsent(x.Id,2,false,2*Day));Assert.Equal(MarriageAllianceState.Withdrawn,x.State);
            Assert.Empty(r.GetPerson(1).PartnerIds());Assert.Empty(r.GetPerson(2).PartnerIds());
        }

        [Fact] public void Mutual_consent_still_does_not_create_romance()
        {
            var r=World(out int a,out int b);var x=r.RecordMarriageOpportunity(r.AssessMarriageAlliance(1,2,new MarriageCandidateContext{VanillaEligible=true}),2*Day);
            r.BeginMarriageNegotiation(x.Id,r.HeadOf(a),r.HeadOf(b),2*Day);r.DecideMarriageNegotiation(x.Id,r.HeadOf(a),true,2*Day);r.DecideMarriageNegotiation(x.Id,r.HeadOf(b),true,2*Day);
            r.BeginMarriageConsent(x.Id,1,r.HeadOf(a),2*Day);r.DecideMarriageConsent(x.Id,1,true,2*Day);r.BeginMarriageConsent(x.Id,2,r.HeadOf(b),2*Day);r.DecideMarriageConsent(x.Id,2,true,2*Day);
            Assert.Equal(MarriageAllianceState.Consented,x.State);Assert.True(r.HasConsentedMatch(1,2));Assert.Empty(r.GetPerson(1).PartnerIds());Assert.Empty(r.GetPerson(2).PartnerIds());
        }

        [Fact] public void Changed_relationship_state_invalidates_consent_before_it_is_requested()
        {
            var r=World(out int a,out int b);var x=r.RecordMarriageOpportunity(r.AssessMarriageAlliance(1,2,new MarriageCandidateContext{VanillaEligible=true}),2*Day);
            r.BeginMarriageNegotiation(x.Id,r.HeadOf(a),r.HeadOf(b),2*Day);r.DecideMarriageNegotiation(x.Id,r.HeadOf(a),true,2*Day);r.DecideMarriageNegotiation(x.Id,r.HeadOf(b),true,2*Day);
            r.GetPerson(1).LoverIds.Add(99);Assert.False(r.BeginMarriageConsent(x.Id,1,r.HeadOf(a),2*Day));Assert.Equal(MarriageAllianceState.Expired,x.State);
        }

        [Fact] public void Consent_is_private_until_the_pawn_is_approached()
        {
            var r=new FamilyRegistry();r.Apply(new[]{T.P(1,last:"Smith"),T.P(3,last:"Smith",parents:new[]{1}),T.P(2,last:"Jones"),T.P(4,last:"Jones",parents:new[]{2})},Day);
            int a=r.FamilyOf(3),b=r.FamilyOf(4);r.GetLineage(a).Situation=new HouseSituation{Continuity=5,LivingSpace=80,Sustenance=80};r.GetLineage(b).Situation=new HouseSituation{Continuity=5,LivingSpace=80,Sustenance=80};
            var x=r.RecordMarriageOpportunity(r.AssessMarriageAlliance(3,4,new MarriageCandidateContext{VanillaEligible=true}),2*Day);
            Assert.True(r.KnowsMarriageAlliance(r.HeadOf(a),x));Assert.False(r.KnowsMarriageAlliance(3,x));
            r.BeginMarriageNegotiation(x.Id,r.HeadOf(a),r.HeadOf(b),2*Day);r.DecideMarriageNegotiation(x.Id,r.HeadOf(a),true,2*Day);r.DecideMarriageNegotiation(x.Id,r.HeadOf(b),true,2*Day);
            Assert.True(r.BeginMarriageConsent(x.Id,3,r.HeadOf(a),2*Day));Assert.True(r.KnowsMarriageAlliance(3,x));Assert.False(r.KnowsMarriageAlliance(4,x));
        }

        [Fact] public void Actual_marriage_opens_but_does_not_automatically_complete_a_house_union()
        {
            var r=World(out int a,out int b);var x=r.RecordMarriageOpportunity(r.AssessMarriageAlliance(1,2,new MarriageCandidateContext{VanillaEligible=true}),2*Day);
            r.BeginMarriageNegotiation(x.Id,r.HeadOf(a),r.HeadOf(b),2*Day);r.DecideMarriageNegotiation(x.Id,r.HeadOf(a),true,2*Day);r.DecideMarriageNegotiation(x.Id,r.HeadOf(b),true,2*Day);
            r.BeginMarriageConsent(x.Id,1,r.HeadOf(a),2*Day);r.DecideMarriageConsent(x.Id,1,true,2*Day);r.BeginMarriageConsent(x.Id,2,r.HeadOf(b),2*Day);r.DecideMarriageConsent(x.Id,2,true,2*Day);
            r.Apply(new[]{T.P(1,last:"Smith",spouses:new[]{2}),T.P(2,last:"Jones",spouses:new[]{1})},3*Day);r.MaintainAgency(3*Day);
            Assert.Equal(MarriageAllianceState.Fulfilled,x.State);Assert.Equal(HouseUnionState.Eligible,x.UnionState);Assert.True(r.IsActiveHouse(a));Assert.True(r.IsActiveHouse(b));
        }

        [Fact] public void Union_requires_both_representatives_and_preserves_the_chosen_surviving_house()
        {
            var r=World(out int a,out int b);var x=r.RecordMarriageOpportunity(r.AssessMarriageAlliance(1,2,new MarriageCandidateContext{VanillaEligible=true}),2*Day);
            r.Apply(new[]{T.P(1,last:"Smith",spouses:new[]{2}),T.P(2,last:"Jones",spouses:new[]{1})},3*Day);r.MaintainAgency(3*Day);r.GetLineage(b).Standing=40;
            int target=r.PreferredMarriageUnionTarget(x);Assert.Equal(b,target);Assert.True(r.BeginMarriageUnion(x.Id,r.HeadOf(a),r.HeadOf(b),target,3*Day));
            Assert.True(r.DecideMarriageUnion(x.Id,r.HeadOf(a),true,3*Day));Assert.True(r.IsActiveHouse(a));
            Assert.True(r.DecideMarriageUnion(x.Id,r.HeadOf(b),true,3*Day));Assert.Equal(HouseUnionState.Completed,x.UnionState);Assert.Equal(b,r.ActiveHouseId(a));
        }

        [Fact] public void Union_refusal_keeps_both_married_houses_active()
        {
            var r=World(out int a,out int b);var x=r.RecordMarriageOpportunity(r.AssessMarriageAlliance(1,2,new MarriageCandidateContext{VanillaEligible=true}),2*Day);
            r.Apply(new[]{T.P(1,last:"Smith",spouses:new[]{2}),T.P(2,last:"Jones",spouses:new[]{1})},3*Day);r.MaintainAgency(3*Day);
            Assert.True(r.BeginMarriageUnion(x.Id,r.HeadOf(a),r.HeadOf(b),r.PreferredMarriageUnionTarget(x),3*Day));Assert.True(r.DecideMarriageUnion(x.Id,r.HeadOf(a),false,3*Day));
            Assert.Equal(HouseUnionState.Rejected,x.UnionState);Assert.True(r.IsActiveHouse(a));Assert.True(r.IsActiveHouse(b));
        }
    }
}
