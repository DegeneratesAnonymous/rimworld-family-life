using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class AssignmentTests
    {
        private const string Teaching = "wils.education.teaching", Care = "wils.care.practice";
        private static ProfessionCatalog Catalog()
        {
            var c = new ProfessionCatalog();
            c.Register(new ProfessionRole { Id = "wils.education.tutor", ModuleId = "wils.education", Career = Teaching, Level = 1 });
            c.Register(new ProfessionRole { Id = "wils.education.teacher", ModuleId = "wils.education", Career = Teaching, Level = 2 });
            c.Register(new ProfessionRole { Id = "wils.education.specialist.melee", ModuleId = "wils.education", Career = Teaching, Level = 3, Specialty = "Melee" });
            c.Register(new ProfessionRole { Id = "wils.care.doctor", ModuleId = "wils.care", Career = Care, Level = 2 });
            return c;
        }
        private static FamilyRegistry Colony() { var r = new FamilyRegistry(); r.Apply(new[] { T.P(1, "A"), T.P(2, "B"), T.P(3, "C") }, 1000); return r; }
        private static CommunityOffice Post(FamilyRegistry r, OfficeKind k, int holder) { var o = r.OfficeFor(k); if (o == null) { o = new CommunityOffice { Id = (int)k + 1, Kind = k }; r.Offices.Add(o); } o.HolderId = holder; o.HolderFamily = r.FamilyOf(holder); return o; }

        [Fact] public void Level_one_can_be_assigned_by_anyone_with_no_skill_or_experience()
        {
            var r = Colony(); var c = Catalog();
            Assert.Equal(AssignResult.Assigned, r.Assignments().Assign(1, c.ById("wils.education.tutor"), r.Careers(), 10));
            Assert.Equal(AssignResult.AlreadyAssigned, r.Assignments().Assign(1, c.ById("wils.education.tutor"), r.Careers(), 11));
        }

        [Fact] public void A_higher_level_needs_the_qualification_and_a_specialty_needs_the_specialty()
        {
            var r = Colony(); var c = Catalog(); var l = r.Careers(); var b = r.Assignments();
            Assert.Equal(AssignResult.NotQualified, b.Assign(1, c.ById("wils.education.teacher"), l, 1));
            l.Grandfather(1, Teaching);
            Assert.Equal(AssignResult.Assigned, b.Assign(1, c.ById("wils.education.teacher"), l, 1));
            Assert.Equal(AssignResult.NotQualified, b.Assign(1, c.ById("wils.education.specialist.melee"), l, 1));
            Assert.Equal(AssignResult.UnknownRole, b.Assign(1, null, l, 1));
        }

        [Fact] public void Qualifying_never_assigns_anyone()
        {
            var r = Colony(); var l = r.Careers(); l.Grandfather(1, Teaching);
            Assert.Empty(r.Assignments().RolesOf(1)); Assert.Equal(0, r.Assignments().Count);
        }

        [Fact] public void Many_people_hold_one_role_and_one_person_holds_many_roles_with_no_cap()
        {
            var r = Colony(); var c = Catalog(); var b = r.Assignments(); var l = r.Careers();
            for (int i = 1; i <= 3; i++) b.Assign(i, c.ById("wils.education.tutor"), l, 1);
            Assert.Equal(new[] { 1, 2, 3 }, b.Holders("wils.education.tutor").ToArray());
            l.Grandfather(1, Teaching); l.Grandfather(1, Care);
            b.Assign(1, c.ById("wils.education.teacher"), l, 1); b.Assign(1, c.ById("wils.care.doctor"), l, 1);
            Assert.Equal(3, b.RolesOf(1).Count());
            var big = new AssignmentBook(); var bl = new CareerLedger(); for (int i = 1; i <= 500; i++) big.Assign(i, c.ById("wils.education.tutor"), bl, 1);
            Assert.Equal(500, big.Count);
        }

        [Fact] public void Changing_an_assignment_keeps_every_qualification_and_all_experience()
        {
            var r = Colony(); var c = Catalog(); var b = r.Assignments(); var l = r.Careers();
            l.Grandfather(1, Teaching); l.Award(1, Teaching, null, "k", "e", 25, 1);
            b.Assign(1, c.ById("wils.education.teacher"), l, 1); b.Unassign(1, "wils.education.teacher"); b.Assign(1, c.ById("wils.education.tutor"), l, 2);
            Assert.True(l.IsQualified(1, Teaching)); Assert.Equal(25, l.GeneralXp(1, Teaching));
            Assert.Equal(new[] { "wils.education.tutor" }, b.RolesOf(1).ToArray());
        }

        [Fact] public void Assignments_survive_save_and_load_and_a_vacancy_loses_only_the_assignment()
        {
            var r = Colony(); var c = Catalog(); r.Assignments().Assign(2, c.ById("wils.education.tutor"), r.Careers(), 5); r.Careers().Grandfather(2, Teaching);
            var back = SaveSim.Reload(r);
            Assert.True(back.Assignments().IsAssigned(2, "wils.education.tutor"));
            Assert.Equal(1, back.Assignments().Vacate(2)); Assert.True(back.Careers().IsQualified(2, Teaching));
        }

        [Fact] public void Roles_are_validated_when_registered()
        {
            var c = new ProfessionCatalog();
            Assert.Throws<RegistrationException>(() => c.Register(new ProfessionRole { Id = "wils.x.a", ModuleId = "wils.y", Career = "c" }));
            Assert.Throws<RegistrationException>(() => c.Register(new ProfessionRole { Id = "wils.x.a", ModuleId = "wils.x", Career = "c", Level = 3 }));
            c.Register(new ProfessionRole { Id = "wils.x.a", ModuleId = "wils.x", Career = "c" });
            Assert.Throws<RegistrationException>(() => c.Register(new ProfessionRole { Id = "wils.x.a", ModuleId = "wils.x", Career = "c" }));
        }

        private static LegacyPost[] Posts() => new[] { new LegacyPost { Kind = OfficeKind.Teacher, RoleId = "wils.education.teacher" }, new LegacyPost { Kind = OfficeKind.Healer, RoleId = "wils.care.doctor" } };

        [Fact] public void Legacy_holders_become_assigned_and_recognised_with_no_invented_experience_and_the_offices_are_untouched()
        {
            var r = Colony(); var c = Catalog(); Post(r, OfficeKind.Teacher, 1); Post(r, OfficeKind.Healer, 2);
            var done = LegacyProfessionMigration.Run(r, 50, Posts(), c);
            Assert.Equal(2, done.Count);
            Assert.True(r.Assignments().IsAssigned(1, "wils.education.teacher")); Assert.True(r.Assignments().IsAssigned(2, "wils.care.doctor"));
            Assert.True(r.Careers().IsLegacy(1, Teaching)); Assert.Equal(0, r.Careers().GeneralXp(1, Teaching));
            Assert.Equal(1, r.OfficeFor(OfficeKind.Teacher).HolderId); Assert.Equal(2, r.OfficeFor(OfficeKind.Healer).HolderId);
        }

        [Fact] public void The_migration_is_idempotent_across_saves_and_does_not_undo_a_later_player_choice()
        {
            var r = Colony(); var c = Catalog(); Post(r, OfficeKind.Teacher, 1);
            LegacyProfessionMigration.Run(r, 50, Posts(), c);
            r.Assignments().Unassign(1, "wils.education.teacher");
            var back = SaveSim.Reload(r);
            Assert.Empty(LegacyProfessionMigration.Run(back, 99, Posts(), c));
            Assert.False(back.Assignments().IsAssigned(1, "wils.education.teacher"));
            Assert.Equal(0, back.Assignments().Count);
        }

        [Fact] public void A_new_holder_of_the_old_post_is_migrated_on_their_own()
        {
            var r = Colony(); var c = Catalog(); Post(r, OfficeKind.Teacher, 1); LegacyProfessionMigration.Run(r, 1, Posts(), c);
            r.OfficeFor(OfficeKind.Teacher).HolderId = 3;
            Assert.Single(LegacyProfessionMigration.Run(r, 2, Posts(), c));
            Assert.True(r.Assignments().IsAssigned(3, "wils.education.teacher"));
        }

        [Fact] public void A_vacant_old_post_and_an_unregistered_role_migrate_nothing()
        {
            var r = Colony(); Post(r, OfficeKind.Teacher, 0); Post(r, OfficeKind.Healer, 2);
            Assert.Empty(LegacyProfessionMigration.Run(r, 1, Posts(), new ProfessionCatalog()));
            Assert.DoesNotContain(LegacyProfessionMigration.Run(r, 1, Posts(), Catalog()), k => k.Contains("Teacher"));
        }

        [Fact] public void Reconcile_drops_assignments_of_people_the_registry_never_knew()
        {
            var r = Colony(); var c = Catalog(); r.Assignments().Assign(1, c.ById("wils.education.tutor"), r.Careers(), 1); r.Assignments().Assign(77, c.ById("wils.education.tutor"), r.Careers(), 1);
            Assert.Equal(1, r.ReconcileAssignments()); Assert.Equal(new[] { 1 }, r.Assignments().Holders("wils.education.tutor").ToArray());
        }

        [Fact] public void Assignment_data_in_a_damaged_save_is_cleaned_not_fatal()
        {
            var b = AssignmentBook.Decode(new StateReader("p=1,1,0,5\nr=a,a,b,"));
            Assert.Equal(1, b.Count);
        }
    }
}
