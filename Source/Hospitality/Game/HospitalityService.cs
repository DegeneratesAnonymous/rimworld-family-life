using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace FamilyLife
{
    public static class HospitalityService
    {
        public static FamilyComponent Component=>FamilyComponent.Current;
        public static EstablishmentBook Book=>Component?.Registry.Establishments();
        public static int Now=>FrameworkServices.Now;
        public static int Day=>FrameworkServices.EconomyDay;
        private static int rotation;
        [ThreadStatic] private static bool menuProbe;
        private static readonly Dictionary<int,Thing> things=new Dictionary<int,Thing>();
        private static int cachedTick=-1;
        public static void Reset(){things.Clear();cachedTick=-1;rotation=0;}
        public static Map Map(Establishment e)=>Find.Maps.FirstOrDefault(m=>m.uniqueID==e.Map);
        public static Pawn Pawn(int id)=>Thing(id) as Pawn??Component?.TryGetPawn(id);
        public static Thing Thing(int id)
        {
            if(id==0)return null;
            if(cachedTick<0||Now-cachedTick>=120)
            {
                cachedTick=Now;things.Clear();
                foreach(var map in Find.Maps)
                {
                    foreach(var thing in map.listerThings.AllThings)things[thing.thingIDNumber]=thing;
                    foreach(var p in map.mapPawns.AllPawnsSpawned){things[p.thingIDNumber]=p;if(p.inventory!=null)foreach(var t in p.inventory.innerContainer)things[t.thingIDNumber]=t;if(p.carryTracker?.CarriedThing!=null)things[p.carryTracker.CarriedThing.thingIDNumber]=p.carryTracker.CarriedThing;}
                }
            }
            return things.TryGetValue(id,out var found)?found:null;
        }
        public static bool InArea(Thing t,Establishment e,bool sales)
        {
            if(t==null||!t.Spawned||t.Map.uniqueID!=e.Map)return false;
            var cells=sales?e.StockCells:e.StorageCells;var containers=sales?e.StockContainers:e.StorageContainers;
            if(cells.Contains(t.Map.cellIndices.CellToIndex(t.Position)))return true;
            return t.Position.GetThingList(t.Map).Any(x=>containers.Contains(x.thingIDNumber));
        }
        public static bool InBusiness(Thing t,Establishment e)=>InArea(t,e,true)||InArea(t,e,false);
        public static bool Working(Pawn pawn,Establishment e)
        {
            if(pawn==null||!pawn.Spawned||pawn.Map.uniqueID!=e.Map||pawn.Dead||pawn.Downed||pawn.Drafted||pawn.InMentalState||!e.Staff.Contains(pawn.thingIDNumber)||!e.OpenAt(GenLocalDate.HourOfDay(pawn)))return false;
            return pawn.Faction==Faction.OfPlayer&&pawn.timetable?.CurrentAssignment==TimeAssignmentDefOf.Work;
        }
        public static Establishment Employer(Pawn p)=>p==null||Book==null?null:Book.Businesses.FirstOrDefault(e=>Working(p,e));
        public static IEnumerable<Pawn> Coverage(Establishment e)=>e.Staff.Select(Pawn).Where(p=>Working(p,e)&&Ready(p)&&Priority(p,EstablishmentSelling.WorkType)>0);
        public static bool Ready(Pawn p)=>p!=null&&!p.Dead&&!p.Downed&&!p.Drafted&&!p.InMentalState&&!PawnReadinessApi.Danger(p.Map)&&FrameworkServices.NeedsReason(p)==null;
        public static int Priority(Pawn p,WorkTypeDef def)=>def==null||p.workSettings==null||!p.workSettings.WorkIsActive(def)?0:p.workSettings.GetPriority(def);
        public static bool HandlesLabour(LabourReport r)
        {
            return Book!=null&&Book.Businesses.Any(e=>e.Staff.Contains(r.Pawn.thingIDNumber)&&e.Benches.Contains(r.ThingId));
        }
        public static void OnLabour(LabourReport r)
        {
            if(r.RecipeSkill!="Cooking")return;
            var e=Book?.Businesses.FirstOrDefault(b=>b.Map==r.Pawn.Map?.uniqueID&&b.Benches.Contains(r.ThingId));
            if(e==null)CareerService.Report(Component,r.Pawn,CulinaryCareers.Career,null,"cook.bill","cooking:"+r.JobId,3,Now);
        }
        public static bool IngredientAllowed(Thing ingredient,Thing bench)
        {
            if(Book==null||ingredient==null||bench==null)return true;
            var owner=Book.Businesses.FirstOrDefault(e=>InBusiness(ingredient,e));
            var kitchen=Book.Businesses.FirstOrDefault(e=>e.Benches.Contains(bench.thingIDNumber));
            return kitchen==null?owner==null:owner==kitchen;
        }
        public static bool FoodAllowed(Pawn pawn,Thing food)
        {
            if(menuProbe||Book==null||food==null)return true;
            if(Book.Orders.Any(o=>o.Thing==food.thingIDNumber&&o.Customer!=pawn?.thingIDNumber&&o.Stage<ServiceStage.Settled))return false;
            var owner=Book.Businesses.FirstOrDefault(e=>InBusiness(food,e));
            if(owner==null)return true;
            return Book.Orders.Any(o=>o.Customer==pawn?.thingIDNumber&&o.Thing==food.thingIDNumber&&o.Stage<ServiceStage.Settled);
        }
        public static bool BillAllowed(Pawn p,Thing bench)
        {
            if(Book==null||bench==null||!(bench is IBillGiver))return true;
            var owns=Book.Businesses.FirstOrDefault(e=>e.Map==bench.Map?.uniqueID&&e.Benches.Contains(bench.thingIDNumber));
            var employer=Employer(p);
            if(owns!=null)return employer==owns&&FamilyFramework.Commerce.Bank!=null;
            if(employer==null||Priority(p,DefDatabase<WorkTypeDef>.GetNamed("Cooking"))==0)return true;
            // Only restrict communal cooking benches; crafting remains independent.
            if(!((IBillGiver)bench).BillStack.Bills.Any(b=>b.recipe?.workSkill==SkillDefOf.Cooking))return true;
            return CookingJob(p,employer)==null;
        }
        public static Job CookingJob(Pawn p,Establishment e)
        {
            foreach(var def in DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(d=>d.workType==DefDatabase<WorkTypeDef>.GetNamed("Cooking")))
            {
                if(!(def.Worker is WorkGiver_DoBill giver))continue;
                foreach(int id in e.Benches)
                {
                    var bench=Thing(id);if(bench==null||!bench.Spawned)continue;
                    var job=giver.JobOnThing(p,bench,false);if(job==null)continue;job.workGiverDef=def;
                    return job;
                }
            }
            return null;
        }
        public static Job CraftingJob(Pawn p,Establishment e)
        {
            if(e?.Kind!=EstablishmentKind.Shop||FamilyFramework.WorkshopProducts.Enabled().Count()==0)return null;
            var crafting=DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting");if(crafting==null)return null;
            foreach(var def in DefDatabase<WorkGiverDef>.AllDefsListForReading.Where(d=>d.workType==crafting))
            {
                if(!(def.Worker is WorkGiver_DoBill giver))continue;
                foreach(int id in e.Benches){var bench=Thing(id);if(bench==null||!bench.Spawned)continue;var job=giver.JobOnThing(p,bench,false);if(job==null)continue;job.workGiverDef=def;return job;}
            }
            return null;
        }
        public static void ProductMade(Pawn p,Thing bench,Thing product,int job)
        {
            if(p==null||product==null||bench==null)return;
            var e=Book?.Businesses.FirstOrDefault(b=>b.Benches.Contains(bench.thingIDNumber)&&b.Staff.Contains(p.thingIDNumber));if(e==null)return;
            if(p.CurJob?.bill?.recipe?.workSkill==SkillDefOf.Cooking)
            {
                bool artisan=CareerService.IsAssignedSpecialist(p,CulinaryCareers.Career,"Artisan");bool failed=artisan&&Rand.Chance(.08f);
                Component.Registry.Production().Lots.Add(new ProductLot{ExperimentFailed=failed,Id=Component.Registry.Production().NextId++,Thing=product.thingIDNumber,Worker=p.thingIDNumber,Business=e.Id,Count=product.stackCount,Day=Day,Presentation=(int)((failed?.6f:1f)*CulinaryCareers.Presentation(CareerService.SkillsOf(p)("Cooking"),CareerService.SkillsOf(p)("Artistic"),Component.Registry.Careers().IsSpecialist(p.thingIDNumber,CulinaryCareers.Career,"Artisan"))*100),Origin="recipe:"+job,Career=CulinaryCareers.Career,Kind="cook.deposit"});
                return;
            }
            if(e.Kind!=EstablishmentKind.Shop)return;
            foreach(var classify in FamilyFramework.WorkshopProducts.Enabled())
            {
                WorkshopProductProfile profile=null;try{profile=classify(p,bench,product);}catch(Exception ex){Log.Warning("[Wil's Cooking & Hospitality] Workshop product classifier skipped: "+ex.Message);}
                if(profile==null||string.IsNullOrEmpty(profile.Career))continue;
                Component.Registry.Production().Lots.Add(new ProductLot{Id=Component.Registry.Production().NextId++,Thing=product.thingIDNumber,Worker=p.thingIDNumber,Business=e.Id,Count=product.stackCount,Day=Day,Presentation=profile.Presentation,ValuePercent=profile.ValuePercent,Origin="recipe:"+job,Career=profile.Career,Specialty=profile.Specialty,Kind=profile.Kind});return;
            }
        }
        public static void Split(Thing from,Thing to){Component?.Registry.Production().Move(from.thingIDNumber,to.thingIDNumber,to.stackCount);cachedTick=-1;}
        public static void Merge(Thing from,Thing to,int count){Component?.Registry.Production().Move(from.thingIDNumber,to.thingIDNumber,count);cachedTick=-1;}
        public static string Role(Pawn p)
        {
            if(p==null)return "Cook";var ledger=Component.Registry.Careers();var employer=Employer(p)??Book.Businesses.FirstOrDefault(e=>e.Staff.Contains(p.thingIDNumber));
            if(employer?.Kind==EstablishmentKind.Inn&&CareerService.IsAssignedSpecialist(p,CulinaryCareers.Career,"Innkeeper"))return "Innkeeper";
            if(employer?.Kind==EstablishmentKind.Tavern&&CareerService.IsAssignedSpecialist(p,CulinaryCareers.Career,"Bartender"))return "Bartender";
            foreach(var specialty in new[]{"Artisan","Innkeeper","Bartender"})if(CareerService.IsAssignedSpecialist(p,CulinaryCareers.Career,specialty))return specialty;
            return ledger.IsQualified(p.thingIDNumber,CulinaryCareers.Career)?"Chef":"Cook";
        }
        public static void Event(Pawn subject,Pawn other,params string[] facts)
        {
            if(subject==null||Book==null)return;
            EventAs(facts.Contains("SupportiveTalk")&&other!=null?Role(other):Role(subject),subject,other,facts);
        }
        public static void EventAs(string role,Pawn subject,Pawn other,params string[] facts)
        {
            if(subject==null||Book==null)return;var possible=HospitalityEvents.Eligible(role,new HashSet<string>(facts),other!=null).ToList();
            // At most one mood and one relationship memory per real event, plus persisted cooldowns.
            foreach(var group in possible.GroupBy(e=>e.Opinion==0))
            {
                var eligible=group.Where(e=>!Book.Memories.TryGetValue(subject.thingIDNumber+":"+e.Id+":"+(other?.thingIDNumber??0),out int d)||Day-d>=3).ToList();if(eligible.Count==0)continue;
                var chosen=eligible[Rand.Range(0,eligible.Count)];if(!Book.Memory(subject.thingIDNumber,chosen.Id,other?.thingIDNumber??0,Day))continue;
                FrameworkServices.GainMemory(subject,chosen.Thought,other);Book.Note(subject.LabelShort+": "+chosen.Label);
            }
        }
        public static void Tick(int now)
        {
            if(Component==null||Book==null)return;
            var bank=FamilyFramework.Commerce.Bank;
            foreach(var e in Book.Businesses){Book.Payday(e,bank,Day);ReconcileOwner(e,bank);}
            foreach(var lot in Component.Registry.Production().Lots.ToList())
            {
                var e=Book.ById(lot.Business);var product=Thing(lot.Thing);
                if(!lot.Credited&&e!=null&&InBusiness(product,e)&&Book.Credit(e,lot.Worker,ContributionKind.Production,lot.Origin+":lot:"+lot.Id,Day,lot.Count))
                {
                    lot.Credited=true;
                    var p=Pawn(lot.Worker);if(p!=null){string specialty=lot.Specialty;if(lot.Career==CulinaryCareers.Career)specialty=CareerService.SkillsOf(p)("Artistic")>=6?"Artisan":null;CareerService.Report(Component,p,lot.Career??CulinaryCareers.Career,specialty,lot.Kind??"cook.deposit",lot.Origin,5,now);if((lot.Career??CulinaryCareers.Career)==CulinaryCareers.Career&&specialty!=null&&Role(p)=="Artisan")Event(p,null,lot.ExperimentFailed?"SignatureFailed":"SignatureSuccess",lot.ExperimentFailed?"ExperimentFailed":"CreativeOrder");}
                }
                if(product==null||product.Destroyed)Component.Registry.Production().Lots.Remove(lot);
            }
            var production=Component.Registry.Production();
            foreach(var group in production.Lots.Where(l=>l.Credited).GroupBy(l=>new {l.Thing,l.Worker,l.Business,l.Presentation,l.ExperimentFailed,l.Career,l.Specialty,l.Kind}).Where(g=>g.Count()>1).ToList())
            {var first=group.First();first.Count=group.Sum(l=>l.Count);foreach(var other in group.Skip(1).ToList())production.Lots.Remove(other);}
            ReconcileOrders(bank);
            foreach(var e in Book.Businesses)
            {
                foreach(var p in e.Staff.Select(Pawn).Where(p=>Working(p,e)))
                {
                    if(bank==null||!Ready(p))continue;
                    bool commercial=p.CurJob?.targetA.Thing!=null&&e.Benches.Contains(p.CurJob.targetA.Thing.thingIDNumber);
                    if(commercial)Book.WorkTime(e,p.thingIDNumber,600,e.Id+":"+p.thingIDNumber+":"+now,Day);
                }
                ObserveTrouble(e);
            }
            // Budget new customers: one establishment and at most four candidates per roster pass.
            var active=Book.Businesses.Where(e=>e.Enabled&&Map(e)!=null).ToList();if(active.Count>0&&bank!=null){var e=active[rotation++%active.Count];SeekCustomers(e);}
            HospitalityVisitors.Tick();HospitalityStories.Tick();HospitalityNarrative.Tick();Book.Prune(Day,Now);
        }
        private static void ObserveTrouble(Establishment e)
        {
            var map=Map(e);if(map==null)return;
            var present=map.mapPawns.AllPawnsSpawned.Where(p=>e.ServiceCells.Contains(map.cellIndices.CellToIndex(p.Position))).Take(40).ToList();
            bool fight=present.Any(p=>p.MentalStateDef==MentalStateDefOf.SocialFighting);
            if(fight&&Book.Once("fight:"+e.Id+":"+(Now/2500),Day))
            {
                e.Review(e.Food,e.Service,e.Comfort,e.Value,10,90);Book.Note(e.Name+": a witnessed social fight hurt safety and made its atmosphere rowdier.");
                foreach(var p in present.Where(p=>p.Awake()))Event(p,null,"WitnessedFight");
            }
        }
        private static void ReconcileOrders(ICommerceBank bank)
        {
            foreach(var order in Book.Orders.Where(o=>o.Stage<ServiceStage.Settled).ToList())
            {
                var customer=Pawn(order.Customer);var e=Book.ById(order.Business);
                var food=Thing(order.Thing);bool ruined=order.Stage<ServiceStage.Delivered&&food?.TryGetComp<CompRottable>()?.Stage==RotStage.Rotting;
                if(ruined){var maker=Pawn(order.Maker);if(maker!=null&&Role(maker)=="Artisan"&&maker.Spawned&&maker.Map==food.Map&&maker.Position.DistanceTo(food.Position)<6&&GenSight.LineOfSight(maker.Position,food.Position,food.Map))EventAs("Artisan",maker,null,"MealRuined");CancelOrder(order);continue;}
                bool invalid=customer==null||!customer.Spawned||customer.Dead||e==null||Map(e)==null;
                if(order.Stage<ServiceStage.Delivered&&(invalid||Now-order.Created>10000||!e.Enabled||bank==null))
                {
                    CancelOrder(order);if(order.Worker>0)Event(Pawn(order.Worker),customer,"OrdersFailed");continue;
                }
                if(order.Stage==ServiceStage.Delivering)
                {
                    var worker=Pawn(order.Worker);
                    if(worker?.CurJob?.def.defName!="WFL_H_Service"||worker.CurJob.count!=order.Id){order.Stage=ServiceStage.Ordered;order.Worker=0;}
                }
                if(order.Stage>=ServiceStage.Delivered&&(invalid||customer.Drafted||customer.InMentalState||Now-order.DeliveredTick>15000))
                {
                    order.Stage=ServiceStage.Consumed;Settle(order,true);
                }
            }
        }
        public static IEnumerable<Thing> Stock(Establishment e)
        {
            var map=Map(e);if(map==null)return Enumerable.Empty<Thing>();
            return map.listerThings.AllThings.Where(t=>t.def.category==ThingCategory.Item&&t.def!=ThingDefOf.Silver&&!t.IsForbidden(Faction.OfPlayer)&&InArea(t,e,true)&&!Book.Orders.Any(o=>o.Thing==t.thingIDNumber&&o.Stage<ServiceStage.Settled));
        }
        private static void SeekCustomers(Establishment e)
        {
            var map=Map(e);if(map==null||!e.OpenAt(GenLocalDate.HourOfDay(map))||!Coverage(e).Any()||Thing(e.Counter)==null||e.ServiceCells.Count==0||PawnReadinessApi.Danger(map))return;
            var people=map.mapPawns.AllPawnsSpawned.Where(p=>p.RaceProps.Humanlike&&!p.IsPrisoner&&!p.Downed&&!p.Drafted&&!p.InMentalState&&!p.HostileTo(Faction.OfPlayer)&&!e.Staff.Contains(p.thingIDNumber)).OrderBy(p=>(p.thingIDNumber+Now/600)%197).Take(4);
            foreach(var p in people)
            {
                if(Book.Orders.Any(o=>o.Customer==p.thingIDNumber&&o.Stage<ServiceStage.Settled)||p.jobs==null||!p.Awake()||FrameworkServices.IsAvatar(p))continue;
                bool resident=p.Faction==Faction.OfPlayer;if(resident?!e.Residents:!e.Visitors)continue;
                if(HospitalityInterop.Busy(p)||!HospitalityNarrative.Admission(e,p))continue;
                if(resident&&p.timetable?.CurrentAssignment==TimeAssignmentDefOf.Work)continue;
                if(p.GetLord()?.LordJob is LordJob_EstablishmentVisit){}
                else if(p.CurJob!=null&&p.CurJob.def!=JobDefOf.Wait_Wander&&p.CurJob.def!=JobDefOf.GotoWander&&p.CurJob.def!=JobDefOf.Wait_MaintainPosture)continue;
                var candidates=Stock(e).Where(t=>Accepts(p,t,e)).Take(20).ToList();if(candidates.Count==0)continue;
                var item=candidates.Where(t=>SalePrice(t,e)<=PurchasingPower(p,e,resident)).OrderByDescending(t=>SalePrice(t,e)).FirstOrDefault();if(item==null)continue;int count=1;
                int price=SalePrice(item,e);
                var bank=FamilyFramework.Commerce.Bank;var bookings=Component.Registry.HospitalityBookings();var booking=bookings.For(e.Id,Component.Registry.GetPerson(p.thingIDNumber)?.HouseholdId??0,Now);
                string account=resident?SpendingAccount(p,e,price):null;
                int budget=booking!=null?booking.Budget-booking.Committed:resident?bank?.Available(account)??0:Math.Max(0,Cash(p)-HospitalityVisitors.JourneyReserve(p));if(budget<price)continue;
                bool communal=map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree).Any(t=>t.def.ingestible!=null&&t.Spawned&&!Book.Businesses.Any(b=>InBusiness(t,b)));
                float utility=VisitorDecisions.VisitUtility(p.needs.food==null?0:1-p.needs.food.CurLevelPercentage,p.needs.joy==null?0:1-p.needs.joy.CurLevelPercentage,.3f,Mathf.Clamp01(e.Reputation/100f*.7f+EstablishmentPersonalityService.Temperament(p).Affinity(e.Personality.Culture)*.3f),price,budget,p.Position.DistanceTo(Thing(e.Counter).Position)/100f,communal);
                if(booking!=null)utility+=.5f;
                if(utility<.2f||!Rand.Chance(booking!=null?.6f:.25f))continue;
                var seats=e.ServiceCells.Select(map.cellIndices.IndexToCell).Where(c=>c.Standable(map)&&p.CanReach(c,PathEndMode.OnCell,Danger.Some)&&!Book.Orders.Any(o=>o.Stage<ServiceStage.Settled&&o.TableCell==map.cellIndices.CellToIndex(c)&&o.Business==e.Id)).OrderBy(c=>c.GetThingList(map).Any(t=>t.def.building?.isSittable==true)?0:1).Take(30).ToList();
                if(seats.Count==0)continue;
                var order=Book.Begin(e,p.thingIDNumber,item.thingIDNumber,price,Now,account,!resident,bank,count,booking,bookings);if(order==null)continue;
                if(order.Visitor&&!VisitorCommerce.Reserve(order,p)){CancelOrder(order);continue;}
                order.TableCell=map.cellIndices.CellToIndex(seats[0]);
                var lot=Component.Registry.Production().Lots.FirstOrDefault(l=>l.Thing==item.thingIDNumber&&l.Business==e.Id);if(lot!=null){order.Maker=lot.Worker;order.Presentation=lot.Presentation;}
                order.DietRequest=item.def.ingestible!=null&&Stock(e).Any(t=>t!=item&&t.def.ingestible!=null&&!Accepts(p,t,e));
                if(booking==null&&resident&&account!=bank.Person(p.thingIDNumber)){string key=account+":"+Day;Book.HouseholdSpent[key]=(Book.HouseholdSpent.TryGetValue(key,out int spent)?spent:0)+price;order.AllowanceAccount=account;order.AllowanceDay=Day;}
                var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WFL_H_Customer"),seats[0],Thing(e.Counter));job.count=order.Id;
                if(!p.jobs.TryTakeOrderedJob(job))CancelOrder(order);
            }
        }
        private static int SalePrice(Thing item,Establishment e)
        {
            var lot=Component.Registry.Production().Lots.FirstOrDefault(l=>l.Thing==item.thingIDNumber&&l.Business==e.Id);int specialist=lot?.ValuePercent??100;
            return Math.Max(1,(int)Math.Round(item.MarketValue*e.PricePercent/100f*specialist/100f));
        }
        private static bool Accepts(Pawn p,Thing t,Establishment e)
        {
            if(e.Kind==EstablishmentKind.Shop)return t.def.category==ThingCategory.Item&&WorkshopAccepts(e,t);
            if(t.def.ingestible==null||!t.IngestibleNow)return false;
            bool eats;menuProbe=true;try{eats=FoodUtility.WillEat(p,t,null,false);}finally{menuProbe=false;}if(!eats)return false;
            if(t.def.IsDrug)
            {
                if(!e.Alcohol||p.ageTracker.AgeBiologicalYearsFloat<18||p.drugs?.CurrentPolicy==null)return false;
                var policy=p.drugs.CurrentPolicy[t.def];if(!policy.allowedForJoy)return false;
                if(e.CutOffDrunks&&p.health.hediffSet.hediffs.Any(h=>h.def.defName=="AlcoholHigh"&&h.Severity>=.35f))return false;
                return (t.def.ingestible.foodType & FoodTypeFlags.Liquor)!=0;
            }
            return t.def.ingestible.foodType!=FoodTypeFlags.Corpse&&t.def.ingestible.preferability>=FoodPreferability.MealAwful;
        }
        private static bool WorkshopAccepts(Establishment e,Thing t)
        {
            if(e.WorkshopFocus==WorkshopFocus.General)return true;var lot=Component.Registry.Production().Lots.FirstOrDefault(l=>l.Thing==t.thingIDNumber&&l.Business==e.Id);string kind=lot?.Kind??"";
            switch(e.WorkshopFocus)
            {
                case WorkshopFocus.Smithy:return kind=="craft.smith"||kind=="craft.weapon"||kind=="craft.armor";
                case WorkshopFocus.Tailor:return kind=="craft.outfit"||kind=="craft.couture"||kind=="craft.armor";
                case WorkshopFocus.Armory:return kind=="craft.weapon"||kind=="craft.armor";
                case WorkshopFocus.Mechanist:return kind=="craft.mechanism";
                case WorkshopFocus.Boutique:return kind=="craft.couture"||kind=="craft.outfit";
                default:return true;
            }
        }
        private static int PurchasingPower(Pawn p,Establishment e,bool resident)
        {
            var booking=Component.Registry.HospitalityBookings().For(e.Id,Component.Registry.GetPerson(p.thingIDNumber)?.HouseholdId??0,Now);if(booking!=null)return booking.Budget-booking.Committed;
            if(!resident)return Math.Max(0,Cash(p)-HospitalityVisitors.JourneyReserve(p));
            var bank=FamilyFramework.Commerce.Bank;if(bank==null)return 0;int personal=bank.Available(bank.Person(p.thingIDNumber));int home=Component.Registry.GetPerson(p.thingIDNumber)?.HouseholdId??0;if(home<=0)return personal;
            string account=bank.Household(home);int spent=Book.HouseholdSpent.TryGetValue(account+":"+Day,out int n)?n:0;return Math.Max(personal,Math.Min(bank.Available(account),Math.Max(0,e.HouseholdBudget-spent)));
        }
        public static string SpendingAccount(Pawn p,Establishment e,int price)
        {
            var bank=FamilyFramework.Commerce.Bank;if(bank==null)return null;string personal=bank.Person(p.thingIDNumber);if(bank.Available(personal)>=price)return personal;
            int home=Component.Registry.GetPerson(p.thingIDNumber)?.HouseholdId??0;if(home<=0)return personal;
            string account=bank.Household(home),key=account+":"+Day;int spent=Book.HouseholdSpent.TryGetValue(key,out int n)?n:0;
            return spent+price<=e.HouseholdBudget?account:personal;
        }
        public static int Cash(Pawn p)=>VisitorCommerce.Available(p);
        public static bool CancelOrder(ServiceOrder order)
        {
            bool cancelled=Book.Cancel(order,FamilyFramework.Commerce.Bank,Component.Registry.HospitalityBookings());if(cancelled)VisitorCommerce.Release(order.Key);return cancelled;
        }
        public static bool Settle(ServiceOrder order,bool remote=false)
        {
            if(order==null)return false;if(order.Stage==ServiceStage.Settled)return true;
            var e=Book.ById(order.Business);var bank=FamilyFramework.Commerce.Bank;if(e==null||bank==null)return false;
            bool result=Book.Checkout(order,bank,Day,o=>bank.ExternalSale("visitor:"+o.Key,bank.Business(e.Id),o.Price,Day,()=>MoveCash(Pawn(o.Customer),o.Price,e,o.Key)));
            if(result)
            {
                VisitorCommerce.Release(order.Key);
                var customer=Pawn(order.Customer);HospitalityStories.BookingServed(order,customer);var staff=Pawn(order.Worker)??Coverage(e).FirstOrDefault();
                if(staff!=null){Book.Credit(e,staff.thingIDNumber,ContributionKind.Checkout,"checkout:"+order.Id,Day);CareerService.Report(Component,staff,CulinaryCareers.Career,e.Kind==EstablishmentKind.Inn?"Innkeeper":e.Kind==EstablishmentKind.Tavern?"Bartender":null,"service.checkout","checkout:"+order.Id,4,Now);if(customer!=null)Social(staff,customer);}
                int service=remote&&customer!=null&&(customer.Drafted||customer.Downed||PawnReadinessApi.Danger(customer.Map))?70:Math.Max(10,90-(Now-order.Created)/300);float presentation=order.Presentation>0?order.Presentation/100f:8;
                service=Mathf.Clamp(service+(e.Personality.StaffClimate-50)/10,10,100);
                if(staff!=null&&e.Kind==EstablishmentKind.Tavern&&CareerService.IsAssignedSpecialist(staff,CulinaryCareers.Career,"Bartender"))service=Math.Min(100,service+10);
                if(staff!=null&&e.Kind==EstablishmentKind.Inn&&CareerService.IsAssignedSpecialist(staff,CulinaryCareers.Career,"Innkeeper"))service=Math.Min(100,service+10);
                if(staff!=null)
                {
                    string needed=e.Kind==EstablishmentKind.Inn?"Innkeeper":e.Kind==EstablishmentKind.Tavern?"Bartender":null;
                    bool professional=needed!=null?Component.Registry.Careers().IsSpecialist(staff.thingIDNumber,CulinaryCareers.Career,needed):Component.Registry.Careers().IsQualified(staff.thingIDNumber,CulinaryCareers.Career);
                    service=Math.Min(service,professional?100:65);
                }
                int comfort=Comfort(e)+(staff!=null&&e.Kind==EstablishmentKind.Inn&&CareerService.IsAssignedSpecialist(staff,CulinaryCareers.Career,"Innkeeper")?10:0);
                e.Review(Math.Min(100,45+(int)presentation*2),service,Math.Min(100,comfort),Math.Max(0,150-e.PricePercent),85,e.Quiet?Math.Max(0,e.Rowdiness-15):Math.Max(0,e.Rowdiness-2));
                string visit="regular:"+e.Id+":"+order.Customer;bool regular=Book.Seen.ContainsKey(visit);Book.Seen[visit]=Day;
                if(customer!=null){Event(customer,staff,service<40?"CustomerUnhappy":regular?"HappyRegular":"CustomerHappy");FamilyVisit(e,customer,staff);}
                if(staff!=null)Event(staff,customer,service<40&&regular?"LostRegular":regular?"HappyRegular":"Compliment");
                if(order.Maker>0)Event(Pawn(order.Maker),customer,"Compliment");
                HospitalityNarrative.OnSettled(order,customer,staff,service);
                var guest=Book.Guests.FirstOrDefault(g=>g.Pawn==order.Customer);if(guest!=null)guest.PersonalExperience=(guest.PersonalExperience+service/100f)/2;
                Book.Note(e.Name+": "+order.Price+" silver collected"+(remote?" (interrupted checkout)":""));
            }
            return result;
        }
        public static bool MoveCash(Pawn p,int amount,Establishment e,string reservationKey=null)=>VisitorCommerce.Move(p,amount,e,reservationKey);
        public static int Comfort(Establishment e)
        {
            var map=Map(e);if(map==null||e.ServiceCells.Count==0)return 10;
            return Mathf.Clamp(40+e.ServiceCells.Take(100).Select(map.cellIndices.IndexToCell).Count(c=>c.GetThingList(map).Any(t=>t.def.building?.isSittable==true))*5,10,95);
        }
        public static bool Social(Pawn staff,Pawn customer)
        {
            if(!HospitalityNarrative.Near(staff,customer,5))return false;
            return staff.interactions?.TryInteractWith(customer,InteractionDefOf.Chitchat)==true;
        }
        private static void FamilyVisit(Establishment e,Pawn customer,Pawn staff)
        {
            var person=Component.Registry.GetPerson(customer.thingIDNumber);if(person==null)return;
            if(staff!=null&&Component.Registry.GetPerson(staff.thingIDNumber)?.HouseholdId==person.HouseholdId&&person.HouseholdId>0)Event(customer,staff,"KinVisit");
            bool shared=person.HouseholdId>0&&Book.Orders.Any(o=>o.Id!=0&&o.Business==e.Id&&o.Customer!=person.Id&&o.Stage>=ServiceStage.Delivered&&o.Stage<=ServiceStage.Settled&&Now-o.DeliveredTick<5000&&Component.Registry.GetPerson(o.Customer)?.HouseholdId==person.HouseholdId);
            if(shared)Event(customer,staff,"FamilyOuting");
        }
        public static string OwnerAccount(Establishment e,ICommerceBank bank)=>e.Owner==EstablishmentOwner.Community?"c:1":e.Owner==EstablishmentOwner.Person?bank.Person(e.OwnerId):bank.Household(e.OwnerId);
        public static void Fund(Establishment e,int amount)
        {
            var bank=FamilyFramework.Commerce.Bank;if(bank==null)return;if(!bank.Transfer("fund:"+e.Id+":"+Now,OwnerAccount(e,bank),bank.Business(e.Id),amount,Day))Messages.Message("Owner does not have enough spendable silver.",MessageTypeDefOf.RejectInput,false);
        }
        public static void Withdraw(Establishment e)
        {
            var bank=FamilyFramework.Commerce.Bank;if(bank==null)return;int amount=Book.Withdrawable(e,bank);if(amount>0)bank.Transfer("profit:"+e.Id+":"+Now,bank.Business(e.Id),OwnerAccount(e,bank),amount,Day,true);
        }
        public static void OwnerPreset(Establishment e)
        {
            var owner=Pawn(e.OwnerId);if(e.Owner!=EstablishmentOwner.Person||owner==null)return;
            bool greedy=Trait(owner,"Greedy"),kind=Trait(owner,"Kind");e.BasePayMilli=greedy?125:kind?400:250;e.StaffShare=greedy?10:kind?30:20;e.PricePercent=greedy?125:kind?90:100;
            Book.Note(e.Name+": owner personality pay preset applied to future work.");
        }
        public static bool Trait(Pawn p,string name)=>p?.story?.traits?.allTraits.Any(t=>t.def.defName==name)==true;
        private static void ReconcileOwner(Establishment e,ICommerceBank bank)
        {
            int deceased=e.Owner==EstablishmentOwner.Person?e.OwnerId:0;
            if(bank==null)return;string old=OwnerAccount(e,bank),successor=bank.Successor(old);if(successor==old)return;
            if(successor.StartsWith("p:")&&int.TryParse(successor.Substring(2),out int person)){e.Owner=EstablishmentOwner.Person;e.OwnerId=person;Event(Pawn(person),null,"Succession");}
            else if(successor.StartsWith("h:")&&int.TryParse(successor.Substring(2),out int house)){e.Owner=EstablishmentOwner.Household;e.OwnerId=house;}
            else {e.Owner=EstablishmentOwner.Community;e.OwnerId=0;}
            Book.Note(e.Name+": ownership followed the recorded estate / household successor.");if(deceased>0)HospitalityNarrative.Inheritance(e,deceased);
        }
        public static void FamilyEvents(FamilyComponent c,IList<FamilyEvent> events,int now)
        {
            foreach(var evt in events.Where(x=>x.Kind==FamilyEventKind.Reunion||x.Kind==FamilyEventKind.Anniversary||x.Kind==FamilyEventKind.MarriageFormed))
            {
                var p=c.TryGetPawn(evt.A);if(p==null)continue;
                foreach(var e in Book.Businesses.Where(e=>e.Map==p.Map?.uniqueID&&e.ServiceCells.Contains(p.Map.cellIndices.CellToIndex(p.Position))))Event(p,c.TryGetPawn(evt.B),"FamilyCelebration");
            }
        }
        public static void Reconcile(FamilyComponent c)
        {
            Reset();VisitorCommerce.Reconcile();var bank=FamilyFramework.Commerce.Bank;
            if(bank!=null)foreach(var o in Book.Orders.Where(o=>o.Stage==ServiceStage.Settled&&!o.Refunded))bank.RememberSale(o.Visitor?"visitor:"+o.Key:"hold:"+o.Key,bank.Business(o.Business),o.Visitor?null:o.Account,o.Price,Day);
            // Loaded job drivers retain their order ids; only genuinely orphaned deliveries are reopened.
            ReconcileOrders(bank);
        }
        public static void CloseAll(FamilyComponent c)
        {
            foreach(var order in Book.Orders.Where(o=>o.Stage<ServiceStage.Settled).ToList())
            {
                if(order.Stage>=ServiceStage.Delivered){order.Stage=ServiceStage.Consumed;if(!Settle(order,true))throw new InvalidOperationException("A delivered bill could not settle; restore Treasury / customer silver and retry.");}
                else if(!CancelOrder(order))throw new InvalidOperationException("Restore the commerce provider to release pending reservations before removing this module.");
            }
            foreach(var map in Find.Maps)foreach(var p in map.mapPawns.AllPawnsSpawned.Where(p=>p.CurJob?.def.defName.StartsWith("WFL_H_")==true).ToList())p.jobs.EndCurrentJob(JobCondition.InterruptForced);
            foreach(var booking in c.Registry.HospitalityBookings().Bookings.Where(b=>!b.Closed))if(!c.Registry.HospitalityBookings().Close(booking,FamilyFramework.Commerce.Bank))throw new InvalidOperationException("Restore the commerce provider to release the sponsored booking before removal.");
            HospitalityVisitors.ReleaseGenerated();Reset();
        }
    }
}
