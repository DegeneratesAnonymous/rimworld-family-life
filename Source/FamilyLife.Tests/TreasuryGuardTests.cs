using System;
using System.Diagnostics;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class TreasuryGuardTests
    {
        private static string Treasury(string file) => System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.ModuleSource("Treasury"), file));

        [Fact] public void Payroll_and_taxation_never_bring_silver_in_so_they_cannot_create_it()
        {
            foreach (var f in new[] { "Domain/Payroll.cs", "Domain/Taxation.cs" }) { string t = Treasury(f); Assert.DoesNotContain(".Receive(", t); Assert.DoesNotContain("new Move(null,", t); }
        }

        [Fact] public void Silver_enters_the_ledger_only_from_the_three_places_that_really_see_it_arrive()
        {
            string service = Treasury("Game/TreasuryService.cs");
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(service, @"\.Receive\(").Count);          // reconciliation of unclaimed silver in storage
            Assert.Contains("Income.DistributeReward", service); Assert.Contains("Trade.Settle", service);          // quest rewards and completed trades
        }

        [Fact] public void The_economy_hooks_live_in_the_framework_and_target_vanilla_by_name()
        {
            string h = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.Framework, "Game/EconomyHooks.cs"));
            Assert.Contains("typeof(TradeDeal), nameof(TradeDeal.TryExecute)", h); Assert.Contains("bool actuallyTraded", h);
            Assert.Contains("typeof(QuestPart_DropPods), nameof(QuestPart_DropPods.Notify_QuestSignalReceived)", h); Assert.Contains("Signal signal", h);
            Assert.DoesNotContain("HarmonyPatch", Treasury("Game/TreasuryModule.cs"));
        }

        [Fact] public void The_treasury_package_is_its_own_package_and_needs_only_the_framework()
        {
            string about = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.Root(), "Packages", "WilsTreasury", "About", "About.xml"));
            Assert.Contains("<packageId>SpacemanWil.WilsTreasury</packageId>", about); Assert.Contains("SpacemanWil.WilsFamilyLife", about);
            Assert.DoesNotContain("WilsEducation", about); Assert.DoesNotContain("WilsCommunityCare", about);
            string module = Treasury("Game/TreasuryModule.cs"); Assert.Contains("Requires = new[] { ModuleCatalog.FrameworkId }", module);
        }

        [Fact] public void Taxation_is_registered_through_the_career_so_it_unlocks_by_assignment_not_by_a_flag()
        {
            string module = Treasury("Game/TreasuryModule.cs");
            Assert.Contains("TreasuryRoles.TaxCollector, ModuleId = Id, Career = TreasuryRoles.Career, Level = 2", module);          // earned, level 2
            Assert.Contains("TreasuryRoles.Treasurer, ModuleId = Id, Career = TreasuryRoles.Career, Level = 1", module);          // anyone, level 1, no skill gate
            Assert.Contains("TaxAuthority.MayAssess", Treasury("Game/TreasuryService.cs"));
        }

        [Fact] public void A_long_colony_stays_balanced_bounded_and_fast()
        {
            var l = new Ledger(); var book = new PayrollBook(); var sw = Stopwatch.StartNew(); var rng = new Random(11);
            for (int p = 1; p <= 60; p++) l.Ensure(AccountKind.Person, p);
            l.Receive(Ledger.CommunityId, 5000, TxKind.Income, "seed", 0); book.SettleDue(l, 0, 1 << 30);
            for (int day = 1; day <= 360; day++)                                    // six game years
            {
                for (int w = 0; w < 25; w++) book.Accrue(rng.Next(1, 61), "r" + rng.Next(4), 1, day, "k" + day + "-" + w);
                if (day % 9 == 0) Income.DistributeReward(l, 400 + rng.Next(300), Enumerable.Range(1, 60).Select(i => new Resident(i, i % 12 + 1)).ToList(), new IncomePolicy(), "q" + day, day);
                book.SettleDue(l, day, 1 << 30); l.Prune(day);
                Assert.True(l.Conserved);
            }
            Assert.True(l.History.Count <= Ledger.MaxHistory); Assert.True(l.Encode().Length < 200_000);
            Assert.True(sw.ElapsedMilliseconds < 5000);
        }
    }
}

