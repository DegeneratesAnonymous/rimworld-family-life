using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class OrganicCeremonyTests
    {
        private const int Day=ActivityTuning.TicksPerDay;

        private static FamilyRegistry Family(out int family)
        {
            var registry=new FamilyRegistry();
            registry.Apply(new[]
            {
                T.P(1,"Alex",spouses:new[]{2}),T.P(2,"Morgan",spouses:new[]{1}),
                T.P(3,"Casey",parents:new[]{1,2}),T.P(4,"Riley",parents:new[]{1,2},presence:Presence.Away)
            },1000);
            family=registry.FamilyOf(1);
            return registry;
        }

        [Fact]
        public void Becoming_spouses_is_distinct_from_becoming_partners()
        {
            var registry=new FamilyRegistry();
            registry.Apply(new[]{T.P(1,lovers:new[]{2}),T.P(2,lovers:new[]{1})},1000);

            var events=registry.Apply(new[]{T.P(1,spouses:new[]{2}),T.P(2,spouses:new[]{1})},2000);

            Assert.Contains(events,e=>e.Kind==FamilyEventKind.MarriageFormed&&e.A==1&&e.B==2);
            Assert.DoesNotContain(events,e=>e.Kind==FamilyEventKind.PartnershipFormed);
            Assert.DoesNotContain(registry.Apply(new[]{T.P(1,spouses:new[]{2}),T.P(2,spouses:new[]{1})},3000),e=>e.Kind==FamilyEventKind.MarriageFormed);
        }

        [Fact]
        public void Funeral_requests_are_deduplicated_and_complete_into_history()
        {
            var registry=Family(out int family);
            registry.RequestGathering("memorial",family,2,2000,Day/2,4*Day);
            registry.RequestGathering("funeral",family,2,2000,Day/2,4*Day);
            var request=Assert.Single(registry.GatheringRequests);
            Assert.Equal(CeremonyKind.Funeral,request.Ceremony);

            Assert.True(registry.MarkCeremonyStarted(request.Id,1,5,request.NotBefore,new[]{1,3}));
            var record=registry.CompleteCeremony(request.Id,request.NotBefore+1000,2,2,.75f);

            Assert.Equal(CeremonyKind.Funeral,record.Kind);
            Assert.Empty(registry.GatheringRequests);
            Assert.Contains(registry.MemoriesOf(family),m=>m.Kind==MemoryKind.Loss&&m.A==2);
            Assert.True(registry.PrestigeOf(1).Value>0);
            Assert.True(registry.GetLineage(family).Standing>0);
        }

        [Fact]
        public void A_culture_that_values_home_plans_one_bounded_annual_reunion()
        {
            var registry=Family(out int family);
            var house=registry.GetLineage(family);
            while(house.Culture.Count<DimInfo.Count)house.Culture.Add(0);
            house.Culture[(int)Dim.Hearth]=30;
            int now=FamilyRegistry.GrandReunionInterval+2000;
            house.LastGrandReunionTick=1;
            house.NextCeremonyCheckTick=now;

            registry.UpdateOrganicCeremonies(now,true);
            var request=Assert.Single(registry.GatheringRequests.Where(r=>r.Ceremony==CeremonyKind.GrandReunion));
            Assert.Contains(4,request.VisitorIds);
            Assert.True(request.Influences.HasFlag(CeremonyInfluence.FamilyCulture));

            registry.UpdateOrganicCeremonies(now+Day,true);
            Assert.Single(registry.GatheringRequests.Where(r=>r.Ceremony==CeremonyKind.GrandReunion));
        }

        [Fact]
        public void Ceremony_catalog_has_variety_for_each_public_occasion()
        {
            Assert.Equal(26,CeremonyMoments.All.Count);
            Assert.Equal(26,CeremonyMoments.All.Select(e=>e.Id).Distinct().Count());
            Assert.Equal(8,CeremonyMoments.For("wedding").Count());
            Assert.Equal(8,CeremonyMoments.For("funeral").Count());
            Assert.Equal(10,CeremonyMoments.For("grandreunion").Count());
            Assert.All(CeremonyMoments.All,e=>Assert.Equal(4,e.Outcomes.Length));
        }

        [Fact]
        public void Every_ceremony_moment_has_localized_story_text()
        {
            var directory=new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            System.IO.FileInfo file=null;
            while(directory!=null)
            {
                var candidate=new System.IO.FileInfo(System.IO.Path.Combine(directory.FullName,"Languages","English","Keyed","CeremonyMoments.xml"));
                if(candidate.Exists){file=candidate;break;}
                directory=directory.Parent;
            }
            Assert.NotNull(file);
            var keys=System.Xml.Linq.XDocument.Load(file.FullName).Root.Elements().Select(e=>e.Name.LocalName).ToHashSet();
            foreach(var scene in CeremonyMoments.All)
            {
                string key="WFL_Ev_"+scene.Id.Replace('-','_');
                foreach(string part in new[]{"Title","Poor","Flat","Good","Great"})Assert.Contains(key+"_"+part,keys);
            }
            Assert.Equal(CeremonyMoments.All.Count*5,keys.Count);
        }
    }
}
