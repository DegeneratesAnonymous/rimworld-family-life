using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>Adds the Family tab to every humanlike race (including modded ones) once all defs are loaded.</summary>
    internal static class FamilyTabs
    {
        public static void RegisterOnHumanlikes()
        {
            var tab = InspectTabManager.GetSharedInstance(typeof(ITab_Pawn_Family));
            int added = 0;
            foreach (var def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def.race == null || !def.race.Humanlike) continue;
                if (def.inspectorTabs == null) def.inspectorTabs = new List<Type>();
                if (def.inspectorTabsResolved == null) def.inspectorTabsResolved = new List<InspectTabBase>();
                if (!def.inspectorTabs.Contains(typeof(ITab_Pawn_Family))) def.inspectorTabs.Add(typeof(ITab_Pawn_Family));
                if (!def.inspectorTabsResolved.Contains(tab)) def.inspectorTabsResolved.Add(tab);
                added++;
            }
            Log.Message("[Wil's Family Life] Family tab added to " + added + " humanlike race(s).");
        }
    }

    internal static class FamilyUi
    {
        public static string NameOf(FamilyRegistry registry, int personId)
        {
            var p = registry.GetPerson(personId);
            return p == null ? "WFL_Someone".Translate().ToString() : p.FullName;
        }

        public static string LineageName(FamilyRegistry registry, int lineageId)
        {
            var lineage = registry.GetLineage(lineageId);
            return lineage == null ? "WFL_Someone".Translate().ToString() : registry.LineageDisplayName(lineage);
        }

        public static string TraditionName(int index)
        {
            return index >= 0 && index < TraditionCatalog.All.Length ? ("WFL_Trad_" + TraditionCatalog.All[index].Id).Translate().ToString() : "";
        }

        public static string TraditionNameById(string id) => ("WFL_Trad_" + id).Translate().ToString();

        public static string DimName(Dim d) => ("WFL_Dim_" + d).Translate().ToString();

        /// <summary>The cause of a change in a bond, as a short phrase that finishes a sentence.</summary>
        public static string WhyText(BondCause cause, bool positive)
        {
            return ("WFL_Why_" + cause).Translate().ToString();
        }

        public static string ReasonsLine(Bond bond)
        {
            if (bond.Reasons == null || bond.Reasons.Count == 0) return "";
            var parts = bond.Reasons.Where(r => System.Math.Abs(r.Delta) >= InsightTuning.ReasonsMinSize)
                .OrderByDescending(r => System.Math.Abs(r.Delta)).Take(3)
                .Select(r => WhyText(r.Cause, r.Delta > 0f) + (r.Delta > 0f ? " (+" : " (") + System.Math.Round(r.Delta) + ")").ToList();
            return parts.Count == 0 ? "" : "WFL_Lately".Translate(string.Join(", ", parts)).ToString();
        }

        /// <summary>First names only, for the short line under a person in the tree.</summary>
        public static string ShortNames(FamilyRegistry registry, IEnumerable<int> ids)
        {
            var names = ids.Select(id => registry.GetPerson(id)?.Name ?? "WFL_Someone".Translate().ToString()).Take(3).ToList();
            return string.Join(", ", names);
        }

        public static string NameList(FamilyRegistry registry, IEnumerable<int> ids)
        {
            var names = ids.Select(id => NameOf(registry, id)).ToList();
            return names.Count == 0 ? "" : string.Join(", ", names);
        }

        /// <summary>A date for a game tick, falling back to a day count if the world is not available.</summary>
        public static string Date(int tick)
        {
            try
            {
                long abs = Find.TickManager.gameStartAbsTick + tick;
                var map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
                Vector2 location = map != null ? Find.WorldGrid.LongLatOf(map.Tile) : Vector2.zero;
                return GenDate.DateFullStringAt(abs, location);
            }
            catch (Exception)
            {
                return "WFL_DayN".Translate(tick / 60000 + 1).ToString();
            }
        }

        public static string StatusText(PersonRecord p)
        {
            switch (p.Presence)
            {
                case Presence.Colony: return "WFL_StatusColony".Translate().ToString();
                case Presence.Away: return "WFL_StatusAway".Translate().ToString();
                default: return p.DeathTick >= 0 ? "WFL_StatusDiedOn".Translate(Date(p.DeathTick)).ToString() : "WFL_StatusDead".Translate().ToString();
            }
        }

        public static void Jump(int personId)
        {
            var pawn = FamilyComponent.Current?.TryGetPawn(personId);
            if (pawn == null) return;
            if (pawn.Spawned) CameraJumper.TryJumpAndSelect(pawn);
            else CameraJumper.TryJump(pawn);
        }

        public static void OpenFamilies(int lineageId)
        {
            Window_Families.PendingLineageId = lineageId;
            var open = Find.WindowStack.WindowOfType<Window_Families>();
            if (open == null) Find.WindowStack.Add(new Window_Families());      // already open: it picks up the new selection on its next frame
        }

        /// <summary>The chronicle line for an event, built from keyed text at display time.</summary>
        public static string Describe(FamilyRegistry registry, FamilyEvent e)
        {
            string a = NameOf(registry, e.A);
            string b = NameOf(registry, e.B);
            switch (e.Kind)
            {
                case FamilyEventKind.RecordsBegan: return "WFL_Chron_RecordsBegan".Translate().ToString();
                case FamilyEventKind.Birth: return "WFL_Chron_Birth".Translate(a, b).ToString();
                case FamilyEventKind.JoinedColony: return "WFL_Chron_Joined".Translate(a).ToString();
                case FamilyEventKind.LeftColony: return "WFL_Chron_Left".Translate(a).ToString();
                case FamilyEventKind.Died: return "WFL_Chron_Died".Translate(a).ToString();
                case FamilyEventKind.SocialMembership:
                    return "WFL_House_Changed".Translate(a,e.B>0?registry.LineageDisplayName(registry.GetLineage(e.B)):"WFL_House_None".Translate().ToString()).ToString();
                case FamilyEventKind.SocialBranch:
                    return "WFL_House_Founded".Translate(a,registry.LineageDisplayName(registry.GetLineage(e.B))).ToString();
                case FamilyEventKind.LineageFounded:
                    return "WFL_Chron_LineageFounded".Translate(a, registry.LineageDisplayName(registry.GetLineage(e.B))).ToString();
                case FamilyEventKind.LineageAffiliated:
                    return "WFL_Chron_Affiliated".Translate(a, registry.LineageDisplayName(registry.GetLineage(e.B))).ToString();
                case FamilyEventKind.PartnershipFormed: return "WFL_Chron_PartnersFormed".Translate(a, b).ToString();
                case FamilyEventKind.PartnershipEnded: return "WFL_Chron_PartnersEnded".Translate(a, b).ToString();
                case FamilyEventKind.HouseholdFormed: return "WFL_Chron_HouseholdFormed".Translate(a).ToString();
                case FamilyEventKind.CameOfAge: return "WFL_Chron_CameOfAge".Translate(a).ToString();
                case FamilyEventKind.Reunion: return "WFL_Chron_Reunion".Translate(a).ToString();
                case FamilyEventKind.Anniversary: return "WFL_Chron_Anniversary".Translate(a, b).ToString();
                case FamilyEventKind.TraditionAdopted: return "WFL_Chron_TraditionAdopted".Translate(LineageName(registry, e.A), TraditionName(e.B)).ToString();
                case FamilyEventKind.TraditionAbandoned: return "WFL_Chron_TraditionAbandoned".Translate(LineageName(registry, e.A), TraditionName(e.B)).ToString();
                case FamilyEventKind.TraditionHeld: return "WFL_Chron_TraditionHeld".Translate(LineageName(registry, e.A), TraditionName(e.B)).ToString();
                case FamilyEventKind.RiftOpened: return "WFL_Chron_RiftOpened".Translate(a, b).ToString();
                case FamilyEventKind.RiftResolved: return "WFL_Chron_RiftResolved".Translate(a, b).ToString();
                case FamilyEventKind.FamiliesJoined:
                    return "WFL_Chron_FamiliesJoined".Translate(a, b, LineageName(registry, registry.GetPerson(e.A)?.PrimaryLineageId ?? 0),
                        LineageName(registry, registry.GetPerson(e.B)?.PrimaryLineageId ?? 0)).ToString();
                case FamilyEventKind.StepFamilyFormed:
                    {
                        var stepKids = registry.StepChildrenOf(e.A).Concat(registry.StepChildrenOf(e.B)).Distinct().ToList();
                        string who = stepKids.Count == 0 ? "" : " (" + "WFL_StepChildren".Translate(ShortNames(registry, stepKids)) + ")";
                        return "WFL_Chron_StepFamily".Translate(a, b, who).ToString();
                    }
                default: return e.Kind.ToString();
            }
        }
    }
}
