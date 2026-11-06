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
        public void A_new_marriage_creates_one_wedding_memory_for_the_pair()
        {
            var registry=new FamilyRegistry();
            registry.Apply(new[]{T.P(1),T.P(2)},1000);
            var events=registry.Apply(new[]{T.P(1,spouses:new[]{2}),T.P(2,spouses:new[]{1})},2000);

            registry.RecordMemories(events,null,2000);

            int family=registry.FamilyOf(1);
            Assert.Single(registry.MemoriesOf(family).Where(m=>m.Kind==MemoryKind.Wedding&&
                (m.A==1&&m.B==2||m.A==2&&m.B==1)));
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
            Assert.Equal("CF-04",CeremonyMoments.Get("CF-04").Id);
        }

        [Fact]
        public void Tradition_completion_records_actual_attendance_moments_and_outcome()
        {
            var registry=Family(out int family);
            registry.GetLineage(family).Traditions.Add(new TraditionRecord{Id="feast",AdoptedTick=1});
            registry.RequestGathering("feast",family,0,2000,0,Day);
            var request=Assert.Single(registry.GatheringRequests);
            registry.MarkCeremonyStarted(request.Id,1,0,2100,new[]{1,2,3});

            registry.MarkGatheringHeld(request.Id,3000,1,2,-.25f);

            var record=Assert.Single(registry.Ceremonies);
            Assert.Equal(1,record.Attendees);
            Assert.Equal(2,record.Moments);
            Assert.Equal(-.25f,record.Outcome);
        }

        [Fact]
        public void Request_recovery_runs_even_when_new_reunions_are_not_planned()
        {
            var registry=Family(out int family);
            var request=registry.RequestCeremony(CeremonyKind.Wedding,family,1,2,1000,0,Day);
            registry.MarkCeremonyStarted(request.Id,1,0,1000,new[]{1,2});

            registry.UpdateOrganicCeremonies(1000+2*Day+1,false);

            Assert.Empty(registry.GatheringRequests);
        }

        [Fact]
        public void Unconnected_guests_cannot_be_cast_in_a_family_ceremony_scene()
        {
            var registry=new FamilyRegistry();
            registry.Apply(new[]{T.P(1,"Alex",spouses:new[]{2}),T.P(2,"Morgan",spouses:new[]{1}),T.P(3,"Casey",parents:new[]{1,2}),T.P(9,"Unrelated","Lee")},1000);
            int family=registry.FamilyOf(1);
            var context=Context(registry,3,9,family,subject:1,subjectB:2);

            Assert.All(CeremonyMoments.For("wedding"),scene=>Assert.False(Eligible(scene,context)));
        }

        [Fact]
        public void Wedding_welcome_requires_people_from_the_two_actual_houses()
        {
            var registry=new FamilyRegistry();
            registry.Apply(new[]{
                T.P(1,"Alex","Smith",parents:new[]{3},spouses:new[]{11}),T.P(3,"Avery","Smith"),T.P(4,"Riley","Smith",parents:new[]{3}),
                T.P(11,"Morgan","Jones",parents:new[]{13},spouses:new[]{1}),T.P(13,"Jordan","Jones")},1000);
            int first=registry.FamilyOf(1),second=registry.FamilyOf(11);
            var scene=CeremonyMoments.For("wedding").Single(e=>e.Id=="CW-02");

            Assert.True(Eligible(scene,Context(registry,3,13,first,second,1,11)));
            Assert.False(Eligible(scene,Context(registry,3,4,first,second,1,11)));
        }

        [Fact]
        public void Funeral_blame_must_be_about_the_person_being_mourned()
        {
            var registry=Family(out int family);
            registry.RecordIncident(IncidentKind.Death,4,3,0,2000,50,new[]{1,3,4});
            var scene=CeremonyMoments.For("funeral").Single(e=>e.Id=="CF-06");
            var wrongDeath=Context(registry,1,3,family,subject:2);
            Assert.False(Eligible(scene,wrongDeath));

            registry.RecordIncident(IncidentKind.Death,4,2,0,3000,50,new[]{1,3,4});
            Assert.True(Eligible(scene,wrongDeath));
        }

        [Fact]
        public void Reunion_welcome_pairs_a_returning_visitor_with_a_host()
        {
            var registry=Family(out int family);
            var scene=CeremonyMoments.For("grandreunion").Single(e=>e.Id=="CR-02");
            var context=Context(registry,1,4,family,visitors:new[]{4});

            Assert.True(Eligible(scene,context));
            context.OccasionVisitorIds=System.Array.Empty<int>();
            Assert.False(Eligible(scene,context));
        }

        private static PairContext Context(FamilyRegistry registry,int a,int b,int family,int second=0,int subject=0,int subjectB=0,int[] visitors=null)
        {
            var first=registry.GetPerson(a);var secondPerson=registry.GetPerson(b);
            return new PairContext
            {
                Registry=registry,Initiator=new Actor{Id=a,LineageId=registry.FamilyOf(a),Profile=new float[DimInfo.Count]},
                Recipient=new Actor{Id=b,LineageId=registry.FamilyOf(b),Profile=new float[DimInfo.Count]},
                InitiatorBond=new Bond{From=a,To=b,Familiarity=0},RecipientBond=new Bond{From=b,To=a,Familiarity=0},
                Kind=registry.KindOf(first,secondPerson),SameFamily=registry.FamilyOf(a)!=0&&registry.FamilyOf(a)==registry.FamilyOf(b),
                OccasionFamilyId=family,OccasionSecondFamilyId=second,OccasionSubjectId=subject,OccasionSubjectBId=subjectB,
                OccasionVisitorIds=visitors??System.Array.Empty<int>()
            };
        }

        private static bool Eligible(EventDef scene,PairContext context)=>scene.Opportunity.All(condition=>condition.Test(context));

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
