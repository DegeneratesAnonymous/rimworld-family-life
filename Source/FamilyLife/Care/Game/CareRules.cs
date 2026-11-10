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
    // What the doctor's routine work gives way to, and who can come to a shared room. Owner: Community Care.

    internal static class CareDemand
    {
        private const int CacheTicks = 120;
        private static readonly Dictionary<int, int[]> mapCache = new Dictionary<int, int[]>();          // map id -> [tick, level]

        internal static void Reset() { mapCache.Clear(); }

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
        internal static PatientResponse Assess(FamilyComponent c, Pawn doctor, Pawn patient, int now)
        {
            var r = c.Registry;
            bool unable = NeedsBedside(patient);
            string stop = PawnReadiness.StopReason(patient, now);
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
    }
}
