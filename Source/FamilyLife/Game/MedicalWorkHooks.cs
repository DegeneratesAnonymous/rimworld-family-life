using System;
using RimWorld;
using Verse;

namespace FamilyLife
{
    // Two read-only hooks on vanilla medical work, owned by the framework (modules add listeners through the contribution lists, never a patch of their own). Each is a postfix: it never changes
    // the operation, its success chance, the tend or its quality, and it calls only listeners of modules that are running. Owner: the framework.

    [HarmonyLib.HarmonyPatch(typeof(Bill_Medical), nameof(Bill_Medical.Notify_IterationCompleted))]
    internal static class Patch_OperationCompleted
    {
        private static void Postfix(Bill_Medical __instance, Pawn billDoer)
        {
            try
            {
                if (__instance?.recipe == null || !__instance.recipe.IsSurgery) return;
                int id = __instance.GetUniqueLoadID().GetHashCode();
                foreach (var listener in FamilyFramework.OperationCompleted.Enabled()) listener(billDoer, __instance.GiverPawn, id);
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] An operation listener was skipped: " + ex.Message); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
    internal static class Patch_TendDone
    {
        private static void Postfix(Pawn doctor, Pawn patient)
        {
            try { foreach (var listener in FamilyFramework.Tended.Enabled()) listener(doctor, patient); }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] A tend listener was skipped: " + ex.Message); }
        }
    }
}
