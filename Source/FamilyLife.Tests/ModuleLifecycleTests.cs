using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// The per-save life of a module: active, preparing, failed, prepared, and absent. A prepared module stays stopped across saves, restarts and reinstalls until someone explicitly
    /// reactivates it; a failed preparation can be run again; a module that is merely not installed has not been prepared and keeps its saved state. The game-side cleanup (ending
    /// jobs, removing memories) cannot be run here: the tests cover the rules around it and the domain parts of it.
    /// </summary>
    public class ModuleLifecycleTests
    {
        private static CleanupReport Ok(string id) { var r = new CleanupReport(id); r.Step("end things", () => "done"); return r; }
        private static CleanupReport Bad(string id) { var r = new CleanupReport(id); r.Step("end things", () => "done"); r.Step("release pupils", () => throw new InvalidOperationException("two pupils are still in class")); return r; }

        /// <summary>What a save does: the lifecycle is written to lists and read back (ExposeLifecycle).</summary>
        private static ModuleLifecycle Persist(ModuleLifecycle l)
        {
            l.ToLists(out var ids, out var states, out var ticks, out var attempts, out var reports);
            var back = new ModuleLifecycle(); back.FromLists(ids?.ToList(), states?.ToList(), ticks?.ToList(), attempts?.ToList(), reports?.ToList());
            return back;
        }

        [Fact]
        public void A_module_with_no_record_is_active_and_a_new_game_records_nothing()
        {
            var l = new ModuleLifecycle();
            Assert.Equal(ModuleState.Active, l.StateOf("wils.education")); Assert.True(l.MayRun("wils.education"));
            Assert.Empty(Persist(l).Records);
        }

        [Fact]
        public void Preparation_requested_stops_new_work_at_once_and_a_clean_report_makes_the_module_prepared()
        {
            var l = new ModuleLifecycle();
            Assert.True(l.RequestPreparation("wils.education", 100));
            Assert.Equal(ModuleState.PreparationRequested, l.StateOf("wils.education")); Assert.False(l.MayRun("wils.education"));
            Assert.Equal(ModuleState.Prepared, l.Finish("wils.education", Ok("wils.education"), 200));
            Assert.False(l.MayRun("wils.education"));
        }

        [Fact]
        public void A_report_with_any_failed_step_is_never_success_and_keeps_the_module_stopped_with_the_reason()
        {
            var l = new ModuleLifecycle(); var report = Bad("wils.education");
            Assert.False(report.Prepared);
            l.RequestPreparation("wils.education", 100);
            Assert.Equal(ModuleState.PreparationFailed, l.Finish("wils.education", report, 200));
            Assert.False(l.MayRun("wils.education"));          // not ready for removal, and not running either
            Assert.Contains("still in class", l.Find("wils.education").Report);          // the reason survives for the next try
            Assert.Contains("NOT prepared", l.Find("wils.education").Report);
        }

        [Fact]
        public void A_failed_preparation_can_be_retried_to_success_and_counts_its_attempts()
        {
            var l = new ModuleLifecycle();
            l.RequestPreparation("wils.education", 100); l.Finish("wils.education", Bad("wils.education"), 110);
            l.RequestPreparation("wils.education", 200); Assert.Equal(ModuleState.PreparationRequested, l.StateOf("wils.education"));
            Assert.Equal(ModuleState.Prepared, l.Finish("wils.education", Ok("wils.education"), 210));
            Assert.Equal(2, l.Find("wils.education").Attempts);
        }

        [Fact]
        public void Preparing_twice_is_safe_and_a_prepared_module_stays_prepared_until_a_later_run_actually_fails()
        {
            var l = new ModuleLifecycle();
            l.RequestPreparation("wils.education", 1); l.Finish("wils.education", Ok("wils.education"), 2);
            Assert.False(l.RequestPreparation("wils.education", 3));          // already prepared: nothing to request
            Assert.Equal(ModuleState.Prepared, l.StateOf("wils.education"));
            Assert.Equal(ModuleState.Prepared, l.Finish("wils.education", Ok("wils.education"), 4));          // a repeat run changes nothing
            Assert.Equal(ModuleState.PreparationFailed, l.Finish("wils.education", Bad("wils.education"), 5));          // and a repeat that fails is reported as not ready
        }

        [Fact]
        public void A_prepared_module_stays_prepared_across_a_save_and_a_restart_until_explicitly_reactivated()
        {
            var l = new ModuleLifecycle(); l.RequestPreparation("wils.education", 1); l.Finish("wils.education", Ok("wils.education"), 2);
            var afterSave = Persist(l); var afterAnotherSave = Persist(afterSave);
            Assert.Equal(ModuleState.Prepared, afterAnotherSave.StateOf("wils.education"));
            Assert.False(afterAnotherSave.MayRun("wils.education"));          // loading the game does not start it again
            Assert.False(afterAnotherSave.Reactivate("wils.care", 9));          // reactivating something else does nothing
            Assert.True(afterAnotherSave.Reactivate("wils.education", 10));
            Assert.True(afterAnotherSave.MayRun("wils.education"));
            Assert.Empty(Persist(afterAnotherSave).Records);          // an active module needs no record
            Assert.False(afterAnotherSave.Reactivate("wils.education", 11));          // and reactivating twice is a no-op
        }

        [Fact]
        public void A_preparation_cut_short_by_the_save_is_a_failed_attempt_not_a_success()
        {
            var l = new ModuleLifecycle(); l.RequestPreparation("wils.education", 1);          // saved while it was still running
            var back = Persist(l);
            Assert.Equal(ModuleState.PreparationFailed, back.StateOf("wils.education")); Assert.False(back.MayRun("wils.education"));
        }

        [Fact]
        public void A_state_this_version_does_not_know_is_treated_as_stopped_never_as_running()
        {
            var l = new ModuleLifecycle(); l.FromLists(new List<string> { "wils.education" }, new List<int> { 99 }, new List<int> { 0 }, new List<int> { 0 }, new List<string> { "" });
            Assert.False(l.MayRun("wils.education"));
        }

        [Fact]
        public void A_module_that_is_not_installed_keeps_its_saved_state_untouched_through_saves()
        {
            // the lifecycle knows nothing about which modules are installed: absence is simply a record nobody reads, and it is written back unchanged
            var l = new ModuleLifecycle(); l.RequestPreparation("wils.education", 1); l.Finish("wils.education", Bad("wils.education"), 2);
            var again = Persist(Persist(Persist(l)));
            Assert.Equal(ModuleState.PreparationFailed, again.StateOf("wils.education"));
            Assert.Equal(l.Find("wils.education").Report, again.Find("wils.education").Report);
            Assert.Equal(1, again.Find("wils.education").Attempts);
        }

        [Fact]
        public void Damaged_lists_do_not_throw_or_invent_records()
        {
            var l = new ModuleLifecycle();
            l.FromLists(new List<string> { "wils.a", "", "wils.a", null }, new List<int> { 3 }, null, null, null);          // short lists, an empty id, a duplicate, a null
            Assert.Single(l.Records); Assert.Equal(ModuleState.Prepared, l.StateOf("wils.a"));
            l.FromLists(null, null, null, null, null); Assert.Empty(l.Records);
        }

        [Fact]
        public void Absence_and_preparation_are_different_things_the_module_must_be_present_to_clean_up_after_itself()
        {
            // A prepared module has a record and was cleaned by its own code. An absent one has no cleanup record at all: nothing says it is ready to remove.
            var prepared = new ModuleLifecycle(); prepared.RequestPreparation("wils.education", 1); prepared.Finish("wils.education", Ok("wils.education"), 2);
            var neverPrepared = new ModuleLifecycle();
            Assert.Equal(ModuleState.Prepared, prepared.StateOf("wils.education"));
            Assert.Null(neverPrepared.Find("wils.education"));          // removing a module without preparing it leaves no claim that it was clean
        }

        // ---------------------------------------------------------------- the registry carries the lifecycle with the records

        [Fact]
        public void The_registry_holds_a_lifecycle_of_its_own_so_each_save_has_its_own()
        {
            var a = new FamilyRegistry(); var b = new FamilyRegistry();
            a.Lifecycle.RequestPreparation("wils.education", 1);
            Assert.False(a.Lifecycle.MayRun("wils.education")); Assert.True(b.Lifecycle.MayRun("wils.education"));
        }
    }
}
