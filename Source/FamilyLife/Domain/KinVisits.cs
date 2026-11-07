using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum KinVisitState : byte { Travelling, Stranded, Arrived, Leaving, Completed, Failed, Expired }

    /// <summary>
    /// Distant relatives coming to a grand reunion. Until the moment they walk onto the map they are untouched: they stay wherever the game keeps them, so
    /// nothing can be duplicated, lost or taken out of a caravan, settlement, quest or leadership. A trip may be held up on the way, which gives the
    /// colony a chance to help; if it does not, the relatives simply do not come and the reunion goes ahead without them.
    /// </summary>
    public sealed partial class KinVisit
    {
        public int Id, FamilyId, RequestId, CreatedTick, ArriveTick, StayEndTick, StrandTile = -1, RescueDeadlineTick, EndedTick, Seed;
        public bool Complication;
        public KinVisitState State;
        public List<int> KinIds = new List<int>();
        public List<int> ArrivedIds = new List<int>();
        public bool Open => State == KinVisitState.Travelling || State == KinVisitState.Stranded || State == KinVisitState.Arrived || State == KinVisitState.Leaving;
    }

    public sealed partial class FamilyRegistry
    {
        public const int KinStayTicks = 3 * ActivityTuning.TicksPerDay, KinRescueTicks = 6 * ActivityTuning.TicksPerDay, MaxKinVisitHistory = 16, MaxVisitingKin = 6;
        public List<KinVisit> KinVisits = new List<KinVisit>();
        public int NextKinVisitId = 1;

        public KinVisit KinVisitFor(int id) => KinVisits.FirstOrDefault(v => v.Id == id);
        public KinVisit KinVisitForRequest(int requestId) => KinVisits.FirstOrDefault(v => v.RequestId == requestId && v.State != KinVisitState.Expired);

        /// <summary>A request waits while its guests are still on the road or held up; once they have arrived or given up, it goes ahead.</summary>
        public bool KinVisitBlocks(int requestId)
        {
            var visit = KinVisits.FirstOrDefault(v => v.RequestId == requestId);
            return visit != null && (visit.State == KinVisitState.Travelling || visit.State == KinVisitState.Stranded);
        }

        /// <summary>
        /// Starts a visit for a planned grand reunion. <paramref name="eligible"/> is what the game has confirmed is safe to bring: alive, not hostile, not a
        /// leader, not quest-bound, not in a caravan or settlement the player owns, not already on a map. Anyone else is simply left out of the request.
        /// </summary>
        public KinVisit PlanKinVisit(int requestId, IEnumerable<int> eligible, int now)
        {
            var request = GatheringRequests.FirstOrDefault(r => r.Id == requestId);
            if (request == null || request.Ceremony != CeremonyKind.GrandReunion || request.State != CeremonyState.Planned || KinVisits.Any(v => v.RequestId == requestId)) return null;
            var allowed = new HashSet<int>(request.VisitorIds);
            var kin = (eligible ?? Enumerable.Empty<int>()).Where(allowed.Contains).Distinct().Where(id => GetPerson(id)?.IsAlive == true && GetPerson(id).Presence == Presence.Away).OrderBy(id => id).Take(MaxVisitingKin).ToList();
            request.VisitorIds.RemoveAll(id => !kin.Contains(id));
            if (kin.Count == 0) return null;
            if (KinVisits.Count >= MaxKinVisitHistory) { var old = KinVisits.FirstOrDefault(v => !v.Open); if (old == null) return null; KinVisits.Remove(old); }
            int seed = unchecked(request.LineageId * 7919 ^ requestId * 104729 ^ now / ActivityTuning.TicksPerDay) & int.MaxValue;
            var visit = new KinVisit
            {
                Id = NextKinVisitId++, FamilyId = request.LineageId, RequestId = requestId, CreatedTick = now, Seed = seed, KinIds = kin, State = KinVisitState.Travelling,
                ArriveTick = now + ActivityTuning.TicksPerDay / 2 + seed % (ActivityTuning.TicksPerDay / 2), Complication = seed % 5 == 0
            };
            KinVisits.Add(visit);
            return visit;
        }

        /// <summary>The trip is held up. The game picked a real place for it; the colony has a limited time to help.</summary>
        public bool StrandKinVisit(int id, int tile, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Travelling || !visit.Complication || tile < 0 || now < visit.ArriveTick) return false;
            visit.State = KinVisitState.Stranded; visit.StrandTile = tile; visit.RescueDeadlineTick = now + KinRescueTicks; visit.Complication = false;
            var request = GatheringRequests.FirstOrDefault(r => r.Id == visit.RequestId);
            if (request != null) request.Expires = Math.Max(request.Expires, visit.RescueDeadlineTick + 4 * ActivityTuning.TicksPerDay);
            return true;
        }

        /// <summary>Someone from the colony reached the place in time. The relatives carry on to the colony a day later and think well of those who came.</summary>
        public bool RescueKinVisit(int id, IEnumerable<int> rescuers, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Stranded || now > visit.RescueDeadlineTick) return false;
            visit.State = KinVisitState.Travelling; visit.ArriveTick = now + ActivityTuning.TicksPerDay; visit.StrandTile = -1;
            foreach (int rescuer in (rescuers ?? Enumerable.Empty<int>()).Distinct().Where(p => GetPerson(p)?.InColony == true).Take(6))
                foreach (int kin in visit.KinIds.Where(k => GetPerson(k)?.IsAlive == true).Take(MaxVisitingKin))
                    ChangeBond(kin, rescuer, new BondChange(2f, 3f, -1f, 1f), now);
            var request = GatheringRequests.FirstOrDefault(r => r.Id == visit.RequestId);
            if (request != null) request.NotBefore = Math.Max(request.NotBefore, visit.ArriveTick + ActivityTuning.TicksPerDay / 4);
            return true;
        }

        /// <summary>People the game actually placed on the map. If nobody could be placed the visit ends and the reunion goes on without them.</summary>
        public bool MarkKinArrived(int id, IEnumerable<int> arrived, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Travelling || now < visit.ArriveTick) return false;
            var here = (arrived ?? Enumerable.Empty<int>()).Where(visit.KinIds.Contains).Distinct().ToList();
            var request = GatheringRequests.FirstOrDefault(r => r.Id == visit.RequestId);
            if (request != null) request.VisitorIds.RemoveAll(p => !here.Contains(p));
            if (here.Count == 0) { visit.State = KinVisitState.Failed; visit.EndedTick = now; return true; }
            visit.ArrivedIds = here; visit.State = KinVisitState.Arrived; visit.StayEndTick = now + KinStayTicks;
            return true;
        }

        /// <summary>Time to go home: the stay is over. The game then sends anyone still on the map on their way.</summary>
        public bool BeginKinLeaving(int id, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Arrived || now < visit.StayEndTick) return false;
            visit.State = KinVisitState.Leaving;
            return true;
        }

        /// <summary>Everyone has left the map (or can no longer be sent: they died or joined the player). The visit is over.</summary>
        public bool CompleteKinVisit(int id, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Leaving) return false;
            visit.State = KinVisitState.Completed; visit.EndedTick = now;
            return true;
        }

        /// <summary>Ends visits that can no longer happen: a held-up trip nobody reached in time, or a visit whose reunion was cancelled before anyone came.</summary>
        public void MaintainKinVisits(int now)
        {
            foreach (var visit in KinVisits.Where(v => v.State == KinVisitState.Travelling || v.State == KinVisitState.Stranded).ToList())
            {
                var request = GatheringRequests.FirstOrDefault(r => r.Id == visit.RequestId);
                if (request == null || request.State == CeremonyState.Cancelled || request.State == CeremonyState.Expired) { visit.State = KinVisitState.Expired; visit.EndedTick = now; continue; }
                if (visit.State == KinVisitState.Stranded && now > visit.RescueDeadlineTick)
                {
                    visit.State = KinVisitState.Failed; visit.EndedTick = now;
                    request.VisitorIds.Clear();                         // they never arrived; the family holds the reunion without them
                }
            }
        }
    }
}
