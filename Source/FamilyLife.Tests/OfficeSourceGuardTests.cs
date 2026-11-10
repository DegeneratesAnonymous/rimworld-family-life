using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Structural guards on the game-side code, which cannot run outside RimWorld. They check that behaviour this change removed has not come back; they do not show that the
    /// replacement works in the game (that needs a play-through, listed in the change notes).
    /// </summary>
    public class OfficeSourceGuardTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Source", "FamilyLife", "FamilyLife.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new FileNotFoundException("repository root");
        }
        private static string Read(string rel) => File.ReadAllText(Path.Combine(Root(), "Source", "FamilyLife", rel));

        [Fact]
        public void A_refused_or_missed_appointment_is_never_chased_or_escorted()
        {
            string clinic = Read("Game/Clinic.cs");
            Assert.DoesNotContain("Fetching", clinic); Assert.DoesNotContain("Escorting", clinic); Assert.DoesNotContain("EscortTicks", clinic);
            Assert.DoesNotMatch(new Regex(@"StartPath\(\s*patient"), clinic);          // the doctor never walks to a patient to bring them in
        }

        [Fact]
        public void A_check_up_does_not_tend_and_has_no_automatic_kin_duration_bonus()
        {
            string driver = Read("Game/OfficeDriver.cs");
            Assert.DoesNotContain("DoTend", driver);
            Assert.DoesNotContain("duration *= 2f", driver);
            Assert.DoesNotContain("ThankPost", driver);          // the one place a relationship is changed is FamilyRegistry.ProfessionalExchange
            Assert.DoesNotContain("RecordTogether", Read("Game/Clinic.cs"));
            Assert.DoesNotContain("RecordTogether", Read("Game/Classes.cs"));
            Assert.DoesNotContain("RecordTogether", Read("Game/OfficeEvents.cs"));
        }

        [Fact]
        public void Lesson_and_book_experience_is_given_in_one_place_only()
        {
            foreach (var file in new[] { "Game/Classes.cs", "Game/ClinicAndClass.cs", "Game/OfficeDriver.cs", "Game/OfficeEvents.cs", "Game/Clinic.cs" })
            {
                string src = Read(file);
                int learns = Regex.Matches(src, @"\.Learn\(").Count;
                if (file == "Game/Classes.cs") Assert.Equal(2, learns);          // the lesson and the book, both inside LessonGrant.Apply
                else if (file == "Game/OfficeEvents.cs") Assert.Equal(1, learns);          // the small consolidation bonus of the assessment
                else if (file == "Game/ClinicAndClass.cs") Assert.Equal(1, learns);          // the professional's own Social experience in a conversation
                else Assert.Equal(0, learns);
            }
            string classes = Read("Game/Classes.cs");
            int grant = classes.IndexOf("internal static class LessonGrant"); int teacherJob = classes.IndexOf("public class JobDriver_ClassTeacher");
            foreach (Match m in Regex.Matches(classes, @"\.Learn\(")) Assert.InRange(m.Index, grant, teacherJob);
        }

        [Fact]
        public void Pupils_and_the_doctor_stop_for_real_needs_and_emergencies()
        {
            string classes = Read("Game/Classes.cs"), clinic = Read("Game/Clinic.cs");
            Assert.Contains("CareDemand.StopReason(pawn", classes);          // both the teacher's and the pupils' jobs
            Assert.Contains("TeacherGone()", classes);
            Assert.Contains("CareDemand.DoctorShouldTend(pawn", clinic);
        }
    }
}
