using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>Someone who shares in income. A resident with a household is credited through the household account but keeps their attribution on the transaction; children are included.</summary>
    public struct Resident
    {
        public int PawnId, HouseholdId;
        public Resident(int pawnId, int householdId) { PawnId = pawnId; HouseholdId = householdId; }
    }

    public sealed class IncomePolicy
    {
        /// <summary>Percent of a reward the community treasury keeps. The rest is shared equally per resident.</summary>
        public int CommunityPercent = 25;
        /// <summary>Percent of an owner's own sale the community keeps (0 by default: what is yours is yours).</summary>
        public int OwnerFeePercent = 0;
    }

    /// <summary>Splitting silver that really arrived. Pure integer arithmetic: nothing is rounded into existence, the odd silver goes to the community, and one key makes it happen once.</summary>
    public static class Income
    {
        public static string AccountFor(Resident r) => Ledger.IdFor(r.HouseholdId > 0 ? AccountKind.Household : AccountKind.Person, r.HouseholdId > 0 ? r.HouseholdId : r.PawnId);

        /// <summary>Shares a reward. The community keeps its percentage plus any remainder; every resident gets an equal share, credited to their household if they have one.</summary>
        public static LedgerResult DistributeReward(Ledger ledger, int amount, IList<Resident> residents, IncomePolicy policy, string key, int day, TxKind kind = TxKind.Reward)
        {
            if (amount <= 0) return LedgerResult.Invalid;
            var people = residents.GroupBy(r => r.PawnId).Select(g => g.First()).OrderBy(r => r.PawnId).ToList();
            int pct = Math.Max(0, Math.Min(100, policy?.CommunityPercent ?? 25));
            int community = (int)((long)amount * pct / 100), rest = amount - community;
            var moves = new List<Move>();
            if (people.Count == 0) community += rest;
            else
            {
                int per = rest / people.Count; community += rest - per * people.Count;
                foreach (var p in people)
                {
                    if (per <= 0) break;
                    ledger.Ensure(p.HouseholdId > 0 ? AccountKind.Household : AccountKind.Person, p.HouseholdId > 0 ? p.HouseholdId : p.PawnId);
                    moves.Add(new Move(null, AccountFor(p), per, "share p:" + p.PawnId));
                }
            }
            if (community > 0) moves.Add(new Move(null, Ledger.CommunityId, community, "community share"));
            return ledger.Apply(key, day, kind, moves);
        }

        /// <summary>One thing sold: its price, who owned it (an account id) or null for a communal good, and what the community paid for it (so only the profit is shared).</summary>
        public struct Sale { public int Proceeds, CostBasis; public string OwnerAccount; }

        /// <summary>
        /// Sale proceeds. An owned item's proceeds go to its owner (less any configured fee). A communal item first returns its cost basis to the community; only what is left is profit and is
        /// shared by the reward policy, so selling something the community bought does not look like new wealth.
        /// </summary>
        public static List<Move> SaleMoves(Ledger ledger, IEnumerable<Sale> sales, IList<Resident> residents, IncomePolicy policy, string memo)
        {
            var moves = new List<Move>(); int communal = 0, profit = 0;
            foreach (var s in sales)
            {
                if (s.Proceeds <= 0) continue;
                if (s.OwnerAccount != null && ledger.Get(s.OwnerAccount) != null && !ledger.Get(s.OwnerAccount).Closed)
                {
                    int fee = (int)((long)s.Proceeds * Math.Max(0, Math.Min(100, policy?.OwnerFeePercent ?? 0)) / 100);
                    if (s.Proceeds - fee > 0) moves.Add(new Move(null, s.OwnerAccount, s.Proceeds - fee, memo + " (owner)"));
                    communal += fee;
                }
                else { int basis = Math.Max(0, Math.Min(s.CostBasis, s.Proceeds)); communal += basis; profit += s.Proceeds - basis; }
            }
            if (communal > 0) moves.Add(new Move(null, Ledger.CommunityId, communal, memo + " (community)"));
            if (profit > 0)
            {
                var tmp = new Ledger(); // shares are computed on a scratch ledger so the real batch stays a single atomic application
                foreach (var r in residents) tmp.Ensure(r.HouseholdId > 0 ? AccountKind.Household : AccountKind.Person, r.HouseholdId > 0 ? r.HouseholdId : r.PawnId);
                DistributeReward(tmp, profit, residents, policy, "x", 0, TxKind.Trade);
                foreach (var t in tmp.History) { if (ledger.Get(t.To) == null) { var a = tmp.Get(t.To); ledger.Ensure(a.Kind, a.Owner); } moves.Add(new Move(null, t.To, t.Amount, memo + " " + t.Memo)); }
            }
            return moves;
        }
    }

    /// <summary>The funding of a purchase from several accounts. Nothing is written until <see cref="Trade.Settle"/>, so a trade that is cancelled leaves every account as it was.</summary>
    public sealed class FundingPlan
    {
        public bool Ok;
        public int Cost, FromCommunity, Unattributed;
        public List<KeyValuePair<string, int>> Legs = new List<KeyValuePair<string, int>>();          // account -> amount (the community's leg included)
    }

    public static class Trade
    {
        /// <summary>
        /// Treasury first, then the personal and household accounts in proportion to their balances (largest-remainder rounding, never more than an account holds). If the accounts together hold
        /// less than the cost, the rest is "unattributed": silver the colony physically has that no account claims. It is paid, not invented, and moves no account. A purchase larger than the
        /// silver that physically exists at the trade (<paramref name="physicalAvailable"/>) is refused: an account balance never makes silver appear in a distant caravan.
        /// </summary>
        public static FundingPlan Plan(Ledger ledger, int cost, int physicalAvailable)
        {
            var plan = new FundingPlan { Cost = cost };
            if (cost < 0 || cost > physicalAvailable) return plan;
            if (cost == 0) { plan.Ok = true; return plan; }
            int fromCommunity = Math.Min(cost, ledger.Available(Ledger.CommunityId));          // only backed silver can be spent
            plan.FromCommunity = fromCommunity;
            if (fromCommunity > 0) plan.Legs.Add(new KeyValuePair<string, int>(Ledger.CommunityId, fromCommunity));
            int need = cost - fromCommunity;
            if (need > 0)
            {
                var pool = ledger.Accounts.Where(a => !a.Closed && a.Kind != AccountKind.Community && ledger.Available(a.Id) > 0).OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
                long total = pool.Sum(a => (long)ledger.Available(a.Id));
                int take = (int)Math.Min(need, total);
                if (take > 0)
                {
                    var shares = pool.Select(a => new { a, av = ledger.Available(a.Id), exact = (long)take * ledger.Available(a.Id), floor = (int)((long)take * ledger.Available(a.Id) / total) }).ToList();
                    int given = shares.Sum(s => s.floor), left = take - given;
                    var order = shares.OrderByDescending(s => s.exact - (long)s.floor * total).ThenBy(s => s.a.Id, StringComparer.Ordinal).ToList();
                    var extra = new HashSet<string>(order.Take(left).Select(s => s.a.Id));
                    foreach (var s in shares) { int amt = Math.Min(s.av, s.floor + (extra.Contains(s.a.Id) ? 1 : 0)); if (amt > 0) plan.Legs.Add(new KeyValuePair<string, int>(s.a.Id, amt)); }
                }
                plan.Unattributed = need - plan.Legs.Where(l => l.Key != Ledger.CommunityId).Sum(l => l.Value);
            }
            plan.Ok = true;
            return plan;
        }

        /// <summary>
        /// Records one completed trade, once (the key is the trade's own id): the funded purchase leaves the accounts, the sale proceeds arrive by owner. A trade that did not complete is simply not
        /// settled, so nothing changes. Mixed purchases and sales are one atomic batch.
        /// </summary>
        public static LedgerResult Settle(Ledger ledger, string tradeId, int day, FundingPlan purchases, IEnumerable<Income.Sale> sales, IList<Resident> residents, IncomePolicy policy)
        {
            var moves = new List<Move>();
            if (purchases != null && purchases.Ok) foreach (var leg in purchases.Legs) moves.Add(new Move(leg.Key, null, leg.Value, "purchase"));
            if (sales != null) moves.AddRange(Income.SaleMoves(ledger, sales, residents ?? new Resident[0], policy, "sale"));
            if (moves.Count == 0) return ledger.Applied("trade:" + tradeId) ? LedgerResult.Duplicate : LedgerResult.Ok;          // nothing moved: not a financial event
            return ledger.Apply("trade:" + tradeId, day, TxKind.Trade, moves);
        }
    }
}
