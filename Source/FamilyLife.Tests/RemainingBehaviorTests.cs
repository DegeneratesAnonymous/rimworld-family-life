using System;
using System.Linq;
using FamilyLife.Domain;
using Xunit;
namespace FamilyLife.Tests
{
    public class RemainingBehaviorTests
    {
        const int Day=ActivityTuning.TicksPerDay;
        static FamilyRegistry World()
        {var r=new FamilyRegistry();r.Apply(new[]{T.P(1,spouses:new[]{2}),T.P(2,spouses:new[]{1}),T.Kid(3,new[]{1,2}),T.Kid(4,new[]{1,2}),T.P(11,last:"Jones",spouses:new[]{12}),T.P(12,last:"Jones",spouses:new[]{11})},10*Day);return r;}
        [Fact] public void Active_connection_aim_modestly_biases_but_does_not_force_companionship()
        {var r=World();r.Agency.Add(new AgencyRecord{PersonId=1,Goal=PersonalGoal.Connection,FamilyId=r.FamilyOf(1)});Assert.Equal(1.2f,r.AgencyCompanionFactor(1,2));Assert.Equal(1,r.AgencyCompanionFactor(1,11));r.Agency[0].EndedTick=11*Day;Assert.Equal(1,r.AgencyCompanionFactor(1,2));}
        [Fact] public void Personal_time_needs_three_completed_sessions_on_separate_days()
        {var r=World();var a=new AgencyRecord{PersonId=1,Goal=PersonalGoal.Independence,Progress=.5f,FamilyId=r.FamilyOf(1)};r.Agency.Add(a);Assert.True(r.CanTakePersonalTime(1,10*Day));Assert.False(r.CompletePersonalTime(1,1700,10*Day));Assert.True(r.CompletePersonalTime(1,1800,10*Day));Assert.False(r.CompletePersonalTime(1,1800,10*Day+1));Assert.True(a.Active);r.CompletePersonalTime(1,1800,11*Day);r.CompletePersonalTime(1,1800,12*Day);Assert.False(a.Active);Assert.Equal(3,a.PersonalSessions);Assert.Equal(r.FamilyOf(2),r.FamilyOf(1));}
        [Fact] public void Attempt_cooldown_survives_incomplete_personal_time()
        {var r=World();var a=new AgencyRecord{PersonId=1,Goal=PersonalGoal.Independence,Progress=.5f,FamilyId=r.FamilyOf(1),LastActionTick=10*Day};r.Agency.Add(a);Assert.False(r.CanTakePersonalTime(1,10*Day+100));Assert.True(r.CanTakePersonalTime(1,11*Day));}
        [Fact] public void Unequal_attention_requires_actual_repeated_personal_time()
        {var r=World();Assert.False(r.CompareAttention(1,4,10*Day).Unequal);for(int d=10;d<13;d++)r.RecordTogether(1,3,2500,d*Day,BondCause.Story);var c=r.CompareAttention(1,4,12*Day);Assert.True(c.Unequal);Assert.Equal(3,c.OtherChild);Assert.Equal(7500,c.OtherTicks);Assert.Equal(0,c.ChildTicks);Assert.False(r.CompareAttention(1,4,20*Day).Unequal);}
        [Theory] [InlineData(BondCause.Care)] [InlineData(BondCause.Gathering)] [InlineData(BondCause.Meal)]
        public void Medical_and_group_attention_do_not_become_favoritism_evidence(BondCause cause)
        {var r=World();for(int d=10;d<13;d++)r.RecordTogether(1,3,2500,d*Day,cause);Assert.Empty(r.Attention);}
        [Fact] public void Quality_gain_is_capped_per_directed_pair_per_day()
        {var r=World();r.RecordTogether(1,2,5000,10*Day);float before=r.GetBond(1,2).Attachment;r.RecordTogether(1,2,5000,10*Day+1);Assert.Equal(before,r.GetBond(1,2).Attachment);r.RecordTogether(1,2,2500,11*Day);Assert.True(r.GetBond(1,2).Attachment>before);Assert.Equal(2500,r.GetBond(1,2).QualityTicksToday);}
        [Theory] [InlineData(-10,.8f)] [InlineData(1,1.6f)] [InlineData(100,4.8f)]
        public void Food_reserves_have_bounded_targets(float days,float expected)
        {Assert.Equal(expected,HouseholdPolicy.FoodTarget(1,days),3);}
        [Theory] [InlineData(true,true,true,false,false,true)] [InlineData(false,true,true,false,false,false)] [InlineData(true,false,false,true,true,true)] [InlineData(true,true,false,false,false,false)]
        public void Emergency_access_requires_policy_and_actual_need(bool policy,bool food,bool hungry,bool medicine,bool tending,bool expected)
        {Assert.Equal(expected,HouseholdPolicy.EmergencyAccess(policy,food,hungry,medicine,tending));}
        [Fact] public void Repair_requires_informed_adults_consent_and_three_separate_days()
        {var r=World();var i=r.RecordIncident(IncidentKind.Injury,11,1,7,10*Day,60,new[]{1});Assert.False(r.DiscussReparation(i.Id,2,11,true,10*Day));Assert.False(r.DiscussReparation(i.Id,1,11,false,10*Day));Assert.False(i.Resolved);Assert.False(r.DiscussReparation(i.Id,1,11,true,10*Day+1));Assert.True(r.DiscussReparation(i.Id,1,11,true,11*Day));Assert.Equal(1,i.RepairSteps);r.DiscussReparation(i.Id,1,11,true,12*Day);Assert.False(i.Resolved);r.DiscussReparation(i.Id,1,11,true,13*Day);Assert.True(i.Resolved);}
        [Fact] public void Final_reparation_requires_house_authority_without_consuming_the_day()
        {var r=World();var i=r.RecordIncident(IncidentKind.Injury,11,1,7,10*Day,60,new[]{1,2,11});Assert.True(r.DiscussReparation(i.Id,2,11,true,11*Day));Assert.True(r.DiscussReparation(i.Id,2,11,true,12*Day));Assert.False(r.DiscussReparation(i.Id,2,11,true,13*Day));Assert.Equal(2,i.RepairSteps);Assert.True(r.DiscussReparation(i.Id,1,11,true,13*Day));Assert.True(i.Resolved);}
        [Fact] public void Repayment_cannot_be_invented_without_a_live_matching_debt()
        {var r=World();r.ClaimStock(100,r.FamilyOf(1),r.FamilyOf(1),7,SupplyKind.Food);var q=r.OpenSupplyRequest(11,1,100,7,2,SupplyKind.Food,SupplyApproach.Repay,10*Day);q.DebtId=999;q.Units=2;q.Definition="Meal";Assert.False(r.NegotiateSupply(q,1,0,false,10*Day));Assert.Equal(SupplyState.Cancelled,q.State);Assert.Empty(r.Incidents);}
        [Fact] public void Completed_matching_repayment_physically_transfers_and_closes_debt()
        {var r=World();r.ClaimStock(100,r.FamilyOf(11),r.FamilyOf(11),7,SupplyKind.Food);var loan=r.OpenSupplyRequest(1,11,100,7,2,SupplyKind.Food,SupplyApproach.Loan,10*Day);loan.Units=2;loan.Definition="Meal";loan.State=SupplyState.Granted;r.ExchangeSupply(loan.Id,100,2,new[]{1,11},10*Day);loan.State=SupplyState.Stored;
         var back=r.OpenSupplyRequest(11,1,100,7,2,SupplyKind.Food,SupplyApproach.Repay,11*Day);back.DebtId=loan.Id;back.Units=2;back.Definition="Meal";Assert.True(r.NegotiateSupply(back,1,0,false,11*Day));Assert.False(loan.DebtPaid);Assert.True(r.ExchangeSupply(back.Id,100,2,new[]{1,11},11*Day));Assert.True(loan.DebtPaid);Assert.Equal(SupplyState.Repaid,loan.State);Assert.Equal(r.FamilyOf(11),r.ClaimFor(100).HolderFamily);}
        [Fact] public void Severe_known_incidents_leave_a_scar_that_fades_after_resolution()
        {var r=World();var i=r.RecordIncident(IncidentKind.Injury,11,1,7,10*Day,100,new[]{1,11});var b=r.GetBond(1,11);b.Trust=100;b.Tension=0;b.Familiarity=100;r.UpdateBonds((a,z)=>100,11*Day,null);float hurtTrust=b.Trust,hurtTension=b.Tension;Assert.True(hurtTrust<100);Assert.True(hurtTension>0);r.ForgiveIncident(i.Id,r.FamilyOf(1),11*Day);r.UpdateBonds((a,z)=>100,42*Day,null);Assert.True(b.Trust>hurtTrust);Assert.True(b.Tension<hurtTension);}
        [Fact] public void Damaged_claims_with_a_missing_holder_are_removed_on_rebuild()
        {var r=World();r.Claims.Add(new StockClaim{ThingId=77,OwnerFamily=r.FamilyOf(1),HolderFamily=999});r.RebuildIndexes();Assert.Null(r.ClaimFor(77));}
        [Fact] public void Gathering_pair_sampling_is_bounded_unique_and_rotates()
        {var people=Enumerable.Range(1,10000).ToList();var pairs=GatheringBudget.Pairs(people,60,0);Assert.Equal(60,pairs.Count);Assert.All(pairs,p=>Assert.NotEqual(p.Key,p.Value));Assert.Equal(60,pairs.Select(p=>(Math.Min(p.Key,p.Value),Math.Max(p.Key,p.Value))).Distinct().Count());Assert.NotEqual(pairs[0],GatheringBudget.Pairs(people,60,100)[0]);Assert.Single(GatheringBudget.Pairs(new[]{1,2},60,0));}
        [Fact] public void Shared_activity_event_does_not_double_count_quality_time()
        {var r=World();r.ChangeBond(1,2,new BondChange(0,0,0,0),10*Day);r.ChangeBond(2,1,new BondChange(0,0,0,0),10*Day);var before=r.GetBond(1,2).QualityRecent;var c=new PairContext{Registry=r,Initiator=new Actor{Id=1},Recipient=new Actor{Id=2},QualityHours=0};r.ApplyEvent(new EventResult{Context=c,Def=new EventDef{Id="credit"},Tier=EventTier.Great},10*Day);Assert.Equal(before,r.GetBond(1,2).QualityRecent);c.QualityHours=.28f;r.ApplyEvent(new EventResult{Context=c,Def=new EventDef{Id="credit2"},Tier=EventTier.Great},10*Day+1);Assert.Equal(before+.28f,r.GetBond(1,2).QualityRecent,3);}
    }
}
