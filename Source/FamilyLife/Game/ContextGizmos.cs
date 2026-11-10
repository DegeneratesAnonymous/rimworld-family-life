using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    // The one patch that lets modules add buttons to a building. It asks the registered contributions (skipped for a disabled module); it knows nothing of lecterns or beds. Owner: the framework.

    [HarmonyLib.HarmonyPatch(typeof(Building), nameof(Building.GetGizmos))]
    internal static class Patch_BuildingContributions
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Building __instance)
        {
            foreach (var g in __result) yield return g;
            List<Gizmo> more = null;
            try { more = OfficeEvents.Gizmos(__instance).ToList(); } catch (Exception e) { Log.Warning("[Wil's Family Life] Module buttons skipped: " + e.Message); }
            if (more != null) foreach (var g in more) yield return g;
        }
    }
}
