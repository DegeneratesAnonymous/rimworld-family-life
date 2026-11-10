using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The framework's registry of modules and what they contribute. Everything is registered once, when this type is first used (the mod constructor touches it at start-up), and
    /// checked once; nothing is looked up by reflection afterwards. A module that is absent or switched off simply has no entry: no post to give out, no work to run, no event to
    /// run, no button to show. Owner: the framework.
    /// </summary>
    internal static class FamilyModules
    {
        /// <summary>The work a module does for the holder of one of its posts. <paramref name="holder"/> is null when they are not on a map.</summary>
        internal delegate void OfficeWork(FamilyComponent c, FamilyRegistry r, CommunityOffice office, Pawn holder, FamilySettings settings, int now);

        internal static readonly ModuleCatalog Catalog = new ModuleCatalog();
        internal static readonly OfficeCatalog Offices;
        internal static readonly WorkRoster Roster;
        internal static readonly PlannedEventCatalog PlannedEvents;
        internal static readonly ContributionList<Func<Building, IEnumerable<Gizmo>>> BuildingGizmos;
        internal static readonly ContributionList<DiagnosticSection> Diagnostics;
        internal static readonly ContributionList<Action<CommunityOffice>> DueNow;          // developer menu: brings a module's timers on a post due
        internal static readonly ContributionList<Func<FamilyComponent, CleanupReport>> Removal;          // "prepare for removal": one per module
        internal static readonly ContributionList<Action> Resets;          // clears a module's in-memory state when a game is loaded or ended
        private static readonly Dictionary<OfficeKind, OfficeWork> officeWork = new Dictionary<OfficeKind, OfficeWork>();
        internal static List<string> Problems { get; private set; } = new List<string>();

        static FamilyModules()
        {
            Offices = new OfficeCatalog(Catalog);
            Roster = new WorkRoster(Catalog);
            PlannedEvents = new PlannedEventCatalog(Catalog);
            BuildingGizmos = new ContributionList<Func<Building, IEnumerable<Gizmo>>>(Catalog);
            Diagnostics = new ContributionList<DiagnosticSection>(Catalog);
            Resets = new ContributionList<Action>(Catalog);
            Removal = new ContributionList<Func<FamilyComponent, CleanupReport>>(Catalog);
            DueNow = new ContributionList<Action<CommunityOffice>>(Catalog);
            try
            {
                Catalog.Register(new ModuleInfo { Id = ModuleCatalog.FrameworkId, Name = "Wil's Community Framework", IsFramework = true });
                // the modules, in a fixed order; each registers only what it owns
                FamilyLifeModule.Register();
                EducationModule.Register();
                CareModule.Register();
                var problems = Catalog.Validate(); problems.AddRange(Offices.Validate());
                Problems = problems;
                foreach (var p in problems) Log.Error("[Wil's Family Life] Module registration: " + p);
                OfficeAvailability.IsOffered = kind => Offices.Available(kind);
            }
            catch (RegistrationException ex)
            {
                Problems.Add(ex.Message);
                Log.Error("[Wil's Family Life] Module registration failed: " + ex.Message);
            }
        }

        internal sealed class DiagnosticSection { public OfficeKind Kind; public Func<FamilyComponent, int, List<string>> Lines; }

        /// <summary>
        /// Prepares one module for removal from a save: runs its cleanup, and only if every step succeeded stops it for the rest of the session. A module that is prepared and then allowed to
        /// run again is no longer prepared; the report says so.
        /// </summary>
        internal static CleanupReport PrepareForRemoval(string moduleId, FamilyComponent c)
        {
            var report = new CleanupReport(moduleId);
            var cleanup = Removal.EnabledFor(moduleId);
            if (cleanup == null) { report.Failed.Add("this module has no removal step registered"); return report; }
            try { report = cleanup(c); }
            catch (Exception ex) { report.Failed.Add("cleanup stopped: " + ex.Message); return report; }
            if (report.Prepared) Catalog.SetEnabled(moduleId, false);          // no new work, posts, events or buttons from it until the game is restarted
            return report;
        }

        /// <summary>Clears every enabled module's in-memory state (sessions, queues, caches). Nothing here touches a save.</summary>
        internal static void ResetAll() { foreach (var reset in Resets.Enabled()) { try { reset(); } catch (Exception ex) { Log.Warning("[Wil's Family Life] A module reset failed: " + ex.Message); } } }

        /// <summary>Touching this at start-up runs the registration (and logs any problem) before the first save is loaded.</summary>
        internal static void Ensure() { }

        /// <summary>
        /// Registers the work a module does for a post. The framework runs it from its existing maintenance step through the one shared roster, so a module adds no loop of its own,
        /// and it does not run while the module is off.
        /// </summary>
        internal static void RegisterOfficeWork(string moduleId, OfficeKind kind, OfficeWork work)
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

        internal static OfficeWork WorkFor(OfficeKind kind) => officeWork.TryGetValue(kind, out var w) && Offices.Available(kind) ? w : null;
    }
}
