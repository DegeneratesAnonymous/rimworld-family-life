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
    /// Takes Education out of a running game cleanly: ends any class (the teacher and every pupil are released from their jobs), stops an assessment in progress, clears the class list, the
    /// queued class and the planned events, removes the memories the module added, and gives up the teacher's post. What the children studied stays on the record. Owner: Education.
    /// </summary>
    internal static class EducationRemoval
    {
        private static readonly string[] Memories = { "WFL_InspiringLesson", "WFL_BoringLesson", "WFL_TestingProud", "WFL_TestingDone", "WFL_TestingDisappointed" };
        private static readonly string[] Jobs = { "WFL_ClassTeacher", "WFL_ClassStudent", "WFL_TestStudent", "WFL_OfficeLesson" };

        internal static CleanupReport Prepare(FamilyComponent c)
        {
            var report = new CleanupReport(EducationModule.Id);
            int now = Activities.Now;
            report.Step("end any class, sitting or lesson in progress", () =>
            {
                HoldClass.Cancel();          // a class asked for at the next opportunity is dropped
                int ended = 0;
                foreach (var p in PawnsFinder.AllMapsWorldAndTemporary_Alive.ToList())
                {
                    var job = p.CurJobDef;
                    if (job != null && Jobs.Contains(job.defName)) { p.jobs.EndCurrentJob(JobCondition.InterruptForced, false); ended++; }
                    if (p.jobs?.jobQueue != null) p.jobs.ClearQueuedJobs();
                }
                FamilyModules.Resets.EnabledFor(EducationModule.Id)?.Invoke();          // forget class sessions and retry gaps
                return ended + " pawn(s) released";
            });
            report.Step("stop the assessment, empty the class list, clear planned events and give up the post", () =>
            {
                var lines = c.Registry.EndEducationActivity(now, PendingEvent.Testing);
                foreach (var l in OfficeEvents.RemoveNotices(PendingEvent.Testing)) lines.Add(l);
                return lines.Count == 0 ? "nothing was under way" : string.Join("; ", lines);
            });
            report.Step("remove the lesson and assessment memories from everyone", () =>
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
            report.Limits.Add("what each child studied, the books used and the recent subjects stay in the save as neutral data, and are picked up again if the module is added back");
            report.Limits.Add("the teacher role icon is shipped by this module; with it gone a post in a saved history shows a plain label");
            report.Limits.Add("skill experience already given stays: it is ordinary skill progress");
            report.Limits.Add("this prepares the game as it is now. If the module is allowed to run again before it is removed, prepare it again");
            return report;
        }
    }
}
