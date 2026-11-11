using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Which modules a loaded save refers to that are not installed, and which of them the player is told about once. Reading only: nothing here changes what a module owns.
    /// </summary>
    public class AbsentModuleNoticeTests
    {
        private static FamilyRegistry Colony()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Doc"), T.P(2, "Teach"), T.P(3, "Kid", parents: new[] { 1 }) }, 1000);
            return r;
        }

        private static CommunityOffice Post(FamilyRegistry r, OfficeKind kind, int holder, params string[] payloads)
        {
            var o = new CommunityOffice { Id = (int)kind + 1, Kind = kind, HolderId = holder, HolderFamily = r.FamilyOf(holder), ElectedTick = 5000 };
            foreach (var p in payloads) { o.PayloadIds.Add(p); o.PayloadData.Add("x=1"); o.PayloadHolders.Add(holder); o.PayloadVersions.Add(1); }
            r.Offices.Add(o);
            return o;
        }

        private static Func<string, bool> Only(params string[] installed) => id => id == ModuleCatalog.FrameworkId || id == "wils.familylife" || installed.Contains(id);

        [Fact]
        public void A_new_game_has_nothing_to_report_and_a_framework_only_game_with_only_framework_posts_has_none_either()
        {
            Assert.Empty(AbsentModules.ToReport(new FamilyRegistry(), Only()));
            var r = Colony(); Post(r, OfficeKind.HallKeeper, 1); Post(r, OfficeKind.Chaplain, 2);
            Assert.Empty(AbsentModules.ToReport(r, Only()));
        }

        [Fact]
        public void An_existing_save_with_both_posts_but_only_the_framework_reports_both_modules_in_a_fixed_order()
        {
            var r = Colony(); Post(r, OfficeKind.Healer, 1, CareRecordData.Id, CareOfficeData.Id); Post(r, OfficeKind.Teacher, 2, "wils.education");
            var report = AbsentModules.ToReport(r, Only());
            Assert.Equal(new[] { "wils.care", "wils.education" }, report.Select(m => m.ModuleId).ToArray());
            Assert.Equal(new[] { OfficeKind.Healer }, report[0].Posts.ToArray()); Assert.Equal(2, report[0].Payloads.Count);
            Assert.Equal(new[] { OfficeKind.Teacher }, report[1].Posts.ToArray());
        }

        [Fact]
        public void One_absent_module_is_reported_alone()
        {
            var r = Colony(); Post(r, OfficeKind.Healer, 1, CareRecordData.Id); Post(r, OfficeKind.Teacher, 2, "wils.education");
            Assert.Equal(new[] { "wils.education" }, AbsentModules.ToReport(r, Only("wils.care")).Select(m => m.ModuleId).ToArray());
            Assert.Equal(new[] { "wils.care" }, AbsentModules.ToReport(r, Only("wils.education")).Select(m => m.ModuleId).ToArray());
            Assert.Empty(AbsentModules.ToReport(r, Only("wils.care", "wils.education")));
        }

        [Fact]
        public void A_post_that_an_old_all_in_one_save_holds_is_attributed_to_its_module_even_with_no_payload_of_its_own()
        {
            var r = Colony(); Post(r, OfficeKind.Teacher, 2);
            Assert.Equal(new[] { "wils.education" }, AbsentModules.ToReport(r, Only()).Select(m => m.ModuleId).ToArray());
        }

        [Fact]
        public void A_vacant_post_with_nothing_saved_is_not_worth_a_warning()
        {
            var r = Colony(); Post(r, OfficeKind.Teacher, 0);
            Assert.Empty(AbsentModules.ToReport(r, Only()));
        }

        [Fact]
        public void A_module_prepared_for_removal_is_never_described_as_missing_even_though_its_records_remain()
        {
            var r = Colony(); Post(r, OfficeKind.Teacher, 0, "wils.education");
            r.Lifecycle.RequestPreparation("wils.education", 10); r.Lifecycle.Finish("wils.education", new CleanupReport("wils.education"), 20);
            Assert.Equal(ModuleState.Prepared, r.Lifecycle.StateOf("wils.education"));
            Assert.Empty(AbsentModules.ToReport(r, Only()));
            var all = AbsentModules.Find(r, Only());          // still known to the status listing
            Assert.Single(all); Assert.True(all[0].Prepared);
        }

        [Fact]
        public void A_module_whose_preparation_failed_and_is_now_missing_is_reported_so_the_player_can_finish_it()
        {
            var r = Colony(); Post(r, OfficeKind.Healer, 1, CareRecordData.Id);
            r.Lifecycle.RequestPreparation("wils.care", 10);
            var bad = new CleanupReport("wils.care"); bad.Step("x", () => throw new InvalidOperationException("stuck"));
            r.Lifecycle.Finish("wils.care", bad, 20);
            var m = Assert.Single(AbsentModules.ToReport(r, Only()));
            Assert.Equal(ModuleState.PreparationFailed, m.State);
        }

        [Fact]
        public void Dismissing_one_module_survives_a_save_and_does_not_hide_a_module_that_goes_missing_later()
        {
            var r = Colony(); Post(r, OfficeKind.Teacher, 2, "wils.education"); Post(r, OfficeKind.Healer, 1, CareRecordData.Id);
            r.Lifecycle.SuppressNotice("wils.education");
            var back = SaveSim.Reload(r);          // the records come back; the lifecycle is saved separately, as the game does it (ExposeLifecycle)
            back.Lifecycle.NoticeFromList(r.Lifecycle.NoticeToList());
            Assert.True(back.Lifecycle.IsNoticeSuppressed("wils.education"));
            Assert.Equal(new[] { "wils.care" }, AbsentModules.ToReport(back, Only()).Select(m => m.ModuleId).ToArray());          // Care is newly missing: it still warns
            Assert.Empty(AbsentModules.ToReport(back, Only("wils.care")));          // Education stays quiet
        }

        [Fact]
        public void Reinstalling_a_module_clears_its_dismissal_without_touching_its_records_so_removing_it_again_warns_again()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Teacher, 2, "wils.education"); o.PayloadData[0] = "lessons=a,b";
            r.Lifecycle.SuppressNotice("wils.education");
            int cleared = AbsentModules.ClearSuppressionForInstalled(r, Only("wils.education"));
            Assert.Equal(1, cleared);
            Assert.False(r.Lifecycle.IsNoticeSuppressed("wils.education"));
            Assert.Equal("lessons=a,b", o.PayloadData[0]);          // the payload is exactly as it was
            Assert.Empty(AbsentModules.ToReport(r, Only("wils.education")));
            Assert.Single(AbsentModules.ToReport(r, Only()));          // removed again: warns again
        }

        [Fact]
        public void Looking_changes_nothing_in_the_save()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Healer, 1, CareRecordData.Id); Post(r, OfficeKind.Teacher, 2, "wils.education");
            string before = string.Join("|", o.PayloadIds) + string.Join("|", o.PayloadData) + r.Lifecycle.Records.Count + r.Lifecycle.NoticeSuppressed.Count + r.Offices.Count;
            AbsentModules.Find(r, Only()); AbsentModules.ToReport(r, Only());
            string after = string.Join("|", o.PayloadIds) + string.Join("|", o.PayloadData) + r.Lifecycle.Records.Count + r.Lifecycle.NoticeSuppressed.Count + r.Offices.Count;
            Assert.Equal(before, after);
        }

        [Fact]
        public void Payload_ownership_is_read_from_the_module_namespace_and_framework_data_belongs_to_nobody()
        {
            Assert.Equal("wils.care", AbsentModules.OwnerOfPayload("wils.care.record"));
            Assert.Equal("wils.care", AbsentModules.OwnerOfPayload("wils.care"));
            Assert.Equal("wils.education", AbsentModules.OwnerOfPayload("wils.education.assessment"));
            Assert.Null(AbsentModules.OwnerOfPayload(LegacyOfficeMigration.PayloadId));
            Assert.Null(AbsentModules.OwnerOfPayload("not-an-id")); Assert.Null(AbsentModules.OwnerOfPayload(null));
        }
    }
}
