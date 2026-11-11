using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// When registration closes. The game closes it from the first game component's FinalizeInit (after every mod constructor), once, however often that hook runs; these tests cover the
    /// host that does it, with no game types.
    /// </summary>
    public class ModuleFinalizationTests
    {
        private static (ModuleHost host, ModuleCatalog cat) Start()
        {
            var cat = new ModuleCatalog(); var host = new ModuleHost(cat);
            cat.Register(new ModuleInfo { Id = ModuleCatalog.FrameworkId, IsFramework = true, ApiVersion = ModuleCatalog.CurrentApiVersion }); host.Note(ModuleCatalog.FrameworkId);
            host.Install("wils.familylife", () => cat.Register(new ModuleInfo { Id = "wils.familylife", ApiVersion = ModuleCatalog.CurrentApiVersion, Requires = new[] { ModuleCatalog.FrameworkId } }));
            return (host, cat);
        }

        private static Action Module(ModuleCatalog cat, string id, int api = ModuleCatalog.CurrentApiVersion, string[] needs = null, bool fail = false) => () =>
        {
            cat.Register(new ModuleInfo { Id = id, ApiVersion = api, Requires = new[] { ModuleCatalog.FrameworkId }, RequiresCapabilities = needs ?? new string[0] });
            if (fail) throw new InvalidOperationException("boom");
        };

        [Fact]
        public void Registration_stays_open_until_finalization_so_modules_that_register_late_in_start_up_are_accepted()
        {
            var (host, cat) = Start();
            Assert.False(host.IsFinalized); Assert.False(host.IsFrozen);
            Assert.True(host.Install("wils.education", Module(cat, "wils.education")));          // e.g. from a mod constructor that ran after the framework's
            Assert.True(host.Install("wils.care", Module(cat, "wils.care")));
            Assert.True(host.Finalize());
            Assert.Equal("Modules: wils.framework, wils.familylife, wils.education, wils.care", host.ActiveLine());
        }

        [Fact]
        public void A_registration_after_finalization_is_refused_with_the_module_named_and_changes_nothing()
        {
            var (host, cat) = Start();
            host.Install("wils.education", Module(cat, "wils.education")); host.Finalize();
            string line = host.ActiveLine();
            Assert.False(host.Install("wils.care", Module(cat, "wils.care")));
            Assert.False(cat.IsRegistered("wils.care"));
            Assert.Contains(host.Problems, p => p.Contains("wils.care") && p.Contains("after start-up"));
            Assert.Contains("wils.care", host.Failures.Keys);
            Assert.Equal(line, host.ActiveLine());
        }

        [Fact]
        public void A_second_package_arriving_late_with_an_installed_modules_id_does_not_evict_the_installed_one()
        {
            var (host, cat) = Start();
            host.Install("wils.education", Module(cat, "wils.education")); host.Finalize();
            Assert.False(host.Install("wils.education", Module(cat, "wils.education")));
            Assert.DoesNotContain("wils.education", host.Failures.Keys);
            Assert.Contains("wils.education", host.ActiveModules);
        }

        [Fact]
        public void Finalizing_again_does_nothing_no_second_validation_no_repeated_problems_no_second_report()
        {
            var (host, cat) = Start();
            host.Install("wils.education", Module(cat, "wils.education", api: ModuleCatalog.CurrentApiVersion + 1));
            int logged = 0; host.OnProblem = _ => logged++;
            Assert.True(host.Finalize(new[] { "other problem" }));
            int problems = host.Problems.Count, afterFirst = logged;
            Assert.True(afterFirst > 0);
            Assert.False(host.Finalize(new[] { "other problem" })); Assert.False(host.Finalize()); Assert.False(host.Finalize());
            Assert.Equal(problems, host.Problems.Count); Assert.Equal(afterFirst, logged);
        }

        [Fact]
        public void A_module_that_failed_while_registering_is_not_listed_as_active_and_says_why()
        {
            var (host, cat) = Start();
            Assert.False(host.Install("wils.education", Module(cat, "wils.education", fail: true)));
            host.Install("wils.care", Module(cat, "wils.care"));
            host.Finalize();
            Assert.Equal("Modules: wils.framework, wils.familylife, wils.care", host.ActiveLine());
            Assert.Contains("boom", host.Failures["wils.education"]);
        }

        [Fact]
        public void Api_and_capability_failures_switch_the_module_off_report_the_reason_and_leave_it_out_of_the_line()
        {
            var (host, cat) = Start();
            host.Install("wils.education", Module(cat, "wils.education", api: ModuleCatalog.CurrentApiVersion + 1));
            host.Install("wils.care", Module(cat, "wils.care", needs: new[] { "wils.nothing.offers" }));
            host.Finalize();
            Assert.Equal("Modules: wils.framework, wils.familylife", host.ActiveLine());
            Assert.Contains("API", host.Failures["wils.education"]);
            Assert.Contains("wils.nothing.offers", host.Failures["wils.care"]);
            Assert.True(cat.IsBroken("wils.education") && cat.IsBroken("wils.care"));
        }

        [Fact]
        public void The_line_follows_the_order_the_packages_were_installed_and_is_the_same_every_time()
        {
            string Line(params string[] order)
            {
                var (host, cat) = Start();
                foreach (var id in order) host.Install(id, Module(cat, id));
                host.Finalize();
                return host.ActiveLine();
            }
            Assert.Equal("Modules: wils.framework, wils.familylife, wils.education, wils.care", Line("wils.education", "wils.care"));
            Assert.Equal("Modules: wils.framework, wils.familylife, wils.care, wils.education", Line("wils.care", "wils.education"));
            Assert.Equal(Line("wils.education", "wils.care"), Line("wils.education", "wils.care"));
            Assert.Equal("Modules: wils.framework, wils.familylife", Line());          // framework alone
        }

        [Fact]
        public void A_duplicate_module_id_is_refused_and_the_first_stays_active()
        {
            var (host, cat) = Start();
            Assert.True(host.Install("wils.care", Module(cat, "wils.care")));
            Assert.False(host.Install("wils.care", Module(cat, "wils.care")));
            host.Finalize();
            Assert.Equal(1, host.ActiveModules.Count(x => x == "wils.care"));
        }

        [Fact]
        public void The_game_closes_registration_only_from_the_first_game_components_FinalizeInit_never_from_a_start_up_class()
        {
            var all = RepoPaths.AllSource().ToList();
            string Text(string f) => System.IO.File.ReadAllText(f);
            var component = all.Single(f => f.Replace((char)92, '/').EndsWith("Game/FamilyComponent.cs"));
            string c = Text(component);
            int fin = c.IndexOf("FamilyFramework.FinalizeStartup()", StringComparison.Ordinal), apply = c.IndexOf("FamilyFramework.ApplyLifecycle(this)", StringComparison.Ordinal);
            Assert.True(fin > 0 && apply > fin, "FinalizeInit must close registration before it applies the lifecycle");
            foreach (var f in all.Where(x => !x.Replace((char)92, '/').Contains("/Tests/") && !x.Replace((char)92, '/').Contains("FamilyLife.Tests")))
            {
                string t = Text(f);
                Assert.DoesNotContain("FamilyFramework.Freeze(", t);
                if (t.Contains("[StaticConstructorOnStartup]")) Assert.DoesNotContain("FinalizeStartup", t);
            }
        }
    }
}
