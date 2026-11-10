using System.Linq;
using LudeonTK;
using Verse;

namespace FamilyLife
{
    /// <summary>Developer-menu shortcuts for the module framework. Owner: the framework.</summary>
    internal static class FrameworkTools
    {
        private const string Cat = "Wil's Family Life, testing";

        [DebugAction(Cat, "Show module status (installed, running, prepared)", allowedGameStates = AllowedGameStates.Playing)]
        private static void ModuleStatus()
        {
            var c = FamilyComponent.Current;
            var lines = FamilyFramework.Catalog.Modules.Select(m => "  " + m.Id + ": " + FamilyFramework.Describe(c, m.Id) + (FamilyFramework.Catalog.IsEnabled(m.Id) ? " (running)" : " (not running)")).ToList();
            var saved = c?.Registry.Lifecycle.Records.Where(r => !FamilyFramework.Catalog.IsRegistered(r.ModuleId)).Select(r => "  " + r.ModuleId + ": Absent (saved state " + r.State + " is kept)");
            Log.Message("[Wil's Family Life] Modules (install order: " + string.Join(", ", FamilyFramework.InstallOrder) + "):\n" + string.Join("\n", lines.Concat(saved ?? Enumerable.Empty<string>())) +
                (FamilyFramework.Problems.Count > 0 ? "\nProblems:\n  " + string.Join("\n  ", FamilyFramework.Problems) : ""));
        }
    }
}
