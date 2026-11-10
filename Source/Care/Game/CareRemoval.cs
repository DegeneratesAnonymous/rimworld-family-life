using System;
using System.Collections.Generic;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Takes Community Care out of a running game cleanly, and says honestly whether it did. Ends the clinic and any appointment (doctor and patients are released from their jobs and
    /// reservations), closes the round, clears planned events and their notices, removes the check-up condition and the module's memories, and gives up the doctor's post. A final step
    /// counts what is left and fails the preparation if anything is, so it can be run again. The community's care record (who was seen, who declined) stays. Owner: Community Care.
    /// </summary>
    internal static class CareRemoval
    {
        internal static readonly string[] Memories = { "WFL_ReassuringCheckup", "WFL_DismissiveCheckup" };
        internal static readonly string[] Jobs = { "WFL_ClinicDoctor", "WFL_ClinicPatient", "WFL_Checkup" };
        internal const string Condition = "WFL_CheckedUp";

        internal static CleanupReport Prepare(FamilyComponent c)
        {
            var report = new CleanupReport(CareModule.Id);
            int now = FrameworkServices.Now;
            report.Step("end any clinic and appointment in progress", () =>
            {
                int released = ModuleCleanup.EndJobs(Jobs);
                foreach (var reset in FamilyFramework.Resets.ForModule(CareModule.Id)) reset();          // forget clinic sessions, appointment gaps and the cached emergency reading
                return released + " pawn(s) released";
            });
            report.Step("close the round, clear planned events and give up the post", () =>
            {
                var lines = c.Registry.EndCareActivity(now, PendingEvent.Checkups, PendingEvent.Illness);
                foreach (var l in FrameworkServices.RemoveNotices(PendingEvent.Checkups, PendingEvent.Illness)) lines.Add(l);
                return lines.Count == 0 ? "nothing was under way" : string.Join("; ", lines);
            });
            report.Step("remove the recent check-up condition from everyone", () => ModuleCleanup.RemoveHediffs(Condition) + " removed");
            report.Step("remove the check-up memories from everyone", () => ModuleCleanup.RemoveMemories(Memories) + " removed");
            report.Step("check that nothing of the module is left", () =>
            {
                var left = new List<string>();
                int onJob = ModuleCleanup.PawnsOnJobs(Jobs); if (onJob > 0) left.Add(onJob + " pawn(s) still on a Care job");
                int cond = ModuleCleanup.CountHediffs(Condition); if (cond > 0) left.Add(cond + " check-up condition(s)");
                int memories = ModuleCleanup.CountMemories(Memories); if (memories > 0) left.Add(memories + " Care memories");
                int notices = ModuleCleanup.CountNotices(PendingEvent.Checkups, PendingEvent.Illness); if (notices > 0) left.Add(notices + " notice(s)");
                var doctor = c.Registry.OfficeFor(OfficeKind.Healer);
                if (doctor != null && (doctor.Held || doctor.PendingKind == PendingEvent.Checkups || doctor.PendingKind == PendingEvent.Illness || doctor.Care().RoundStartTick != 0)) left.Add("the doctor's post still holds work");
                if (left.Count > 0) throw new InvalidOperationException("not ready for removal: " + string.Join(", ", left) + ". Run the preparation again.");
                return "nothing left";
            });
            report.Limits.Add("the community's care record (who was seen, who was missed, who declined and until when) stays in the save as neutral data, and is picked up again if the module is added back");
            report.Limits.Add("the healer role icon and name are shipped by the framework, so a saved post still reads as 'Healer' in the history once the module is gone");
            report.Limits.Add("health already improved or worsened is ordinary game state and is not reversed; only the module's own check-up condition and memories are removed");
            report.Limits.Add("this prepares the game as it is now. The module stays stopped across restarts until it is explicitly reactivated");
            return report;
        }
    }
}
