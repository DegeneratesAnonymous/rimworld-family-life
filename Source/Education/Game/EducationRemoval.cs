using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Takes Education out of a running game cleanly, and says honestly whether it did. Ends any class, sitting or lesson (the teacher and every pupil are released from their jobs and
    /// reservations; attendance is settled once, as for any interrupted class), stops an assessment in progress, empties the class list, drops the queued class and the planned
    /// event and its notice, removes the memories the module added, and gives up the teacher's post. A final step counts what is left and fails the preparation if anything is, so it can
    /// be run again. What the children studied stays on the record; skill experience already given stays (it is ordinary skill progress). Owner: Education.
    /// </summary>
    internal static class EducationRemoval
    {
        internal static readonly string[] Memories = { "WFL_InspiringLesson", "WFL_BoringLesson", "WFL_TestingProud", "WFL_TestingDone", "WFL_TestingDisappointed" };
        internal static readonly string[] Jobs = { "WFL_ClassTeacher", "WFL_ClassStudent", "WFL_TestStudent", "WFL_OfficeLesson" };

        internal static CleanupReport Prepare(FamilyComponent c)
        {
            var report = new CleanupReport(EducationModule.Id);
            int now = FrameworkServices.Now;
            report.Step("end any class, sitting or lesson in progress", () =>
            {
                HoldClass.Cancel();          // a class asked for at the next opportunity is dropped
                int released = ModuleCleanup.EndJobs(Jobs);          // the class settles once, whichever job ends first
                foreach (var reset in FamilyFramework.Resets.ForModule(EducationModule.Id)) reset();          // forget sessions and retry gaps
                return released + " pawn(s) released";
            });
            report.Step("stop the assessment, empty the class list, clear planned events and give up the post", () =>
            {
                var lines = c.Registry.EndEducationActivity(now, PendingEvent.Testing);
                foreach (var l in Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_Assessment>().ToList()) Find.LetterStack.RemoveLetter(l); c.Registry.Assessments().NoticeTick = 0;
                int sittings = c.Registry.Assessments().Active.Count; c.Registry.Assessments().Active.Clear(); if (sittings > 0) lines.Add(sittings + " assessment(s) in progress were stopped");
                foreach (var l in FrameworkServices.RemoveNotices(PendingEvent.Testing)) lines.Add(l);
                return lines.Count == 0 ? "nothing was under way" : string.Join("; ", lines);
            });
            report.Step("remove the lesson and assessment memories from everyone", () => ModuleCleanup.RemoveMemories(Memories) + " removed");
            report.Step("check that nothing of the module is left", () =>
            {
                var left = new List<string>();
                int onJob = ModuleCleanup.PawnsOnJobs(Jobs); if (onJob > 0) left.Add(onJob + " pawn(s) still on an Education job");
                int memories = ModuleCleanup.CountMemories(Memories); if (memories > 0) left.Add(memories + " Education memories");
                int notices = ModuleCleanup.CountNotices(PendingEvent.Testing); if (notices > 0) left.Add(notices + " notice(s)");
                var teacher = c.Registry.OfficeFor(OfficeKind.Teacher);
                if (teacher != null && (teacher.Held || teacher.SessionQueue.Count > 0 || teacher.PendingKind == PendingEvent.Testing || teacher.Assessment().TestingStartTick != 0)) left.Add("the teacher's post still holds work");
                if (c.Registry.Assessments().Active.Count > 0) left.Add("an assessment is still in progress");
                if (left.Count > 0) throw new InvalidOperationException("not ready for removal: " + string.Join(", ", left) + ". Run the preparation again.");
                return "nothing left";
            });
            report.Limits.Add("what each child studied, the books used and the recent subjects stay in the save as neutral data, and are picked up again if the module is added back");
            report.Limits.Add("the teacher role icon and name are shipped by the framework, so a saved post still reads as 'Teacher' in the history once the module is gone");
            report.Limits.Add("skill experience already given stays: it is ordinary skill progress and is not reversed");
            report.Limits.Add("a lesson or class cut short is settled once for the time attended, as any interrupted class is; it is not paid again");
            report.Limits.Add("this prepares the game as it is now. The module stays stopped across restarts until it is explicitly reactivated");
            return report;
        }
    }
}
