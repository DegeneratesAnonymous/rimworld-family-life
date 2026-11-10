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
            foreach (var o in r.Offices.Where(x => x.Kind == OfficeKind.Teacher)) { o.PendingKind = 0; o.Assessment().TestingStartTick = 0; }
            FrameworkServices.ForceUntil = Now + 6000;
            bool ok = FrameworkServices.BeginPlannedEvent(PendingEvent.Testing);
            Messages.Message(ok ? "The testing has begun: each child sits a check every free hour for two days." : "The testing could not begin.", MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
