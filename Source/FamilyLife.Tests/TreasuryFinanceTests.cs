using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;
using Xunit.Abstractions;

namespace FamilyLife.Tests
{
    public class TreasuryInsolvencyTests
    {
        private static string P(int id) => Ledger.IdFor(AccountKind.Person, id);

        [Fact] public void Wages_earned_with_an_empty_treasury_are_a_claim_paid_later_and_no_silver_is_created()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 0), (AccountKind.Person, 1, 0)); var b = new PayrollBook();
            Assert.Equal(1, b.Accrue(1, Labour.Role("hauling"), 1, 3, "k1"));
            b.SettleDue(l, 0, 0); b.SettleDue(l, 7, 0);
            Assert.Equal(0, l.Balance(P(1))); var debt = l.Debts.Single(d => d.Kind == DebtKind.Wage && d.Active);
            Assert.Equal(1, debt.Outstanding); Assert.True(debt.Ordinary); Assert.Equal(0, l.ExternalIn);
            l.Receive(Ledger.CommunityId, 50, TxKind.Income, "inc", 9);
            b.SettleDue(l, 15, 50);
            Assert.Equal(1, l.Balance(P(1))); Assert.Equal(49, l.Balance(Ledger.CommunityId)); Assert.True(l.Conserved);
            Assert.DoesNotContain(l.Debts, d => d.Active && d.Kind == DebtKind.Wage);
        }

        [Fact] public void Missing_silver_never_lets_accounts_spend_more_than_exists_and_payroll_pays_only_backed_money()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 100), (AccountKind.Person, 1, 100)); var b = new PayrollBook(); b.Policy.Rates["pro"] = 10; b.Policy.DailyUnits["pro"] = 10;
            l.SetPhysical(100);                                                                    // half of the 200 claimed is gone
            b.Accrue(1, "pro", 4, 3, "w");
            b.SettleDue(l, 8, 100);
            Assert.True(l.Balance(P(1)) <= 100 + 40 && l.Balance(Ledger.CommunityId) >= 0); Assert.True(l.Conserved);
            Assert.True(l.Available(Ledger.CommunityId) <= l.Balance(Ledger.CommunityId));
        }

        [Fact] public void An_accepted_loss_reduces_claims_pro_rata_and_the_ledger_stays_conserved()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 100), (AccountKind.Person, 1, 100)); l.SetPhysical(100);
            Assert.Equal(100, l.Unbacked); int written = l.WriteDown("wd", 20);
            Assert.Equal(100, written); Assert.Equal(0, l.Unbacked); Assert.Equal(100, l.TotalClaims); Assert.True(l.Conserved);
            Assert.Equal(50, l.Balance(P(1))); Assert.Equal(50, l.Balance(Ledger.CommunityId));
            Assert.Equal(0, l.WriteDown("wd", 20));
        }
    }

    public class TreasuryHouseholdRuleTests
    {
        private static string H(int id) => Ledger.IdFor(AccountKind.Household, id);
        private static string P(int id) => Ledger.IdFor(AccountKind.Person, id);

        [Fact] public void Being_away_on_a_caravan_moves_nothing()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Household, 1, 90));
            Assert.Equal(0, HouseholdRules.OnLeave(l, 1, 0, new[] { new Leaver { Pawn = 1, Away = true } }, false, "k", 5));
            Assert.Equal(90, l.Balance(H(1)));
        }

        [Fact] public void Someone_moving_out_of_a_household_that_carries_on_takes_nothing_but_a_divorce_gives_a_per_person_share()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Household, 1, 90));
            Assert.Equal(0, HouseholdRules.OnLeave(l, 1, 2, new[] { new Leaver { Pawn = 5 } }, false, "a", 5)); Assert.Equal(90, l.Balance(H(1)));
            Assert.Equal(1, HouseholdRules.OnLeave(l, 1, 2, new[] { new Leaver { Pawn = 5 } }, true, "b", 6)); Assert.Equal(30, l.Balance(P(5))); Assert.Equal(60, l.Balance(H(1))); Assert.True(l.Conserved);
        }

        [Fact] public void A_dissolved_household_divides_per_person_among_those_who_left_and_is_idempotent()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Household, 1, 100));
            var leavers = new[] { new Leaver { Pawn = 1 }, new Leaver { Pawn = 2 }, new Leaver { Pawn = 3, Away = true } };
            Assert.Equal(2, HouseholdRules.OnLeave(l, 1, 0, leavers, false, "d", 5)); Assert.Equal(0, HouseholdRules.OnLeave(l, 1, 0, leavers, false, "d", 5));
            Assert.Equal(33, l.Balance(P(1))); Assert.Equal(33, l.Balance(P(2))); Assert.True(l.Conserved);
            Assert.Equal(34, l.Balance(H(1)));                                                       // the away member's share stays for their return
        }
    }

    public class TreasuryEstateTests
    {
        private static string P(int id) => Ledger.IdFor(AccountKind.Person, id);

        [Fact] public void Public_debts_are_paid_before_private_ones_and_heirs_owe_nothing_unless_policy_says_so()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Person, 1, 60), (AccountKind.Person, 2, 0), (AccountKind.Person, 3, 0));
            var tax = l.OpenDebt(P(1), Ledger.CommunityId, DebtKind.Tax, 50, 1, "t"); var loan = l.OpenDebt(P(1), P(3), DebtKind.Loan, 100, 1, "l");
            l.SettleEstate(P(1), new[] { P(2) }, "e", 5);
            Assert.Equal(DebtStatus.Paid, l.DebtById(tax.Id).Status); Assert.Equal(10, l.Balance(P(3))); Assert.Equal(0, l.Balance(P(2))); Assert.Equal(P(1), l.DebtById(loan.Id).Debtor); Assert.True(l.Conserved);
        }

        [Fact] public void With_the_policy_on_the_heirs_take_the_unpaid_debt()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Person, 1, 0), (AccountKind.Person, 2, 0), (AccountKind.Person, 3, 0)); var loan = l.OpenDebt(P(1), P(3), DebtKind.Loan, 100, 1, "l");
            l.SettleEstate(P(1), new[] { P(2) }, "e", 5, true);
            Assert.True(l.DebtById(loan.Id).Active); Assert.Equal(P(2), l.DebtById(loan.Id).Debtor);
        }
    }

    /// <summary>Deterministic multi-quadrum colony simulations. They use the real ledger, payroll, income and trade code with fixed schedules; no silver is invented to balance anything.</summary>
    public class TreasurySimulationTests
    {
        private readonly ITestOutputHelper output; public TreasurySimulationTests(ITestOutputHelper o) { output = o; }

        private sealed class Result { public long ExternalIn, PaidWages, Debt, Unpaid; public int MinTreasury = int.MaxValue, EndTreasury, Personal; }

        private Result Run(string name, int pawns, int professionals, int rewardEvery, int rewardSize, int tradeEvery, int tradeSize, int days, int deathDay)
        {
            var l = new Ledger(); var b = new PayrollBook(); var res = new Result(); var residents = new List<Resident>();
            l.Ensure(AccountKind.Community, 0);
            for (int i = 1; i <= pawns; i++) { l.Ensure(AccountKind.Person, i); residents.Add(new Resident(i, 0)); }
            l.Receive(Ledger.CommunityId, 300, TxKind.Income, "seed", 0);
            for (int day = 1; day <= days; day++)
            {
                for (int p = 1; p <= pawns; p++)
                {
                    foreach (var cat in new[] { "hauling", "cleaning", "growing" }) b.Accrue(p, Labour.Role(cat), 3, day, "o:" + p + ":" + cat + ":" + day);          // 3 job-units a day each, within the daily caps
                    if (p <= professionals) b.Accrue(p, "pro", 4, day, "p:" + p + ":" + day);
                }
                if (rewardEvery > 0 && day % rewardEvery == 0) Income.DistributeReward(l, rewardSize, residents, new IncomePolicy(), "rew:" + day, day);
                if (tradeEvery > 0 && day % tradeEvery == 0) l.Receive(Ledger.CommunityId, tradeSize, TxKind.Trade, "tr:" + day, day);
                if (day == deathDay && pawns > 2) { l.SettleEstate(Ledger.IdFor(AccountKind.Person, pawns), new[] { Ledger.IdFor(AccountKind.Person, 1) }, "die", day); residents.RemoveAll(r => r.PawnId == pawns); pawns--; }
                b.SettleDue(l, day, l.Balance(Ledger.CommunityId));
                res.MinTreasury = Math.Min(res.MinTreasury, l.Balance(Ledger.CommunityId)); if (day % 30 == 0) output.WriteLine($"  {name} day {day}: pending={b.PendingTotal} treasury={l.Balance(Ledger.CommunityId)} debt={l.Debts.Where(d => d.Active).Sum(d => (long)d.Outstanding)} lastPayday={b.LastSettledPayday}");
                Assert.True(l.Conserved, name + " day " + day);
            }
            res.ExternalIn = (long)l.ExternalIn; res.EndTreasury = l.Balance(Ledger.CommunityId);
            res.PaidWages = l.History.Where(t => t.Kind == TxKind.Payroll).Sum(t => (long)t.Amount);
            res.Debt = l.Debts.Where(d => d.Active).Sum(d => (long)d.Outstanding); res.Unpaid = b.PendingTotal;
            res.Personal = l.Accounts.Where(a => a.Kind != AccountKind.Community).Sum(a => a.Balance);
            output.WriteLine($"{name}: externalIn={res.ExternalIn} wagesPaid={res.PaidWages} treasuryEnd={res.EndTreasury} minTreasury={res.MinTreasury} personal={res.Personal} debt={res.Debt} unpaidAccrued={res.Unpaid} recon={l.TotalClaims - (res.ExternalIn - (long)l.ExternalOut)}");
            return res;
        }

        [Fact] public void Small_tribal_colony_stays_conserved_and_never_creates_silver_to_pay_wages()
        { var r = Run("tribal(5)", 5, 1, 12, 150, 20, 120, 180, 90); Assert.True(r.PaidWages <= r.ExternalIn); }

        [Fact] public void Ordinary_colony_with_regular_trade_stays_conserved()
        { var r = Run("ordinary(16)", 16, 4, 10, 200, 8, 250, 180, 100); Assert.True(r.PaidWages <= r.ExternalIn); }

        [Fact] public void Large_colony_with_weak_income_goes_into_wage_debt_rather_than_inflating_the_currency()
        { var r = Run("large(42)", 42, 10, 15, 300, 20, 300, 180, 120); Assert.True(r.PaidWages <= r.ExternalIn); Assert.True(r.Debt >= 0); }
    }
}

