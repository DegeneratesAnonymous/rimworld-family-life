using System;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;
namespace FamilyLife.Tests
{
    public class HospitalityFinancialEdgeTests
    {
        private static (Ledger ledger,BusinessBank bank,EstablishmentBook book,Establishment e) Setup()
        {
            var ledger=new Ledger();var bank=new BusinessBank(ledger);var book=new EstablishmentBook();var e=book.Create(1,EstablishmentKind.Inn);ledger.Receive(bank.Person(1),200,TxKind.Income,"seed",0);bank.Business(e.Id);return(ledger,bank,book,e);
        }
        [Fact] public void Cancelled_booking_orders_recycle_the_exact_budget_once_even_after_reload()
        {
            var x=Setup();var bookings=new BookingBook();var booking=bookings.Create(x.e,BookingKind.FamilyMeal,1,2,50,x.bank.Person(1),0,x.bank);var order=x.book.Begin(x.e,2,10,20,1,null,false,x.bank,1,booking,bookings);
            var restored=BookingBook.Decode(new StateReader(bookings.Encode()));var saved=EstablishmentBook.Decode(new StateReader(x.book.Encode()));var bank=new BusinessBank(Ledger.Decode(new StateReader(x.ledger.Encode())));order=saved.Order(order.Id);booking=restored.Bookings.Single();
            Assert.True(saved.Cancel(order,bank,restored));Assert.True(saved.Cancel(order,bank,restored));Assert.Equal(0,booking.Committed);Assert.Equal(150,bank.Available(bank.Person(1)));Assert.NotNull(saved.Begin(saved.ById(x.e.Id),3,11,50,2,null,false,bank,1,booking,restored));
        }
        [Fact] public void Closing_a_booking_does_not_reopen_it_when_a_pending_order_cancels()
        {
            var x=Setup();var bookings=new BookingBook();var b=bookings.Create(x.e,BookingKind.FamilyMeal,1,2,50,x.bank.Person(1),0,x.bank);var order=x.book.Begin(x.e,2,10,20,1,null,false,x.bank,1,b,bookings);bookings.Close(b,x.bank);Assert.Equal(180,x.bank.Available(x.bank.Person(1)));Assert.True(x.book.Cancel(order,x.bank,bookings));Assert.Equal(200,x.bank.Available(x.bank.Person(1)));Assert.True(b.Closed);
        }
        [Fact] public void An_absent_provider_cannot_silently_orphan_a_resident_reservation()
        {
            var x=Setup();var o=x.book.Begin(x.e,1,10,20,0,x.bank.Person(1),false,x.bank);Assert.False(x.book.Cancel(o,null));Assert.Equal(ServiceStage.Ordered,o.Stage);Assert.Equal(180,x.bank.Available(x.bank.Person(1)));
        }
        [Fact] public void Returning_a_hold_to_another_accounts_budget_is_rejected_atomically()
        {
            var x=Setup();x.ledger.Receive(x.bank.Person(2),20,TxKind.Income,"seed2",0);x.bank.Reserve("one",x.bank.Person(1),20);x.bank.Reserve("two",x.bank.Person(2),20);Assert.False(x.bank.ReturnReservation("one","two"));Assert.Equal(20,x.ledger.Held(x.bank.Person(1)));Assert.Equal(20,x.ledger.Held(x.bank.Person(2)));
        }
        [Fact] public void Refund_reverses_business_taxable_income_and_cannot_be_replayed_or_redirected()
        {
            var x=Setup();var o=x.book.Begin(x.e,1,10,40,0,x.bank.Person(1),false,x.bank);o.Stage=ServiceStage.Consumed;x.book.Checkout(o,x.bank,0,null);string sale="hold:"+o.Key;
            Assert.Equal(40,x.ledger.IncomeSince(x.bank.Business(x.e.Id)));Assert.False(x.bank.RefundSale(sale,x.bank.Business(x.e.Id),x.bank.Person(3),40,0));Assert.False(x.bank.RefundSale(sale,x.bank.Business(x.e.Id),x.bank.Person(1),41,0));
            Assert.True(x.bank.RefundSale(sale,x.bank.Business(x.e.Id),x.bank.Person(1),40,0));Assert.True(x.bank.RefundSale(sale,x.bank.Business(x.e.Id),x.bank.Person(1),40,0));Assert.Equal(0,x.ledger.IncomeSince(x.bank.Business(x.e.Id)));Assert.Equal(200,x.ledger.Balance(x.bank.Person(1)));Assert.True(x.ledger.Conserved);
        }
        [Fact] public void Refund_after_assessment_carries_a_deduction_across_empty_periods_and_reload()
        {
            var x=Setup();x.bank.ExternalSale("sale",x.bank.Business(x.e.Id),50,0,()=>true);Assert.Equal(50,x.ledger.TakeIncome(x.bank.Business(x.e.Id)));Assert.True(x.bank.RefundSale("sale",x.bank.Business(x.e.Id),null,50,1,()=>true));Assert.Equal(-50,x.ledger.IncomeSince(x.bank.Business(x.e.Id)));Assert.Equal(0,x.ledger.TakeIncome(x.bank.Business(x.e.Id)));
            var restored=Ledger.Decode(new StateReader(x.ledger.Encode()));var bank=new BusinessBank(restored);bank.ExternalSale("new",bank.Business(x.e.Id),70,2,()=>true);Assert.Equal(20,restored.TakeIncome(bank.Business(x.e.Id)));Assert.True(restored.Conserved);
        }
        [Fact] public void Visitor_refund_requires_physical_return_and_receipt_survives_history_trimming()
        {
            var x=Setup();x.bank.ExternalSale("sale",x.bank.Business(x.e.Id),30,0,()=>true);for(int i=0;i<700;i++){x.bank.Transfer("a"+i,x.bank.Person(1),x.bank.Person(2),1,0);x.bank.Transfer("b"+i,x.bank.Person(2),x.bank.Person(1),1,0);}
            Assert.DoesNotContain(x.ledger.History,h=>h.Key=="sale");Assert.False(x.bank.RefundSale("sale",x.bank.Business(x.e.Id),null,30,1,()=>false));Assert.Equal(30,x.ledger.IncomeSince(x.bank.Business(x.e.Id)));Assert.True(x.bank.RefundSale("sale",x.bank.Business(x.e.Id),null,30,1,()=>true));Assert.Equal(0,x.ledger.IncomeSince(x.bank.Business(x.e.Id)));Assert.True(x.ledger.Conserved);
        }
        [Fact] public void Paid_business_wages_reduce_taxable_business_income_but_create_no_currency()
        {
            var x=Setup();x.bank.ExternalSale("sale",x.bank.Business(x.e.Id),100,0,()=>true);x.bank.Wages("work",x.bank.Business(x.e.Id),2,20,7);Assert.Equal(100,x.ledger.IncomeSince(x.bank.Business(x.e.Id)));x.bank.Payday(x.bank.Business(x.e.Id),7);Assert.Equal(80,x.ledger.IncomeSince(x.bank.Business(x.e.Id)));Assert.Equal(20,x.ledger.IncomeSince(x.bank.Person(2)));Assert.True(x.ledger.Conserved);
        }
        [Fact] public void Cancelling_a_household_purchase_restores_its_shared_allowance_once()
        {
            var x=Setup();x.book.HouseholdSpent["h:5:10"]=30;var o=x.book.Begin(x.e,1,10,20,0,x.bank.Person(1),false,x.bank);o.AllowanceAccount="h:5";o.AllowanceDay=10;x.book.Cancel(o,x.bank);x.book.Cancel(o,x.bank);Assert.Equal(10,x.book.HouseholdSpent["h:5:10"]);
        }
        [Fact] public void Group_wallets_include_pack_animals_and_cannot_reserve_the_same_coins_twice()
        {
            var wallets=new VisitorWalletBook();var members=new[]{new WalletMember(1,5),new WalletMember(2,10),new WalletMember(3,100)};Assert.True(wallets.Reserve("order",1,members,40));Assert.Equal(new[]{5,10,25},wallets.Find("order").Pledges.Select(p=>p.Silver));Assert.Equal(75,wallets.Available(members));Assert.False(wallets.Reserve("other",2,members,76));Assert.True(wallets.Reserve("order",1,members,40));Assert.Equal(75,wallets.Available(members));
        }
        [Fact] public void Changed_group_funds_can_replan_a_payment_without_losing_the_old_hold_on_failure()
        {
            var wallets=new VisitorWalletBook();wallets.Reserve("order",1,new[]{new WalletMember(1,20)},20);Assert.False(wallets.Replan("order",1,new[]{new WalletMember(2,10)},20));Assert.Equal(20,wallets.Held(1));Assert.True(wallets.Replan("order",1,new[]{new WalletMember(2,30)},20));Assert.Equal(0,wallets.Held(1));Assert.Equal(20,wallets.Held(2));
        }
        [Fact] public void Group_wallet_holds_survive_reload_and_cancel_restores_availability()
        {
            var wallets=new VisitorWalletBook();var members=new[]{new WalletMember(1,20),new WalletMember(2,50)};wallets.Reserve("order",1,members,45);var copy=VisitorWalletBook.Decode(new StateReader(wallets.Encode()));Assert.Equal(wallets.Encode(),copy.Encode());Assert.Equal(25,copy.Available(members));copy.Release("order");copy.Release("order");Assert.Equal(70,copy.Available(members));
        }
        [Fact] public void Provider_absence_keeps_sponsored_booking_open_until_its_hold_can_be_released()
        {
            var x=Setup();var b=new BookingBook();var booking=b.Create(x.e,BookingKind.CommunityMeal,0,2,50,x.bank.Person(1),0,x.bank);Assert.False(b.Close(booking,null));Assert.False(booking.Closed);Assert.Equal(50,x.ledger.Held(x.bank.Person(1)));Assert.False(b.ReserveOrder(booking,"order",10,null));Assert.True(b.Close(booking,x.bank));Assert.True(booking.Closed);Assert.Equal(200,x.bank.Available(x.bank.Person(1)));
        }
        [Fact] public void Business_wages_posted_after_death_follow_the_recorded_successor()
        {
            var x=Setup();x.bank.Person(2);x.bank.Person(3);Assert.Equal(LedgerResult.Ok,x.ledger.SettleEstate(x.bank.Person(2),new[]{x.bank.Person(3)},"estate",0));x.bank.Transfer("fund",x.bank.Person(1),x.bank.Business(x.e.Id),20,0);
            Assert.True(x.bank.Wages("work",x.bank.Business(x.e.Id),2,10,7));Assert.True(x.bank.Wages("work",x.bank.Business(x.e.Id),2,10,7));Assert.False(x.bank.Wages("work",x.bank.Business(x.e.Id),2,11,7));x.bank.Payday(x.bank.Business(x.e.Id),7);Assert.Equal(10,x.ledger.Balance(x.bank.Person(3)));Assert.Equal(0,x.ledger.Balance(x.bank.Person(2)));Assert.True(x.ledger.Conserved);
        }
        [Fact] public void A_refund_after_payer_death_returns_to_the_same_recorded_estate_successor()
        {
            var x=Setup();var o=x.book.Begin(x.e,1,10,20,0,x.bank.Person(1),false,x.bank);o.Stage=ServiceStage.Consumed;x.book.Checkout(o,x.bank,0,null);x.bank.Person(2);x.ledger.SettleEstate(x.bank.Person(1),new[]{x.bank.Person(2)},"estate",0);
            Assert.False(x.bank.RefundSale("hold:"+o.Key,x.bank.Business(x.e.Id),x.bank.Person(3),20,1));Assert.True(x.bank.RefundSale("hold:"+o.Key,x.bank.Business(x.e.Id),x.bank.Person(2),20,1));Assert.Equal(200,x.ledger.Balance(x.bank.Person(2)));Assert.True(x.ledger.Conserved);
        }
    }
}
