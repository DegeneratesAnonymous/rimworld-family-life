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
