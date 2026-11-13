using System;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    /// <summary>Treasury's implementation of the shared business contract. Internal purchases transfer claims; payroll creates liabilities, never currency.</summary>
    public sealed class BusinessBank : ICommerceBank, ICommerceAdjustments
    {
        private readonly Ledger ledger;
        public BusinessBank(Ledger ledger) { this.ledger = ledger; }
        public string Business(int id) => ledger.Ensure(AccountKind.Business, id).Id;
        public string Person(int id) => ledger.Ensure(AccountKind.Person, id).Id;
        public string Household(int id) => ledger.Ensure(AccountKind.Household, id).Id;
        public string Successor(string account)
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            while (ledger.Get(account)?.Closed == true && seen.Add(account)) account = ledger.Get(account).Successor;
            return ledger.Get(account)?.Closed == false ? account : Ledger.CommunityId;
        }
        public int Available(string account) => ledger.Available(account);
        public int Owed(string account) => ledger.Liabilities(account);
        public bool Transfer(string key, string from, string to, int silver, int day, bool income = false)
        {
            var result = ledger.Transfer(from, to, silver, income ? TxKind.Trade : TxKind.Transfer, key, day, "establishment");
            return result == LedgerResult.Ok || result == LedgerResult.Duplicate;
        }
        public bool Reserve(string key, string account, int silver) => ledger.Hold(key, account, silver);
        public bool SplitReservation(string fromKey, string toKey, int silver) => ledger.SplitHold(fromKey,toKey,silver);
        public bool Commit(string key, string destination, int day) => ledger.CommitHold(key, destination, day);
        public bool ReturnReservation(string fromKey,string toKey)=>ledger.MergeHold(fromKey,toKey);
        public bool RememberSale(string key,string business,string payer,int silver,int day)=>ledger.RememberSale(key,business,payer,silver,day);
        public bool RefundSale(string key,string business,string recipient,int silver,int day,Func<bool> physicalReturn=null)
        {
            var receipt=ledger.Receipt(key);string refundKey="refund:"+key;
            if(receipt==null||receipt.Business!=business||silver<=0||silver!=receipt.Gross||receipt.Payer!=null&&recipient!=Successor(receipt.Payer))return false;
            if(ledger.Applied(refundKey))return receipt.Refunded==silver;
            if(receipt.Refunded!=0||silver!=receipt.Gross||Available(business)<silver)return false;
            if(receipt.Payer==null)
            {
                if(physicalReturn==null||!physicalReturn())return false;
                if(ledger.Spend(business,silver,TxKind.Spend,refundKey,day,"refund of "+key)!=LedgerResult.Ok)return false;
            }
            else
            {
                if(recipient!=Successor(receipt.Payer))return false;
                if(ledger.Transfer(business,recipient,silver,TxKind.Transfer,refundKey,day,"refund of "+key)!=LedgerResult.Ok)return false;
            }
            return ledger.RecordRefund(key,silver);
        }
        public void Cancel(string key) => ledger.ReleaseHold(key);
        public int WagesOwed(string business,int worker,int dueBefore)=>ledger.Debts.Where(d=>d.Active&&d.Kind==DebtKind.Wage&&d.Debtor==business&&d.Creditor==Ledger.IdFor(AccountKind.Person,worker)&&d.DueDay<=dueBefore).Sum(d=>d.Outstanding);
        public bool Wages(string key, string business, int worker, int silver, int dueDay)
        {
            if (silver <= 0 || worker <= 0 || ledger.Get(business)?.Kind != AccountKind.Business || ledger.Get(business).Closed) return false;
            string origin = "business wage:" + key;
            var old=ledger.Debts.FirstOrDefault(d=>d.Origin==origin);
            if(old!=null)return old.Debtor==business&&old.Original==silver&&old.DueDay==dueDay&&(old.Creditor==Person(worker)||old.Creditor==Successor(Person(worker)));
            return ledger.OpenDebt(business, Successor(Person(worker)), DebtKind.Wage, silver, dueDay, origin) != null;
        }
        public bool ExternalSale(string key, string business, int silver, int day, Func<bool> deliverSilver)
        {
            if (ledger.Applied(key)) return ledger.RememberSale(key,business,null,silver,day);
            if (silver <= 0 || ledger.Get(business)?.Kind != AccountKind.Business || deliverSilver == null || !deliverSilver()) return false;
            return ledger.Receive(business, silver, TxKind.Trade, key, day, "visitor purchase; physical silver delivered") == LedgerResult.Ok && ledger.RememberSale(key,business,null,silver,day);
        }
        public bool ExternalRefund(string key,string business,int silver,int day,Func<bool> deliverSilver)
        {
            if(ledger.Applied(key))return true;
            if(silver<=0||Available(business)<silver||deliverSilver==null||!deliverSilver())return false;
            return ledger.Spend(business,silver,TxKind.Spend,key,day,"visitor refund; physical silver returned")==LedgerResult.Ok;
        }
        public void Payday(string business, int day)
        {
            var due = ledger.Debts.Where(d => d.Active && d.Kind == DebtKind.Wage && d.Debtor == business && d.DueDay <= day).OrderBy(d => d.Id).ToList();
            long total = due.Sum(d => (long)d.Outstanding); int pool = (int)Math.Min(total, Available(business));
            if (pool <= 0) return;
            var shares = due.ToDictionary(d => d.Id, d => (int)((long)pool * d.Outstanding / total));
            int left = pool - shares.Values.Sum();
            foreach (var d in due.OrderByDescending(d => (long)pool * d.Outstanding % total).ThenBy(d => d.Id).Take(left)) shares[d.Id]++;
            var moves = due.Where(d => shares[d.Id] > 0).Select(d => new Move(business, Successor(d.Creditor), shares[d.Id], "business wages " + d.Id)).ToList();
            if (moves.Count == 0 || ledger.Apply("business-pay:" + business + ":" + day, day, TxKind.Payroll, moves) != LedgerResult.Ok) return;
            foreach (var d in due) if (shares[d.Id] > 0) ledger.RecordPayment(d, shares[d.Id], day);
        }
    }
}
