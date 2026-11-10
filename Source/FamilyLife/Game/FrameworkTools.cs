using System.Linq;
using LudeonTK;
using Verse;

namespace FamilyLife
{
    /// <summary>Developer-menu shortcuts for the module framework. Owner: the framework.</summary>
    internal static class FrameworkTools
    {
        private const string Cat = "Wil's Family Life, testing";

        [DebugAction(Cat, "Careers: show status (assignments, qualifications, hooks)", allowedGameStates = AllowedGameStates.Playing)]
        private static void CareersStatus()
        {
            var c = FamilyComponent.Current; if (c == null) return;
            Log.Message("[Wil's Family Life] Careers status:" + System.Environment.NewLine + string.Join(System.Environment.NewLine, CareerService.Status(c).Select(l => "  " + l)));
        }

        [DebugAction(Cat, "Show module status (installed, running, prepared)", allowedGameStates = AllowedGameStates.Playing)]
        private static void ModuleStatus()
        {
            var c = FamilyComponent.Current;
            var cat = FamilyFramework.Catalog;
            var lines = new System.Collections.Generic.List<string>
            {
                "Registration " + (FamilyFramework.IsFinalized ? "closed at the first FinalizeInit" : "still open") + ". Install order: " + string.Join(", ", FamilyFramework.InstallOrder)
            };
            foreach (var m in cat.Modules)
            {
                string state = FamilyFramework.Failures.TryGetValue(m.Id, out var why) || cat.IsBroken(m.Id) ? "FAILED" + (why != null ? " - " + why : "")
                    : (c?.Registry.Lifecycle.StateOf(m.Id) ?? FamilyLife.Framework.ModuleState.Active) == FamilyLife.Framework.ModuleState.Prepared ? "PREPARED"
                    : cat.IsEnabled(m.Id) ? "active" : "registered, not running (" + FamilyFramework.Describe(c, m.Id) + ")";
                lines.Add("  " + m.Id + ": " + state + " (api " + m.ApiVersion + ")");
            }
            foreach (var f in FamilyFramework.Failures.Where(f => !cat.IsRegistered(f.Key))) lines.Add("  " + f.Key + ": FAILED - " + f.Value);
            if (c != null)
                foreach (var a in FamilyLife.Framework.AbsentModules.Find(c.Registry, cat.IsRegistered))
                    lines.Add("  " + a.ModuleId + ": ABSENT, saved records kept (" + a.State + "; posts " + a.Posts.Count + ", payloads " + a.Payloads.Count + (c.Registry.Lifecycle.IsNoticeSuppressed(a.ModuleId) ? "; notice dismissed" : "") + ")");
            Log.Message("[Wil's Family Life] Module status:" + System.Environment.NewLine + string.Join(System.Environment.NewLine, lines) + (FamilyFramework.Problems.Count > 0 ? System.Environment.NewLine + "Validation problems:" + System.Environment.NewLine + "  " + string.Join(System.Environment.NewLine + "  ", FamilyFramework.Problems) : ""));
        }
    }
}
