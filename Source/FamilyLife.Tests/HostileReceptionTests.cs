using System;
using System.Collections.Generic;
using FamilyLife.Domain;
using Xunit;
namespace FamilyLife.Tests
{
    public class HostileReceptionTests
    {
        [Fact] public void Skilled_public_slight_cannot_force_warm_reception_or_forgiveness()
        {
            var r=new FamilyRegistry();
            r.Apply(new[]{T.P(1,spouses:new[]{2}),T.P(2,spouses:new[]{1}),T.P(5,last:"Jones",spouses:new[]{6}),T.P(6,last:"Jones",spouses:new[]{5})},600000);
            var actor=new Actor{Id=1,LineageId=r.FamilyOf(1),Skills=new Dictionary<string,int>{{"Social",20}},Profile=new float[DimInfo.Count]};
            actor.Profile[(int)Dim.Warmth]=-1.5f;
            var other=new Actor{Id=5,LineageId=r.FamilyOf(5),Mood=.1f,Profile=new float[DimInfo.Count]};
            other.Profile[(int)Dim.Warmth]=-1.5f;
            var c=new PairContext{Registry=r,Initiator=actor,Recipient=other,OpinionOfRecipient=-90,OpinionOfInitiator=-90,RecipientBond=new Bond{From=5,To=1,Trust=0,Tension=90},Now=600000};
            for(int seed=0;seed<100;seed++)
            {var result=r.ResolveEvent(EventCatalog.Get("C1-03"),c,new Random(seed));Assert.True(result.Execution>.5f);Assert.True(result.RecipientFeeling<=0);Assert.True(result.Tier<EventTier.Good);Assert.False(result.RiftResolved);}
        }
    }
}
