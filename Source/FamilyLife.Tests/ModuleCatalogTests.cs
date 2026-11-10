using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class ModuleCatalogTests
    {
        private static ModuleCatalog Standard()
        {
            var c = new ModuleCatalog();
            c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = ModuleCatalog.FrameworkId, Name = "Framework", IsFramework = true });
            c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = "wils.education", Name = "Education", Requires = new[] { ModuleCatalog.FrameworkId } });
            c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = "wils.care", Name = "Community Care", Requires = new[] { ModuleCatalog.FrameworkId } });
            c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = "wils.familylife", Name = "Family Life", Requires = new[] { ModuleCatalog.FrameworkId } });
            return c;
        }

        // ---------------------------------------------------------------- modules and capabilities

        [Fact]
        public void A_valid_set_of_modules_has_no_problems()
        {
            Assert.Empty(Standard().Validate());
        }

        [Theory]
        [InlineData("")] [InlineData(null)] [InlineData("education")] [InlineData("Wils.Education")] [InlineData("wils.")] [InlineData("wils.edu cation")] [InlineData("wils..x")]
        public void Identifiers_must_be_stable_namespaced_names(string id)
        {
            var ex = Assert.Throws<RegistrationException>(() => new ModuleCatalog().Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = id }));
            Assert.Contains("identifier", ex.Message);
        }

        [Fact]
        public void A_duplicate_module_or_capability_is_refused_with_a_message_that_names_it()
        {
            var c = Standard();
            var dup = Assert.Throws<RegistrationException>(() => c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = "wils.education" }));
            Assert.Contains("wils.education", dup.Message);
            c.Provide("wils.education", "wils.education.tutoring");
            var clash = Assert.Throws<RegistrationException>(() => c.Provide("wils.care", "wils.education.tutoring"));
            Assert.Contains("wils.education.tutoring", clash.Message);          // also refused because it is outside the offering module's namespace
            Assert.Throws<RegistrationException>(() => c.Provide("wils.nonexistent", "wils.nonexistent.x"));
        }

        [Fact]
        public void A_capability_exists_only_while_its_module_is_present_and_enabled()
        {
            var c = Standard();
            c.Provide("wils.education", "wils.education.tutoring");
            Assert.True(c.Has("wils.education.tutoring"));
            c.SetEnabled("wils.education", false);
            Assert.False(c.Has("wils.education.tutoring"));          // no tutoring suggestion without Education
            c.SetEnabled("wils.education", true);
            Assert.True(c.Has("wils.education.tutoring"));
            Assert.False(c.Has("wils.care.checkups"));          // a capability nobody offered
            Assert.False(new ModuleCatalog().Has("wils.education.tutoring"));          // framework only: nothing is offered at all
        }

        [Fact]
        public void The_framework_cannot_be_switched_off_and_unknown_modules_cannot_be_toggled()
        {
            var c = Standard();
            Assert.Throws<RegistrationException>(() => c.SetEnabled(ModuleCatalog.FrameworkId, false));
            Assert.Throws<RegistrationException>(() => c.SetEnabled("wils.missing", true));
        }

        [Fact]
        public void Optional_modules_may_require_only_the_framework_and_never_each_other()
        {
            var c = Standard();
            c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = "wils.bad", Requires = new[] { "wils.education" } });
            var problems = c.Validate();
            Assert.Contains(problems, p => p.Contains("wils.bad") && p.Contains("never on one another"));
        }

        [Fact]
        public void The_framework_may_not_depend_on_a_module_and_a_missing_requirement_is_reported()
        {
            var c = new ModuleCatalog();
            c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = ModuleCatalog.FrameworkId, IsFramework = true, Requires = new[] { "wils.education" } });
            c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = "wils.education", Requires = new[] { "wils.missing" } });
            var problems = c.Validate();
            Assert.Contains(problems, p => p.Contains("framework must not depend"));
            Assert.Contains(problems, p => p.Contains("wils.missing") && p.Contains("not present"));
            var none = new ModuleCatalog(); Assert.Contains(none.Validate(), p => p.Contains("exactly one framework"));
        }

        [Fact]
        public void A_cycle_is_found_and_reported()
        {
            var c = new ModuleCatalog();
            c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = ModuleCatalog.FrameworkId, IsFramework = true });
            c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = "wils.a", Requires = new[] { "wils.b" } });
            c.Register(new ModuleInfo { ApiVersion = ModuleCatalog.CurrentApiVersion, Id = "wils.b", Requires = new[] { "wils.a" } });
            Assert.Contains(c.Validate(), p => p.Contains("cycle"));
        }

        // ---------------------------------------------------------------- offices

        private static OfficeCatalog Offices(ModuleCatalog m)
        {
            var o = new OfficeCatalog(m);
            o.Register(new OfficeDefinition { Id = "wils.care.healer", ModuleId = "wils.care", Kind = OfficeKind.Healer, LabelKey = "WFL_OfficeKind_Healer" });
            o.Register(new OfficeDefinition { Id = "wils.education.teacher", ModuleId = "wils.education", Kind = OfficeKind.Teacher, LabelKey = "WFL_OfficeKind_Teacher" });
            return o;
        }

        [Fact]
        public void Offices_are_registered_once_by_id_and_by_saved_kind_and_stay_inside_their_module()
        {
            var m = Standard(); var o = Offices(m);
            Assert.Equal("wils.education.teacher", o.ByKind(OfficeKind.Teacher).Id);
            Assert.Equal(OfficeKind.Healer, o.ById("wils.care.healer").Kind);
            Assert.Throws<RegistrationException>(() => o.Register(new OfficeDefinition { Id = "wils.care.healer", ModuleId = "wils.care", Kind = OfficeKind.Chaplain }));          // same id
            var clash = Assert.Throws<RegistrationException>(() => o.Register(new OfficeDefinition { Id = "wils.care.nurse", ModuleId = "wils.care", Kind = OfficeKind.Healer }));          // same saved kind
            Assert.Contains("wils.care.healer", clash.Message);
            Assert.Throws<RegistrationException>(() => o.Register(new OfficeDefinition { Id = "wils.education.nurse", ModuleId = "wils.care", Kind = OfficeKind.Chaplain }));          // outside the namespace
            Assert.Null(o.ById("wils.care.unknown")); Assert.Null(o.ById(null));
        }

        [Fact]
        public void A_post_is_available_only_while_its_module_is_present_and_every_saved_kind_has_a_definition()
        {
            var m = Standard(); var o = Offices(m);
            Assert.True(o.Available(OfficeKind.Teacher));
            m.SetEnabled("wils.education", false);
            Assert.False(o.Available(OfficeKind.Teacher));          // Teacher is not offered without Education
            Assert.True(o.Available(OfficeKind.Healer));            // and Care does not depend on it
            Assert.False(o.Available(OfficeKind.HallKeeper));       // never registered
            Assert.True(o.IsHistoric(OfficeKind.HallKeeper) && o.IsHistoric(OfficeKind.Chaplain));          // a post whose module is not installed is a historic record, not an error
            Assert.Empty(o.Validate());
            o.Register(new OfficeDefinition { Id = "wils.familylife.hallkeeper", ModuleId = "wils.familylife", Kind = OfficeKind.HallKeeper });
            o.Register(new OfficeDefinition { Id = "wils.familylife.chaplain", ModuleId = "wils.familylife", Kind = OfficeKind.Chaplain });
            Assert.Empty(o.Validate());
        }

        // ---------------------------------------------------------------- shared work

        [Fact]
        public void Registered_work_runs_when_due_staggered_and_never_for_a_disabled_module()
        {
            var m = Standard(); var roster = new WorkRoster(m);
            var ran = new List<string>();
            roster.Register(new WorkItem { Id = "wils.education.work", ModuleId = "wils.education", Interval = 0, Run = now => ran.Add("edu") });
            roster.Register(new WorkItem { Id = "wils.care.work", ModuleId = "wils.care", Interval = 0, Run = now => ran.Add("care") });
            Assert.Equal(2, roster.RunDue(100, 0).Count);
            m.SetEnabled("wils.education", false);
            ran.Clear();
            roster.RunDue(200, 0);
            Assert.Equal(new[] { "care" }, ran.ToArray());          // a module that is off schedules nothing
            m.SetEnabled("wils.education", true);
            ran.Clear(); roster.RunDue(300, 0); Assert.Equal(2, ran.Count);
        }

        [Fact]
        public void Interval_work_waits_its_turn_is_staggered_by_identifier_and_survives_the_clock_going_back()
        {
            var m = Standard(); var roster = new WorkRoster(m); int calls = 0;
            roster.Register(new WorkItem { Id = "wils.care.slow", ModuleId = "wils.care", Interval = 600, Run = now => calls++ });
            int first = -1;
            for (int t = 0; t <= 1300; t += 10) { int before = calls; roster.RunDue(t, 0); if (calls > before && first < 0) first = t; }
            Assert.InRange(first, 0, 600);          // the first run lands somewhere inside one interval (the stagger), not all at once
            int total = calls; Assert.InRange(total, 2, 3);
            calls = 0; roster.RunDue(5, 0); roster.RunDue(5, 0);          // the clock went back: it does not wait for a time that has not come
            Assert.True(calls >= 1);
        }

        [Fact]
        public void A_budget_limits_each_run_and_rotates_so_no_item_is_starved()
        {
            var m = Standard(); var roster = new WorkRoster(m); var counts = new Dictionary<string, int>();
            foreach (var n in new[] { "a", "b", "c", "d" })
            {
                string id = "wils.care." + n; counts[id] = 0;
                roster.Register(new WorkItem { Id = id, ModuleId = "wils.care", Interval = 0, Run = now => counts[id]++ });
            }
            for (int i = 0; i < 40; i++) Assert.Equal(2, roster.RunDue(i, 2).Count);          // never more than the budget
            Assert.All(counts.Values, v => Assert.Equal(20, v));          // and each of the four got an equal share
        }

        [Fact]
        public void Cancelled_work_stops_and_one_failure_does_not_stop_the_rest()
        {
            var m = Standard(); var roster = new WorkRoster(m); int good = 0; var errors = new List<string>();
            roster.Register(new WorkItem { Id = "wils.care.bad", ModuleId = "wils.care", Run = now => throw new InvalidOperationException("boom") });
            roster.Register(new WorkItem { Id = "wils.care.good", ModuleId = "wils.care", Run = now => good++ });
            roster.RunDue(1, 0, (item, ex) => errors.Add(item.Id));
            Assert.Equal(1, good); Assert.Equal(new[] { "wils.care.bad" }, errors.ToArray());
            Assert.True(roster.Cancel("wils.care.good")); Assert.False(roster.Cancel("wils.care.good"));
            roster.RunDue(2, 0); Assert.Equal(1, good);
            Assert.Equal(1, roster.Count);
        }

        [Fact]
        public void Duplicate_or_misplaced_work_is_refused()
        {
            var m = Standard(); var roster = new WorkRoster(m);
            roster.Register(new WorkItem { Id = "wils.care.work", ModuleId = "wils.care", Run = n => { } });
            Assert.Throws<RegistrationException>(() => roster.Register(new WorkItem { Id = "wils.care.work", ModuleId = "wils.care", Run = n => { } }));
            Assert.Throws<RegistrationException>(() => roster.Register(new WorkItem { Id = "wils.care.x", ModuleId = "wils.education", Run = n => { } }));
            Assert.Throws<RegistrationException>(() => roster.Register(new WorkItem { Id = "wils.care.y", ModuleId = "wils.care" }));
        }

        // ---------------------------------------------------------------- planned events and contributions

        [Fact]
        public void A_planned_event_of_an_absent_module_is_known_but_never_run()
        {
            var m = Standard(); var events = new PlannedEventCatalog(m); bool began = false;
            events.Register(new PlannedEventDefinition { Id = "wils.education.assessment", ModuleId = "wils.education", SavedKind = PendingEvent.Testing, Post = OfficeKind.Teacher, Begin = () => { began = true; return true; }, Pass = () => { } });
            Assert.Throws<RegistrationException>(() => events.Register(new PlannedEventDefinition { Id = "wils.care.checkup", ModuleId = "wils.care", SavedKind = PendingEvent.Testing, Begin = () => true, Pass = () => { } }));
            Assert.Throws<RegistrationException>(() => events.Register(new PlannedEventDefinition { Id = "wils.care.nothing", ModuleId = "wils.care", SavedKind = 9 }));
            events.For(PendingEvent.Testing).Begin();
            Assert.True(began);
            m.SetEnabled("wils.education", false);
            Assert.Null(events.For(PendingEvent.Testing));          // a waiting event for an absent module is not run
            Assert.True(events.Known(PendingEvent.Testing));        // but it is recognised, so it can be cleared rather than misread
            Assert.Null(events.For(99)); Assert.False(events.Known(99));
        }

        [Fact]
        public void A_module_that_is_absent_contributes_nothing_to_the_interface()
        {
            var m = Standard(); var buttons = new ContributionList<string>(m);
            buttons.Add("wils.education.holdclass", "wils.education", "Hold class");
            buttons.Add("wils.care.checkups", "wils.care", "Begin check-ups");
            Assert.Equal(2, buttons.Enabled().Count());
            m.SetEnabled("wils.education", false);
            Assert.Equal(new[] { "Begin check-ups" }, buttons.Enabled().ToArray());
            Assert.Throws<RegistrationException>(() => buttons.Add("wils.care.checkups", "wils.care", "again"));
            Assert.Throws<RegistrationException>(() => buttons.Add("wils.care.other", "wils.education", "x"));
        }
    }
}
