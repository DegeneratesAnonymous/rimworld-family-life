using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum StewardDutyKind : byte { OrganizeCeremony, ReceivePetition, Negotiate, GuestCare, Mediate, SupplyShortage }
    public enum StewardDutyState : byte { Assigned, Completed, Failed, Interrupted, Lapsed }
    /// <summary>Decisions that stay with the head unless a house's institutions later delegate them.</summary>
    public enum HouseDecision : byte { Membership, MarriageUnion, Succession, Routine }

    /// <summary>One piece of office work. It counts as a success only when evidence of a completed action is presented.</summary>
    public sealed partial class StewardDuty
    {
        public int Id, FamilyId, HolderId, TargetId, StartedTick, EndedTick, EvidenceToken;
        public StewardDutyKind Kind;
        public StewardDutyState State;
        /// <summary>True when the Steward was unavailable and the head took the work instead. Such work is never credited to a Steward term.</summary>
        public bool HeadActed;
        public bool Open => State == StewardDutyState.Assigned;
    }

    /// <summary>An optional preferred meeting spot. It is a venue preference, never a building or a requirement.</summary>
    public sealed partial class FamilyOffice
    {
        public int FamilyId, MapId, X, Z, SetTick;
    }

    public sealed partial class FamilyRegistry
    {
        public const int StewardDutyLifeTicks = 2 * ActivityTuning.TicksPerDay;
        public List<StewardDuty> StewardDuties = new List<StewardDuty>();
        public List<FamilyOffice> FamilyOffices = new List<FamilyOffice>();
        public int NextStewardDutyId = 1;

        /// <summary>
        /// Whoever should do the work. The Steward when able; otherwise the head. Anyone the game reports as not free (drafted, ordered,
        /// or an avatar the player is playing) is never given duties, so neither role overrides player or avatar agency.
        /// </summary>
        public int DutyHolder(int family, Func<int, bool> free, out bool headActed)
        {
            headActed = false;
            bool Able(PersonRecord p) => p != null && p.InColony && !p.Juvenile && BelongsToFamily(p, family) && (free == null || free(p.Id));
            var house = GetLineage(family);
            if (house == null || !IsActiveHouse(family)) return 0;
            var steward = house.Stewards.Select(GetPerson).Where(Able).OrderByDescending(p => StewardshipOf(p.Id, family, true)?.Successes ?? 0).ThenBy(p => p.Id).FirstOrDefault();
            if (steward != null && RoleInHouse(steward.Id, family) == HouseRole.Steward) return steward.Id;
            var head = GetPerson(HeadOf(family));
            if (Able(head)) { headActed = true; return head.Id; }
            return 0;
        }

        /// <summary>The head holds final say over membership, unions and succession. A Steward prepares and carries these out but never decides them.</summary>
        public bool MayDecide(int person, int family, HouseDecision decision)
        {
            if (GetPerson(person)?.InColony != true) return false;
            if (decision == HouseDecision.Routine) return CanManageHouse(person, family);
            return HeadOf(family) == person;
        }

        /// <summary>
        /// Picks at most one duty that the house's actual situation calls for. This reads existing records only (requests, petitions,
        /// negotiations, shortages, rifts); it never scans the map and never invents work.
        /// </summary>
        public bool TryChooseDuty(int family, int guestsPresent, out StewardDutyKind kind, out int target)
        {
            kind = StewardDutyKind.OrganizeCeremony; target = 0;
            var house = GetLineage(family);
            if (house == null || !IsActiveHouse(family)) return false;
            bool Taken(StewardDutyKind k, int t) => StewardDuties.Any(d => d.Open && d.FamilyId == family && d.Kind == k && d.TargetId == t);
            var petition = HousePetitions.Where(p => p.FamilyId == family && p.State == PetitionState.Proposed && p.ReceivedBy == 0).OrderBy(p => p.CreatedTick).FirstOrDefault();
            if (petition != null && !Taken(StewardDutyKind.ReceivePetition, petition.Id)) { kind = StewardDutyKind.ReceivePetition; target = petition.Id; return true; }
            var shortage = Requests.Where(r => r.Open && r.Approach != SupplyApproach.Steal && FamilyOf(r.RequesterId) == family).OrderBy(r => r.CreatedTick).FirstOrDefault();
            if (shortage != null && !Taken(StewardDutyKind.SupplyShortage, shortage.Id)) { kind = StewardDutyKind.SupplyShortage; target = shortage.Id; return true; }
            var request = GatheringRequests.Where(r => r.LineageId == family && r.State == CeremonyState.Planned && r.OrganizerId == 0).OrderBy(r => r.NotBefore).FirstOrDefault();
            if (request != null && !Taken(StewardDutyKind.OrganizeCeremony, request.Id)) { kind = StewardDutyKind.OrganizeCeremony; target = request.Id; return true; }
            var alliance = MarriageAlliances.FirstOrDefault(a => (a.FamilyA == family || a.FamilyB == family) && (a.State == MarriageAllianceState.Opportunity || a.State == MarriageAllianceState.Negotiating) && !Taken(StewardDutyKind.Negotiate, a.Id));
            if (alliance != null && house.Ambition?.Primary != HouseAmbitionKind.Contentment) { kind = StewardDutyKind.Negotiate; target = alliance.Id; return true; }
            var rift = Rifts.FirstOrDefault(r => r.ResolvedTick == 0 && FamilyOf(r.A) == family && FamilyOf(r.B) == family && !Taken(StewardDutyKind.Mediate, r.Id));
            if (rift != null) { kind = StewardDutyKind.Mediate; target = rift.Id; return true; }
            if (guestsPresent > 0 && !Taken(StewardDutyKind.GuestCare, 0)) { kind = StewardDutyKind.GuestCare; return true; }
            return false;
        }

        public StewardDuty BeginStewardDuty(int family, StewardDutyKind kind, int target, Func<int, bool> free, int now)
        {
            int holder = DutyHolder(family, free, out bool headActed);
            if (holder == 0 || StewardDuties.Any(d => d.Open && d.HolderId == holder) ||
                StewardDuties.Any(d => d.Open && d.FamilyId == family && d.Kind == kind && d.TargetId == target)) return null;
            if (StewardDuties.Any(d => d.HolderId == holder && now - d.StartedTick < ActivityTuning.TicksPerDay / 2)) return null;
            if (StewardDuties.Count >= 256)
            {
                var old = StewardDuties.Where(d => !d.Open).OrderBy(d => d.EndedTick).FirstOrDefault();
                if (old == null) return null;
                StewardDuties.Remove(old);
            }
            var duty = new StewardDuty { Id = NextStewardDutyId++, FamilyId = family, HolderId = holder, Kind = kind, TargetId = target, StartedTick = now, HeadActed = headActed };
            StewardDuties.Add(duty);
            return duty;
        }

        /// <summary>Withdraws a duty that could not be started at all. Nothing was attempted, so nothing is recorded against anyone.</summary>
        public bool CancelStewardDuty(int dutyId)
        {
            var duty = StewardDuties.FirstOrDefault(d => d.Id == dutyId);
            return duty != null && duty.Open && StewardDuties.Remove(duty);
        }

        /// <summary>The Steward (or acting head) hears a petition. Only the head can then decide it.</summary>
        public bool ReceivePetitionAsOffice(int petitionId, int holder, int now)
        {
            var petition = HousePetitions.FirstOrDefault(p => p.Id == petitionId);
            if (petition == null || petition.State != PetitionState.Proposed || !CanManageHouse(holder, petition.FamilyId) || now - petition.CreatedTick > 3 * ActivityTuning.TicksPerDay) return false;
            petition.ReceivedBy = holder;
            return true;
        }

        /// <summary>
        /// Counts the duty only if what it was for actually happened. Ceremonies need the finished ceremony; petitions need a recorded hearing; negotiations need a
        /// decided alliance with this person as negotiator; everything else needs a one-time token that the game produced when the physical work finished.
        /// </summary>
        public bool CompleteStewardDuty(int dutyId, int token, int now)
        {
            var duty = StewardDuties.FirstOrDefault(d => d.Id == dutyId);
            if (duty == null || !duty.Open) return false;
            if (!EvidenceExists(duty, token)) return false;
            duty.State = StewardDutyState.Completed; duty.EndedTick = now; duty.EvidenceToken = token;
            // A head covering for a missing Steward earns public regard but adds nothing to the Steward's term.
            if (!duty.HeadActed && CreditedHere(duty.Kind)) NoteStewardService(duty.HolderId, duty.FamilyId, ServiceOf(duty.Kind), true, now);
            NotePublicService(duty.HolderId, duty.FamilyId, duty.HeadActed ? .5f : 1f, now);
            return true;
        }

        public bool FailStewardDuty(int dutyId, int now)
        {
            var duty = StewardDuties.FirstOrDefault(d => d.Id == dutyId);
            if (duty == null || !duty.Open) return false;
            duty.State = StewardDutyState.Failed; duty.EndedTick = now;
            if (!duty.HeadActed && CreditedHere(duty.Kind)) NoteStewardService(duty.HolderId, duty.FamilyId, ServiceOf(duty.Kind), false, now);
            return true;
        }

        /// <summary>Interruption (downed, drafted, map left) is neither success nor failure. It is recorded so the term shows it plainly.</summary>
        public bool InterruptStewardDuty(int dutyId, int now)
        {
            var duty = StewardDuties.FirstOrDefault(d => d.Id == dutyId);
            if (duty == null || !duty.Open) return false;
            duty.State = StewardDutyState.Interrupted; duty.EndedTick = now;
            var term = StewardshipOf(duty.HolderId, duty.FamilyId, true);
            if (term != null && !duty.HeadActed) term.Interruptions++;
            return true;
        }

        public void MaintainStewardDuties(int now, Func<int, bool> free = null)
        {
            foreach (var duty in StewardDuties.Where(d => d.Open).ToList())
            {
                var holder = GetPerson(duty.HolderId);
                if (holder?.InColony != true || (free != null && !free(duty.HolderId)) || now - duty.StartedTick > StewardDutyLifeTicks)
                    InterruptStewardDuty(duty.Id, now);
            }
        }

        private bool EvidenceExists(StewardDuty duty, int token)
        {
            switch (duty.Kind)
            {
                case StewardDutyKind.OrganizeCeremony:
                    return Ceremonies.Any(c => c.Id == duty.TargetId && c.OrganizerId == duty.HolderId && c.CompletedTick > 0);
                case StewardDutyKind.ReceivePetition:
                    return HousePetitions.Any(p => p.Id == duty.TargetId && p.ReceivedBy == duty.HolderId);
                case StewardDutyKind.Negotiate:
                    return MarriageAlliances.Any(a => a.Id == duty.TargetId && (a.NegotiatorA == duty.HolderId || a.NegotiatorB == duty.HolderId) &&
                        (a.State == MarriageAllianceState.Supported || a.State == MarriageAllianceState.Opposed || a.State == MarriageAllianceState.Consented || a.State == MarriageAllianceState.Fulfilled));
                case StewardDutyKind.SupplyShortage:
                    // The request must really have reached its destination (granted goods carried home) after the duty began.
                    return Requests.Any(r => r.Id == duty.TargetId && r.CreatedTick >= duty.StartedTick - ActivityTuning.TicksPerDay && (r.State == SupplyState.Exchanged || r.State == SupplyState.Stored || r.State == SupplyState.Repaid));
                default:
                    return token > 0 && !TokenUsed(token);
            }
        }

        /// <summary>Ceremonies, aid, reconciliations and negotiations are already credited by the systems that perform them; crediting them again here would double count.</summary>
        private static bool CreditedHere(StewardDutyKind kind) => kind == StewardDutyKind.ReceivePetition || kind == StewardDutyKind.GuestCare || kind == StewardDutyKind.Mediate;

        private bool TokenUsed(int token) => StewardDuties.Any(d => d.EvidenceToken == token && d.State == StewardDutyState.Completed);

        private static StewardServiceKind ServiceOf(StewardDutyKind kind)
        {
            switch (kind)
            {
                case StewardDutyKind.OrganizeCeremony: return StewardServiceKind.Ceremony;
                case StewardDutyKind.ReceivePetition: return StewardServiceKind.Petition;
                case StewardDutyKind.Negotiate: return StewardServiceKind.Negotiation;
                case StewardDutyKind.GuestCare: return StewardServiceKind.GuestCare;
                case StewardDutyKind.Mediate: return StewardServiceKind.Reconciliation;
                default: return StewardServiceKind.SupplyCrisis;
            }
        }

        // ---- the optional office ----

        public FamilyOffice OfficeOf(int family) => FamilyOffices.FirstOrDefault(o => o.FamilyId == family);

        public bool SetFamilyOffice(int family, int mapId, int x, int z, int now)
        {
            if (!IsActiveHouse(family) || mapId < 0) return false;
            var office = OfficeOf(family);
            if (office == null) { if (FamilyOffices.Count >= 64) return false; office = new FamilyOffice { FamilyId = family }; FamilyOffices.Add(office); }
            office.MapId = mapId; office.X = x; office.Z = z; office.SetTick = now;
            return true;
        }

        public bool ClearFamilyOffice(int family) => FamilyOffices.RemoveAll(o => o.FamilyId == family) > 0;

        /// <summary>Removes offices whose house is gone or whose map no longer exists. Duties then simply meet wherever the house normally gathers.</summary>
        public void PruneOffices(Func<int, bool> mapExists)
            => FamilyOffices.RemoveAll(o => !IsActiveHouse(o.FamilyId) || (mapExists != null && !mapExists(o.MapId)));

        /// <summary>The preferred meeting place if it is still usable, else null. Callers fall back to any suitable family area.</summary>
        public FamilyOffice UsableOffice(int family, Func<int, int, int, bool> valid)
        {
            var office = OfficeOf(family);
            return office != null && (valid == null || valid(office.MapId, office.X, office.Z)) ? office : null;
        }
    }
}
