using FamilyLife.Domain;
using HarmonyLib;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>These hooks run inside the game's own social code, so a fault in them must never reach it. Reported once, then ignored.</summary>
    internal static class HookSafety
    {
        private static readonly System.Collections.Generic.HashSet<string> reported = new System.Collections.Generic.HashSet<string>();

        public static void Report(string where, System.Exception e)
        {
            if (reported.Add(where)) Log.Warning("[Wil's Family Life] The " + where + " hook failed and was skipped: " + e.Message);
        }
    }

    // These two only scale the game's own weights for a breakup and a new romance. They never start or end a relationship themselves.

    /// <summary>A couple that has drifted apart is likelier to split; a steady couple less so.</summary>
    [HarmonyPatch(typeof(InteractionWorker_Breakup), nameof(InteractionWorker_Breakup.RandomSelectionWeight))]
    internal static class Hook_BreakupWeight
    {
        private static void Postfix(Pawn initiator, Pawn recipient, ref float __result)
        {
            try
            {
                var settings = FamilyLifeMod.Settings;
                if (settings != null && (!settings.conflicts || !settings.relationshipHealth)) return;
                if (__result <= 0f) return;
                var component = FamilyComponent.Current;
                if (component == null || initiator == null || recipient == null) return;
                var stage = component.Registry.CoupleStageOf(initiator.thingIDNumber, recipient.thingIDNumber, Activities.Now);
                __result *= ConflictTuning.BreakupMultiplier(stage, false);
            }
            catch (System.Exception e) { HookSafety.Report("breakup weight", e); }
        }
    }

    /// <summary>
    /// Someone who already has a partner does not start a romance with another, unless affairs are turned on and their own relationship
    /// is in real crisis. This is not about vanilla's cheating thoughts; those still apply to whatever happens.
    /// </summary>
    [HarmonyPatch(typeof(InteractionWorker_RomanceAttempt), nameof(InteractionWorker_RomanceAttempt.RandomSelectionWeight))]
    internal static class Hook_RomanceWeight
    {
        private static void Postfix(Pawn initiator, Pawn recipient, ref float __result)
        {
            try { Adjust(initiator, recipient, ref __result); }
            catch (System.Exception e) { HookSafety.Report("romance weight", e); }
        }

        private static void Adjust(Pawn initiator, Pawn recipient, ref float __result)
        {
            if (__result <= 0f || initiator == null || recipient == null) return;
            var settings = FamilyLifeMod.Settings;
            if (settings != null && !settings.conflicts && !settings.strictRomance) return;
            var component = FamilyComponent.Current;
            if (component == null) return;
            var registry = component.Registry;
            var me = registry.GetPerson(initiator.thingIDNumber);
            if (me == null) return;

            bool partnered = false;
            int partnerId = 0;
            foreach (var id in me.PartnerIds())
            {
                var partner = registry.GetPerson(id);
                if (partner == null || !partner.IsAlive) continue;
                partnered = true;
                partnerId = id;
                if (id == recipient.thingIDNumber) return;       // their own partner: nothing to change
            }
            if (!partnered) return;

            bool strict = settings != null && settings.strictRomance;
            bool affairsOn = settings != null && settings.affairs;
            if (strict || !affairsOn) { __result = 0f; return; }

            var stage = registry.CoupleStageOf(initiator.thingIDNumber, partnerId, Activities.Now);
            var profile = component.ProfileOf(initiator.thingIDNumber);
            float devotion = profile == null ? 0f : profile[(int)Dim.Devotion];
            float familiarity = registry.GetBond(initiator.thingIDNumber, recipient.thingIDNumber)?.Familiarity ?? 0f;
            if (!ConflictTuning.AffairPossible(true, stage, devotion, familiarity)) { __result = 0f; return; }
            __result *= 0.5f;
        }
    }
}
