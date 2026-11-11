using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class CareerPersistenceTests
    {
        private static FamilyRegistry Colony() { var r = new FamilyRegistry(); r.Apply(new[] { T.P(1, "A"), T.P(2, "B") }, 1000); return r; }

        [Fact] public void Careers_survive_a_save_and_load_through_the_shared_payload_store()
        {
            var r = Colony(); var c = r.Careers(); c.Award(1, "wils.education.teaching", "Melee", "k", "e1", 30, 3); c.Grandfather(2, "wils.care.practice");
            var back = SaveSim.Reload(r);
            Assert.Equal(30, back.Careers().SpecialtyXp(1, "wils.education.teaching", "Melee")); Assert.True(back.Careers().IsLegacy(2, "wils.care.practice"));
            Assert.Equal(0, back.Careers().Award(1, "wils.education.teaching", "Melee", "k", "e1", 30, 3));          // the dedup record came back too
            Assert.Contains(CareerLedger.Id, back.Shared.Ids);
        }

        [Fact] public void A_second_save_and_load_changes_nothing()
        {
            var r = Colony(); r.Careers().Award(1, "c", null, "k", "e", 10, 1);
            var once = SaveSim.Reload(r); var twice = SaveSim.Reload(once);
            Assert.Equal(once.Careers().Encode(), twice.Careers().Encode());
        }

        [Fact] public void A_save_without_the_payload_loads_with_an_empty_ledger()
        {
            var r = Colony(); var back = SaveSim.Reload(r);
            Assert.False(back.Shared.Has(CareerLedger.Id)); Assert.Equal(1, back.Careers().Level(1, "c")); Assert.True(back.Shared.Has(CareerLedger.Id));
        }

        [Fact] public void Payloads_of_modules_that_are_not_installed_are_carried_through_untouched()
        {
            var r = Colony(); r.Shared.Ids.Add("wils.future.thing"); r.Shared.Data.Add("x=1\ny=2"); r.Shared.Versions.Add(7);
            r.Careers().Award(1, "c", null, "k", "e", 5, 1);
            var back = SaveSim.Reload(SaveSim.Reload(r));
            int i = back.Shared.Ids.IndexOf("wils.future.thing");
            Assert.True(i >= 0); Assert.Equal("x=1\ny=2", back.Shared.Data[i]); Assert.Equal(7, back.Shared.Versions[i]);
        }

        [Fact] public void A_payload_with_uneven_lists_from_a_damaged_save_is_repaired_not_fatal()
        {
            var r = Colony(); r.Shared.Ids.Add(CareerLedger.Id);          // no data, no version
            Assert.Equal(1, r.Careers().Level(1, "c"));
        }

        [Fact] public void Reconcile_drops_only_people_the_registry_has_never_heard_of()
        {
            var r = Colony(); var c = r.Careers(); c.Award(1, "c", null, "k", "a", 5, 1); c.Award(99, "c", null, "k", "b", 5, 1);
            Assert.Equal(1, r.ReconcileCareers()); Assert.Equal(5, c.GeneralXp(1, "c")); Assert.Equal(0, c.GeneralXp(99, "c"));
        }

        [Fact] public void Every_career_payload_of_every_module_is_carried_through_untouched_when_that_module_is_absent()
        {
            var r = Colony();
            var ids = new[] { AssessmentLedger.Id, EducationRecord.Id, TeacherSchedule.Id, CareTerms.Id, "wils.future.payload" };
            foreach (var id in ids) { r.Shared.Ids.Add(id); r.Shared.Data.Add("k=v" + (char)10 + "n=" + id.Length); r.Shared.Versions.Add(9); }
            var back = SaveSim.Reload(SaveSim.Reload(r));          // nothing reads them: no module code is present in this scenario
            foreach (var id in ids) { int i = back.Shared.Ids.IndexOf(id); Assert.True(i >= 0, id); Assert.Equal("k=v" + (char)10 + "n=" + id.Length, back.Shared.Data[i]); Assert.Equal(9, back.Shared.Versions[i]); }
        }

        [Fact] public void A_career_report_for_the_same_event_twice_credits_once_even_across_a_save()
        {
            var r = Colony(); var l = r.Careers();
            Assert.True(l.Award(1, "c", "Surgeon", "care.surgery", "surgery|7|1|5000", 12, 3) > 0);
            Assert.Equal(0, SaveSim.Reload(r).Careers().Award(1, "c", "Surgeon", "care.surgery", "surgery|7|1|5000", 12, 3));
        }
    }
}
