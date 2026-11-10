using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;

namespace FamilyLife.Framework
{
    /// <summary>
    /// A post a module defines (the framework only knows how to show, hold, elect and authorise it). <see cref="Kind"/> is the value already written in saves, so the
    /// stable identifier is an alias for it and a save needs no migration for the post itself.
    /// </summary>
    public sealed class OfficeDefinition
    {
        public string Id, ModuleId, LabelKey;
        public OfficeKind Kind;
        /// <summary>Names of the skills whose passion makes someone want this post (a calling is the clearest reason to want it).</summary>
        public string[] PassionSkills = new string[0];
    }

    public sealed class OfficeCatalog
    {
        private readonly ModuleCatalog modules;
        private readonly Dictionary<string, OfficeDefinition> byId = new Dictionary<string, OfficeDefinition>();
        private readonly Dictionary<OfficeKind, OfficeDefinition> byKind = new Dictionary<OfficeKind, OfficeDefinition>();

        public OfficeCatalog(ModuleCatalog modules) { this.modules = modules; }

        public IEnumerable<OfficeDefinition> All => byId.Values;

        public void Register(OfficeDefinition def)
        {
            if (def == null) throw new RegistrationException("An office was registered as null.");
            ModuleCatalog.CheckId(def.Id, "Office");
            if (string.IsNullOrEmpty(def.ModuleId)) throw new RegistrationException("Office '" + def.Id + "' has no owning module.");
            if (!def.Id.StartsWith(def.ModuleId + ".", StringComparison.Ordinal)) throw new RegistrationException("Office '" + def.Id + "' must be inside its module's namespace '" + def.ModuleId + ".'.");
            if (byId.ContainsKey(def.Id)) throw new RegistrationException("Office '" + def.Id + "' is registered twice.");
            if (byKind.TryGetValue(def.Kind, out var clash)) throw new RegistrationException("Office '" + def.Id + "' uses saved kind " + def.Kind + ", which already belongs to '" + clash.Id + "'.");
            byId[def.Id] = def; byKind[def.Kind] = def;
        }

        public OfficeDefinition ById(string id) => id != null && byId.TryGetValue(id, out var d) ? d : null;
        public OfficeDefinition ByKind(OfficeKind kind) => byKind.TryGetValue(kind, out var d) ? d : null;

        /// <summary>A post can be given out only while the module that defines it is present and enabled. An old save that holds a post of an absent module keeps the record and shows it as historic.</summary>
        public bool Available(OfficeKind kind) { var d = ByKind(kind); return d != null && modules.IsEnabled(d.ModuleId); }

        /// <summary>
        /// A saved kind with no definition is a post of a module that is not installed: it stays in the save as a historic record and is never given out. It is not an error here (the
        /// tests check that every kind the framework ships has an owner).
        /// </summary>
        public bool IsHistoric(OfficeKind kind) => !byKind.ContainsKey(kind);

        public List<string> Validate()
        {
            var problems = new List<string>();
            foreach (var d in byId.Values) if (!modules.Modules.Any(m => m.Id == d.ModuleId)) problems.Add("Office '" + d.Id + "' belongs to unregistered module '" + d.ModuleId + "'.");
            return problems;
        }
    }

    /// <summary>
    /// Whether a post may be given out, listed as vacant or wished for. The framework sets this once its catalogs are built; until then, and in tests that do not care, every post is
    /// offered. A post of an absent module is neither elected nor shown as vacant, but a holder it already has stays visible.
    /// </summary>
    public static class OfficeAvailability
    {
        public static Func<OfficeKind, bool> IsOffered = k => true;
    }

    /// <summary>One piece of autonomous work a module wants run on a schedule.</summary>
    public sealed class WorkItem
    {
        public string Id, ModuleId;
        /// <summary>Ticks between runs; 0 means every time the roster is run (the existing cadence of the work it replaces).</summary>
        public int Interval;
        public Action<int> Run;
        internal int NextDue = int.MinValue;
        internal bool Cancelled;
    }

    /// <summary>
    /// The one list of registered autonomous work. The framework runs it from its existing maintenance step, so modules add no polling loop of their own. Work is staggered by
    /// identifier, limited by a budget per run, rotated so a full budget never starves the same item, skipped for a disabled module, and one item failing never stops the rest.
    /// </summary>
    public sealed class WorkRoster
    {
        private readonly ModuleCatalog modules;
        private readonly List<WorkItem> items = new List<WorkItem>();
        private int cursor;
        public WorkRoster(ModuleCatalog modules) { this.modules = modules; }

        public int Count => items.Count(i => !i.Cancelled);

        public void Register(WorkItem item)
        {
            if (item == null || item.Run == null) throw new RegistrationException("A work item was registered without anything to run.");
            ModuleCatalog.CheckId(item.Id, "Work item");
            if (!item.Id.StartsWith(item.ModuleId + ".", StringComparison.Ordinal)) throw new RegistrationException("Work item '" + item.Id + "' must be inside its module's namespace '" + item.ModuleId + ".'.");
            if (items.Any(i => i.Id == item.Id && !i.Cancelled)) throw new RegistrationException("Work item '" + item.Id + "' is registered twice.");
            items.Add(item);
        }

        /// <summary>Stops an item and any future runs of it. A module that is switched off cancels its own; the framework also skips a disabled module's work.</summary>
        public bool Cancel(string id)
        {
            var item = items.FirstOrDefault(i => i.Id == id && !i.Cancelled);
            if (item == null) return false;
            item.Cancelled = true;
            return true;
        }

