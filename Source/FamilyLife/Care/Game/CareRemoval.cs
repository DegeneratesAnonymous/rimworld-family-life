using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse.AI;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Takes Community Care out of a running game cleanly: ends the clinic and any appointment, clears the round and the planned events, removes the check-up condition and the memories the
    /// module added (so no saved thing names a def the module no longer ships), and gives up the doctor's post. The neutral record of who was seen stays. Owner: Community Care.
    /// </summary>
    internal static class CareRemoval
    {
        private static readonly string[] Memories = { "WFL_ReassuringCheckup", "WFL_DismissiveCheckup" };
        private static readonly string[] Jobs = { "WFL_ClinicDoctor", "WFL_ClinicPatient", "WFL_Checkup" };

        internal static CleanupReport Prepare(FamilyComponent c)
        {
            var report = new CleanupReport(CareModule.Id);
            int now = Activities.Now;
            report.Step("end any clinic and appointment in progress", () =>
            {
                int ended = 0;
                foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive.ToList())
                {
                    var job = p.CurJobDef;
                    if (job != null && Jobs.Contains(job.defName)) { p.jobs.EndCurrentJob(JobCondition.InterruptForced, false); ended++; }
                    if (p.jobs?.jobQueue != null) p.jobs.ClearQueuedJobs();
                }
                FamilyModules.Resets.EnabledFor(CareModule.Id)?.Invoke();          // forget clinic sessions, appointment gaps and the cached emergency reading
                return ended + " pawn(s) released";
            });
            report.Step("close the round, clear planned events and give up the post", () =>
            {
                var lines = c.Registry.EndCareActivity(now, PendingEvent.Checkups, PendingEvent.Illness);
                foreach (var l in OfficeEvents.RemoveNotices(PendingEvent.Checkups, PendingEvent.Illness)) lines.Add(l);
                return lines.Count == 0 ? "nothing was under way" : string.Join("; ", lines);
            });
            report.Step("remove the recent check-up condition from everyone", () =>
            {
                int n = 0;
                var def = DefDatabase<HediffDef>.GetNamedSilentFail("WFL_CheckedUp");
                if (def != null)
                    foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.ToList())
                    {
                        var h = p.health?.hediffSet?.GetFirstHediffOfDef(def);
                        if (h != null) { p.health.RemoveHediff(h); n++; }
                    }
                return n + " removed";
            });
            report.Step("remove the check-up memories from everyone", () =>
            {
                int n = 0;
                foreach (var name in Memories)
                {
                    var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(name); if (def == null) continue;
                    foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.ToList())
                    {
                        var memories = p.needs?.mood?.thoughts?.memories; if (memories == null) continue;
                        n += memories.Memories.Count(m => m.def == def);
                        memories.RemoveMemoriesOfDef(def);
                    }
                }
                return n + " removed";
            });
            report.Limits.Add("the doctor's record of rounds and who was seen is kept in the save as neutral data, and is picked up again if the module is added back");
            report.Limits.Add("the healer role icon is shipped by this module; with it gone a post in a saved history shows a plain label");
            report.Limits.Add("this prepares the game as it is now. If the module is allowed to run again before it is removed, prepare it again");
            return report;
        }
    }
}
