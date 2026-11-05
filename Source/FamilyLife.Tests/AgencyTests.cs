using FamilyLife.Domain;
using Xunit;
namespace FamilyLife.Tests
{
    public class AgencyTests
    {
        [Fact] public void Unknown_person_has_no_manufactured_family_support()
        { var r=new FamilyRegistry(); var a=r.AssessBelonging(99); Assert.Equal(0,a.FamilyId);Assert.True(a.Isolated);Assert.False(a.Divided); }
        [Fact] public void Belonging_is_directed_and_can_be_divided()
        {
            var r=new FamilyRegistry();r.Apply(new[]{T.P(1,spouses:new[]{2}),T.P(2,spouses:new[]{1}),T.Kid(3,new[]{1,2})},600000);
            r.ChangeBond(1,2,new BondChange(0,0,0,0),600000);r.ChangeBond(1,3,new BondChange(0,0,0,0),600000);
            var support=r.GetBond(1,2);support.Attachment=70;support.Trust=70;support.Tension=0;
            var strain=r.GetBond(1,3);strain.Trust=10;strain.Tension=70;
            var a=r.AssessBelonging(1);Assert.Equal(1,a.TrustedConnections);Assert.Equal(1,a.StrainedConnections);Assert.True(a.Divided);
            r.GetPerson(2).Presence=Presence.Away;Assert.Equal(0,r.AssessBelonging(1).TrustedConnections);
        }
        [Theory] [InlineData(0f)] [InlineData(.25f)] [InlineData(1f)]
        public void Ordinary_event_pace_scales_all_primary_axes(float pace)
        {
            var r=new FamilyRegistry();r.Apply(new[]{T.P(1,spouses:new[]{2}),T.P(2,spouses:new[]{1})},600000);
            r.ChangeBond(1,2,new BondChange(0,0,0,0),600000);r.ChangeBond(2,1,new BondChange(0,0,0,0),600000);
            var b=r.GetBond(2,1);b.Trust=50;b.Tension=30;float familiar=b.Familiarity;
            var c=new PairContext{Registry=r,Initiator=new Actor{Id=1},Recipient=new Actor{Id=2},RelationshipPace=pace};
            r.ApplyEvent(new EventResult{Context=c,Def=TraditionMoments.Get("T2-06"),Tier=EventTier.Great},600000);
            Assert.Equal(50-2*pace,b.Trust,3);Assert.Equal(30-pace,b.Tension,3);Assert.Equal(familiar+3*pace,b.Familiarity,3);
        }
    }
}
