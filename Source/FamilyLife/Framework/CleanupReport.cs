using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Framework
{
    /// <summary>
    /// What preparing a module for removal actually did, and what it cannot do. A module is reported as prepared only if no step failed; the limits are always listed, so nothing is
    /// presented as cleaner than it is.
    /// </summary>
    public sealed class CleanupReport
    {
        public string ModuleId;
        public readonly List<string> Done = new List<string>(), Failed = new List<string>(), Limits = new List<string>();
        public bool Prepared => Failed.Count == 0;

        public CleanupReport(string moduleId) { ModuleId = moduleId; }

        /// <summary>Runs a step. A step that throws, or returns false, is recorded as failed (with the reason) and the rest still run.</summary>
        public void Step(string what, System.Func<string> action)
        {
            try { string result = action(); Done.Add(string.IsNullOrEmpty(result) ? what : what + ": " + result); }
            catch (System.Exception ex) { Failed.Add(what + " (" + ex.Message + ")"); }
        }

        public override string ToString() =>
            (Prepared ? "Prepared " : "NOT prepared ") + ModuleId + "\n" + string.Join("\n", Done.Select(d => "  done: " + d).Concat(Failed.Select(f => "  FAILED: " + f)).Concat(Limits.Select(l => "  limit: " + l)));
    }
}
