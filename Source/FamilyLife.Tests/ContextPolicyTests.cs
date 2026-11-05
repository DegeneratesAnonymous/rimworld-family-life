using FamilyLife.Domain;
using Xunit;
namespace FamilyLife.Tests
{
    public class ContextPolicyTests
    {
        [Theory] [InlineData(0,0,false)] [InlineData(7,8,false)] [InlineData(7,7,true)]
        public void Coincidental_recreation_requires_an_actual_shared_target(int a,int b,bool expected)
        {Assert.Equal(expected,SharedActivityPolicy.SameTarget(a,b));}
        sealed class Area { public int Id, Map; }
        [Fact] public void Colliding_ids_resolve_only_on_the_explicit_map()
        {
            var a = new Area { Id=7, Map=1 }; var b = new Area { Id=7, Map=2 };
            var candidates = new[]{a,b};
            Assert.Same(a,FamilyAreaPolicy.Resolve(candidates,7,1,x=>x.Id,x=>x.Map));
            Assert.Same(b,FamilyAreaPolicy.Resolve(candidates,7,2,x=>x.Id,x=>x.Map));
            Assert.Null(FamilyAreaPolicy.Resolve(candidates,7,3,x=>x.Id,x=>x.Map));
            Assert.Null(FamilyAreaPolicy.Resolve(candidates,7,-1,x=>x.Id,x=>x.Map));
        }
        [Fact] public void Legacy_area_can_resolve_only_if_unique()
        {var a=new Area{Id=7,Map=1};Assert.Same(a,FamilyAreaPolicy.Resolve(new[]{a},7,-1,x=>x.Id,x=>x.Map));Assert.Null(FamilyAreaPolicy.Resolve(new[]{a},9,-1,x=>x.Id,x=>x.Map));}
        [Theory] [InlineData(null)] [InlineData("")] [InlineData("WFL_FamilyTime")]
        public void Ordinary_work_and_family_jobs_never_become_organized_aid(string joy)
        {Assert.Equal(ActivityKind.None,SharedActivityPolicy.Discover(joy));}
        [Theory] [InlineData("Gaming_Cerebral",ActivityKind.Competition)] [InlineData("Gaming_Dexterity",ActivityKind.Competition)] [InlineData("Social",ActivityKind.GameNight)]
        public void Shared_recreation_uses_recreation_context(string joy,ActivityKind expected)
        {Assert.Equal(expected,SharedActivityPolicy.Discover(joy));Assert.NotEqual(ActivityKind.CommunityAid,SharedActivityPolicy.Discover(joy));}
    }
}
