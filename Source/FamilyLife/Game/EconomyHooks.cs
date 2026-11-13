using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace FamilyLife
{
    // Read-only hooks on vanilla economy events, owned by the framework (modules add listeners through the contribution lists, never a patch of their own). They change nothing about the trade, the
    // reward or the work; each calls only listeners of modules that are running and never throws into the game. Owner: the framework.

    /// <summary>How often each economy hook fired this session. Shown by the Treasury diagnostics: the way to see in a running game that the hooks attached.</summary>
    public static class EconomyHits { public static int Trades, TradesCompleted, SilverDrops, WorkReports, JobsFinished; }

    /// <summary>One line of a completed trade, from the colony's side. <see cref="Sold"/> is true when the colony sold it.</summary>
    public sealed class TradeLine { public string DefName; public int Count, UnitPrice; public bool Sold; public Pawn OwnerPawn; }

    /// <summary>A trade as it was agreed. <see cref="ColonySilverBefore"/> is the silver the colony side held when the trade began (the physical limit on what it could pay).</summary>
    public sealed class TradeReport { public readonly List<TradeLine> Lines = new List<TradeLine>(); public int ColonySilverBefore; public string Id; }

    public static class EconomyHooks
    {
        [ThreadStatic] private static TradeReport pending;
        internal static void Begin(TradeDeal deal)
        {
            pending = null;
            var report = new TradeReport { Id = "t" + Find.TickManager.TicksGame + "-" + (++seq) };
            report.ColonySilverBefore = deal.CurrencyTradeable?.CountHeldBy(Transactor.Colony) ?? 0;
            foreach (var t in deal.AllTradeables)
            {
                if (t == null || t.IsCurrency || t.IsFavor || t.CountToTransfer == 0 || t.ThingDef == null) continue;
                var action = t.ActionToDo; if (action != TradeAction.PlayerBuys && action != TradeAction.PlayerSells) continue;
                var owner = action == TradeAction.PlayerSells ? OwnerOf(t.FirstThingColony) : null;
                report.Lines.Add(new TradeLine { DefName = t.ThingDef.defName, Count = Math.Abs(t.CountToTransfer), UnitPrice = (int)Math.Round(t.GetPriceFor(action)), Sold = action == TradeAction.PlayerSells, OwnerPawn = owner });
            }
            pending = report;
        }
        private static int seq;

        /// <summary>Vanilla has no ownership of trade goods, but what a person carries, wears or wields is theirs. Anything else is communal (null).</summary>
        private static Pawn OwnerOf(Thing thing)
        {
            var holder = thing?.ParentHolder;
            if (holder is Pawn_InventoryTracker inv) return inv.pawn;
            if (holder is Pawn_ApparelTracker app) return app.pawn;
            if (holder is Pawn_EquipmentTracker eq) return eq.pawn;
            return null;
        }

        internal static void Finish(bool traded)
        {
            var report = pending; pending = null;
            if (report == null) return;
            EconomyHits.Trades++;
            if (!traded || report.Lines.Count == 0) return;
            EconomyHits.TradesCompleted++;
            foreach (var listener in FamilyFramework.TradeCompleted.Enabled())
                try { listener(report); } catch (Exception ex) { Log.Warning("[Wil's Family Life] A trade listener was skipped: " + ex.Message); }
        }

        internal static void SilverDropped(QuestPart_DropPods part, int silver)
        {
            if (silver <= 0) return;
            EconomyHits.SilverDrops++;
            string key = "quest:" + (part.quest?.id ?? 0) + ":" + (part.quest?.PartsListForReading.IndexOf(part) ?? 0);
            foreach (var listener in FamilyFramework.SilverReceived.Enabled())
                try { listener(silver, key); } catch (Exception ex) { Log.Warning("[Wil's Family Life] A silver-reward listener was skipped: " + ex.Message); }
        }
    }


    [HarmonyLib.HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.TryExecute))]
    internal static class Patch_TradeExecute
    {
        private static void Prefix(TradeDeal __instance) { try { EconomyHooks.Begin(__instance); } catch (Exception ex) { Log.Warning("[Wil's Family Life] A trade could not be read: " + ex.Message); } }
        private static void Postfix(bool actuallyTraded) { try { EconomyHooks.Finish(actuallyTraded); } catch (Exception ex) { Log.Warning("[Wil's Family Life] A trade could not be recorded: " + ex.Message); } }
    }

    [HarmonyLib.HarmonyPatch(typeof(QuestPart_DropPods), nameof(QuestPart_DropPods.Notify_QuestSignalReceived))]
    internal static class Patch_QuestDrop
    {
        private static void Prefix(QuestPart_DropPods __instance, Signal signal)
        {
            try
            {
                if (__instance == null || signal.tag != __instance.inSignal || __instance.Things == null) return;
                int silver = __instance.Things.Where(t => t != null && t.def == ThingDefOf.Silver).Sum(t => t.stackCount);
                EconomyHooks.SilverDropped(__instance, silver);
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] A quest reward could not be read: " + ex.Message); }
        }
    }
}

