using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// The start-up sequence and the combinations of packages, with the framework's real registries and stand-ins for the two modules that register exactly what the real ones do (ids,
    /// saved kinds, work, events, contributions). This proves the contracts: order, duplicates, versions, late arrivals, failures part way, and what each combination offers. It does not
    /// prove the real module classes register correctly in the game; the compiler and the architecture tests check their code, and loading the packages in RimWorld checks the rest.
    /// </summary>
    public class ModuleHostTests
    {
        private sealed class World
        {
            public readonly ModuleCatalog Catalog = new ModuleCatalog();
            public readonly ModuleHost Host;
            public readonly OfficeCatalog Offices;
            public readonly WorkRoster Roster;
            public readonly PlannedEventCatalog Events;
            public readonly ContributionList<string> Buttons;
            public readonly List<string> Ran = new List<string>();

            public World()
            {
                Host = new ModuleHost(Catalog);
                Offices = new OfficeCatalog(Catalog); Roster = new WorkRoster(Catalog); Events = new PlannedEventCatalog(Catalog); Buttons = new ContributionList<string>(Catalog);
                Catalog.Register(new ModuleInfo { Id = ModuleCatalog.FrameworkId, IsFramework = true, ApiVersion = ModuleCatalog.CurrentApiVersion });
                Host.Note(ModuleCatalog.FrameworkId);
                Host.Install("wils.familylife", () =>
                {
                    Catalog.Register(new ModuleInfo { Id = "wils.familylife", ApiVersion = ModuleCatalog.CurrentApiVersion, Requires = new[] { ModuleCatalog.FrameworkId } });
                    Offices.Register(new OfficeDefinition { Id = "wils.familylife.hallkeeper", ModuleId = "wils.familylife", Kind = OfficeKind.HallKeeper });
                    Offices.Register(new OfficeDefinition { Id = "wils.familylife.chaplain", ModuleId = "wils.familylife", Kind = OfficeKind.Chaplain });
                });
            }

            public Action Education(int api = ModuleCatalog.CurrentApiVersion, bool failHalfway = false, string[] needs = null) => () =>
            {
                Catalog.Register(new ModuleInfo { Id = "wils.education", ApiVersion = api, Requires = new[] { ModuleCatalog.FrameworkId }, RequiresCapabilities = needs ?? new string[0] });
                Catalog.Provide("wils.education", "wils.education.classes");
                Offices.Register(new OfficeDefinition { Id = "wils.education.teacher", ModuleId = "wils.education", Kind = OfficeKind.Teacher });
                Roster.Register(new WorkItem { Id = "wils.education.office.teacher", ModuleId = "wils.education", Run = now => Ran.Add("teacher") });
                Events.Register(new PlannedEventDefinition { Id = "wils.education.assessment", ModuleId = "wils.education", SavedKind = PendingEvent.Testing, Post = OfficeKind.Teacher, Begin = () => true, Pass = () => { } });
                Buttons.Add("wils.education.buttons", "wils.education", "lectern");
                if (failHalfway) throw new InvalidOperationException("boom");
            };

            public Action Care(int api = ModuleCatalog.CurrentApiVersion) => () =>
            {
                Catalog.Register(new ModuleInfo { Id = "wils.care", ApiVersion = api, Requires = new[] { ModuleCatalog.FrameworkId } });
                Catalog.Provide("wils.care", "wils.care.checkups");
                Offices.Register(new OfficeDefinition { Id = "wils.care.healer", ModuleId = "wils.care", Kind = OfficeKind.Healer });
                Roster.Register(new WorkItem { Id = "wils.care.office.healer", ModuleId = "wils.care", Run = now => Ran.Add("healer") });
                Events.Register(new PlannedEventDefinition { Id = "wils.care.checkups", ModuleId = "wils.care", SavedKind = PendingEvent.Checkups, Post = OfficeKind.Healer, Begin = () => true, Pass = () => { } });
                Buttons.Add("wils.care.buttons", "wils.care", "bed");
            };

            public void Freeze() { Host.Freeze(Offices.Validate()); }
            public List<string> RunWork() { Ran.Clear(); Roster.RunDue(1000, 0); return Ran.ToList(); }
        }

        // ---------------------------------------------------------------- the four combinations

        [Fact]
        public void Framework_only_offers_no_teacher_or_healer_runs_no_module_work_and_logs_no_problem()
        {
            var w = new World(); w.Freeze();
            Assert.Empty(w.Host.Problems);
            Assert.False(w.Offices.Available(OfficeKind.Teacher)); Assert.False(w.Offices.Available(OfficeKind.Healer));
            Assert.True(w.Offices.IsHistoric(OfficeKind.Teacher) && w.Offices.IsHistoric(OfficeKind.Healer));          // a saved post is kept as history, not an error
            Assert.True(w.Offices.Available(OfficeKind.HallKeeper));          // the framework package still provides its own posts
            Assert.Empty(w.RunWork());
            Assert.Empty(w.Buttons.Enabled());
            Assert.Null(w.Events.For(PendingEvent.Testing)); Assert.Null(w.Events.For(PendingEvent.Checkups));
            Assert.Equal(new[] { ModuleCatalog.FrameworkId, "wils.familylife" }, w.Host.InstallOrder);
        }

        [Fact]
        public void Framework_plus_Education_works_without_Care()
        {
            var w = new World(); Assert.True(w.Host.Install("wils.education", w.Education())); w.Freeze();
            Assert.Empty(w.Host.Problems);
            Assert.True(w.Offices.Available(OfficeKind.Teacher)); Assert.False(w.Offices.Available(OfficeKind.Healer));
            Assert.Equal(new[] { "teacher" }, w.RunWork());
            Assert.Equal(new[] { "lectern" }, w.Buttons.Enabled().ToArray());
            Assert.NotNull(w.Events.For(PendingEvent.Testing)); Assert.Null(w.Events.For(PendingEvent.Checkups));
            Assert.True(w.Catalog.Has("wils.education.classes")); Assert.False(w.Catalog.Has("wils.care.checkups"));
        }

        [Fact]
        public void Framework_plus_Care_works_without_Education()
        {
            var w = new World(); Assert.True(w.Host.Install("wils.care", w.Care())); w.Freeze();
            Assert.Empty(w.Host.Problems);
            Assert.True(w.Offices.Available(OfficeKind.Healer)); Assert.False(w.Offices.Available(OfficeKind.Teacher));
            Assert.Equal(new[] { "healer" }, w.RunWork());
            Assert.Equal(new[] { "bed" }, w.Buttons.Enabled().ToArray());
        }

        [Fact]
        public void Both_modules_load_in_either_order_and_each_registers_exactly_once()
        {
            foreach (var educationFirst in new[] { true, false })
            {
                var w = new World();
                if (educationFirst) { w.Host.Install("wils.education", w.Education()); w.Host.Install("wils.care", w.Care()); }
                else { w.Host.Install("wils.care", w.Care()); w.Host.Install("wils.education", w.Education()); }
                w.Freeze();
                Assert.Empty(w.Host.Problems);
                Assert.Equal(new[] { "healer", "teacher" }, w.RunWork().OrderBy(x => x).ToArray());          // one run each, whatever the order
                Assert.Equal(2, w.Roster.Count); Assert.Equal(2, w.Buttons.Count);
                Assert.Equal(educationFirst ? "wils.education" : "wils.care", w.Host.InstallOrder[2]);          // install order is the load order of the packages, recorded
            }
        }

        // ---------------------------------------------------------------- refusals

        [Fact]
        public void A_second_module_with_the_same_id_is_refused_and_the_first_is_left_untouched()
        {
            var w = new World();
            Assert.True(w.Host.Install("wils.education", w.Education()));
            Assert.False(w.Host.Install("wils.education", w.Education()));          // two packages must not provide the same module
            w.Freeze();
            Assert.Contains(w.Host.Problems, p => p.Contains("already installed"));
            Assert.True(w.Catalog.IsEnabled("wils.education") && !w.Catalog.IsBroken("wils.education"));
            Assert.Equal(new[] { "teacher" }, w.RunWork());          // one copy of the work, not two
        }

        [Fact]
        public void A_module_arriving_after_start_up_is_refused()
        {
            var w = new World(); w.Freeze();
            Assert.False(w.Host.Install("wils.care", w.Care()));
            Assert.Contains(w.Host.Problems, p => p.Contains("arrived after start-up"));
            Assert.False(w.Catalog.IsRegistered("wils.care"));
            Assert.Empty(w.RunWork());
        }

        [Fact]
        public void A_module_built_for_another_api_is_switched_off_not_half_loaded()
        {
            var w = new World(); w.Host.Install("wils.education", w.Education(api: ModuleCatalog.CurrentApiVersion + 1)); w.Host.Install("wils.care", w.Care()); w.Freeze();
            Assert.Contains(w.Host.Problems, p => p.Contains("wils.education") && p.Contains("API"));
            Assert.True(w.Catalog.IsBroken("wils.education"));
            Assert.False(w.Offices.Available(OfficeKind.Teacher)); Assert.Null(w.Events.For(PendingEvent.Testing));
            Assert.Equal(new[] { "healer" }, w.RunWork());          // the other module is unaffected
            Assert.Equal(new[] { "bed" }, w.Buttons.Enabled().ToArray());
        }

        [Fact]
        public void A_module_needing_a_capability_nothing_offers_is_switched_off()
        {
            var w = new World(); w.Host.Install("wils.education", w.Education(needs: new[] { "wils.care.checkups" })); w.Freeze();
            Assert.Contains(w.Host.Problems, p => p.Contains("needs the capability"));
            Assert.True(w.Catalog.IsBroken("wils.education"));
            Assert.Empty(w.RunWork());
        }

        [Fact]
        public void A_module_that_fails_part_way_has_everything_it_registered_ignored()
        {
            var w = new World();
            Assert.False(w.Host.Install("wils.education", w.Education(failHalfway: true)));
            w.Host.Install("wils.care", w.Care()); w.Freeze();
            Assert.Contains(w.Host.Problems, p => p.Contains("failed while registering"));
            Assert.True(w.Catalog.IsBroken("wils.education"));
            Assert.False(w.Offices.Available(OfficeKind.Teacher));          // its post was registered before the failure, and is still not offered
            Assert.Null(w.Events.For(PendingEvent.Testing));
            Assert.Equal(new[] { "healer" }, w.RunWork());          // its work item was registered and never runs
            Assert.DoesNotContain("lectern", w.Buttons.Enabled());
            Assert.DoesNotContain("wils.education", w.Host.InstallOrder);
        }

        [Fact]
        public void Duplicate_provider_ids_inside_a_module_fail_that_module_only()
        {
            var w = new World();
            Assert.False(w.Host.Install("wils.education", () =>
            {
                w.Education()();
                w.Buttons.Add("wils.education.buttons", "wils.education", "again");          // the same contribution id twice
            }));
            w.Host.Install("wils.care", w.Care()); w.Freeze();
            Assert.True(w.Catalog.IsBroken("wils.education")); Assert.False(w.Catalog.IsBroken("wils.care"));
        }

        [Fact]
        public void Two_modules_may_not_claim_the_same_saved_post_or_the_same_capability()
        {
            var w = new World();
            w.Host.Install("wils.education", w.Education());
            Assert.False(w.Host.Install("wils.care", () =>
            {
                w.Catalog.Register(new ModuleInfo { Id = "wils.care", ApiVersion = ModuleCatalog.CurrentApiVersion, Requires = new[] { ModuleCatalog.FrameworkId } });
                w.Offices.Register(new OfficeDefinition { Id = "wils.care.tutor", ModuleId = "wils.care", Kind = OfficeKind.Teacher });          // Education's saved kind
            }));
            Assert.Contains(w.Host.Problems, p => p.Contains("wils.education.teacher"));
            Assert.True(w.Catalog.IsBroken("wils.care"));
        }

        [Fact]
        public void An_optional_module_requiring_another_optional_module_is_refused()
        {
            var w = new World();
            w.Host.Install("wils.education", () => { w.Education()(); });
            w.Host.Install("wils.care", () =>
            {
                w.Catalog.Register(new ModuleInfo { Id = "wils.care", ApiVersion = ModuleCatalog.CurrentApiVersion, Requires = new[] { "wils.education" } });
            });
            w.Freeze();
            Assert.Contains(w.Host.Problems, p => p.Contains("only on the framework"));
            Assert.True(w.Catalog.IsBroken("wils.care"));
        }

        // ---------------------------------------------------------------- scheduling cost

        [Fact]
        public void A_module_that_is_stopped_schedules_nothing_and_costs_next_to_nothing()
        {
            var w = new World(); w.Host.Install("wils.education", w.Education()); w.Host.Install("wils.care", w.Care()); w.Freeze();
            w.Catalog.SetEnabled("wils.education", false);          // prepared / preparing: same switch
            Assert.Equal(new[] { "healer" }, w.RunWork());
            w.Catalog.SetEnabled("wils.care", false);
            Assert.Empty(w.RunWork());          // nothing at all runs, and nothing is looked up by name

            // A rough cost, reported rather than asserted (a build machine's clock says nothing about a game's frame): one maintenance pass over the roster with every module stopped.
            var sw = Stopwatch.StartNew(); const int Passes = 100000;
            for (int i = 0; i < Passes; i++) w.Roster.RunDue(2000 + i, 0);
            sw.Stop();
            Console.WriteLine("roster pass with all modules stopped: " + (sw.Elapsed.TotalMilliseconds * 1000.0 / Passes).ToString("0.000") + " microseconds per pass (" + Passes + " passes)");
        }
    }
}
