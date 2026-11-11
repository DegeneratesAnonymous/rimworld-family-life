using System;

namespace FamilyLife.Domain
{
    public enum ReconcileAction : byte { None, AdoptSurplus, WriteOffLoss }

    /// <summary>What the reconciliation found and decided. <see cref="Amount"/> is the silver to adopt or write off.</summary>
    public struct ReconcileDecision { public ReconcileAction Action; public int Amount; public bool Initial; public int Discrepancy; }

    /// <summary>
    /// Compares what accounts claim with the silver physically in storage, without ever hiding a difference. Surplus (silver nobody claims) is adopted by the community only after it has stayed for
    /// a day (so a quest drop landing or a trade settling is not mistaken for it), except the first time the module runs in a game, when existing silver is recorded once. A shortfall (claims above
    /// the silver in storage) is only written off after it has persisted for several days, and only from the community's own account; ownership of personal and household money is never erased,
    /// and whatever cannot be covered stays visible as a discrepancy.
    /// </summary>
    public static class SilverReconciler
    {
        public const int LossGraceDays = 3, SurplusGraceDays = 1;

        public static ReconcileDecision Decide(TreasuryState st, long claims, int physical, int communityBalance, int today, bool adopt)
        {
            long diff = physical - claims; var none = new ReconcileDecision { Action = ReconcileAction.None, Discrepancy = (int)Math.Max(0, claims - physical) };
            if (!st.Initialized)
            {
                st.Initialized = true;
                if (diff > 0 && adopt) return new ReconcileDecision { Action = ReconcileAction.AdoptSurplus, Amount = (int)Math.Min(int.MaxValue, diff), Initial = true };
                return none;
            }
            if (diff > 0)
            {
                st.ShortSinceDay = 0;
                if (st.SurplusSinceDay == 0 || st.LastSurplus <= 0) { st.SurplusSinceDay = today; st.LastSurplus = (int)Math.Min(int.MaxValue, diff); return none; }
                st.LastSurplus = (int)Math.Min(st.LastSurplus, diff);          // only the part that has stayed
                if (adopt && today - st.SurplusSinceDay >= SurplusGraceDays) { int a = st.LastSurplus; st.SurplusSinceDay = 0; st.LastSurplus = 0; return new ReconcileDecision { Action = ReconcileAction.AdoptSurplus, Amount = a }; }
                return none;
            }
            st.SurplusSinceDay = 0; st.LastSurplus = 0;
            if (diff == 0) { st.ShortSinceDay = 0; return none; }
            if (st.ShortSinceDay == 0) st.ShortSinceDay = today;
            if (today - st.ShortSinceDay >= LossGraceDays && communityBalance > 0)
            {
                int loss = (int)Math.Min(-diff, communityBalance); st.ShortSinceDay = 0;
                return new ReconcileDecision { Action = ReconcileAction.WriteOffLoss, Amount = loss, Discrepancy = (int)(-diff - loss) };
            }
            return none;
        }
    }
}
