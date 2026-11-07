using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>The durable needs a house can recognize. Values are supplied by the game layer as bounded 0-100 scores.</summary>
    public enum HouseNeed : byte
    {
        None, Sustenance, LivingSpace, Safety, Continuity, Capability, Cohesion, Influence, Independence, Legacy
    }

    public enum HouseAmbitionKind : byte
    {
        None,
        SecureProvisions,
        EstablishFamilyQuarters,
        ImproveSafety,
        SeekProtection,
        SecureSuccession,
        FillSkillGap,
        TrainNextGeneration,
        RestoreCohesion,
        ExpandLegacy,
        PreserveIndependence,
        IncreaseInfluence,
        Contentment
    }

    public enum HouseCapability : byte
    {
        None, Medical, Cooking, Growing, Construction, Crafting, Intellectual, Social, Childcare, Combat, Animals
    }

    /// <summary>A compact saved snapshot. It is deliberately aggregates-only: no map cells, Thing references or pairwise pawn matrices.</summary>
    public sealed partial class HouseSituation
    {
        public int EvaluatedTick;
        public int Members, Adults, Children, PartneredAdults, EligibleSingles, SuitableBeds, SpareBeds;
        public float Sustenance = 50f, LivingSpace = 50f, Safety = 50f, Continuity = 50f, Capability = 50f;
        public float Cohesion = 50f, Influence = 50f, Independence = 50f, Legacy = 50f, Power;
        public HouseCapability WeakestCapability;
        public int ThreateningFamilyId, CooperativeFamilyId;

        public float Score(HouseNeed need)
        {
            switch (need)
            {
                case HouseNeed.Sustenance: return Sustenance;
                case HouseNeed.LivingSpace: return LivingSpace;
                case HouseNeed.Safety: return Safety;
                case HouseNeed.Continuity: return Continuity;
                case HouseNeed.Capability: return Capability;
                case HouseNeed.Cohesion: return Cohesion;
                case HouseNeed.Influence: return Influence;
                case HouseNeed.Independence: return Independence;
                case HouseNeed.Legacy: return Legacy;
                default: return 50f;
            }
        }

        internal void Clamp()
        {
            Sustenance = Bound(Sustenance); LivingSpace = Bound(LivingSpace); Safety = Bound(Safety);
            Continuity = Bound(Continuity); Capability = Bound(Capability); Cohesion = Bound(Cohesion);
            Influence = Bound(Influence); Independence = Bound(Independence); Legacy = Bound(Legacy);
            Power = Math.Max(0f, Power);
            Members = Math.Max(0, Members); Adults = Math.Max(0, Adults); Children = Math.Max(0, Children);
            PartneredAdults = Math.Max(0, PartneredAdults); EligibleSingles = Math.Max(0, EligibleSingles);
            SuitableBeds = Math.Max(0, SuitableBeds); SpareBeds = Math.Max(0, SpareBeds);
        }

        private static float Bound(float value) => float.IsNaN(value) ? 50f : Math.Max(0f, Math.Min(100f, value));
    }

    /// <summary>The reasoned, persistent intent of a house. Reasons are enum values so saves do not contain localized prose.</summary>
    public sealed partial class HouseAmbitionRecord
    {
        public HouseAmbitionKind Primary, Secondary;
        public HouseNeed PrimaryReason, SecondaryReason;
        public int StartedTick, LastChangedTick, LockedUntilTick, TargetFamilyId;
        public HouseCapability TargetCapability;
        public float Urgency, Progress;
    }

    public sealed partial class Lineage
    {
        public HouseSituation Situation = new HouseSituation();
        public HouseAmbitionRecord Ambition = new HouseAmbitionRecord();
    }

    public sealed partial class FamilyRegistry
    {
        private struct AmbitionCandidate
        {
            public HouseAmbitionKind Kind;
            public HouseNeed Need;
            public float Score;
            public int TargetFamily;
            public HouseCapability Capability;
        }

        /// <summary>
        /// Accepts one already-aggregated snapshot and selects stable goals. Emergency food or safety can interrupt a goal;
        /// ordinary changes wait for the three-day lock, preventing houses from thrashing between intentions.
        /// </summary>
        public HouseAmbitionRecord UpdateHouseAmbition(int familyId, HouseSituation snapshot, int now)
        {
            var house = GetLineage(familyId);
            if (house == null || house.AbsorbedIntoId != 0 || snapshot == null) return null;
            snapshot.Clamp(); snapshot.EvaluatedTick = now; house.Situation = snapshot;
            var current = house.Ambition ?? (house.Ambition = new HouseAmbitionRecord());
            var candidates = AmbitionCandidates(house, snapshot).OrderByDescending(c => c.Score).ThenBy(c => c.Kind).ToList();
            var first = candidates.FirstOrDefault();
            bool emergency = snapshot.Sustenance < 20f || snapshot.Safety < 15f;
            if (current.Primary != HouseAmbitionKind.None && now < current.LockedUntilTick && !emergency)
            {
                var held = candidates.FirstOrDefault(c => c.Kind == current.Primary && c.Need == current.PrimaryReason);
                var alternative = candidates.FirstOrDefault(c => c.Kind != current.Primary && c.Score >= 38f);
                current.Secondary = alternative.Kind;
                current.SecondaryReason = alternative.Need;
                current.Urgency = Math.Min(100f, Math.Max(0f, held.Kind == HouseAmbitionKind.None ? first.Score : held.Score));
                return current;
            }
            if (first.Kind == HouseAmbitionKind.None) first = new AmbitionCandidate { Kind = HouseAmbitionKind.Contentment, Need = HouseNeed.Legacy, Score = 25f };
            var second = candidates.FirstOrDefault(c => c.Kind != first.Kind && c.Score >= 38f);
            if (current.Primary != first.Kind || current.PrimaryReason != first.Need || current.TargetFamilyId != first.TargetFamily || current.TargetCapability != first.Capability)
            {
                current.Primary = first.Kind; current.PrimaryReason = first.Need;
                current.TargetFamilyId = first.TargetFamily; current.TargetCapability = first.Capability;
                current.StartedTick = now; current.LastChangedTick = now; current.Progress = 0f;
                current.LockedUntilTick = now + 3 * ActivityTuning.TicksPerDay;
            }
            current.Secondary = second.Kind; current.SecondaryReason = second.Need;
            current.Urgency = Math.Min(100f, Math.Max(0f, first.Score));
            return current;
        }

        private static IEnumerable<AmbitionCandidate> AmbitionCandidates(Lineage house, HouseSituation s)
        {
            float hearth = house.CultureOf(Dim.Hearth), scholarly = house.CultureOf(Dim.Scholarly);
            float martial = house.CultureOf(Dim.Martial), status = house.CultureOf(Dim.Status);
            float rooted = house.CultureOf(Dim.Rootedness), devotion = house.CultureOf(Dim.Devotion);
            yield return C(HouseAmbitionKind.SecureProvisions, HouseNeed.Sustenance, 100f - s.Sustenance + Math.Max(0f, hearth) * .08f);
            yield return C(HouseAmbitionKind.EstablishFamilyQuarters, HouseNeed.LivingSpace, 100f - s.LivingSpace + Math.Max(0f, rooted) * .1f);
            if (s.ThreateningFamilyId != 0 && s.CooperativeFamilyId != 0 && s.Safety < 48f)
                yield return C(HouseAmbitionKind.SeekProtection, HouseNeed.Safety, 112f - s.Safety + Math.Max(0f, devotion) * .05f, s.CooperativeFamilyId);
            else yield return C(HouseAmbitionKind.ImproveSafety, HouseNeed.Safety, 100f - s.Safety + Math.Max(0f, martial) * .08f);
            yield return C(HouseAmbitionKind.SecureSuccession, HouseNeed.Continuity, 100f - s.Continuity + Math.Max(0f, devotion + hearth) * .06f);
            if (s.WeakestCapability != HouseCapability.None)
                yield return C(HouseAmbitionKind.FillSkillGap, HouseNeed.Capability, 100f - s.Capability + Math.Max(0f, scholarly) * .08f, 0, s.WeakestCapability);
            if (s.Children > 0 && s.Capability < 70f)
                yield return C(HouseAmbitionKind.TrainNextGeneration, HouseNeed.Capability, 75f - s.Capability + s.Children * 3f + Math.Max(0f, scholarly) * .1f, 0, s.WeakestCapability);
            yield return C(HouseAmbitionKind.RestoreCohesion, HouseNeed.Cohesion, 100f - s.Cohesion + Math.Max(0f, devotion) * .08f);
            yield return C(HouseAmbitionKind.IncreaseInfluence, HouseNeed.Influence, 92f - s.Influence + Math.Max(0f, status) * .15f);
            if (s.Independence < 55f && rooted > 10f)
                yield return C(HouseAmbitionKind.PreserveIndependence, HouseNeed.Independence, 95f - s.Independence + rooted * .12f);
            bool canExpand = s.Sustenance >= 65f && s.LivingSpace >= 65f && s.Safety >= 55f && s.Cohesion >= 55f && s.EligibleSingles > 0;
            if (canExpand)
                yield return C(HouseAmbitionKind.ExpandLegacy, HouseNeed.Legacy, 42f + (s.Sustenance + s.LivingSpace + s.Cohesion - 195f) * .15f + Math.Max(0f, hearth + devotion + status) * .08f);
            if (new[] { s.Sustenance, s.LivingSpace, s.Safety, s.Continuity, s.Capability, s.Cohesion }.Min() >= 62f)
                yield return C(HouseAmbitionKind.Contentment, HouseNeed.Legacy, 38f + Math.Max(0f, 20f - Math.Abs(status)) * .15f);
        }

        private static AmbitionCandidate C(HouseAmbitionKind kind, HouseNeed need, float score, int target = 0, HouseCapability capability = HouseCapability.None)
            => new AmbitionCandidate { Kind = kind, Need = need, Score = score, TargetFamily = target, Capability = capability };
    }
}
