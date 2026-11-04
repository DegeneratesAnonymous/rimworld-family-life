using System.Linq;
using FamilyLife.Domain;
using Xunit;
namespace FamilyLife.Tests
{
    public class AmbitionTests
    {
        const int Day = ActivityTuning.TicksPerDay;
        static FamilyRegistry World()
        { var r=new FamilyRegistry();r.Apply(new[]{T.P(1,spouses:new[]{2}),T.P(2,spouses:new[]{1}),T.Kid(3,new[]{1,2}),T.P(9,last:"Other")},10*Day);r.ChangeBond(1,2,new BondChange(70,70,0,0),10*Day);return r; }
        static void Moment(FamilyRegistry r,int day,EventTier tier=EventTier.Good,int other=2,EventWeight weight=EventWeight.Meaningful)
        {r.NoteAgencyMoment(new EventResult{Def=new EventDef{Id="observed",Weight=weight},Tier=tier,Context=new PairContext{Initiator=new Actor{Id=1},Recipient=new Actor{Id=other}}},day*Day);}
        [Fact] public void A_single_moment_never_assigns_an_ambition()
        {var r=World();Moment(r,10);Assert.Equal(PersonalGoal.None,r.AgencyOf(1).Goal);}
        [Fact] public void Repeated_same_day_events_do_not_farm_progress()
        {var r=World();for(int n=0;n<50;n++)Moment(r,10);Assert.Equal(1,r.AgencyOf(1).Included);}
        [Theory] [InlineData(EventTier.Good,PersonalGoal.Connection)] [InlineData(EventTier.Poor,PersonalGoal.Independence)]
        public void Three_separate_days_of_experience_can_establish_an_aim(EventTier tier,PersonalGoal goal)
        {var r=World();for(int d=10;d<13;d++)Moment(r,d,tier);Assert.Equal(goal,r.AgencyOf(1).Goal);Assert.Equal(0,r.AgencyOf(1).Progress);Moment(r,13,tier);Assert.Equal(.125f,r.AgencyOf(1).Progress);}
        [Fact] public void Children_and_outsiders_are_not_assigned_family_ambitions()
        {var r=World();Moment(r,10,other:3);Assert.Null(r.AgencyOf(3));Moment(r,11,other:9);Assert.Null(r.AgencyOf(9));Assert.Equal(1,r.AgencyOf(1).Included);}
        [Theory] [InlineData(EventTier.Flat,EventWeight.Meaningful)] [InlineData(EventTier.Good,EventWeight.Ambient)]
        public void Ambient_and_neutral_moments_are_not_evidence(EventTier tier,EventWeight weight)
        {var r=World();Moment(r,10,tier,weight:weight);Assert.Empty(r.Agency);}
        [Fact] public void Leaving_sets_aside_an_active_goal()
        {var r=World();for(int d=10;d<13;d++)Moment(r,d);r.GetPerson(1).Presence=Presence.Away;r.MaintainAgency(13*Day);Assert.False(r.AgencyOf(1).Active);}
        [Fact] public void Goals_need_sustained_experience_and_have_a_cooldown()
        {var r=World();for(int d=10;d<21;d++)Moment(r,d);var a=r.AgencyOf(1);Assert.Equal(1,a.Progress);Assert.False(a.Active);Moment(r,21);Assert.Equal(20*Day,a.EndedTick);}
        [Fact] public void Long_silence_expires_the_aim_without_inventing_exclusion()
        {var r=World();for(int d=10;d<13;d++)Moment(r,d);r.MaintainAgency(28*Day);Assert.False(r.AgencyOf(1).Active);Assert.Equal(0,r.AgencyOf(1).Excluded);}
        [Fact] public void Independence_is_not_fulfilled_merely_by_further_rejection()
        {var r=World();for(int d=10;d<22;d++)Moment(r,d,EventTier.Poor);Assert.Equal(1,r.AgencyOf(1).Progress);Assert.True(r.AgencyOf(1).Active);}
        [Fact] public void Repair_accepts_old_saves_and_removes_duplicate_records()
        {var r=World();r.Agency=null;r.RebuildIndexes();Assert.Empty(r.Agency);Moment(r,10);r.Agency.Add(r.Agency[0]);r.RebuildIndexes();Assert.Equal(r.Agency.Count,r.Agency.Select(a=>a.PersonId).Distinct().Count());}
        [Fact] public void Mixed_outcome_can_cost_the_recipient_trust_without_counting_as_welcome()
        {var r=World();r.NoteAgencyMoment(new EventResult{Def=TraditionMoments.Get("T2-06"),Tier=EventTier.Great,Context=new PairContext{Initiator=new Actor{Id=1},Recipient=new Actor{Id=2}}},10*Day);Assert.Equal(1,r.AgencyOf(2).Excluded);Assert.Equal(1,r.AgencyOf(1).Included);}
    }
}
