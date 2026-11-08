using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace FamilyLife
{
    /// <summary>
    /// Brings distant relatives to a grand reunion and sends them home. A relative is touched only at the moment they walk onto the map, and only if
    /// they are still safe to bring (alive, free to move, in a non-hostile faction, not a leader, not tied to a quest, not in a caravan or on another map).
    /// They leave the usual way, through the map edge, which hands them back to the world. A trip held up on the road is a call for help the player may answer.
    /// </summary>
    internal static class KinVisitDriver
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private static readonly HashSet<int> told = new HashSet<int>();

        internal static void Reset() => told.Clear();

        internal static void Run(FamilyComponent c, int now)
        {
            var r = c.Registry;
            var s = FamilyLifeMod.Settings;
            r.MaintainKinVisits(now);
            if (s == null || (s.ceremonies && s.visitingKin)) PlanNew(c, r, now);
            foreach (var visit in r.KinVisits.Where(v => v.Open).ToList())
            {
                try { Step(c, r, visit, now); }
                catch (Exception ex) { Log.Warning("[Wil's Family Life] Kin visit " + visit.Id + " skipped: " + ex.Message); }
            }
            foreach (var visit in r.KinVisits.Where(v => v.State == KinVisitState.Failed && told.Add(v.Id) && now - v.EndedTick < 2 * Day).ToList())
                Messages.Message("WFL_Kin_Failed".Translate(r.LineageDisplayName(r.GetLineage(visit.FamilyId))), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>The relatives the game confirms can safely come.</summary>
        internal static bool Eligible(Pawn p)
        {
            if (p == null || p.Dead || p.Destroyed || p.Spawned || p.Downed || p.InMentalState || p.IsPrisoner) return false;
            if (p.Faction == null || p.Faction == Faction.OfPlayer || p.Faction.HostileTo(Faction.OfPlayer) || p.Faction.leader == p) return false;
            if (!Find.WorldPawns.Contains(p) || p.GetCaravan() != null || p.IsQuestLodger() || QuestReserved(p)) return false;
            if (p.ageTracker.AgeBiologicalYears < 16 || p.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true) return false;
            return true;
        }

        private static bool QuestReserved(Pawn p)
        {
            foreach (var quest in Find.QuestManager.QuestsListForReading)
                if (quest.State == QuestState.Ongoing && quest.QuestLookTargets.Contains(p)) return true;
            return false;
        }

        private static void PlanNew(FamilyComponent c, FamilyRegistry r, int now)
        {
            foreach (var request in r.GatheringRequests.Where(q => q.Ceremony == CeremonyKind.GrandReunion && q.State == CeremonyState.Planned &&
                         q.VisitorIds.Count > 0 && r.PlanFor(q.Id)?.Open == false && r.KinVisitForRequest(q.Id) == null).Take(1).ToList())
            {
                var eligible = request.VisitorIds.Where(id => Eligible(c.TryGetPawn(id))).ToList();
                r.PlanKinVisit(request.Id, eligible, now);          // anyone not confirmed is left off the request, so this is tried once
            }
        }

        private static Map HomeMap(FamilyRegistry r, int family)
            => Find.Maps.FirstOrDefault(m => m.IsPlayerHome && m.uniqueID == r.GetLineage(family)?.ZoneMapId) ?? Find.AnyPlayerHomeMap;

        private static void Step(FamilyComponent c, FamilyRegistry r, KinVisit visit, int now)
        {
            switch (visit.State)
            {
                case KinVisitState.Travelling:
                    if (now < visit.ArriveTick) return;
                    if (visit.Complication) { Strand(r, visit, now); return; }
                    Arrive(c, r, visit, now);
                    break;
                case KinVisitState.Stranded:
                    var rescuers = CampingDriver.CaravansAt(visit.StrandTile).SelectMany(cv => cv.PawnsListForReading).Where(p => p != null && !p.Dead).Select(p => p.thingIDNumber).Distinct().ToList();
                    if (rescuers.Count > 0 && r.RescueKinVisit(visit.Id, rescuers, now))
                        Messages.Message("WFL_Kin_Rescued".Translate(r.LineageDisplayName(r.GetLineage(visit.FamilyId))), MessageTypeDefOf.PositiveEvent, false);
                    break;
                case KinVisitState.Landing:
                    FinishLanding(c, r, visit, now);
                    break;
                case KinVisitState.Arrived:
                    TendGuests(c, r, visit, now);
                    if (r.BeginKinLeaving(visit.Id, now)) SendHome(c, visit);
                    break;
                case KinVisitState.Leaving:
                    bool gone = !visit.ArrivedIds.Any(id => c.TryGetPawn(id) is Pawn p && p.Spawned && !p.Dead && p.Faction != Faction.OfPlayer);
                    if (gone || now > visit.StayEndTick + 2 * Day) { ReturnStragglers(c, visit); r.CompleteKinVisit(visit.Id, now); }
                    else SendHome(c, visit);
                    break;
            }
        }

        private static void Strand(FamilyRegistry r, KinVisit visit, int now)
        {
            var map = Find.AnyPlayerHomeMap;
            if (map == null) return;
            int tile = CampingDriver.PickTile(WorldTiles.HomeTile(map), visit.Seed, 7);
            if (tile < 0) { visit.Complication = false; return; }                                   // nowhere sensible to strand them: the trip simply goes well
            if (r.StrandKinVisit(visit.Id, tile, now))
                Find.LetterStack.ReceiveLetter("WFL_Kin_StrandedTitle".Translate(), "WFL_Kin_StrandedText".Translate(r.LineageDisplayName(r.GetLineage(visit.FamilyId)), (visit.RescueDeadlineTick - now) / Day),
                    LetterDefOf.ThreatSmall, WorldTiles.Target(tile));
        }

        private static void Arrive(FamilyComponent c, FamilyRegistry r, KinVisit visit, int now)
        {
            var map = HomeMap(r, visit.FamilyId);
            if (map == null) { if (now > visit.ArriveTick + 2 * Day) r.MarkKinArrived(visit.Id, new int[0], now); return; }
            if (!CellFinder.TryFindRandomEdgeCellWith(cell => cell.Standable(map) && map.reachability.CanReachColony(cell), map, CellFinder.EdgeRoadChance_Neutral, out var edge)) return;
            var placed = new List<Pawn>();
            // Relatives from a spacefaring faction come down by shuttle; everyone else comes on foot along the road, as they always did.
            var byShuttle = new List<Pawn>();
            foreach (int id in visit.KinIds)
            {
                var p = c.TryGetPawn(id);
                if (Eligible(p) && p.Faction.def.techLevel >= TechLevel.Spacer) byShuttle.Add(p);
            }
            bool landed = byShuttle.Count > 0 && TryShuttle(map, byShuttle);
            foreach (int id in visit.KinIds)
            {
                var p = c.TryGetPawn(id);
                if (landed && byShuttle.Contains(p)) { placed.Add(p); continue; }                      // already aboard and on their way down
                if (!Eligible(p)) continue;                                                           // checked again now: things change in a day
                Find.WorldPawns.RemovePawn(p);
                try { GenSpawn.Spawn(p, CellFinder.RandomClosewalkCellNear(edge, map, 8), map); placed.Add(p); }
                catch (Exception ex)
                {
                    Log.Warning("[Wil's Family Life] Could not place a visiting relative: " + ex.Message);
                    if (!p.Spawned) Find.WorldPawns.PassToWorld(p);          // never leave anyone in limbo
                }
            }
            if (landed)
            {
                if (!r.MarkKinLanding(visit.Id, placed.Select(p => p.thingIDNumber), byShuttle.Select(p => p.thingIDNumber), now)) return;
                var walking = placed.Where(p => p.Spawned).ToList();
                if (walking.Count > 0) Greet(walking, map, Day);
                return;
            }
            if (!r.MarkKinArrived(visit.Id, placed.Select(p => p.thingIDNumber), now)) return;
            AnnounceArrival(r, visit, placed.Where(p => p.Spawned).ToList(), map, false);
        }

        private static void FinishLanding(FamilyComponent c, FamilyRegistry r, KinVisit visit, int now)
        {
            var map = HomeMap(r, visit.FamilyId);
            if (map == null) return;
            var here = visit.ArrivedIds.Select(id => c.TryGetPawn(id)).Where(p => p != null && p.Spawned && !p.Dead && p.Map == map).ToList();
            bool waiting = visit.LandingIds.Any(id => c.TryGetPawn(id) is Pawn p && !p.Spawned && !p.Dead);
            if (waiting && now <= visit.ArriveTick + Day) return;
            if (!r.MarkKinArrived(visit.Id, here.Select(p => p.thingIDNumber), now)) return;
            AnnounceArrival(r, visit, here, map, true);
        }

        private static void AnnounceArrival(FamilyRegistry r, KinVisit visit, List<Pawn> guests, Map map, bool shuttle)
        {
            Greet(guests, map, Math.Max(Day, KinStayLeft(visit, Activities.Now)));
            if (guests.Count == 0) return;
            Find.LetterStack.ReceiveLetter("WFL_Kin_ArrivedTitle".Translate(), (shuttle ? "WFL_Kin_ArrivedShuttleText" : "WFL_Kin_ArrivedText").Translate(
                r.LineageDisplayName(r.GetLineage(visit.FamilyId)), string.Join(", ", guests.Select(p => p.LabelShort))), LetterDefOf.PositiveEvent, new LookTargets(guests));
        }

        /// <summary>
        /// A shuttle brings the relatives from the world and sets them down at the colony's landing spot, then leaves. They are not touched until the
        /// ship is made; if it cannot be made, nothing has changed and they come on foot instead.
        /// </summary>
        private static bool TryShuttle(Map map, List<Pawn> pawns)
        {
            try
            {
                var faction = pawns[0].Faction;
                IntVec3 spot = DropCellFinder.GetBestShuttleLandingSpot(map, faction);
                if (!spot.IsValid) return false;
                foreach (var p in pawns) if (Find.WorldPawns.Contains(p)) Find.WorldPawns.RemovePawn(p);
                var ship = TransportShipMaker.MakeTransportShip(TransportShipDefOf.Ship_Shuttle, pawns.Cast<Thing>().ToList());
                ship.ArriveAt(spot, map.Parent);
                ship.AddJobs(ShipJobDefOf.Unload, ShipJobDefOf.FlyAway);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning("[Wil's Family Life] Could not bring relatives by shuttle; they will walk in. " + ex.Message);
                foreach (var p in pawns) if (!p.Spawned && !Find.WorldPawns.Contains(p) && p.holdingOwner == null) Find.WorldPawns.PassToWorld(p);
                return false;
            }
        }

        private static int KinStayLeft(KinVisit visit, int now) => Math.Max(0, visit.StayEndTick - now);

        /// <summary>Guests wait near the colony as visitors do, until the reunion takes them in or the stay ends.</summary>
        private static void Greet(List<Pawn> guests, Map map, int ticks)
        {
            foreach (var group in guests.Where(g => g.Faction != null && g.GetLord() == null).GroupBy(g => g.Faction))
            {
                var spot = DropCellFinder.TradeDropSpot(map);
                LordMaker.MakeNewLord(group.Key, new LordJob_VisitColony(group.Key, spot, ticks), map, group);
            }
        }

        private static void TendGuests(FamilyComponent c, FamilyRegistry r, KinVisit visit, int now)
        {
            var map = HomeMap(r, visit.FamilyId);
            if (map == null) return;
            var idle = visit.ArrivedIds.Select(id => c.TryGetPawn(id)).Where(p => p != null && p.Spawned && !p.Dead && !p.Downed && p.Map == map && p.Faction != Faction.OfPlayer && p.GetLord() == null).ToList();
            if (idle.Count > 0) Greet(idle, map, Math.Max(2500, KinStayLeft(visit, now)));
        }

        private static void SendHome(FamilyComponent c, KinVisit visit)
        {
            foreach (int id in visit.ArrivedIds)
            {
                var p = c.TryGetPawn(id);
                if (p == null || !p.Spawned || p.Dead || p.Downed || p.Faction == null || p.Faction == Faction.OfPlayer) continue;     // a guest who joined the player is not ours to send away
                var lord = p.GetLord();
                if (lord?.LordJob is LordJob_ExitMapBest) continue;
                lord?.Notify_PawnLost(p, PawnLostCondition.ForcedToJoinOtherLord);
                LordMaker.MakeNewLord(p.Faction, new LordJob_ExitMapBest(LocomotionUrgency.Walk), p.Map, new[] { p });
            }
        }

        /// <summary>Anyone who ended up neither on the map nor in the world's keeping is handed back, so nobody is lost.</summary>
        private static void ReturnStragglers(FamilyComponent c, KinVisit visit)
        {
            foreach (int id in visit.ArrivedIds)
            {
                var p = c.TryGetPawn(id);
                if (p == null || p.Dead || p.Spawned || p.Faction == Faction.OfPlayer || Find.WorldPawns.Contains(p) || p.GetCaravan() != null || p.holdingOwner != null) continue;
                Find.WorldPawns.PassToWorld(p);
            }
        }

        /// <summary>When the reunion begins, guests already on the map join its lord so they take part like everyone else.</summary>
        internal static void JoinGathering(FamilyComponent c, GatheringRequest request, Pawn organizer)
        {
            if (request.VisitorIds.Count == 0) return;
            var lord = organizer.GetLord();
            if (lord == null) return;
            foreach (int id in request.VisitorIds)
            {
                var guest = c.TryGetPawn(id);
                if (guest == null || !guest.Spawned || guest.Dead || guest.Downed || guest.Map != organizer.Map || guest.Faction == Faction.OfPlayer) continue;
                guest.GetLord()?.Notify_PawnLost(guest, PawnLostCondition.ForcedToJoinOtherLord);
                lord.AddPawn(guest);
            }
        }
    }
}
