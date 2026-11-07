using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The game-side facts the planning, duty, succession and institution rules need, and the light maintenance that keeps them current.
    /// Nothing here scans a map: every fact is looked up for the few pawns and houses a record actually names.
    /// </summary>
    internal static class Governance
    {
        /// <summary>Occasions that get a planning card.</summary>
        internal static bool IsPlanned(CeremonyKind kind) =>
            kind == CeremonyKind.Wedding || kind == CeremonyKind.Funeral || kind == CeremonyKind.GrandReunion || kind == CeremonyKind.ComingOfAge;

        internal static bool IsAvatar(FamilyComponent c, int id)
        {
            var pawn = c.TryGetPawn(id);
            return pawn != null && PerspectiveShiftBridge.IsAvatar(pawn);
        }

        /// <summary>False for anyone drafted, downed, in a mental state, held prisoner or playing as the avatar.</summary>
        internal static bool Free(FamilyComponent c, int id)
        {
            var pawn = c.TryGetPawn(id);
            if (pawn == null) return true;
            return !pawn.Drafted && !pawn.Downed && !pawn.InMentalState && !pawn.IsPrisoner && !PerspectiveShiftBridge.IsAvatar(pawn);
        }

        internal static PlanContext PlanContextFor(FamilyComponent c, int family)
        {
            int home = c.Registry.GetLineage(family)?.ZoneMapId ?? -1;
            return new PlanContext
            {
                DefaultMapId = home,
                Available = id => { var p = c.TryGetPawn(id); return p == null || (!p.Downed && !p.Drafted && !p.IsPrisoner && !p.InMentalState); },
                IsAvatar = id => IsAvatar(c, id),
                // Nobody is brought to the map from elsewhere. A relative counts as able to attend only if they are already here.
                ArrivalEstimate = id => { var p = c.TryGetPawn(id); return p != null && p.Spawned && p.Map != null && (home < 0 || p.Map.uniqueID == home) ? 0 : -1; },
                VenueValid = (map, area) => map < 0 || Find.Maps.Any(m => m.uniqueID == map)
            };
        }

        internal static void Maintain(FamilyComponent c, int now)
        {
            var r = c.Registry;
            void Safe(System.Action step, string name) { try { step(); } catch (System.Exception ex) { Log.Warning("[Wil's Family Life] Governance upkeep '" + name + "' skipped: " + ex.Message); } }
            Safe(() => { foreach (var request in r.GatheringRequests.Where(q => q.State == CeremonyState.Planned && q.NotBefore > now && IsPlanned(q.Ceremony) && r.PlanFor(q.Id) == null).Take(4).ToList())
                r.OpenCeremonyPlan(request.Id, now, PlanContextFor(c, request.LineageId)); }, "plans");
            Safe(() => r.AdvanceCeremonyPlans(now, family => PlanContextFor(c, family)), "plan upkeep");
            Safe(() => r.MaintainStewardDuties(now, id => Free(c, id)), "duties");
            Safe(() => r.PruneOffices(map => Find.Maps.Any(m => m.uniqueID == map)), "offices");
            Safe(() => r.MaintainSuccession(now), "succession");
            Safe(() => r.MaintainCrime(now), "crime upkeep");
            GovernanceDriver.Run(c, now);
        }
    }
}
