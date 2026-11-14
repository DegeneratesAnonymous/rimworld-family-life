using System.Linq;
using System.IO;
using System.Xml.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class CraftingTests
    {
        [Fact] public void Smith_and_tailor_are_parallel_and_armorsmith_accepts_either_route()
        {
            var d=CraftingCareers.Definition();var l=new CareerLedger();
            l.Award(1,d.Id,"Tailor","craft","tailor",150,0);l.Evaluate(1,d,k=>20);
            Assert.True(l.IsSpecialist(1,d.Id,"Tailor"));Assert.False(l.IsSpecialist(1,d.Id,"Smith"));
            l.Award(1,d.Id,"Armorsmith","craft","armor",100,1);l.Evaluate(1,d,k=>20);
            Assert.True(l.IsSpecialist(1,d.Id,"Armorsmith"));
        }
        [Fact] public void Advanced_routes_do_not_bypass_their_skilled_branch()
        {
            var d=CraftingCareers.Definition();var l=new CareerLedger();l.Award(1,d.Id,"Weaponsmith","craft","weapon",300,0);l.Evaluate(1,d,k=>20);
            Assert.False(l.IsSpecialist(1,d.Id,"Weaponsmith"));
        }
        [Theory]
        [InlineData(true,false,false,false,false,false,"Smith","Weaponsmith")]
        [InlineData(false,true,false,true,false,false,"Tailor","Couturier")]
        [InlineData(false,true,true,false,false,true,"Smith","Armorsmith")]
        [InlineData(false,false,false,false,true,false,"Smith","Mechanist")]
        public void Products_map_to_expected_routes(bool weapon,bool apparel,bool armor,bool prestige,bool mechanical,bool metal,string route,string specialty)
        {
            var t=CraftingClassifier.Classify(new CraftingProductFacts{Weapon=weapon,Apparel=apparel,Armor=armor,Prestige=prestige,Mechanical=mechanical,Metal=metal});Assert.Equal(route,t.Route);Assert.Equal(specialty,t.Specialty);
        }
        [Fact] public void The_tree_has_the_agreed_roles()
        {
            var d=CraftingCareers.Definition();Assert.Equal(new[]{"Smith","Tailor"},d.Level2Branches.Keys.OrderBy(x=>x).ToArray());Assert.Equal(5,d.Specialties.Count);Assert.Equal(new[]{"Smith","Tailor"},d.Specialties["Armorsmith"].RequiresAny.OrderBy(x=>x).ToArray());
        }
        [Fact] public void A_level_two_branch_assignment_requires_that_exact_branch()
        {
            var d=CraftingCareers.Definition();var l=new CareerLedger();var smith=new ProfessionRole{Id="wils.crafting.smith",ModuleId=CraftingCareers.Module,Career=d.Id,Level=2,Specialty="Smith"};
            l.Award(1,d.Id,"Tailor","craft","tailor",150,0);l.Evaluate(1,d,k=>20);Assert.Equal(AssignResult.NotQualified,AssignmentBook.Check(smith,l,1));
            l.Award(1,d.Id,"Smith","craft","smith",150,1);l.Evaluate(1,d,k=>20);Assert.Equal(AssignResult.Assigned,AssignmentBook.Check(smith,l,1));
        }
        [Fact] public void Specialty_bonuses_are_bounded_and_require_an_active_assignment()
        {
            Assert.Equal(100,CraftingBonuses.ValuePercent("Couturier",false));Assert.Equal(125,CraftingBonuses.ValuePercent("Couturier",true));Assert.Equal(120,CraftingBonuses.ValuePercent("Mechanist",true));
            Assert.Equal(3,CraftingBonuses.ImprovedQuality(2,true));Assert.Equal(4,CraftingBonuses.ImprovedQuality(4,true));Assert.Equal(2,CraftingBonuses.ImprovedQuality(2,false));
        }
        [Fact] public void Every_crafting_role_has_five_positive_negative_and_relationship_events()
        {
            foreach(var role in CraftingEvents.Roles){var e=CraftingEvents.All.Where(x=>x.Role==role).ToList();Assert.Equal(5,e.Count(x=>x.Mood>0));Assert.Equal(5,e.Count(x=>x.Mood<0));Assert.Equal(5,e.Count(x=>x.Opinion!=0));}
            Assert.Equal(CraftingEvents.All.Length,CraftingEvents.All.Select(x=>x.Id).Distinct().Count());
        }
        [Fact] public void Crafting_thoughts_and_package_xml_are_well_formed()
        {
            string root=RepoPaths.Root();var doc=XDocument.Load(Path.Combine(root,"Packages/WilsCrafting/Defs/CraftingThoughts.xml"));var names=doc.Descendants("defName").Select(x=>x.Value).ToList();foreach(var e in CraftingEvents.All)Assert.Contains(e.Thought,names);
            foreach(var file in Directory.GetFiles(Path.Combine(root,"Packages/WilsCrafting"),"*.xml",SearchOption.AllDirectories))XDocument.Load(file);
        }
    }
}
