using System.Collections.Generic;

namespace FamilyLife.Domain
{
    public static class CraftingCareers
    {
        public const string Module="wils.crafting", Career="wils.crafting.craft";
        public const string Woodworker="wils.crafting.woodworker", Stoneworker="wils.crafting.stoneworker";
        public static string Role(string name)=>Module+"."+name.ToLowerInvariant();
        public static CareerDefinition Definition()=>new CareerDefinition
        {
            Id=Career,ModuleId=Module,LabelKey="WFL_Crafting_Career",
            Level2Branches=new Dictionary<string,CareerRule>
            {
                {"Smith",new CareerRule{GeneralXp=120,SpecialtyXp=60}.Skill("Crafting",8)},
                {"Tailor",new CareerRule{GeneralXp=120,SpecialtyXp=60}.Skill("Crafting",8)}
            },
            Specialties=new Dictionary<string,CareerRule>
            {
                {"Weaponsmith",new CareerRule{GeneralXp=250,SpecialtyXp=60}.Skill("Crafting",10).After("Smith")},
                {"Mechanist",new CareerRule{GeneralXp=250,SpecialtyXp=60}.Skill("Crafting",10).After("Smith")},
                {"Armorsmith",new CareerRule{GeneralXp=250,SpecialtyXp=60}.Skill("Crafting",10).After("Smith","Tailor")},
                {"Couturier",new CareerRule{GeneralXp=250,SpecialtyXp=60}.Skill("Crafting",10).Skill("Artistic",6).After("Tailor")},
                {"Outfitter",new CareerRule{GeneralXp=250,SpecialtyXp=60}.Skill("Crafting",10).After("Tailor")}
            }
        };
    }

    public sealed class CraftingProductFacts
    {
        public bool Weapon,Apparel,Armor,Prestige,Utility,Mechanical,Metal,Wood,Stone;
    }
    public sealed class CraftingTrack
    {
        public string Entry,Route,Specialty,Kind;
    }
    public static class CraftingClassifier
    {
        public static CraftingTrack Classify(CraftingProductFacts f)
        {
            if(f==null)return null;
            if(f.Weapon)return new CraftingTrack{Route="Smith",Specialty="Weaponsmith",Kind="craft.weapon"};
            if(f.Armor)return new CraftingTrack{Route=f.Metal?"Smith":"Tailor",Specialty="Armorsmith",Kind="craft.armor"};
            if(f.Mechanical)return new CraftingTrack{Route="Smith",Specialty="Mechanist",Kind="craft.mechanism"};
            if(f.Apparel&&f.Prestige)return new CraftingTrack{Route="Tailor",Specialty="Couturier",Kind="craft.couture"};
            if(f.Apparel)return new CraftingTrack{Route="Tailor",Specialty="Outfitter",Kind="craft.outfit"};
            if(f.Metal)return new CraftingTrack{Route="Smith",Kind="craft.smith"};
            if(f.Stone)return new CraftingTrack{Entry="Stoneworker",Kind="craft.stone"};
            if(f.Wood)return new CraftingTrack{Entry="Woodworker",Kind="craft.wood"};
            return null;
        }
    }
}
