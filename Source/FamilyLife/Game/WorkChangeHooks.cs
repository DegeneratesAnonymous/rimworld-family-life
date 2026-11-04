using HarmonyLib;
using RimWorld;
using Verse;

namespace FamilyLife
{
    internal static class WorkChangeNotifications
    {
        public static void Changed(Thing thing)
        {
            // All spawned things use these vanilla methods. Exit cheaply for unrelated things and maps without cached family work.
            if (!(thing is Building) && !(thing is Blueprint) && !(thing is Frame)) return;
            try { GuestPasses.WorkChanged(thing.Map, thing.Position); }
            catch (System.Exception ex) { HookSafety.Report("family work invalidation", ex); }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.SpawnSetup))]
    internal static class Hook_FamilyWorkSpawn
    {
        private static void Postfix(Thing __instance) => WorkChangeNotifications.Changed(__instance);
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.DeSpawn))]
    internal static class Hook_FamilyWorkDespawn
    {
        // Read the map and cell before vanilla removes the thing.
        private static void Prefix(Thing __instance) => WorkChangeNotifications.Changed(__instance);
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class Hook_FamilyWorkDamage
    {
        private static void Postfix(Thing __instance) => WorkChangeNotifications.Changed(__instance);
    }

    [HarmonyPatch(typeof(RoofGrid), nameof(RoofGrid.SetRoof))]
    internal static class Hook_FamilyWorkRoof
    {
        private static void Postfix(Map ___map, IntVec3 c)
        {
            try { GuestPasses.WorkChanged(___map, c); }
            catch (System.Exception ex) { HookSafety.Report("family roof work invalidation", ex); }
        }
    }
}
