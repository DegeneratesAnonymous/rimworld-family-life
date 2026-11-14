using System;
using System.Linq;
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
            FamilyFramework.Catalog.Register(new ModuleInfo{Id=Id,Name="Wil's Role Modules : Crafting",ApiVersion=FamilyFramework.ApiVersion,Requires=new[]{ModuleCatalog.FrameworkId}});
            CareerService.RegisterCareer(CraftingCareers.Definition());
            CareerService.RegisterRole(new ProfessionRole{Id=CraftingCareers.Woodworker,ModuleId=Id,Career=CraftingCareers.Career,Level=1});
            CareerService.RegisterRole(new ProfessionRole{Id=CraftingCareers.Stoneworker,ModuleId=Id,Career=CraftingCareers.Career,Level=1});
            foreach(var branch in new[]{"Smith","Tailor"})CareerService.RegisterRole(new ProfessionRole{Id=CraftingCareers.Role(branch),ModuleId=Id,Career=CraftingCareers.Career,Level=2,Specialty=branch});
            foreach(var specialty in new[]{"Weaponsmith","Armorsmith","Mechanist","Couturier","Outfitter"})CareerService.RegisterRole(new ProfessionRole{Id=CraftingCareers.Role(specialty),ModuleId=Id,Career=CraftingCareers.Career,Level=3,Specialty=specialty,BonusKey="WFL_Crafting_"+specialty+"Bonus"});
            FamilyFramework.WorkshopProducts.Add("wils.crafting.products",Id,CraftingProducts.Profile);
            FamilyFramework.ProductMade.Add("wils.crafting.specialtybonuses",Id,CraftingProducts.ApplyBonus);
            FamilyFramework.WorkshopSale.Add("wils.crafting.customerevents",Id,CraftingStories.Sale);
            FamilyFramework.Resets.Add("wils.crafting.reset",Id,CraftingStories.Reset);
            CareerService.Registered(Id);
        }
    }
    public static class CraftingProducts
    {
        private static bool Has(string value,params string[] words){value=value??"";foreach(var word in words)if(value.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0)return true;return false;}
        public static CraftingTrack Track(Thing product)
        {
            if(product?.def==null)return null;
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
            return CraftingClassifier.Classify(facts);
        }
        /// <summary>The specialty an advanced product is credited to: it needs any accepted branch (Armorsmith accepts Smith or Tailor), otherwise the product's route.</summary>
        public static string ReportedSpecialty(Pawn pawn,CraftingTrack track)
        {
            if(track?.Specialty==null)return track?.Specialty;
            var ledger=FamilyComponent.Current?.Registry.Careers();if(ledger==null)return track.Route;
            bool qualified=ledger.IsSpecialist(pawn.thingIDNumber,CraftingCareers.Career,track.Route)||(track.Specialty=="Armorsmith"&&(ledger.IsSpecialist(pawn.thingIDNumber,CraftingCareers.Career,"Smith")||ledger.IsSpecialist(pawn.thingIDNumber,CraftingCareers.Career,"Tailor")));
            return qualified?track.Specialty:track.Route;
        }
        public static WorkshopProductProfile Profile(Pawn pawn,Thing bench,Thing product)
        {
            if(pawn?.CurJob?.bill?.recipe?.workSkill!=SkillDefOf.Crafting)return null;var track=Track(product);if(track==null)return null;
            string specialty=ReportedSpecialty(pawn,track);
            bool active=track.Specialty!=null&&CareerService.IsAssignedSpecialist(pawn,CraftingCareers.Career,track.Specialty);
            int premium=CraftingBonuses.ValuePercent(track.Specialty,active);
            return new WorkshopProductProfile{Career=CraftingCareers.Career,Route=track.Route,Specialty=specialty,Kind=track.Kind,Presentation=active?premium:100,ValuePercent=premium};
        }
        public static void ApplyBonus(Pawn pawn,Thing bench,Thing product,int job)
        {
            if(pawn?.CurJob?.bill?.recipe?.workSkill!=SkillDefOf.Crafting)return;var track=Track(product);
            if(track==null)return;bool active=track.Specialty!=null&&CareerService.IsAssignedSpecialist(pawn,CraftingCareers.Career,track.Specialty);
            if(active&&track.Specialty!="Mechanist")
            {
                var quality=product.TryGetComp<CompQuality>();if(quality!=null){var current=quality.Quality;var improved=(QualityCategory)CraftingBonuses.ImprovedQuality((int)current,true);if(improved!=current)quality.SetQuality(improved,ArtGenerationContext.Colony);}
            }
            // Crafting owns its experience so it works without a commercial shop. Hospitality credits shop deposits under the same activity key, which the ledger counts once.
            if(job!=0)CareerService.Report(FamilyComponent.Current,pawn,CraftingCareers.Career,ReportedSpecialty(pawn,track),track.Kind,"recipe:"+job,5,FrameworkServices.Now);
            CraftingStories.Produced(pawn,track,product,job);
        }
    }
    public static class CraftingStories
    {
        private static readonly System.Collections.Generic.Dictionary<string,int> last=new System.Collections.Generic.Dictionary<string,int>();
        public static void Reset()=>last.Clear();
        private static string ActiveRole(Pawn pawn,CraftingTrack track)
        {
            if(track?.Specialty!=null&&CareerService.IsAssignedSpecialist(pawn,CraftingCareers.Career,track.Specialty))return track.Specialty;
            if(track?.Route!=null&&CareerService.IsAssignedSpecialist(pawn,CraftingCareers.Career,track.Route))return track.Route;
            var book=FamilyComponent.Current?.Registry.Assignments();if(track?.Entry=="Stoneworker"&&book?.IsAssigned(pawn.thingIDNumber,CraftingCareers.Stoneworker)==true)return "Stoneworker";
            return "Woodworker";
        }
        private static void Give(Pawn subject,Pawn other,string role,string fact,int seed)
        {
            int day=FrameworkServices.EconomyDay;var e=CraftingEvents.Eligible(role,fact,other!=null).OrderBy(x=>x.Id).FirstOrDefault();if(e==null)return;
            string key=subject.thingIDNumber+":"+e.Id+":"+(other?.thingIDNumber??0);if(last.TryGetValue(key,out int before)&&day-before<3)return;last[key]=day;FrameworkServices.GainMemory(subject,e.Thought,other);
        }
        public static void Produced(Pawn pawn,CraftingTrack track,Thing product,int job)
        {
            string role=ActiveRole(pawn,track);string fact;
            if(product.TryGetQuality(out QualityCategory q)&&q<=QualityCategory.Poor)fact="FlawedWork";
            else fact=new[]{"FineWork","DifficultOrder","MaterialMastery","CustomerDelight","ProudCraft"}[System.Math.Abs(job)%5];
            Give(pawn,null,role,fact,job);
        }
        public static void Sale(Pawn maker,Pawn customer,string kind,int order)
        {
            if(maker==null||customer==null||kind==null||!kind.StartsWith("craft."))return;var track=new CraftingTrack{Specialty=kind=="craft.weapon"?"Weaponsmith":kind=="craft.armor"?"Armorsmith":kind=="craft.mechanism"?"Mechanist":kind=="craft.couture"?"Couturier":kind=="craft.outfit"?"Outfitter":null,Route=kind=="craft.outfit"||kind=="craft.couture"?"Tailor":"Smith",Entry=kind=="craft.stone"?"Stoneworker":"Woodworker"};
            Give(customer,maker,ActiveRole(maker,track),new[]{"CraftPraised","HelpfulAdvice","SharedTechnique"}[System.Math.Abs(order)%3],order);
        }
    }
}
