using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Camping trips. A family may wish for one; the player decides, forms the caravan with the game's own screen and sends it. Steward only watches:
    /// a trip counts when people of the house are really at the chosen place, stationary, for the whole stay. It never creates, moves or removes a caravan.
    /// </summary>
    internal static class CampingDriver
    {
        private const int Day = ActivityTuning.TicksPerDay;

        internal static void Run(FamilyComponent c, int now)
        {
            var s = FamilyLifeMod.Settings;
            var r = c.Registry;
            r.MaintainCamps(now);
            foreach (var trip in r.CampTrips.Where(t => t.State == CampState.Authorized || t.State == CampState.Away).ToList()) Watch(c, r, trip, now);
            if (s != null && !s.camping) return;
            if (now - r.LastCampScanTick < Day) return;
            r.LastCampScanTick = now;
            Propose(c, r, now);
        }

        private static CampContext ContextFor(FamilyComponent c, int tile = -1) => new CampContext
        {
            Available = id => { var p = c.TryGetPawn(id); return p != null && p.Spawned && !p.Dead && !p.IsQuestLodger() && Governance.Free(c, id); },
            IsAvatar = id => Governance.IsAvatar(c, id),
            Skill = id => (SkillLevel(c.TryGetPawn(id), SkillDefOf.Plants) + SkillLevel(c.TryGetPawn(id), SkillDefOf.Animals)) / 2f,
            Profile = id => c.ProfileOf(id),
            Weather = tile < 0 ? 0f : WeatherAt(tile)
        };

        private static float SkillLevel(Pawn p, SkillDef def) { var s = def == null ? null : p?.skills?.GetSkill(def); return s == null || s.TotallyDisabled ? 0f : Math.Min(1f, s.Level / 20f); }

        private static void Propose(FamilyComponent c, FamilyRegistry r, int now)
        {
            var houses = r.Lineages.Where(h => r.IsActiveHouse(h.Id)).OrderBy(h => h.Id).ToList();
            if (houses.Count == 0) return;
            var house = houses[Math.Abs(r.CampCursor++ % houses.Count)];
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == house.ZoneMapId && m.IsPlayerHome) ?? Find.AnyPlayerHomeMap;
            if (map == null) return;
            var ctx = ContextFor(c);
            var motive = r.AssessCamp(house.Id, now, ctx);
            if (motive == null) return;
            int tile = PickTile(WorldTiles.HomeTile(map), unchecked(house.Id * 31 + now / Day), Math.Max(1, FamilyLifeMod.Settings?.campRadius ?? 4));
            if (tile < 0) return;
            var trip = r.ProposeCamp(house.Id, motive.Value, tile, now, ctx);
            if (trip == null) return;
            var organizer = c.TryGetPawn(trip.OrganizerId);
            Find.LetterStack.ReceiveLetter("WFL_Camp_LetterTitle".Translate(), "WFL_Camp_LetterText".Translate(organizer?.LabelShort ?? "?", ("WFL_CampMotive_" + trip.Motive).Translate(),
                r.LineageDisplayName(house)), LetterDefOf.NeutralEvent, WorldTiles.Target(tile));
        }

        /// <summary>A safe place within a few tiles of home: land, passable, empty, reachable, and not freezing or scorching.</summary>
        internal static int PickTile(int home, int seed, int radius)
        {
            if (home < 0 || Find.WorldGrid == null) return -1;
            var seen = new HashSet<int> { home };
            var frontier = new List<int> { home };
            var good = new List<int>();
            for (int depth = 0; depth < radius && frontier.Count > 0 && seen.Count < 400; depth++)
            {
                var next = new List<int>();
                foreach (int t in frontier)
                    foreach (int n in WorldTiles.Neighbours(t))
                    {
                        if (!seen.Add(n)) continue;
                        next.Add(n);
                        if (depth >= 1 && WorldTiles.Safe(home, n)) good.Add(n);
                    }
                frontier = next;
            }
            if (good.Count == 0) return -1;
            good.Sort();
            return good[System.Math.Abs(seed % good.Count)];
        }

        private static float WeatherAt(int tile)
        {
            float t = WorldTiles.Temperature(tile);
            float rain = WorldTiles.Rainfall(tile);
            float comfort = t >= 8f && t <= 28f ? 1f : t < -5f || t > 38f ? -1f : 0f;
            return Math.Max(-1f, Math.Min(1f, comfort - (rain > 2200f ? .3f : 0f)));
        }

        /// <summary>Player caravans that are sitting still at this tile.</summary>
        internal static IEnumerable<Caravan> CaravansAt(int tile)
            => Find.WorldObjects.Caravans.Where(cv => cv.IsPlayerControlled && WorldTiles.CaravanTile(cv) == tile && cv.pather != null && !cv.pather.Moving);

        private static void Watch(FamilyComponent c, FamilyRegistry r, CampTrip trip, int now)
        {
            var here = CaravansAt(trip.Tile).SelectMany(cv => cv.PawnsListForReading).Where(p => p != null && !p.Dead).Select(p => p.thingIDNumber).Distinct().ToList();
            if (trip.State == CampState.Authorized)
            {
                if (here.Count > 0 && r.BeginCampStay(trip.Id, here, now))
                    Messages.Message("WFL_Camp_Arrived".Translate(r.LineageDisplayName(r.GetLineage(trip.FamilyId))), WorldTiles.Target(trip.Tile), MessageTypeDefOf.PositiveEvent, false);
                return;
            }
            if (!r.AdvanceCamp(trip.Id, here, now, ContextFor(c, trip.Tile))) return;
            string house = r.LineageDisplayName(r.GetLineage(trip.FamilyId));
            if (trip.State == CampState.Completed)
            {
                Messages.Message((trip.Quality > .25f ? "WFL_Camp_Good" : trip.Quality < -.1f ? "WFL_Camp_Bad" : "WFL_Camp_Fine").Translate(house), WorldTiles.Target(trip.Tile), MessageTypeDefOf.NeutralEvent, false);
                foreach (var moment in trip.Moments.Take(3))
                    Messages.Message(("WFL_CampMoment_" + moment.Kind).Translate(FamilyUi.NameOf(r, moment.ActorId), FamilyUi.NameOf(r, moment.OtherId)), MessageTypeDefOf.NeutralEvent, false);
            }
            else Messages.Message("WFL_Camp_Cut".Translate(house), WorldTiles.Target(trip.Tile), MessageTypeDefOf.NeutralEvent, false);
        }

        // ---- the player's side of the card ----

        internal static void Authorize(FamilyComponent c, CampTrip trip)
        {
            if (!c.Registry.AuthorizeCamp(trip.Id, Activities.Now)) { Messages.Message("WFL_Camp_Unavailable".Translate(), MessageTypeDefOf.RejectInput, false); return; }
            WorldTiles.Jump(trip.Tile);
            Messages.Message("WFL_Camp_Authorized".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        internal static void OpenCaravanForm()
        {
            var map = Find.CurrentMap;
            if (map != null && map.IsPlayerHome) Find.WindowStack.Add(new Dialog_FormCaravan(map));
            else Messages.Message("WFL_Camp_NeedHomeMap".Translate(), MessageTypeDefOf.RejectInput, false);
        }
    }

}
