using System;
using System.Collections.Generic;

namespace FamilyLife.Framework
{
    /// <summary>Optional, game-independent account capability. A provider owns money, backing, reservations and debts; consumers never copy balances.</summary>
    public interface ICommerceBank
    {
        string Business(int id);
        string Person(int id);
        string Household(int id);
        string Successor(string account);
        int Available(string account);
        int Owed(string account);
        bool Transfer(string key, string from, string to, int silver, int day, bool income = false);
        bool Reserve(string key, string account, int silver);
        bool SplitReservation(string fromKey, string toKey, int silver);
        bool Commit(string key, string destination, int day);
        void Cancel(string key);
        bool Wages(string key, string business, int worker, int silver, int dueDay);
        void Payday(string business, int day);
        bool ExternalSale(string key, string business, int silver, int day, Func<bool> deliverSilver);
        bool ExternalRefund(string key, string business, int silver, int day, Func<bool> deliverSilver);
    }

    /// <summary>Additive capability: older commerce providers still implement the unchanged base contract.
    /// Unsupported adjustments refuse safely and preserve the pending obligation.</summary>
    public interface ICommerceAdjustments
    {
        bool ReturnReservation(string fromKey,string toKey);
        bool RememberSale(string key,string business,string payer,int silver,int day);
        bool RefundSale(string key,string business,string recipient,int silver,int day,Func<bool> physicalReturn=null);
        int WagesOwed(string business,int worker,int dueBefore);
    }
    public static class CommerceBankExtensions
    {
        public static bool ReturnReservation(this ICommerceBank bank,string fromKey,string toKey)=>bank is ICommerceAdjustments advanced&&advanced.ReturnReservation(fromKey,toKey);
        public static bool RememberSale(this ICommerceBank bank,string key,string business,string payer,int silver,int day)=>bank is ICommerceAdjustments advanced&&advanced.RememberSale(key,business,payer,silver,day);
        public static bool RefundSale(this ICommerceBank bank,string key,string business,string recipient,int silver,int day,Func<bool> physicalReturn=null)=>bank is ICommerceAdjustments advanced&&advanced.RefundSale(key,business,recipient,silver,day,physicalReturn);
        public static int WagesOwed(this ICommerceBank bank,string business,int worker,int dueBefore)=>bank is ICommerceAdjustments advanced?advanced.WagesOwed(business,worker,dueBefore):0;
    }

    /// <summary>At most one provider, registered during startup. Availability follows the provider's module lifecycle.</summary>
    public sealed class CommerceCatalog
    {
        private string owner;
        private Func<ICommerceBank> factory;
        private readonly Func<string, bool> enabled;
        public CommerceCatalog(Func<string, bool> enabled) { this.enabled = enabled; }
        public void Register(string moduleId, Func<ICommerceBank> provider)
        {
            if (factory != null) throw new RegistrationException("A commerce provider is already registered by " + owner);
            owner = moduleId; factory = provider ?? throw new ArgumentNullException(nameof(provider));
        }
        public ICommerceBank Bank => factory != null && enabled(owner) ? factory() : null;
    }

}
