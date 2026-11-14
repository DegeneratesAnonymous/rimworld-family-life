using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class HospitalityTests
    {
        private static (Ledger ledger,BusinessBank bank,EstablishmentBook book,Establishment e) Setup()
        {
            var l=new Ledger();var bank=new BusinessBank(l);var book=new EstablishmentBook();var e=book.Create(1,EstablishmentKind.Restaurant);
            l.Receive(bank.Person(1),200,TxKind.Income,"seed",0);bank.Business(e.Id);return(l,bank,book,e);
        }
        [Fact] public void Order_reserves_real_money_checkout_once_and_never_creates_currency()
        {
            var x=Setup();var o=x.book.Begin(x.e,1,10,30,60000,x.bank.Person(1),false,x.bank);Assert.NotNull(o);Assert.Equal(170,x.bank.Available(x.bank.Person(1)));
            Assert.False(x.bank.Transfer("steal-reserved",x.bank.Person(1),x.bank.Business(x.e.Id),180,1));
            Assert.False(x.book.Checkout(o,x.bank,1,null));o.Stage=ServiceStage.Consumed;
            Assert.True(x.book.Checkout(o,x.bank,1,null));Assert.True(x.book.Checkout(o,x.bank,1,null));
            Assert.Equal(170,x.ledger.Balance(x.bank.Person(1)));Assert.Equal(30,x.ledger.Balance(x.bank.Business(x.e.Id)));Assert.Equal(30,x.e.Revenue);Assert.True(x.ledger.Conserved);
        }
        [Fact] public void An_unfilled_order_cancels_and_releases_its_reservation()
        {
            var x=Setup();var o=x.book.Begin(x.e,1,10,30,1,x.bank.Person(1),false,x.bank);Assert.True(x.book.Cancel(o,x.bank));Assert.True(x.book.Cancel(o,x.bank));Assert.Equal(200,x.bank.Available(x.bank.Person(1)));Assert.Equal(0,x.e.Revenue);
        }
        [Theory][InlineData(ServiceStage.Delivered)][InlineData(ServiceStage.Consumed)]
        public void Delivered_goods_cannot_be_cancelled_for_a_free_meal(ServiceStage stage)
        {
            var x=Setup();var o=x.book.Begin(x.e,1,10,30,1,x.bank.Person(1),false,x.bank);o.Stage=stage;Assert.False(x.book.Cancel(o,x.bank));Assert.Equal(170,x.bank.Available(x.bank.Person(1)));
        }
        [Fact] public void Reservations_survive_reload_and_other_treasury_operations_obey_them()
        {
            var x=Setup();var o=x.book.Begin(x.e,1,10,30,1,x.bank.Person(1),false,x.bank);var l=Ledger.Decode(new StateReader(x.ledger.Encode()));
            Assert.Equal(30,l.Held(x.bank.Person(1)));Assert.Equal(170,l.Available(x.bank.Person(1)));Assert.Equal(LedgerResult.Insufficient,l.Spend(x.bank.Person(1),171,TxKind.Spend,"spend",0));Assert.True(new BusinessBank(l).Commit(o.Key,x.bank.Business(x.e.Id),0));Assert.True(l.Conserved);
        }
        [Fact] public void Insufficient_backing_never_forces_a_checkout_or_loses_the_hold()
        {
            var x=Setup();var o=x.book.Begin(x.e,1,10,100,1,x.bank.Person(1),false,x.bank);x.ledger.SetPhysical(20);Assert.False(x.bank.Commit(o.Key,x.bank.Business(x.e.Id),0));Assert.Equal(100,x.ledger.Held(x.bank.Person(1)));x.ledger.SetPhysical(200);Assert.True(x.bank.Commit(o.Key,x.bank.Business(x.e.Id),0));
        }
        [Fact] public void External_sales_require_delivery_and_replay_never_delivers_twice()
        {
            var x=Setup();int deliveries=0;Assert.False(x.bank.ExternalSale("v",x.bank.Business(x.e.Id),25,0,()=>false));Assert.Equal(200,x.ledger.ExternalIn);
            Assert.True(x.bank.ExternalSale("v",x.bank.Business(x.e.Id),25,0,()=>{deliveries++;return true;}));Assert.True(x.bank.ExternalSale("v",x.bank.Business(x.e.Id),25,0,()=>{deliveries++;return true;}));Assert.Equal(1,deliveries);Assert.Equal(225,x.ledger.ExternalIn);Assert.True(x.ledger.Conserved);
        }
        [Fact] public void Business_wages_use_paydays_and_never_drain_households_or_community()
        {
            var x=Setup();x.bank.Transfer("fund",x.bank.Person(1),x.bank.Business(x.e.Id),5,0);x.ledger.Receive(Ledger.CommunityId,1000,TxKind.Income,"public",0);
            Assert.True(x.bank.Wages("work",x.bank.Business(x.e.Id),2,12,7));Assert.True(x.bank.Wages("work",x.bank.Business(x.e.Id),2,12,7));
            x.bank.Payday(x.bank.Business(x.e.Id),0);Assert.Equal(0,x.ledger.Balance(x.bank.Person(2)));x.bank.Payday(x.bank.Business(x.e.Id),7);x.bank.Payday(x.bank.Business(x.e.Id),7);
            Assert.Equal(5,x.ledger.Balance(x.bank.Person(2)));Assert.Equal(7,x.bank.Owed(x.bank.Business(x.e.Id)));Assert.Equal(1000,x.ledger.Balance(Ledger.CommunityId));Assert.True(x.ledger.Conserved);
            x.bank.Transfer("fund2",x.bank.Person(1),x.bank.Business(x.e.Id),7,8);x.bank.Payday(x.bank.Business(x.e.Id),15);Assert.Equal(12,x.ledger.Balance(x.bank.Person(2)));Assert.Equal(0,x.bank.Owed(x.bank.Business(x.e.Id)));
        }
        [Fact] public void Wage_shortfalls_are_shared_proportionally_without_silver_loss()
        {
            var x=Setup();x.bank.Transfer("f",x.bank.Person(1),x.bank.Business(x.e.Id),7,0);x.bank.Wages("a",x.bank.Business(x.e.Id),2,10,7);x.bank.Wages("b",x.bank.Business(x.e.Id),3,20,7);x.bank.Payday(x.bank.Business(x.e.Id),7);
            Assert.Equal(2,x.ledger.Balance(x.bank.Person(2)));Assert.Equal(5,x.ledger.Balance(x.bank.Person(3)));Assert.Equal(23,x.bank.Owed(x.bank.Business(x.e.Id)));Assert.True(x.ledger.Conserved);
        }
        [Fact] public void Fractional_shift_pay_accumulates_and_same_or_overlapping_time_cannot_pay_twice()
        {
            var x=Setup();x.e.BasePayMilli=1000;
            x.book.WorkTime(x.e,2,1250,"sample:1250",1);x.book.WorkTime(x.e,2,1250,"sample:1250",1);Assert.Empty(x.book.Earnings);
            x.book.WorkTime(x.e,2,1250,"sample:2500",1);Assert.Equal(1,x.book.Earnings.Sum(e=>e.Silver));
            var restored=EstablishmentBook.Decode(new StateReader(x.book.Encode()));restored.WorkTime(restored.ById(x.e.Id),2,1250,"sample:2500",1);Assert.Equal(1,restored.Earnings.Sum(e=>e.Silver));
        }
        [Fact] public void Contribution_pool_uses_collected_sales_weights_and_pays_exactly_once()
        {
            var x=Setup();x.bank.Transfer("fund",x.bank.Person(1),x.bank.Business(x.e.Id),100,0);x.e.StaffShare=20;x.book.Sale(x.e,100);
            x.book.Credit(x.e,2,ContributionKind.Production,"cook",0);x.book.Credit(x.e,3,ContributionKind.Service,"serve",0);Assert.False(x.book.Credit(x.e,2,ContributionKind.Production,"cook",0));
            x.book.Payday(x.e,x.bank,7);x.book.Payday(x.e,x.bank,7);Assert.Equal(20,x.ledger.Balance(x.bank.Person(2))+x.ledger.Balance(x.bank.Person(3)));Assert.Equal(0,x.e.BonusPool);Assert.True(x.ledger.Conserved);
        }
        [Fact] public void No_staff_pool_is_discarded_when_nobody_has_contributed()
        {
            var x=Setup();x.book.Sale(x.e,100);x.book.Payday(x.e,x.bank,7);Assert.Equal(20,x.e.BonusPool);x.book.Credit(x.e,2,ContributionKind.Production,"work",8);x.book.Payday(x.e,x.bank,15);Assert.Equal(20,x.bank.Owed(x.bank.Business(x.e.Id)));
        }
        [Fact] public void Owner_withdrawal_protects_reserve_pending_pay_bonus_and_debts()
        {
            var x=Setup();x.bank.Transfer("fund",x.bank.Person(1),x.bank.Business(x.e.Id),100,0);x.e.Reserve=10;x.book.Sale(x.e,100);x.book.WorkTime(x.e,2,100000,"sample:100000",1);x.bank.Wages("debt",x.bank.Business(x.e.Id),3,5,7);Assert.Equal(55,x.book.Withdrawable(x.e,x.bank));
        }
        [Fact] public void Small_sales_accumulate_a_fair_staff_share_instead_of_rounding_it_away()
        {
            var x=Setup();for(int i=0;i<5;i++)x.book.Sale(x.e,1);Assert.Equal(1,x.e.BonusPool);Assert.Equal(0,x.e.BonusRemainder);
        }
        [Fact] public void Working_hours_support_overnight_and_equal_hours_are_closed()
        {
            var e=new Establishment{Enabled=true,OpenHour=20,CloseHour=4};Assert.True(e.OpenAt(23));Assert.True(e.OpenAt(2));Assert.False(e.OpenAt(12));e.CloseHour=20;Assert.False(e.OpenAt(20));
        }
        [Theory][InlineData(0,0,0)][InlineData(10,20,14)][InlineData(20,20,24)][InlineData(10,4,11)]
        public void Art_only_adds_a_bounded_presentation_bonus(int cooking,int art,int expected)
        {
            Assert.Equal(expected,CulinaryCareers.Presentation(cooking,art,true));Assert.Equal(cooking,CulinaryCareers.Presentation(cooking,art,false));
        }
        [Fact] public void Specialty_practice_can_precede_qualification_but_does_not_bypass_chef()
        {
            var l=new CareerLedger();var d=CulinaryCareers.Definition();l.Award(1,d.Id,"Artisan","recipe","a",60,0);l.Evaluate(1,d,k=>20);Assert.False(l.IsQualified(1,d.Id));l.Award(1,d.Id,null,"recipe","b",250,1);l.Evaluate(1,d,k=>20);Assert.True(l.IsSpecialist(1,d.Id,"Artisan"));Assert.False(l.IsSpecialist(1,d.Id,"Bartender"));
        }
        [Fact] public void Every_role_has_five_positive_five_negative_and_five_relationship_events()
        {
            foreach(string role in new[]{"Cook","Chef","Artisan","Bartender","Innkeeper"}){var events=HospitalityEvents.All.Where(e=>e.Role==role).ToList();Assert.Equal(5,events.Count(e=>e.Mood>0));Assert.Equal(5,events.Count(e=>e.Mood<0));Assert.Equal(5,events.Count(e=>e.Opinion!=0));}
            Assert.Equal(HospitalityEvents.All.Length,HospitalityEvents.All.Select(e=>e.Id).Distinct().Count());
        }
        [Fact] public void No_event_fires_without_its_fact_and_social_events_require_a_real_other_person()
        {
            Assert.Empty(HospitalityEvents.Eligible("Cook",new HashSet<string>(),true));Assert.DoesNotContain(HospitalityEvents.Eligible("Cook",new HashSet<string>{"Compliment"},false),e=>e.Opinion!=0);
            var b=new EstablishmentBook();Assert.True(b.Memory(1,"event",2,0));Assert.False(b.Memory(1,"event",2,1));Assert.True(b.Memory(1,"event",2,3));
        }
        [Fact] public void Matching_traits_never_override_budget_urgent_departure_or_group_wishes()
        {
            var e=new Establishment{Food=90,Service=90,Comfort=90,Safety=90,Value=90,Reliability=90,Rowdiness=90};
            var happy=new VisitorPreference{Budget=100,NightPrice=8,FoodReserve=15,TravelReserve=15,Sociable=true,Experience=1};Assert.True(VisitorDecisions.ExtendGroup(e,new[]{happy},0));
            var poor=new VisitorPreference{Budget=10,NightPrice=8,FoodReserve=15,TravelReserve=15,Experience=1};Assert.False(VisitorDecisions.ExtendGroup(e,new[]{happy,poor},0));happy.Urgent=true;Assert.False(VisitorDecisions.ExtendGroup(e,new[]{happy},0));
        }
        [Fact] public void A_rowdy_venue_attracts_some_and_discourages_quiet_guests_without_using_xenotype()
        {
            var e=new Establishment{Rowdiness=90};Assert.True(new VisitorPreference{Sociable=true}.Appeal(e)>new VisitorPreference{Quiet=true}.Appeal(e));
        }
        [Fact] public void Stay_limits_and_negative_experiences_still_matter()
        {
            var p=new VisitorPreference{Budget=100,MaxDays=5,DaysStayed=5};Assert.False(p.CanExtend);var e=new Establishment();p.DaysStayed=1;p.Experience=1;float high=p.Appeal(e);p.Experience=0;Assert.True(p.Appeal(e)<high);
        }
        [Fact] public void Community_food_and_prices_affect_the_choice_to_visit()
        {
            Assert.True(VisitorDecisions.VisitUtility(.5f,.5f,.5f,.5f,5,100,.1f,false)>VisitorDecisions.VisitUtility(.5f,.5f,.5f,.5f,5,100,.1f,true));Assert.Equal(-1,VisitorDecisions.VisitUtility(1,1,1,1,10,5,0,false));
        }
        [Fact] public void Provenance_survives_stack_splits_merges_and_partial_moves()
        {
            var b=new ProductionBook();b.Lots.Add(new ProductLot{Thing=1,Worker=2,Business=3,Count=10,Origin="recipe",Day=0});b.Move(1,4,3);b.Move(4,5,2);var copy=ProductionBook.Decode(new StateReader(b.Encode()));Assert.Equal(10,copy.Lots.Sum(l=>l.Count));Assert.Equal(2,copy.Lots.Single(l=>l.Thing==5).Count);Assert.All(copy.Lots,l=>Assert.Equal("recipe",l.Origin));
        }
        [Fact] public void Saved_establishments_keep_ownership_areas_orders_earnings_guests_and_event_cooldowns()
        {
            var x=Setup();x.e.Name="Will's | Kitchen, & Inn";x.e.Owner=EstablishmentOwner.Household;x.e.OwnerId=42;x.e.ServiceCells.Add(23);x.e.StockContainers.Add(99);x.e.Staff.Add(2);x.e.Beds.Add(10);x.e.Points[2]=20;
            x.book.Begin(x.e,1,10,20,3,x.bank.Person(1),false,x.bank);x.book.Guests.Add(new GuestStay{Pawn=90,Business=x.e.Id,UntilDay=5,SleepTicks=5000,LastDecisionDay=2});x.book.Memory(1,"event",2,3);x.book.WorkTime(x.e,2,100000,"s:100000",1);
            var copy=EstablishmentBook.Decode(new StateReader(x.book.Encode()));Assert.Equal(x.book.Encode(),copy.Encode());Assert.Equal(5000,copy.Guests.Single().SleepTicks);Assert.Equal(x.e.Name,copy.ById(x.e.Id).Name);
        }
        [Fact] public void Module_payload_survives_an_absent_module_and_reappears_without_migration()
        {
            var r=new FamilyRegistry();r.Establishments().Create(1,EstablishmentKind.Inn);r.Shared.Flush();var saved=new SharedState{Ids=r.Shared.Ids.ToList(),Data=r.Shared.Data.ToList(),Versions=r.Shared.Versions.ToList()};Assert.Contains(EstablishmentBook.StateId,saved.Ids);Assert.Single(saved.Of(EstablishmentBook.StateId,EstablishmentFactory.Instance).Businesses);
        }
        [Fact] public void All_event_thought_definitions_exist_and_xml_is_well_formed()
        {
            string root=RepoPaths.Root();var doc=XDocument.Load(Path.Combine(root,"Packages/WilsHospitality/Defs/HospitalityThoughts.xml"));var names=doc.Descendants("defName").Select(x=>x.Value).ToList();foreach(var e in HospitalityEvents.All)Assert.Contains(e.Thought,names);
            foreach(var f in Directory.GetFiles(Path.Combine(root,"Packages/WilsHospitality"),"*.xml",SearchOption.AllDirectories))XDocument.Load(f);
        }
        [Fact] public void Sponsored_bookings_reserve_one_budget_and_subdivide_it_without_double_spending()
        {
            var x=Setup();var bookings=new BookingBook();var booking=bookings.Create(x.e,BookingKind.FamilyMeal,1,2,50,x.bank.Person(1),0,x.bank);Assert.NotNull(booking);Assert.Equal(150,x.bank.Available(x.bank.Person(1)));
            var order=x.book.Begin(x.e,2,10,20,1,null,false,x.bank,1,booking,bookings);Assert.NotNull(order);Assert.Equal(150,x.bank.Available(x.bank.Person(1)));order.Stage=ServiceStage.Consumed;Assert.True(x.book.Checkout(order,x.bank,0,null));
            bookings.Close(booking,x.bank);Assert.Equal(180,x.bank.Available(x.bank.Person(1)));Assert.Equal(20,x.ledger.Balance(x.bank.Business(x.e.Id)));Assert.True(x.ledger.Conserved);
            Assert.Equal(bookings.Encode(),BookingBook.Decode(new StateReader(bookings.Encode())).Encode());
        }
        [Fact] public void A_booking_cannot_overcommit_its_budget_or_appear_without_funding()
        {
            var x=Setup();var bookings=new BookingBook();Assert.Null(bookings.Create(x.e,BookingKind.CommunityMeal,0,10,1000,x.bank.Person(1),0,x.bank));var b=bookings.Create(x.e,BookingKind.FamilyMeal,1,1,10,x.bank.Person(1),0,x.bank);
            Assert.Null(x.book.Begin(x.e,2,10,11,0,null,false,x.bank,1,b,bookings));Assert.Equal(190,x.bank.Available(x.bank.Person(1)));
        }
        [Fact] public void External_refunds_require_actual_return_and_do_not_repeat_after_reload()
        {
            var x=Setup();x.bank.Transfer("fund",x.bank.Person(1),x.bank.Business(x.e.Id),50,0);int returns=0;
            Assert.False(x.bank.ExternalRefund("refund",x.bank.Business(x.e.Id),20,0,()=>false));Assert.True(x.bank.ExternalRefund("refund",x.bank.Business(x.e.Id),20,0,()=>{returns++;return true;}));
            var copy=Ledger.Decode(new StateReader(x.ledger.Encode()));Assert.True(new BusinessBank(copy).ExternalRefund("refund",x.bank.Business(x.e.Id),20,0,()=>{returns++;return true;}));Assert.Equal(1,returns);Assert.Equal(20,copy.ExternalOut);Assert.True(copy.Conserved);
        }
        [Fact] public void A_three_year_business_simulation_preserves_currency_and_every_earned_wage()
        {
            var x=Setup();int earned=0,paid=0;var random=new Random(42);
            for(int day=0;day<180;day++)
            {
                int sales=random.Next(1,20);Assert.True(x.bank.ExternalSale("sale:"+day,x.bank.Business(x.e.Id),sales,day,()=>true));x.book.Sale(x.e,sales);
                for(int worker=2;worker<=5;worker++){x.book.WorkTime(x.e,worker,10000,"clock:"+(day*60000+10000),day);x.book.Credit(x.e,worker,ContributionKind.Production,"production:"+worker+":"+day,day);}
                x.book.Payday(x.e,x.bank,day);x.book.Prune(day);Assert.True(x.ledger.Conserved);Assert.True(x.ledger.TotalAvailable<=x.ledger.TotalClaims);
            }
            x.book.Payday(x.e,x.bank,180);earned=x.ledger.Debts.Where(d=>d.Kind==DebtKind.Wage).Sum(d=>d.Original);paid=Enumerable.Range(2,4).Sum(p=>x.ledger.Balance(x.bank.Person(p)));
            Assert.Equal(earned,paid+x.bank.Owed(x.bank.Business(x.e.Id)));Assert.True(x.ledger.Conserved);
        }
        [Fact] public void Pending_checkout_defers_an_estate_without_mutating_its_debts_and_retries_after_release()
        {
            var x=Setup();string from=x.bank.Person(1),heir=x.bank.Person(2),creditor=x.bank.Person(3);var debt=x.ledger.OpenDebt(from,creditor,DebtKind.Loan,30,0,"loan");
            x.bank.Reserve("meal",from,20);Assert.Equal(LedgerResult.Insufficient,x.ledger.SettleEstate(from,new[]{heir},"estate",0));Assert.Equal(30,debt.Outstanding);Assert.False(x.ledger.Get(from).Closed);
            x.bank.Cancel("meal");Assert.Equal(LedgerResult.Ok,x.ledger.SettleEstate(from,new[]{heir},"estate",1));Assert.Equal(170,x.ledger.Balance(heir));Assert.True(x.ledger.Conserved);
        }
        [Fact] public void Economic_year_offsets_do_not_immediately_delete_recent_customer_bills()
        {
            var x=Setup();var o=x.book.Begin(x.e,1,10,20,60000,x.bank.Person(1),false,x.bank);o.Stage=ServiceStage.Consumed;x.book.Checkout(o,x.bank,330001,null);x.book.Prune(330001,62000);Assert.Single(x.book.Orders);x.book.Prune(330005,260000);Assert.Empty(x.book.Orders);
        }
        [Fact] public void Batch_production_and_split_deliveries_credit_the_same_total_output()
        {
            var whole=new EstablishmentBook();var a=whole.Create(1,EstablishmentKind.Restaurant);whole.Credit(a,1,ContributionKind.Production,"batch",1,10);
            var fragments=new EstablishmentBook();var b=fragments.Create(1,EstablishmentKind.Restaurant);fragments.Credit(b,1,ContributionKind.Production,"part-a",1,3);fragments.Credit(b,1,ContributionKind.Production,"part-b",1,7);Assert.Equal(a.Points[1],b.Points[1]);
            Assert.False(fragments.Credit(b,1,ContributionKind.Production,"part-a",1,3));
        }
    }
}
