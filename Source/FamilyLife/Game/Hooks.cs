using HarmonyLib;
using RimWorld;
using Verse;

namespace FamilyLife
{
    // These hooks only say "something changed". The reconcile in FamilyComponent works out what, so a missed,
    // duplicated or bypassed hook (another mod editing relations directly) cannot corrupt the family records.

    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.AddDirectRelation))]
    internal static class Hook_AddDirectRelation
    {
        private static void Postfix() => FamilyComponent.NotifyChanged();
    }

    [HarmonyPatch(typeof(Pawn_RelationsTracker), nameof(Pawn_RelationsTracker.TryRemoveDirectRelation))]
    internal static class Hook_TryRemoveDirectRelation
    {
        private static void Postfix() => FamilyComponent.NotifyChanged();
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill), new[] { typeof(DamageInfo?), typeof(Hediff) })]
    internal static class Hook_PawnKill
    {
        // Every raider and animal that dies passes through here, so only the death of someone the family records know about is reported.
        private static void Postfix(Pawn __instance)
        {
            var component = FamilyComponent.Current;
            if (component == null || __instance == null || __instance.RaceProps == null || !__instance.RaceProps.Humanlike) return;
            if (component.Registry.GetPerson(__instance.thingIDNumber) == null) return;
            component.MarkDirty();
        }
    }
}
