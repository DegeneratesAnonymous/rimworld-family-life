using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum SupplyKind : byte { Food, Fuel, Apparel, Medicine, Drug, Book, Currency }
    public enum SupplyApproach : byte { Ask, Loan, Barter, Threat, Steal, PublicClaim, Return, Relocate, Repay, Offer }
    public enum SupplyState : byte { Pending, Granted, Refused, Exchanged, Stored, Cancelled, Repaid, Defaulted }
    public sealed partial class StockClaim
    {
        public int ThingId, OwnerFamily, HolderFamily, MapId;
        public SupplyKind Kind;
    }
    public sealed partial class SupplyRequest
    {
        public int Id, RequesterId, DonorId, SourceId, MapId, Count, CreatedTick, DueTick, Price, RootIncidentId;
        public int HomeX, HomeZ, DoorX, DoorZ, DebtId, RepaymentAttemptTick;
        public SupplyKind Kind;
        public SupplyApproach Approach;
        public SupplyState State;
        public string Definition = "";
        public float Units;
        public bool DebtPaid;
        public bool Open => State == SupplyState.Pending || State == SupplyState.Granted;
    }

    public sealed partial class FamilyRegistry
    {
        public List<StockClaim> Claims = new List<StockClaim>();
        public List<SupplyRequest> Requests = new List<SupplyRequest>();
        public int NextSupplyRequestId = 1;
        private readonly Dictionary<int, StockClaim> claimByThing = new Dictionary<int, StockClaim>();
        public StockClaim ClaimFor(int thingId)
        {
            if (claimByThing.Count != Claims.Count) { claimByThing.Clear(); foreach (var c in Claims) claimByThing[c.ThingId] = c; }
            return claimByThing.TryGetValue(thingId, out var claim) ? claim : null;
        }
        public void ClaimStock(int thingId, int owner, int holder, int map, SupplyKind kind)
        {
            if (GetLineage(owner) == null || GetLineage(holder) == null || thingId <= 0) return;
            var claim = ClaimFor(thingId);
            if (claim == null) { claim = new StockClaim { ThingId = thingId }; Claims.Add(claim); claimByThing[thingId] = claim; }
            claim.OwnerFamily = owner; claim.HolderFamily = holder; claim.MapId = map; claim.Kind = kind;
        }
        public void ForgetStock(int thingId) { Claims.RemoveAll(c => c.ThingId == thingId); claimByThing.Remove(thingId); }
        public void SplitStock(int source, int result)
        {
            if (source == result) return;
            var claim = ClaimFor(source);
            if (claim != null) ClaimStock(result, claim.OwnerFamily, claim.HolderFamily, claim.MapId, claim.Kind);
        }
        public bool SameOwnership(int a, int b)
        {
            var ca = ClaimFor(a); var cb = ClaimFor(b);
            return (ca?.OwnerFamily ?? 0) == (cb?.OwnerFamily ?? 0) && (ca?.HolderFamily ?? 0) == (cb?.HolderFamily ?? 0);
        }
        public bool MayUseStock(int thing, int family) => ClaimFor(thing) == null || (family != 0 && ClaimFor(thing).HolderFamily == family);

        public SupplyRequest OpenSupplyRequest(int requester, int donor, int source, int map, int count, SupplyKind kind, SupplyApproach approach, int now)
        {
            if (FamilyOf(requester) == 0 || GetPerson(requester)?.InColony != true || GetPerson(requester).Juvenile || source <= 0 || count < 1 || Requests.Any(r => r.RequesterId == requester && r.Open)) return null;
            if (approach != SupplyApproach.PublicClaim && approach != SupplyApproach.Relocate && (GetPerson(donor)?.InColony != true || FamilyOf(donor) == FamilyOf(requester))) return null;
            var request = new SupplyRequest { Id = NextSupplyRequestId++, RequesterId = requester, DonorId = donor, SourceId = source, MapId = map, Count = count,
                Kind = kind, Approach = approach, CreatedTick = now, DueTick = now + ActivityTuning.TicksPerDay };
            Requests.Add(request);
            while (Requests.Count > 128)
            {
                var old = Requests.FirstOrDefault(r => !r.Open && !(r.Approach == SupplyApproach.Loan && r.State >= SupplyState.Exchanged && !r.DebtPaid && r.State != SupplyState.Defaulted));
                if (old == null) { Requests.Remove(request); return null; }
                Requests.Remove(old);
            }
            return request;
        }

        /// <summary>No transfer or goodwill occurs here: the game must actually hand over the goods.</summary>
        public bool NegotiateSupply(SupplyRequest r, float donorWarmth, float donorNeed, bool hasPayment, int now)
        {
            if (r == null || r.State != SupplyState.Pending) return false;
            float trust = GetBond(r.DonorId, r.RequesterId)?.Trust ?? 25;
            float grievance = GetTie(FamilyOf(r.DonorId), FamilyOf(r.RequesterId))?.Grievance ?? 0;
            bool accept;
            switch (r.Approach)
            {
                case SupplyApproach.Offer: accept = donorNeed < .5f; break;
                case SupplyApproach.PublicClaim: accept = true; break;
                case SupplyApproach.Return: accept = ClaimFor(r.SourceId)?.OwnerFamily == FamilyOf(r.RequesterId) && donorWarmth > -0.3f; break;
                case SupplyApproach.Repay:
                    var debt = Requests.FirstOrDefault(x => x.Id == r.DebtId && x.RequesterId == r.DonorId && x.DonorId == r.RequesterId && x.Approach == SupplyApproach.Loan && !x.DebtPaid && (x.State == SupplyState.Stored || x.State == SupplyState.Exchanged));
                    if (debt == null) { r.State = SupplyState.Cancelled; return false; }
                    accept = donorNeed < .5f && ClaimFor(r.SourceId)?.OwnerFamily == FamilyOf(r.DonorId) && debt.Kind == r.Kind && (r.Kind == SupplyKind.Food ? r.Units >= debt.Units : r.Definition == debt.Definition && r.Count >= debt.Count); break;
                case SupplyApproach.Loan: accept = donorNeed < 0.6f && trust >= 35 && grievance < 45; break;
                case SupplyApproach.Barter: accept = donorNeed < 0.7f && hasPayment && grievance < 70; break;
                case SupplyApproach.Threat: accept = donorNeed < 0.85f && donorWarmth > -0.4f && (GetBond(r.DonorId, r.RequesterId)?.Tension ?? 0) >= 25; break;
                default: accept = donorNeed < 0.5f && donorWarmth + trust / 100f - grievance / 100f >= 0.3f; break;
            }
            r.State = accept ? SupplyState.Granted : SupplyState.Refused;
            if (!accept) RecordIncident(IncidentKind.Refusal, r.DonorId, r.RequesterId, r.MapId, now, donorNeed >= 0.6f ? 2 : 8, new[] { r.DonorId, r.RequesterId });
            return accept;
        }

        public bool ExchangeSupply(int requestId, int carriedThingId, int actualCount, IEnumerable<int> witnesses, int now)
        {
            var r = Requests.FirstOrDefault(x => x.Id == requestId);
            if (r == null || r.State != SupplyState.Granted || carriedThingId<=0 || GetPerson(r.RequesterId)?.InColony!=true || FamilyOf(r.RequesterId)==0 || actualCount < r.Count) return false;
            int receiver = FamilyOf(r.RequesterId), donor = FamilyOf(r.DonorId);
            var source = ClaimFor(carriedThingId);
            if (r.Approach == SupplyApproach.PublicClaim && source != null) return false;
            if (r.Approach == SupplyApproach.Relocate) return source != null && source.HolderFamily == receiver && MarkRelocated(r, carriedThingId);
            if (r.Approach != SupplyApproach.PublicClaim && (source == null || source.HolderFamily != donor)) return false;
            int owner = r.Approach == SupplyApproach.Steal || source != null && source.OwnerFamily != source.HolderFamily ? source.OwnerFamily : receiver;
            ClaimStock(carriedThingId, owner, receiver, r.MapId, r.Kind);
            r.State = SupplyState.Exchanged; r.SourceId = carriedThingId;
            r.DueTick = now + 3 * ActivityTuning.TicksPerDay;
            if (r.Approach == SupplyApproach.PublicClaim) return true;
            if (r.Approach == SupplyApproach.Steal)
                {
                var theft = RecordIncident(IncidentKind.Theft, r.RequesterId, r.DonorId, r.MapId, now, 25, witnesses, amount: r.Count);
                if (theft != null) theft.Resource = r.Definition;
                }
            else if (r.Approach == SupplyApproach.Threat)
            {
                RecordIncident(IncidentKind.Intimidation, r.RequesterId, r.DonorId, r.MapId, now, 18, new[] { r.RequesterId, r.DonorId });
                ChangeBond(r.RequesterId, r.DonorId, new BondChange(0, -2, 3, 2), now);
            }
            else if (r.Approach == SupplyApproach.Return)
            {
                RecordIncident(IncidentKind.Restitution, r.DonorId, r.RequesterId, r.MapId, now, 15, new[] { r.RequesterId, r.DonorId });
                var root = Incidents.FirstOrDefault(i => i.Id == r.RootIncidentId);
                if (root != null && root.Kind == IncidentKind.Theft && root.VictimFamily == receiver && root.Resource == r.Definition)
                { root.Restored += r.Count; if (root.Restored >= root.Amount) ForgiveIncident(root.Id, receiver); }
            }
            else if (r.Approach != SupplyApproach.Repay)
            {
                RecordIncident(IncidentKind.Gift, r.DonorId, r.RequesterId, r.MapId, now, 10, new[] { r.RequesterId, r.DonorId });
                ChangeBond(r.DonorId, r.RequesterId, new BondChange(1, 1, -1, 2), now);
            }
            // Repayment needs the reverse, actually completed transfer of the same resource category.
            var debt = r.Approach == SupplyApproach.Steal || r.Approach == SupplyApproach.Threat ? null : Requests.FirstOrDefault(x => x.Id != r.Id && (r.Approach != SupplyApproach.Repay || x.Id == r.DebtId) && x.Approach == SupplyApproach.Loan && !x.DebtPaid &&
                (x.State == SupplyState.Exchanged || x.State == SupplyState.Stored) && x.RequesterId == r.DonorId && x.DonorId == r.RequesterId && x.Kind == r.Kind && (r.Kind == SupplyKind.Food ? r.Units >= x.Units : r.Definition == x.Definition && r.Count >= x.Count));
            if (debt != null) { NoteHouseService(debt.RequesterId,FamilyOf(debt.DonorId),2,now); debt.DebtPaid = true; debt.State = SupplyState.Repaid; ChangeBond(debt.DonorId, debt.RequesterId, new BondChange(1, 4, -3, 1), now); }
            return true;
        }

        private static bool MarkRelocated(SupplyRequest r, int thing)
        { r.State = SupplyState.Exchanged; r.SourceId = thing; return true; }

        public void MaintainSupplies(int now)
        {
            foreach (var r in Requests)
            {
                if (r.Open && (now > r.DueTick || GetPerson(r.RequesterId)?.InColony != true)) r.State = SupplyState.Cancelled;
                if (r.Approach == SupplyApproach.Loan && !r.DebtPaid && (r.State == SupplyState.Exchanged || r.State == SupplyState.Stored) && now > r.DueTick)
                { r.State = SupplyState.Defaulted; ChangeBond(r.DonorId, r.RequesterId, new BondChange(-2, -6, 6, 1), now); }
            }
        }
    }
}
