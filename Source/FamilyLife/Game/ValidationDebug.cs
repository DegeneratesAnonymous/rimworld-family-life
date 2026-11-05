using System.Linq;
using System.Text;
using FamilyLife.Domain;
using LudeonTK;
using Verse;

namespace FamilyLife
{
    internal static class FamilyValidationDebug
    {
        [DebugAction("Wil's Family Life", "Validate story state (read only)", allowedGameStates = AllowedGameStates.Playing)]
        private static void Validate()
        {
            var component = FamilyComponent.Current;
            if (component == null) return;
            var r = component.Registry;
            var report = new StringBuilder("[Wil's Family Life] Read-only state validation at tick " + Find.TickManager.TicksGame + "\n");
            int problems = 0;
            void Problem(string message) { problems++; report.AppendLine(message); }
            foreach (var home in r.LineagesWithZones())
            {
                var area = FamilyZones.AreaFor(home);
                if (home.ZoneMapId < 0) Problem("Legacy/unresolved map identity for family " + home.Id + "; explicitly reassign its home.");
                else if (area == null || area.Map.uniqueID != home.ZoneMapId) Problem("Missing or mismatched home for family " + home.Id);
            }
            foreach (var group in r.Claims.GroupBy(c => c.ThingId).Where(g => g.Count() > 1)) Problem("Duplicate ownership claim for thing " + group.Key);
            foreach (var claim in r.Claims)
                if (r.GetLineage(claim.OwnerFamily) == null || r.GetLineage(claim.HolderFamily) == null) Problem("Invalid owner/holder for thing " + claim.ThingId);
            foreach (var plot in r.Plots.Where(p => p.Active))
                if (!r.Incidents.Any(i => i.Id == plot.IncidentId && !i.Resolved)) Problem("Active plot " + plot.Id + " has no unresolved cause; maintenance may be pending.");
            foreach (var a in r.Agency)
                if (a.Progress < 0 || a.Progress > 1 || float.IsNaN(a.Progress) || r.GetPerson(a.PersonId) == null) Problem("Invalid agency record for " + a.PersonId);
            report.AppendLine(problems + " potential issue(s). Homes=" + r.LineagesWithZones().Count + ", claims=" + r.Claims.Count + ", plots=" + r.Plots.Count + ", aims=" + r.Agency.Count);
            report.AppendLine("This snapshot does not test pathing, completed handovers, save/reload continuity, or TPS. Run before/after the live acceptance scenarios; allow a maintenance interval for transient states.");
            Log.Message(report.ToString());
        }
    }
}
