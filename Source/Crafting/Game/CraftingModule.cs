using System;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    public sealed class CraftingMod:Mod
    {
        public CraftingMod(ModContentPack content):base(content){FamilyFramework.Install(new CraftingModule());}
    }
    public sealed class CraftingModule:IFamilyModule
    {
        public string Id=>CraftingCareers.Module;
        public void Register()
        {
            FamilyFramework.Catalog.Register(new ModuleInfo{Id=Id,Name="Wil's Crafting & Workshops",ApiVersion=FamilyFramework.ApiVersion,Requires=new[]{ModuleCatalog.FrameworkId}});
            CareerService.RegisterCareer(CraftingCareers.Definition());
            CareerService.RegisterRole(new ProfessionRole{Id=CraftingCareers.Woodworker,ModuleId=Id,Career=CraftingCareers.Career,Level=1});
            CareerService.RegisterRole(new ProfessionRole{Id=CraftingCareers.Stoneworker,ModuleId=Id,Career=CraftingCareers.Career,Level=1});
            foreach(var branch in new[]{"Smith","Tailor"})CareerService.RegisterRole(new ProfessionRole{Id=CraftingCareers.Role(branch),ModuleId=Id,Career=CraftingCareers.Career,Level=2,Specialty=branch});
            foreach(var specialty in new[]{"Weaponsmith","Armorsmith","Mechanist","Couturier","Outfitter"})CareerService.RegisterRole(new ProfessionRole{Id=CraftingCareers.Role(specialty),ModuleId=Id,Career=CraftingCareers.Career,Level=3,Specialty=specialty});
            FamilyFramework.WorkshopProducts.Add("wils.crafting.products",Id,CraftingProducts.Profile);
            CareerService.Registered(Id);
        }
    }
    public static class CraftingProducts
    {
        private static bool Has(string value,params string[] words){value=value??"";foreach(var word in words)if(value.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0)return true;return false;}
        public static WorkshopProductProfile Profile(Pawn pawn,Thing bench,Thing product)
        {
            if(product?.def==null||pawn?.CurJob?.bill?.recipe?.workSkill!=SkillDefOf.Crafting)return null;
            string name=product.def.defName??"",stuff=product.Stuff?.defName??"";
            bool apparel=product.def.IsApparel,weapon=product.def.IsWeapon;
            var facts=new CraftingProductFacts
            {
                Weapon=weapon,Apparel=apparel,
                Armor=apparel&&(Has(name,"Armor","Helmet","Vest","Shield","Plate","Flak")||product.GetStatValue(StatDefOf.ArmorRating_Sharp)>0.15f),
                Prestige=apparel&&Has(name,"Royal","Prestige","Formal","Corset","Crown","Robe","Drape"),
                Utility=apparel&&Has(name,"Parka","Duster","Tuque","Hat","Pants","Shirt","Jacket","Cape","Utility"),
                Mechanical=Has(name,"Component","Mechan","Bionic","Prosthe","Chip","Sensor","Engine","Turret"),
                Metal=Has(stuff,"Steel","Plasteel","Uranium","Gold","Silver","Metal"),Wood=Has(stuff,"Wood","Log","Timber"),Stone=Has(stuff,"Granite","Limestone","Marble","Slate","Sandstone","Stone")
            };
            var track=CraftingClassifier.Classify(facts);if(track==null)return null;
            string specialty=track.Specialty;
            var ledger=FamilyComponent.Current?.Registry.Careers();
            if(specialty!=null&&ledger?.IsSpecialist(pawn.thingIDNumber,CraftingCareers.Career,track.Route)!=true)specialty=track.Route;
            return new WorkshopProductProfile{Career=CraftingCareers.Career,Route=track.Route,Specialty=specialty,Kind=track.Kind,Presentation=100};
        }
    }
}
