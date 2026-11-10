using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;

namespace FamilyLife.Framework
{
    /// <summary>A module whose code is not installed but which a save still has records for.</summary>
    public sealed class AbsentModule
    {
        public string ModuleId;
        /// <summary>The module's state in this save (Active when it was running when the save was made).</summary>
        public ModuleState State;
        /// <summary>The posts the save holds for it (their saved kinds), and the ids of the state payloads it owns.</summary>
        public List<OfficeKind> Posts = new List<OfficeKind>();
        public List<string> Payloads = new List<string>();
        /// <summary>The module was deliberately prepared for removal: nothing is wrong, so nothing is reported.</summary>
        public bool Prepared => State == ModuleState.Prepared;
    }

    /// <summary>
    /// Finds the modules a save refers to that are not installed. Read-only: it only looks, so it can never activate, migrate or change anything a module owns. Plain C#, no game types.
    /// </summary>
    public static class AbsentModules
    {
        /// <summary>The module a state payload id belongs to ("wils.care.record" is "wils.care"). Framework-owned ids (legacy migration data) and malformed ids belong to nobody.</summary>
        public static string OwnerOfPayload(string payloadId)
        {
            if (string.IsNullOrEmpty(payloadId) || !ModuleCatalog.IdPattern.IsMatch(payloadId)) return null;
            var parts = payloadId.Split('.');
            if (parts.Length < 2) return null;
            string owner = parts[0] + "." + parts[1];
            return owner == "wils.legacy" || owner == ModuleCatalog.FrameworkId || owner == "wils.familylife" ? null : owner;
        }

        /// <summary>
        /// Every module this save holds a post, a state payload or a lifecycle record for that is not installed. Sorted by id so the order is always the same. A module that was prepared
        /// for removal is included with <see cref="AbsentModule.Prepared"/> set; callers that warn skip it.
        /// </summary>
        public static List<AbsentModule> Find(FamilyRegistry registry, Func<string, bool> isInstalled)
        {
            var found = new Dictionary<string, AbsentModule>();
            AbsentModule Entry(string id) { if (!found.TryGetValue(id, out var e)) found[id] = e = new AbsentModule { ModuleId = id }; return e; }
            if (registry == null) return new List<AbsentModule>();
            foreach (var o in registry.Offices)
            {
                string owner = LegacyOfficeOwners.ModuleOf(o.Kind);
                bool has = o.Held || (o.PayloadIds != null && o.PayloadIds.Any(x => OwnerOfPayload(x) == owner || x == LegacyOfficeMigration.PayloadId));
                if (owner != null && !isInstalled(owner) && has) { var e = Entry(owner); if (!e.Posts.Contains(o.Kind)) e.Posts.Add(o.Kind); }
                if (o.PayloadIds == null) continue;
                foreach (var pid in o.PayloadIds)
                {
                    string who = OwnerOfPayload(pid);
                    if (who == null || isInstalled(who)) continue;
                    var e = Entry(who); if (!e.Payloads.Contains(pid)) e.Payloads.Add(pid);
                }
            }
            foreach (var rec in registry.Lifecycle.Records)
                if (!string.IsNullOrEmpty(rec.ModuleId) && !isInstalled(rec.ModuleId)) Entry(rec.ModuleId);
            foreach (var e in found.Values) { e.State = registry.Lifecycle.StateOf(e.ModuleId); e.Posts.Sort(); e.Payloads.Sort(StringComparer.Ordinal); }
            return found.Values.OrderBy(x => x.ModuleId, StringComparer.Ordinal).ToList();
        }

        /// <summary>
        /// The modules to tell the player about now: absent, not deliberately prepared, and not dismissed in this save. Nothing for a new game (no saved records), a prepared module,
        /// or a module whose notice was dismissed; a newly absent module is never covered by an earlier dismissal because dismissal is per module.
        /// </summary>
        public static List<AbsentModule> ToReport(FamilyRegistry registry, Func<string, bool> isInstalled) =>
            Find(registry, isInstalled).Where(m => !m.Prepared && !registry.Lifecycle.IsNoticeSuppressed(m.ModuleId)).ToList();

        /// <summary>A reinstalled module is no longer absent: a dismissal made while it was missing no longer applies (the next removal warns again). Changes nothing the module owns.</summary>
        public static int ClearSuppressionForInstalled(FamilyRegistry registry, Func<string, bool> isInstalled) =>
            registry.Lifecycle.NoticeSuppressed.Where(isInstalled).ToList().Count(id => registry.Lifecycle.ClearNoticeSuppression(id));
    }
}
