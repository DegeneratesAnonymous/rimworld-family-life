using HarmonyLib;
using FamilyLife.Domain;
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
        private static void Prefix(Pawn __instance, DamageInfo? dinfo, out FatalIncident __state)
        {
            __state = null;
            try
            {
                var c = FamilyComponent.Current;
                if (c == null || !FamilyLifeMod.Settings.familyStories || c.Registry.GetPerson(__instance.thingIDNumber) == null) return;
                var actor = dinfo?.Instigator as Pawn;
                __state = new FatalIncident { Actor = actor?.thingIDNumber ?? 0, Victim = __instance.thingIDNumber, Map = __instance.Map?.uniqueID ?? -1,
                    Context = StoryActions.Context(actor, __instance, dinfo?.IntendedTarget), Witnesses = StoryActions.Witnesses(__instance.Map, actor, __instance.Position) };
            }
            catch (System.Exception ex) { HookSafety.Report("death context", ex); }
        }
        // Every raider and animal that dies passes through here, so only the death of someone the family records know about is reported.
        private static void Postfix(Pawn __instance, FatalIncident __state)
        {
            var component = FamilyComponent.Current;
            if (component == null || __instance == null || __instance.RaceProps == null || !__instance.RaceProps.Humanlike) return;
            if (component.Registry.GetPerson(__instance.thingIDNumber) == null) return;
            component.MarkDirty();
            if (__instance.Dead && __state != null)
                try { component.Registry.RecordIncident(IncidentKind.Death, __state.Actor, __state.Victim, __state.Map, Activities.Now, 100, __state.Witnesses, __state.Context); }
                catch (System.Exception ex) { HookSafety.Report("family death", ex); }
        }
    }
}
