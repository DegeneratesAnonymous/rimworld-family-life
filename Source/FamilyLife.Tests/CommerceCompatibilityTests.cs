using System;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;
namespace FamilyLife.Tests
{
    public class CommerceCompatibilityTests
    {
        // Deliberately implements only the original provider API, as an older compiled provider would.
        private sealed class LegacyBank:ICommerceBank
        {
            public string Business(int id)=>"b:"+id;public string Person(int id)=>"p:"+id;public string Household(int id)=>"h:"+id;public string Successor(string account)=>account;
            public int Available(string account)=>10;public int Owed(string account)=>0;
            public bool Transfer(string key,string from,string to,int silver,int day,bool income=false)=>throw new Exception("unsafe fallback");
            public bool Reserve(string key,string account,int silver)=>true;
            public bool SplitReservation(string fromKey,string toKey,int silver)=>true;
            public bool Commit(string key,string destination,int day)=>true;
            public void Cancel(string key)=>throw new Exception("must preserve hold");
            public bool Wages(string key,string business,int worker,int silver,int dueDay)=>true;
            public void Payday(string business,int day){}
            public bool ExternalSale(string key,string business,int silver,int day,Func<bool> deliverSilver)=>true;
            public bool ExternalRefund(string key,string business,int silver,int day,Func<bool> deliverSilver)=>throw new Exception("unbound refund");
        }
        [Fact] public void An_older_provider_loads_but_cannot_silently_release_or_refund_an_unverified_sale()
        {
            ICommerceBank bank=new LegacyBank();Assert.False(bank.ReturnReservation("child","parent"));Assert.False(bank.RememberSale("sale","b:1","p:1",10,0));Assert.False(bank.RefundSale("sale","b:1","p:1",10,0,()=>throw new Exception("physical money moved")));Assert.Equal(0,bank.WagesOwed("b:1",1,0));
        }
        [Fact] public void The_current_provider_exposes_adjustments_through_the_base_contract()
        {
            var ledger=new Ledger();ICommerceBank bank=new BusinessBank(ledger);ledger.Receive(bank.Person(1),30,TxKind.Income,"seed",0);bank.Business(1);bank.Reserve("parent","p:1",20);bank.SplitReservation("parent","child",10);Assert.True(bank.ReturnReservation("child","parent"));Assert.Equal(20,ledger.Held("p:1"));Assert.True(bank.ExternalSale("sale","b:1",10,0,()=>true));Assert.True(bank.RefundSale("sale","b:1",null,10,0,()=>true));Assert.True(ledger.Conserved);
        }
    }
}
