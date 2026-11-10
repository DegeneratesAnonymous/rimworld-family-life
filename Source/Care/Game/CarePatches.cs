using RimWorld;
using Verse;

namespace FamilyLife
{
    // Two read-only hooks on vanilla medical work, so a specialist's experience comes from real operations and real tending. Each is a postfix: it never changes the operation, its success
    // chance, the tend or its quality. Both do nothing while Community Care is off or prepared for removal. Owner: Community Care.

    [HarmonyLib.HarmonyPatch(typeof(Bill_Medical), nameof(Bill_Medical.Notify_IterationCompleted))]
    internal static class Patch_OperationCompleted
    {
        private static void Postfix(Bill_Medical __instance, Pawn billDoer)
        {
            try { if (__instance?.recipe != null && __instance.recipe.IsSurgery) CareSpecialistWork.OnSurgery(billDoer, __instance.GiverPawn, __instance.GetUniqueLoadID().GetHashCode()); }
            catch (System.Exception ex) { Log.Warning("[Wil's Family Life] Operation experience skipped: " + ex.Message); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
    internal static class Patch_TendDone
    {
        private static void Postfix(Pawn doctor, Pawn patient)
        {
            try { CareSpecialistWork.OnTend(doctor, patient); }
            catch (System.Exception ex) { Log.Warning("[Wil's Family Life] Tend experience skipped: " + ex.Message); }
        }
    }
}
