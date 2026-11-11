using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace FamilyLife
{
    // Whether a pawn should stop what a class or an appointment has them doing (a real need, an emergency, danger), and their skill and tiredness. Read on a short timer and cached,
    // so jobs may ask every tick. Owner: the framework; Education and Community Care both use it.

    internal static class PawnReadiness
    {
        private const int CacheTicks = 120, StopCacheTicks = 30;
        private static readonly Dictionary<int, StopEntry> stopCache = new Dictionary<int, StopEntry>();
        private sealed class StopEntry { public int Tick; public string Reason; }
        private static readonly Dictionary<int, int[]> dangerCache = new Dictionary<int, int[]>();

        internal static void Reset() { stopCache.Clear(); dangerCache.Clear(); }

        /// <summary>A threat on the map: no one goes to class or to a check-up while raiders are in the colony.</summary>
        internal static bool Danger(Map map)
        {
            if (map == null) return false;
            int now = Activities.Now;
            if (dangerCache.TryGetValue(map.uniqueID, out var c) && now >= c[0] && now - c[0] < CacheTicks) return c[1] == 1;          // read at most twice a game minute per map, not once per pawn
            bool danger = false;
            try { danger = GenHostility.AnyHostileActiveThreatToPlayer(map, false); } catch (Exception) { }
            dangerCache[map.uniqueID] = new[] { now, danger ? 1 : 0 };
            return danger;
        }

        /// <summary>Why this person should stop what a class or an appointment has them doing, or null. Recomputed every half second at most.</summary>
        internal static string StopReason(Pawn p, int now)
        {
            if (p == null || p.Dead) return "gone";
            if (stopCache.TryGetValue(p.thingIDNumber, out var e) && now >= e.Tick && now - e.Tick < StopCacheTicks) return e.Reason;
            string reason = null;
            try
            {
                if (p.Drafted) reason = "drafted";
                else if (p.InMentalState) reason = "mental break";
                else if (p.Downed) reason = "down";
                else
                {
                    reason = OfficeDriver.NeedsReason(p);
                    if (reason == null && p.health != null)
                    {
                        if (p.health.hediffSet.BleedRateTotal > .01f || HealthAIUtility.ShouldBeTendedNowByPlayer(p)) reason = "needs tending";
                        else if (HealthAIUtility.ShouldSeekMedicalRest(p)) reason = "needs rest";
                    }
                    if (reason == null && Danger(p.Map)) reason = "danger";
                }
            }
            catch (Exception) { }
            if (stopCache.Count > 200) stopCache.Clear();
            stopCache[p.thingIDNumber] = new StopEntry { Tick = now, Reason = reason };
            return reason;
        }

        /// <summary>A stop that is nobody's fault, so an absence for it is not held against them.</summary>
        internal static bool Excusable(string reason) => reason != null && reason != "gone";

        internal static int Level(Pawn p, SkillDef d) { var s = p?.skills?.GetSkill(d); return s == null || s.TotallyDisabled ? 0 : s.Level; }

        /// <summary>Tiredness counted against delivery: none when rested, more as rest runs out.</summary>
        internal static float Fatigue(Pawn p) { float rest = p?.needs?.rest?.CurLevelPercentage ?? .7f; return rest >= .5f ? 0f : (.5f - rest) * 6f; }
    }
}
