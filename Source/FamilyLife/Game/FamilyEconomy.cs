using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    [DefOf]
    public static class WFL_StoryDefOf
    {
        public static JobDef WFL_CollectSupply, WFL_ExchangeSupply, WFL_StealSupply, WFL_StoryTalk, WFL_StoryListen, WFL_PersonalTime, WFL_BarterFunds, WFL_ReconcileStory, WFL_HousePetition, WFL_HouseInvite, WFL_MarriageNegotiation, WFL_MarriageConsent, WFL_HouseUnionNegotiation, WFL_StewardPetition, WFL_StewardGuest, WFL_StewardMediate, WFL_StewardNegotiate, WFL_CoupRecruit, WFL_CoupConfront, WFL_HeistRecruit, WFL_CastVote, WFL_ProtectionOffer, WFL_OfficeLesson, WFL_Checkup, WFL_HallSit, WFL_HallServe, WFL_CastAssemblyVote, WFL_Comfort;
        static WFL_StoryDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(WFL_StoryDefOf)); }
    }

    /// <summary>One instance per game. All stock snapshots and planners are bounded, tick-driven and main-thread only.</summary>
    internal sealed class FamilyEconomy
    {
        private readonly FamilyComponent component;
        private FamilyRegistry R => component.Registry;
        private sealed class Snapshot
        {
            public readonly TickGate Refresh = new TickGate(2500, 300);
            public List<Thing> Things = new List<Thing>();
        }
        private sealed class Need
        {
            public SupplyKind Kind;
            public float Target, Urgency;
            public Pawn ForPawn;
            public ThingFilter Fuel;
            public ThingDef Clothing;
        }
        private readonly Dictionary<Map, Snapshot> stocks = new Dictionary<Map, Snapshot>();
        private readonly TickGate planner = new TickGate(600);
        private readonly SearchCursor maps = new SearchCursor(), families = new SearchCursor();
        private readonly Dictionary<int, int> lastFamily = new Dictionary<int, int>();
        private readonly HashSet<int> pressured = new HashSet<int>();
        private bool warned;
        internal FamilyEconomy(FamilyComponent component) { this.component = component; }
        internal bool Pressure(int family) => pressured.Contains(family);
        /// <summary>A bounded, cached reading of food physically held by a house. Used by daily ambitions, never by a tick loop.</summary>
        internal float FoodSecurity(Lineage family, List<Pawn> members, int now)
        {
            if (family == null || members == null || members.Count == 0) return 50f;
            var map = members.Select(p => p?.Map).FirstOrDefault(m => m != null);
            if (map == null) return 50f;
            var area = FamilyZones.AreaFor(family);
            float held = 0f;
            foreach (var thing in Stock(map, now))
            {
                if (KindOf(thing) != SupplyKind.Food) continue;
                var claim = R.ClaimFor(thing.thingIDNumber);
                if (claim?.HolderFamily != family.Id) continue;
                if (area != null && area.Map == map && !area[thing.Position]) continue;
                held += thing.stackCount * Units(thing, SupplyKind.Food);
            }
            float target = HouseholdPolicy.FoodTarget(members.Count, family.FoodReserveDays);
            if (target <= 0f) return 100f;
            if (held > 0f) return Math.Min(100f, held / target * 100f);
            // Before a family has claimed a pantry, current hunger prevents a false crisis while keeping the assessment conservative.
            float current = members.Where(p => p?.needs?.food != null).Select(p => p.needs.food.CurLevelPercentage).DefaultIfEmpty(.5f).Average() * 70f;
            if (current < 20f) return current; // observed severe hunger must outrank an unknown pantry
            return Math.Max(Pressure(family.Id) ? 15f : 35f, current);
        }
        internal void Invalidate(Map map) { if (map != null && stocks.TryGetValue(map, out var snapshot)) snapshot.Refresh.Invalidate(); }
        /// <summary>Stock the economy already tracks for a map, for planners that need to know what is physically there.</summary>
        internal List<Thing> StockOf(Map map, int now) => Stock(map, now);

        /// <summary>Starts a real theft of real goods: the actor walks to the item, carries it home, and the supply system records what happened.</summary>
        internal bool StartTheft(Pawn actor, Pawn donor, Thing source, int count, int now, SupplyApproach approach = SupplyApproach.Steal)
        {
            if (actor == null || donor == null || source == null || !source.Spawned || !actor.CanReserve(source) || !actor.CanReach(source, PathEndMode.Touch, Danger.None)) return false;
            var kind = KindOf(source);
            var home = FamilyZones.AreaFor(R.GetLineage(R.FamilyOf(actor.thingIDNumber)));
            if (home?.Map != actor.Map) return false;
            var need = new Need { Kind = kind, ForPawn = actor, Target = count, Urgency = 1f };
            return Start(actor, donor, source, count, need, approach, home, now);
        }

        /// <summary>A willing sale: the buyer walks to the seller's goods and pays in silver through the same barter flow as any trade.</summary>
        internal bool StartSale(Pawn buyer, Pawn seller, Thing source, int count, int now)
        {
            if (buyer == null || seller == null || source == null || !source.Spawned || !buyer.CanReserve(source) || !buyer.CanReach(source, PathEndMode.Touch, Danger.None)) return false;
            int price = Math.Max(1, (int)Math.Ceiling(source.GetStatValue(StatDefOf.MarketValue) * count));
            if (InventorySilver(buyer) < price) return false;
            var home = FamilyZones.AreaFor(R.GetLineage(R.FamilyOf(buyer.thingIDNumber)));
            if (home?.Map != buyer.Map) return false;
            var need = new Need { Kind = KindOf(source), ForPawn = buyer, Target = count, Urgency = .5f };
            return Start(buyer, seller, source, count, need, SupplyApproach.Barter, home, now);
        }

        private List<Thing> Stock(Map map, int now)
        {
            if (!stocks.TryGetValue(map, out var s)) stocks[map] = s = new Snapshot();
            if (s.Refresh.Take(now)) s.Things = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver).Where(t => t.Spawned && t.stackCount > 0).ToList();
            return s.Things;
        }
        internal bool MayUse(Pawn pawn, Thing thing)
        {
            if (!FamilyLifeMod.Settings.familySupplies || pawn == null || thing == null || pawn.CurJob?.playerForced == true || pawn.Drafted) return true;
            var claim = R.ClaimFor(thing.thingIDNumber);
            if (claim == null) return true;
            int family = R.FamilyOf(pawn.thingIDNumber);
            bool treatment = pawn.CurJobDef == JobDefOf.TendPatient && pawn.CurJob.targetA.Pawn is Pawn patient && (patient.Downed || patient.health.hediffSet.BleedRateTotal > 0);
            if (HouseholdPolicy.EmergencyAccess(R.GetLineage(claim.HolderFamily)?.EmergencySharing == true, KindOf(thing) == SupplyKind.Food, pawn.needs?.food?.CurLevel < .1f, thing.def.IsMedicine, treatment)) return true;
            if (family == claim.HolderFamily || R.BelongsToFamily(R.GetPerson(pawn.thingIDNumber),claim.HolderFamily)) return true;
            return (pawn.CurJobDef == WFL_StoryDefOf.WFL_ExchangeSupply || pawn.CurJobDef == WFL_StoryDefOf.WFL_StealSupply) && R.Requests.Any(r => r.RequesterId == pawn.thingIDNumber && r.SourceId == thing.thingIDNumber && r.Open &&
                (pawn.CurJobDef == WFL_StoryDefOf.WFL_ExchangeSupply || pawn.CurJobDef == WFL_StoryDefOf.WFL_StealSupply));
        }
        internal static SupplyKind KindOf(Thing t)
        {
            if (t.def == ThingDefOf.Silver) return SupplyKind.Currency;
            if (t is Apparel) return SupplyKind.Apparel;
            if (t.def.IsMedicine) return SupplyKind.Medicine;
            if (t.def.IsDrug) return SupplyKind.Drug;
            if ((t as ThingWithComps)?.GetComp<CompBook>() != null) return SupplyKind.Book;
            if (t.def.ingestible != null && t.GetStatValue(StatDefOf.Nutrition) > 0) return SupplyKind.Food;
            return SupplyKind.Fuel;
        }
        private static float Units(Thing t, SupplyKind kind) => kind == SupplyKind.Food ? Math.Max(0.001f, t.GetStatValue(StatDefOf.Nutrition)) : 1f;
        private bool Fits(Thing t, Need n)
        {
            if (!t.Spawned || t.Map != n.ForPawn.Map || t.Destroyed || t.stackCount <= 0 || t.IsForbidden(Faction.OfPlayer)) return false;
            if (n.Kind == SupplyKind.Fuel) return n.Fuel?.Allows(t) == true;
            if (KindOf(t) != n.Kind) return false;
            if (n.Kind == SupplyKind.Food) return n.ForPawn.RaceProps.CanEverEat(t) && FoodUtility.WillEat(n.ForPawn, t, null, false);
            if (n.Kind == SupplyKind.Apparel)
            {
                var a = t as Apparel;
                return a != null && ApparelUtility.HasPartsToWear(n.ForPawn, a.def) &&
                    (a.def.apparel.developmentalStageFilter & n.ForPawn.DevelopmentalStage) != 0 &&
                    n.ForPawn.outfits?.CurrentApparelPolicy?.filter.Allows(a) == true &&
                    (n.Clothing == null || a.def.apparel.layers.Intersect(n.Clothing.apparel.layers).Any());
            }
            if (n.Kind == SupplyKind.Drug) return n.ForPawn.drugs != null &&
                (AddictionUtility.IsAddicted(n.ForPawn, t) || AddictionUtility.HasChemicalDependency(n.ForPawn, t)) &&
                n.ForPawn.drugs.CurrentPolicy[t.def].allowedForAddiction;
            return true;
        }
        private IEnumerable<Need> Needs(List<Pawn> members, Area area)
        {
            var adult = members.First();
            var policy = R.GetLineage(R.FamilyOf(adult.thingIDNumber));
            yield return new Need { Kind = SupplyKind.Food, Target = HouseholdPolicy.FoodTarget(members.Count, policy?.FoodReserveDays ?? 1f), Urgency = members.Any(p => p.needs?.food?.CurLevel < 0.2f) ? 1f : 0.4f, ForPawn = adult };
            if (members.Any(p => p.health.HasHediffsNeedingTend() || (p.skills?.GetSkill(SkillDefOf.Medicine)?.Level ?? 0) >= 6))
                yield return new Need { Kind = SupplyKind.Medicine, Target = Math.Max(policy?.MedicineReserve ?? 2, members.Count(p => p.health.HasHediffsNeedingTend()) * 3), Urgency = members.Any(p => p.health.HasHediffsNeedingTend()) ? 0.9f : 0.3f, ForPawn = adult };
            foreach (var building in area.Map.listerBuildings.allBuildingsColonist.Where(b => area[b.Position]).Take(128))
            {
                var fuel = building.GetComp<CompRefuelable>();
                if (fuel != null && !fuel.Props.fuelIsMortarBarrel) yield return new Need { Kind = SupplyKind.Fuel, Fuel = fuel.Props.fuelFilter,
                    Target = Math.Max(1, fuel.Props.fuelConsumptionRate * Math.Max(.5f, Math.Min(3f,policy?.FuelReserveDays ?? 2f))), Urgency = fuel.Fuel <= 0 ? 0.7f : 0.25f, ForPawn = adult };
            }
            foreach (var p in members)
            {
                if (AddictionUtility.AddictedToAnything(p) || p.health.hediffSet.hediffs.Any(h => h.def.defName.IndexOf("Dependency", StringComparison.OrdinalIgnoreCase) >= 0))
                    yield return new Need { Kind = SupplyKind.Drug, Target = 3, Urgency = 0.6f, ForPawn = p };
                foreach (var worn in (p.apparel?.WornApparel ?? new List<Apparel>()).Take(4))
                    yield return new Need { Kind = SupplyKind.Apparel, Target = members.Count(m => !m.DevelopmentalStage.Juvenile() == !p.DevelopmentalStage.Juvenile()), Urgency = worn.HitPoints < worn.MaxHitPoints * 0.5f ? 0.5f : 0.1f, ForPawn = p, Clothing = worn.def };
            }
            if (component.Registry.GetLineage(R.FamilyOf(adult.thingIDNumber))?.CultureOf(Dim.Scholarly) > 22)
                yield return new Need { Kind = SupplyKind.Book, Target = 1, Urgency = 0.1f, ForPawn = adult };
        }
        private bool Destination(Pawn actor, Area home, Thing thing, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            foreach (var group in actor.Map.haulDestinationManager.AllGroupsListForReading)
                foreach (var c in group.CellsList)
                    if (home[c] && !c.IsForbidden(actor) && StoreUtility.IsGoodStoreCell(c, actor.Map, thing, actor, Faction.OfPlayer) && actor.CanReserve(c))
                    { cell = c; return true; }
            return false;
        }
        internal void Tick(int now)
        {
            if (!planner.Take(now)) return;
            using var measurement = component.Performance.Measure("Family story planner");
            try
            {
                StoryActions.CancelUnsafeAttacks(component);
                if (!FamilyLifeMod.Settings.familyStories) return;
                R.MaintainStories(now); R.MaintainSupplies(now);
                foreach (var gone in stocks.Keys.Where(m => !Find.Maps.Contains(m)).ToList()) stocks.Remove(gone);
                if (Find.Maps.Count == 0) return;
                var map = Find.Maps[maps.Take(Find.Maps.Count)];
                if (!map.IsPlayerHome) return;
                if ((now / 600) % 2 == 0 && StoryActions.TryPlan(component, map, now)) return;
                if (!FamilyLifeMod.Settings.familySupplies) return;
                var homes = R.LineagesWithZones().Where(l => FamilyZones.AreaFor(l)?.Map == map).ToList();
                if (homes.Count == 0) return;
                var family = homes[families.Take(homes.Count)];
                if (lastFamily.TryGetValue(family.Id, out int last) && now - last < 2500) return;
                lastFamily[family.Id] = now;
                var members = component.ZoneMemberIds(family).Select(component.TryGetPawn).Where(p => p != null && p.Spawned && p.Map == map && !p.Dead).ToList();
                var actor = members.FirstOrDefault(p => R.FamilyOf(p.thingIDNumber) == family.Id && !p.DevelopmentalStage.Juvenile() && Activities.IsFree(p) && p.CurJob?.playerForced != true && (p.needs?.rest == null || p.needs.rest.CurLevel >= 0.15f) && !HealthAIUtility.ShouldSeekMedicalRest(p) && p.carryTracker.CarriedThing == null);
                if (actor == null) return;
                var home = FamilyZones.AreaFor(family);
                var stock = Stock(map, now);
                if (TryRepayment(family, members, stock, now)) return;
                var homeAreas = homes.Select(l => new KeyValuePair<int, Area>(l.Id, FamilyZones.AreaFor(l))).ToList();
                var cellHomes = new Dictionary<IntVec3, int>();
                Func<IntVec3, int> householdAt = cell =>
                {
                    if (cellHomes.TryGetValue(cell, out int cached)) return cached;
                    int owner = 0;
                    foreach (var h in homeAreas.Where(h => h.Value != null && h.Value[cell]))
                    { if (owner != 0) { owner = 0; break; } owner = h.Key; }
                    cellHomes[cell] = owner; return owner;
                };
                pressured.Remove(family.Id);
                foreach (var need in Needs(members, home).OrderByDescending(n => n.Urgency).Take(24))
                {
                    var matching = stock.Where(t => Fits(t, need)).ToList();
                    float held = matching.Where(t => R.ClaimFor(t.thingIDNumber)?.HolderFamily == family.Id && home[t.Position]).Sum(t => t.stackCount * Units(t, need.Kind));
                    held += R.Requests.Where(r => R.FamilyOf(r.RequesterId) == family.Id && r.Kind == need.Kind && matching.Any(t => t.def.defName == r.Definition) && (r.Open || r.State == SupplyState.Exchanged && component.TryGetPawn(r.RequesterId)?.carryTracker?.CarriedThing?.thingIDNumber == r.SourceId)).Sum(r => r.Units);
                    if (held >= need.Target) continue;
                    pressured.Add(family.Id);
                    var misplaced = matching.FirstOrDefault(t => R.ClaimFor(t.thingIDNumber)?.HolderFamily == family.Id && !home[t.Position] && actor.CanReserve(t) && actor.CanReach(t, PathEndMode.Touch, Danger.None));
                    if (misplaced != null && Start(actor, null, misplaced, Math.Min(20, misplaced.stackCount), need, SupplyApproach.Relocate, home, now)) return;
                    var source = matching.Where(t => R.ClaimFor(t.thingIDNumber) == null && (householdAt(t.Position) == 0 || householdAt(t.Position) == family.Id) && StoreUtility.IsInAnyStorage(t))
                        .OrderBy(t => t.Position.DistanceToSquared(actor.Position)).FirstOrDefault(t => !t.Position.IsForbidden(actor) && actor.CanReserve(t) && actor.CanReach(t, PathEndMode.Touch, Danger.None));
                    if (source != null)
                    {
                        float publicTotal = matching.Where(t => R.ClaimFor(t.thingIDNumber) == null).Sum(t => t.stackCount * Units(t, need.Kind));
                        float reserve = need.Kind == SupplyKind.Food ? map.mapPawns.FreeColonistsSpawned.Count * 0.8f : (need.Kind == SupplyKind.Medicine ? 2 : 0);
                        int count = Math.Min(source.stackCount, Math.Min(20, (int)Math.Ceiling((need.Target - held) / Units(source, need.Kind))));
                        count = Math.Min(count, Math.Max(0, (int)Math.Floor((publicTotal - reserve) / Units(source, need.Kind))));
                        if (count > 0 && Start(actor, null, source, count, need, SupplyApproach.PublicClaim, home, now)) return;
                        continue; // usable public supplies exist: do not manufacture a feud over a stocking reserve
                    }
                    // Ask nearby households; inspecting their inventory only supplies the donor's response, not a thief's omniscience.
                    foreach (var donor in map.mapPawns.FreeColonistsSpawned.Where(p => R.FamilyOf(p.thingIDNumber) != family.Id && R.FamilyOf(p.thingIDNumber) != 0 && !p.DevelopmentalStage.Juvenile() && Activities.IsFree(p) && p.CurJob?.playerForced != true).OrderBy(p => p.Position.DistanceToSquared(actor.Position)).Take(3))
                    {
                        var offered = matching.FirstOrDefault(t => R.ClaimFor(t.thingIDNumber)?.HolderFamily == R.FamilyOf(donor.thingIDNumber));
                        if (offered == null || !actor.CanReserve(offered) || offered.Position.IsForbidden(actor)) continue;
                        int count = Math.Min(offered.stackCount, Math.Min(5, (int)Math.Ceiling((need.Target - held) / Units(offered, need.Kind))));
                        var approach = R.ClaimFor(offered.thingIDNumber).OwnerFamily == family.Id ? SupplyApproach.Return :
                            ((R.GetBond(donor.thingIDNumber, actor.thingIDNumber)?.Trust ?? 0) >= 35 ? SupplyApproach.Loan : SupplyApproach.Ask);
                        int price = Math.Max(1, (int)Math.Ceiling(offered.GetStatValue(StatDefOf.MarketValue) * count));
                        if (approach != SupplyApproach.Return && InventorySilver(actor) >= price) approach = SupplyApproach.Barter;
                        else if (approach != SupplyApproach.Return && TryGetBarterFunds(actor, price, stock, family)) return;
                        bool refused = R.Requests.Any(r => r.RequesterId == actor.thingIDNumber && r.DonorId == donor.thingIDNumber && r.Kind == need.Kind && r.State == SupplyState.Refused && now - r.CreatedTick < ActivityTuning.TicksPerDay);
                        bool visible = offered.Position.DistanceTo(actor.Position) <= 12 && GenSight.LineOfSight(actor.Position, offered.Position, map);
                        float hostility = R.GetTie(family.Id, R.FamilyOf(donor.thingIDNumber))?.Grievance ?? 0;
                        if (refused && FamilyLifeMod.Settings.familyTheft && visible && (need.Urgency >= 0.9f || need.Urgency >= 0.6f && hostility >= 20 || actor.story.traits.allTraits.Any(t => t.def.defName.IndexOf("Klepto", StringComparison.OrdinalIgnoreCase) >= 0))) approach = SupplyApproach.Steal;
                        else if (refused && need.Urgency >= 0.7f && (component.ProfileOf(actor.thingIDNumber)?[(int)Dim.Warmth] ?? 0) < -0.5f) approach = SupplyApproach.Threat;
                        else if (refused) continue;
                        if (Start(actor, donor, offered, count, need, approach, home, now)) return;
                    }
                }
            }
            catch (Exception ex) { if (!warned) { warned = true; Log.Warning("[Wil's Family Life] Family supply planner skipped: " + ex); } }
        }
        internal bool OfferAid(Pawn donor,int family,bool byPlayer=false)
        {
            if(!FamilyLifeMod.Settings.familyStories || !FamilyLifeMod.Settings.familySupplies || donor==null || !donor.Spawned || !byPlayer && (!Activities.IsFree(donor) || donor.CurJob?.playerForced==true) || R.FamilyOf(donor.thingIDNumber)==0 || family==R.FamilyOf(donor.thingIDNumber)) return false;
            var recipient=component.TryGetPawn(R.HeadOf(family));var home=FamilyZones.AreaFor(R.GetLineage(family));
            if(recipient?.Map!=donor.Map || home?.Map!=donor.Map || !Activities.IsFree(recipient) || recipient.CurJob?.playerForced==true) return false;
            var need=new Need{Kind=SupplyKind.Food,ForPawn=recipient,Target=1.6f,Urgency=.4f};
            int own=R.FamilyOf(donor.thingIDNumber);
            var source=Stock(donor.Map,Activities.Now).FirstOrDefault(t=>Fits(t,need) && R.ClaimFor(t.thingIDNumber)?.HolderFamily==own && R.ClaimFor(t.thingIDNumber)?.OwnerFamily==own && recipient.CanReserve(t));
            return source!=null && Start(recipient,donor,source,Math.Min(source.stackCount,Math.Max(1,(int)System.Math.Ceiling(1.6f/Units(source,SupplyKind.Food)))),need,SupplyApproach.Offer,home,Activities.Now);
        }
        internal bool RequestAid(Pawn requester,int donorFamily,bool byPlayer=false)
        {
            if(!FamilyLifeMod.Settings.familyStories || !FamilyLifeMod.Settings.familySupplies || requester==null || !requester.Spawned || !byPlayer && (!Activities.IsFree(requester) || requester.CurJob?.playerForced==true) || R.FamilyOf(requester.thingIDNumber)==0 || donorFamily==R.FamilyOf(requester.thingIDNumber)) return false;
            var home=FamilyZones.AreaFor(R.GetLineage(R.FamilyOf(requester.thingIDNumber)));
            var donor=component.TryGetPawn(R.HeadOf(donorFamily));
            if(home?.Map!=requester.Map || donor?.Map!=requester.Map || !Activities.IsFree(donor) || donor.CurJob?.playerForced==true) return false;
            var need=new Need{Kind=SupplyKind.Food,ForPawn=requester,Target=1.6f,Urgency=.6f};
            var source=Stock(requester.Map,Activities.Now).FirstOrDefault(t=>Fits(t,need) && R.ClaimFor(t.thingIDNumber)?.HolderFamily==donorFamily && requester.CanReserve(t));
            return source!=null && Start(requester,donor,source,Math.Min(source.stackCount,Math.Max(1,(int)System.Math.Ceiling(1.6f/Units(source,SupplyKind.Food)))),need,SupplyApproach.Ask,home,Activities.Now);
        }
        private bool TryRepayment(Lineage family, List<Pawn> members, List<Thing> stock, int now)
        {
            foreach (var debt in R.Requests.Where(r => R.FamilyOf(r.RequesterId) == family.Id && r.Approach == SupplyApproach.Loan && !r.DebtPaid && (r.State == SupplyState.Exchanged || r.State == SupplyState.Stored) && now-r.CreatedTick >= ActivityTuning.TicksPerDay && now-r.RepaymentAttemptTick >= ActivityTuning.TicksPerDay/2).Take(4).ToList())
            {
                var borrower = members.FirstOrDefault(p => p.thingIDNumber == debt.RequesterId);
                if (borrower == null || !Activities.IsFree(borrower) || borrower.CurJob?.playerForced == true || !Activities.CanSpareAttention(borrower) || R.Requests.Any(r => r.DebtId == debt.Id && r.Open)) continue;
                var creditor = component.TryGetPawn(debt.DonorId);
                if (creditor?.Map != borrower.Map || !Activities.IsFree(creditor) || creditor.CurJob?.playerForced == true || !Activities.CanSpareAttention(creditor)) continue;
                var home = FamilyZones.AreaFor(R.GetLineage(R.FamilyOf(creditor.thingIDNumber)));
                if (home?.Map != borrower.Map) continue;
                var need = new Need{Kind=debt.Kind,ForPawn=creditor};
                if (debt.Kind==SupplyKind.Fuel)
                {
                    var def=DefDatabase<ThingDef>.GetNamedSilentFail(debt.Definition); if(def==null) continue;
                    need.Fuel=new ThingFilter();need.Fuel.SetAllow(def,true);
                }
                var source = stock.FirstOrDefault(t => t.Spawned && t.Map==borrower.Map && t.def.defName==debt.Definition && Fits(t,need) && R.ClaimFor(t.thingIDNumber)?.HolderFamily==family.Id && R.ClaimFor(t.thingIDNumber)?.OwnerFamily==family.Id);
                if(source==null) continue;
                int count=debt.Kind==SupplyKind.Food ? (int)Math.Ceiling(debt.Units/Units(source,debt.Kind)) : debt.Count;
                if(count<=0 || source.stackCount<count || creditor.carryTracker.AvailableStackSpace(source.def)<count || !creditor.CanReserve(source)) continue;
                float held=stock.Where(t=>t.Spawned && t.Map==borrower.Map && KindOf(t)==debt.Kind && R.ClaimFor(t.thingIDNumber)?.HolderFamily==family.Id).Sum(t=>t.stackCount*Units(t,debt.Kind));
                float reserve=debt.Kind==SupplyKind.Food ? HouseholdPolicy.FoodTarget(members.Count,family.FoodReserveDays) : debt.Kind==SupplyKind.Medicine ? family.MedicineReserve : debt.Count;
                if(held-count*Units(source,debt.Kind)<reserve) continue;
                debt.RepaymentAttemptTick=now;
                if(Start(creditor,borrower,source,count,need,SupplyApproach.Repay,home,now,debt.Id)) return true;
            }
            return false;
        }
        private bool TryGetBarterFunds(Pawn pawn, int price, List<Thing> stock, Lineage family)
        {
            if (price<=0 || price>pawn.carryTracker.AvailableStackSpace(ThingDefOf.Silver) || InventorySilver(pawn)>=price) return false;
            int publicSilver=stock.Where(t=>t.Spawned && t.Map==pawn.Map && t.def==ThingDefOf.Silver && R.ClaimFor(t.thingIDNumber)==null).Sum(t=>t.stackCount);
            var source=stock.Where(t=>t.Spawned && t.Map==pawn.Map && t.def==ThingDefOf.Silver && t.stackCount>=price && !t.IsForbidden(pawn) )
                .FirstOrDefault(t=> (R.ClaimFor(t.thingIDNumber)?.OwnerFamily==family.Id && R.ClaimFor(t.thingIDNumber)?.HolderFamily==family.Id || family.UsePublicBarterFunds && StoreUtility.IsInAnyStorage(t) && R.ClaimFor(t.thingIDNumber)==null && publicSilver-price>=100) && pawn.CanReserve(t) && pawn.CanReach(t,PathEndMode.Touch,Danger.None));
            if(source==null) return false;
            var job=JobMaker.MakeJob(WFL_StoryDefOf.WFL_BarterFunds,source);job.count=price;
            pawn.jobs.StartJob(job,JobCondition.InterruptForced,null,false,tag:JobTag.Misc);return true;
        }
        private bool Start(Pawn actor, Pawn donor, Thing source, int count, Need need, SupplyApproach approach, Area home, int now, int debtId = 0)
        {
            count = Math.Min(count, actor.carryTracker.AvailableStackSpace(source.def));
            if (count <= 0 || !Destination(actor, home, source, out var storage)) return false;
            var door = donor == null || approach == SupplyApproach.Steal ? storage : DoorFor(actor, donor);
            if (!door.IsValid) return false;
            var request = R.OpenSupplyRequest(actor.thingIDNumber, donor?.thingIDNumber ?? 0, source.thingIDNumber, actor.Map.uniqueID, count, need.Kind, approach, now);
            if (request == null) return false;
            request.DebtId = debtId;
            request.HomeX = storage.x; request.HomeZ = storage.z; request.DoorX = door.x; request.DoorZ = door.z;
            request.Definition = source.def.defName; request.Units = count * Units(source, need.Kind);
            request.Price = Math.Max(1, (int)Math.Ceiling(source.GetStatValue(StatDefOf.MarketValue) * count));
            if (approach == SupplyApproach.Return) request.RootIncidentId = R.Incidents.LastOrDefault(i => i.Kind == IncidentKind.Theft && i.VictimFamily == R.FamilyOf(actor.thingIDNumber) && i.Resource == source.def.defName && !i.Resolved)?.Id ?? 0;
            var def = approach == SupplyApproach.PublicClaim || approach == SupplyApproach.Relocate ? WFL_StoryDefOf.WFL_CollectSupply : (approach == SupplyApproach.Steal ? WFL_StoryDefOf.WFL_StealSupply : WFL_StoryDefOf.WFL_ExchangeSupply);
            var job = JobMaker.MakeJob(def, source, donor, door); job.count = count;
            actor.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            return true;
        }
        private IntVec3 DoorFor(Pawn actor, Pawn donor)
        {
            var home = FamilyZones.AreaFor(R.GetLineage(R.FamilyOf(donor.thingIDNumber)));
            if (home == null || home.Map != actor.Map) return IntVec3.Invalid;
            int checkedCells = 0;
            foreach (var c in home.ActiveCells.OrderBy(c => c.DistanceToSquared(actor.Position)))
            {
                if (++checkedCells > 128) break;
                foreach (var edge in GenAdj.CellsAdjacent8Way(c, Rot4.North, IntVec2.One))
                    if (edge.InBounds(actor.Map) && !home[edge] && edge.Standable(actor.Map) && !edge.IsForbidden(actor) && actor.CanReach(edge, PathEndMode.OnCell, Danger.None) && donor.CanReach(c, PathEndMode.OnCell, Danger.None)) return edge;
            }
            return IntVec3.Invalid;
        }
        internal static int InventorySilver(Pawn pawn) => pawn.inventory?.innerContainer.Where(t => t.def == ThingDefOf.Silver).Select(t => t.stackCount).DefaultIfEmpty(0).Max() ?? 0;
        internal bool Pay(Pawn actor, Pawn donor, int price)
        {
            // Reserve one complete physical payment. Splitting several stacks and dropping them can charge a partial price.
            var silver = actor.inventory?.innerContainer.FirstOrDefault(t => t.def == ThingDefOf.Silver && t.stackCount >= price && (R.ClaimFor(t.thingIDNumber)==null || R.ClaimFor(t.thingIDNumber).OwnerFamily==R.FamilyOf(actor.thingIDNumber) && R.ClaimFor(t.thingIDNumber).HolderFamily==R.FamilyOf(actor.thingIDNumber)));
            if (silver == null || donor.inventory == null) return false;
            var payment = silver.SplitOff(price);
            int owner = R.ClaimFor(payment.thingIDNumber)?.OwnerFamily ?? 0, holder = R.ClaimFor(payment.thingIDNumber)?.HolderFamily ?? 0;
            R.ClaimStock(payment.thingIDNumber, R.FamilyOf(donor.thingIDNumber), R.FamilyOf(donor.thingIDNumber), donor.Map.uniqueID, SupplyKind.Currency);
            if (donor.inventory.innerContainer.TryAdd(payment, false)) return true;
            R.ForgetStock(payment.thingIDNumber);
            if (owner != 0) R.ClaimStock(payment.thingIDNumber, owner, holder, actor.Map.uniqueID, SupplyKind.Currency);
            actor.inventory.innerContainer.TryAdd(payment, false);
            return false;
        }
        internal float DonorNeed(Pawn donor, SupplyRequest request)
        {
            if (request.Kind == SupplyKind.Food)
            {
                var members = donor.Map.mapPawns.FreeColonistsSpawned.Where(p => R.FamilyOf(p.thingIDNumber) == R.FamilyOf(donor.thingIDNumber)).ToList();
                if (members.Any(p => p.needs?.food?.CurLevel < 0.3f)) return 1f;
                float reserve = Stock(donor.Map, Activities.Now).Where(t => t.Spawned && KindOf(t) == SupplyKind.Food && R.ClaimFor(t.thingIDNumber)?.HolderFamily == R.FamilyOf(donor.thingIDNumber)).Sum(t => t.stackCount * Units(t, SupplyKind.Food));
                return reserve - request.Units < members.Count * 0.8f ? 1f : 0.2f;
            }
            if (request.Kind == SupplyKind.Medicine && donor.Map.mapPawns.FreeColonistsSpawned.Any(p => R.FamilyOf(p.thingIDNumber) == R.FamilyOf(donor.thingIDNumber) && p.health.HasHediffsNeedingTend())) return 1f;
            return 0.2f;
        }
    }
}
