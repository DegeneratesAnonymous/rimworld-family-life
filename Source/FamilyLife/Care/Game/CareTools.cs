using System;
using System.Linq;
using FamilyLife.Domain;
using LudeonTK;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>Developer-menu shortcuts for the doctor's work. Owner: Community Care.</summary>
    internal static class CareTools
    {
        private const string Cat = "Wil's Family Life, testing";
        private static FamilyComponent Comp => FamilyComponent.Current;
        private static int Now => Find.TickManager.TicksGame;

        [DebugAction(Cat, "Prepare Community Care for removal (report in the log)", allowedGameStates = AllowedGameStates.Playing)]
        private static void PrepareRemoval() { var c = Comp; if (c == null) return; Log.Message("[FamilyLife] " + FamilyModules.PrepareForRemoval(CareModule.Id, c)); }

        [DebugAction(Cat, "Start a check-up round now (clears recent check-ups)", allowedGameStates = AllowedGameStates.Playing)]
        private static void CheckupNow()
        {
            var c = Comp; var r = c?.Registry; if (r == null) return;
            foreach (var o in r.Offices.Where(x => x.Held && x.Kind == OfficeKind.Healer)) { o.Care().NextRoundTick = 0; o.Care().RoundStartTick = 0; o.Care().Examined.Clear(); }
            AppointmentBook.Reset();
            foreach (var p in Find.Maps.SelectMany(m => m.mapPawns.FreeColonistsSpawned))          // a test should not be skipped because someone was seen recently
            {
                var h = p.health?.hediffSet?.GetFirstHediffOfDef(WFL_HediffDefOf.WFL_CheckedUp);
                if (h != null) p.health.RemoveHediff(h);
            }
            OfficeDriver.ForceUntil = Now + 6000;
            OfficeDriver.WorkNow(c, Now, OfficeKind.Healer);
            var lines = OfficeDriver.Diagnose(c, Now, OfficeKind.Healer);
            foreach (var l in lines) Log.Message("[Wil's Family Life] test: " + l);
            Messages.Message("Started the check-up round if it could start.\n" + string.Join("\n", lines), MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
