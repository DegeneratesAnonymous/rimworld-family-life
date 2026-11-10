using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>Teacher and Healer are professions the player assigns. The assembly and the vote never touch them; the political posts still work.</summary>
    public class ProfessionsNotElectedTests
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private static FamilyRegistry Colony(bool professions = true)
        {
            var r = new FamilyRegistry { ProfessionsAssigned = professions };
            r.Apply(new[] { T.P(1, "A"), T.P(2, "B"), T.P(3, "C"), T.P(4, "D") }, 1000);
            return r;
        }
        private static readonly OfficeContext Everyone = new OfficeContext { Skill = (id, k) => .9f };

        [Fact] public void A_vacant_teacher_or_healer_post_is_not_filled_by_a_vote_but_the_hall_keeper_still_is()
        {
            var r = Colony(); r.ElectOffices(2000, Everyone);
            Assert.False(r.OfficeFor(OfficeKind.Healer)?.Held ?? false); Assert.False(r.OfficeFor(OfficeKind.Teacher)?.Held ?? false);
            Assert.True(r.OfficeFor(OfficeKind.HallKeeper)?.Held ?? false);
        }

        [Fact] public void The_assembly_holds_no_race_for_a_profession_and_still_runs_the_hall_keepers()
        {
            var r = Colony(); r.ElectOffices(2000, Everyone);
            r.MaintainOffices(3000);                                                    // sets the first assembly date
            long guard = 0; int now = 3000 + 61 * Day;
            var assembly = r.CallAssembly(now, Everyone);
            Assert.NotNull(assembly);
            Assert.DoesNotContain(assembly.Races, x => x.Kind == OfficeKind.Teacher || x.Kind == OfficeKind.Healer);
            Assert.Contains(assembly.Races, x => x.Kind == OfficeKind.HallKeeper);
            Assert.Equal(0, guard);
        }

        [Fact] public void An_assigned_professional_in_the_working_seat_is_not_reviewed_or_unseated_by_the_assembly_or_for_neglect()
        {
            var r = Colony(); r.SeatService(OfficeKind.Teacher, 2, 1500);
            r.ElectOffices(2000, Everyone); r.MaintainOffices(3000);
            r.NoteOfficeNeed(OfficeKind.Teacher, true, 3000);
            r.MaintainOffices(3000 + 40 * Day);                                         // long past the neglect limit, with no work recorded
            Assert.Equal(2, r.OfficeFor(OfficeKind.Teacher).HolderId);
            r.CallAssembly(3000 + 61 * Day, Everyone);
            Assert.Equal(2, r.OfficeFor(OfficeKind.Teacher).HolderId); Assert.Equal(0, r.OfficeFor(OfficeKind.Teacher).TermEndTick);
        }

        [Fact] public void Qualifications_and_assignments_do_not_depend_on_the_assembly_at_all()
        {
            var r = Colony(); var l = r.Careers(); l.Grandfather(2, "c");
            var role = new ProfessionRole { Id = "wils.x.a", ModuleId = "wils.x", Career = "c", Level = 2 };
            Assert.Equal(AssignResult.Assigned, r.Assignments().Assign(2, role, l, 5));
            r.ElectOffices(2000, Everyone); r.CallAssembly(3000 + 61 * Day, Everyone);
            Assert.True(r.Assignments().IsAssigned(2, "wils.x.a")); Assert.True(l.IsQualified(2, "c"));
            Assert.Empty(r.Assemblies);                                       // nothing needed an assembly
        }

        [Fact] public void Without_the_setting_the_generic_vote_still_works_for_those_kinds()
        {
            var r = Colony(false); r.ElectOffices(2000, Everyone);
            Assert.True(r.OfficeFor(OfficeKind.Healer)?.Held ?? false);
        }

        [Fact] public void An_existing_save_keeps_its_old_holder_in_the_seat_until_someone_is_assigned()
        {
            var r = Colony(false); r.ElectOffices(2000, Everyone);
            int healer = r.OfficeFor(OfficeKind.Healer).HolderId;
            var back = SaveSim.Reload(r); back.ProfessionsAssigned = true;
            back.MaintainOffices(5000); back.ElectOffices(5000, Everyone);
            Assert.Equal(healer, back.OfficeFor(OfficeKind.Healer).HolderId);
        }
    }
}
