using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace FamilyLife
{
    public static class WFL_RescueDefOf
    {
        public static WorldObjectDef WFL_KinRescueSite;
        static WFL_RescueDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(WFL_RescueDefOf)); }
    }

    /// <summary>
    /// The quest-and-site side of a stranded reunion party. The registry's kin visit is the one authoritative record; a real quest in the Quest tab and a real world site
    /// are how the player meets it. Every path that ends the rescue (success, a declined or ignored offer, a lapsed deadline, an abandoned or destroyed site, a lost party)
    /// asks the registry first, and only the registry decides which ending counts: a second report is simply refused. Relatives are only ever moved between the world's
    /// keeping and the rescue map, one instance each, and are handed back to the world whenever the map goes away.
    /// </summary>
    internal static class KinRescueDriver
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private static readonly TickGate siteGate = new TickGate(250);
        private static readonly HashSet<int> told = new HashSet<int>();

        internal static void Reset() { siteGate.Invalidate(); told.Clear(); }

        // ------------------------------------------------------------------ offering

        /// <summary>The trip is held up: make the offer. Nothing happens to the relatives yet; they stay exactly where the world keeps them.</summary>
        internal static void Strand(FamilyComponent c, FamilyRegistry r, KinVisit visit, int now)
        {
            var map = Find.AnyPlayerHomeMap;
            if (map == null) return;
            int tile = CampingDriver.PickTile(WorldTiles.HomeTile(map), visit.Seed, 7);
            if (tile < 0) { visit.Complication = false; return; }                                   // nowhere sensible to strand them: the trip simply goes well
            if (r.StrandKinVisit(visit.Id, tile, now)) Offer(c, r, visit, now);
        }

        /// <summary>Makes the offer and the quest for a held-up party. Also catches parties stranded by earlier versions, which had a destination but no quest.</summary>
        internal static void Offer(FamilyComponent c, FamilyRegistry r, KinVisit visit, int now)
        {
            if (visit.State != KinVisitState.Stranded || visit.Rescue != RescueStage.None || visit.StrandTile < 0) return;
            if (visit.RescueDeadlineTick - now < Day) return;                                      // too little time left to be worth offering; the old rules end it
            var trouble = FamilyRegistry.ChooseTrouble(visit.Seed, TroubleWeights(c, visit));
            if (!r.OfferKinRescue(visit.Id, trouble, now)) return;
            var quest = MakeQuest(c, r, visit, now);
            if (quest == null) { r.FailKinRescue(visit.Id, RescueStage.Declined, now); return; }   // the game could not make it: the reunion goes ahead rather than wait on nothing
            r.BindKinRescueQuest(visit.Id, quest.id);
        }

        private static bool Spacefaring(FamilyComponent c, KinVisit visit)
            => visit.KinIds.Select(c.TryGetPawn).Any(p => p?.Faction != null && p.Faction.def.techLevel >= TechLevel.Spacer);

        /// <summary>What can plausibly go wrong where they are: the cold or the heat, the wildlife around, whether anyone hostile is about. Nothing impossible is offered.</summary>
        private static Dictionary<RescueComplication, int> TroubleWeights(FamilyComponent c, KinVisit visit)
        {
            var w = new Dictionary<RescueComplication, int> { [RescueComplication.BrokenTransport] = 3, [RescueComplication.Injury] = 2 };
            try
            {
                float temp = WorldTiles.Temperature(visit.StrandTile);
                if (temp <= -5f || temp >= 36f) w[RescueComplication.Weather] = 3; else if (temp <= 2f || temp >= 32f) w[RescueComplication.Weather] = 1;
                float animals = WorldTiles.AnimalDensity(visit.StrandTile);
                if (animals >= .5f) w[RescueComplication.Animals] = 3; else if (animals >= .15f) w[RescueComplication.Animals] = 1;
                if (Find.FactionManager.AllFactionsVisible.Any(f => !f.defeated && f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction)) w[RescueComplication.Raiders] = 2;
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Could not read the conditions for a rescue; keeping it simple. " + ex.Message); }
            return w;
        }

        private static Quest MakeQuest(FamilyComponent c, FamilyRegistry r, KinVisit visit, int now)
        {
            try
            {
                var root = DefDatabase<QuestScriptDef>.GetNamedSilentFail("WFL_KinRescue");
                if (root == null) return null;
                string house = r.LineageDisplayName(r.GetLineage(visit.FamilyId));
                var names = visit.KinIds.Select(id => FamilyUi.NameOf(r, id)).ToList();
                string high = Spacefaring(c, visit) ? "high" : "low";
                var quest = Quest.MakeRaw();
                quest.root = root;
                quest.name = "WFL_Quest_Name".Translate(house);
                quest.description = "WFL_Quest_Text".Translate(house, string.Join(", ", names), ("WFL_Rescue_Trouble_" + visit.Trouble + "_" + high).Translate(), Mathf.Max(1, (visit.OfferExpiryTick - now) / Day), WorldTileDistance(visit));
                quest.points = 0f; quest.challengeRating = 1;
                quest.acceptanceExpireTick = visit.OfferExpiryTick + 1;
                quest.AddPart(new QuestPart_KinRescue { visitId = visit.Id });
                quest.AddPart(new QuestPart_Delay
                {
                    inSignalEnable = quest.InitiateSignal, delayTicks = FamilyRegistry.KinSiteTicks, isBad = true,
                    expiryInfoPart = "QuestExpiresIn", expiryInfoPartTip = "QuestExpiresOn"
                });
                quest.SetNotYetAccepted();
                Find.QuestManager.Add(quest);
                QuestUtility.SendLetterQuestAvailable(quest);
                return quest;
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Could not make the rescue quest: " + ex.Message); return null; }
        }

        private static string WorldTileDistance(KinVisit visit)
        {
            var home = Find.AnyPlayerHomeMap;
            if (home == null || visit.StrandTile < 0) return "?";
            float days = Mathf.Max(0.1f, (float)(Find.WorldGrid.ApproxDistanceInTiles(home.Tile, WorldTiles.Native(visit.StrandTile)) * 0.6f));
            return days.ToString("0.#");
        }

        internal static Quest QuestOf(KinVisit visit) => visit.QuestId == 0 ? null : Find.QuestManager.QuestsListForReading.FirstOrDefault(q => q.id == visit.QuestId);

        // ------------------------------------------------------------------ signals from the quest

        /// <summary>The player accepted the quest: the world site appears. Doing this twice, or after the offer lapsed, changes nothing.</summary>
        internal static void OnAccepted(int visitId)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; var visit = r?.KinVisitFor(visitId);
            if (visit == null || visit.Rescue != RescueStage.Offered) return;
            int now = Find.TickManager.TicksGame;
            if (now > visit.OfferExpiryTick) return;
            int tile = visit.StrandTile;
            if (tile < 0 || Find.WorldObjects.AnyWorldObjectAt(WorldTiles.Native(tile)))
            {
                var home = Find.AnyPlayerHomeMap;
                tile = home == null ? -1 : CampingDriver.PickTile(WorldTiles.HomeTile(home), visit.Seed + 1, 7);
            }
            if (tile < 0) { r.FailKinRescue(visit.Id, RescueStage.SiteLost, now); return; }
            var site = (WorldObject_KinRescue)WorldObjectMaker.MakeWorldObject(WFL_RescueDefOf.WFL_KinRescueSite);
            site.Tile = WorldTiles.Native(tile); site.visitId = visit.Id; site.trouble = visit.Trouble;
            Find.WorldObjects.Add(site);
            if (!r.AcceptKinRescue(visit.Id, site.ID, now)) { site.Destroy(); return; }
            visit.StrandTile = tile;
            Messages.Message("WFL_Rescue_SiteAppeared".Translate(r.LineageDisplayName(r.GetLineage(visit.FamilyId)), (visit.RescueDeadlineTick - now) / Day), site, MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>The quest ended, however it ended. If the registry still thinks the rescue is open, this decides how.</summary>
        internal static void OnQuestEnded(int visitId, Quest quest)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; var visit = r?.KinVisitFor(visitId);
            if (visit == null || !visit.RescueOpen) return;
            int now = Find.TickManager.TicksGame;
            var why = visit.Rescue == RescueStage.Offered ? (quest.State == QuestState.EndedOfferExpired ? RescueStage.Expired : RescueStage.Declined) : RescueStage.Abandoned;
            r.FailKinRescue(visit.Id, why, now);
        }

        internal static void OnSiteRemoved(int visitId)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; var visit = r?.KinVisitFor(visitId);
            if (visit == null || !visit.RescueOpen || visit.Rescue == RescueStage.Offered) return;
            r.FailKinRescue(visit.Id, RescueStage.SiteLost, Find.TickManager.TicksGame);
        }

        internal static void OnReached(WorldObject_KinRescue site, Caravan caravan)
        {
            var c = FamilyComponent.Current; var r = c?.Registry;
            if (r == null || !r.ReachKinRescue(site.visitId, Find.TickManager.TicksGame)) return;
            Messages.Message("WFL_Rescue_Reached".Translate(r.LineageDisplayName(r.GetLineage(r.KinVisitFor(site.visitId)?.FamilyId ?? 0))), MessageTypeDefOf.NeutralEvent, false);
        }

        // ------------------------------------------------------------------ the hourly reconciliation

        /// <summary>Brings quests, sites and the registry back into agreement. Everything here is safe to repeat.</summary>
        internal static void Reconcile(FamilyComponent c, FamilyRegistry r, int now)
        {
            using (c.Performance.Measure("Kin rescue: reconcile"))
            {
                foreach (var visit in r.KinVisits.Where(v => v.State == KinVisitState.Stranded && v.Rescue == RescueStage.None).ToList()) Offer(c, r, visit, now);
                foreach (var visit in r.KinVisits.Where(v => v.RescueOpen).ToList())
                {
                    var quest = QuestOf(visit);
                    if (quest == null || quest.State >= QuestState.EndedUnknownOutcome)
                    {
                        // The quest is gone or finished without the registry hearing: decide the ending now.
                        var why = visit.Rescue == RescueStage.Offered ? (quest?.State == QuestState.EndedOfferExpired ? RescueStage.Expired : RescueStage.Declined) : RescueStage.Abandoned;
                        r.FailKinRescue(visit.Id, why, now);
                    }
                    else if (visit.Rescue == RescueStage.Offered && quest.State == QuestState.Ongoing) OnAccepted(visit.Id);      // the accept signal was missed (a save reloaded in between)
                }
                foreach (var visit in r.KinVisits.Where(v => v.QuestId != 0 && !v.RescueOpen).ToList()) FinishQuest(visit);
                CleanSites(r);
            }
        }

        private static void FinishQuest(KinVisit visit)
        {
            var quest = QuestOf(visit);
            if (quest != null && !quest.Historical && quest.State <= QuestState.Ongoing)
                quest.End(visit.Rescue == RescueStage.Succeeded ? QuestEndOutcome.Success : QuestEndOutcome.Fail, false, false);
        }

        /// <summary>A site whose rescue is over is retired once no player pawn is on its map; a site no rescue knows about is removed.</summary>
        private static void CleanSites(FamilyRegistry r)
        {
            foreach (var site in Find.WorldObjects.AllWorldObjects.OfType<WorldObject_KinRescue>().ToList())
            {
                var visit = r.KinVisitFor(site.visitId);
                if (visit != null && visit.RescueOpen) continue;
                site.resolved = true;
                if (!site.HasMap) site.Destroy();
            }
        }

        // ------------------------------------------------------------------ while the caravan is there

        internal static void Tick(FamilyComponent c, int now)
        {
            var r = c.Registry;
            if (r.KinVisits.Count == 0 || !r.KinVisits.Any(v => v.Rescue == RescueStage.OnSite) || !siteGate.Take(now)) return;
            using (c.Performance.Measure("Kin rescue: site"))
                foreach (var visit in r.KinVisits.Where(v => v.State == KinVisitState.Stranded && v.Rescue == RescueStage.OnSite).ToList())
                {
                    try { TickSite(c, r, visit, now); }
                    catch (Exception ex) { Log.Warning("[Wil's Family Life] Rescue " + visit.Id + " skipped: " + ex.Message); }
                }
        }

        private static float Exposure(Pawn p)
        {
            var hypo = p.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Hypothermia)?.Severity ?? 0f;
            var heat = p.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.Heatstroke)?.Severity ?? 0f;
            return Mathf.Max(hypo, heat);
        }

        private static void TickSite(FamilyComponent c, FamilyRegistry r, KinVisit visit, int now)
        {
            var site = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_KinRescue>().FirstOrDefault(s => s.visitId == visit.Id);
            var map = site?.Map;
            if (map == null) { r.LeaveKinRescue(visit.Id); return; }
            var all = visit.KinIds.Select(id => (id, pawn: c.TryGetPawn(id))).ToList();
            var lost = all.Where(x => x.pawn == null || x.pawn.Dead || x.pawn.Destroyed).Select(x => x.id).ToList();
            var living = all.Where(x => x.pawn != null && !x.pawn.Dead && x.pawn.Spawned && x.pawn.Map == map).Select(x => x.pawn).ToList();
            var colonists = map.mapPawns.FreeColonistsSpawned.Where(p => !p.Downed).ToList();
            var near = colonists.Where(cp => living.Any(k => k.Position.InHorDistOf(cp.Position, 9f))).ToList();
            foreach (var p in near) if (site.rescuers.Count < 6 && !site.rescuers.Contains(p.thingIDNumber) && r.GetPerson(p.thingIDNumber)?.InColony == true) site.rescuers.Add(p.thingIDNumber);
            site.contactTicks = near.Count > 0 ? site.contactTicks + 250 : site.contactTicks;
            if (living.Count == 0 && lost.Count > 0) { Resolve(c, r, visit, site, lost, now); return; }
            bool hostile = map.mapPawns.AllPawnsSpawned.Any(p => !p.Dead && !p.Downed && p.Faction != Faction.OfPlayer && p.HostileTo(Faction.OfPlayer) && !visit.KinIds.Contains(p.thingIDNumber));
            bool needsAid = living.Any(k => k.Downed || k.health.HasHediffsNeedingTend());
            bool exposed = living.Any(k => Exposure(k) > .25f);
            bool ok;
            switch (site.trouble)
            {
                case RescueComplication.Injury: ok = site.contactTicks >= 250 && !needsAid; break;
                case RescueComplication.Animals:
                case RescueComplication.Raiders: ok = site.contactTicks >= 250 && !hostile; break;
                case RescueComplication.Weather: ok = site.contactTicks >= 1250 && !exposed; break;
                default: ok = site.contactTicks >= 2500; break;
            }
            ok &= !living.Any(k => k.Downed);                                                   // nobody who still needs carrying is sent on
            if (ok && living.Count > 0) Resolve(c, r, visit, site, lost, now);
        }

        private static void Resolve(FamilyComponent c, FamilyRegistry r, KinVisit visit, WorldObject_KinRescue site, List<int> lost, int now)
        {
            string house = r.LineageDisplayName(r.GetLineage(visit.FamilyId));
            var rescuers = site.rescuers.ToList();
            site.resolved = true;
            var map = site.Map;
            if (!r.CompleteKinRescue(visit.Id, rescuers, lost, now)) return;
            if (visit.Rescue == RescueStage.Succeeded)
            {
                KinRescueMap.HandBack(site, map);                                                   // back into the world's keeping; they walk in later, once, on the road or by shuttle
                Messages.Message("WFL_Rescue_Succeeded".Translate(house, string.Join(", ", rescuers.Select(id => FamilyUi.NameOf(r, id)))), MessageTypeDefOf.PositiveEvent, false);
            }
            else Messages.Message("WFL_Rescue_Lost".Translate(house), MessageTypeDefOf.NegativeEvent, false);
            FinishQuest(visit);
        }

        // ------------------------------------------------------------------ testing

        /// <summary>Developer shortcut: brings one ending forward through the normal paths (the quest, the registry, the site), never by writing a result.</summary>
        internal static string Force(FamilyComponent c, KinVisit visit, string how, int now)
        {
            var r = c.Registry;
            var quest = QuestOf(visit);
            var site = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_KinRescue>().FirstOrDefault(s => s.visitId == visit.Id);
            switch (how)
            {
                case "complete":
                    if (visit.Rescue == RescueStage.Offered && quest != null && quest.State == QuestState.NotYetAccepted) { quest.Accept(null); site = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_KinRescue>().FirstOrDefault(s => s.visitId == visit.Id); }
                    if (visit.Rescue == RescueStage.Accepted) r.ReachKinRescue(visit.Id, now);
                    var rescuers = r.Persons.Where(p => p.InColony && !p.Juvenile).OrderBy(p => p.Id).Take(2).Select(p => p.Id).ToList();
                    if (site != null) site.resolved = true;
                    if (!r.CompleteKinRescue(visit.Id, rescuers, null, now)) { if (site != null) site.resolved = false; return "The rescue could not be completed (" + Describe(visit) + ")."; }
                    if (site?.Map != null) KinRescueMap.HandBack(site, site.Map);
                    FinishQuest(visit);
                    return "Rescue completed. " + Describe(visit);
                case "expire":
                    visit.OfferExpiryTick = now - 1; visit.RescueDeadlineTick = now - 1;
                    r.MaintainKinVisits(now); Reconcile(c, r, now);
                    return "Rescue expired. " + Describe(visit);
                default:
                    if (quest != null && quest.State <= QuestState.Ongoing) quest.End(QuestEndOutcome.Fail, false, false);
                    r.FailKinRescue(visit.Id, visit.Rescue == RescueStage.Offered ? RescueStage.Declined : RescueStage.Abandoned, now);
                    Reconcile(c, r, now);
                    return "Rescue abandoned. " + Describe(visit);
            }
        }

        internal static string Describe(KinVisit v) => "visit " + v.Id + " " + v.State + "/" + v.Rescue + " trouble=" + v.Trouble + " quest=" + v.QuestId + " site=" + v.SiteId + " kin=" + v.KinIds.Count + " lost=" + v.LostIds.Count;
    }

    /// <summary>The quest's own record of which kin visit it belongs to. It reports; the registry decides.</summary>
    public class QuestPart_KinRescue : QuestPart
    {
        public int visitId;

        public override void Notify_QuestSignalReceived(Signal signal)
        {
            base.Notify_QuestSignalReceived(signal);
            if (quest != null && signal.tag == quest.InitiateSignal) KinRescueDriver.OnAccepted(visitId);
        }

        public override void Cleanup()
        {
            base.Cleanup();
            if (quest != null) KinRescueDriver.OnQuestEnded(visitId, quest);
        }

        public override bool QuestPartReserves(Pawn p)
        {
            var visit = FamilyComponent.Current?.Registry.KinVisitFor(visitId);
            return visit != null && visit.RescueOpen && visit.KinIds.Contains(p.thingIDNumber);
        }

        public override IEnumerable<GlobalTargetInfo> QuestLookTargets
        {
            get
            {
                foreach (var t in base.QuestLookTargets) yield return t;
                var c = FamilyComponent.Current; var visit = c?.Registry.KinVisitFor(visitId);
                if (visit == null) yield break;
                foreach (int id in visit.KinIds.Take(6)) { var p = c.TryGetPawn(id); if (p != null && !p.Discarded) yield return new GlobalTargetInfo(p); }
                if (visit.SiteId != 0)
                {
                    var site = Find.WorldObjects.AllWorldObjects.FirstOrDefault(o => o.ID == visit.SiteId);
                    if (site != null) yield return new GlobalTargetInfo(site);
                }
                else if (visit.StrandTile >= 0) yield return WorldTiles.Target(visit.StrandTile);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref visitId, "visitId");
        }
    }

    /// <summary>The place where the party is stranded. A caravan can go there; the map is made when it arrives, and the relatives are placed on it from the world's keeping.</summary>
    public class WorldObject_KinRescue : MapParent
    {
        public int visitId, contactTicks;
        public RescueComplication trouble;
        public bool resolved;
        public List<int> rescuers = new List<int>();

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref visitId, "visitId"); Scribe_Values.Look(ref trouble, "trouble", RescueComplication.BrokenTransport);
            Scribe_Values.Look(ref resolved, "resolved"); Scribe_Values.Look(ref contactTicks, "contactTicks");
            Scribe_Collections.Look(ref rescuers, "rescuers", LookMode.Value);
            if (rescuers == null) rescuers = new List<int>();
        }

        public override void PostMapGenerate()
        {
            base.PostMapGenerate();
            KinRescueMap.Populate(this, Map);
        }

        public override bool ShouldRemoveMapNow(out bool alsoRemoveWorldObject)
        {
            alsoRemoveWorldObject = resolved;
            return !Map.mapPawns.AnyPawnBlockingMapRemoval;
        }

        public override void Notify_MyMapAboutToBeRemoved()
        {
            base.Notify_MyMapAboutToBeRemoved();
            KinRescueMap.HandBack(this, Map);
            FamilyComponent.Current?.Registry.LeaveKinRescue(visitId);
        }

        public override void PostRemove()
        {
            base.PostRemove();
            if (!resolved) KinRescueDriver.OnSiteRemoved(visitId);
        }

        public override string GetInspectString()
        {
            var c = FamilyComponent.Current; var visit = c?.Registry.KinVisitFor(visitId);
            string baseText = base.GetInspectString();
            if (visit == null || !visit.RescueOpen) return baseText;
            string line = "WFL_Rescue_Inspect".Translate(Mathf.Max(0, (visit.RescueDeadlineTick - Find.TickManager.TicksGame) / ActivityTuning.TicksPerDay));
            return string.IsNullOrEmpty(baseText) ? line : baseText + "\n" + line;
        }

        public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan)
        {
            foreach (var option in base.GetFloatMenuOptions(caravan)) yield return option;
            if (resolved) yield break;
            foreach (var option in CaravanArrivalAction_VisitKinRescue.GetFloatMenuOptions(caravan, this)) yield return option;
        }
    }

    public class CaravanArrivalAction_VisitKinRescue : CaravanArrivalAction
    {
        private WorldObject_KinRescue site;

        public CaravanArrivalAction_VisitKinRescue() { }
        public CaravanArrivalAction_VisitKinRescue(WorldObject_KinRescue site) { this.site = site; }

        public override string Label => "WFL_Rescue_Approach".Translate();
        public override string ReportString => "WFL_Rescue_Approaching".Translate();

        public override FloatMenuAcceptanceReport StillValid(Caravan caravan, PlanetTile destinationTile)
        {
            var report = base.StillValid(caravan, destinationTile);
            if (!report) return report;
            if (site != null && site.Tile != destinationTile) return false;
            return CanVisit(caravan, site);
        }

        public static FloatMenuAcceptanceReport CanVisit(Caravan caravan, WorldObject_KinRescue site)
            => site != null && site.Spawned && !site.resolved && caravan != null && caravan.IsPlayerControlled;

        public static IEnumerable<FloatMenuOption> GetFloatMenuOptions(Caravan caravan, WorldObject_KinRescue site)
            => CaravanArrivalActionUtility.GetFloatMenuOptions(() => CanVisit(caravan, site), () => new CaravanArrivalAction_VisitKinRescue(site), "WFL_Rescue_Approach".Translate(), caravan, site.Tile, site);

        public override void Arrived(Caravan caravan)
        {
            if (!site.HasMap) LongEventHandler.QueueLongEvent(() => DoEnter(caravan), "GeneratingMapForNewEncounter", false, null);
            else DoEnter(caravan);
        }

        private void DoEnter(Caravan caravan)
        {
            var map = GetOrGenerateMapUtility.GetOrGenerateMap(site.Tile, new IntVec3(110, 1, 110), null);
            CaravanEnterMapUtility.Enter(caravan, map, CaravanEnterMode.Edge, CaravanDropInventoryMode.DoNotDrop, false);
            KinRescueDriver.OnReached(site, caravan);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_References.Look(ref site, "site");
        }
    }

    /// <summary>Puts the relatives on the rescue map, applies the trouble, and gives them back to the world when the map goes.</summary>
    internal static class KinRescueMap
    {
        internal static void Populate(WorldObject_KinRescue site, Map map)
        {
            var c = FamilyComponent.Current; var r = c?.Registry; var visit = r?.KinVisitFor(site.visitId);
            if (visit == null || !visit.RescueOpen || map == null) { site.resolved = true; return; }
            int now = Find.TickManager.TicksGame;
            var pawns = new List<Pawn>(); var unavailable = new List<int>();
            foreach (int id in visit.KinIds.ToList())
            {
                var p = c.TryGetPawn(id);
                if (p == null || p.Dead) { unavailable.Add(id); continue; }
                if (p.Spawned && p.Map == map) { pawns.Add(p); continue; }
                if (!KinVisitDriver.Eligible(p)) { unavailable.Add(id); continue; }            // hostile now, captured, a leader, in a caravan, bound to another quest: left out, not touched
                pawns.Add(p);
            }
            if (unavailable.Count > 0) r.RemoveKinFromVisit(visit.Id, unavailable, now);
            if (!visit.RescueOpen || pawns.Count == 0) { site.resolved = true; return; }
            var spot = map.Center;
            CellFinder.TryFindRandomCellNear(map.Center, map, 14, cell => cell.Standable(map) && !cell.Fogged(map) && cell.GetEdifice(map) == null, out spot);
            var placed = new List<Pawn>();
            foreach (var p in pawns)
            {
                try
                {
                    if (!p.Spawned)
                    {
                        if (Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p);
                        GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(spot, map, 4), map);
                    }
                    placed.Add(p);
                }
                catch (Exception ex)
                {
                    Log.Warning("[Wil's Family Life] Could not place " + p.LabelShort + " on the rescue map: " + ex.Message);
                    if (!p.Spawned && !Find.WorldPawns.Contains(p) && p.holdingOwner == null) Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever);
                }
            }
            if (placed.Count == 0) return;
            site.trouble = ApplyTrouble(site, map, placed, spot) ?? RescueComplication.BrokenTransport;
        }

        /// <summary>Applies the trouble to the map. Anything that cannot be set up is reported as a plain breakdown, so the rescue can always be completed.</summary>
        private static RescueComplication? ApplyTrouble(WorldObject_KinRescue site, Map map, List<Pawn> kin, IntVec3 spot)
        {
            try
            {
                switch (site.trouble)
                {
                    case RescueComplication.Injury:
                        HealthUtility.DamageUntilDowned(kin[0], false);
                        return RescueComplication.Injury;
                    case RescueComplication.Weather:
                        foreach (var k in kin)
                        {
                            var def = WorldTiles.Temperature(WorldTiles.Id(map.Tile)) < 15f ? HediffDefOf.Hypothermia : HediffDefOf.Heatstroke;
                            var hediff = HediffMaker.MakeHediff(def, k); hediff.Severity = .3f; k.health.AddHediff(hediff);
                        }
                        return RescueComplication.Weather;
                    case RescueComplication.Animals:
                        return SpawnAnimals(map, spot) ? RescueComplication.Animals : RescueComplication.BrokenTransport;
                    case RescueComplication.Raiders:
                        return SpawnRaiders(map, spot) ? RescueComplication.Raiders : RescueComplication.BrokenTransport;
                    default:
                        SpawnWreck(map, spot, kin[0]);
                        return RescueComplication.BrokenTransport;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[Wil's Family Life] Could not set up the trouble at the rescue site: " + ex.Message);
                return RescueComplication.BrokenTransport;
            }
        }

        private static void SpawnWreck(Map map, IntVec3 spot, Pawn kin)
        {
            bool spacer = kin.Faction != null && kin.Faction.def.techLevel >= TechLevel.Spacer;
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(spacer ? "ShipChunk" : "Campfire");
            if (def == null) return;
            var cell = CellFinder.RandomClosewalkCellNear(spot, map, 6);
            if (cell.Standable(map) && cell.GetEdifice(map) == null) GenSpawn.Spawn(ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.WoodLog : null), cell, map);
        }

        private static bool SpawnAnimals(Map map, IntVec3 spot)
        {
            if (!ManhunterPackGenStepUtility.TryGetAnimalsKind(180f, map.Tile, out var kind)) return false;
            int count = Mathf.Clamp(Mathf.RoundToInt(180f / Mathf.Max(20f, kind.combatPower)), 1, 6);
            var animals = new List<Pawn>();
            for (int i = 0; i < count; i++) animals.Add(PawnGenerator.GeneratePawn(kind));
            if (!CellFinder.TryFindRandomEdgeCellWith(cell => cell.Standable(map) && cell.DistanceTo(spot) > 20f, map, CellFinder.EdgeRoadChance_Ignore, out var edge)) return false;
            foreach (var animal in animals)
            {
                GenSpawn.Spawn(animal, CellFinder.RandomClosewalkCellNear(edge, map, 6), map, Rot4.Random);
                animal.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.ManhunterPermanent);
            }
            return true;
        }

        private static bool SpawnRaiders(Map map, IntVec3 spot)
        {
            var faction = Find.FactionManager.AllFactionsVisible.Where(f => !f.defeated && f.HostileTo(Faction.OfPlayer) && f.def.humanlikeFaction).OrderBy(f => f.loadID).FirstOrDefault();
            if (faction == null) return false;
            var request = new PawnGroupMakerParms { groupKind = PawnGroupKindDefOf.Combat, tile = map.Tile, faction = faction, points = 140f, generateFightersOnly = true };
            var raiders = PawnGroupMakerUtility.GeneratePawns(request).ToList();
            if (raiders.Count == 0) return false;
            if (!CellFinder.TryFindRandomEdgeCellWith(cell => cell.Standable(map) && cell.DistanceTo(spot) > 25f, map, CellFinder.EdgeRoadChance_Hostile, out var edge)) { foreach (var p in raiders) p.Destroy(); return false; }
            foreach (var p in raiders) GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(edge, map, 6), map);
            LordMaker.MakeNewLord(faction, new LordJob_AssaultColony(faction, false, false), map, raiders);
            return true;
        }

        /// <summary>Living relatives still on the map go back into the world's keeping, one instance each, off any lord.</summary>
        internal static void HandBack(WorldObject_KinRescue site, Map map)
        {
            var c = FamilyComponent.Current; var visit = c?.Registry.KinVisitFor(site.visitId);
            if (visit == null || map == null) return;
            foreach (int id in visit.KinIds.ToList())
            {
                var p = c.TryGetPawn(id);
                if (p == null || p.Dead || p.Destroyed || !p.Spawned || p.Map != map || p.Faction == Faction.OfPlayer) continue;
                p.GetLord()?.Notify_PawnLost(p, PawnLostCondition.ForcedToJoinOtherLord);
                p.DeSpawn();
                if (!Find.WorldPawns.Contains(p)) Find.WorldPawns.PassToWorld(p, PawnDiscardDecideMode.KeepForever);
            }
        }
    }
}
