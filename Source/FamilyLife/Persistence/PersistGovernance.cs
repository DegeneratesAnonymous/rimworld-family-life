using System.Collections.Generic;
using FamilyLife.Domain;
using Verse;

// Save/load for the planning, office, succession, crime and institution records. Ids and numbers only; every list recovers from null.
namespace FamilyLife.Domain
{
    public sealed partial class CampMoment : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref ActorId, "actor"); Scribe_Values.Look(ref OtherId, "other"); Scribe_Values.Look(ref Tick, "tick");
            Scribe_Values.Look(ref Outcome, "outcome"); Scribe_Values.Look(ref Kind, "kind", CampMomentKind.FiresideTalk);
        }
    }

    public sealed partial class PlanInvitee : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref PersonId, "person"); Scribe_Values.Look(ref ArrivalTick, "arrival");
            Scribe_Values.Look(ref Source, "source", InviteSource.Family); Scribe_Values.Look(ref Status, "status", InviteStatus.Invited);
            Scribe_Values.Look(ref Barriers, "barriers", InviteBarrier.None); Scribe_Values.Look(ref ByPlayer, "byPlayer");
        }
    }

    public sealed partial class CeremonyPlan : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref RequestId, "request"); Scribe_Values.Look(ref FamilyId, "family");
            Scribe_Values.Look(ref OpenedTick, "opened"); Scribe_Values.Look(ref DeadlineTick, "deadline"); Scribe_Values.Look(ref EarliestTick, "earliest");
            Scribe_Values.Look(ref LatestTick, "latest"); Scribe_Values.Look(ref ScheduledTick, "scheduled"); Scribe_Values.Look(ref OrganizerId, "organizer");
            Scribe_Values.Look(ref VenueMapId, "venueMap", -1); Scribe_Values.Look(ref VenueAreaId, "venueArea"); Scribe_Values.Look(ref AvatarId, "avatar");
            Scribe_Values.Look(ref State, "state", PlanState.Open); Scribe_Values.Look(ref Ask, "ask", AvatarAsk.None); Scribe_Values.Look(ref PlayerAdjusted, "playerAdjusted");
            Scribe_Collections.Look(ref Invitees, "invitees", LookMode.Deep); Scribe_Collections.Look(ref KnownBy, "knownBy", LookMode.Value);
            if (Invitees == null) Invitees = new List<PlanInvitee>();
            if (KnownBy == null) KnownBy = new List<int>();
        }
    }

    public sealed partial class StewardDuty : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref FamilyId, "family"); Scribe_Values.Look(ref HolderId, "holder"); Scribe_Values.Look(ref TargetId, "target");
            Scribe_Values.Look(ref StartedTick, "started"); Scribe_Values.Look(ref EndedTick, "ended"); Scribe_Values.Look(ref EvidenceToken, "evidence");
            Scribe_Values.Look(ref Kind, "kind", StewardDutyKind.OrganizeCeremony); Scribe_Values.Look(ref State, "state", StewardDutyState.Assigned);
            Scribe_Values.Look(ref HeadActed, "headActed");
        }
    }

    public sealed partial class FamilyOffice : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref FamilyId, "family"); Scribe_Values.Look(ref MapId, "map", -1); Scribe_Values.Look(ref X, "x");
            Scribe_Values.Look(ref Z, "z"); Scribe_Values.Look(ref SetTick, "set");
        }
    }

    public sealed partial class SuccessionCase : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref FamilyId, "family"); Scribe_Values.Look(ref FormerHeadId, "former");
            Scribe_Values.Look(ref StartedTick, "started"); Scribe_Values.Look(ref StageTick, "stageTick"); Scribe_Values.Look(ref ChosenId, "chosen");
            Scribe_Values.Look(ref RunnerUpId, "runnerUp"); Scribe_Values.Look(ref BranchHouseId, "branch");
            Scribe_Values.Look(ref Stage, "stage", SuccessionStage.Orderly); Scribe_Values.Look(ref Custom, "custom", SuccessionCustom.Seniority);
            Scribe_Values.Look(ref Dispute, "dispute"); Scribe_Values.Look(ref DecidingFactors, "factors", LegitimacyFactor.None);
            Scribe_Collections.Look(ref Declined, "declined", LookMode.Value);
            if (Declined == null) Declined = new List<int>();
        }
    }

    public sealed partial class CrimeRecord : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Direction, "direction", CrimeDirection.None); Scribe_Values.Look(ref Status, "status", CrimeStatus.None);
            Scribe_Values.Look(ref StartedTick, "started"); Scribe_Values.Look(ref LastPlanTick, "lastPlan", -1000000); Scribe_Values.Look(ref EndedTick, "ended");
            Scribe_Values.Look(ref Heat, "heat"); Scribe_Values.Look(ref ReformProgress, "reform"); Scribe_Values.Look(ref LastAmendsIncident, "lastAmends");
        }
    }

    public sealed partial class HeistParticipant : IExposable
    {
        public void ExposeData() { Scribe_Values.Look(ref PersonId, "person"); Scribe_Values.Look(ref Stance, "stance", CrimeStance.Unasked); }
    }

    public sealed partial class Heist : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref FamilyId, "family"); Scribe_Values.Look(ref OrganizerId, "organizer");
            Scribe_Values.Look(ref TargetFamilyId, "targetFamily"); Scribe_Values.Look(ref TargetMapId, "targetMap", -1); Scribe_Values.Look(ref TargetToken, "targetToken");
            Scribe_Values.Look(ref PlannedTick, "planned"); Scribe_Values.Look(ref StateTick, "stateTick"); Scribe_Values.Look(ref GoodsMoved, "goods");
            Scribe_Values.Look(ref IncidentId, "incident"); Scribe_Values.Look(ref LaunderedThingId, "laundered"); Scribe_Collections.Look(ref RequestIds, "requests", LookMode.Value);
            if (RequestIds == null) RequestIds = new List<int>(); Scribe_Values.Look(ref EvidenceToken, "evidence"); Scribe_Values.Look(ref Risk, "risk");
            Scribe_Values.Look(ref Direction, "direction", CrimeDirection.None); Scribe_Values.Look(ref State, "state", HeistState.Planning);
            Scribe_Collections.Look(ref Participants, "participants", LookMode.Deep);
            if (Participants == null) Participants = new List<HeistParticipant>();
        }
    }

    public sealed partial class CrimeKnowledge : IExposable
    {
        public void ExposeData() { Scribe_Values.Look(ref HeistId, "heist"); Scribe_Values.Look(ref PersonId, "person"); Scribe_Values.Look(ref Level, "level", CrimeKnowledgeLevel.None); }
    }

    public sealed partial class ProtectionArrangement : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref ProtectorFamily, "protector"); Scribe_Values.Look(ref ProtectedFamily, "protected");
            Scribe_Values.Look(ref AgreedTick, "agreed"); Scribe_Values.Look(ref EndedTick, "ended"); Scribe_Values.Look(ref Payments, "payments");
            Scribe_Values.Look(ref PaymentsRecorded, "recorded"); Scribe_Values.Look(ref LastPaymentToken, "lastToken"); Scribe_Values.Look(ref TributeAmount, "tribute");
            Scribe_Values.Look(ref AgreedBy, "agreedBy"); Scribe_Values.Look(ref LastRequestId, "lastRequest"); Scribe_Values.Look(ref LastCollectionTick, "lastCollection"); Scribe_Values.Look(ref Coerced, "coerced");
        }
    }

    public sealed partial class ColonyInstitution : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Kind, "kind", InstitutionKind.Consensus); Scribe_Values.Look(ref SinceTick, "since");
            Scribe_Values.Look(ref LeaderId, "leader"); Scribe_Values.Look(ref TermEndTick, "termEnd");
            Scribe_Collections.Look(ref CouncilHouseIds, "council", LookMode.Value);
            Scribe_Values.Look(ref PollMapId, "pollMap", -1); Scribe_Values.Look(ref PollX, "pollX"); Scribe_Values.Look(ref PollZ, "pollZ");
            if (CouncilHouseIds == null) CouncilHouseIds = new List<int>();
        }
    }

    public sealed partial class InstitutionMovement : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref ProposerId, "proposer"); Scribe_Values.Look(ref StartedTick, "started");
            Scribe_Values.Look(ref DeadlineTick, "deadline"); Scribe_Values.Look(ref EndedTick, "ended"); Scribe_Values.Look(ref From, "from", InstitutionKind.Consensus);
            Scribe_Values.Look(ref Target, "target", InstitutionKind.Council); Scribe_Values.Look(ref State, "state", InstitutionMovementState.Organizing);
            Scribe_Collections.Look(ref SupportHouseIds, "support", LookMode.Value); Scribe_Collections.Look(ref OpposeHouseIds, "oppose", LookMode.Value);
            Scribe_Collections.Look(ref ContactedHouseIds, "contacted", LookMode.Value);
            SupportHouseIds = SupportHouseIds ?? new List<int>(); OpposeHouseIds = OpposeHouseIds ?? new List<int>(); ContactedHouseIds = ContactedHouseIds ?? new List<int>();
        }
    }

    public sealed partial class ElectionEndorsement : IExposable
    {
        public void ExposeData() { Scribe_Values.Look(ref FamilyId, "family"); Scribe_Values.Look(ref EndorserId, "endorser"); Scribe_Values.Look(ref CandidateId, "candidate"); Scribe_Values.Look(ref Weight, "weight"); }
    }

    public sealed partial class ElectionVote : IExposable
    {
        public void ExposeData() { Scribe_Values.Look(ref VoterId, "voter"); Scribe_Values.Look(ref CandidateId, "candidate"); Scribe_Values.Look(ref Reason, "reason", PoliticalFactor.None); }
    }

    public sealed partial class Election : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref StartedTick, "started"); Scribe_Values.Look(ref VotingTick, "voting");
            Scribe_Values.Look(ref CountedTick, "counted"); Scribe_Values.Look(ref WinnerId, "winner"); Scribe_Values.Look(ref Margin, "margin"); Scribe_Values.Look(ref Abstained, "abstained");
            Scribe_Values.Look(ref State, "state", ElectionState.Campaign);
            Scribe_Collections.Look(ref Candidates, "candidates", LookMode.Value); Scribe_Collections.Look(ref Pending, "pending", LookMode.Value); Scribe_Values.Look(ref Council, "council"); Scribe_Collections.Look(ref Endorsements, "endorsements", LookMode.Deep);
            Scribe_Collections.Look(ref Votes, "votes", LookMode.Deep); Scribe_Collections.Look(ref Voted, "voted", LookMode.Value);
            if (Candidates == null) Candidates = new List<int>();
            if (Pending == null) Pending = new List<int>();
            if (Endorsements == null) Endorsements = new List<ElectionEndorsement>();
            if (Votes == null) Votes = new List<ElectionVote>();
            if (Voted == null) Voted = new List<int>();
        }
    }

    public sealed partial class CoupParticipant : IExposable
    {
        public void ExposeData() { Scribe_Values.Look(ref PersonId, "person"); Scribe_Values.Look(ref Stance, "stance", CrimeStance.Unasked); }
    }

    public sealed partial class CoupPlan : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref OrganizerId, "organizer"); Scribe_Values.Look(ref TargetLeaderId, "target");
            Scribe_Values.Look(ref PolicyId, "policy"); Scribe_Values.Look(ref StartedTick, "started"); Scribe_Values.Look(ref StageTick, "stageTick");
            Scribe_Values.Look(ref Setbacks, "setbacks"); Scribe_Values.Look(ref EndedTick, "ended"); Scribe_Values.Look(ref DiscoveredBy, "discoveredBy");
            Scribe_Values.Look(ref Goal, "goal", CoupGoal.ReplaceLeader); Scribe_Values.Look(ref Stage, "stage", CoupStage.Recruiting);
            Scribe_Values.Look(ref State, "state", CoupState.Active); Scribe_Values.Look(ref Discovery, "discovery");
            Scribe_Collections.Look(ref Participants, "participants", LookMode.Deep);
            if (Participants == null) Participants = new List<CoupParticipant>();
        }
    }

    public sealed partial class FavorDebt : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref CreditorFamily, "creditor"); Scribe_Values.Look(ref DebtorId, "debtor"); Scribe_Values.Look(ref DebtorFamily, "debtorFamily");
            Scribe_Values.Look(ref GiftIncidentId, "gift"); Scribe_Values.Look(ref CreatedTick, "created"); Scribe_Values.Look(ref CalledTick, "called");
            Scribe_Values.Look(ref EndedTick, "ended"); Scribe_Values.Look(ref RequestId, "request"); Scribe_Values.Look(ref State, "state", FavorState.Owed);
        }
    }

    public sealed partial class CouncilMatter : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref FamilyA, "familyA"); Scribe_Values.Look(ref FamilyB, "familyB");
            Scribe_Values.Look(ref RequestedTick, "requested"); Scribe_Values.Look(ref DecidedTick, "decided"); Scribe_Values.Look(ref Yes, "yes"); Scribe_Values.Look(ref No, "no");
            Scribe_Values.Look(ref Kind, "kind", CouncilMatterKind.Union); Scribe_Values.Look(ref State, "state", CouncilMatterState.Pending);
            Scribe_Collections.Look(ref Voted, "voted", LookMode.Value);
            if (Voted == null) Voted = new List<int>();
        }
    }

    public sealed partial class CommunityOffice : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref HolderId, "holder"); Scribe_Values.Look(ref HolderFamily, "holderFamily"); Scribe_Values.Look(ref ElectedTick, "elected");
            Scribe_Values.Look(ref TermEndTick, "termEnd"); Scribe_Values.Look(ref LastWorkTick, "lastWork"); Scribe_Values.Look(ref Work, "work"); Scribe_Values.Look(ref DayStamp, "dayStamp");
            Scribe_Values.Look(ref ActsToday, "actsToday"); Scribe_Values.Look(ref Counter, "counter"); Scribe_Values.Look(ref Backers, "backers"); Scribe_Values.Look(ref Opponents, "opponents");
            Scribe_Values.Look(ref UnmetSinceTick, "unmetSince"); Scribe_Values.Look(ref NextVoteTick, "nextVote"); Scribe_Values.Look(ref LastHolderId, "lastHolder");
            Scribe_Values.Look(ref BarredId, "barred"); Scribe_Values.Look(ref BarredUntil, "barredUntil"); Scribe_Values.Look(ref EndedTick, "ended");
            Scribe_Values.Look(ref Terms, "terms"); Scribe_Values.Look(ref QualityCount, "qualityCount"); Scribe_Values.Look(ref QualitySum, "qualitySum"); Scribe_Values.Look(ref LastGrade, "lastGrade");
            Scribe_Values.Look(ref NextRoundTick, "nextRound"); Scribe_Values.Look(ref RoundStartTick, "roundStart");
            Scribe_Values.Look(ref NextSessionTick, "nextSession"); Scribe_Values.Look(ref SessionStartTick, "sessionStart");
            Scribe_Values.Look(ref PendingKind, "pendingKind"); Scribe_Values.Look(ref PendingTick, "pendingTick"); Scribe_Values.Look(ref LastIllnessTick, "lastIllness");
            Scribe_Values.Look(ref LastTestingYear, "lastTestingYear"); Scribe_Values.Look(ref IllnessRound, "illnessRound");
            Scribe_Values.Look(ref TestingStartTick, "testingStart"); Scribe_Values.Look(ref TestingSlot, "testingSlot");
            Scribe_Collections.Look(ref TestIds, "testIds", LookMode.Value); Scribe_Collections.Look(ref TestPass, "testPass", LookMode.Value); Scribe_Collections.Look(ref TestTry, "testTry", LookMode.Value);
            Scribe_Values.Look(ref CounterReady, "counterReady"); Scribe_Values.Look(ref Kind, "kind", OfficeKind.Healer); Scribe_Values.Look(ref EndReason, "endReason", OfficeEnd.None);
            Scribe_Collections.Look(ref Tokens, "tokens", LookMode.Value); Scribe_Collections.Look(ref Examined, "examined", LookMode.Value); Scribe_Collections.Look(ref SessionQueue, "sessionQueue", LookMode.Value);
            if (Tokens == null) Tokens = new List<int>();
            if (Examined == null) Examined = new List<int>();
            if (SessionQueue == null) SessionQueue = new List<int>();
            if (TestIds == null) TestIds = new List<int>();
            if (TestPass == null) TestPass = new List<int>();
            if (TestTry == null) TestTry = new List<int>();
        }
    }

    public sealed partial class AssemblyEndorsement : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref FamilyId, "family"); Scribe_Values.Look(ref EndorserId, "endorser"); Scribe_Values.Look(ref CandidateId, "candidate"); Scribe_Values.Look(ref Kind, "kind", OfficeKind.Healer);
        }
    }

    public sealed partial class AssemblyRace : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Kind, "kind", OfficeKind.Healer); Scribe_Values.Look(ref IncumbentId, "incumbent"); Scribe_Values.Look(ref WinnerId, "winner");
            Scribe_Values.Look(ref Abstained, "abstained"); Scribe_Values.Look(ref IncumbentGrade, "incumbentGrade"); Scribe_Values.Look(ref Margin, "margin");
            Scribe_Collections.Look(ref Candidates, "candidates", LookMode.Value); Scribe_Collections.Look(ref Motives, "motives", LookMode.Value); Scribe_Collections.Look(ref Votes, "votes", LookMode.Deep);
            if (Candidates == null) Candidates = new List<int>();
            if (Motives == null) Motives = new List<int>();
            if (Votes == null) Votes = new List<ElectionVote>();
        }
    }

    public sealed partial class CommunityAssembly : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref StartedTick, "started"); Scribe_Values.Look(ref VotingTick, "voting"); Scribe_Values.Look(ref CountedTick, "counted");
            Scribe_Values.Look(ref State, "state", AssemblyState.Campaign); Scribe_Values.Look(ref Convened, "convened");
            Scribe_Collections.Look(ref Races, "races", LookMode.Deep); Scribe_Collections.Look(ref Voted, "voted", LookMode.Value); Scribe_Collections.Look(ref Endorsements, "endorsements", LookMode.Deep);
            if (Races == null) Races = new List<AssemblyRace>();
            if (Voted == null) Voted = new List<int>();
            if (Endorsements == null) Endorsements = new List<AssemblyEndorsement>();
        }
    }

    public sealed partial class CampTrip : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref FamilyId, "family"); Scribe_Values.Look(ref OrganizerId, "organizer"); Scribe_Values.Look(ref ProposedTick, "proposed");
            Scribe_Values.Look(ref ExpiresTick, "expires"); Scribe_Values.Look(ref AuthorizedTick, "authorized"); Scribe_Values.Look(ref ArrivedTick, "arrived");
            Scribe_Values.Look(ref EndedTick, "ended"); Scribe_Values.Look(ref Tile, "tile", -1); Scribe_Values.Look(ref StayTicks, "stay"); Scribe_Values.Look(ref Seed, "seed");
            Scribe_Values.Look(ref SubjectId, "subject"); Scribe_Values.Look(ref Quality, "quality"); Scribe_Values.Look(ref Motive, "motive", CampMotive.Recreation);
            Scribe_Values.Look(ref State, "state", CampState.Proposed);
            Scribe_Collections.Look(ref Suggested, "suggested", LookMode.Value); Scribe_Collections.Look(ref Members, "members", LookMode.Value); Scribe_Collections.Look(ref Moments, "moments", LookMode.Deep);
            if (Suggested == null) Suggested = new List<int>();
            if (Members == null) Members = new List<int>();
            if (Moments == null) Moments = new List<CampMoment>();
        }
    }

    public sealed partial class KinVisit : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref FamilyId, "family"); Scribe_Values.Look(ref RequestId, "request"); Scribe_Values.Look(ref CreatedTick, "created");
            Scribe_Values.Look(ref ArriveTick, "arrive"); Scribe_Values.Look(ref StayEndTick, "stayEnd"); Scribe_Values.Look(ref StrandTile, "strandTile", -1);
            Scribe_Values.Look(ref RescueDeadlineTick, "rescueDeadline"); Scribe_Values.Look(ref EndedTick, "ended"); Scribe_Values.Look(ref Seed, "seed");
            Scribe_Values.Look(ref Complication, "complication"); Scribe_Values.Look(ref State, "state", KinVisitState.Travelling);
            Scribe_Collections.Look(ref KinIds, "kin", LookMode.Value); Scribe_Collections.Look(ref ArrivedIds, "arrivedIds", LookMode.Value); Scribe_Collections.Look(ref LandingIds, "landingIds", LookMode.Value);
            Scribe_Values.Look(ref Rescue, "rescue", RescueStage.None); Scribe_Values.Look(ref Trouble, "trouble", RescueComplication.None);
            Scribe_Values.Look(ref QuestId, "questId"); Scribe_Values.Look(ref SiteId, "siteId"); Scribe_Values.Look(ref OfferExpiryTick, "offerExpiry");
            Scribe_Values.Look(ref AcceptedTick, "acceptedTick"); Scribe_Values.Look(ref ReachedTick, "reachedTick"); Scribe_Values.Look(ref RescueEndedTick, "rescueEnded");
            Scribe_Values.Look(ref SiteVisits, "siteVisits");
            Scribe_Collections.Look(ref RescuerIds, "rescuers", LookMode.Value); Scribe_Collections.Look(ref LostIds, "lostIds", LookMode.Value);
            if (KinIds == null) KinIds = new List<int>();
            if (ArrivedIds == null) ArrivedIds = new List<int>();
            if (LandingIds == null) LandingIds = new List<int>();
            if (RescuerIds == null) RescuerIds = new List<int>();
            if (LostIds == null) LostIds = new List<int>();
        }
    }
    public sealed partial class PersonalItem : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref ThingId, "thing"); Scribe_Values.Look(ref OwnerId, "owner"); Scribe_Values.Look(ref HouseId, "house"); Scribe_Values.Look(ref RecordedTick, "recorded");
            Scribe_Values.Look(ref LastNoteTick, "lastNote"); Scribe_Values.Look(ref HeldHours, "heldHours"); Scribe_Values.Look(ref Quality, "quality", -1);
            Scribe_Values.Look(ref DefName, "def", ""); Scribe_Values.Look(ref Label, "label", ""); Scribe_Values.Look(ref Value, "value");
            Scribe_Values.Look(ref Kind, "kind", PersonalItemKind.Other); Scribe_Values.Look(ref Heirloom, "heirloom"); Scribe_Values.Look(ref Basis, "basis", HeirloomBasis.None);
            Scribe_Collections.Look(ref UsedBy, "usedBy", LookMode.Value);
            if (UsedBy == null) UsedBy = new List<int>();
            if (DefName == null) DefName = ""; if (Label == null) Label = "";
        }
    }

    public sealed partial class Beneficiary : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref OwnerId, "owner"); Scribe_Values.Look(ref HeirId, "heir"); Scribe_Values.Look(ref ThingId, "thing"); Scribe_Values.Look(ref Tick, "tick");
            Scribe_Collections.Look(ref KnownBy, "knownBy", LookMode.Value);
            if (KnownBy == null) KnownBy = new List<int>();
        }
    }

    public sealed partial class EstateClaim : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref ClaimantId, "claimant"); Scribe_Values.Look(ref Basis, "basis", ClaimBasis.None); Scribe_Values.Look(ref Strength, "strength");
            Scribe_Values.Look(ref Raised, "raised"); Scribe_Values.Look(ref Relinquished, "relinquished"); Scribe_Values.Look(ref Dormant, "dormant"); Scribe_Values.Look(ref Pressed, "pressed");
        }
    }

    public sealed partial class EstateItem : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref ThingId, "thing"); Scribe_Values.Look(ref HeirId, "heir"); Scribe_Values.Look(ref RecipientHouseId, "houseHeir"); Scribe_Values.Look(ref ProposedHeirId, "proposed");
            Scribe_Values.Look(ref Attempts, "attempts"); Scribe_Values.Look(ref NextTryTick, "nextTry"); Scribe_Values.Look(ref DeliveryStartTick, "deliveryStart");
            Scribe_Values.Look(ref BlockedSince, "blockedSince"); Scribe_Values.Look(ref HouseId, "house"); Scribe_Values.Look(ref ClosedTick, "closed");
            Scribe_Values.Look(ref DefName, "def", ""); Scribe_Values.Look(ref Label, "label", ""); Scribe_Values.Look(ref Kind, "kind", PersonalItemKind.Other);
            Scribe_Values.Look(ref Heirloom, "heirloom"); Scribe_Values.Look(ref Contested, "contested"); Scribe_Values.Look(ref Value, "value");
            Scribe_Values.Look(ref Outcome, "outcome", ItemOutcome.Pending); Scribe_Values.Look(ref Lost, "lost", LostReason.None);
            Scribe_Collections.Look(ref Claims, "claims", LookMode.Deep); Scribe_Collections.Look(ref UsedBy, "usedBy", LookMode.Value);
            if (Claims == null) Claims = new List<EstateClaim>();
            if (UsedBy == null) UsedBy = new List<int>();
            if (DefName == null) DefName = ""; if (Label == null) Label = "";
        }
    }

    public sealed partial class EstateAsk : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref ItemThingId, "thing"); Scribe_Values.Look(ref PersonId, "person"); Scribe_Values.Look(ref AskedTick, "asked"); Scribe_Values.Look(ref ExpiresTick, "expires");
            Scribe_Values.Look(ref Kind, "kind", EstateAskKind.Heir); Scribe_Values.Look(ref Answered, "answered"); Scribe_Values.Look(ref Expired, "expired");
        }
    }

    public sealed partial class EstateCase : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id"); Scribe_Values.Look(ref DeceasedId, "deceased"); Scribe_Values.Look(ref FamilyId, "family"); Scribe_Values.Look(ref OpenedTick, "opened");
            Scribe_Values.Look(ref StageTick, "stageTick"); Scribe_Values.Look(ref ClosedTick, "closed"); Scribe_Values.Look(ref MediatorId, "mediator");
            Scribe_Values.Look(ref Stage, "stage", EstateStage.Mourning); Scribe_Values.Look(ref MediatorAsked, "mediatorAsked"); Scribe_Values.Look(ref Compromised, "compromised");
            Scribe_Values.Look(ref StewardAppointedId, "steward"); Scribe_Values.Look(ref GrievanceCount, "grievances");
            Scribe_Collections.Look(ref Items, "items", LookMode.Deep); Scribe_Collections.Look(ref Asks, "asks", LookMode.Deep);
            if (Items == null) Items = new List<EstateItem>();
            if (Asks == null) Asks = new List<EstateAsk>();
        }
    }
}
