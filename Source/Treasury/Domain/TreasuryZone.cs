using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    [Flags] public enum ZoneNeeds : byte { None = 0, Enclosed = 1, Table = 2, Seat = 4, SilverStorage = 8 }

    /// <summary>What was found in a painted zone. The game layer fills it in from room detection and the things inside; this decides whether it is a Treasury.</summary>
    public sealed class ZoneFacts { public int Cells; public bool Enclosed; public int Tables, Seats, SilverStorages; }

    public static class TreasuryRoom
    {
        /// <summary>The requirements still missing. A zone is valid when none are. A storage location need not hold silver yet, only accept it.</summary>
        public static ZoneNeeds Missing(ZoneFacts f)
        {
            var m = ZoneNeeds.None;
            if (f == null || f.Cells <= 0 || !f.Enclosed) m |= ZoneNeeds.Enclosed;
            if (f == null || f.Tables <= 0) m |= ZoneNeeds.Table;
            if (f == null || f.Seats <= 0) m |= ZoneNeeds.Seat;
            if (f == null || f.SilverStorages <= 0) m |= ZoneNeeds.SilverStorage;
            return m;
        }
        public static bool Valid(ZoneFacts f) => Missing(f) == ZoneNeeds.None;
        /// <summary>The Treasurer's office work needs a valid room and a Treasurer; accounts and payroll need neither.</summary>
        public static bool OfficeOpen(ZoneFacts f, bool hasTreasurer) => hasTreasurer && Valid(f);
    }

    /// <summary>One kind of valuable: liquid silver is spendable; everything else is stored wealth with an estimated value and is never silently turned into silver.</summary>
    public sealed class ValuableClass { public string DefName; public bool Liquid; }

    /// <summary>The recognised valuables, configurable. Silver is liquid; gold, jade and onyx are listed by default; the player can add any (modded) material.</summary>
    public sealed class ValuablesRegistry
    {
        private readonly Dictionary<string, ValuableClass> byDef = new Dictionary<string, ValuableClass>(StringComparer.Ordinal);
        public ValuablesRegistry()
        {
            Set("Silver", true);
            foreach (var d in new[] { "Gold", "Jade", "Onyx" }) Set(d, false);
        }
        public IEnumerable<ValuableClass> All => byDef.Values.OrderBy(v => v.DefName, StringComparer.Ordinal);
        public void Set(string defName, bool liquid) { if (!string.IsNullOrEmpty(defName)) byDef[defName] = new ValuableClass { DefName = defName, Liquid = liquid }; }
        public bool Remove(string defName) => defName != "Silver" && byDef.Remove(defName);
        public bool Recognises(string defName) => defName != null && byDef.ContainsKey(defName);
        public bool IsLiquid(string defName) => defName != null && byDef.TryGetValue(defName, out var v) && v.Liquid;

        public struct Stack { public string DefName; public int Count; public int UnitValue; public bool IsArtOrHeirloom; }
        public sealed class Summary { public int LiquidSilver, EstimatedValuables, ValuableStacks; }

        /// <summary>Totals what a treasury holds. Only liquid silver is spendable; valuables are counted separately by their estimated market value (artwork and heirlooms always count as valuables).</summary>
        public Summary Summarise(IEnumerable<Stack> stacks)
        {
            var s = new Summary();
            foreach (var st in stacks ?? new Stack[0])
            {
                if (st.Count <= 0) continue;
                if (!st.IsArtOrHeirloom && IsLiquid(st.DefName)) s.LiquidSilver += st.Count;
                else if (st.IsArtOrHeirloom || Recognises(st.DefName)) { s.EstimatedValuables += (int)Math.Min(int.MaxValue, (long)st.Count * Math.Max(0, st.UnitValue)); s.ValuableStacks++; }
            }
            return s;
        }
    }

    /// <summary>
    /// The settlement's treasury as the ledger sees it: which zone (by id) is the primary one and which valuables are recognised. The zone's validity is recomputed from the world and is never saved,
    /// and nothing here is ever deleted when a zone is removed, resized or destroyed: accounts live in the ledger.
    /// </summary>
    public sealed class TreasuryState : IModuleState
    {
        public const string StateId = "wils.treasury.state";
        public int PrimaryZoneId, MapId, LastZoneDay, CommunityPercent = 25, OwnerFeePercent;
        public bool AdoptSurplus = true;
        public readonly ValuablesRegistry Valuables = new ValuablesRegistry();
        public string Encode()
        {
            var w = new StateWriter().Int("zone", PrimaryZoneId).Int("map", MapId).Int("zday", LastZoneDay).Int("cpct", CommunityPercent + 1).Int("fee", OwnerFeePercent).Bool("noadopt", !AdoptSurplus);
            w.Strings("val", Valuables.All.Select(v => v.DefName + "|" + (v.Liquid ? 1 : 0)).ToList());
            return w.ToString();
        }
        public static TreasuryState Decode(StateReader r)
        {
            var s = new TreasuryState { PrimaryZoneId = r.Int("zone"), MapId = r.Int("map"), LastZoneDay = r.Int("zday"), CommunityPercent = r.Has("cpct") ? r.Int("cpct") - 1 : 25, OwnerFeePercent = r.Int("fee"), AdoptSurplus = !r.Bool("noadopt") };
            var list = r.Strings("val");
            if (list.Count > 0) { foreach (var v in s.Valuables.All.ToList()) s.Valuables.Remove(v.DefName); foreach (var x in list) { var p = x.Split('|'); if (p.Length == 2) s.Valuables.Set(p[0], p[1] == "1"); } }
            return s;
        }
    }

    public sealed class TreasuryStateFactory : IStateFactory<TreasuryState>
    {
        public static readonly TreasuryStateFactory Instance = new TreasuryStateFactory();
        public int Version => 1;
        public TreasuryState Create() => new TreasuryState();
        public TreasuryState Decode(StateReader reader, int version) => TreasuryState.Decode(reader);
        public TreasuryState FromLegacy(StateReader legacy) => new TreasuryState();
    }

    public static class TreasuryStateAccess { public static TreasuryState Treasury(this FamilyRegistry r) => r.Shared.Of(TreasuryState.StateId, TreasuryStateFactory.Instance); }
}