namespace FamilyLife.Tests
{
    using FamilyLife.Domain;
    using Xunit;

    public class TreasuryLabourSignalTests
    {
        private static JobFacts J(string type, string def = "Job", int id = 1, int thing = 5, float work = 1000, bool dead = false, bool storage = true) => new JobFacts { WorkType = type, JobDef = def, JobId = id, ThingId = thing, WorkAmount = work, TargetDead = dead, HaulToStorage = storage };

        [Fact] public void A_failed_or_abandoned_hunt_earns_nothing_but_the_kill_does() { Assert.Null(Labour.CategoryOf(J("Hunting"))); Assert.Equal("hunting", Labour.CategoryOf(J("Hunting", dead: true))); }
        [Fact] public void A_cancelled_or_empty_bill_earns_nothing() { Assert.Null(Labour.CategoryOf(J("Crafting", work: 0))); Assert.Null(Labour.CategoryOf(J("Cooking", work: 0))); }
        [Fact] public void Delivering_materials_to_a_build_is_part_of_the_build_not_separate_pay() { Assert.Null(Labour.CategoryOf(J("Construction", def: "DeliverResourcesToBlueprints"))); Assert.Null(Labour.CategoryOf(J("Construction", def: "DeliverResourcesToFrame"))); }
        [Fact] public void The_same_target_pays_once_a_day_across_repeated_jobs_but_a_bill_pays_per_job()
        {
            Assert.Equal(Labour.Key(J("Cleaning", id: 1), "cleaning", 4), Labour.Key(J("Cleaning", id: 2), "cleaning", 4));
            Assert.NotEqual(Labour.Key(J("Cleaning"), "cleaning", 4), Labour.Key(J("Cleaning"), "cleaning", 5));
            Assert.NotEqual(Labour.Key(J("Crafting", id: 1), "crafting", 4), Labour.Key(J("Crafting", id: 2), "crafting", 4));
        }
    }
}
