using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// The cleanup operations a module's removal step is made of: end the jobs it owns (releasing every reservation), remove the conditions and memories it added, and count what is
    /// left so the step can fail instead of claiming success. Each operation touches only the defs it is given by name, so a module can remove only what it owns, and a def that is
    /// not loaded is simply nothing to remove. Safe to repeat. Owner: the framework.
    /// </summary>
    public static class ModuleCleanup
    {
        private static List<Pawn> Living() => PawnsFinder.AllMapsWorldAndTemporary_Alive.ToList();
        private static List<Pawn> Everyone() => PawnsFinder.AllMapsWorldAndTemporary_AliveOrDead.ToList();

        private static bool OnJob(Pawn p, ICollection<string> names)
        {
            if (p.CurJobDef != null && names.Contains(p.CurJobDef.defName)) return true;
            var queue = p.jobs?.jobQueue;
            if (queue != null) foreach (var q in queue) if (q.job?.def != null && names.Contains(q.job.def.defName)) return true;
            return false;
        }

        /// <summary>Ends the current and queued jobs of the given defs on everyone, and releases the reservations those pawns held. Returns how many pawns were released.</summary>
        public static int EndJobs(params string[] jobDefNames)
        {
            int released = 0;
            foreach (var p in Living())
            {
                if (p.jobs == null || !OnJob(p, jobDefNames)) continue;
                if (p.CurJobDef != null && jobDefNames.Contains(p.CurJobDef.defName)) p.jobs.EndCurrentJob(JobCondition.InterruptForced, false);
                if (p.jobs.jobQueue != null) p.jobs.ClearQueuedJobs();
                var map = p.Map;
                if (map != null) { map.reservationManager.ReleaseAllClaimedBy(p); map.pawnDestinationReservationManager.ReleaseAllClaimedBy(p); map.physicalInteractionReservationManager.ReleaseAllClaimedBy(p); }
                released++;
            }
            return released;
        }

        /// <summary>How many pawns are still on (or queued for) one of the given jobs.</summary>
        public static int PawnsOnJobs(params string[] jobDefNames) => Living().Count(p => p.jobs != null && OnJob(p, jobDefNames));

        /// <summary>Removes every instance of a hediff from everyone. Returns how many were removed.</summary>
        public static int RemoveHediffs(string defName)
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail(defName); if (def == null) return 0;
            int n = 0;
            foreach (var p in Everyone())
            {
                var h = p.health?.hediffSet?.GetFirstHediffOfDef(def);
                while (h != null) { p.health.RemoveHediff(h); n++; h = p.health.hediffSet.GetFirstHediffOfDef(def); }
            }
            return n;
        }

        public static int CountHediffs(string defName)
        {
            var def = DefDatabase<HediffDef>.GetNamedSilentFail(defName); if (def == null) return 0;
            return Everyone().Count(p => p.health?.hediffSet?.GetFirstHediffOfDef(def) != null);
        }

        /// <summary>Removes the given memories from everyone. Returns how many were removed.</summary>
        public static int RemoveMemories(params string[] thoughtDefNames)
        {
            int n = 0;
            foreach (var name in thoughtDefNames)
            {
                var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(name); if (def == null) continue;
                foreach (var p in Everyone())
                {
                    var memories = p.needs?.mood?.thoughts?.memories; if (memories == null) continue;
                    n += memories.Memories.Count(m => m.def == def);
                    memories.RemoveMemoriesOfDef(def);
                }
            }
            return n;
        }

        public static int CountMemories(params string[] thoughtDefNames)
        {
            int n = 0;
            foreach (var name in thoughtDefNames)
            {
                var def = DefDatabase<ThoughtDef>.GetNamedSilentFail(name); if (def == null) continue;
                foreach (var p in Everyone()) n += p.needs?.mood?.thoughts?.memories?.Memories.Count(m => m.def == def) ?? 0;
            }
            return n;
        }

        /// <summary>Planned-event notices for the given saved kinds still on the letter stack.</summary>
        public static int CountNotices(params int[] savedKinds) => Find.LetterStack.LettersListForReading.OfType<ChoiceLetter_OfficeEvent>().Count(l => savedKinds.Contains(l.kind));
    }
}
