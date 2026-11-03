using System;
using System.IO;
using System.Linq;
using System.Text;
using FamilyLife.Domain;
using LudeonTK;
using Verse;

namespace FamilyLife
{
    internal static class FamilyDebug
    {
        /// <summary>
        /// Writes everything the mod knows about the family records (who is related to whom, as recorded, and how the tree was laid out)
        /// to a text file, so a tree that looks wrong can be checked against the records it was built from.
        /// </summary>
        public static string Export(FamilyRegistry registry, int lineageId = 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Wil's Family Life records, tick " + Find.TickManager.TicksGame);
            string Name(int id) => registry.GetPerson(id)?.FullName ?? ("#" + id);
            string Names(System.Collections.Generic.IEnumerable<int> ids) => string.Join(", ", ids.Select(i => Name(i) + " (#" + i + ")"));
            foreach (var l in registry.Lineages.Where(x => lineageId == 0 || x.Id == lineageId))
            {
                sb.AppendLine();
                sb.AppendLine("== Lineage " + l.Id + " " + registry.LineageDisplayName(l) + " (founder " + Name(l.FounderId) + ")");
                var people = registry.MembersOf(l.Id).Concat(registry.AffiliatesOf(l.Id)).Distinct().OrderBy(p => p.Id).ToList();
                foreach (var p in people)
                {
                    string how = p.PrimaryLineageId == l.Id ? "by descent" : "affiliate (" + l.Affiliations.FirstOrDefault(a => a.PersonId == p.Id)?.Kind + ")";
                    sb.AppendLine("  #" + p.Id + " " + p.FullName + " [" + p.Presence + ", " + how + ", household " + p.HouseholdId + ", primary lineage " + p.PrimaryLineageId + "]");
                    if (p.ParentIds.Count > 0) sb.AppendLine("      parents:  " + Names(p.ParentIds));
                    var kids = registry.ChildrenOf(p.Id);
                    if (kids.Count > 0) sb.AppendLine("      children: " + Names(kids));
                    if (p.SpouseIds.Count > 0) sb.AppendLine("      spouses:  " + Names(p.SpouseIds));
                    if (p.LoverIds.Count > 0) sb.AppendLine("      lovers:   " + Names(p.LoverIds));
                    if (p.ExIds.Count > 0) sb.AppendLine("      exes:     " + Names(p.ExIds));
                }
                var tree = registry.BuildTree(l.Id);
                sb.AppendLine("  -- tree: " + tree.Units.Count + " units, " + tree.Generations + " generations, " + tree.PeopleShown + " people");
                for (int i = 0; i < tree.Units.Count; i++)
                {
                    var u = tree.Units[i];
                    string parent = u.ParentUnit < 0 ? "root" : "hangs from unit " + u.ParentUnit + " at " + u.ParentAnchorLocal.ToString("0.0");
                    sb.AppendLine("     unit " + i + " gen " + u.Generation + " x " + u.X.ToString("0.0") + ": " + string.Join(" -" , u.Members.Select((m, k) => Name(m) + (k < u.Links.Count ? " [" + u.Links[k] + "]" : ""))) + "  (" + parent + ")");
                    foreach (var arc in u.Arcs) sb.AppendLine("         arc: " + Name(u.Members[arc.A]) + " <-" + arc.Kind + "-> " + Name(u.Members[arc.B]));
                }
                foreach (var link in tree.CoParentLinks) sb.AppendLine("     faint link: " + Name(link.ParentPerson) + " -> unit " + link.ChildUnit);
            }
            string path = Path.Combine(GenFilePaths.SaveDataFolderPath, "WFL_FamilyRecords.txt");
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        [DebugAction("Wil's Family Life", "Write family records to a file", allowedGameStates = AllowedGameStates.Playing)]
        private static void ExportAll()
        {
            var registry = FamilyComponent.Current?.Registry;
            if (registry == null) return;
            Log.Message("[Wil's Family Life] Wrote " + Export(registry));
        }

        [DebugAction("Wil's Family Life", "Resync family records now", allowedGameStates = AllowedGameStates.Playing)]
        private static void Resync()
        {
            FamilyComponent.Current?.Sync(Find.TickManager.TicksGame);
            Log.Message("[Wil's Family Life] Resynced.");
        }

        [DebugAction("Wil's Family Life", "Log family records", allowedGameStates = AllowedGameStates.Playing)]
        private static void Dump()
        {
            var registry = FamilyComponent.Current?.Registry;
            if (registry == null) return;
            Log.Message("[Wil's Family Life] " + registry.Persons.Count + " people, " + registry.Lineages.Count + " lineages, "
                + registry.Households.Count + " households, " + registry.Chronicle.Count + " chronicle entries.");
            foreach (var l in registry.Lineages)
                Log.Message("  Lineage " + l.Id + " " + registry.LineageDisplayName(l) + ": "
                    + registry.MembersOf(l.Id).Count + " members, " + l.Affiliations.Count + " affiliates");
            foreach (var h in registry.Households)
                Log.Message("  Household " + h.Id + ": " + string.Join(", ", h.ResidentIds.Select(id => registry.GetPerson(id)?.FullName ?? "?")));
        }
    }
}
