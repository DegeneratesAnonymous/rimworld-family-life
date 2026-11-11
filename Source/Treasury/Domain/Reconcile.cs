using System;

namespace FamilyLife.Domain
{
    public enum ReconcileAction : byte { None, AdoptSurplus }

    /// <summary>What the reconciliation found. <see cref="Unbacked"/> is the claims with no silver behind them right now (the ledger takes it as spending-power impairment); <see cref="Amount"/> is surplus to adopt.</summary>
    public struct ReconcileDecision { public ReconcileAction Action; public int Amount; public bool Initial; public int Unbacked; }

    /// <summary>
    /// Compares what accounts claim with the silver the colony physically holds (stored, on the ground, carried, in caravans) without ever hiding a difference or erasing ownership.
    /// A <b>shortage</b> is never written off here: it impairs spending power proportionally (<see cref="Ledger.Available"/>) until the silver is found or the owners explicitly accept the loss
    /// (<see cref="Ledger.WriteDown"/>). A <b>surplus</b> (silver nobody claims) is adopted by the community only when it is more than rounding noise, has stood unchanged for
    /// <see cref="SurplusGraceDays"/> days, and no reward or trade has settled in that time (so a delivery still being recognised through its proper source is not adopted as unrelated income);
    /// the one exception is the first run in a game, when existing silver is recorded once for the community.
    /// </summary>
    public static class SilverReconciler
    {
        public const int SurplusGraceDays = 2, RoundingTolerance = 3;

        public static ReconcileDecision Decide(TreasuryState st, long claims, long physical, int today, bool adopt)
        {
            long diff = physical - claims; var none = new ReconcileDecision { Action = ReconcileAction.None, Unbacked = (int)Math.Max(0, Math.Min(int.MaxValue, -diff)) };
            if (!st.Initialized)
            {
                st.Initialized = true;
                if (diff > 0 && adopt) return new ReconcileDecision { Action = ReconcileAction.AdoptSurplus, Amount = (int)Math.Min(int.MaxValue, diff), Initial = true };
                return none;
            }
            bool settling = st.LastIncomeDay != 0 && today - st.LastIncomeDay < SurplusGraceDays;
            if (diff <= RoundingTolerance || settling) { st.SurplusSinceDay = 0; st.LastSurplus = 0; return none; }
            if (st.SurplusSinceDay == 0 || st.LastSurplus <= 0) { st.SurplusSinceDay = today; st.LastSurplus = (int)Math.Min(int.MaxValue, diff); return none; }
            st.LastSurplus = (int)Math.Min(st.LastSurplus, diff);          // only the part that has stayed
            if (adopt && today - st.SurplusSinceDay >= SurplusGraceDays) { int a = st.LastSurplus; st.SurplusSinceDay = 0; st.LastSurplus = 0; return new ReconcileDecision { Action = ReconcileAction.AdoptSurplus, Amount = a }; }
            return none;
        }
    }
}
