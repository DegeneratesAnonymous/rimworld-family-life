using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>Builds one aggregate house snapshot at a time; no map cells or pawn-pair matrices are retained.</summary>
    internal static class HouseSituationPlanner
    {
        private static readonly SkillDef[] capabilitySkills =
        {
            SkillDefOf.Medicine, SkillDefOf.Cooking, SkillDefOf.Plants, SkillDefOf.Construction, SkillDefOf.Crafting,
            SkillDefOf.Intellectual, SkillDefOf.Social, SkillDefOf.Social, SkillDefOf.Shooting, SkillDefOf.Animals
        };
        private static readonly HouseCapability[] capabilities =
        {
            HouseCapability.Medical, HouseCapability.Cooking, HouseCapability.Growing, HouseCapability.Construction, HouseCapability.Crafting,
            HouseCapability.Intellectual, HouseCapability.Social, HouseCapability.Childcare, HouseCapability.Combat, HouseCapability.Animals
        };

        internal static SkillDef PreferredSkill(HouseCapability capability, Pawn mentor = null)
        {
            if (capability == HouseCapability.Combat && mentor != null)
                return (mentor.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0) >= (mentor.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0) ? SkillDefOf.Shooting : SkillDefOf.Melee;
            int index = Array.IndexOf(capabilities, capability);
            return index < 0 ? null : capabilitySkills[index];
        }

        internal static void Tick(FamilyComponent component, int now)
        {
            if (!component.AmbitionGate.Take(now)) return;
            var active = component.Registry.Lineages.Where(l => l.AbsorbedIntoId == 0 && component.Registry.LivingCount(l.Id, Presence.Colony) > 0).OrderBy(l => l.Id).ToList();
            if (active.Count == 0) return;
            var house = active[component.AmbitionCursor.Take(active.Count)];
            if (house.Situation != null && house.Situation.EvaluatedTick > 0 && now - house.Situation.EvaluatedTick < ActivityTuning.TicksPerDay) return;
            using (component.Performance.Measure("House ambition snapshot"))
            {
                component.Registry.UpdateHouseAmbition(house.Id, Assess(component, house, active, now), now);
                MarriageAlliancePlanner.AssessHouse(component,house,active,now);
            }
        }

        internal static void Invalidate(FamilyComponent component)
        {
            if (component?.Registry?.Lineages == null) return;
            foreach (var h in component.Registry.Lineages) if (h.Situation != null) h.Situation.EvaluatedTick = 0;
            component.AmbitionGate.Invalidate();
        }

        private static HouseSituation Assess(FamilyComponent component, Lineage house, List<Lineage> active, int now)
        {
            var r = component.Registry;
            var records = r.MembersOf(house.Id).Where(p => p.InColony).ToList();
            var pawns = records.Select(p => component.TryGetPawn(p.Id)).Where(p => p != null && !p.Dead).ToList();
            var adults = pawns.Where(p => !p.DevelopmentalStage.Juvenile()).ToList();
            var situation = new HouseSituation
            {
                Members = pawns.Count, Adults = adults.Count, Children = pawns.Count - adults.Count,
                PartneredAdults = adults.Count(p => r.GetPerson(p.thingIDNumber)?.PartnerIds().Any() == true),
                EligibleSingles = adults.Count(p => r.GetPerson(p.thingIDNumber)?.PartnerIds().Any() != true),
                Sustenance = component.Economy.FoodSecurity(house, pawns, now)
            };
            AssessHousing(house, pawns, situation);
            var levels = AssessCapabilities(adults, situation);
            situation.Power = PowerOf(adults, levels);
            situation.Safety = Math.Min(100f, adults.Count == 0 ? 0f : 30f + situation.Power / adults.Count * 2f);
            situation.Continuity = ContinuityOf(situation);
            situation.Cohesion = CohesionOf(r, records);
            situation.Influence = InfluenceOf(r, house, adults);
            situation.Legacy = Math.Min(100f, 20f + situation.Children * 15f + house.Traditions.Count * 5f + Math.Min(20, r.MemoriesOf(house.Id).Count) * 1.5f);
            FindRelations(component, house, active, situation);
            situation.Independence = Math.Min(situation.Sustenance, Math.Min(situation.LivingSpace, Math.Min(situation.Safety, situation.Capability)));
            return situation;
        }

        private static void AssessHousing(Lineage house, List<Pawn> members, HouseSituation result)
        {
            if (members.Count == 0) { result.LivingSpace = 50f; return; }
            var area = FamilyZones.AreaFor(house); int housed = 0, suitable = 0;
            foreach (var pawn in members)
            {
                var bed = pawn.ownership?.OwnedBed;
                if (bed == null || !bed.Spawned || bed.Medical) continue;
                housed++;
                var room = bed.GetRoom();
                if (room != null && !room.PsychologicallyOutdoors && (area == null || area.Map != bed.Map || area[bed.Position])) suitable++;
            }
            int freeSlots = 0;
            if (area != null)
                foreach (var bed in area.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
                    if (bed.Spawned && !bed.Medical && bed.ForColonists && area[bed.Position])
                        freeSlots += Math.Max(0, bed.SleepingSlotsCount - bed.OwnersForReading.Count);
            result.SuitableBeds = suitable; result.SpareBeds = freeSlots;
            result.LivingSpace = Math.Min(100f, 70f * housed / members.Count + 25f * suitable / members.Count + Math.Min(10f, freeSlots * 3f) - (area == null ? 10f : 0f));
        }

        private static Dictionary<HouseCapability, float> AssessCapabilities(List<Pawn> adults, HouseSituation result)
        {
            var scores = new Dictionary<HouseCapability, float>();
            float weakest = float.MaxValue; HouseCapability gap = HouseCapability.None;
            for (int i = 0; i < capabilities.Length; i++)
            {
                int best = 0, competent = 0;
                foreach (var pawn in adults)
                {
                    int level = capabilities[i] == HouseCapability.Combat
                        ? Math.Max(pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0, pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0)
                        : pawn.skills?.GetSkill(capabilitySkills[i])?.Level ?? 0;
                    best = Math.Max(best, level); if (level >= 6) competent++;
                }
                float score = Math.Min(100f, best * 5f + competent * 8f);
                scores[capabilities[i]] = score;
                if (score < weakest) { weakest = score; gap = capabilities[i]; }
            }
            var essential = new[] { HouseCapability.Medical, HouseCapability.Cooking, HouseCapability.Growing, HouseCapability.Construction };
            result.Capability = essential.Select(c => scores[c]).DefaultIfEmpty(50f).Average();
            result.WeakestCapability = gap;
            return scores;
        }

        private static float PowerOf(List<Pawn> adults, Dictionary<HouseCapability, float> levels)
        {
            float power = 0f;
            foreach (var pawn in adults)
            {
                if (pawn.Downed || pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Consciousness) != true) continue;
                int combat = Math.Max(pawn.skills?.GetSkill(SkillDefOf.Shooting)?.Level ?? 0, pawn.skills?.GetSkill(SkillDefOf.Melee)?.Level ?? 0);
                power += 8f + combat * 1.5f + Math.Min(20f, (pawn.equipment?.Primary?.MarketValue ?? 0f) / 100f);
            }
            return power + (levels.TryGetValue(HouseCapability.Medical, out var medicine) ? medicine * .1f : 0f);
        }

        private static float ContinuityOf(HouseSituation s)
        {
            if (s.Adults == 0) return 0f;
            float score = 20f + Math.Min(45f, s.Children * 18f) + Math.Min(25f, s.PartneredAdults * 8f);
            if (s.Adults <= 2 && s.Children == 0) score -= 20f;
            return Math.Max(0f, Math.Min(100f, score));
        }

        private static float CohesionOf(FamilyRegistry r, List<PersonRecord> members)
        {
            var sample = members.Take(20).ToList(); float total = 0f; int count = 0;
            for (int i = 0; i < sample.Count; i++)
                for (int j = i + 1; j < sample.Count; j++)
                {
                    var ab = r.GetBond(sample[i].Id, sample[j].Id); var ba = r.GetBond(sample[j].Id, sample[i].Id);
                    if (ab == null && ba == null) continue;
                    total += 50f + ((ab?.Trust ?? 25f) + (ba?.Trust ?? 25f) - 50f) * .35f
                        + ((ab?.Attachment ?? 0f) + (ba?.Attachment ?? 0f)) * .15f
                        - ((ab?.Tension ?? 0f) + (ba?.Tension ?? 0f)) * .25f;
                    count++;
                }
            float score = count == 0 ? 50f : total / count;
            var ids = new HashSet<int>(members.Select(p => p.Id));
            int rifts = r.Rifts.Count(x => x.IsOpen && ids.Contains(x.A) && ids.Contains(x.B));
            return Math.Max(0f, Math.Min(100f, score - rifts * 12f));
        }

        private static float InfluenceOf(FamilyRegistry r, Lineage house, List<Pawn> adults)
        {
            int associates = r.HouseContacts.Count(c => c.FamilyId == house.Id && c.Respect >= 6f);
            int social = adults.Select(p => p.skills?.GetSkill(SkillDefOf.Social)?.Level ?? 0).DefaultIfEmpty(0).Max();
            return Math.Min(100f, adults.Count * 6f + house.Stewards.Count * 8f + associates * 4f + social * 2f + Math.Max(0f, house.Standing) * .25f);
        }

        private static void FindRelations(FamilyComponent component, Lineage house, List<Lineage> active, HouseSituation situation)
        {
            var r = component.Registry; float bestThreat = 0f, bestCooperation = 0f;
            foreach (var other in active)
            {
                if (other.Id == house.Id) continue;
                var tie = r.GetTie(house.Id, other.Id);
                float grievance = tie?.Grievance ?? 0f, goodwill = tie?.Goodwill ?? 0f;
                bool intimidated = r.Incidents.Any(i => !i.Resolved && i.ActorFamily == other.Id && i.VictimFamily == house.Id &&
                    (i.Kind == IncidentKind.Intimidation || i.Kind == IncidentKind.Injury || i.Kind == IncidentKind.Theft));
                // Disparity creates pressure, not automatic hostility. It becomes a meaningful threat only when the
                // other house is overwhelmingly stronger or has supplied concrete grievance/intimidation evidence.
                float theirPower = other.Situation?.Power ?? 0f;
                float ratio = theirPower <= 0f ? 0f : theirPower / Math.Max(12f, situation.Power);
                float presence = ratio <= 1.25f ? 0f : Math.Min(30f, (ratio - 1.25f) * 16f);
                float threat = grievance + (intimidated ? 30f : 0f) + presence * (grievance >= 10f || intimidated ? 1f : .75f);
                if (threat > bestThreat) { bestThreat = threat; situation.ThreateningFamilyId = other.Id; }
                // Shared history prevents a single goodwill change from manufacturing an alliance. A capable friendly
                // house is a more credible protector, but strength alone never creates cooperation.
                float cooperation = goodwill + Math.Min(20f, (tie?.Shared ?? 0) * 2f);
                if (goodwill >= 10f && theirPower > situation.Power) cooperation += Math.Min(10f, (theirPower - situation.Power) / 20f);
                if (cooperation > bestCooperation && grievance < 25f) { bestCooperation = cooperation; situation.CooperativeFamilyId = other.Id; }
            }
            if (bestThreat < 20f) situation.ThreateningFamilyId = 0;
            if (bestCooperation < 20f) situation.CooperativeFamilyId = 0;
            if (situation.ThreateningFamilyId != 0) situation.Safety = Math.Max(0f, situation.Safety - Math.Min(40f, bestThreat * .4f));
        }
    }
}
