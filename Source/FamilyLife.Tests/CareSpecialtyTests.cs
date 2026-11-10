using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class CareSpecialtyTests
    {
        private static CareerDefinition Def()
        {
            var d = new CareerDefinition { Id = CareRoles.Career, Level2 = new CareerRule { GeneralXp = CareRoles.DoctorExperience }.Skill("Medicine", CareRoles.DoctorMedicine) };
            foreach (var s in CareSpecialties.All) d.Specialties[s.Key] = CareSpecialties.Rule(s);
            return d;
        }
        private static FamilyRegistry Colony() { var r = new FamilyRegistry(); r.Apply(new[] { T.P(1, "A"), T.P(2, "B") }, 100); return r; }

        [Fact] public void Four_specialties_have_activities_and_geneticist_is_deliberately_absent()
        {
            Assert.Equal(new[] { "Counselor", "Surgeon", "Midwife", "Veterinarian" }, CareSpecialties.All.Select(s => s.Key).ToArray());
            Assert.Null(CareSpecialties.ByKey("Geneticist"));
            Assert.Equal(CareSpecialties.All.Length, CareSpecialties.All.Select(s => s.RoleId).Distinct().Count());
        }

        [Fact] public void A_specialty_needs_doctor_first_then_skills_and_specialty_experience()
        {
            var r = Colony(); var l = r.Careers(); var d = Def();
            for (int i = 0; i < 30; i++) l.Award(1, d.Id, "Surgeon", "care.specialty.Surgeon", "e" + i, CareSpecialties.ActivityPoints, 10 + i / 5);
            Assert.True(l.SpecialtyXp(1, d.Id, "Surgeon") >= CareSpecialties.SpecialtyExperience);
            Assert.Empty(l.Evaluate(1, d, s => 20 - (s == "Medicine" ? 20 - 5 : 0)));                  // Medicine 5: not even a Doctor
            Assert.False(l.IsSpecialist(1, d.Id, "Surgeon"));
            var got = l.Evaluate(1, d, s => 12);
            Assert.Contains(d.Id, got); Assert.Contains(d.Id + ":Surgeon", got);
        }

        [Fact] public void Skills_for_each_specialty_are_checked_and_a_veterinarian_needs_animals_too()
        {
            var vet = CareSpecialties.ByKey("Veterinarian");
            Assert.False(CareSpecialties.MeetsSkills(vet, s => s == "Medicine" ? 10 : 0));
            Assert.True(CareSpecialties.MeetsSkills(vet, s => 6));
            Assert.False(CareSpecialties.MeetsSkills(CareSpecialties.ByKey("Counselor"), s => s == "Medicine" ? 20 : 3));
        }

        [Fact] public void A_specialist_role_can_be_assigned_only_once_qualified_and_the_specialist_still_counts_as_a_care_worker()
        {
            var r = Colony(); var l = r.Careers(); var cat = new ProfessionCatalog();
            var spec = CareSpecialties.ByKey("Counselor");
            cat.Register(new ProfessionRole { Id = spec.RoleId, ModuleId = "wils.care", Career = CareRoles.Career, Level = 3, Specialty = spec.Key });
            Assert.Equal(AssignResult.NotQualified, r.Assignments().Assign(1, cat.ById(spec.RoleId), l, 1));
            l.Grandfather(1, CareRoles.Career);
            Assert.Equal(AssignResult.NotQualified, r.Assignments().Assign(1, cat.ById(spec.RoleId), l, 1));          // Doctor alone is not enough
            for (int i = 0; i < 30; i++) l.Award(1, CareRoles.Career, spec.Key, "k", "e" + i, 10, 5 + i / 4);
            l.Award(1, CareRoles.Career, null, "k2", "g", 1, 99);
            var d = Def(); for (int i = 0; i < 30; i++) l.Award(1, CareRoles.Career, null, "gen", "g" + i, 10, 100 + i / 4);
            l.Evaluate(1, d, s => 12);
            Assert.Equal(AssignResult.Assigned, r.Assignments().Assign(1, cat.ById(spec.RoleId), l, 2));
            Assert.True(r.IsPractitioner(1));
        }

        [Fact] public void Specialty_experience_survives_save_and_load()
        {
            var r = Colony(); r.Careers().Award(1, CareRoles.Career, "Midwife", "k", "e", 10, 1);
            Assert.Equal(10, SaveSim.Reload(r).Careers().SpecialtyXp(1, CareRoles.Career, "Midwife"));
        }

        [Fact] public void Counselling_is_never_certain_and_a_better_listener_and_a_bond_of_trust_help()
        {
            float low = CounselingMath.HelpChance(2, -50f, false, false), mid = CounselingMath.HelpChance(8, 0f, false, false), high = CounselingMath.HelpChance(20, 100f, true, true);
            Assert.True(low >= .15f && high <= .9f); Assert.True(low < mid && mid < high);
            Assert.True(CounselingMath.HelpChance(8, 60f, false, false) > CounselingMath.HelpChance(8, 0f, false, false));
            Assert.True(CounselingMath.HelpChance(8, 0f, false, true) > CounselingMath.HelpChance(8, 0f, false, false));          // a qualified counselor does somewhat better
            Assert.Equal(0f, CounselingMath.ReconcileBoost(4)); Assert.Equal(.15f, CounselingMath.ReconcileBoost(40), 3);
        }
    }
}
