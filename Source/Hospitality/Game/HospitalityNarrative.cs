using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;
using Verse.AI;
namespace FamilyLife
{
    public static class HospitalityNarrative
    {
        private static FamilyComponent C=>HospitalityService.Component;
        private static EstablishmentBook B=>HospitalityService.Book;
        private static HospitalityNarrativeBook S=>C?.Registry.HospitalityStories();
        private static int Now=>HospitalityService.Now;
        private static int Day=>HospitalityService.Day;
        private static Pawn P(int id)=>HospitalityService.Pawn(id);
        private static string Role(Pawn p)=>HospitalityService.Role(p);
        public static bool Near(Pawn a,Pawn b,int distance=6)=>a!=null&&b!=null&&a.Spawned&&b.Spawned&&a.Map==b.Map&&a.Position.DistanceTo(b.Position)<=distance&&GenSight.LineOfSight(a.Position,b.Position,a.Map);
        private static bool Eligible(Pawn p)=>p!=null&&p.Spawned&&!p.Dead&&!p.Downed&&!p.Drafted&&!p.InMentalState&&!FrameworkServices.IsAvatar(p);
        private static bool Kin(Pawn a,Pawn b)=>a!=null&&b!=null&&EstablishmentPersonalityService.Related(a,b);
        private static bool At(Pawn p,Establishment e)=>p!=null&&p.Spawned&&p.Map.uniqueID==e.Map&&(e.ServiceCells.Contains(p.Map.cellIndices.CellToIndex(p.Position))||HospitalityService.Thing(e.Counter) is Thing counter&&p.Position.DistanceTo(counter.Position)<7);
        private static bool RivalFamilies(Pawn a,Pawn b)
        {
            var pa=C.Registry.GetPerson(a?.thingIDNumber??0);var pb=C.Registry.GetPerson(b?.thingIDNumber??0);if(pa==null||pb==null)return false;
            var tie=C.Registry.GetTie(C.Registry.LineageIdFor(pa),C.Registry.LineageIdFor(pb));return tie!=null&&(tie.State==FamilyTieState.Strained||tie.State==FamilyTieState.Feuding);
        }
        public static Job StoryJob(Pawn worker,Establishment e)
        {
            var task=S?.Tasks.FirstOrDefault(t=>t.Actor==worker.thingIDNumber&&t.Business==e.Id&&!t.Completed&&!t.Active&&Now<t.Deadline);
            if(task==null)return null;var other=P(task.Other);if(!Eligible(other)||!worker.CanReach(other,PathEndMode.Touch,Danger.Some))return null;
            var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WFL_H_Story"),other);job.count=task.Id;return job;
        }
        private static bool Schedule(Establishment e,Pawn actor,Pawn other,HospitalityStoryKind kind,int order=0,Pawn third=null,int confidence=0)
        {
            if(!Eligible(actor)||!Eligible(other)||!e.Staff.Contains(actor.thingIDNumber)||!Near(actor,other,12)||third!=null&&!Eligible(third))return false;
            string cooldown="story:"+e.Id+":"+kind+":"+actor.thingIDNumber+":"+other.thingIDNumber;
            if(B.Seen.TryGetValue(cooldown,out int last)&&Day-last<3)return false;
            if(S.Tasks.Count(t=>!t.Completed&&t.Deadline>Now)>=64)return false;
            var task=S.Schedule(e.Id,actor.thingIDNumber,other.thingIDNumber,kind,Now,order,third?.thingIDNumber??0,confidence);if(task==null)return false;B.Seen[cooldown]=Day;return true;
        }
        public static void RequestMentoring(Establishment e,Pawn mentor,Pawn learner)=>Schedule(e,mentor,learner,HospitalityStoryKind.Mentoring);
        public static void Tick()
        {
            if(C==null||S==null)return;
            foreach(var task in S.Tasks.Where(t=>!t.Completed))
            {
                if(task.Deadline<Now)task.Completed=true;
                else if(task.Active&&P(task.Actor)?.CurJob?.def.defName!="WFL_H_Story")task.Active=false;
            }
            S.Tasks.RemoveAll(t=>t.Completed&&Now-t.Created>60000);
            S.Confidences.RemoveAll(c=>Now-c.Tick>60000*15);while(S.Confidences.Count>100)S.Confidences.RemoveAt(0);
            foreach(var key in S.Counts.Keys.Where(k=>k.StartsWith("hostility:")).ToList()){var parts=key.Split(':');if(parts.Length==3&&int.TryParse(parts[2],out int date)&&Day-date>3)S.Counts.Remove(key);}
            if(S.Counts.Count>2000)foreach(var key in S.Counts.Keys.Take(S.Counts.Count-2000).ToList())S.Counts.Remove(key);
            var promiseIds=new HashSet<int>(C.Registry.Commitments.Select(p=>p.Id));S.WorkedPromises.RemoveWhere(id=>!promiseIds.Contains(id));S.ExcusedPromises.RemoveWhere(id=>!promiseIds.Contains(id));S.HandledPromises.RemoveWhere(id=>!promiseIds.Contains(id));
            foreach(var e in B.Businesses)
            {
                var map=HospitalityService.Map(e);if(map==null)continue;
                var staff=e.Staff.Select(P).Where(p=>Eligible(p)&&HospitalityService.Working(p,e)).Take(20).ToList();
                var patrons=map.mapPawns.AllPawnsSpawned.Where(p=>Eligible(p)&&p.RaceProps.Humanlike&&At(p,e)&&!e.Staff.Contains(p.thingIDNumber)).Take(24).ToList();
                ObserveService(e,staff,patrons);ObservePromises(e);
                if(Now%2500>=600||!e.Enabled||!e.OpenAt(GenLocalDate.HourOfDay(map)))continue;
                foreach(var worker in staff.Where(HospitalityService.Ready).Take(4))
                {
                    if(S.Tasks.Any(t=>t.Actor==worker.thingIDNumber&&!t.Completed&&t.Deadline>Now))continue;
                    var latest=B.Orders.Where(o=>o.Business==e.Id&&Now-o.Created<60000).OrderByDescending(o=>o.Id).ToList();
                    var another=staff.FirstOrDefault(p=>p!=worker&&Near(worker,p));
                    if(another!=null)
                    {
                        var madeByOther=latest.FirstOrDefault(o=>o.Maker==another.thingIDNumber&&o.Stage==ServiceStage.Settled);
                        if(madeByOther!=null&&(HospitalityService.Trait(worker,"Greedy")||HospitalityService.Trait(worker,"Psychopath"))&&patrons.Any(p=>Near(worker,p))&&Rand.Chance(.06f))
                            if(Schedule(e,worker,another,HospitalityStoryKind.CreditClaim,madeByOther.Id,patrons.First(p=>Near(worker,p))))continue;
                        var failed=latest.FirstOrDefault(o=>o.Stage==ServiceStage.Cancelled&&o.Worker==worker.thingIDNumber&&o.Maker>0&&o.Maker!=another.thingIDNumber&&S.Count("failure:"+o.Id+":"+worker.thingIDNumber)>0);
                        if(failed!=null&&HospitalityService.Trait(worker,"Abrasive")&&Rand.Chance(.08f))if(Schedule(e,worker,another,HospitalityStoryKind.FalseBlame,failed.Id))continue;
                    }
                    var rival=map.mapPawns.FreeColonistsSpawned.FirstOrDefault(p=>p!=worker&&Eligible(p)&&Near(worker,p)&&Role(p)!="Cook"&&B.Orders.Any(o=>o.Maker==p.thingIDNumber&&o.Stage==ServiceStage.Settled&&Now-o.Created<60000));
                    if(Role(worker)=="Artisan"&&rival!=null&&Role(rival)=="Artisan"&&latest.Any(o=>o.Maker==worker.thingIDNumber&&o.Stage==ServiceStage.Settled)&&Rand.Chance(.08f))if(Schedule(e,worker,rival,HospitalityStoryKind.Rivalry))continue;
                    if(e.Kind==EstablishmentKind.Tavern||e.Kind==EstablishmentKind.Inn)
                    {
                        var confidence=S.Confidences.FirstOrDefault(c=>!c.Disclosed&&c.Keeper==worker.thingIDNumber&&Near(worker,P(c.Confider)));
                        var listener=patrons.FirstOrDefault(p=>confidence==null||p.thingIDNumber!=confidence.Confider);
                        if(confidence!=null&&listener!=null&&(HospitalityService.Trait(worker,"Abrasive")||HospitalityService.Trait(worker,"Psychopath"))&&Rand.Chance(.06f))
                            if(Schedule(e,worker,listener,HospitalityStoryKind.Disclosure,third:P(confidence.Confider),confidence:confidence.Id))continue;
                        var distressed=patrons.FirstOrDefault(p=>(p.needs?.mood?.CurLevelPercentage??.5f)<.4f&&Near(worker,p));
                        if(distressed!=null&&Rand.Chance(.15f))if(Schedule(e,worker,distressed,HospitalityStoryKind.Confidence))continue;
                        if(patrons.Count>=2&&Rand.Chance(.12f))
                        {
                            var first=patrons.FirstOrDefault(p=>Near(worker,p));var second=patrons.FirstOrDefault(p=>p!=first&&Near(first,p)&&Near(worker,p));
                            if(first!=null&&second!=null&&Math.Abs(first.relations.OpinionOf(second))<15)if(Schedule(e,worker,first,HospitalityStoryKind.Introduction,third:second))continue;
                        }
                    }
                    var bank=FamilyFramework.Commerce.Bank;
                    if(bank!=null&&bank.WagesOwed(bank.Business(e.Id),worker.thingIDNumber,Day-15)>0)
                    {
                        var relative=patrons.Concat(staff).FirstOrDefault(p=>p!=worker&&Kin(worker,p)&&Near(worker,p));
                        if(relative!=null&&HospitalityService.Social(worker,relative))
                        {
                            string fact=e.Owner==EstablishmentOwner.Household&&C.Registry.GetPerson(worker.thingIDNumber)?.HouseholdId==e.OwnerId?"UnpaidFamilyWork":"HouseholdHardship";
                            if(B.Once("wage-concern:"+e.Id+":"+worker.thingIDNumber+":"+Day,Day))HospitalityService.Event(relative,e.Owner==EstablishmentOwner.Person?P(e.OwnerId):worker,fact);
                        }
                    }
                }
            }
        }
        private static void ObserveService(Establishment e,List<Pawn> staff,List<Pawn> patrons)
        {
            var recent=B.Orders.Where(o=>o.Business==e.Id&&o.Stage==ServiceStage.Settled&&Now-o.Created<60000).ToList();
            if(recent.Count>=4&&B.Once("busy-service:"+e.Id+":"+Day,Day))
            {
                foreach(var p in staff.Where(p=>recent.Any(o=>o.Worker==p.thingIDNumber||o.Maker==p.thingIDNumber)))HospitalityService.Event(p,null,"BusyService");
                if(recent.SelectMany(o=>new[]{o.Worker,o.Maker}).Where(id=>id>0).Distinct().Count()>1&&!B.Orders.Any(o=>o.Business==e.Id&&o.Stage==ServiceStage.Cancelled&&Now-o.Created<60000))foreach(var p in staff.Where(p=>recent.Any(o=>o.Worker==p.thingIDNumber||o.Maker==p.thingIDNumber)))HospitalityService.Event(p,null,"TeamSuccess");
            }
            foreach(var failed in B.Orders.Where(o=>o.Business==e.Id&&o.Stage==ServiceStage.Cancelled&&o.Worker>0&&Now-o.Created<10000))
            {
                var absent=P(failed.Worker);if(!Eligible(absent)||!HospitalityService.Ready(absent)||!HospitalityService.Working(absent,e)||absent.CurJob?.def.defName.StartsWith("WFL_H_")==true)continue;
                var colleague=staff.FirstOrDefault(p=>p!=absent&&Near(p,absent)&&p.CurJob?.def.defName.StartsWith("WFL_H_")==true);
                if(colleague!=null&&B.Once("coverage-failed:"+failed.Id,Day)&&HospitalityService.Social(colleague,absent))HospitalityService.Event(colleague,absent,"CoverageFailed");
            }
            var fighters=HospitalityService.Map(e).mapPawns.AllPawnsSpawned.Where(p=>At(p,e)&&p.MentalStateDef==MentalStateDefOf.SocialFighting).Take(12).ToList();bool fighting=fighters.Count>0;
            string fight="fight-active:"+e.Id;
            if(fighting){B.Fractions[fight]=Now;foreach(var fighter in fighters)S.Counts["fighter:"+e.Id+":"+fighter.thingIDNumber]=Now;}
            else if(B.Fractions.TryGetValue(fight,out int last)&&Now-last<2500)
            {
                var host=staff.FirstOrDefault();var pair=patrons.Concat(staff).Where(p=>S.Count("fighter:"+e.Id+":"+p.thingIDNumber)>0&&Now-S.Count("fighter:"+e.Id+":"+p.thingIDNumber)<2500).Take(2).ToList();
                if(host!=null&&pair.Count==2&&Near(host,pair[0])&&Near(host,pair[1])&&HospitalityService.Social(host,pair[0]))
                {HospitalityService.EventAs("Bartender",host,pair[0],"DisputeResolved");B.Fractions.Remove(fight);}
            }
            foreach(var patron in patrons)
            {
                if(e.CutOffDrunks&&e.Alcohol&&(patron.needs?.joy?.CurLevelPercentage??1)<.7f&&patron.health.hediffSet.hediffs.Any(h=>h.def.defName=="AlcoholHigh"&&h.Severity>=.35f))
                {
                    var host=staff.FirstOrDefault(p=>Near(p,patron));if(host!=null&&B.Once("cutoff:"+e.Id+":"+patron.thingIDNumber+":"+Day,Day)&&HospitalityService.Social(host,patron))
                    {bool resents=HospitalityService.Trait(patron,"Abrasive")||(patron.needs?.mood?.CurLevelPercentage??.5f)<.4f;if(resents)HospitalityService.EventAs("Bartender",patron,host,"CutOff");if(resents)HospitalityService.EventAs("Innkeeper",patron,host,"RulesDispute");}
                }
                var stay=B.Guests.FirstOrDefault(g=>g.Pawn==patron.thingIDNumber&&g.Business==e.Id&&!g.Leaving);
                var innkeeper=staff.FirstOrDefault(p=>Near(p,patron));
                if(stay!=null&&innkeeper!=null&&B.Once("checkin:"+e.Id+":"+stay.Pawn+":"+stay.ArrivalDay,Day)&&HospitalityService.Social(innkeeper,patron))
                {
                    HospitalityService.EventAs("Innkeeper",innkeeper,patron,S.Count("guest:"+e.Id+":"+patron.thingIDNumber)>0?"ReturningGuest":"CheckIn");
                }
            }
        }
        private static void ObservePromises(Establishment e)
        {
            foreach(var promise in C.Registry.Commitments.Where(p=>e.Staff.Contains(p.FromId)).ToList())
            {
                var worker=P(promise.FromId);if(worker==null||!Kin(worker,P(promise.ToId)))continue;
                if(promise.IsOpen)
                {
                    if(worker.Drafted||worker.Downed||worker.InMentalState||worker.Spawned&&PawnReadinessApi.Danger(worker.Map))S.ExcusedPromises.Add(promise.Id);
                    if(Math.Abs((long)Now-promise.DueTick)<=2500&&HospitalityService.Working(worker,e)&&worker.CurJob?.def.defName.StartsWith("WFL_H_",StringComparison.Ordinal)==true)S.WorkedPromises.Add(promise.Id);
                }
                if(S.MissedPromise(promise,S.WorkedPromises.Contains(promise.Id)))HospitalityService.Event(P(promise.ToId),worker,"MissedFamilyPlan");
            }
        }
        public static void Complete(HospitalityStoryTask task,Pawn actor,Pawn other)
        {
            if(task==null||task.Completed||!Near(actor,other,4))return;
            var e=B.ById(task.Business);var order=B.Order(task.Order);var third=P(task.Third);
            var evidence=new StoryEvidence{OtherPresent=true,ThirdPresent=Near(actor,third),OwnsWork=CareerService.SkillsOf(actor)("Cooking")>=CareerService.SkillsOf(other)("Cooking")+2,OtherOwnsWork=order?.Maker==other.thingIDNumber&&order.Maker!=actor.thingIDNumber,ActorCausedFailure=order?.Stage==ServiceStage.Cancelled&&order.Worker==actor.thingIDNumber&&S.Count("failure:"+order.Id+":"+actor.thingIDNumber)>0,OtherInnocent=order!=null&&order.Maker>0&&order.Maker!=other.thingIDNumber&&order.Worker!=other.thingIDNumber,ComparableWork=B.Orders.Any(o=>o.Maker==actor.thingIDNumber&&o.Stage==ServiceStage.Settled&&Now-o.Created<60000)&&B.Orders.Any(o=>o.Maker==other.thingIDNumber&&o.Stage==ServiceStage.Settled&&Now-o.Created<60000),KnownConfidence=S.Confidences.Any(c=>c.Id==task.Confidence&&c.Keeper==actor.thingIDNumber&&c.Knows.Contains(actor.thingIDNumber)),ConfiderCanHear=Near(actor,third),ActualDamage=S.Count("damage:"+e.Id+":"+other.thingIDNumber)>0,RecordedHeir=task.Third>0&&(C.Registry.GetPerson(task.Third)?.SpouseIds.Contains(actor.thingIDNumber)==true||C.Registry.ChildrenOf(task.Third).Contains(actor.thingIDNumber))};
            if(!HospitalityService.Social(actor,other))return;evidence.RealConversation=true;task.Completed=true;
            if(!evidence.Allows(task.Kind))return;
            switch(task.Kind)
            {
                case HospitalityStoryKind.Mentoring:
                    bool refuses=HospitalityService.Trait(other,"Abrasive")&&other.relations.OpinionOf(actor)<0&&Rand.Chance(.4f);
                    if(refuses){HospitalityService.EventAs("Chef",actor,other,"AdviceDismissed","StandardsConflict");}
                    else {other.skills?.Learn(SkillDefOf.Cooking,30f);HospitalityService.Event(actor,other,"MentorSuccess","AdviceAccepted");HospitalityService.Event(other,actor,"CoworkerHelp");}break;
                case HospitalityStoryKind.CreditClaim:
                    HospitalityService.Event(other,actor,"StolenCredit");B.Note(e.Name+": "+actor.LabelShort+" publicly claimed credit for "+other.LabelShort+"'s recorded work. Financial credit stayed with its maker.");break;
                case HospitalityStoryKind.FalseBlame:HospitalityService.EventAs("Cook",other,actor,"FalseBlame");break;
                case HospitalityStoryKind.Rivalry:
                    bool respectful=actor.relations.OpinionOf(other)>=10;HospitalityService.EventAs("Artisan",actor,other,respectful?"RespectfulRivalry":"CreativeRivalry");if(!respectful)HospitalityService.EventAs("Chef",other,actor,"StandardsConflict");break;
                case HospitalityStoryKind.Introduction:
                    if(Near(other,third)&&other.interactions?.TryInteractWith(third,InteractionDefOf.Chitchat)==true){HospitalityService.EventAs("Bartender",other,actor,"Introduction");HospitalityService.EventAs("Bartender",third,actor,"Introduction");}break;
                case HospitalityStoryKind.Confidence:
                    if((other.needs?.mood?.CurLevelPercentage??.5f)<.4f){S.Confide(e.Id,other.thingIDNumber,actor.thingIDNumber,Now);HospitalityService.EventAs("Bartender",other,actor,"SupportiveTalk");}break;
                case HospitalityStoryKind.Disclosure:
                    if(S.Disclose(task.Confidence,actor.thingIDNumber,other.thingIDNumber,evidence.ConfiderCanHear))HospitalityService.EventAs("Bartender",third,actor,"ConfidenceDisclosed");break;
                case HospitalityStoryKind.HouseRules:
                    HospitalityService.EventAs("Innkeeper",other,actor,"RulesDispute");if(other.relations.OpinionOf(actor)>0)HospitalityVisitors.RequestLeave(other);break;
                case HospitalityStoryKind.OwnershipClaim:HospitalityService.Event(actor,other,"ContestedOwnership");HospitalityService.Event(other,actor,"ContestedOwnership");break;
            }
        }
        public static void OnSettled(ServiceOrder order,Pawn customer,Pawn staff,int service)
        {
            var e=B.ById(order.Business);var maker=P(order.Maker);var bank=FamilyFramework.Commerce.Bank;if(e==null||customer==null)return;
            if(order.DietRequest)HospitalityService.EventAs("Cook",customer,maker??staff,"DietAccommodation");
            if(maker!=null&&Role(maker)=="Artisan"&&EstablishmentPersonalityService.Temperament(customer).Refinement>=65&&Near(customer,maker)&&HospitalityService.Social(maker,customer))HospitalityService.EventAs("Artisan",customer,maker,"SharedTaste");
            if(staff!=null&&RivalFamilies(customer,staff))HospitalityService.Event(customer,staff,"RivalServed");
            var rival=HospitalityService.Map(e).mapPawns.FreeColonistsSpawned.FirstOrDefault(p=>p!=customer&&At(p,e)&&RivalFamilies(customer,p)&&Near(customer,p));
            if(rival!=null&&!customer.InMentalState&&!rival.InMentalState&&HospitalityService.Social(customer,rival))HospitalityService.Event(customer,rival,"RivalsPeaceful");
            if(staff!=null&&!order.TipAttempted&&service>=60)
            {
                float chance=.08f+(HospitalityService.Trait(customer,"Kind")?.1f:0)-(HospitalityService.Trait(customer,"Greedy")?.06f:0);
                int tip=Rand.Chance(chance)&&((customer.Faction==Faction.OfPlayer?bank.Available(bank.Person(customer.thingIDNumber)):HospitalityService.Cash(customer)-HospitalityVisitors.JourneyReserve(customer))>=1)?1:0;
                if(B.GiveTip(order,staff.thingIDNumber,tip,Day,bank,()=>HospitalityService.MoveCash(customer,tip,e)))HospitalityService.EventAs("Bartender",staff,customer,"TipReceived");
            }
        }
        public static bool Admission(Establishment e,Pawn customer)
        {
            int family=C.Registry.GetPerson(customer.thingIDNumber)?.PrimaryLineageId??0;bool familyExcluded=family>0&&e.ExcludedFamilies.Contains(family),excluded=e.BannedCustomers.Contains(customer.thingIDNumber);
            if(!familyExcluded&&!excluded)return true;
            if(At(customer,e))
            {
                var staff=HospitalityService.Coverage(e).FirstOrDefault(p=>Near(p,customer));
                if(staff!=null&&B.Once("refused:"+e.Id+":"+customer.thingIDNumber+":"+Day,Day)&&HospitalityService.Social(staff,customer))HospitalityService.Event(customer,staff,familyExcluded?"FamilyExcluded":"Excluded");
            }
            return false;
        }
        public static void BillTalk(ServiceOrder order,Pawn staff)
        {
            var customer=P(order.Customer);if(customer==null||staff==null||!Near(customer,staff))return;
            if((HospitalityService.Trait(customer,"Greedy")||(customer.needs?.mood?.CurLevelPercentage??.5f)<.3f)&&B.Once("bill-dispute:"+order.Id,Day)&&Rand.Chance(.15f)&&HospitalityService.Social(customer,staff))HospitalityService.EventAs("Bartender",staff,customer,"BillDispute");
        }
        public static void SuppliesRemoved(Establishment e,IntVec3 cell)
        {
            var map=HospitalityService.Map(e);if(map==null||!cell.GetThingList(map).Any(t=>t.def.ingestible!=null)||!B.Orders.Any(o=>o.Business==e.Id&&o.Stage<ServiceStage.Delivered))return;
            foreach(var p in e.Staff.Select(P).Where(p=>p!=null&&p.Spawned&&p.Map==map&&p.Position.DistanceTo(cell)<6&&GenSight.LineOfSight(p.Position,cell,map)))HospitalityService.Event(p,null,"SuppliesDiverted");
        }
        public static void Damage(PropertyDamageReport report)
        {
            if(B==null||report.Map==null)return;
            foreach(var e in B.Businesses.Where(e=>e.Map==report.Map.uniqueID))
            {
                bool asset=e.Counter==report.Target.thingIDNumber||e.Beds.Contains(report.Target.thingIDNumber)||e.Benches.Contains(report.Target.thingIDNumber)||e.StockContainers.Contains(report.Target.thingIDNumber)||e.StorageContainers.Contains(report.Target.thingIDNumber);
                int index=report.Map.cellIndices.CellToIndex(report.Cell);asset|=e.StockCells.Contains(index)||e.StorageCells.Contains(index);
                var witnesses=e.Staff.Select(P).Where(p=>Eligible(p)&&p.Map==report.Map&&p.Position.DistanceTo(report.Cell)<=6&&GenSight.LineOfSight(p.Position,report.Cell,report.Map)).ToList();
                if(report.Target.Destroyed&&report.Actor!=null)foreach(var o in B.Orders.Where(o=>o.Business==e.Id&&o.Thing==report.Target.thingIDNumber&&o.Worker==report.Actor.thingIDNumber&&o.Stage<ServiceStage.Delivered))S.Increment("failure:"+o.Id+":"+report.Actor.thingIDNumber);
                if(report.Target.def.ingestible!=null)
                    foreach(var lot in C.Registry.Production().Lots.Where(l=>l.Thing==report.Target.thingIDNumber&&l.Business==e.Id))
                    {var maker=P(lot.Worker);if(witnesses.Contains(maker)&&Role(maker)=="Artisan"&&report.Target.Destroyed)HospitalityService.EventAs("Artisan",maker,report.Actor,"MealRuined");}
                if(!asset||report.Actor==null||!B.Guests.Any(g=>g.Business==e.Id&&g.Pawn==report.Actor.thingIDNumber&&!g.Leaving))continue;
                var host=witnesses.FirstOrDefault(p=>Near(p,report.Actor));if(host==null)continue;
                S.Increment("damage:"+e.Id+":"+report.Actor.thingIDNumber);HospitalityService.EventAs("Innkeeper",host,report.Actor,"GuestDamage");
                Schedule(e,host,report.Actor,HospitalityStoryKind.HouseRules);
            }
        }
        public static void OnRefund(ServiceOrder order,Pawn customer)
        {
            var e=B.ById(order.Business);var staff=P(order.Worker);var owner=e.Owner==EstablishmentOwner.Person?P(e.OwnerId):null;
            if(staff!=null&&owner!=null&&staff!=owner&&Near(owner,staff)&&HospitalityService.Social(owner,staff))HospitalityService.EventAs("Chef",staff,owner,"TookResponsibility");
        }
        public static void CompletedStay(GuestStay stay)=>S?.Increment("guest:"+stay.Business+":"+stay.Pawn);
        public static void Inheritance(Establishment e,int deceased)
        {
            var dead=C.Registry.GetPerson(deceased);if(dead==null)return;var heirs=dead.SpouseIds.Select(P).Where(Eligible).ToList();if(heirs.Count==0)heirs=C.Registry.ChildrenOf(deceased).Select(P).Where(Eligible).ToList();
            var owner=e.Owner==EstablishmentOwner.Person?P(e.OwnerId):e.Owner==EstablishmentOwner.Household?C.Registry.GetHousehold(e.OwnerId)?.ResidentIds.Select(P).FirstOrDefault(Eligible):null;
            if(owner==null)return;var claimant=heirs.FirstOrDefault(p=>p!=owner&&HospitalityService.Trait(p,"Greedy")&&Near(p,owner));if(claimant!=null&&e.Staff.Contains(claimant.thingIDNumber)){var task=S.Schedule(e.Id,claimant.thingIDNumber,owner.thingIDNumber,HospitalityStoryKind.OwnershipClaim,Now,third:deceased);if(task!=null)B.Note(e.Name+": an eligible heir wants to discuss the succession.");}
        }
    }
    public sealed class JobDriver_HospitalityStory:JobDriver
    {
        private int started;
        private HospitalityStoryTask Task=>HospitalityService.Component?.Registry.HospitalityStories().Tasks.FirstOrDefault(t=>t.Id==job.count);
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref started,"started");}
        public override bool TryMakePreToilReservations(bool errorOnFailed)=>Task!=null&&!Task.Completed&&!Task.Active;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(()=>Task==null||Task.Completed||HospitalityService.Now>Task.Deadline||HospitalityService.Employer(pawn)?.Id!=Task.Business||!HospitalityService.Ready(pawn)||job.targetA.Pawn==null||!job.targetA.Pawn.Spawned||job.targetA.Pawn.Drafted||job.targetA.Pawn.InMentalState);
            AddFinishAction(condition=>{var t=Task;if(t!=null)t.Active=false;var e=t==null?null:HospitalityService.Book.ById(t.Business);if(e!=null&&started>0)HospitalityService.Book.WorkTime(e,pawn.thingIDNumber,Math.Max(0,HospitalityService.Now-started),"story:"+job.loadID+":"+HospitalityService.Now,HospitalityService.Day);});
            yield return Toils_General.Do(()=>{Task.Active=true;started=HospitalityService.Now;});
            yield return Toils_Interpersonal.GotoInteractablePosition(TargetIndex.A);
            var talk=Toils_General.Wait(180,TargetIndex.A);talk.socialMode=RandomSocialMode.Off;yield return talk;
            yield return Toils_General.Do(()=>HospitalityNarrative.Complete(Task,pawn,job.targetA.Pawn));
        }
    }
}
