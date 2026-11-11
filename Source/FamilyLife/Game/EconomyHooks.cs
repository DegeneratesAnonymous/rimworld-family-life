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
    public static class EconomyHits { public static int Trades, TradesCompleted, SilverDrops, WorkReports; }

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