        /// <summary>Runs what is due, at most <paramref name="budget"/> items (0 for no limit). Returns the identifiers run, in order.</summary>
        public List<string> RunDue(int now, int budget, Action<WorkItem, Exception> onError = null)
        {
            var ran = new List<string>();
            var live = items.Where(i => !i.Cancelled).ToList();
            if (live.Count == 0) return ran;
            int start = cursor % live.Count;
            int last = -1;
            for (int n = 0; n < live.Count; n++)
            {
                int index = (start + n) % live.Count;
                var item = live[index];
                if (!modules.IsEnabled(item.ModuleId)) continue;
                if (item.NextDue == int.MinValue) item.NextDue = now + Offset(item);          // first run is staggered, so items with the same interval do not fall on the same tick
                if (item.NextDue > now + Math.Max(1, item.Interval)) item.NextDue = now;          // the clock went back (a load): no waiting for a time that has not come
                if (now < item.NextDue) continue;
                if (budget > 0 && ran.Count >= budget) break;
                item.NextDue = now + item.Interval;
                try { item.Run(now); } catch (Exception ex) { onError?.Invoke(item, ex); }
                ran.Add(item.Id); last = index;
            }
            cursor = last >= 0 ? last + 1 : cursor;          // the next run begins after the last item that ran
            return ran;
        }

        private static int Offset(WorkItem item) => item.Interval <= 0 ? 0 : (item.Id.GetHashCode() & int.MaxValue) % item.Interval;
    }

    /// <summary>
    /// A routine event that a module plans, announces and runs (a check-up round, the annual assessment). The framework owns the notice, the saved pending state and the
    /// authority checks; the module owns what beginning, skipping and putting it off actually do.
    /// </summary>
    public sealed class PlannedEventDefinition
    {
        public string Id, ModuleId;
        /// <summary>The value already written in saves (<see cref="PendingEvent"/>).</summary>
        public int SavedKind;
        public OfficeKind Post;
        /// <summary>Translation key stem for the notice (WFL_Event_{NoticeKey}_Title / _Text) and the message shown when nobody holds the post.</summary>
        public string NoticeKey, NoPostKey;
        public Func<bool> Begin;
        public Action Pass;
    }

    public sealed class PlannedEventCatalog
    {
        private readonly ModuleCatalog modules;
        private readonly Dictionary<int, PlannedEventDefinition> byKind = new Dictionary<int, PlannedEventDefinition>();
        private readonly HashSet<string> ids = new HashSet<string>();
        public PlannedEventCatalog(ModuleCatalog modules) { this.modules = modules; }

        public void Register(PlannedEventDefinition def)
        {
            if (def == null || def.Begin == null || def.Pass == null) throw new RegistrationException("A planned event was registered without its Begin and Pass behaviour.");
            ModuleCatalog.CheckId(def.Id, "Planned event");
            if (!def.Id.StartsWith(def.ModuleId + ".", StringComparison.Ordinal)) throw new RegistrationException("Planned event '" + def.Id + "' must be inside its module's namespace '" + def.ModuleId + ".'.");
            if (!ids.Add(def.Id)) throw new RegistrationException("Planned event '" + def.Id + "' is registered twice.");
            if (byKind.ContainsKey(def.SavedKind)) throw new RegistrationException("Planned event '" + def.Id + "' uses saved kind " + def.SavedKind + ", which is already taken.");
            byKind[def.SavedKind] = def;
        }

        /// <summary>The event for a saved kind, or null when its module is absent or disabled: a waiting event of an absent module is dropped, never run.</summary>
        public PlannedEventDefinition For(int savedKind) => byKind.TryGetValue(savedKind, out var d) && modules.IsEnabled(d.ModuleId) ? d : null;
        public bool Known(int savedKind) => byKind.ContainsKey(savedKind);
    }

    /// <summary>
    /// Things a module adds to a game object's context (buttons on a lectern or a bed, say). One list per kind of contribution, each entry owned by a module and skipped while
    /// that module is disabled, so an absent module leaves no control behind.
    /// </summary>
    public sealed class ContributionList<T>
    {
        private readonly ModuleCatalog modules;
        private readonly List<KeyValuePair<string, T>> entries = new List<KeyValuePair<string, T>>();
        private readonly HashSet<string> ids = new HashSet<string>();
        public ContributionList(ModuleCatalog modules) { this.modules = modules; }

        public void Add(string id, string moduleId, T contribution)
        {
            ModuleCatalog.CheckId(id, "Contribution");
            if (!id.StartsWith(moduleId + ".", StringComparison.Ordinal)) throw new RegistrationException("Contribution '" + id + "' must be inside its module's namespace '" + moduleId + ".'.");
            if (!ids.Add(id)) throw new RegistrationException("Contribution '" + id + "' is registered twice.");
            entries.Add(new KeyValuePair<string, T>(moduleId, contribution));
        }

        public IEnumerable<T> Enabled() { foreach (var e in entries) if (modules.IsEnabled(e.Key)) yield return e.Value; }
        /// <summary>Every contribution of one module, whether or not the module is currently enabled (a preparation step must still be reachable while the module is stopped).</summary>
        public IEnumerable<T> ForModule(string moduleId) { foreach (var e in entries) if (e.Key == moduleId) yield return e.Value; }
        public int Count => entries.Count;
    }
}
