using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // Appended to the kin-visit model; none of the earlier values change.
    public enum RescueComplication : byte { None, BrokenTransport, Injury, Animals, Raiders, Weather }
    public enum RescueStage : byte { None, Offered, Accepted, OnSite, Succeeded, Declined, Expired, Abandoned, Lost, SiteLost }

    public sealed partial class KinVisit
    {
        public RescueStage Rescue;
        public RescueComplication Trouble;
        public int QuestId, SiteId, OfferExpiryTick, AcceptedTick, ReachedTick, RescueEndedTick, SiteVisits;
        public List<int> RescuerIds = new List<int>();
        public List<int> LostIds = new List<int>();
        /// <summary>The rescue is still open: a quest is on offer, accepted or being carried out.</summary>
        public bool RescueOpen => State == KinVisitState.Stranded && (Rescue == RescueStage.Offered || Rescue == RescueStage.Accepted || Rescue == RescueStage.OnSite);
    }

    public sealed partial class FamilyRegistry
    {
        public const int KinOfferTicks = 3 * ActivityTuning.TicksPerDay, KinSiteTicks = 3 * ActivityTuning.TicksPerDay, KinOnSiteTicks = ActivityTuning.TicksPerDay;

        /// <summary>
        /// Picks the trouble from what the place and the people actually allow. <paramref name="weights"/> come from the game (cold or heat, wildlife, a hostile faction,
        /// the relatives' tech level); a missing or all-zero entry means that kind of trouble does not fit here. The same seed always gives the same answer.
        /// </summary>
        public static RescueComplication ChooseTrouble(int seed, IDictionary<RescueComplication, int> weights)
        {
            var pool = new List<KeyValuePair<RescueComplication, int>>();
            if (weights != null)
                foreach (var kv in weights.OrderBy(k => k.Key)) if (kv.Value > 0 && kv.Key != RescueComplication.None) pool.Add(kv);
            if (pool.Count == 0) return RescueComplication.BrokenTransport;
            int total = pool.Sum(k => k.Value), roll = Math.Abs(seed % total), at = 0;
            foreach (var kv in pool) { at += kv.Value; if (roll < at) return kv.Key; }
            return pool[pool.Count - 1].Key;
        }

        /// <summary>The held-up trip becomes an offer to help. The offer stays open for a limited time and is never forced on anyone.</summary>
        public bool OfferKinRescue(int id, RescueComplication trouble, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Stranded || visit.Rescue != RescueStage.None) return false;
            visit.Rescue = RescueStage.Offered;
            visit.Trouble = trouble == RescueComplication.None ? RescueComplication.BrokenTransport : trouble;
            visit.OfferExpiryTick = Math.Min(now + KinOfferTicks, visit.RescueDeadlineTick > now ? visit.RescueDeadlineTick : now + KinOfferTicks);
            return true;
        }

        public bool BindKinRescueQuest(int id, int questId)
        {
            var visit = KinVisitFor(id);
            if (visit == null || !visit.RescueOpen || questId <= 0 || visit.QuestId != 0 && visit.QuestId != questId) return false;
            visit.QuestId = questId;
            return true;
        }

        /// <summary>The player took the quest up. Accepting after the offer lapsed, or twice, does nothing.</summary>
        public bool AcceptKinRescue(int id, int siteId, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Stranded || visit.Rescue != RescueStage.Offered || now > visit.OfferExpiryTick) return false;
            visit.Rescue = RescueStage.Accepted; visit.SiteId = siteId; visit.AcceptedTick = now;
            visit.RescueDeadlineTick = now + KinSiteTicks;
            var request = GatheringRequests.FirstOrDefault(r => r.Id == visit.RequestId);
            if (request != null) request.Expires = Math.Max(request.Expires, visit.RescueDeadlineTick + 4 * ActivityTuning.TicksPerDay);
            return true;
        }

        /// <summary>A caravan reached the place. The clock stops running out from under them: there is always at least a day to put things right once they are there.</summary>
        public bool ReachKinRescue(int id, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Stranded || (visit.Rescue != RescueStage.Accepted && visit.Rescue != RescueStage.OnSite) || now > visit.RescueDeadlineTick) return false;
            if (visit.Rescue == RescueStage.Accepted) { visit.Rescue = RescueStage.OnSite; visit.ReachedTick = now; visit.SiteVisits++; }
            visit.RescueDeadlineTick = Math.Max(visit.RescueDeadlineTick, Math.Min(now + KinOnSiteTicks, visit.AcceptedTick + KinSiteTicks + KinOnSiteTicks));
            return true;
        }

        /// <summary>The caravan left before things were put right. The relatives wait where they are and the offer stays open until the time runs out.</summary>
        public bool LeaveKinRescue(int id)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Stranded || visit.Rescue != RescueStage.OnSite) return false;
            visit.Rescue = RescueStage.Accepted;
            return true;
        }

        /// <summary>
        /// The relatives were reached in time. Anyone who died on the way is removed from the guest list; the rest carry on after a delay that fits the trouble,
        /// and think well of the ones who actually came. Reporting a second time does nothing.
        /// </summary>
        public bool CompleteKinRescue(int id, IEnumerable<int> rescuers, IEnumerable<int> lost, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Stranded || visit.Rescue == RescueStage.Offered || now > visit.RescueDeadlineTick) return false;
            var gone = (lost ?? Enumerable.Empty<int>()).Where(visit.KinIds.Contains).Distinct().ToList();
            foreach (int dead in gone) RemoveKinFrom(visit, dead);
            visit.LostIds = visit.LostIds.Union(gone).ToList();
            if (visit.KinIds.Count == 0) return FailKinRescue(id, RescueStage.Lost, now);
            var credited = (rescuers ?? Enumerable.Empty<int>()).Distinct().Where(p => GetPerson(p)?.InColony == true).Take(6).ToList();
            visit.State = KinVisitState.Travelling; visit.Complication = false; visit.StrandTile = -1;
            visit.Rescue = RescueStage.Succeeded; visit.RescueEndedTick = now; visit.RescuerIds = credited;
            int delay = ActivityTuning.TicksPerDay + (visit.Trouble == RescueComplication.Injury || visit.Trouble == RescueComplication.Weather ? ActivityTuning.TicksPerDay / 2 : 0);
            visit.ArriveTick = now + delay;
            var survivors = visit.KinIds.Where(k => GetPerson(k)?.IsAlive == true).Take(MaxVisitingKin).ToList();
            foreach (int rescuer in credited)
                foreach (int kin in survivors)
                    ChangeBond(kin, rescuer, new BondChange(2f, 3f, -1f, 1f), now);
            foreach (int rescuer in credited.Take(3))
                RecordIncident(IncidentKind.Gift, rescuer, survivors.First(), 0, now, 14f, credited.Concat(survivors));
            var request = GatheringRequests.FirstOrDefault(r => r.Id == visit.RequestId);
            if (request != null) request.NotBefore = Math.Max(request.NotBefore, visit.ArriveTick + ActivityTuning.TicksPerDay / 4);
            return true;
        }

        /// <summary>
        /// The rescue ended without reaching them: declined, ignored until it lapsed, abandoned, the place gone, or no one left alive. Whatever the reason,
        /// the party comes off the guest list and the reunion goes ahead without them. Only one ending can ever apply.
        /// </summary>
        public bool FailKinRescue(int id, RescueStage why, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || visit.State != KinVisitState.Stranded) return false;
            if (why == RescueStage.None || why == RescueStage.Offered || why == RescueStage.Accepted || why == RescueStage.OnSite || why == RescueStage.Succeeded) why = RescueStage.Expired;
            visit.State = KinVisitState.Failed; visit.EndedTick = now; visit.Rescue = why; visit.RescueEndedTick = now; visit.StrandTile = -1;
            var request = GatheringRequests.FirstOrDefault(r => r.Id == visit.RequestId);
            if (request != null) request.VisitorIds.Clear();
            return true;
        }

        /// <summary>People the game says can no longer come (hostile now, captured, joined the player, gone): they leave the guest list and nobody else is touched.</summary>
        public bool RemoveKinFromVisit(int id, IEnumerable<int> unavailable, int now)
        {
            var visit = KinVisitFor(id);
            if (visit == null || !visit.Open) return false;
            bool any = false;
            foreach (int p in (unavailable ?? Enumerable.Empty<int>()).Distinct().ToList())
                if (visit.KinIds.Contains(p)) { RemoveKinFrom(visit, p); any = true; }
            if (any && visit.KinIds.Count == 0 && (visit.State == KinVisitState.Travelling || visit.State == KinVisitState.Stranded || visit.State == KinVisitState.Landing))
            {
                if (visit.State == KinVisitState.Stranded) FailKinRescue(id, RescueStage.Lost, now);
                else { visit.State = KinVisitState.Failed; visit.EndedTick = now; }
            }
            return any;
        }

        private void RemoveKinFrom(KinVisit visit, int person)
        {
            visit.KinIds.Remove(person); visit.LandingIds.Remove(person);
            var request = GatheringRequests.FirstOrDefault(r => r.Id == visit.RequestId);
            request?.VisitorIds.Remove(person);
        }

        /// <summary>Relatives who have died are taken off every open party that has not yet reached the colony.</summary>
        public void DropDeadKin(int now)
        {
            foreach (var visit in KinVisits.Where(v => v.State == KinVisitState.Travelling || v.State == KinVisitState.Stranded || v.State == KinVisitState.Landing).ToList())
            {
                var dead = visit.KinIds.Where(k => GetPerson(k)?.IsAlive != true).ToList();
                if (dead.Count == 0) continue;
                visit.LostIds = visit.LostIds.Union(dead).ToList();
                RemoveKinFromVisit(visit.Id, dead, now);
            }
        }

        /// <summary>
        /// The whole visit lifecycle in one place: parties end when their reunion is cancelled, when their offer lapses, when the time to reach them runs out,
        /// and when no one in them is left. Calling it again changes nothing.
        /// </summary>
        public void MaintainKinRescues(int now)
        {
            DropDeadKin(now);
            foreach (var visit in KinVisits.Where(v => v.State == KinVisitState.Stranded).ToList())
            {
                if (visit.Rescue == RescueStage.Offered && now > visit.OfferExpiryTick) FailKinRescue(visit.Id, RescueStage.Expired, now);
                else if (visit.Rescue != RescueStage.Offered && now > visit.RescueDeadlineTick) FailKinRescue(visit.Id, visit.Rescue == RescueStage.None ? RescueStage.Expired : RescueStage.Abandoned, now);
            }
        }
    }
}
