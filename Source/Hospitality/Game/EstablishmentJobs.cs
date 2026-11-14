using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    public sealed class WorkGiver_EstablishmentKitchen : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            if(!FamilyFramework.Catalog.IsEnabled(CulinaryCareers.Module)||FamilyFramework.Commerce.Bank==null)return null;
            var e=HospitalityService.Employer(pawn);if(e==null||!HospitalityService.Ready(pawn))return null;
            int cooking=HospitalityService.Priority(pawn,DefDatabase<WorkTypeDef>.GetNamed("Cooking")),sell=HospitalityService.Priority(pawn,EstablishmentSelling.WorkType);
            if(sell>0&&sell<cooking&&HospitalityService.Book.Orders.Any(o=>o.Business==e.Id&&o.Stage==ServiceStage.Ordered))return null;
            return HospitalityService.CookingJob(pawn,e);
        }
    }
    public sealed class WorkGiver_EstablishmentSell : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            if(!FamilyFramework.Catalog.IsEnabled(CulinaryCareers.Module)||FamilyFramework.Commerce.Bank==null)return null;
            var e=HospitalityService.Employer(pawn);if(e==null||!HospitalityService.Ready(pawn)||HospitalityService.Priority(pawn,EstablishmentSelling.WorkType)==0)return null;
            var counter=HospitalityService.Thing(e.Counter);if(counter==null||!counter.Spawned)return null;
            int cooking=HospitalityService.Priority(pawn,DefDatabase<WorkTypeDef>.GetNamed("Cooking")),sell=HospitalityService.Priority(pawn,EstablishmentSelling.WorkType);
            if(cooking>0&&(sell==0||cooking<sell)&&HospitalityService.CookingJob(pawn,e)!=null)return null;
            foreach(var order in HospitalityService.Book.Orders.Where(o=>o.Business==e.Id&&o.Stage==ServiceStage.Consumed).OrderBy(o=>o.Id))
            {
                var customer=HospitalityService.Pawn(order.Customer);if(customer==null||!customer.Spawned||!pawn.CanReserveAndReach(counter,PathEndMode.Touch,Danger.Some))continue;
                var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WFL_H_Checkout"),counter,customer);job.count=order.Id;return job;
            }
            foreach(var order in HospitalityService.Book.Orders.Where(o=>o.Business==e.Id&&o.Stage==ServiceStage.Ordered).OrderBy(o=>o.Id))
            {
                var item=HospitalityService.Thing(order.Thing);var customer=HospitalityService.Pawn(order.Customer);if(item==null||!item.Spawned||customer==null||!customer.Spawned||!pawn.CanReserveAndReach(item,PathEndMode.ClosestTouch,Danger.Some))continue;
                var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WFL_H_Service"),item,customer);job.count=order.Id;return job;
            }
            var story=HospitalityNarrative.StoryJob(pawn,e);if(story!=null)return story;
            if(e.Kind==EstablishmentKind.Inn)
            {
                foreach(int bedId in e.Beds)
                {
                    string clean="room:"+e.Id+":"+bedId+":"+HospitalityService.Day;
                    var bed=HospitalityService.Thing(bedId);if(bed==null||HospitalityService.Book.Seen.ContainsKey(clean)||!pawn.CanReserveAndReach(bed,PathEndMode.Touch,Danger.Some))continue;
                    var job=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WFL_H_Room"),bed);job.count=e.Id;return job;
                }
            }
            if(cooking>0&&HospitalityService.CookingJob(pawn,e)!=null)return null;
            // A staffed counter requires coverage even between orders. Standby is bounded and interruptible by needs.
            if(!pawn.CanReserveAndReach(counter,PathEndMode.Touch,Danger.Some))return null;
            var standby=JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("WFL_H_Standby"),counter);standby.count=e.Id;return standby;
        }
    }
    public sealed class WorkGiver_EstablishmentCrafting : WorkGiver
    {
        public override Job NonScanJob(Pawn pawn)
        {
            if(!FamilyFramework.Catalog.IsEnabled(CulinaryCareers.Module)||FamilyFramework.Commerce.Bank==null)return null;var e=HospitalityService.Employer(pawn);
            if(e==null||e.Kind!=EstablishmentKind.Shop||!HospitalityService.Ready(pawn))return null;
            int craft=HospitalityService.Priority(pawn,DefDatabase<WorkTypeDef>.GetNamedSilentFail("Crafting")),sell=HospitalityService.Priority(pawn,EstablishmentSelling.WorkType);
            if(craft==0||(sell>0&&sell<craft&&HospitalityService.Book.Orders.Any(o=>o.Business==e.Id&&(o.Stage==ServiceStage.Ordered||o.Stage==ServiceStage.Consumed))))return null;
            return HospitalityService.CraftingJob(pawn,e);
        }
    }
    public sealed class JobDriver_EstablishmentService : JobDriver
    {
        private int started,lastPaid;
        private ServiceOrder Order=>HospitalityService.Book?.Order(job.count);
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref started,"started");Scribe_Values.Look(ref lastPaid,"lastPaid");}
        public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.Reserve(job.targetA,job,1,job.def.defName=="WFL_H_Service"?1:-1,null,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(()=>Order==null||Order.Stage==ServiceStage.Cancelled||HospitalityService.Employer(pawn)?.Id!=Order.Business||!HospitalityService.Ready(pawn));
            AddFinishAction(condition=>
            {
                PayTime();var o=Order;
                if(job.def.defName=="WFL_H_Service"&&pawn.Spawned&&pawn.carryTracker.CarriedThing!=null&&pawn.carryTracker.TryDropCarriedThing(pawn.Position,ThingPlaceMode.Near,out var dropped)&&o!=null)o.Thing=dropped.thingIDNumber;
                if(o!=null&&o.Stage==ServiceStage.Delivering){o.Stage=ServiceStage.Ordered;o.Worker=0;}
            });
            yield return Toils_General.Do(()=>{started=HospitalityService.Now;lastPaid=started;var o=Order;if(o.Stage==ServiceStage.Ordered){o.Stage=ServiceStage.Delivering;o.Worker=pawn.thingIDNumber;o.LastTouched=started;}});
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            if(job.def.defName=="WFL_H_Checkout")
            {
                var talk=Toils_General.Wait(120,TargetIndex.B);talk.tickAction=PayTime;yield return talk;
                yield return Toils_General.Do(()=>{if(Order!=null&&Order.Stage==ServiceStage.Consumed){Order.Worker=pawn.thingIDNumber;HospitalityNarrative.BillTalk(Order,pawn);HospitalityService.Settle(Order);}});yield break;
            }
            yield return Toils_General.Do(()=>{var o=Order;var item=job.targetA.Thing;if(o==null||item==null||pawn.carryTracker.TryStartCarry(item,o.Count)==0)EndJobWith(JobCondition.Incompletable);});
            yield return Toils_Goto.GotoThing(TargetIndex.B,PathEndMode.Touch);
            yield return Toils_General.Do(()=>
            {
                var o=Order;var customer=HospitalityService.Pawn(o.Customer);var carried=pawn.carryTracker.CarriedThing;
                if(customer==null||carried==null||!pawn.carryTracker.TryDropCarriedThing(customer.Position,ThingPlaceMode.Near,out var placed)){EndJobWith(JobCondition.Incompletable);return;}
                o.Thing=placed.thingIDNumber;o.Stage=ServiceStage.Delivered;o.DeliveredTick=HospitalityService.Now;o.LastTouched=o.DeliveredTick;
                var e=HospitalityService.Book.ById(o.Business);HospitalityService.Book.Credit(e,pawn.thingIDNumber,ContributionKind.Service,"serve:"+o.Id,HospitalityService.Day);
                foreach(var lot in HospitalityService.Component.Registry.Production().Lots.Where(l=>l.Thing==placed.thingIDNumber&&l.Business==e.Id).ToList()){if(!lot.Credited){HospitalityService.Book.Credit(e,lot.Worker,ContributionKind.Production,lot.Origin+":lot:"+lot.Id,HospitalityService.Day,lot.Count);lot.Credited=true;}o.Maker=lot.Worker;o.Presentation=lot.Presentation;}
                CareerService.Report(HospitalityService.Component,pawn,CulinaryCareers.Career,e.Kind==EstablishmentKind.Tavern?"Bartender":e.Kind==EstablishmentKind.Inn?"Innkeeper":null,"service.deliver","serve:"+o.Id,4,HospitalityService.Now);
                HospitalityService.Social(pawn,customer);
            });
        }
        private void PayTime()
        {
            int now=HospitalityService.Now;if(lastPaid<=0)lastPaid=now;int elapsed=now-lastPaid;if(elapsed<=0)return;
            var e=Order==null?null:HospitalityService.Book.ById(Order.Business);if(e!=null)HospitalityService.Book.WorkTime(e,pawn.thingIDNumber,elapsed,"service:"+job.loadID+":"+now,HospitalityService.Day);lastPaid=now;
        }
    }
    public sealed class JobDriver_EstablishmentShift : JobDriver
    {
        private int lastPaid;
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref lastPaid,"lastPaid");}
        public override bool TryMakePreToilReservations(bool errorOnFailed)=>pawn.Reserve(job.targetA,job,1,-1,null,errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(()=>HospitalityService.Employer(pawn)?.Id!=job.count||!HospitalityService.Ready(pawn));
            AddFinishAction(condition=>Pay());
            yield return Toils_General.Do(()=>lastPaid=HospitalityService.Now);
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            var work=Toils_General.Wait(job.def.defName=="WFL_H_Room"?300:600,TargetIndex.A);work.tickAction=()=>{Pay();if(job.def.defName=="WFL_H_Standby"&&HospitalityService.Book.Orders.Any(o=>o.Business==job.count&&(o.Stage==ServiceStage.Ordered||o.Stage==ServiceStage.Consumed)))ReadyForNextToil();};yield return work;
            yield return Toils_General.Do(()=>
            {
                var e=HospitalityService.Book.ById(job.count);if(e==null||job.def.defName!="WFL_H_Room")return;
                string key="room:"+e.Id+":"+job.targetA.Thing.thingIDNumber+":"+HospitalityService.Day;
                if(HospitalityService.Book.Once(key,HospitalityService.Day)){HospitalityService.Book.Credit(e,pawn.thingIDNumber,ContributionKind.Support,key,HospitalityService.Day);CareerService.Report(HospitalityService.Component,pawn,CulinaryCareers.Career,"Innkeeper","inn.room",key,3,HospitalityService.Now);HospitalityService.Event(pawn,null,"SmoothShift");}
            });
        }
        private void Pay(){int now=HospitalityService.Now;if(lastPaid<=0)lastPaid=now;var e=HospitalityService.Book?.ById(job.count);if(e!=null&&now>lastPaid)HospitalityService.Book.WorkTime(e,pawn.thingIDNumber,now-lastPaid,"shift:"+job.loadID+":"+now,HospitalityService.Day);lastPaid=now;}
    }
    public sealed class JobDriver_EstablishmentCustomer : JobDriver
    {
        private int orderId,checkoutStarted;
        private ServiceOrder Order=>HospitalityService.Book?.Order(orderId==0?job.count:orderId);
        public override void ExposeData(){base.ExposeData();Scribe_Values.Look(ref orderId,"orderId");Scribe_Values.Look(ref checkoutStarted,"checkoutStarted");}
        public override bool TryMakePreToilReservations(bool errorOnFailed){if(orderId==0)orderId=job.count;return pawn.Reserve(job.targetA,job,1,-1,null,errorOnFailed);}
        protected override IEnumerable<Toil> MakeNewToils()
        {
            if(orderId==0)orderId=job.count;
            this.FailOn(()=>Order==null||Order.Stage==ServiceStage.Cancelled||pawn.Drafted||pawn.Downed||pawn.InMentalState||PawnReadinessApi.Danger(pawn.Map));
            AddFinishAction(condition=>{var o=Order;if(o==null)return;if(o.Stage<ServiceStage.Delivered)HospitalityService.CancelOrder(o);else if(o.Stage<ServiceStage.Settled){o.Stage=ServiceStage.Consumed;HospitalityService.Settle(o,true);}});
            yield return Toils_Goto.GotoCell(TargetIndex.A,PathEndMode.OnCell);
            // Browsing is a short walk through the service floor; diners wait at their chosen seat.
            if(HospitalityService.Book.ById(Order.Business).Kind==EstablishmentKind.Shop)
            {
                yield return Toils_General.Wait(180);
                var browse=Toils_General.Do(()=>{var e=HospitalityService.Book.ById(Order.Business);var cell=e.ServiceCells.Select(pawn.Map.cellIndices.IndexToCell).Where(c=>c.Standable(pawn.Map)).OrderBy(c=>c.DistanceTo(pawn.Position)).Skip(2).Cast<IntVec3?>().FirstOrDefault();if(cell.HasValue)job.SetTarget(TargetIndex.A,cell.Value);});yield return browse;
                yield return Toils_Goto.GotoCell(TargetIndex.A,PathEndMode.OnCell);
            }
            var wait=ToilMaker.MakeToil("WaitForService");wait.defaultCompleteMode=ToilCompleteMode.Never;wait.tickAction=()=>
            {
                var o=Order;if(o==null||o.Stage==ServiceStage.Cancelled){EndJobWith(JobCondition.Incompletable);return;}
                if(o.Stage==ServiceStage.Delivered){job.SetTarget(TargetIndex.A,HospitalityService.Thing(o.Thing));job.count=o.Count;ReadyForNextToil();}
                else if(HospitalityService.Now-o.Created>10000)EndJobWith(JobCondition.Incompletable);
            };yield return wait;
            var shop=HospitalityService.Book.ById(Order.Business).Kind==EstablishmentKind.Shop;
            yield return Toils_Goto.GotoThing(TargetIndex.A,PathEndMode.Touch);
            if(shop)
                yield return Toils_General.Do(()=>{var thing=job.targetA.Thing;if(thing==null){EndJobWith(JobCondition.Incompletable);return;}var part=thing.SplitOff(1);if(part.Spawned)part.DeSpawn();if(pawn.inventory?.innerContainer.TryAdd(part)!=true)GenPlace.TryPlaceThing(part,pawn.Position,pawn.Map,ThingPlaceMode.Near);});
            else
            {
                yield return Toils_Ingest.ReserveFoodFromStackForIngesting(TargetIndex.A,pawn);
                yield return Toils_Ingest.PickupIngestible(TargetIndex.A,pawn);
                yield return Toils_Ingest.FindAdjacentEatSurface(TargetIndex.B,TargetIndex.A);
                yield return Toils_Ingest.ChewIngestible(pawn,1f,TargetIndex.A,TargetIndex.B);
                yield return Toils_Ingest.FinalizeIngest(pawn,TargetIndex.A);
            }
            yield return Toils_General.Do(()=>{Order.Stage=ServiceStage.Consumed;checkoutStarted=HospitalityService.Now;job.SetTarget(TargetIndex.C,HospitalityService.Thing(HospitalityService.Book.ById(Order.Business).Counter));});
            // Queue outside the counter; only the first completed diner approaches it.
            var queue=ToilMaker.MakeToil("CheckoutQueue");queue.defaultCompleteMode=ToilCompleteMode.Never;queue.tickAction=()=>
            {
                if(Order.Stage==ServiceStage.Settled){ReadyForNextToil();return;}
                if(!HospitalityService.Book.Orders.Any(o=>o.Business==Order.Business&&o.Stage==ServiceStage.Consumed&&o.Id<Order.Id))ReadyForNextToil();
                if(HospitalityService.Now-checkoutStarted>2500){HospitalityService.Settle(Order,true);ReadyForNextToil();}
            };yield return queue;
            yield return Toils_Goto.GotoThing(TargetIndex.C,PathEndMode.Touch);
            var checkout=ToilMaker.MakeToil("WaitForCheckout");checkout.defaultCompleteMode=ToilCompleteMode.Never;checkout.tickAction=()=>{if(Order.Stage==ServiceStage.Settled)ReadyForNextToil();else if(HospitalityService.Now-checkoutStarted>5000){HospitalityService.Settle(Order,true);ReadyForNextToil();}};yield return checkout;
        }
    }
}
