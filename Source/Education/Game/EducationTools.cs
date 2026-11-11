using System;
using System.Linq;
using FamilyLife.Domain;
using LudeonTK;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>Developer-menu shortcuts for the teacher's work. Owner: Education.</summary>
    internal static class EducationTools
    {
        private const string Cat = "Wil's Family Life, testing";
        private static FamilyComponent Comp => FamilyComponent.Current;
        private static int Now => Find.TickManager.TicksGame;

        [DebugAction("Wil's Family Life, testing", "Reactivate Education (after preparing it for removal)", allowedGameStates = AllowedGameStates.Playing)]
        private static void Reactivate() { var c = FamilyComponent.Current; if (c == null) return; Log.Message("[FamilyLife] Education reactivated: " + FamilyFramework.Reactivate(EducationModule.Id, c) + ". State: " + FamilyFramework.Describe(c, EducationModule.Id)); }

        [DebugAction("Wil's Family Life, testing", "Prepare Education for removal (report in the log)", allowedGameStates = AllowedGameStates.Playing)]
        private static void PrepareRemoval() { var c = FamilyComponent.Current; if (c == null) return; Log.Message("[FamilyLife] " + FamilyFramework.PrepareForRemoval(EducationModule.Id, c)); }

        [DebugAction(Cat, "Start a class now", allowedGameStates = AllowedGameStates.Playing)]
        private static void ClassNow()
        {
            var c = Comp; var r = c?.Registry; if (r == null) return;
            foreach (var o in r.Offices.Where(x => x.Held && x.Kind == OfficeKind.Teacher)) { o.NextSessionTick = 0; o.SessionQueue.Clear(); }
            foreach (int t in r.TeacherSchedule().People.ToList()) r.TeacherSchedule().Set(t, 0);
            ClassSessions.ClearBackoff();          // earlier failed tries must not hold a test off
            FrameworkServices.ForceUntil = Now + 6000;
            HoldClass.Queue(null, Now);          // if the teacher cannot start now, the class is held at the next opportunity (for up to a day)
            FrameworkServices.WorkNow(c, Now, OfficeKind.Teacher);
            var lines = FrameworkServices.Diagnose(c, Now, OfficeKind.Teacher);
            foreach (var l in lines) Log.Message("[Wil's Family Life] test: " + l);
            Messages.Message("Started the class if it could start.\n" + string.Join("\n", lines), MessageTypeDefOf.TaskCompletion, false);
        }

        [DebugAction(Cat, "Start annual testing now (two days)", allowedGameStates = AllowedGameStates.Playing)]
        private static void TestingNow()
        {
            var r = Comp?.Registry; if (r == null) return;
            var ledger = r.Assessments(); bool ok = false;
            foreach (int id in EducationCareers.Instructors(r)) { ledger.End(id); ledger.SetLastYear(id, 0); ledger.Postpone(id, 0); }
            ledger.NoticeTick = 0;
            FrameworkServices.ForceUntil = Now + 6000;
            foreach (int id in EducationCareers.Instructors(r)) if (Comp.TryGetPawn(id) is Pawn t && t.Spawned) ok |= EducationEvents.BeginTesting(Comp, r, t, Now, true);
            Messages.Message(ok ? "Each teacher's own testing has begun for their pupils: each child sits a check every free hour for two days." : "The testing could not begin (no teacher has lessons on record to assess).", MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