namespace FamilyLife
{
    /// <summary>A vanilla job that ended <i>successfully</i> (never one that was started, interrupted or cancelled), with the few facts a wage needs.</summary>
    public sealed class LabourReport
    {
        public Pawn Pawn; public string JobDef, WorkType, RecipeSkill; public int JobId, ThingId; public bool TargetDead, HaulToStorage; public float WorkAmount;
    }

    [HarmonyLib.HarmonyPatch(typeof(Verse.AI.Pawn_JobTracker), nameof(Verse.AI.Pawn_JobTracker.EndCurrentJob))]
    internal static class Patch_JobSucceeded
    {
        private static void Prefix(Verse.AI.Pawn_JobTracker __instance, Verse.AI.JobCondition condition, Pawn ___pawn)
        {
            try
            {
                if (condition != Verse.AI.JobCondition.Succeeded || FamilyFramework.LabourDone.Count == 0) return;
                var pawn = ___pawn; var job = __instance?.curJob; if (pawn == null || job == null || job.def == null || pawn.Faction != Faction.OfPlayer) return;
                var report = new LabourReport
                {
                    Pawn = pawn, JobDef = job.def.defName, JobId = job.loadID, ThingId = job.targetA.Thing?.thingIDNumber ?? 0,
                    WorkType = job.workGiverDef?.workType?.defName, HaulToStorage = job.haulMode == Verse.AI.HaulMode.ToCellStorage,
                    TargetDead = job.targetA.Thing is Pawn prey && (prey.Dead || prey.Destroyed), RecipeSkill = job.bill?.recipe?.workSkill?.defName, WorkAmount = job.bill?.recipe?.workAmount ?? 0f
                };
                EconomyHits.JobsFinished++;
                foreach (var listener in FamilyFramework.LabourDone.Enabled()) { try { listener(report); } catch (Exception ex) { Log.Warning("[Wil's Family Life] A labour listener was skipped: " + ex.Message); } }
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] A finished job could not be read: " + ex.Message); }
        }
    }

    [HarmonyLib.HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.ResearchPerformed))]
    internal static class Patch_ResearchPerformed
    {
        private static void Postfix(float amount, Pawn researcher)
        {
            try
            {
                if (amount <= 0f || researcher == null || FamilyFramework.ResearchDone.Count == 0) return;
                foreach (var listener in FamilyFramework.ResearchDone.Enabled()) { try { listener(researcher, amount); } catch (Exception ex) { Log.Warning("[Wil's Family Life] A research listener was skipped: " + ex.Message); } }
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Research work could not be read: " + ex.Message); }
        }
    }
}