namespace FamilyLife.Tests
{
    public class TreasuryLabourHookGuardTests
    {
        private static string Hooks() => System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.Framework, "Game/EconomyHooks.cs"));

        [Fact] public void Wages_are_only_ever_considered_for_jobs_that_ended_successfully()
        {
            string h = Hooks();
            Assert.Contains("typeof(Verse.AI.Pawn_JobTracker), nameof(Verse.AI.Pawn_JobTracker.EndCurrentJob)", h);
            Assert.Contains("condition != Verse.AI.JobCondition.Succeeded", h);          // started, interrupted and cancelled jobs return before anything is reported
            Assert.Contains("typeof(ResearchManager), nameof(ResearchManager.ResearchPerformed)", h);
        }

        [Fact] public void Ordinary_labour_is_gated_on_the_treasury_being_able_to_pay()
        {
            string s = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.ModuleSource("Treasury"), "Game/TreasuryService.cs"));
            Assert.Contains("CanFundOrdinary(r)", s); Assert.Contains("Labour.CategoryOf(facts)", s);
        }
    }
}

namespace FamilyLife.Tests
{
    public class TreasuryOfficeAndXpTests
    {
        private static string Src(string f) => System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.ModuleSource("Treasury"), f));

        [Fact] public void The_treasurers_physical_work_never_touches_a_balance()
        {
            string office = Src("Game/TreasuryOffice.cs");
            foreach (var forbidden in new[] { ".Receive(", ".Spend(", ".Transfer(", ".Apply(", "Ledger()", "TransferShare", "RecordLoss" }) Assert.DoesNotContain(forbidden, office);          // hauling and sitting move nothing in the ledger
            Assert.Contains("JobDefOf.HaulToCell", office); Assert.Contains("HaulMode.ToCellStorage", office);          // the carrying is the game's own haul job
        }

        [Fact] public void Experience_is_granted_per_operation_key_so_repeated_sitting_or_reconciliation_earns_nothing()
        {
            string office = Src("Game/TreasuryOffice.cs");
            Assert.Contains("\"treasury.office\", item.Key", office); Assert.Contains("\"deposit|\" + thingId", office); Assert.Contains("items.Any(i => i.Key == key)", office);
        }

        [Fact] public void Away_from_the_office_counts_for_half()
        {
            Assert.Equal(4, TreasuryXp.AwayFromOffice(8)); Assert.Equal(1, TreasuryXp.AwayFromOffice(1));
        }

        private static double MonthsToCollector(int residents, int rewards, int trades, int deposits)
        {
            int perYear = 8 * TreasuryXp.Points(FinancialOp.Payday, residents) + rewards * TreasuryXp.Points(FinancialOp.RewardDistribution, residents) + trades * TreasuryXp.Points(FinancialOp.PooledTrade, residents) + deposits * TreasuryXp.Points(FinancialOp.Deposit, residents);
            return TreasuryRoles.CollectorExperience / (double)perYear * 12.0;
        }

        [Fact] public void A_treasurer_can_qualify_in_roughly_a_year_in_a_small_colony_and_faster_in_larger_ones_from_real_operations_alone()
        {
            double small = MonthsToCollector(6, 5, 10, 10), ordinary = MonthsToCollector(15, 8, 20, 20), large = MonthsToCollector(40, 12, 40, 30);
            Assert.InRange(small, 6.0, 12.0); Assert.InRange(ordinary, 3.0, 8.0); Assert.InRange(large, 1.5, 4.0); Assert.True(small > ordinary && ordinary > large);
        }

        [Fact] public void Even_a_colony_with_almost_no_trade_qualifies_within_two_years_without_any_invented_work()
        {
            Assert.True(MonthsToCollector(6, 2, 2, 2) < 24.0);          // paydays and a couple of rewards only
        }
    }
}

namespace FamilyLife.Tests
{
    public class TreasuryTaxVisitGuardTests
    {
        private static string Visit() => System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.ModuleSource("Treasury"), "Game/TreasuryTaxVisit.cs"));

        [Fact] public void A_collection_visit_creates_and_destroys_no_silver()
        {
            string v = Visit(); Assert.DoesNotContain(".Receive(", v); Assert.DoesNotContain(".Spend(", v); Assert.DoesNotContain("RecordLoss", v);
        }

        [Fact] public void Every_waiver_or_diversion_is_remembered_so_it_can_be_found_and_recovered()
        {
            string v = Visit();
            foreach (var kind in new[] { "\"embezzle\"", "\"favour\"", "\"bribe\"", "\"own taxes\"", "\"overcharge\"", "\"conceal\"" }) Assert.Contains(kind, v);
            Assert.Contains("tax.DiscoverFirst(l, today)", v);
        }

        [Fact] public void The_outcome_weighs_relationships_family_mood_skills_and_traits_from_the_real_pawns()
        {
            string v = Visit();
            foreach (var needle in new[] { "OpinionOf(collector)", "HouseholdOpinion", "CurLevelPercentage", "SkillDefOf.Social", "SkillDefOf.Melee", "TraitDefOf.Greedy", "TraitDefOf.Abrasive", "PriorRefusals" }) Assert.Contains(needle, v);
            Assert.Contains("TaxAuthority.MayCollect", v);
        }
    }
}
