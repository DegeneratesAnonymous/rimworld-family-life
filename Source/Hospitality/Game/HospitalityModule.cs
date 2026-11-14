using System;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    public sealed class HospitalityMod : Mod
    {
        public HospitalityMod(ModContentPack content) : base(content) { FamilyFramework.Install(new HospitalityModule()); }
    }
    public sealed class HospitalityModule : IFamilyModule
    {
        public string Id => CulinaryCareers.Module;
        public void Register()
        {
            FamilyFramework.Catalog.Register(new ModuleInfo { Id=Id,Name="Wil's Cooking & Hospitality",ApiVersion=FamilyFramework.ApiVersion,Requires=new[]{ModuleCatalog.FrameworkId} });
            CareerService.RegisterCareer(CulinaryCareers.Definition());
            CareerService.RegisterRole(new ProfessionRole{Id=CulinaryCareers.Cook,ModuleId=Id,Career=CulinaryCareers.Career,Level=1});
            CareerService.RegisterRole(new ProfessionRole{Id=CulinaryCareers.Chef,ModuleId=Id,Career=CulinaryCareers.Career,Level=2});
            foreach(var specialty in new[]{"Artisan","Bartender","Innkeeper"}) CareerService.RegisterRole(new ProfessionRole{Id=CulinaryCareers.Role(specialty),ModuleId=Id,Career=CulinaryCareers.Career,Level=3,Specialty=specialty});
            CareerService.Registered(Id);
            FamilyFramework.Pages.Register(new CommunityPage<Func<Rect,FamilyComponent,float>>{Id="wils.hospitality.establishments",ModuleId=Id,LabelKey="WFL_H_Establishments",Order=140,Render=EstablishmentPage.Draw,Status=()=>FamilyFramework.Commerce.Bank==null?1:0});
            FamilyFramework.Roster.Register(new WorkItem{Id="wils.hospitality.work",ModuleId=Id,Interval=600,Run=HospitalityService.Tick});
            FamilyFramework.ExternalCommerceAllowed.Add("wils.hospitality.external",Id,HospitalityInterop.Permit);
            FamilyFramework.TimedDepartureAllowed.Add("wils.hospitality.departure",Id,HospitalityVisitors.AllowDeparture);
            FamilyFramework.PropertyDamaged.Add("wils.hospitality.damage",Id,HospitalityNarrative.Damage);
            FamilyFramework.SocialInteraction.Add("wils.hospitality.interaction",Id,HospitalityStories.Interaction);
            FamilyFramework.BillIngredientAllowed.Add("wils.hospitality.ingredients",Id,HospitalityService.IngredientAllowed);
            FamilyFramework.FoodAccess.Add("wils.hospitality.food",Id,HospitalityService.FoodAllowed);
            FamilyFramework.ProductMade.Add("wils.hospitality.production",Id,HospitalityService.ProductMade);
            FamilyFramework.StackSplit.Add("wils.hospitality.split",Id,HospitalityService.Split);
            FamilyFramework.StackMerged.Add("wils.hospitality.merge",Id,HospitalityService.Merge);
            FamilyFramework.BillWorkAllowed.Add("wils.hospitality.bills",Id,HospitalityService.BillAllowed);
            FamilyFramework.LabourDone.Add("wils.hospitality.labour",Id,HospitalityService.OnLabour);
            FamilyFramework.PrivateLabour.Add("wils.hospitality.private",Id,HospitalityService.HandlesLabour);
            FamilyFramework.PrivateWork.Add("wils.hospitality.wages",Id,(pawn,career)=>career==CulinaryCareers.Career);
            FamilyFramework.PrivateWorkEvent.Add("wils.hospitality.workshop-wages",Id,(pawn,career,kind)=>kind!=null&&kind.StartsWith("craft.",StringComparison.Ordinal));
            FamilyFramework.FamilyEventsApplied.Add("wils.hospitality.family",Id,HospitalityService.FamilyEvents);
            FamilyFramework.Resets.Add("wils.hospitality.reset",Id,HospitalityService.Reset);
            FamilyFramework.Reconcilers.Add("wils.hospitality.reconcile",Id,HospitalityService.Reconcile);
            FamilyFramework.Removal.Add("wils.hospitality.removal",Id,c=>{
                var report=new CleanupReport(Id);
                report.Step("Finish delivered bills and release unfilled orders",()=>{HospitalityService.CloseAll(c);return "Saved establishments, careers and wage obligations retained";});
                return report;
            });
        }
    }
    [StaticConstructorOnStartup]
    public static class EstablishmentSelling
    {
        public static readonly WorkTypeDef WorkType;
        static EstablishmentSelling()
        {
            var own=DefDatabase<WorkTypeDef>.GetNamedSilentFail("WFL_Sell");
            // Resolve once after all definitions load. Do not merge unrelated Entertain/Wait jobs into Sell.
            var existing=DefDatabase<WorkTypeDef>.AllDefsListForReading.FirstOrDefault(d=>d!=own && (d.defName=="Sell"||string.Equals(d.labelShort,"Sell",StringComparison.OrdinalIgnoreCase)));
            WorkType=existing??own;
            if(existing!=null && own!=null)
            {
                foreach(var giver in DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(g=>g.workType==own)) { giver.workType=existing; if(!existing.workGiversByPriority.Contains(giver))existing.workGiversByPriority.Add(giver); }
                own.visible = false;
                own.workGiversByPriority.Clear();
                existing.workGiversByPriority.Sort((a,b)=>b.priorityInType.CompareTo(a.priorityInType));
                Log.Message("[Wil's Cooking & Hospitality] Using existing Sell work type: "+existing.defName);
            }
        }
    }
}
