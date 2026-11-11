using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class CareCareerTests
    {
        private static ProfessionCatalog Catalog()
        {
            var c = new ProfessionCatalog();
            c.Register(new ProfessionRole { Id = CareRoles.Healer, ModuleId = "wils.care", Career = CareRoles.Career, Level = 1 });
            c.Register(new ProfessionRole { Id = CareRoles.Doctor, ModuleId = "wils.care", Career = CareRoles.Career, Level = 2 });
            return c;
        }
        private static FamilyRegistry Colony() { var r = new FamilyRegistry(); r.Apply(new[] { T.P(1, "A"), T.P(2, "B"), T.P(3, "C"), T.P(4, "D") }, 1000); return r; }
        private static void Post(FamilyRegistry r, int holder) { var o = new CommunityOffice { Id = 2, Kind = OfficeKind.Healer, HolderId = holder, HolderFamily = r.FamilyOf(holder) }; r.Offices.Add(o); }
        private static LegacyPost[] Posts() => new[] { new LegacyPost { Kind = OfficeKind.Healer, RoleId = CareRoles.Healer } };

        [Fact] public void A_legacy_healer_keeps_the_healer_assignment_gains_doctor_qualification_but_is_not_made_a_doctor()
        {
            var r = Colony(); Post(r, 1);
            LegacyProfessionMigration.Run(r, 10, Posts(), Catalog());
            Assert.True(r.Assignments().IsAssigned(1, CareRoles.Healer)); Assert.False(r.Assignments().IsAssigned(1, CareRoles.Doctor));
            Assert.True(r.Careers().IsQualified(1, CareRoles.Career)); Assert.True(r.Careers().IsLegacy(1, CareRoles.Career)); Assert.Equal(0, r.Careers().GeneralXp(1, CareRoles.Career));
            Assert.Equal(1, r.OfficeFor(OfficeKind.Healer).HolderId);
            Assert.False(r.CareTerms().NurseTitles); Assert.Equal("wils.care.healer_label", CareTermsAccess.EntryLevelTitleKey(r));
        }

        [Fact] public void The_first_doctor_the_player_assigns_turns_healers_into_nurses_for_good()
        {
            var r = Colony(); var c = Catalog(); Post(r, 1); LegacyProfessionMigration.Run(r, 10, Posts(), c);
            Assert.False(r.CareTerms().Observe(r.Assignments().Holders(CareRoles.Doctor).Count(), 11));
            Assert.Equal(AssignResult.Assigned, r.Assignments().Assign(1, c.ById(CareRoles.Doctor), r.Careers(), 12));
            Assert.True(r.CareTerms().Observe(r.Assignments().Holders(CareRoles.Doctor).Count(), 13));
            Assert.Equal("wils.care.nurse_label", CareTermsAccess.EntryLevelTitleKey(r));
            r.Assignments().Unassign(1, CareRoles.Doctor);
            Assert.False(r.CareTerms().Observe(0, 20));
            var back = SaveSim.Reload(r);
            Assert.Equal("wils.care.nurse_label", CareTermsAccess.EntryLevelTitleKey(back));
            Assert.Equal(13, back.CareTerms().FirstDoctorTick);
        }

        [Fact] public void Only_a_qualified_person_can_be_made_a_doctor_and_anyone_can_be_a_healer()
        {
            var r = Colony(); var c = Catalog();
            Assert.Equal(AssignResult.Assigned, r.Assignments().Assign(2, c.ById(CareRoles.Healer), r.Careers(), 1));
            Assert.Equal(AssignResult.NotQualified, r.Assignments().Assign(2, c.ById(CareRoles.Doctor), r.Careers(), 1));
        }

        [Fact] public void Doctor_qualification_needs_medicine_and_experience()
        {
            var r = Colony(); var l = r.Careers();
            var def = new CareerDefinition { Id = CareRoles.Career, Level2 = new CareerRule { GeneralXp = CareRoles.DoctorExperience }.Skill("Medicine", CareRoles.DoctorMedicine) };
            for (int i = 0; i < 13; i++) l.Award(2, def.Id, null, "care.checkup", "v" + i, CareRoles.CheckupPoints, 5 + i / 6);
            Assert.Empty(l.Evaluate(2, def, s => 3));
            Assert.Single(l.Evaluate(2, def, s => 6));
        }

        [Fact] public void A_patient_is_claimed_by_one_practitioner_and_a_practitioner_sees_one_patient_at_a_time()
        {
            var c = new CareClaims();
            Assert.True(c.TryClaim(10, 1, 100)); Assert.False(c.TryClaim(10, 2, 100)); Assert.False(c.TryClaim(11, 1, 100));
            Assert.True(c.TryClaim(11, 2, 100));
            c.Release(10); Assert.True(c.TryClaim(10, 3, 100));
        }

        [Fact] public void Claims_expire_and_cannot_be_made_on_oneself()
        {
            var c = new CareClaims(); c.TryClaim(10, 1, 100, 50);
            Assert.False(c.IsClaimed(10, 150)); Assert.True(c.TryClaim(10, 2, 150));
            Assert.False(c.TryClaim(5, 5, 0));
        }

        [Fact] public void A_bed_hosts_one_practitioner_and_release_all_frees_a_practitioners_claims()
        {
            var c = new CareClaims();
            Assert.True(c.TryClaimPlace("bed1", 1, 0)); Assert.False(c.TryClaimPlace("bed1", 2, 0)); Assert.True(c.TryClaimPlace("bed1", 1, 0));
            c.TryClaim(10, 1, 0); c.ReleaseAll(1);
            Assert.True(c.TryClaimPlace("bed1", 2, 0)); Assert.True(c.TryClaim(10, 2, 0));
        }

        [Fact] public void A_refusal_of_one_care_worker_does_not_bind_another_but_a_refusal_of_check_ups_binds_all()
        {
            var r = Colony(); Post(r, 1);
            r.NoteRefusal(3, CareRefusalKind.HealerSpecific, CareRefusalReason.ReluctantWithHealer, 100, 1);
            r.NoteRefusal(4, CareRefusalKind.ServiceWide, CareRefusalReason.LowMood, 100, 1);
            Assert.True(r.HasRefused(3, 200, 1)); Assert.False(r.HasRefused(3, 200, 2));
            Assert.True(r.HasRefused(4, 200, 1)); Assert.True(r.HasRefused(4, 200, 2));
        }

        [Fact] public void When_a_care_worker_stops_serving_only_refusals_about_them_lapse()
        {
            var r = Colony(); Post(r, 1);
            r.NoteRefusal(3, CareRefusalKind.HealerSpecific, "x", 100, 1); r.NoteRefusal(3, CareRefusalKind.HealerSpecific, "x", 100, 2); r.NoteRefusal(4, CareRefusalKind.ServiceWide, "x", 100, 1);
            Assert.Equal(1, r.LapseHealerRefusals(id => id != 1));
            Assert.False(r.HasRefused(3, 200, 1)); Assert.True(r.HasRefused(3, 200, 2)); Assert.True(r.HasRefused(4, 200, 2));
        }

        [Fact] public void Existing_single_holder_refusal_calls_behave_as_before()
        {
            var r = Colony(); Post(r, 1);
            r.NoteRefusal(3, CareRefusalKind.HealerSpecific, "x", 100);
            Assert.True(r.HasRefused(3, 200)); Assert.Equal(1, r.LapseHealerRefusals(2)); Assert.False(r.HasRefused(3, 200));
        }
    }
}
