using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Structural guards on the game-side code, which cannot run outside RimWorld. They check that behaviour the rebalance removed has not come back; they do not show that the
    /// replacement works in the game (that needs a play-through, listed in the change notes).
    /// </summary>
    public class OfficeSourceGuardTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Source", "FamilyLife", "FamilyLife.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new FileNotFoundException("repository root");
        }
        private static string Src => Path.Combine(Root(), "Source", "FamilyLife");
        private static string Read(string rel) => RepoPaths.Read(rel);
        private static string[] AllGameFiles() => RepoPaths.AllSource();

        [Fact]
        public void A_refused_or_missed_appointment_is_never_chased_or_escorted()
        {
            string clinic = Read("Care/Game/Clinic.cs");
            Assert.DoesNotContain("Fetching", clinic); Assert.DoesNotContain("Escorting", clinic); Assert.DoesNotContain("EscortTicks", clinic);
            Assert.DoesNotMatch(new Regex(@"StartPath\(\s*patient"), clinic);          // the doctor never walks to a patient to bring them in
        }

        [Fact]
        public void A_check_up_does_not_tend_and_has_no_automatic_kin_duration_bonus()
        {
            string care = Read("Care/Game/CareDriver.cs");
            Assert.DoesNotContain("DoTend", care);
            Assert.DoesNotContain("duration *= 2f", care);
            foreach (var f in AllGameFiles()) Assert.DoesNotContain("ThankPost(", File.ReadAllText(f).Replace("public void ThankPost(", "").Replace(".ThankPost(OfficeKind.HallKeeper", "").Replace("ThankPost(kind", ""));
            foreach (var rel in new[] { "Care/Game/Clinic.cs", "Education/Game/Classes.cs", "Education/Game/EducationEvents.cs", "Care/Game/CareEvents.cs" })
                Assert.DoesNotContain("RecordTogether", Read(rel));
        }

        [Fact]
        public void Lesson_and_book_experience_is_given_in_one_place_only()
        {
            string classes = Read("Education/Game/Classes.cs");
            int grant = classes.IndexOf("internal static class LessonGrant"); int teacherJob = classes.IndexOf("public class JobDriver_ClassTeacher");
            var learns = Regex.Matches(classes, @"\.Learn\(");
            Assert.Equal(2, learns.Count);          // the lesson and the book, both inside LessonGrant.Apply
            foreach (Match m in learns) Assert.InRange(m.Index, grant, teacherJob);
            Assert.Equal(1, Regex.Matches(Read("Education/Game/EducationEvents.cs"), @"\.Learn\(").Count);          // the small consolidation bonus of the assessment
            Assert.Equal(1, Regex.Matches(Read("Game/ProfessionalConversations.cs"), @"\.Learn\(").Count);          // the professional's own Social experience in a conversation
            foreach (var rel in new[] { "Education/Game/EducationDriver.cs", "Education/Game/ClassRooms.cs", "Education/Game/HoldClass.cs", "Care/Game/Clinic.cs", "Care/Game/CareDriver.cs", "Care/Game/CareEvents.cs" })
                Assert.Equal(0, Regex.Matches(Read(rel), @"\.Learn\(").Count);
        }

        [Fact]
        public void Pupils_and_the_doctor_stop_for_real_needs_and_emergencies()
        {
            string classes = Read("Education/Game/Classes.cs"), clinic = Read("Care/Game/Clinic.cs");
            Assert.Contains("PawnReadinessApi.StopReason(pawn", classes);          // both the teacher's and the pupils' jobs
            Assert.Contains("TeacherGone()", classes);
            Assert.Contains("CareDemand.DoctorShouldTend(pawn", clinic);
        }
    }
}
