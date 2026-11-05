using System;
using System.Collections.Generic;
using FamilyLife.Domain;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FamilyLife
{
    [HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    internal static class Hook_SupplySplit
    {
        private static void Postfix(Thing __instance, Thing __result)
        {
            try { if (__result != null) FamilyComponent.Current?.Registry.SplitStock(__instance.thingIDNumber, __result.thingIDNumber); }
            catch (Exception ex) { HookSafety.Report("supply split", ex); }
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.CanStackWith))]
    internal static class Hook_SupplyStack
    {
        private static void Postfix(Thing __instance, Thing other, ref bool __result)
        {
            try { if (__result && other != null && FamilyComponent.Current is FamilyComponent c) __result = c.Registry.SameOwnership(__instance.thingIDNumber, other.thingIDNumber); }
            catch (Exception ex) { HookSafety.Report("supply stack", ex); }
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
    internal static class Hook_SupplyDestroyed
    {
        private static void Postfix(Thing __instance)
        {
            try { if (__instance.Destroyed) FamilyComponent.Current?.Registry.ForgetStock(__instance.thingIDNumber); }
            catch (Exception ex) { HookSafety.Report("supply destruction", ex); }
        }
    }
    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.IsForbidden), new[] { typeof(Thing), typeof(Pawn) })]
    internal static class Hook_PrivateSupplies
    {
        private static void Postfix(Thing t, Pawn pawn, ref bool __result)
        {
            try { if (!__result && FamilyComponent.Current is FamilyComponent c) __result = !c.Economy.MayUse(pawn, t); }
            catch (Exception ex) { HookSafety.Report("private supplies", ex); }
        }
    }
    [HarmonyPatch(typeof(WorkGiver_HaulGeneral), nameof(WorkGiver_HaulGeneral.JobOnThing))]
    internal static class Hook_PrivateHauling
    {
        private static void Postfix(Thing t, bool forced, ref Verse.AI.Job __result)
        {
            try { if (!forced && __result != null && FamilyLifeMod.Settings.familySupplies && FamilyComponent.Current?.Registry.ClaimFor(t.thingIDNumber) != null) __result = null; }
            catch (Exception ex) { HookSafety.Report("private hauling", ex); }
        }
    }
    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    internal static class Hook_FamilyInjury
    {
        private static void Prefix(Thing __instance, DamageInfo dinfo, out IncidentContext __state)
        {
            __state = IncidentContext.None;
            try { if (FamilyLifeMod.Settings.familyStories && __instance is Pawn victim && FamilyComponent.Current?.Registry.GetPerson(victim.thingIDNumber) != null) __state = StoryActions.Context(dinfo.Instigator as Pawn, victim, dinfo.IntendedTarget); }
            catch (Exception ex) { HookSafety.Report("injury context", ex); }
        }
        private static void Postfix(Thing __instance, DamageInfo dinfo, DamageWorker.DamageResult __result, IncidentContext __state)
        {
            try
            {
                var c = FamilyComponent.Current;
                if (!FamilyLifeMod.Settings.familyStories || c == null || !(__instance is Pawn victim) || victim.Dead || c.Registry.GetPerson(victim.thingIDNumber) == null || __result == null || __result.totalDamageDealt < 8) return;
                var actor = dinfo.Instigator as Pawn;
                c.Registry.RecordIncident(IncidentKind.Injury, actor?.thingIDNumber ?? 0, victim.thingIDNumber, victim.Map?.uniqueID ?? -1, Activities.Now, Math.Min(60, __result.totalDamageDealt * 2),
                    StoryActions.Witnesses(victim.Map, actor, victim.Position), __state);
            }
            catch (Exception ex) { HookSafety.Report("family injury", ex); }
        }
    }
    internal sealed class FatalIncident
    {
        public int Actor, Victim, Map;
        public IncidentContext Context;
        public List<int> Witnesses;
    }
}
