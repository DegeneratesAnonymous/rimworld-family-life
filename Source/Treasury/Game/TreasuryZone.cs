using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>What a painted Treasury Zone is, as the world shows it right now. Recomputed (never saved), so resizing, removing or destroying the room cannot lose an account.</summary>
    internal sealed class TreasuryZoneStatus
    {
        public ZoneFacts Facts = new ZoneFacts();
        public ZoneNeeds Missing = ZoneNeeds.Enclosed | ZoneNeeds.Table | ZoneNeeds.Seat | ZoneNeeds.SilverStorage;
        public int Silver, ValuablesValue, FreeSlots, Storages, LooseSilver, LooseStacks, Computed = -99999;
        public Building Seat; public Thing DepositThing; public SlotGroup DepositGroup;
        public bool Valid => Missing == ZoneNeeds.None;
    }

    /// <summary>The Treasury: a painted zone over an enclosed room with a table, a seat and storage that accepts silver. The zone itself stores nothing and replaces no stockpile; it only says where the office is.</summary>
    public class Zone_Treasury : Zone
    {
        private const int CacheTicks = 600, MaxRoomCells = 400;
        internal TreasuryZoneStatus Status = new TreasuryZoneStatus();

        public Zone_Treasury() { }
        public Zone_Treasury(ZoneManager zoneManager) : base("WFL_TreasuryZone".Translate(), zoneManager) { }

        protected override Color NextZoneColor => new Color(0.95f, 0.8f, 0.3f, 0.05f);
        protected override Material CreateMaterial() { var m = SolidColorMaterials.SimpleSolidColorMaterial(new Color(color.r, color.g, color.b, 0.06f)); m.renderQueue = 3600; return m; }

        /// <summary>The first Treasury Zone on a map is the primary one; any other is ignored (one treasury per settlement to begin with).</summary>
        public bool IsPrimary => Map != null && Map.zoneManager.AllZones.OfType<Zone_Treasury>().OrderBy(z => z.ID).FirstOrDefault() == this;

        internal TreasuryZoneStatus Evaluate(bool force = false)
        {
            int now = Find.TickManager.TicksGame;
            if (!force && now - Status.Computed < CacheTicks) return Status;
            var s = new TreasuryZoneStatus { Computed = now }; var facts = s.Facts; facts.Cells = cells.Count;
            var room = cells.Count > 0 ? cells[0].GetRoom(Map) : null;
            facts.Enclosed = room != null && room.ProperRoom && !room.TouchesMapEdge && cells.All(c => c.GetRoom(Map) == room);
            if (room != null && room.ProperRoom)
            {
                var seen = new HashSet<Thing>(); int scanned = 0; var stacks = new List<ValuablesRegistry.Stack>(); var registry = FamilyComponent.Current?.Registry.Treasury().Valuables ?? new ValuablesRegistry();
                foreach (var cell in room.Cells)
                {
                    if (++scanned > MaxRoomCells) break;
                    foreach (var thing in cell.GetThingList(Map))
                    {
                        if (thing is Building b)
                        {
                            if (!seen.Add(b)) continue;
                            if (b.def.surfaceType == SurfaceType.Eat) facts.Tables++;
                            if (b.def.building != null && b.def.building.isSittable) { facts.Seats++; if (s.Seat == null) s.Seat = b; }
                            if (b is IStoreSettingsParent parent && parent.GetStoreSettings() != null && parent.GetStoreSettings().AllowedToAccept(ThingDefOf.Silver)) { facts.SilverStorages++; s.Storages++; }
                        }
                        else if (thing.def.category == ThingCategory.Item)
                        {
                            bool art = thing.TryGetComp<CompArt>() != null;
                            if (art || registry.Recognises(thing.def.defName)) stacks.Add(new ValuablesRegistry.Stack { DefName = thing.def.defName, Count = thing.stackCount, UnitValue = (int)thing.MarketValue, IsArtOrHeirloom = art });
                        }
                    }
                }
                foreach (var group in room.Cells.Select(c => c.GetSlotGroup(Map)).Where(g => g != null && g.Settings.AllowedToAccept(ThingDefOf.Silver)).Distinct())
                {
                    if (group.parent is Zone_Stockpile) { facts.SilverStorages++; s.Storages++; }
                    s.Silver += group.HeldThings.Where(t => t.def == ThingDefOf.Silver).Sum(t => t.stackCount);
                    s.FreeSlots += group.CellsList.Count(c => !c.GetThingList(Map).Any(t => t.def.category == ThingCategory.Item));
                }
                // Silver that is not yet in the treasury's own storage (loose, or in a lower-priority store) and could be carried there: the outstanding deposits.
                var groups = room.Cells.Select(c => c.GetSlotGroup(Map)).Where(g => g != null && g.Settings.AllowedToAccept(ThingDefOf.Silver)).Distinct().ToList();
                var best = groups.OrderByDescending(g => (int)g.Settings.Priority).FirstOrDefault();
                if (best != null && s.FreeSlots > 0)
                    foreach (var t in Map.listerThings.ThingsOfDef(ThingDefOf.Silver))
                    {
                        if (t.Destroyed || t.IsForbidden(Faction.OfPlayer)) continue;
                        var g = t.GetSlotGroup(); if (g != null && groups.Contains(g)) continue;
                        if (g == null || (int)g.Settings.Priority < (int)best.Settings.Priority) { s.LooseSilver += t.stackCount; s.LooseStacks++; if (s.DepositThing == null) { s.DepositThing = t; s.DepositGroup = best; } }
                    }
                var sum = registry.Summarise(stacks); s.ValuablesValue = sum.EstimatedValuables;
            }
            s.Missing = TreasuryRoom.Missing(facts);
            Status = s;
            return s;
        }

        public override string GetInspectString()
        {
            if (!IsPrimary) return "WFL_TZ_Secondary".Translate();
            var s = Evaluate(true); var c = FamilyComponent.Current; var sb = new System.Text.StringBuilder();
            sb.Append((s.Valid ? "WFL_TZ_Status_Valid" : "WFL_TZ_Status_Invalid").Translate());
            if (!s.Valid) sb.Append('\n').Append("WFL_TZ_Missing".Translate(string.Join(", ", Enum.GetValues(typeof(ZoneNeeds)).Cast<ZoneNeeds>().Where(n => n != ZoneNeeds.None && (s.Missing & n) != 0).Select(n => ("WFL_TZ_Need_" + n).Translate().ToString()))));
            var treasurer = c == null ? null : TreasuryService.Holders(c.Registry, TreasuryRoles.Treasurer).Select(c.TryGetPawn).FirstOrDefault(p => p != null);
            sb.Append('\n').Append("WFL_TZ_Treasurer".Translate(treasurer != null ? treasurer.LabelShort : "WFL_TZ_NoTreasurer".Translate().ToString()));
            sb.Append('\n').Append("WFL_TZ_Reserves".Translate(s.Silver, s.ValuablesValue, s.FreeSlots));
            if (s.LooseStacks > 0) sb.Append('\n').Append("WFL_TZ_Deposits".Translate(s.LooseSilver, s.LooseStacks));
            int waiting = TreasuryOffice.Pending(); if (waiting > 0) sb.Append('\n').Append("WFL_TZ_Office".Translate(waiting));
            long claims = c == null ? 0 : c.Registry.Ledger().TotalClaims; int physical = TreasuryService.PhysicalSilver(Map);
            if (claims > physical) sb.Append('\n').Append("WFL_TZ_MissingSilver".Translate(claims - physical));
            return sb.ToString();
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var g in base.GetGizmos()) yield return g;
            yield return new Command_Action
            {
                defaultLabel = "WFL_TZ_Open".Translate(), defaultDesc = "WFL_TZ_OpenDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/AssignOwner", false) ?? BaseContent.BadTex,
                action = () => MainTabWindow_Community.Open(TreasuryService.PageId)
            };
        }

        public override void PostRegister() { base.PostRegister(); NoteZone(); }
        public override void AddCell(IntVec3 c) { base.AddCell(c); Status.Computed = -99999; }
        public override void RemoveCell(IntVec3 c) { base.RemoveCell(c); Status.Computed = -99999; }
        /// <summary>Deleting the zone removes only the zone. Accounts, debts and payroll live in the ledger and are untouched.</summary>
        public override void Delete(bool playSound) { var st = FamilyComponent.Current?.Registry.Treasury(); if (st != null && st.PrimaryZoneId == ID) st.PrimaryZoneId = 0; base.Delete(playSound); }

        private void NoteZone() { var st = FamilyComponent.Current?.Registry.Treasury(); if (st != null && IsPrimary) { st.PrimaryZoneId = ID; st.MapId = Map.uniqueID; } }
    }

    /// <summary>Architect &gt; Zone &gt; Treasury Zone. Painted like any zone; it may overlap furniture because the room's table, seat and shelves are inside it.</summary>
    public class Designator_ZoneAdd_Treasury : Designator_ZoneAdd
    {
        protected override string NewZoneLabel => "WFL_TreasuryZone".Translate();

        public Designator_ZoneAdd_Treasury()
        {
            zoneTypeToPlace = typeof(Zone_Treasury);
            defaultLabel = "WFL_TreasuryZone".Translate();
            defaultDesc = "WFL_TreasuryZoneDesc".Translate();
            icon = ContentFinder<Texture2D>.Get("UI/Designators/ZoneCreate_Stockpile", false) ?? BaseContent.BadTex;
            hotKey = null;
        }

        protected override Zone MakeNewZone() => new Zone_Treasury(Find.CurrentMap.zoneManager);

        public override AcceptanceReport CanDesignateCell(IntVec3 c)
        {
            if (!c.InBounds(Map)) return false;
            var existing = Map.zoneManager.ZoneAt(c);
            if (existing != null && existing.GetType() != zoneTypeToPlace) return false;
            if (c.Fogged(Map)) return false;
            if (c.InNoZoneEdgeArea(Map)) return "TooCloseToMapEdge".Translate();
            if (c.Impassable(Map)) return false;
            return true;
        }
    }
}
