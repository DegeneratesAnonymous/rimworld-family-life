using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Source-level guards for the concrete runtime risks found in the pre-validation review. They cannot show behaviour in the game (nothing here does); they stop each fix from being undone.
    /// </summary>
    public class RuntimeRiskGuardTests
    {
        private static string Src(string suffix) => RepoPaths.AllSource().Where(f => f.Replace((char)92, '/').EndsWith(suffix)).Select(System.IO.File.ReadAllText).Single();

        [Fact] public void Professions_are_assigned_from_the_first_tick_of_a_new_game_not_only_after_a_load()
        {
            string c = Src("Game/FamilyComponent.cs");
            Assert.Matches(@"FamilyRegistry\s+Registry\s*=\s*new FamilyRegistry\s*\{\s*ProfessionsAssigned\s*=\s*true", c);
            Assert.Contains("Registry.ProfessionsAssigned = true; CareerService.OnLoaded(this);", c);
        }

        [Fact] public void A_check_up_releases_its_patient_claim_before_any_early_return()
        {
            string d = Src("Care/Game/CareDriver.cs");
            int begin = d.IndexOf("internal static void CompleteCheckup", System.StringComparison.Ordinal);
            int release = d.IndexOf("Claims.Release(patient.thingIDNumber)", begin, System.StringComparison.Ordinal), record = d.IndexOf("r.RecordCheckup(", begin, System.StringComparison.Ordinal);
            Assert.True(begin > 0 && release > begin && release < record, "the claim must be released before RecordCheckup can return early");
        }

        [Fact] public void Only_the_colonys_own_people_build_a_career()
        {
            string s = Src("Game/CareerService.cs");
            int report = s.IndexOf("public static int Report(", System.StringComparison.Ordinal);
            Assert.True(report > 0 && s.IndexOf("InColony != true", report, System.StringComparison.Ordinal) > report);
        }

        [Fact] public void Long_medical_side_jobs_end_for_real_medical_work()
        {
            string w = Src("Care/Game/CareSpecialistWork.cs");
            Assert.Equal(2, Regex.Matches(w, @"FailOn\(\(\) => PawnReadinessApi\.StopReason\(pawn, FrameworkServices\.Now\) != null \|\| CareDemand\.DoctorShouldTend").Count);          // the vet examination and the birth attendance
        }

        [Fact] public void A_class_whose_teacher_never_reached_the_class_job_is_swept_up()
        {
            Assert.Contains("ClassSessions.EndStale(c, now);", Src("Education/Game/EducationDriver.cs"));
        }

        [Fact] public void No_def_uses_an_empty_requiredCapacities_element_that_1_7_rejects()
        {
            // Observed in a real 1.7.5005 log: "<requiredCapacities /> doesn't correspond to any field in type ThoughtDef" (WFL_BabyCloseBy).
            foreach (var f in RepoPaths.DefAndPatchFiles())
                Assert.DoesNotContain("<requiredCapacities", System.IO.File.ReadAllText(f));
        }

        [Fact] public void The_vanilla_hooks_target_the_two_methods_by_name_and_live_in_the_framework_only()
        {
            string h = Src("Game/MedicalWorkHooks.cs");
            Assert.Contains("typeof(Bill_Medical), nameof(Bill_Medical.Notify_IterationCompleted)", h); Assert.Contains("typeof(TendUtility), nameof(TendUtility.DoTend)", h);
            Assert.Contains("Pawn billDoer", h); Assert.Contains("Pawn doctor, Pawn patient", h);          // parameter names must match the game's for Harmony to bind them
            Assert.Contains("[HarmonyLib.HarmonyPatch", h);
        }
    }
}
