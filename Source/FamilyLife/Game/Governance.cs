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
            return !pawn.Drafted && !pawn.Downed && !pawn.InMentalState && !pawn.IsPrisoner && pawn.CurJob?.playerForced != true &&
                !Find.Selector.IsSelected(pawn) && !PerspectiveShiftBridge.IsAvatar(pawn);
        }

        internal static bool OfficeCellValid(FamilyComponent c, FamilyOffice office)
        {
            if(c==null||office==null)return false;
            var map=Find.Maps.FirstOrDefault(m=>m.uniqueID==office.MapId);var house=c.Registry.GetLineage(office.FamilyId);
            var cell=new IntVec3(office.X,0,office.Z);var area=FamilyZones.AreaFor(house);
            return map!=null&&cell.InBounds(map)&&cell.Standable(map)&&area?.Map==map&&area[cell];
        }

        internal static bool TryMeetingCell(FamilyComponent c,int family,Pawn first,Pawn second,out IntVec3 cell)
        {
            cell=IntVec3.Invalid;
            var office=c?.Registry.OfficeOf(family);if(!OfficeCellValid(c,office)||first?.Map==null||second?.Map!=first.Map||first.Map.uniqueID!=office.MapId)return false;
            var candidate=new IntVec3(office.X,0,office.Z);
            if(!first.CanReach(candidate,Verse.AI.PathEndMode.OnCell,Danger.None)||!second.CanReach(candidate,Verse.AI.PathEndMode.OnCell,Danger.None))return false;
            cell=candidate;return true;
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
                VenueValid = (map, area) => map < 0 || Find.Maps.Any(m => m.uniqueID == map &&
                    (area == 0 || m.areaManager.AllAreas.Any(a => a.ID == area)))
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
            Safe(() => r.PruneOffices(office => OfficeCellValid(c, office)), "offices");
            if (FamilyLifeMod.Settings == null || FamilyLifeMod.Settings.governance) Safe(() => r.MaintainSuccession(now), "succession");
            Safe(() => r.MaintainCrime(now), "crime upkeep");
            Safe(() => CampingDriver.Run(c, now), "camping");
            Safe(() => KinVisitDriver.Run(c, now), "kin visits");
            GovernanceDriver.Run(c, now);
        }
    }
}
