using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>One person's place in a household, as the Home tab lists it.</summary>
    internal sealed class HomeRow
    {
        public PersonRecord Person;
        public string Link = "", Bed = "", Room = "", Area = "", Problem = "";
        public bool NoBed;
    }

    internal sealed class HomeSupply
    {
        public SupplyKind Kind;
        public bool Known;
        public float Current, Target;
        public string Unit = "";
        public string Text = "";
        public SupplyLevel Level;
    }

    /// <summary>
    /// The live facts the Home tab and the Summary need: beds and rooms, what the family holds against what it wants to keep, and what is
    /// missing. Worked out from the map every few seconds, not every frame, and only when one of those tabs is open.
    /// </summary>
    internal sealed class HomeModel
    {
        public const int LiveTicks = 240;
        public FamilyRegistry Registry;
        public int LineageId, Revision = -1, Viewer = -1, BuiltTick = -100000;
        public bool HasMap;
        public string AreaLabel = "";
        public Color AreaColor = Color.white;
        public int Covered, Total, WithBed;
        public List<KeyValuePair<int, List<HomeRow>>> Households = new List<KeyValuePair<int, List<HomeRow>>>();
        public List<HomeSupply> Supplies = new List<HomeSupply>();
        public List<string> Missing = new List<string>();
        public string MeetingPlace = "";

        public bool Fresh(FamilyComponent c, int lineageId, int now) =>
            Registry == c.Registry && LineageId == lineageId && Revision == c.Revision && Viewer == c.PerspectivePawnId && now - BuiltTick < LiveTicks;

        public static HomeModel Build(FamilyComponent component, FamilyView view, int now)
        {
            var r = component.Registry;
            var house = view.House;
            var m = new HomeModel { Registry = r, LineageId = view.LineageId, Revision = component.Revision, Viewer = component.PerspectivePawnId, BuiltTick = now };
            var area = FamilyZones.AreaFor(house);
            var map = area?.Map ?? FamilyZones.HomeMap();
            m.HasMap = map != null;
            if (area != null) { m.AreaLabel = area.Label; m.AreaColor = area.Color; m.Covered = FamilyZones.RestrictedCount(component, house, area); }
            m.Total = view.Colony.Count;
            var office = r.OfficeOf(house.Id);
            if (office != null) m.MeetingPlace = "WFL_Office_At".Translate(office.X, office.Z);

            var bedless = new List<string>();
            foreach (var household in r.HouseholdsOfLineage(house.Id))
            {
                var rows = new List<HomeRow>();
                foreach (var id in household.ResidentIds)
                {
                    var p = r.GetPerson(id);
                    if (p == null) continue;
                    var row = new HomeRow { Person = p, Link = LinkText(r, p, household) };
                    var pawn = component.TryGetPawn(id);
                    if (p.InColony && pawn != null)
                    {
                        var bed = pawn.ownership?.OwnedBed;
                        if (bed != null)
                        {
                            row.Bed = bed.LabelShortCap;
                            try { var room = bed.GetRoom(); row.Room = room == null || room.PsychologicallyOutdoors ? "WFL_FW_Outdoors".Translate().ToString() : (room.Role?.label ?? "").CapitalizeFirst(); }
                            catch (Exception) { }
                            m.WithBed++;
                            if (area != null && area.Map == bed.Map) row.Area = area[bed.Position] ? "WFL_FW_InsideArea".Translate().ToString() : "WFL_FW_OutsideArea".Translate().ToString();
                            if (area != null && area.Map == bed.Map && !area[bed.Position]) row.Problem = "WFL_FW_Problem_BedOutside".Translate().ToString();
                        }
                        else if (!pawn.Dead && pawn.ageTracker.CurLifeStage.developmentalStage != DevelopmentalStage.Newborn)
                        {
                            row.NoBed = true; row.Problem = "WFL_FW_Problem_NoBed".Translate().ToString();
                            bedless.Add(p.Name);
                        }
                    }
                    rows.Add(row);
                }
                if (rows.Count > 0) m.Households.Add(new KeyValuePair<int, List<HomeRow>>(household.Id, rows));
            }

            // What the family holds against what it wants to keep.
            int members = Math.Max(1, view.Colony.Count);
            var pawns = view.Colony.Select(p => component.TryGetPawn(p.Id)).Where(p => p != null && p.Spawned).ToList();
            if (map != null)
            {
                float foodHeld = component.Economy.HeldBy(house, map, SupplyKind.Food, now);
                float foodDays = foodHeld / (members * 1.6f);
                m.Supplies.Add(Make(SupplyKind.Food, true, foodDays, house.FoodReserveDays, "WFL_FW_Unit_Days", "0.0"));

                float lowestFuel = float.MaxValue;
                if (area != null && area.Map == map)
                    foreach (var building in map.listerBuildings.allBuildingsColonist.Take(400))
                    {
                        if (!area[building.Position]) continue;
                        var fuel = building.GetComp<CompRefuelable>();
                        if (fuel == null || fuel.Props.fuelIsMortarBarrel || fuel.Props.fuelConsumptionRate <= 0f) continue;
                        lowestFuel = Math.Min(lowestFuel, fuel.Fuel / fuel.Props.fuelConsumptionRate);
                    }
                m.Supplies.Add(Make(SupplyKind.Fuel, lowestFuel < float.MaxValue, lowestFuel < float.MaxValue ? lowestFuel : 0f, house.FuelReserveDays, "WFL_FW_Unit_Days", "0.0"));
                m.Supplies.Add(Make(SupplyKind.Medicine, true, component.Economy.HeldBy(house, map, SupplyKind.Medicine, now), house.MedicineReserve, "WFL_FW_Unit_Pieces", "0"));
            }
            int unclothed = pawns.Count(p => p.apparel != null && p.apparel.WornApparelCount == 0);
            m.Supplies.Add(new HomeSupply
            {
                Kind = SupplyKind.Apparel, Known = pawns.Count > 0, Level = pawns.Count == 0 ? SupplyLevel.Unknown : unclothed == 0 ? SupplyLevel.Enough : SupplyLevel.Short,
                Text = pawns.Count == 0 ? "" : unclothed == 0 ? "WFL_FW_Apparel_Enough".Translate().ToString() : "WFL_FW_Apparel_Short".Translate(unclothed).ToString()
            });

            if (bedless.Count > 0) m.Missing.Add("WFL_FW_Missing_Beds".Translate(string.Join(", ", bedless.Take(5)) + (bedless.Count > 5 ? "..." : "")));
            if (area == null && m.HasMap) m.Missing.Add("WFL_FW_Missing_Area".Translate());
            if (office == null && area != null) m.Missing.Add("WFL_FW_Missing_Meeting".Translate());
            var food = m.Supplies.FirstOrDefault(s => s.Kind == SupplyKind.Food);
            if (food != null && food.Level == SupplyLevel.Short) m.Missing.Add("WFL_FW_Missing_Food".Translate());
            return m;
        }

        private static HomeSupply Make(SupplyKind kind, bool known, float current, float target, string unitKey, string format)
        {
            string unit = unitKey.Translate();
            return new HomeSupply
            {
                Kind = kind, Known = known, Current = current, Target = target, Unit = unit, Level = SupplyReading.Level(known, current, target),
                Text = known ? current.ToString(format) + " / " + target.ToString(format) + " " + unit : "WFL_FW_NotTracked".Translate().ToString()
            };
        }

        /// <summary>Who a person lives with and how they are connected to the household's other residents: a partner, a parent or guardian.</summary>
        private static string LinkText(FamilyRegistry r, PersonRecord p, Household household)
        {
            var partner = p.PartnerIds().Select(id => household.ResidentIds.Contains(id) ? r.GetPerson(id) : null).FirstOrDefault(x => x != null);
            if (partner != null) return "WFL_FW_PartnerOfN".Translate(partner.Name).ToString();
            if (p.Juvenile)
            {
                var guardians = p.ParentIds.Where(id => household.ResidentIds.Contains(id)).Select(id => r.GetPerson(id)?.Name).Where(n => n != null).ToList();
                if (guardians.Count > 0) return "WFL_FW_ChildOfN".Translate(string.Join(" & ", guardians)).ToString();
                var other = household.ResidentIds.Where(id => id != p.Id).Select(id => r.GetPerson(id)).FirstOrDefault(x => x != null && !x.Juvenile);
                if (other != null) return "WFL_FW_GuardianN".Translate(other.Name).ToString();
            }
            return "";
        }
    }

    public partial class Window_Families
    {
        private HomeModel homeModel;

        internal HomeModel HomeFor(FamilyComponent component)
        {
            int now = Find.TickManager.TicksGame;
            if (homeModel == null || !homeModel.Fresh(component, State.LineageId, now)) homeModel = HomeModel.Build(component, view, now);
            return homeModel;
        }

        private void DrawHome(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry)
        {
            var home = HomeFor(component);
            switch ((HomeSub)State.HomeSub)
            {
                case HomeSub.Supplies: DrawSupplies(v, ref y, component, registry, home); break;
                case HomeSub.Area: DrawArea(v, ref y, component, registry, home); break;
                default: DrawHouseholds(v, ref y, component, registry, home); break;
            }
        }

        // ------------------------------------------------------------------ households

        private void DrawHouseholds(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry, HomeModel home)
        {
            var house = view.House;
            if (home.Households.Count == 0) { FamilyKit.EmptyState(v, ref y, "WFL_NoHouseholds".Translate()); return; }
            int actor = component.PerspectivePawnId;
            PlaceCards(v, ref y, home.Households, (col, ys, ci, pair) => { float cardY = ys[ci]; DrawHouseholdCard(col, ref cardY, component, registry, pair, actor); ys[ci] = cardY; });
        }

        /// <summary>One household as a card: its own name, who lives there, and where each sleeps. Residents who belong to another House say so.</summary>
        private void DrawHouseholdCard(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, KeyValuePair<int, List<HomeRow>> pair, int actor)
        {
            var house = view.House;
            {
                var household = registry.GetHousehold(pair.Key);
                float? cohesion = household == null ? null : registry.HouseholdCohesion(household);
                string title = view.HouseholdLabels.TryGetValue(pair.Key, out var hhLabel) ? hhLabel : "WFL_FW_HH_Plain".Translate().ToString();
                if (cohesion.HasValue) title += "  -  " + ("WFL_Tier_" + BondTuning.CohesionTier(cohesion.Value)).Translate();
                var card = FamilyKit.BeginCard(col, ref y, "hh" + pair.Key, title);
                var cv = card.View;
                float cy = 0f;
                foreach (var row in pair.Value)
                {
                    var r = new Rect(0f, cy, cv.width, 38f);
                    Widgets.DrawHighlightIfMouseover(r);
                    DrawPortrait(new Rect(r.x, r.y + 1f, 28f, 34f), row.Person, component);
                    float actionW = 34f;
                    float tx = 38f, tw = cv.width - tx - actionW - 4f;
                    Widgets.Label(new Rect(tx, r.y, tw * 0.45f, 20f), (row.Person.FullName + (row.Person.Juvenile ? " (" + "WFL_Child".Translate() + ")" : "")).Truncate(tw * 0.45f));
                    Text.Font = GameFont.Tiny;
                    GUI.color = FamilyKit.Dim;
                    string linkText = People(component).ById.TryGetValue(row.Person.Id, out var personRow) && personRow.HouseNote.Length > 0 ? personRow.HouseNote : row.Link;
                    Widgets.Label(new Rect(tx + tw * 0.45f, r.y + 2f, tw * 0.55f, 18f), linkText.Truncate(tw * 0.55f));
                    string bed = row.Bed.Length > 0 ? row.Bed + (row.Room.Length > 0 ? " - " + row.Room : "") + (row.Area.Length > 0 ? " - " + row.Area : "") : "";
                    if (bed.Length > 0) Widgets.Label(new Rect(tx, r.y + 20f, tw, 16f), bed.Truncate(tw));
                    GUI.color = Color.white;
                    if (row.Problem.Length > 0)
                    {
                        float pw = FamilyKit.BadgeWidth(row.Problem);
                        FamilyKit.Badge(new Rect(tx + (bed.Length > 0 ? 0f : 0f) + (bed.Length > 0 ? Mathf.Min(tw - pw, Text.CalcSize(bed).x + 8f) : 0f), r.y + 20f, pw, 16f), row.Problem, FamilyKit.Warn);
                    }
                    Text.Font = GameFont.Small;
                    bool may = actor == 0 || actor == row.Person.Id || registry.CanManageHouse(actor, house.Id);
                    var more = new Rect(cv.width - actionW, r.y + 4f, actionW, 26f);
                    if (FamilyKit.Action(more, "...", may, "WFL_FW_WhyNotMove".Translate(FamilyUi.NameOf(registry, actor), row.Person.Name), "WFL_FW_RowMenuTip".Translate(row.Person.Name)))
                        OpenRowMenu(component, registry, row.Person);
                    cy += 40f;
                }
                FamilyKit.EndCard(card, cy, ref y);
            }
        }

        /// <summary>Everything that can be done about one person's housing, each option saying who moves where.</summary>
        private void OpenRowMenu(FamilyComponent component, FamilyRegistry registry, PersonRecord person)
        {
            var options = new List<FloatMenuOption>();
            foreach (var household in registry.Households.OrderBy(h => h.Id))
            {
                if (household.Id == person.HouseholdId) continue;
                var h = household;
                options.Add(new FloatMenuOption("WFL_MoveTo".Translate(FamilyUi.NameList(registry, h.ResidentIds)).ToString().Insert(0, person.Name + ": "), () => component.MovePerson(person.Id, h.Id)));
            }
            options.Add(new FloatMenuOption(person.Name + ": " + "WFL_MoveToNew".Translate(), () => component.MovePerson(person.Id, 0)));
            if (person.InColony) options.Add(new FloatMenuOption("WFL_FW_FindNamed".Translate(person.Name), () => FamilyUi.Jump(person.Id)));
            options.Add(new FloatMenuOption("WFL_FW_RelationshipsOf".Translate(person.Name), () => { State.SelectedPerson = person.Id; State.SelectedRelation = 0; State.Navigate(FamilyTab.Relationships); }));
            Find.WindowStack.Add(new FloatMenu(options));
        }

        // ------------------------------------------------------------------ supplies

        private void SupplyRow(Rect v, ref float y, HomeSupply s, string label, string tip)
        {
            var row = new Rect(0f, y, v.width, 24f);
            Widgets.Label(new Rect(0f, y, 80f, 24f), label);
            TooltipHandler.TipRegion(new Rect(0f, y, 80f, 24f), tip);
            Color c = s.Level == SupplyLevel.Short ? FamilyKit.Urgent : s.Level == SupplyLevel.Low ? FamilyKit.Warn : s.Level == SupplyLevel.Enough ? FamilyKit.Good : FamilyKit.Unknown;
            float valueW = Mathf.Min(150f, v.width * 0.4f);
            GUI.color = s.Known ? Color.white : FamilyKit.Dim;
            Widgets.Label(new Rect(84f, y, valueW, 24f), s.Text);
            GUI.color = Color.white;
            float bx = 84f + valueW + 6f;
            float badgeW = 76f;
            float barW = Mathf.Max(40f, v.width - bx - badgeW - 6f);
            float fill = s.Target > 0f ? Mathf.Clamp01(s.Current / s.Target) * 100f : 100f;
            FamilyKit.MetricBar(new Rect(bx, y + 6f, barW, 12f), fill, c, tip, s.Known && s.Kind != SupplyKind.Apparel);
            string lvl = ("WFL_FW_Level_" + s.Level).Translate();
            FamilyKit.Badge(new Rect(v.width - badgeW, y + 3f, badgeW, 18f), lvl, c);
            y += 28f;
        }

        private void DrawSupplies(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry, HomeModel home)
        {
            var house = view.House;
            var col = new Rect(0f, 0f, Mathf.Min(v.width, 720f), 0f);
            bool may = MayManage(registry, house);
            var card = FamilyKit.BeginCard(col, ref y, "reserves", "WFL_Pantry_Title".Translate(), "WFL_FW_ReservesTip".Translate());
            var cv = card.View;
            float cy = 0f;
            if (!may) { cy += FamilyKit.Label(cv, cy, "WFL_FW_WhyNot_Manage".Translate(FamilyUi.NameOf(registry, component.PerspectivePawnId), view.Name), 0f, true); }
            bool saved = GUI.enabled;
            GUI.enabled = saved && may;
            try
            {
                foreach (var s in home.Supplies)
                {
                    string label = ("WFL_FW_Supply_" + s.Kind).Translate();
                    SupplyRow(cv, ref cy, s, label, ("WFL_FW_SupplyTip_" + s.Kind).Translate());
                    if (s.Kind == SupplyKind.Food || s.Kind == SupplyKind.Fuel)
                    {
                        float value = s.Kind == SupplyKind.Food ? house.FoodReserveDays : house.FuelReserveDays;
                        GUI.color = FamilyKit.Dim; Widgets.Label(new Rect(14f, cy, 80f, 22f), "WFL_FW_Keep".Translate()); GUI.color = Color.white;
                        float nv = Widgets.HorizontalSlider(new Rect(94f, cy + 2f, cv.width - 94f - 70f, 22f), value, 0.5f, 3f);
                        Widgets.Label(new Rect(cv.width - 64f, cy, 64f, 22f), nv.ToString("0.0") + " " + "WFL_FW_Unit_Days".Translate());
                        if (s.Kind == SupplyKind.Food) house.FoodReserveDays = nv; else house.FuelReserveDays = nv;
                        cy += 28f;
                    }
                    else if (s.Kind == SupplyKind.Medicine)
                    {
                        GUI.color = FamilyKit.Dim; Widgets.Label(new Rect(14f, cy, 80f, 22f), "WFL_FW_Keep".Translate()); GUI.color = Color.white;
                        int nv = Mathf.RoundToInt(Widgets.HorizontalSlider(new Rect(94f, cy + 2f, cv.width - 94f - 70f, 22f), house.MedicineReserve, 0, 10));
                        Widgets.Label(new Rect(cv.width - 64f, cy, 64f, 22f), nv + " " + "WFL_FW_Unit_Pieces".Translate());
                        house.MedicineReserve = nv;
                        cy += 28f;
                    }
                }
            }
            finally { GUI.enabled = saved; }
            FamilyKit.EndCard(card, cy, ref y);

            var policy = FamilyKit.BeginCard(col, ref y, "policies", "WFL_FW_Policies".Translate());
            var pv = policy.View;
            float py = 0f;
            saved = GUI.enabled;
            GUI.enabled = saved && may;
            try
            {
                var r1 = new Rect(0f, py, pv.width, 28f);
                Widgets.CheckboxLabeled(r1, "WFL_Pantry_Emergency".Translate(), ref house.EmergencySharing);
                TooltipHandler.TipRegion(r1, "WFL_FW_PolicyEmergencyTip".Translate());
                py += 32f;
                float h2 = Text.CalcHeight("WFL_Pantry_PublicFunds".Translate(), pv.width - 36f) + 6f;
                var r2 = new Rect(0f, py, pv.width, Mathf.Max(28f, h2));
                Widgets.CheckboxLabeled(r2, "WFL_Pantry_PublicFunds".Translate(), ref house.UsePublicBarterFunds);
                TooltipHandler.TipRegion(r2, "WFL_FW_PolicyFundsTip".Translate());
                py += r2.height + 4f;
            }
            finally { GUI.enabled = saved; }
            FamilyKit.EndCard(policy, py, ref y);
        }

        // ------------------------------------------------------------------ the family area

        private void DrawArea(Rect v, ref float y, FamilyComponent component, FamilyRegistry registry, HomeModel home)
        {
            var house = view.House;
            var col = new Rect(0f, 0f, Mathf.Min(v.width, 720f), 0f);
            var current = house.ZoneAreaId == 0 ? null : FamilyZones.AreaFor(house);
            var card = FamilyKit.BeginCard(col, ref y, "area", "WFL_FW_FamilyArea".Translate());
            var cv = card.View;
            float cy = 0f;
            if (current == null) cy += FamilyKit.Label(cv, cy, "WFL_ZoneNone".Translate(), 0f, true);
            else
            {
                Widgets.DrawBoxSolid(new Rect(0f, cy + 4f, 14f, 14f), current.Color);
                Widgets.Label(new Rect(22f, cy, cv.width - 22f, 22f), current.Label);
                cy += 26f;
                cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_Covers".Translate(), "WFL_FW_CoversN".Translate(home.Covered, home.Total));
            }
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_BedsAssigned".Translate(), "WFL_FW_BedsN".Translate(home.WithBed, home.Total));
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_Access".Translate(), "WFL_FW_AccessPolicy".Translate());
            cy += FamilyKit.KeyValue(cv, cy, "WFL_FW_MeetingPlace".Translate(), home.MeetingPlace.Length > 0 ? home.MeetingPlace : "WFL_Office_None".Translate().ToString());
            cy += 4f;

            if (home.Missing.Count > 0)
            {
                FamilyKit.SectionHeader(cv, ref cy, "WFL_FW_Missing".Translate());
                foreach (var m in home.Missing) cy += FamilyKit.Label(cv, cy, "- " + m, 6f);
            }
            else if (current != null) cy += FamilyKit.Label(cv, cy, "WFL_FW_NothingMissing".Translate(), 0f, true);
            FamilyKit.EndCard(card, cy, ref y);

            var actions = FamilyKit.BeginCard(col, ref y, "areaActions", "WFL_FW_AreaActions".Translate());
            var av = actions.View;
            float ay = 0f;
            float bw = Mathf.Min(260f, av.width);
            if (home.HasMap)
            {
                if (FamilyKit.PrimaryButton(new Rect(0f, ay, bw, 28f), (current == null ? "WFL_ZoneCreate" : "WFL_FW_EditArea").Translate(), MayManage(registry, house), WhyNot(registry, house))) { FamilyZones.StartPainting(); Close(); }
                ay += 34f;
                var free = Find.Maps.SelectMany(m => m.zoneManager.AllZones).OfType<Zone_Family>().Where(z => z.areaId == 0 || z.areaId != house.ZoneAreaId).ToList();
                if (free.Count > 0)
                {
                    if (HouseButton(new Rect(0f, ay, bw, 28f), "WFL_ZoneChoose".Translate(), registry, house))
                        Find.WindowStack.Add(new FloatMenu(free.Select(z => { var zone = z; return new FloatMenuOption(zone.label, () => zone.SetFamily(component, house)); }).ToList()));
                    ay += 34f;
                }
                if (current != null)
                {
                    if (HouseButton(new Rect(0f, ay, bw, 28f), "WFL_ZoneRemove".Translate(), registry, house))
                    {
                        foreach (var z in FamilyZones.ZonesOf(house).ToList()) z.areaId = 0;
                        FamilyZones.Release(component, house, current);
                    }
                    ay += 34f;
                }
                if (HouseButton(new Rect(0f, ay, bw, 28f), "WFL_Office_Set".Translate(), registry, house)) BeginMeetingPlace(registry, house);
                if (registry.OfficeOf(house.Id) != null && HouseButton(new Rect(bw + 6f, ay, 150f, 28f), "WFL_Office_Clear".Translate(), registry, house)) registry.ClearFamilyOffice(house.Id);
                ay += 34f;
            }
            else ay += FamilyKit.Label(av, ay, "WFL_FW_NoMap".Translate(), 0f, true);
            FamilyKit.EndCard(actions, ay, ref y);
        }

        private static void BeginMeetingPlace(FamilyRegistry registry, Lineage house)
        {
            int family = house.Id;
            Find.Targeter.BeginTargeting(TargetingParameters.ForCell(), target =>
            {
                var map = Find.CurrentMap; var h = registry.GetLineage(family); var area = FamilyZones.AreaFor(h);
                if (target.Cell.IsValid && map != null && target.Cell.InBounds(map) && target.Cell.Standable(map) && area?.Map == map && area[target.Cell] &&
                    registry.SetFamilyOffice(family, map.uniqueID, target.Cell.x, target.Cell.z, Activities.Now))
                    Messages.Message("WFL_Office_Saved".Translate(), MessageTypeDefOf.TaskCompletion, false);
                else Messages.Message("WFL_Office_Invalid".Translate(), MessageTypeDefOf.RejectInput, false);
            });
        }
    }
}
