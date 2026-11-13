using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    public static class EstablishmentPersonalityService
    {
        public static BusinessTemperament Temperament(Pawn pawn)
        {
            var traits=pawn?.story?.traits?.allTraits;
            int industry=traits?.FirstOrDefault(t=>t.def.defName=="Industriousness")?.Degree??0;
            int drugs=traits?.FirstOrDefault(t=>t.def.defName=="DrugDesire")?.Degree??0;
            return BusinessTemperament.FromTraits(traits?.Select(t=>t.def.defName),industry,drugs);
        }
        private static List<Pawn> Owners(Establishment e)
        {
            var c=HospitalityService.Component;
            if(e.Owner==EstablishmentOwner.Person){var p=HospitalityService.Pawn(e.OwnerId);return p==null||p.Dead?new List<Pawn>():new List<Pawn>{p};}
            if(e.Owner==EstablishmentOwner.Household)return (c.Registry.GetHousehold(e.OwnerId)?.ResidentIds??new List<int>()).Select(HospitalityService.Pawn).Where(p=>p!=null&&!p.Dead&&c.Registry.GetPerson(p.thingIDNumber)?.Juvenile==false).OrderBy(p=>p.thingIDNumber).ToList();
            return new List<Pawn>();
        }
        public static string OwnerMood(Establishment e)
        {
            var owners=Owners(e);return owners.Count==0?"No individual owner present":Math.Round(owners.Average(p=>p.needs?.mood?.CurLevelPercentage??.5f)*100)+"% average owner mood";
        }
        public static bool Related(Pawn owner,Pawn employee)
        {
            var r=HospitalityService.Component.Registry;var a=r.GetPerson(owner.thingIDNumber);var b=r.GetPerson(employee.thingIDNumber);if(a==null||b==null)return false;
            return a.HouseholdId>0&&a.HouseholdId==b.HouseholdId||a.PrimaryLineageId>0&&a.PrimaryLineageId==b.PrimaryLineageId||a.ParentIds.Contains(b.Id)||b.ParentIds.Contains(a.Id)||a.HasPartner(b.Id);
        }
        public static void Tick()
        {
            var c=HospitalityService.Component;var book=HospitalityService.Book;if(c==null||book==null)return;
            foreach(var e in book.Businesses)
            {
                var map=HospitalityService.Map(e);if(map==null)continue;
                var owners=Owners(e);var staff=e.Staff.Select(HospitalityService.Pawn).Where(p=>p!=null&&!p.Dead).ToList();
                if(e.Personality.LastCultureDay<HospitalityService.Day)
                {
                    var customers=book.Orders.Where(o=>o.Business==e.Id&&o.Stage==ServiceStage.Settled&&HospitalityService.Now-o.Created<=180000).Select(o=>o.Customer).Distinct().Take(20).Select(HospitalityService.Pawn).Where(p=>p!=null).ToList();
                    e.Personality.Develop(owners.Count>0?BusinessTemperament.Average(owners.Select(Temperament)):null,staff.Select(Temperament).ToList(),customers.Select(Temperament).ToList(),HospitalityService.Day,e.Quiet);
                }
                if(!e.Personality.OwnerAutonomy||!e.OpenAt(GenLocalDate.HourOfDay(map))||e.Personality.LastOwnerDay>=HospitalityService.Day)continue;
                var present=staff.Where(p=>HospitalityService.Working(p,e)&&HospitalityService.Ready(p)).ToList();
                var owner=owners.FirstOrDefault(p=>p.Spawned&&p.Map==map&&HospitalityService.Ready(p)&&!FrameworkServices.IsAvatar(p)&&present.Any(s=>s!=p&&p.Position.DistanceTo(s.Position)<=4));
                if(owner==null)continue;
                if(!book.Once("owner-attempt:"+e.Id+":"+HospitalityService.Day+":"+GenLocalDate.HourOfDay(map),HospitalityService.Day))continue;
                var available=present.Where(p=>p!=owner&&p.Position.DistanceTo(owner.Position)<=4&&GenSight.LineOfSight(owner.Position,p.Position,map)).ToList();if(available.Count==0)continue;
                // Affection biases attention; equal-treatment policy removes preferential family targeting.
                var target=available.OrderByDescending(p=>(e.Personality.EqualTreatment?0:Related(owner,p)?20:0)+owner.relations.OpinionOf(p)/5f+Rand.Value*30).First();
                var bank=FamilyFramework.Commerce.Bank;bool productive=e.Points.TryGetValue(target.thingIDNumber,out int points)&&points>0;
                var context=new OwnerSituation{Owner=Temperament(owner),Mood=owner.needs?.mood?.CurLevelPercentage??.5f,Opinion=owner.relations.OpinionOf(target),Productive=productive,EmployeeDistressed=(target.needs?.mood?.CurLevelPercentage??.5f)<.4f,Backlog=book.Orders.Count(o=>o.Business==e.Id&&o.Stage<ServiceStage.Delivered)>Math.Max(1,present.Count),WageDebt=bank!=null&&bank.Owed(bank.Business(e.Id))>0,Profitable=book.Orders.Any(o=>o.Business==e.Id&&o.Stage==ServiceStage.Settled&&!o.Refunded&&HospitalityService.Now-o.Created<60000),CanBonus=e.Personality.AllowBonuses&&bank!=null&&book.Withdrawable(e,bank)>=1,Relative=Related(owner,target),OtherStaffPresent=available.Any(p=>p!=target&&!Related(owner,p)),EqualTreatment=e.Personality.EqualTreatment,Nervous=HospitalityService.Trait(owner,"Nervous")||(owner.story?.traits?.allTraits.FirstOrDefault(t=>t.def.defName=="Nerves")?.Degree??0)<0};
                var action=OwnerDecisions.Choose(context,Rand.Value);if(action==null||owner.interactions?.TryInteractWith(target,InteractionDefOf.Chitchat)!=true)continue;
                e.Personality.LastOwnerDay=HospitalityService.Day;
                Apply(e,owner,target,action.Value,available,context);
            }
        }
        private static void Apply(Establishment e,Pawn owner,Pawn target,OwnerApproach action,List<Pawn> witnesses,OwnerSituation context)
        {
            var book=HospitalityService.Book;string fact,reason;int change;
            switch(action)
            {
                case OwnerApproach.Support:fact="OwnerSupport";reason="offered support because the employee was struggling";change=3;break;
                case OwnerApproach.Criticize:fact="OwnerCriticism";reason="criticized a real service backlog; temperament and current mood influenced the response";change=-4;break;
                case OwnerApproach.DiscussMargins:fact="OwnerMargins";reason=context.WageDebt?"discussed margins while business wages remained owed":"put profit at the center of a conversation after recent sales";change=-2;break;
                case OwnerApproach.Bonus:
                    var bank=FamilyFramework.Commerce.Bank;
                    if(bank==null||!e.Personality.AllowBonuses||book.Withdrawable(e,bank)<1)return;
                    book.Earn(e,target.thingIDNumber,1,BusinessCalendar.Next(HospitalityService.Day));
                    fact="OwnerBonus";reason="awarded one silver of discretionary pay for recorded work, due on payday";change=4;break;
                case OwnerApproach.FavorRelative:
                    fact="OwnerFamilyPraise";reason="singled out a working relative for special praise in front of colleagues";change=-2;
                    foreach(var other in witnesses.Where(p=>p!=target&&p!=owner&&!Related(owner,p)&&otherCanSee(p,owner)))HospitalityService.Event(other,owner,"OwnerFavorWitness");
                    break;
                case OwnerApproach.Celebrate:fact="OwnerCelebration";reason="celebrated recent sales and the employee's contribution";change=3;break;
                default:fact="OwnerPraise";reason="recognized the employee's recorded contribution";change=2;break;
            }
            HospitalityService.Event(target,owner,fact);
            e.Personality.StaffClimate=BusinessTemperament.Clamp(e.Personality.StaffClimate+change);
            e.Personality.LastReason=owner.LabelShort+" "+reason+" ("+target.LabelShort+").";book.Note(e.Name+": "+e.Personality.LastReason);
        }
        private static bool otherCanSee(Pawn p,Pawn owner)=>p.Spawned&&p.Map==owner.Map&&p.Position.DistanceTo(owner.Position)<=4&&GenSight.LineOfSight(p.Position,owner.Position,p.Map);
    }
}
