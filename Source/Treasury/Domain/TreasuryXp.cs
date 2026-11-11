using System;

namespace FamilyLife.Domain
{
    public enum FinancialOp : byte { Payday, RewardDistribution, PooledTrade, Deposit, Collection }

    /// <summary>
    /// Treasury experience comes only from real financial operations, each with its own unique key (so a reconciliation or a replay can never grant it twice). Bigger colonies handle more people's
    /// money per operation, so payroll and distributions are worth a little more there; deposits and collections are per piece of physical work and do not scale.
    /// </summary>
    public static class TreasuryXp
    {
        public const int Payday = 8, Reward = 6, Trade = 4, Deposit = 3, Collection = 6, MaxSizeBonus = 6, PeoplePerBonus = 6;

        public static int Points(FinancialOp op, int residents)
        {
            int bonus = Math.Min(MaxSizeBonus, Math.Max(0, residents) / PeoplePerBonus);
            switch (op)
            {
                case FinancialOp.Payday: return Payday + bonus;
                case FinancialOp.RewardDistribution: return Reward + bonus / 2;
                case FinancialOp.PooledTrade: return Trade + bonus / 3;
                case FinancialOp.Deposit: return Deposit;
                default: return Collection;
            }
        }

        /// <summary>Experience handled away from the office counts for half: the operation was legitimate, but the office was not used.</summary>
        public static int AwayFromOffice(int points) => Math.Max(1, points / 2);
    }

    /// <summary>The business waiting for the Treasurer to deal with at the office. In memory only: if it is lost in a reload nothing financial is lost, because the operation itself already settled.</summary>
    public sealed class OfficeItem { public string Key; public int Points, ExpiresDay; }
}
