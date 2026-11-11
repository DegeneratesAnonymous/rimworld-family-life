using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Framework
{
    /// <summary>
    /// Where a module stands in a particular save. "Absent" is not stored: it is what a recorded module is when its code is not installed. A module with no record is Active.
    /// </summary>
    public enum ModuleState { Active = 0, PreparationRequested = 1, PreparationFailed = 2, Prepared = 3 }

    public sealed class ModuleLifecycleRecord
    {
        public string ModuleId;
        public ModuleState State;
        public int Tick;
        public int Attempts;
        /// <summary>The last preparation report, kept in the save so a failed preparation can be read and retried later.</summary>
        public string Report = "";
    }

    /// <summary>
    /// The per-save lifecycle of the modules. It is saved with the family records, so a prepared module stays inactive across a restart and a reinstall until someone explicitly
    /// reactivates it, and a module that is not installed keeps its record untouched. Plain C#, no game types.
    /// </summary>
    public sealed class ModuleLifecycle
    {
        public readonly List<ModuleLifecycleRecord> Records = new List<ModuleLifecycleRecord>();

        /// <summary>
        /// Modules whose "not installed" notice the player asked not to see again, in this save. Per module, so dismissing one never hides another that goes missing later.
        /// Cleared for a module when it is installed again, so removing it a second time warns again.
        /// </summary>
        public readonly List<string> NoticeSuppressed = new List<string>();
        public bool IsNoticeSuppressed(string id) => NoticeSuppressed.Contains(id);
        public void SuppressNotice(string id) { if (!string.IsNullOrEmpty(id) && !NoticeSuppressed.Contains(id)) NoticeSuppressed.Add(id); }
        public bool ClearNoticeSuppression(string id) => NoticeSuppressed.Remove(id);
        /// <summary>The dismissed notices as a list for the save, and back (the game's ExposeLifecycle uses exactly these).</summary>
        public List<string> NoticeToList() => NoticeSuppressed.ToList();
        public void NoticeFromList(IEnumerable<string> ids) { NoticeSuppressed.Clear(); if (ids != null) foreach (var id in ids) SuppressNotice(id); }

        public ModuleLifecycleRecord Find(string id) => Records.FirstOrDefault(r => r.ModuleId == id);
        public ModuleState StateOf(string id) => Find(id)?.State ?? ModuleState.Active;

        /// <summary>Whether the module may schedule new work, give out posts and show controls. Anything but Active means no.</summary>
        public bool MayRun(string id) => StateOf(id) == ModuleState.Active;

        private ModuleLifecycleRecord Ensure(string id)
        {
            var r = Find(id);
            if (r == null) { r = new ModuleLifecycleRecord { ModuleId = id }; Records.Add(r); }
            return r;
        }

        /// <summary>New work stops from this moment. Returns false if the module is already prepared (nothing to do).</summary>
        public bool RequestPreparation(string id, int tick)
        {
            var r = Ensure(id);
            if (r.State == ModuleState.Prepared) return false;
            r.State = ModuleState.PreparationRequested; r.Tick = tick; r.Attempts++;
            return true;
        }

        /// <summary>Records how a preparation ended. Only a report with no failed step makes the module Prepared; otherwise it stays stopped, marked failed, and can be retried.</summary>
        public ModuleState Finish(string id, CleanupReport report, int tick)
        {
            var r = Ensure(id);
            r.State = report != null && report.Prepared ? ModuleState.Prepared : ModuleState.PreparationFailed;
            r.Tick = tick; r.Report = report == null ? "" : report.ToString();
            return r.State;
        }

        /// <summary>Puts a Prepared (or failed) module back to Active. Only ever called by an explicit request; loading, restarting or reinstalling never does this.</summary>
        public bool Reactivate(string id, int tick)
        {
            var r = Find(id);
            if (r == null || r.State == ModuleState.Active) return false;
            r.State = ModuleState.Active; r.Tick = tick;
            return true;
        }

        // ---- the parallel lists a save keeps

        public void ToLists(out List<string> ids, out List<int> states, out List<int> ticks, out List<int> attempts, out List<string> reports)
        {
            var live = Records.Where(r => r.State != ModuleState.Active).ToList();          // an Active module needs no record
            ids = live.Select(r => r.ModuleId).ToList(); states = live.Select(r => (int)r.State).ToList(); ticks = live.Select(r => r.Tick).ToList();
            attempts = live.Select(r => r.Attempts).ToList(); reports = live.Select(r => r.Report ?? "").ToList();
        }

        public void FromLists(List<string> ids, List<int> states, List<int> ticks, List<int> attempts, List<string> reports)
        {
            Records.Clear();
            if (ids == null) return;
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.IsNullOrEmpty(ids[i]) || Records.Any(r => r.ModuleId == ids[i])) continue;
                int st = states != null && i < states.Count ? states[i] : 0;
                // an unknown state from a newer version is treated as the safest one: stopped, not running
                var state = Enum.IsDefined(typeof(ModuleState), st) ? (ModuleState)st : ModuleState.PreparationFailed;
                Records.Add(new ModuleLifecycleRecord
                {
                    ModuleId = ids[i], State = state, Tick = ticks != null && i < ticks.Count ? ticks[i] : 0,
                    Attempts = attempts != null && i < attempts.Count ? attempts[i] : 0, Report = reports != null && i < reports.Count ? reports[i] ?? "" : ""
                });
            }
            // a save written while a preparation was only requested means it never finished: it is a failed attempt to retry, not a half-done success
            foreach (var r in Records.Where(x => x.State == ModuleState.PreparationRequested)) r.State = ModuleState.PreparationFailed;
        }
    }
}
