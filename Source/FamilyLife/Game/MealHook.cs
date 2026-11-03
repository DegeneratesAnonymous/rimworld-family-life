using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// Notes when someone finishes eating so relatives who ate in the same room at about the same time count as dining together.
    /// It changes nothing about how or where anyone eats.
    /// </summary>
    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.FinalizeIngest))]
    internal static class Hook_FinalizeIngest
    {
        private static void Postfix(Pawn ingester, Toil __result)
        {
            if (__result == null || ingester == null) return;
            __result.AddFinishAction(() => Activities.OnAte(ingester));
        }
    }
}
