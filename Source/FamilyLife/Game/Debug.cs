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

        [DebugAction("Wil's Family Life", "Explain relationship events for two selected pawns", allowedGameStates = AllowedGameStates.Playing)]
        private static void ExplainEvents()
        {
            var component = FamilyComponent.Current;
            var selected = Find.Selector.SelectedPawns;
            if (component == null || selected.Count < 2) { Log.Message("[Wil's Family Life] Select two colonists first."); return; }
            var sb = new StringBuilder();
            sb.AppendLine("Relationship events for " + selected[0].LabelShort + " and " + selected[1].LabelShort + " (children " + (ModsConfig.BiotechActive ? "supported" : "not supported") + ")");
            for (int order = 0; order < 2; order++)
            {
                var a = selected[order];
                var b = selected[1 - order];
                var ctx = SocialContext.Build(component, a, b, ActivityKind.None, false);
                if (ctx == null) { sb.AppendLine(a.LabelShort + " -> " + b.LabelShort + ": not in the family records"); continue; }
                sb.AppendLine("== " + a.LabelShort + " reaching toward " + b.LabelShort + ": " + FamilyRegistry.ScenarioOf(ctx) + ", " + (ctx.SameFamily || ctx.Related ? "family" : (ctx.DifferentFamilies ? "different families" : "outsiders"))
                    + ", attachment " + ctx.AttachI.ToString("0") + "/" + ctx.AttachR.ToString("0") + ", trust " + ctx.TrustI.ToString("0") + "/" + ctx.TrustR.ToString("0")
                    + ", tension " + ctx.TensionI.ToString("0") + "/" + ctx.TensionR.ToString("0") + ", opinion " + ctx.OpinionOfRecipient + "/" + ctx.OpinionOfInitiator
                    + (ctx.Rift != null ? ", open rift" : "") + (ctx.FamilyTie != null ? ", families " + ctx.FamilyTie.State : ""));
                var lines = component.Registry.Explain(ctx, EventCatalog.All, ModsConfig.BiotechActive, SocialMoments.EventAllowed);
                foreach (var l in lines.OrderByDescending(x => x.Eligible).ThenBy(x => x.Def.Id))
                    sb.AppendLine("  " + l.Def.Id + " " + l.Def.Key + ": " + (l.Eligible ? "POSSIBLE (motive " + l.Motive.ToString("0.00") + ")" : l.Reason));
            }
            Log.Message(sb.ToString());
        }

        [DebugAction("Wil's Family Life", "Why can the selected pawn not use a bed?", allowedGameStates = AllowedGameStates.Playing)]
        private static void ExplainBeds()
        {
            var pawn = Find.Selector.SelectedPawns.FirstOrDefault();
            if (pawn == null) { Log.Message("[Wil's Family Life] Select a colonist first."); return; }
            var area = pawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
            var sb = new StringBuilder();
            sb.AppendLine(pawn.LabelShort + ": area restriction " + (area == null ? "none" : area.Label) + ", owns " + (pawn.ownership?.OwnedBed?.LabelCap.ToString() ?? "no bed"));
            foreach (var bed in pawn.Map.listerBuildings.AllBuildingsColonistOfClass<RimWorld.Building_Bed>())
            {
                string why = !RimWorld.ForbidUtility.InAllowedArea(bed.Position, pawn) ? "OUTSIDE the pawn's area at " + bed.Position
                    : (RimWorld.ForbidUtility.IsForbidden(bed, pawn) ? "forbidden" : (!RimWorld.RestUtility.CanUseBedEver(pawn, bed.def) ? "not a bed this pawn can ever use" : (!pawn.CanReach(bed, Verse.AI.PathEndMode.OnCell, Danger.Some) ? "UNREACHABLE (a cell on the way is outside the area?)" : "usable")));
                var owners = bed.OwnersForReading.Count == 0 ? "unowned" : string.Join(", ", bed.OwnersForReading.Select(o => o.LabelShort));
                sb.AppendLine("  " + bed.LabelCap + " at " + bed.Position + " (" + owners + "): " + why);
            }
            Log.Message(sb.ToString());
        }

        [DebugAction("Wil's Family Life", "Why is the selected pawn staying in their family zone?", allowedGameStates = AllowedGameStates.Playing)]
        private static void ExplainZoneWork()
        {
            var component = FamilyComponent.Current;
            var pawn = Find.Selector.SelectedPawns.FirstOrDefault();
            if (component == null || pawn == null) { Log.Message("[Wil's Family Life] Select a colonist first."); return; }
            var registry = component.Registry;
            var record = registry.GetPerson(pawn.thingIDNumber);
            var lineage = record == null ? null : registry.GetLineage(registry.LineageIdFor(record));
            var sb = new StringBuilder();
            sb.AppendLine(pawn.LabelShort + ": family " + (lineage == null ? "none" : registry.LineageDisplayName(lineage)) + ", zone area id " + (lineage?.ZoneAreaId ?? 0));
            var held = pawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
            sb.AppendLine("  restriction now: " + (held == null ? "none" : held.Label + " (id " + held.ID + ")"));
            sb.AppendLine("  schedule now: " + (pawn.timetable?.CurrentAssignment?.defName ?? "unknown") + "; current job: " + (pawn.CurJobDef?.defName ?? "none") + "; setting 'only outside work hours': " + (FamilyLifeMod.Settings == null || FamilyLifeMod.Settings.zonesAllowWork));
            sb.AppendLine("  in a door visit: " + GuestPasses.IsActive(pawn));
            var after = pawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
            sb.AppendLine("  after checking work time: " + (after == null ? "none" : after.Label));
            if (pawn.workSettings != null)
                sb.AppendLine("  active work: " + string.Join(", ", DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(w => !pawn.WorkTypeIsDisabled(w) && pawn.workSettings.GetPriority(w) > 0).Select(w => w.defName + "=" + pawn.workSettings.GetPriority(w))));
            Log.Message(sb.ToString());
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
