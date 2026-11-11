using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// What an optional module must provide to be installed: a stable id and one registration call that adds everything it owns (posts, work, events, buttons, meetings, cleanup).
    /// </summary>
    public interface IFamilyModule
    {
        string Id { get; }
        void Register();
    }

    /// <summary>
    /// The framework's public host. It builds its registries once, installs the modules that announce themselves (each from its own mod constructor, in load order), then finalizes at the first FinalizeInit: the
    /// whole set is validated once, and from then on nothing can be added and nothing is looked up by reflection. A module that is absent has no entry; one that is refused, failed or is
    /// in a stopped lifecycle state is ignored by every registry. Owner: the framework. Optional modules reference this type and nothing in the framework's internals.
    /// </summary>
    public static class FamilyFramework
    {
        /// <summary>The registration contract version modules are built against (<see cref="ModuleInfo.ApiVersion"/>).</summary>
        public const int ApiVersion = ModuleCatalog.CurrentApiVersion;

        /// <summary>The work a module does for the holder of one of its posts. <paramref name="holder"/> is null when they are not on a map.</summary>
        public delegate void OfficeWork(FamilyComponent c, FamilyRegistry r, CommunityOffice office, Pawn holder, FamilySettings settings, int now);

        public static readonly ModuleCatalog Catalog = new ModuleCatalog();
        public static readonly OfficeCatalog Offices;
        public static readonly WorkRoster Roster;
        public static readonly PlannedEventCatalog PlannedEvents;
        public static readonly MeetingCatalog Meetings;
        /// <summary>The pages of the Community window. A page draws itself into the rect it is given and returns the height it used (ignored when the page sets <c>OwnsScrolling</c>).</summary>
        public static readonly CommunityPageCatalog<Func<Rect, FamilyComponent, float>> Pages;
        public static readonly ContributionList<Func<Building, IEnumerable<Gizmo>>> BuildingGizmos;
        public static readonly ContributionList<DiagnosticSection> Diagnostics;
        public static readonly ContributionList<Action<Pawn, Pawn, int>> OperationCompleted;          // a vanilla operation was completed: (doer, patient, bill id). Read-only: the framework owns the patch, modules only listen
        public static readonly ContributionList<Action<Pawn, Pawn>> Tended;          // a vanilla tend was done: (doctor, patient). Read-only, as above
        public static readonly ContributionList<Action<CommunityOffice>> DueNow;          // developer menu: brings a module's timers on a post due
        public static readonly ContributionList<Func<FamilyComponent, CleanupReport>> Removal;          // "prepare for removal": one per module
        public static readonly ContributionList<Action> Resets;          // clears a module's in-memory state when a game is loaded or ended
        public static readonly ContributionList<Action<FamilyComponent>> Reconcilers;          // once per loaded game: checks the module's saved state against the world before any work
        public static readonly ContributionList<Action<FamilyComponent, CommunityOffice, int, int>> Handoffs;          // a post changed hands: (office, old holder, new holder)
        private static readonly Dictionary<OfficeKind, OfficeWork> officeWork = new Dictionary<OfficeKind, OfficeWork>();
        private static readonly ModuleHost host;

        public static bool IsFrozen => host.IsFrozen;
        public static IReadOnlyList<string> Problems => host.Problems;
        /// <summary>Module ids in the order they were installed (the framework first). Registration order is the load order of the packages.</summary>
        public static IReadOnlyList<string> InstallOrder => host.InstallOrder;

        static FamilyFramework()
        {
            host = new ModuleHost(Catalog) { OnProblem = m => Log.Error("[Wil's Family Life] Module registration: " + m) };
            Offices = new OfficeCatalog(Catalog);
            Roster = new WorkRoster(Catalog);
            PlannedEvents = new PlannedEventCatalog(Catalog);
            Meetings = new MeetingCatalog(Catalog);
            Pages = new CommunityPageCatalog<Func<Rect, FamilyComponent, float>>(Catalog);
            BuildingGizmos = new ContributionList<Func<Building, IEnumerable<Gizmo>>>(Catalog);
            Diagnostics = new ContributionList<DiagnosticSection>(Catalog);
            OperationCompleted = new ContributionList<Action<Pawn, Pawn, int>>(Catalog);
            Tended = new ContributionList<Action<Pawn, Pawn>>(Catalog);
            Resets = new ContributionList<Action>(Catalog);
            Removal = new ContributionList<Func<FamilyComponent, CleanupReport>>(Catalog);
            DueNow = new ContributionList<Action<CommunityOffice>>(Catalog);
            Reconcilers = new ContributionList<Action<FamilyComponent>>(Catalog);
            Handoffs = new ContributionList<Action<FamilyComponent, CommunityOffice, int, int>>(Catalog);
            try
            {
                Catalog.Register(new ModuleInfo { Id = ModuleCatalog.FrameworkId, Name = "Wil's Community Framework", IsFramework = true, ApiVersion = ApiVersion });
                host.Note(ModuleCatalog.FrameworkId);
            }
            catch (RegistrationException ex) { host.Fail(ex.Message); }
            FamilyRegistry.HolderChanged = OnHolderChanged;
            OfficeAvailability.IsOffered = kind => Offices.Available(kind);
            Install(new FamilyLifeModule());          // Family Life's own posts are still hosted here until it is extracted (docs/MODULAR-ARCHITECTURE.md)
        }

        public sealed class DiagnosticSection { public OfficeKind Kind; public Func<FamilyComponent, int, List<string>> Lines; }

        // ------------------------------------------------------------------ initialization

        /// <summary>
        /// Installs one module. Refused (and logged, never thrown) when the framework is already frozen, the id is taken, or the module's own registration throws; a module that fails part
        /// way is switched off entirely so none of what it did register can run.
        /// </summary>
        public static bool Install(IFamilyModule module) => module != null ? host.Install(module.Id, module.Register) : host.Install(null, null);

        /// <summary>
        /// Ends registration and checks the whole set once, then writes the one authoritative line naming the modules that are really running. Called from the first game
        /// component's FinalizeInit, which is certainly after every mod constructor and after definition loading, so no module can arrive "late" however the game orders its
        /// start-up. Safe to call any number of times: only the first does anything (no repeated validation, log line or side effect).
        /// </summary>
        public static void FinalizeStartup()
        {
            if (host.Finalize(Offices.Validate())) Log.Message("[Wil's Family Life] " + host.ActiveLine() + (host.Problems.Count > 0 ? " (" + host.Problems.Count + " problem(s) logged above)" : "") + ".");
        }

        /// <summary>Whether start-up has been finalized (registration is closed).</summary>
        public static bool IsFinalized => host.IsFinalized;
        /// <summary>Modules refused, failed or switched off by validation, with the reason.</summary>
        public static IReadOnlyDictionary<string, string> Failures => host.Failures;

        /// <summary>Touching this at start-up runs the framework's own registration before the first save is loaded.</summary>
        public static void Ensure() { }

        // ------------------------------------------------------------------ module work

        /// <summary>
        /// Registers the work a module does for a post. The framework runs it from its existing maintenance step through the one shared roster, so a module adds no loop of its own, and
        /// it does not run while the module is off.
        /// </summary>
        public static void RegisterOfficeWork(string moduleId, OfficeKind kind, OfficeWork work)
        {
            officeWork[kind] = work;
            Roster.Register(new WorkItem
            {
                Id = moduleId + ".office." + kind.ToString().ToLowerInvariant(),
                ModuleId = moduleId,
                Interval = 0,          // the same cadence the work had before: each maintenance pass
                Run = now =>
                {
                    var c = FamilyComponent.Current; if (c == null) return;
                    foreach (var office in c.Registry.Offices.Where(o => o.Held && o.Kind == kind).ToList())
                        OfficeDriver.Work(c, c.Registry, office, now);
                }
            });
        }

        /// <summary>How fit a pawn is for a post (0 to 1), given by the module that owns the post. Kept beside the post definition so the framework never switches on a post kind.</summary>
        private static readonly Dictionary<OfficeKind, Func<Pawn, float>> fitness = new Dictionary<OfficeKind, Func<Pawn, float>>();
        public static void RegisterFitness(OfficeKind kind, Func<Pawn, float> fit) { fitness[kind] = fit; }

        internal struct PostSkillInfo { public string[] Passion; public Func<Pawn, float> Fitness; }
        internal static PostSkillInfo PostSkills(OfficeKind kind)
        {
            var def = Offices.ByKind(kind);
            return new PostSkillInfo { Passion = def?.PassionSkills ?? new string[0], Fitness = fitness.TryGetValue(kind, out var f) ? f : null };
        }

        internal static OfficeWork WorkFor(OfficeKind kind) => officeWork.TryGetValue(kind, out var w) && Offices.Available(kind) ? w : null;

        /// <summary>Clears every enabled module's in-memory state (sessions, queues, caches). Nothing here touches a save.</summary>
        internal static void ResetAll() { foreach (var reset in Resets.Enabled()) { try { reset(); } catch (Exception ex) { Log.Warning("[Wil's Family Life] A module reset failed: " + ex.Message); } } }

        // ------------------------------------------------------------------ a post changes hands

        private static void OnHolderChanged(FamilyRegistry registry, CommunityOffice office, int oldHolder, int newHolder)
        {
            var c = FamilyComponent.Current;
            if (c == null || c.Registry != registry) return;
            var def = Offices.ByKind(office.Kind);
            if (def == null || !Catalog.IsEnabled(def.ModuleId)) return;
            foreach (var handoff in Handoffs.ForModule(def.ModuleId))
            {
                try { handoff(c, office, oldHolder, newHolder); }
                catch (Exception ex) { Log.Warning("[Wil's Family Life] A hand-over for '" + def.ModuleId + "' failed: " + ex.Message); }
            }
        }

        // ------------------------------------------------------------------ lifecycle (saved with the family records)

        /// <summary>
        /// Brings every installed module's on/off state in line with what this save recorded: a module that was prepared for removal (or whose preparation failed) stays stopped until
        /// someone reactivates it, whatever the session. Then each running module checks its saved state against the world once. Called when a game starts or loads.
        /// </summary>
        internal static void ApplyLifecycle(FamilyComponent c)
        {
            var r = c?.Registry; if (r == null) return;
            foreach (var m in Catalog.Modules.Where(x => !x.IsFramework).ToList()) Catalog.SetEnabled(m.Id, r.Lifecycle.MayRun(m.Id));
            foreach (var m in Catalog.Modules.Where(x => !x.IsFramework && Catalog.IsEnabled(x.Id)).ToList())
            {
                foreach (var reconcile in Reconcilers.ForModule(m.Id))
                {
                    try { reconcile(c); }
                    catch (Exception ex) { Log.Warning("[Wil's Family Life] '" + m.Id + "' could not check its saved state: " + ex.Message); }
                }
            }
            foreach (var m in AbsentModules.Find(r, Catalog.IsRegistered))
                Log.Message("[Wil's Family Life] Module '" + m.ModuleId + "' is not installed; its saved records (" + m.State + (m.Posts.Count > 0 ? ", " + m.Posts.Count + " post(s)" : "") + (m.Payloads.Count > 0 ? ", " + m.Payloads.Count + " state payload(s)" : "") + ") are kept untouched" + (m.Prepared ? " (it was prepared for removal)." : "."));
            ModuleNotice.ShowIfNeeded(c);
        }

        /// <summary>The state of one module in this save: Active, a preparation state, or Absent when its code is not installed.</summary>
        public static string Describe(FamilyComponent c, string moduleId)
        {
            if (!Catalog.IsRegistered(moduleId)) return "Absent";
            if (Catalog.IsBroken(moduleId)) return "Refused";
            return (c?.Registry.Lifecycle.StateOf(moduleId) ?? ModuleState.Active).ToString();
        }

        /// <summary>
        /// Prepares one module for removal from a save. New work stops at once; the module's cleanup ends what is under way and releases everyone; and the result is recorded in the
        /// save: Prepared only if nothing failed, otherwise PreparationFailed with the report, so it can be run again. Safe to repeat. Needs the module installed, because only its own
        /// code knows what it owns: a module that is merely absent has not been prepared.
        /// </summary>
        public static CleanupReport PrepareForRemoval(string moduleId, FamilyComponent c)
        {
            var report = new CleanupReport(moduleId);
            var r = c?.Registry;
            if (r == null) { report.Failed.Add("no game is loaded"); return report; }
            if (!Catalog.IsRegistered(moduleId) || Catalog.IsBroken(moduleId)) { report.Failed.Add("this module is not installed (or was refused), so it cannot clean up after itself; install it, prepare it, then remove it"); return report; }
            if (Catalog.Modules.FirstOrDefault(m => m.Id == moduleId)?.IsFramework == true) { report.Failed.Add("the framework cannot be prepared for removal"); return report; }
            var cleanup = Removal.ForModule(moduleId).FirstOrDefault();
            if (cleanup == null) { report.Failed.Add("this module has no removal step registered"); return report; }
            int now = Activities.Now;
            bool wasPrepared = r.Lifecycle.StateOf(moduleId) == ModuleState.Prepared;
            r.Lifecycle.RequestPreparation(moduleId, now);
            Catalog.SetEnabled(moduleId, false);          // no new work, posts, events or buttons from this moment, whatever happens next
            try { report = cleanup(c); }
            catch (Exception ex) { report.Failed.Add("cleanup stopped: " + ex.Message); }
            r.Lifecycle.Finish(moduleId, report, now);
            if (wasPrepared && !report.Prepared) report.Failed.Add("it was prepared before, but this run did not finish");
            return report;
        }

        /// <summary>Starts a stopped module again. Only an explicit request does this: loading, restarting or reinstalling never does.</summary>
        public static bool Reactivate(string moduleId, FamilyComponent c)
        {
            var r = c?.Registry; if (r == null || !Catalog.IsRegistered(moduleId) || Catalog.IsBroken(moduleId)) return false;
            if (!r.Lifecycle.Reactivate(moduleId, Activities.Now)) return false;
            Catalog.SetEnabled(moduleId, true);
            foreach (var reset in Resets.ForModule(moduleId)) { try { reset(); } catch (Exception ex) { Log.Warning("[Wil's Family Life] A module reset failed: " + ex.Message); } }
            return true;
        }
    }
}
