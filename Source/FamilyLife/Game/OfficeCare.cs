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
    // What the doctor's and teacher's routine work has to give way to, and what makes a person ready to take part. Everything here is read on a timer or from a cache:
    // the jobs ask often, the answers are recomputed rarely.

    internal static class CareDemand
    {
        private const int CacheTicks = 120, StopCacheTicks = 30;
        private static readonly Dictionary<int, int[]> mapCache = new Dictionary<int, int[]>();          // map id -> [tick, level]
        private static readonly Dictionary<int, StopEntry> stopCache = new Dictionary<int, StopEntry>();
        private sealed class StopEntry { public int Tick; public string Reason; }

        internal static void Reset() { mapCache.Clear(); stopCache.Clear(); dangerCache.Clear(); }

        /// <summary>
        /// The most urgent medical need on the map: someone down or bleeding (emergency), or someone who should be tended now (ongoing care). Read at most twice a game
        /// minute per map. Routine check-ups yield to both; an outbreak assessment yields to both too.
        /// </summary>
        internal static CareLevel MedicalNeed(Map map, int now)
        {
            if (map == null) return CareLevel.None;
            if (mapCache.TryGetValue(map.uniqueID, out var c) && now >= c[0] && now - c[0] < CacheTicks) return (CareLevel)c[1];
            var level = CareLevel.None;
            try
            {
                int looked = 0;
                foreach (var list in new IEnumerable<Pawn>[] { map.mapPawns.FreeColonistsSpawned, map.mapPawns.PrisonersOfColonySpawned })
                    foreach (var p in list)
                    {
                        if (++looked > 160) break;
                        if (p.Dead || p.health == null) continue;
                        if (HealthAIUtility.ShouldBeTendedNowByPlayerUrgent(p) || p.Downed && p.health.hediffSet.BleedRateTotal > .01f) { level = CareLevel.Emergency; goto done; }
                        if (HealthAIUtility.ShouldBeTendedNowByPlayer(p)) level = CareLevel.Ongoing;
                    }
            }
            catch (Exception) { }
            done:
            mapCache[map.uniqueID] = new[] { now, (int)level };
            return level;
        }

        /// <summary>The doctor does tending when there is some, so a routine visit gives way. A colonist who is not set to doctor does not hold up their own work for it.</summary>
        internal static bool DoctorShouldTend(Pawn doctor, int now)
        {
            if (doctor?.Map == null || doctor.WorkTypeIsDisabled(WorkTypeDefOf.Doctor)) return false;
            if (doctor.workSettings != null && !doctor.workSettings.WorkIsActive(WorkTypeDefOf.Doctor)) return false;
            return CarePriority.TendingFirst(MedicalNeed(doctor.Map, now));
        }

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
        private static readonly Dictionary<int, int[]> dangerCache = new Dictionary<int, int[]>();

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
    }

    internal static class CareRules
    {
        /// <summary>Whether this patient can come to a shared room: someone ill, bleeding, down or told to rest is visited, not summoned (and is never carried through the colony).</summary>
        internal static bool NeedsBedside(Pawn p)
        {
            if (p?.health == null) return false;
            try
            {
                return p.Downed || p.health.hediffSet.BleedRateTotal > .01f || HealthAIUtility.ShouldBeTendedNowByPlayer(p) || HealthAIUtility.ShouldSeekMedicalRest(p)
                    || OfficeEvents.IsSick(p) || p.InBed() && p.CurrentBed()?.Medical == true;
            }
            catch (Exception) { return false; }
        }

        private static float BondValue(FamilyRegistry r, int from, int to, Func<Bond, float> pick) { var b = r.BondsFrom(from).FirstOrDefault(x => x.To == to); return b == null ? 0f : pick(b); }

        /// <summary>
        /// Whether the patient will take part in a routine appointment: from how they feel about this doctor, how they are, and who they are. Kinship makes a person a little more
        /// willing and never less. Nothing else about relatedness matters.
        /// </summary>
        internal static Willingness Assess(FamilyComponent c, Pawn doctor, Pawn patient, int now)
        {
            var r = c.Registry;
            bool unable = NeedsBedside(patient);
            string stop = CareDemand.StopReason(patient, now);
            bool busy = stop != null && stop != "needs tending" && stop != "needs rest" && stop != "down" || patient.CurJob?.playerForced == true && !OfficeDriver.Forced;
            float trust = BondValue(r, patient.thingIDNumber, doctor.thingIDNumber, b => b.Trust), tension = BondValue(r, patient.thingIDNumber, doctor.thingIDNumber, b => b.Tension), attach = BondValue(r, patient.thingIDNumber, doctor.thingIDNumber, b => b.Attachment);
            float mood = patient.needs?.mood?.CurLevelPercentage ?? .6f;
            int personality = 0;
            try
            {
                var t = patient.story?.traits;
                if (t != null) { if (t.HasTrait(TraitDefOf.Kind)) personality += 4; }
            }
            catch (Exception) { }
            float reluctance = CareParticipation.Reluctance(trust, tension, attach, 0f, mood, r.KinOfHolder(patient.thingIDNumber, OfficeKind.Healer), personality);
            if (OfficeDriver.Forced) reluctance = 0f;          // a developer test is not held up by who happens to be in a mood
            return CareParticipation.Decide(unable, busy, reluctance, Rand.RangeInclusive(0, 99));
        }

        /// <summary>The room and the doctor's own state, summed, for the quality of a check-up. Bounded by <see cref="CheckupQuality"/>.</summary>
        internal static float Modifiers(Pawn doctor, Building_Bed bed, bool inClinic)
        {
            float m = 0f;
            try
            {
                if (!inClinic || bed == null) m -= .5f;          // a visit in someone's room has less to work with
                else
                {
                    if (bed.Medical) m += .5f;
                    var room = bed.GetRoom();
                    if (room != null)
                    {
                        if (room.Role == RoomRoleDefOf.Hospital) m += .5f;
                        float clean = room.GetStat(RoomStatDefOf.Cleanliness);
                        m += clean >= .4f ? .5f : clean < -.6f ? -1f : 0f;
                    }
                }
                float rest = doctor.needs?.rest?.CurLevelPercentage ?? .7f;
                if (rest < .3f) m -= 1f; else if (rest > .6f) m += .25f;          // preparation: a doctor who is rested and has been getting on with it
            }
            catch (Exception) { }
            return m;
        }

        internal static int Level(Pawn p, SkillDef d) { var s = p?.skills?.GetSkill(d); return s == null || s.TotallyDisabled ? 0 : s.Level; }

        /// <summary>Tiredness counted against delivery: none when rested, more as rest runs out.</summary>
        internal static float Fatigue(Pawn p) { float rest = p?.needs?.rest?.CurLevelPercentage ?? .7f; return rest >= .5f ? 0f : (.5f - rest) * 6f; }
    }
}
