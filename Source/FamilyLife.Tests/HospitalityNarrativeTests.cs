using System;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;
namespace FamilyLife.Tests
{
    public class HospitalityNarrativeTests
    {
        [Theory]
        [InlineData(HospitalityStoryKind.CreditClaim)]
        [InlineData(HospitalityStoryKind.FalseBlame)]
        [InlineData(HospitalityStoryKind.Disclosure)]
        [InlineData(HospitalityStoryKind.HouseRules)]
        [InlineData(HospitalityStoryKind.OwnershipClaim)]
        [InlineData(HospitalityStoryKind.FamilyPlan)]
        [InlineData(HospitalityStoryKind.Coverage)]
        public void Conversation_alone_cannot_invent_an_accusation_or_promise(HospitalityStoryKind kind)
        {Assert.False(new StoryEvidence{RealConversation=true,OtherPresent=true,ThirdPresent=true}.Allows(kind));}
        [Fact] public void Credit_theft_requires_the_actual_maker_and_a_present_witness()
        {
            var e=new StoryEvidence{RealConversation=true,OtherPresent=true,OtherOwnsWork=true};Assert.False(e.Allows(HospitalityStoryKind.CreditClaim));e.ThirdPresent=true;Assert.True(e.Allows(HospitalityStoryKind.CreditClaim));e.RealConversation=false;Assert.False(e.Allows(HospitalityStoryKind.CreditClaim));
        }
        [Fact] public void Blame_requires_recorded_failure_and_an_innocent_target()
        {
            var e=new StoryEvidence{RealConversation=true,OtherPresent=true,ActorCausedFailure=true};Assert.False(e.Allows(HospitalityStoryKind.FalseBlame));e.OtherInnocent=true;Assert.True(e.Allows(HospitalityStoryKind.FalseBlame));
        }
        [Fact] public void A_confidence_requires_real_shared_knowledge_and_disclosure_is_once_only_after_reload()
        {
            var book=new HospitalityNarrativeBook();var confidence=book.Confide(1,2,3,100);Assert.Same(confidence,book.Confide(1,2,3,200));
            Assert.False(book.Disclose(confidence.Id,9,4,true));Assert.False(book.Disclose(confidence.Id,3,4,false));Assert.False(book.Disclose(confidence.Id,3,2,true));Assert.False(book.Disclose(confidence.Id,3,3,true));
            var copy=HospitalityNarrativeBook.Decode(new StateReader(book.Encode()));Assert.True(copy.Disclose(confidence.Id,3,4,true));Assert.Contains(4,copy.Confidences.Single().Knows);Assert.False(copy.Disclose(confidence.Id,3,5,true));
            Assert.Equal(copy.Encode(),HospitalityNarrativeBook.Decode(new StateReader(copy.Encode())).Encode());
        }
        [Fact] public void Disclosure_memories_need_the_confider_to_hear_a_real_conversation()
        {
            var e=new StoryEvidence{RealConversation=true,OtherPresent=true,ThirdPresent=true,KnownConfidence=true};Assert.False(e.Allows(HospitalityStoryKind.Disclosure));e.ConfiderCanHear=true;Assert.True(e.Allows(HospitalityStoryKind.Disclosure));
        }
        [Fact] public void Missed_family_plans_use_real_broken_commitments_and_excuse_emergencies()
        {
            var b=new HospitalityNarrativeBook();var p=new Commitment{Id=1,State=0};Assert.False(b.MissedPromise(p,true));p.State=2;Assert.False(b.MissedPromise(p,false));b.ExcusedPromises.Add(1);Assert.False(b.MissedPromise(p,true));b.ExcusedPromises.Clear();Assert.True(b.MissedPromise(p,true));
            var copy=HospitalityNarrativeBook.Decode(new StateReader(b.Encode()));Assert.False(copy.MissedPromise(p,true));Assert.False(copy.MissedPromise(null,true));
        }
        [Fact] public void Queued_conversations_keep_their_order_and_participants_across_reload()
        {
            var b=new HospitalityNarrativeBook();var task=b.Schedule(7,1,2,HospitalityStoryKind.CreditClaim,100,order:9,third:3);Assert.Null(b.Schedule(7,1,4,HospitalityStoryKind.Confidence,200));Assert.Null(b.Schedule(7,2,2,HospitalityStoryKind.Confidence,200));
            var copy=HospitalityNarrativeBook.Decode(new StateReader(b.Encode()));Assert.Equal(b.Encode(),copy.Encode());Assert.Equal(9,copy.Tasks.Single().Order);Assert.Equal(3,copy.Tasks.Single().Third);copy.Tasks.Single().Completed=true;Assert.NotNull(copy.Schedule(7,1,4,HospitalityStoryKind.Confidence,200));
        }
        [Fact] public void Tips_are_real_payday_obligations_and_never_take_household_sponsor_funds()
        {
            var l=new Ledger();var bank=new BusinessBank(l);var b=new EstablishmentBook();var e=b.Create(1,EstablishmentKind.Restaurant);l.Receive(bank.Person(1),50,TxKind.Income,"seed",0);
            var order=b.Begin(e,1,2,10,0,bank.Person(1),false,bank);order.Stage=ServiceStage.Consumed;Assert.True(b.Checkout(order,bank,0,null));Assert.True(b.GiveTip(order,2,2,0,bank));Assert.False(b.GiveTip(order,2,2,0,bank));Assert.Equal(38,bank.Available(bank.Person(1)));Assert.Equal(2,b.Earnings.Single().Silver);Assert.Equal(0,l.Balance(bank.Person(2)));b.Payday(e,bank,7);Assert.Equal(2,l.Balance(bank.Person(2)));Assert.True(l.Conserved);
        }
        [Fact] public void Visitor_tips_need_physical_delivery_and_persist_without_a_second_payment()
        {
            var l=new Ledger();var bank=new BusinessBank(l);var b=new EstablishmentBook();var e=b.Create(1,EstablishmentKind.Tavern);var o=b.Begin(e,1,2,10,0,null,true,bank);o.Stage=ServiceStage.Settled;
            Assert.False(b.GiveTip(o,2,1,0,bank,()=>false));Assert.Empty(b.Earnings);Assert.Equal(0,l.ExternalIn);
            var copy=EstablishmentBook.Decode(new StateReader(b.Encode()));Assert.False(copy.GiveTip(copy.Order(o.Id),2,1,0,bank,()=>throw new Exception("duplicate")));
        }
        [Fact] public void Sponsored_visitor_tips_use_physical_cash_not_a_colony_account()
        {
            var l=new Ledger();var bank=new BusinessBank(l);var b=new EstablishmentBook();var e=b.Create(1,EstablishmentKind.Tavern);l.Receive(bank.Person(1),50,TxKind.Income,"seed",0);var o=b.Begin(e,1,2,10,0,bank.Person(1),false,bank);o.Stage=ServiceStage.Settled;bool paidCash=false;
            Assert.True(b.GiveTip(o,2,1,0,bank,()=>{paidCash=true;return true;},true));Assert.True(paidCash);Assert.Equal(40,bank.Available(bank.Person(1)));
        }
        [Fact] public void Booking_selection_skips_a_nearly_empty_booking_for_one_that_can_pay()
        {
            var l=new Ledger();var bank=new BusinessBank(l);var b=new EstablishmentBook();var e=b.Create(1,EstablishmentKind.Restaurant);l.Receive(bank.Person(1),200,TxKind.Income,"seed",0);
            var bk=new BookingBook();var small=bk.Create(e,BookingKind.FamilyMeal,0,2,1,bank.Person(1),0,bank);var big=bk.Create(e,BookingKind.FamilyMeal,0,2,50,bank.Person(1),0,bank);
            Assert.Same(big,bk.For(e.Id,0,1,5));Assert.Same(big,bk.For(e.Id,0,1));
        }
        [Fact] public void Experiment_outcome_follows_split_stock_without_awarding_extra_credit()
        {
            var b=new ProductionBook();b.Lots.Add(new ProductLot{Id=1,Thing=10,Worker=2,Business=3,Count=4,ExperimentFailed=true,Presentation=500,Credited=true,Origin="recipe:1"});b.NextId=2;b.Move(10,11,2);
            var copy=ProductionBook.Decode(new StateReader(b.Encode()));Assert.All(copy.Lots,l=>{Assert.True(l.ExperimentFailed);Assert.True(l.Credited);Assert.Equal(500,l.Presentation);});Assert.Equal(4,copy.Lots.Sum(l=>l.Count));
        }
        [Fact] public void Admission_policies_and_diet_requests_persist()
        {
            var b=new EstablishmentBook();var e=b.Create(1,EstablishmentKind.Inn);e.BannedCustomers.Add(5);e.ExcludedFamilies.Add(6);b.Orders.Add(new ServiceOrder{Id=1,DietRequest=true});var copy=EstablishmentBook.Decode(new StateReader(b.Encode()));Assert.Contains(5,copy.ById(e.Id).BannedCustomers);Assert.Contains(6,copy.ById(e.Id).ExcludedFamilies);Assert.True(copy.Order(1).DietRequest);
        }
    }
}
