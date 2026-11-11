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

        [Fact] public void An_accepted_loss_falls_on_the_treasury_first_and_the_ledger_stays_conserved()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 100), (AccountKind.Person, 1, 100)); l.SetPhysical(100);
            Assert.Equal(100, l.Unbacked); int written = l.WriteDown("wd", 20);
            Assert.Equal(100, written); Assert.Equal(0, l.Unbacked); Assert.Equal(100, l.TotalClaims); Assert.True(l.Conserved);
            Assert.Equal(100, l.Balance(P(1))); Assert.Equal(0, l.Balance(Ledger.CommunityId));          // the person lost nothing: the institution that held the silver bore it
            Assert.Equal(0, l.WriteDown("wd", 20));
        }

        [Fact] public void Three_hundred_silver_backing_a_thousand_of_claims_keeps_ownership_separates_spending_and_says_who_bears_it()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 400), (AccountKind.Household, 1, 400), (AccountKind.Person, 2, 200)); l.SetPhysical(300);
            Assert.Equal(1000, l.TotalClaims); Assert.Equal(700, l.Unbacked);                                           // 700 is missing
            Assert.Equal(400, l.Balance(Ledger.IdFor(AccountKind.Household, 1))); Assert.Equal(200, l.Balance(P(2)));       // legal ownership is unchanged
            Assert.Equal(400, l.CommunityShare); Assert.Equal(300, l.SpilledToOwners);                                   // the treasury bears all it can; owners share the rest
            Assert.Equal(0, l.Available(Ledger.CommunityId));
            Assert.Equal(300, l.TotalAvailable);                                                                         // spendable never exceeds physical silver
            Assert.True(l.Available(Ledger.IdFor(AccountKind.Household, 1)) > l.Available(P(2)));
            Assert.Equal(LedgerResult.Insufficient, l.Transfer(Ledger.IdFor(AccountKind.Household, 1), P(2), 300, TxKind.Transfer, "x", 5));          // not spendable twice
            l.SetPhysical(1000); Assert.Equal(0, l.Unbacked); Assert.Equal(400, l.Available(Ledger.CommunityId)); Assert.Equal(1000, l.TotalAvailable);          // recovery restores spending
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
    /// <summary>
    /// Deterministic colony simulations over 180 days (three quadrums) using the real ledger, income split and payroll code with fixed activity levels. No silver is invented: every silver paid
    /// as a wage came from outside income that reached the treasury. They report, rather than hide, whether a configuration can sustain its payroll.
    /// </summary>
    public class TreasurySimulationTests
    {
        private readonly ITestOutputHelper output; public TreasurySimulationTests(ITestOutputHelper o) { output = o; }

        private sealed class Cfg
        {
            public string Name; public int Pawns, Professionals, Deaths; public double IncomePerPawnDay; public int CommunityPct = 50;
            public int OrdinaryRate = 1, OrdinaryUnitsPerDay = 1, OrdinaryDailyCap = 1, ProRate = 3, ProUnits = 2;
            public int Days = 180;
        }

        private sealed class Result
        {
            public long External, CommunityIn, Earned, Paid, Unpaid, Households, Treasury, Claims, Physical; public int TreasurerXp; public double RunwayDays; public bool Sustainable;
        }

        // A typical working pawn finishes a few jobs a day; a professional adds one unit of their own work.
        private static Result Run(Cfg c, bool print, ITestOutputHelper output)
        {
            var l = new Ledger(); var b = new PayrollBook(); var res = new Result(); var residents = new List<Resident>();
            b.Policy.OrdinaryDailyCap = c.OrdinaryDailyCap; b.Policy.Rates["pro"] = c.ProRate; b.Policy.DailyUnits["pro"] = c.ProUnits;
            foreach (var cat in new[] { "hauling", "cleaning", "growing" }) { b.Policy.Rates[Labour.Role(cat)] = c.OrdinaryRate; b.Policy.DailyUnits[Labour.Role(cat)] = c.OrdinaryUnitsPerDay; }
            l.Ensure(AccountKind.Community, 0); int pawns = c.Pawns;
            for (int i = 1; i <= pawns; i++) { l.Ensure(AccountKind.Person, i); residents.Add(new Resident(i, 0)); }
            var policy = new IncomePolicy { CommunityPercent = c.CommunityPct }; double carry = 0; long communityIn = 0, earned = 0, paidTotal = 0; int xp = 0;
            for (int day = 1; day <= c.Days; day++)
            {
                for (int p = 1; p <= pawns; p++)
                {
                    foreach (var cat in new[] { "hauling", "cleaning", "growing" }) earned += b.Accrue(p, Labour.Role(cat), 1, day, "o:" + p + ":" + cat + ":" + day);
                    if (p <= c.Professionals) earned += b.Accrue(p, "pro", 1, day, "p:" + p + ":" + day);
                }
                carry += c.IncomePerPawnDay * pawns;                                              // income arrives as whole quest rewards and trade profits
                if (day % 6 == 0 && carry >= 1)
                {
                    int amount = (int)carry; carry -= amount; int before = l.Balance(Ledger.CommunityId);
                    Income.DistributeReward(l, amount, residents, policy, "inc:" + day, day, day % 12 == 0 ? TxKind.Trade : TxKind.Reward);
                    communityIn += l.Balance(Ledger.CommunityId) - before; xp += TreasuryXp.Points(day % 12 == 0 ? FinancialOp.PooledTrade : FinancialOp.RewardDistribution, pawns) / 2;
                }
                if (c.Deaths > 0 && day == c.Days / 2) for (int d = 0; d < c.Deaths && pawns > 2; d++) { l.SettleEstate(Ledger.IdFor(AccountKind.Person, pawns), new[] { Ledger.IdFor(AccountKind.Person, 1) }, "die" + pawns, day); residents.RemoveAll(r => r.PawnId == pawns); pawns--; }
                var rs = b.SettleDue(l, day, l.Balance(Ledger.CommunityId)); int reports = rs.Count; paidTotal += rs.Sum(x => (long)x.TotalPaid); if (reports > 0) xp += TreasuryXp.Points(FinancialOp.Payday, pawns);
                Assert.True(l.Conserved, c.Name + " day " + day);
            }
            res.External = (long)l.ExternalIn; res.CommunityIn = communityIn; res.Earned = earned;
            res.Paid = paidTotal;          // from the payroll reports (the ledger history is a bounded window)
            res.Unpaid = l.Debts.Where(d => d.Active && d.Kind == DebtKind.Wage).Sum(d => (long)d.Outstanding) + b.PendingTotal;
            res.Households = l.Accounts.Where(a => a.Kind != AccountKind.Community).Sum(a => (long)a.Balance); res.Treasury = l.Balance(Ledger.CommunityId); res.Claims = l.TotalClaims; res.Physical = l.TotalClaims; res.TreasurerXp = xp;
            double accrualPerDay = earned / (double)c.Days, communityPerDay = communityIn / (double)c.Days, burn = accrualPerDay - communityPerDay;
            res.Sustainable = res.Unpaid <= accrualPerDay * 8;                                 // about one payday of work outstanding is normal; more means it is falling behind
            res.RunwayDays = burn <= 0.0001 ? double.PositiveInfinity : res.Treasury / burn;
            if (print) output.WriteLine($"{c.Name,-44} pct={c.CommunityPct,2} ord={c.OrdinaryRate}x{c.OrdinaryUnitsPerDay}/cap{c.OrdinaryDailyCap} pro={c.ProRate}x{c.ProUnits} | in={res.External,6} toTreasury={res.CommunityIn,5} earned={res.Earned,6} paid={res.Paid,6} unpaid={res.Unpaid,6} households={res.Households,6} treasury={res.Treasury,5} claims={res.Claims,6} backed={res.Physical,6} cover={(res.Earned == 0 ? 1.0 : res.Paid / (double)res.Earned):P0} runway={(double.IsInfinity(res.RunwayDays) ? "stable" : res.RunwayDays.ToString("F0") + "d")} treasurerXp={res.TreasurerXp}");
            Assert.Equal(res.Earned, res.Paid + res.Unpaid);                                    // every wage earned is either paid or still owed: nothing is forgiven or invented
            Assert.True(res.Paid <= res.External);
            return res;
        }

        private static readonly Cfg[] Colonies =
        {
            new Cfg { Name = "tribal 5/1pro", Pawns = 5, Professionals = 1 },
            new Cfg { Name = "ordinary 16/4pro", Pawns = 16, Professionals = 4, Deaths = 1 },
            new Cfg { Name = "large 42/10pro", Pawns = 42, Professionals = 10, Deaths = 2 },
        };
        private static readonly (string label, double income)[] Incomes = { ("low", 0.5), ("typical", 2.0), ("prosperous", 6.0) };

        private static Cfg With(Cfg c, string label, double income, int pct, int ordRate, int ordUnits, int ordCap, int proRate, int proUnits) =>
            new Cfg { Name = c.Name + " " + label, Pawns = c.Pawns, Professionals = c.Professionals, Deaths = c.Deaths, IncomePerPawnDay = income, CommunityPct = pct, OrdinaryRate = ordRate, OrdinaryUnitsPerDay = ordUnits, OrdinaryDailyCap = ordCap, ProRate = proRate, ProUnits = proUnits };

        [Fact] public void The_previous_defaults_are_measured_without_any_debt_cap_for_comparison()
        {
            output.WriteLine("-- PREVIOUS defaults: ordinary 1x3/day cap 8, professional 4x4, community keeps 25% --");
            foreach (var c in Colonies) foreach (var (label, income) in Incomes) Run(With(c, label, income, 25, 1, 3, 8, 4, 4), true, output);
        }

        [Fact] public void Wage_rate_and_treasury_retention_configurations_are_compared()
        {
            var configs = new[] { (pct: 40, o: 1, ou: 2, oc: 2, p: 3, pu: 2), (pct: 30, o: 1, ou: 2, oc: 2, p: 3, pu: 2), (pct: 50, o: 1, ou: 2, oc: 2, p: 3, pu: 2), (pct: 40, o: 1, ou: 1, oc: 1, p: 3, pu: 2), (pct: 40, o: 1, ou: 2, oc: 3, p: 4, pu: 3), (pct: 50, o: 1, ou: 1, oc: 1, p: 2, pu: 2) };
            foreach (var k in configs) { output.WriteLine($"-- retention {k.pct}%, ordinary {k.o}x{k.ou} cap {k.oc}, professional {k.p}x{k.pu} --"); foreach (var c in Colonies) foreach (var (label, income) in Incomes) Run(With(c, label, income, k.pct, k.o, k.ou, k.oc, k.p, k.pu), true, output); }
        }

        [Fact] public void A_colony_with_no_income_owes_everything_and_creates_no_silver()
        {
            var r = Run(With(Colonies[1], "no income", 0, 25, 1, 3, 8, 4, 4), true, output);
            Assert.Equal(0, r.Paid); Assert.Equal(r.Earned, r.Unpaid); Assert.False(r.Sustainable);
        }
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

namespace FamilyLife.Tests
{
    using System.Linq;
    using FamilyLife.Domain;
    using Xunit;

    public class TreasuryPayrollFundingTests
    {
        private static string P(int id) => Ledger.IdFor(AccountKind.Person, id);
        private static (Ledger, PayrollBook) Shortfall(PayrollFunding f, int treasury)
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, treasury), (AccountKind.Person, 1, 0), (AccountKind.Person, 2, 0), (AccountKind.Household, 9, 500)); var b = new PayrollBook(); b.Policy.Funding = f; b.Policy.PawnDailyCap = 100;
            b.Policy.Rates["doc"] = 10; b.Policy.DailyUnits["doc"] = 10; b.Policy.Rates["tutor"] = 10; b.Policy.DailyUnits["tutor"] = 10; b.Policy.Essential.Add("doc"); b.Policy.Order.Add("tutor"); b.Policy.Order.Add("doc");
            b.SettleDue(l, 0, 0); b.Accrue(1, "doc", 5, 3, "a"); b.Accrue(2, "tutor", 5, 3, "b");          // 50 owed to each
            return (l, b);
        }

        [Fact] public void Proportional_funding_pays_everyone_the_same_fraction_and_the_rest_stays_owed()
        {
            var (l, b) = Shortfall(PayrollFunding.Proportional, 50); b.SettleDue(l, 7, 50);
            Assert.Equal(25, l.Balance(P(1))); Assert.Equal(25, l.Balance(P(2))); Assert.Equal(50, l.Debts.Where(d => d.Active).Sum(d => d.Outstanding)); Assert.True(l.Conserved);
        }

        [Fact] public void Essential_first_pays_the_marked_roles_in_full_before_anyone_else()
        {
            var (l, b) = Shortfall(PayrollFunding.EssentialFirst, 50); b.SettleDue(l, 7, 50);
            Assert.Equal(50, l.Balance(P(1))); Assert.Equal(0, l.Balance(P(2))); Assert.Equal(50, l.Debts.Single(d => d.Active).Outstanding);
        }

        [Fact] public void Manual_priority_pays_in_the_players_order()
        {
            var (l, b) = Shortfall(PayrollFunding.Manual, 60); b.SettleDue(l, 7, 60);
            Assert.Equal(10, l.Balance(P(1))); Assert.Equal(50, l.Balance(P(2)));                  // tutor was listed first
        }

        [Fact] public void Household_savings_are_never_touched_to_cover_payroll()
        {
            var (l, b) = Shortfall(PayrollFunding.Proportional, 0); b.SettleDue(l, 7, 0);
            Assert.Equal(500, l.Balance(Ledger.IdFor(AccountKind.Household, 9))); Assert.Equal(0, l.Balance(P(1)));
        }

        [Fact] public void Unpaid_wages_are_never_capped_or_forgiven_however_long_nothing_is_paid()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 0), (AccountKind.Person, 1, 0)); var b = new PayrollBook(); b.Policy.PawnDailyCap = 100; b.Policy.Rates["doc"] = 10; b.Policy.DailyUnits["doc"] = 10; b.SettleDue(l, 0, 0);
            int earned = 0; for (int day = 1; day <= 120; day++) { earned += b.Accrue(1, "doc", 1, day, "d" + day); b.SettleDue(l, day, 0); }
            int owed = l.Debts.Where(d => d.Active).Sum(d => d.Outstanding) + b.PendingTotal;
            Assert.Equal(earned, owed); Assert.True(owed > 1000);                                        // far beyond the old 240 cap
            Assert.True(b.Missed(1) >= 14);
        }

        [Fact] public void Recovered_income_pays_the_arrears_oldest_workers_first_by_tier_and_clears_the_missed_count()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 0), (AccountKind.Person, 1, 0)); var b = new PayrollBook(); b.Policy.PawnDailyCap = 100; b.Policy.Rates["doc"] = 10; b.Policy.DailyUnits["doc"] = 10; b.SettleDue(l, 0, 0);
            b.Accrue(1, "doc", 1, 3, "a"); b.SettleDue(l, 7, 0); Assert.Equal(1, b.Missed(1));
            l.Receive(Ledger.CommunityId, 100, TxKind.Income, "inc", 8); b.SettleDue(l, 15, 100);
            Assert.Equal(10, l.Balance(P(1))); Assert.Equal(0, b.Missed(1)); Assert.Empty(l.Debts.Where(d => d.Active));
        }

        [Fact] public void The_funding_choice_survives_a_save_and_reload()
        {
            var b = new PayrollBook(); b.Policy.Funding = PayrollFunding.Manual; b.Policy.Order.AddRange(new[] { "x", "y" }); b.Policy.Essential.Add("e");
            var back = PayrollBook.Decode(new FamilyLife.Framework.StateReader(b.Encode()));
            Assert.Equal(PayrollFunding.Manual, back.Policy.Funding); Assert.Equal(new[] { "x", "y" }, back.Policy.Order.ToArray()); Assert.Contains("e", back.Policy.Essential);
        }
    }

    public class TreasuryOfficeNeverDecidesMoneyTests
    {
        private static string Src(string f) => System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.ModuleSource("Treasury"), f));

        [Fact] public void Half_value_applies_only_to_treasurer_experience_never_to_silver_or_claims()
        {
            Assert.Contains("AwayFromOffice(int points)", Src("Domain/TreasuryXp.cs"));
            string office = Src("Game/TreasuryOffice.cs");
            foreach (var money in new[] { "Ledger", ".Apply(", ".Receive(", ".Transfer(", "Balance(", "Income.", "Trade." }) Assert.DoesNotContain(money, office);          // the office code cannot touch a balance
        }

        [Fact] public void A_reward_or_trade_settles_in_full_before_any_office_item_exists()
        {
            string s = Src("Game/TreasuryService.cs");
            int silver = s.IndexOf("public static void OnSilver"), dist = s.IndexOf("Income.DistributeReward", silver), add = s.IndexOf("TreasuryOffice.Add", silver);
            Assert.True(dist > 0 && add > dist);
            int trade = s.IndexOf("public static void OnTrade"), settle = s.IndexOf("Trade.Settle(", trade), addT = s.IndexOf("TreasuryOffice.Add", trade);
            Assert.True(settle > 0 && addT > settle);
        }

        [Fact] public void Settlement_does_not_depend_on_a_treasurer_existing()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 0), (AccountKind.Person, 1, 0));
            Assert.Equal(LedgerResult.Ok, Income.DistributeReward(l, 100, new[] { new Resident(1, 0) }, new IncomePolicy(), "r", 1));
            Assert.Equal(100, l.Balance(Ledger.CommunityId) + l.Balance(Ledger.IdFor(AccountKind.Person, 1))); Assert.True(l.Conserved);
        }
    }
}
