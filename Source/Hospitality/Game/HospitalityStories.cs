using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    public static class HospitalityStories
    {
        private static EstablishmentBook Book=>HospitalityService.Book;
        public static void BookMeal(Establishment e,BookingKind kind,int household,int expected,int budget)
        {
            var bank=FamilyFramework.Commerce.Bank;if(bank==null)return;
            string sponsor=household>0?bank.Household(household):"c:1";
            var booking=HospitalityService.Component.Registry.HospitalityBookings().Create(e,kind,household,expected,budget,sponsor,HospitalityService.Now,bank);
            if(booking==null){Messages.Message("The sponsor cannot reserve that budget.",MessageTypeDefOf.RejectInput,false);return;}
            Book.Note(e.Name+": accepted "+kind+"; "+budget+" silver reserved, unused funds return after one day.");
        }
        public static void BookingServed(ServiceOrder order,Pawn customer)
        {
            if(order.Booking<=0)return;var booking=HospitalityService.Component.Registry.HospitalityBookings().Bookings.FirstOrDefault(b=>b.Id==order.Booking);if(booking==null)return;
            booking.Served++;string fact=booking.Kind==BookingKind.CommunityMeal?"PublicFeast":booking.Kind==BookingKind.EmergencyShelter?"EmergencyShelter":"FamilyCelebration";
            var staff=HospitalityService.Pawn(order.Worker);HospitalityService.Event(customer,staff,fact);
            if(staff!=null)HospitalityService.Event(staff,customer,"CommissionDone");
        }
        public static bool Refund(ServiceOrder order)
        {
            var e=Book.ById(order.Business);var bank=FamilyFramework.Commerce.Bank;var customer=HospitalityService.Pawn(order.Customer);
            if(e==null||bank==null||order.Stage!=ServiceStage.Settled||order.Refunded)return false;
            int protectedPay=bank.Owed(bank.Business(e.Id))+Book.Earnings.Where(x=>x.Business==e.Id&&!x.Posted).Sum(x=>x.Silver)+e.BonusPool;
            if(bank.Available(bank.Business(e.Id))-protectedPay<order.Price){Messages.Message("The business cannot fund this refund while protecting earned staff pay.",MessageTypeDefOf.RejectInput,false);return false;}
            bool paid=order.Visitor?bank.ExternalRefund("refund:"+order.Id,bank.Business(e.Id),order.Price,HospitalityService.Day,()=>ReturnCash(e,customer,order.Price)):bank.Transfer("refund:"+order.Id,bank.Business(e.Id),bank.Successor(order.Account),order.Price,HospitalityService.Day);
            if(!paid)return false;order.Refunded=true;e.Revenue=Math.Max(0,e.Revenue-order.Price);e.Service=Math.Min(100,e.Service+3);e.Reliability=Math.Min(100,e.Reliability+2);
            HospitalityService.Event(customer,Owner(e),"ComplaintResolved");var staff=HospitalityService.Pawn(order.Worker);HospitalityService.Event(staff,customer,"ComplaintResolved");Book.Note(e.Name+": refunded a "+order.Price+" silver bill; earned staff pay was preserved.");return true;
        }
        private static bool ReturnCash(Establishment e,Pawn customer,int amount)
        {
            var map=HospitalityService.Map(e);if(customer?.inventory==null||map==null)return false;
            var stacks=map.listerThings.ThingsOfDef(ThingDefOf.Silver).Where(t=>t.Spawned&&!t.IsForbidden(Faction.OfPlayer)).ToList();if(stacks.Sum(t=>t.stackCount)<amount)return false;
            int left=amount;foreach(var stack in stacks){int take=Math.Min(left,stack.stackCount);if(take<=0)break;stack.SplitOff(take).Destroy();left-=take;}
            var money=ThingMaker.MakeThing(ThingDefOf.Silver);money.stackCount=amount;
            if(!customer.inventory.innerContainer.TryAdd(money)){GenPlace.TryPlaceThing(money,customer.Position,customer.Map,ThingPlaceMode.Near);}return true;
        }
        public static void Tick()
        {
            var c=HospitalityService.Component;if(c==null||Book==null)return;int now=HospitalityService.Now,day=HospitalityService.Day;var bank=FamilyFramework.Commerce.Bank;
            foreach(var booking in c.Registry.HospitalityBookings().Bookings.Where(b=>!b.Closed).ToList())
            {
                if(now>=booking.End)
                {
                    if(bank==null)continue; // keep the reservation until its provider returns
                    c.Registry.HospitalityBookings().Close(booking,bank);var e=Book.ById(booking.Business);if(e==null)continue;
                    bool success=booking.Served>=Math.Max(1,booking.Expected/2);
                    foreach(var p in e.Staff.Select(HospitalityService.Pawn).Where(p=>p!=null))HospitalityService.Event(p,null,success?"CommissionDone":"CommissionFailed");
                    if(booking.Household>0)foreach(int id in c.Registry.GetHousehold(booking.Household)?.ResidentIds??new List<int>())HospitalityService.Event(HospitalityService.Pawn(id),null,success?"FamilyCelebration":"FamilyBookingFailed");
                    else if(booking.Kind==BookingKind.CommunityMeal)foreach(var p in HospitalityService.Map(e)?.mapPawns.FreeColonistsSpawned??new List<Pawn>())if(p.Position.DistanceTo(HospitalityService.Thing(e.Counter)?.Position??p.Position)<15)HospitalityService.Event(p,null,success?"PublicContractDone":"PublicContractFailed");
                    Book.Note(e.Name+": "+booking.Kind+" ended; "+booking.Served+" meals served.");
                }
                else if(booking.Kind==BookingKind.EmergencyShelter)
                {
                    var e=Book.ById(booking.Business);var map=e==null?null:HospitalityService.Map(e);if(map==null)continue;
                    foreach(var p in map.mapPawns.FreeColonistsSpawned.Where(p=>p.ownership.OwnedBed==null&&p.needs.rest?.CurLevelPercentage<.25f&&!p.Drafted&&!p.Downed&&!p.InMentalState).Take(3))
                    {
                        var bed=e.Beds.Select(HospitalityService.Thing).OfType<Building_Bed>().FirstOrDefault(b=>b.Spawned&&!b.ForPrisoners&&!b.OwnersForReading.Any()&&!Book.Guests.Any(g=>g.Bed==b.thingIDNumber&&!g.Leaving)&&p.CanReserveAndReach(b,PathEndMode.OnCell,Danger.Some));
                        if(bed!=null&&p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.LayDown,bed)))HospitalityService.Event(p,null,"EmergencyShelter");
                    }
                }
            }
            c.Registry.HospitalityBookings().Bookings.RemoveAll(b=>b.Closed&&now-b.End>60000*30);
            foreach(var e in Book.Businesses)
            {
                var map=HospitalityService.Map(e);if(map==null)continue;
                var workers=e.Staff.Select(HospitalityService.Pawn).Where(p=>HospitalityService.Working(p,e)).ToList();
                int outstanding=Book.Orders.Count(o=>o.Business==e.Id&&o.Stage<ServiceStage.Delivered);
                if(outstanding>workers.Count*2&&outstanding>2)foreach(var p in workers)HospitalityService.Event(p,null,"Overworked");
                foreach(var p in workers)
                {
                    if(!HospitalityService.Ready(p)||p.Position.DistanceTo(HospitalityService.Thing(e.Counter)?.Position??p.Position)>20)continue;
                    bool paid=bank!=null&&bank.Owed(bank.Business(e.Id))==0;
                    if(paid&&e.Revenue>0&&BusinessCalendar.IsPayday(day))HospitalityService.Event(p,Owner(e),"FairPay","FairCredit");
                    if(e.Visits>=10&&Book.Once("milestone:"+e.Id+":"+p.thingIDNumber,day))HospitalityService.Event(p,Owner(e),"FamilyMilestone","CommunityMilestone");
                    if(outstanding>0||now%2500>=600)continue;
                    var colleague=workers.FirstOrDefault(other=>other!=p&&other.Position.DistanceTo(p.Position)<4&&CareerService.SkillsOf(p)("Cooking")>=CareerService.SkillsOf(other)("Cooking")+2&&other.CurJob?.bill?.recipe?.workSkill==SkillDefOf.Cooking);
                    if(colleague!=null&&HospitalityService.Role(p)!="Cook"&&Book.Once("mentor:"+e.Id+":"+p.thingIDNumber+":"+day,day))
                    {
                        p.interactions?.TryInteractWith(colleague,InteractionDefOf.Chitchat);colleague.skills?.Learn(SkillDefOf.Cooking,30f);
                        HospitalityService.Event(p,colleague,"MentorSuccess");HospitalityService.Event(colleague,p,"CoworkerHelp");
                    }
                    var customer=map.mapPawns.AllPawnsSpawned.FirstOrDefault(other=>other!=p&&other.RaceProps.Humanlike&&!e.Staff.Contains(other.thingIDNumber)&&other.Position.DistanceTo(p.Position)<3&&!other.Downed&&!other.InMentalState&&e.ServiceCells.Contains(map.cellIndices.CellToIndex(other.Position)));
                    if(customer!=null&&Book.Once("attention:"+e.Id+":"+p.thingIDNumber+":"+customer.thingIDNumber+":"+day,day))
                    {
                        bool success=p.interactions?.TryInteractWith(customer,InteractionDefOf.Chitchat)==true;
                        if(success){Book.Credit(e,p.thingIDNumber,ContributionKind.Service,"attention:"+customer.thingIDNumber+":"+day,day);HospitalityService.Event(customer,p,"SupportiveTalk");CareerService.Report(c,p,CulinaryCareers.Career,e.Kind==EstablishmentKind.Tavern?"Bartender":"Innkeeper","service.attention","attention:"+e.Id+":"+customer.thingIDNumber+":"+day,2,now);}
                    }
                }
                if(e.LastReviewDay!=day)
                {
                    e.LastReviewDay=day;
                    if(e.Revenue>0&&outstanding==0)foreach(var p in workers)HospitalityService.Event(p,null,"SmoothShift","SafeShift");
                }
            }
        }
        public static void Interaction(Pawn actor,Pawn recipient,InteractionDef interaction)
        {
            if(Book==null||actor==null||recipient==null||actor.Map==null)return;
            var e=Book.Businesses.FirstOrDefault(b=>b.Map==actor.Map.uniqueID&&b.ServiceCells.Contains(actor.Map.cellIndices.CellToIndex(actor.Position))&&b.ServiceCells.Contains(actor.Map.cellIndices.CellToIndex(recipient.Position)));
            if(e==null)return;
            if(interaction==InteractionDefOf.Insult)
            {
                HospitalityService.Event(recipient,actor,"Insult");
                e.Review(e.Food,Math.Max(0,e.Service-10),e.Comfort,e.Value,Math.Max(0,e.Safety-5),Math.Min(100,e.Rowdiness+20));
                int household=HospitalityService.Component.Registry.GetPerson(actor.thingIDNumber)?.HouseholdId??0;
                int other=HospitalityService.Component.Registry.GetPerson(recipient.thingIDNumber)?.HouseholdId??0;
                if(household>0&&other>0&&household!=other)HospitalityService.Event(recipient,actor,"FamilyDispute");
            }
            else if(interaction==InteractionDefOf.Chitchat)
            {
                bool sameHouse=HospitalityService.Component.Registry.GetPerson(actor.thingIDNumber)?.HouseholdId>0&&HospitalityService.Component.Registry.GetPerson(actor.thingIDNumber)?.HouseholdId==HospitalityService.Component.Registry.GetPerson(recipient.thingIDNumber)?.HouseholdId;
                if(sameHouse&&e.Staff.Contains(actor.thingIDNumber)&&e.Staff.Contains(recipient.thingIDNumber))HospitalityService.Event(actor,recipient,"FamilyWork");
                if(e.Staff.Contains(actor.thingIDNumber)&&!e.Staff.Contains(recipient.thingIDNumber))HospitalityService.Event(recipient,actor,"SupportiveTalk");
            }
        }
        private static Pawn Owner(Establishment e)=>e.Owner==EstablishmentOwner.Person?HospitalityService.Pawn(e.OwnerId):null;
    }
}