namespace FamilyLife
{
    [HarmonyLib.HarmonyPatch(typeof(RimWorld.WorkGiver_DoBill), nameof(RimWorld.WorkGiver_DoBill.JobOnThing))]
    internal static class Patch_BillWorkPermission
    {
        private static bool Prefix(Pawn pawn, Thing thing, ref Verse.AI.Job __result)
        {
            foreach (var predicate in FamilyFramework.BillWorkAllowed.Enabled())
                try { if (!predicate(pawn, thing)) { __result = null; return false; } } catch (Exception ex) { Log.Warning("[Wil's Family Life] Work permission skipped: " + ex.Message); }
            return true;
        }
    }
    [HarmonyLib.HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
    internal static class Patch_RecipeProducts
    {
        private static void Postfix(ref IEnumerable<Thing> __result, Pawn worker, IBillGiver billGiver)
        {
            if (FamilyFramework.ProductMade.Count > 0) __result = Observe(__result, worker, billGiver as Thing, worker?.CurJob?.loadID ?? 0);
        }
        private static IEnumerable<Thing> Observe(IEnumerable<Thing> products, Pawn worker, Thing bench, int job)
        {
            foreach (var product in products)
            {
                foreach (var listener in FamilyFramework.ProductMade.Enabled())
                    try { listener(worker, bench, product, job); } catch (Exception ex) { Log.Warning("[Wil's Family Life] Production observer skipped: " + ex.Message); }
                yield return product;
            }
        }
    }
    [HarmonyLib.HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
    internal static class Patch_CommercialSplit
    {
        private static void Postfix(Thing __instance, Thing __result)
        {
            if (__result == null || __instance == __result) return;
            foreach (var listener in FamilyFramework.StackSplit.Enabled())
                try { listener(__instance, __result); } catch (Exception ex) { Log.Warning("[Wil's Family Life] Split observer skipped: " + ex.Message); }
        }
    }
    [HarmonyLib.HarmonyPatch(typeof(Thing), nameof(Thing.TryAbsorbStack))]
    internal static class Patch_CommercialMerge
    {
        private static void Prefix(Thing __instance, out int __state) { __state = __instance.stackCount; }
        private static void Postfix(Thing __instance, Thing other, int __state)
        {
            int moved = __instance.stackCount - __state; if (moved <= 0) return;
            foreach (var listener in FamilyFramework.StackMerged.Enabled())
                try { listener(other, __instance, moved); } catch (Exception ex) { Log.Warning("[Wil's Family Life] Merge observer skipped: " + ex.Message); }
        }
    }
}

namespace FamilyLife
{
    [HarmonyLib.HarmonyPatch(typeof(RimWorld.WorkGiver_DoBill), "IsUsableIngredient")]
    internal static class Patch_BusinessIngredients
    {
        private static void Postfix(Thing t, RimWorld.Bill bill, ref bool __result)
        {
            if (!__result) return;
            foreach (var predicate in FamilyFramework.BillIngredientAllowed.Enabled())
                try { if (!predicate(t, bill?.billStack?.billGiver as Thing)) { __result = false; return; } } catch (Exception ex) { Log.Warning("[Wil's Family Life] Ingredient access skipped: " + ex.Message); }
        }
    }
    [HarmonyLib.HarmonyPatch(typeof(RimWorld.FoodUtility), nameof(RimWorld.FoodUtility.WillEat), new[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) })]
    internal static class Patch_BusinessFoodAccess
    {
        private static void Postfix(Pawn p, Thing food, ref bool __result)
        {
            if (!__result) return;
            foreach (var predicate in FamilyFramework.FoodAccess.Enabled())
                try { if (!predicate(p, food)) { __result = false; return; } } catch (Exception ex) { Log.Warning("[Wil's Family Life] Food access skipped: " + ex.Message); }
        }
    }
}

namespace FamilyLife
{
    [HarmonyLib.HarmonyPatch(typeof(RimWorld.Pawn_InteractionsTracker), nameof(RimWorld.Pawn_InteractionsTracker.TryInteractWith))]
    internal static class Patch_CommercialInteraction
    {
        private static void Postfix(Pawn ___pawn, Pawn recipient, InteractionDef intDef, bool __result)
        {
            if (!__result) return;
            foreach (var listener in FamilyFramework.SocialInteraction.Enabled())
                try { listener(___pawn, recipient, intDef); } catch (Exception ex) { Log.Warning("[Wil's Family Life] Interaction observer skipped: " + ex.Message); }
        }
    }
}

namespace FamilyLife
{
    [HarmonyLib.HarmonyPatch(typeof(Verse.AI.Group.Trigger_TicksPassed), nameof(Verse.AI.Group.Trigger_TicksPassed.ActivateOn))]
    internal static class Patch_VisitorTimedDeparture
    {
        private static readonly Type TraderToil = typeof(LordJob_TradeWithColony).Assembly.GetType("RimWorld.LordToil_DefendTraderCaravan");
        private static void Postfix(Verse.AI.Group.Lord lord, ref bool __result)
        {
            if (!__result || lord == null || !(lord.LordJob is LordJob_TradeWithColony || lord.LordJob is LordJob_VisitColony)) return;
            // Only the normal visit/trade timer. Travel, danger, violence and other transitions are unaffected.
            if (!(lord.CurLordToil is Verse.AI.Group.LordToil_DefendPoint || lord.CurLordToil?.GetType() == TraderToil)) return;
            foreach (var predicate in FamilyFramework.TimedDepartureAllowed.Enabled())
                try { if (!predicate(lord)) { __result = false; return; } } catch (Exception ex) { Log.Warning("[Wil's Family Life] Departure preference skipped: " + ex.Message); }
        }
    }
}
