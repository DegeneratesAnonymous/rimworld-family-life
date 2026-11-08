using System.Collections.Generic;
using FamilyLife.Domain;
using Verse;

// Save/load for the pure domain classes. Kept in partial classes so the Domain folder never references the game.
// Rules: ids and numbers only (no Pawn references), every field has a default, and nothing here is localised text.
namespace FamilyLife.Domain
{
    public sealed partial class FamilyEvent : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Kind, "k", FamilyEventKind.PersonRecorded);
            Scribe_Values.Look(ref Tick, "t");
            Scribe_Values.Look(ref A, "a");
            Scribe_Values.Look(ref B, "b");
        }
    }

    public sealed partial class PersonRecord : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref Name, "name", "");
            Scribe_Values.Look(ref LastName, "last", "");
            Scribe_Values.Look(ref BirthAbsTick, "birth");
            Scribe_Values.Look(ref Presence, "presence", Presence.Away);
            Scribe_Values.Look(ref EverColonist, "everColonist");
            Scribe_Values.Look(ref Juvenile, "juvenile");
            Scribe_Values.Look(ref FirstSeenTick, "seen");
            Scribe_Values.Look(ref DeathTick, "died", -1);
            Scribe_Values.Look(ref SocialFamilyId, "socialFamily");
            Scribe_Values.Look(ref PrimaryLineageId, "lineage");
            Scribe_Values.Look(ref HouseholdId, "household");
            Scribe_Values.Look(ref LastHouseholdId, "lastHousehold");
            Scribe_Collections.Look(ref ParentIds, "parents", LookMode.Value);
            Scribe_Collections.Look(ref SpouseIds, "spouses", LookMode.Value);
            Scribe_Collections.Look(ref LoverIds, "lovers", LookMode.Value);
            Scribe_Collections.Look(ref ExIds, "exes", LookMode.Value);
            Scribe_Collections.Look(ref SpouseSince, "spouseSince", LookMode.Value);
            Scribe_Values.Look(ref TopSkill, "topSkill", "");
            Scribe_Values.Look(ref TopSkillLevel, "topSkillLevel");
            Scribe_Values.Look(ref AwayTick, "awayTick", -1);
        }
    }

    public sealed partial class Affiliation : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref PersonId, "person");
            Scribe_Values.Look(ref Kind, "kind", AffiliationKind.Spouse);
            Scribe_Values.Look(ref SinceTick, "since");
        }
    }

    public sealed partial class Lineage : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref Name, "name", "");
            Scribe_Values.Look(ref FounderId, "founder");
            Scribe_Values.Look(ref FoundedTick, "founded");
            Scribe_Collections.Look(ref Affiliations, "affiliations", LookMode.Deep);
            Scribe_Collections.Look(ref Culture, "culture", LookMode.Value);
            Scribe_Values.Look(ref LastCultureTick, "lastCultureTick");
            Scribe_Collections.Look(ref Traditions, "traditions", LookMode.Deep);
            Scribe_Values.Look(ref StoriesTold, "storiesTold");
            Scribe_Values.Look(ref MealsShared, "mealsShared");
            Scribe_Values.Look(ref LastGrandReunionTick,"lastGrandReunion");
            Scribe_Values.Look(ref NextCeremonyCheckTick,"nextCeremonyCheck");
            Scribe_Values.Look(ref Standing,"standing");
            Scribe_Values.Look(ref ZoneMapId, "zoneMapId", -1);
            Scribe_Values.Look(ref FoodReserveDays, "foodReserveDays", 1f);
            Scribe_Values.Look(ref FuelReserveDays, "fuelReserveDays", 2f);
            Scribe_Values.Look(ref MedicineReserve, "medicineReserve", 2);
            Scribe_Values.Look(ref EmergencySharing, "emergencySharing", true);
            Scribe_Values.Look(ref UsePublicBarterFunds, "usePublicBarterFunds");
            Scribe_Values.Look(ref ParentHouseId, "parentHouse");
            Scribe_Values.Look(ref AbsorbedIntoId, "absorbedInto");
            Scribe_Values.Look(ref RetiredTick, "retiredTick");
            Scribe_Values.Look(ref HeadId,"head");
            Scribe_Collections.Look(ref Stewards, "stewards", LookMode.Value);
            Scribe_Values.Look(ref CrestPrimary, "crestPrimary", -1);
            Scribe_Values.Look(ref CrestSecondary, "crestSecondary", 7);
            Scribe_Values.Look(ref CrestMark, "crestMark", -1);
            Scribe_Values.Look(ref CrestPattern, "crestPattern");
            Scribe_Values.Look(ref ZoneAreaId, "zoneAreaId");
            Scribe_Collections.Look(ref ZoneApplied, "zoneApplied", LookMode.Value);
            if (ZoneApplied == null) ZoneApplied = new List<int>();
            Scribe_Collections.Look(ref BedDeals, "bedDeals", LookMode.Value);
            if (BedDeals == null) BedDeals = new List<int>();
            Scribe_Deep.Look(ref Situation, "situation");
            Scribe_Deep.Look(ref Ambition, "ambition");
            if (Situation == null) Situation = new HouseSituation();
            if (Ambition == null) Ambition = new HouseAmbitionRecord();
            Scribe_Values.Look(ref VacantHeadId,"vacantHead");Scribe_Values.Look(ref VacantSeenTick,"vacantSeen");
            Scribe_Values.Look(ref Custom,"successionCustom",SuccessionCustom.Seniority);Scribe_Values.Look(ref CustomSet,"successionCustomSet");
            Scribe_Collections.Look(ref Precedent,"successionPrecedent",LookMode.Value);Scribe_Values.Look(ref DesignatedHeirId,"designatedHeir");
            Scribe_Values.Look(ref DesignatedTick,"designatedTick");Scribe_Collections.Look(ref DesignationKnownBy,"designationKnownBy",LookMode.Value);
            Scribe_Deep.Look(ref Crime,"crime");
            if (Precedent == null) Precedent = new List<int> { 0, 0, 0, 0, 0 };
            if (DesignationKnownBy == null) DesignationKnownBy = new List<int>();
            if (Crime == null) Crime = new CrimeRecord();
        }
    }

    public sealed partial class HouseSituation : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref EvaluatedTick, "evaluated");
            Scribe_Values.Look(ref Members, "members"); Scribe_Values.Look(ref Adults, "adults"); Scribe_Values.Look(ref Children, "children");
            Scribe_Values.Look(ref PartneredAdults, "partnered"); Scribe_Values.Look(ref EligibleSingles, "singles");
            Scribe_Values.Look(ref SuitableBeds, "suitableBeds"); Scribe_Values.Look(ref SpareBeds, "spareBeds");
            Scribe_Values.Look(ref Sustenance, "sustenance", 50f); Scribe_Values.Look(ref LivingSpace, "livingSpace", 50f);
            Scribe_Values.Look(ref Safety, "safety", 50f); Scribe_Values.Look(ref Continuity, "continuity", 50f);
            Scribe_Values.Look(ref Capability, "capability", 50f); Scribe_Values.Look(ref Cohesion, "cohesion", 50f);
            Scribe_Values.Look(ref Influence, "influence", 50f); Scribe_Values.Look(ref Independence, "independence", 50f);
            Scribe_Values.Look(ref Legacy, "legacy", 50f); Scribe_Values.Look(ref Power, "power");
            Scribe_Values.Look(ref WeakestCapability, "weakestCapability", HouseCapability.None);
            Scribe_Values.Look(ref ThreateningFamilyId, "threateningFamily"); Scribe_Values.Look(ref CooperativeFamilyId, "cooperativeFamily");
        }
    }

    public sealed partial class HouseAmbitionRecord : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Primary, "primary", HouseAmbitionKind.None); Scribe_Values.Look(ref Secondary, "secondary", HouseAmbitionKind.None);
            Scribe_Values.Look(ref PrimaryReason, "primaryReason", HouseNeed.None); Scribe_Values.Look(ref SecondaryReason, "secondaryReason", HouseNeed.None);
            Scribe_Values.Look(ref StartedTick, "started"); Scribe_Values.Look(ref LastChangedTick, "changed"); Scribe_Values.Look(ref LockedUntilTick, "lockedUntil");
            Scribe_Values.Look(ref TargetFamilyId, "targetFamily"); Scribe_Values.Look(ref TargetCapability, "targetCapability", HouseCapability.None);
            Scribe_Values.Look(ref Urgency, "urgency"); Scribe_Values.Look(ref Progress, "progress");
        }
    }

    public sealed partial class StewardRecord : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref PersonId, "person"); Scribe_Values.Look(ref FamilyId, "family");
            Scribe_Values.Look(ref AppointedTick, "appointed"); Scribe_Values.Look(ref EndedTick, "ended"); Scribe_Values.Look(ref LastServiceTick, "lastService");
            Scribe_Values.Look(ref CeremoniesOrganized, "ceremonies"); Scribe_Values.Look(ref AidTransfers, "aid");
            Scribe_Values.Look(ref Reconciliations, "reconciliations"); Scribe_Values.Look(ref NegotiationsLed, "negotiations");
            Scribe_Values.Look(ref GuestsHoused, "guests"); Scribe_Values.Look(ref ShortagesAverted, "shortages"); Scribe_Values.Look(ref Failures, "failures"); Scribe_Values.Look(ref PetitionsHeard, "petitionsHeard"); Scribe_Values.Look(ref Interruptions, "interruptions");
        }
    }

    public sealed partial class MarriageAlliance : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id,"id");Scribe_Values.Look(ref PawnA,"pawnA");Scribe_Values.Look(ref PawnB,"pawnB");
            Scribe_Values.Look(ref FamilyA,"familyA");Scribe_Values.Look(ref FamilyB,"familyB");Scribe_Values.Look(ref CreatedTick,"created");
            Scribe_Values.Look(ref ExpiresTick,"expires");Scribe_Values.Look(ref LastChangedTick,"changed");
            Scribe_Values.Look(ref NegotiatorA,"negotiatorA");Scribe_Values.Look(ref NegotiatorB,"negotiatorB");
            Scribe_Values.Look(ref HouseAInterest,"houseAInterest");Scribe_Values.Look(ref HouseBInterest,"houseBInterest");
            Scribe_Values.Look(ref PawnAWillingness,"pawnAWillingness");Scribe_Values.Look(ref PawnBWillingness,"pawnBWillingness");
            Scribe_Values.Look(ref Reasons,"reasons",MarriageAllianceReason.None);Scribe_Values.Look(ref ObjectionsA,"objectionsA",MarriageAllianceObjection.None);
            Scribe_Values.Look(ref ObjectionsB,"objectionsB",MarriageAllianceObjection.None);Scribe_Values.Look(ref State,"state",MarriageAllianceState.Opportunity);
            Scribe_Values.Look(ref ConsentA,"consentA",MarriageConsentState.NotAsked);Scribe_Values.Look(ref ConsentB,"consentB",MarriageConsentState.NotAsked);
            Scribe_Values.Look(ref ConsentApproacherA,"consentApproacherA");Scribe_Values.Look(ref ConsentApproacherB,"consentApproacherB");
            Scribe_Values.Look(ref DefianceA,"defianceA");Scribe_Values.Look(ref DefianceB,"defianceB");Scribe_Collections.Look(ref KnownBy,"knownBy",LookMode.Value);
            Scribe_Values.Look(ref UnionState,"unionState",HouseUnionState.None);Scribe_Values.Look(ref UnionTargetFamilyId,"unionTarget");
            Scribe_Values.Look(ref UnionNegotiatorA,"unionNegotiatorA");Scribe_Values.Look(ref UnionNegotiatorB,"unionNegotiatorB");
            if(KnownBy==null)KnownBy=new List<int>();
        }
    }

    public sealed partial class Household : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref FormedTick, "formed");
            Scribe_Collections.Look(ref ResidentIds, "residents", LookMode.Value);
        }
    }

    public sealed partial class Bond : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref From, "from");
            Scribe_Values.Look(ref To, "to");
            Scribe_Values.Look(ref Attachment, "attachment");
            Scribe_Values.Look(ref Trust, "trust");
            Scribe_Values.Look(ref Tension, "tension");
            Scribe_Values.Look(ref Familiarity, "familiarity");
            Scribe_Values.Look(ref LastTick, "last");
            Scribe_Values.Look(ref LastQualityTick, "lastQuality");
            Scribe_Values.Look(ref QualityDay, "qualityDay", -1);
            Scribe_Values.Look(ref QualityTicksToday, "qualityTicksToday");
            Scribe_Values.Look(ref HighTensionSince, "highTensionSince");
            Scribe_Values.Look(ref QualityRecent, "qualityRecent");
            Scribe_Values.Look(ref LastOpinion, "lastOpinion");
            Scribe_Collections.Look(ref Reasons, "reasons", LookMode.Deep);
            if (Reasons == null) Reasons = new List<BondReason>();
            Scribe_Values.Look(ref Tier, "tier");
            Scribe_Values.Look(ref TierSeen, "tierSeen");
            Scribe_Values.Look(ref LastMilestoneTick, "lastMilestone");
        }
    }

    public sealed partial class TraditionRecord : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id", "");
            Scribe_Values.Look(ref AdoptedTick, "adopted");
            Scribe_Values.Look(ref LastHeldTick, "lastHeld");
            Scribe_Values.Look(ref TimesHeld, "timesHeld");
            Scribe_Values.Look(ref Muted, "muted");
        }
    }

    public sealed partial class GatheringRequest : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref Kind, "kind", "");
            Scribe_Values.Look(ref LineageId, "lineage");
            Scribe_Values.Look(ref SubjectId, "subject");
            Scribe_Values.Look(ref NotBefore, "notBefore");
            Scribe_Values.Look(ref Expires, "expires");
            Scribe_Values.Look(ref Attempts, "attempts");
            Scribe_Values.Look(ref Ceremony,"ceremony",CeremonyKind.None);
            Scribe_Values.Look(ref State,"ceremonyState",CeremonyState.Planned);
            Scribe_Values.Look(ref Influences,"influences",CeremonyInfluence.None);
            Scribe_Values.Look(ref SecondLineageId,"secondLineage");
            Scribe_Values.Look(ref SubjectBId,"subjectB");
            Scribe_Values.Look(ref CreatedTick,"created");
            Scribe_Values.Look(ref StartedTick,"started");
            Scribe_Values.Look(ref OrganizerId,"organizer");
            Scribe_Values.Look(ref MapId,"map",-1);
            Scribe_Values.Look(ref Seed,"seed");
            Scribe_Values.Look(ref MomentBudget,"momentBudget");
            Scribe_Values.Look(ref MomentsResolved,"momentsResolved");
            Scribe_Collections.Look(ref InvitedIds,"invited",LookMode.Value);
            Scribe_Collections.Look(ref VisitorIds,"visitors",LookMode.Value);
            if(Scribe.mode==LoadSaveMode.PostLoadInit)
            {
                InvitedIds=InvitedIds??new List<int>();VisitorIds=VisitorIds??new List<int>();
                if(Ceremony==CeremonyKind.None)Ceremony=FamilyRegistry.CeremonyOf(Kind);
            }
        }
    }

    public sealed partial class CeremonyRecord : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id,"id");Scribe_Values.Look(ref Kind,"kind",CeremonyKind.None);
            Scribe_Values.Look(ref LineageId,"lineage");Scribe_Values.Look(ref SecondLineageId,"secondLineage");
            Scribe_Values.Look(ref SubjectId,"subject");Scribe_Values.Look(ref SubjectBId,"subjectB");Scribe_Values.Look(ref OrganizerId,"organizer");
            Scribe_Values.Look(ref StartedTick,"started");Scribe_Values.Look(ref CompletedTick,"completed");Scribe_Values.Look(ref Attendees,"attendees");
            Scribe_Values.Look(ref Visitors,"visitors");Scribe_Values.Look(ref Moments,"moments");Scribe_Values.Look(ref Influences,"influences",CeremonyInfluence.None);
            Scribe_Values.Look(ref Outcome,"outcome");
        }
    }

    public sealed partial class PrestigeRecord : IExposable
    {
        public void ExposeData()
        {Scribe_Values.Look(ref PersonId,"person");Scribe_Values.Look(ref LastTick,"last");Scribe_Values.Look(ref CeremoniesOrganized,"ceremonies");Scribe_Values.Look(ref Value,"value");}
    }

    public sealed partial class Memory : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref LineageId, "lineage");
            Scribe_Values.Look(ref Kind, "kind", MemoryKind.Birth);
            Scribe_Values.Look(ref Tick, "tick");
            Scribe_Values.Look(ref A, "a");
            Scribe_Values.Look(ref B, "b");
            Scribe_Values.Look(ref Weight, "weight");
        }
    }

    public sealed partial class BondReason : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Cause, "cause");
            Scribe_Values.Look(ref Tick, "tick");
            Scribe_Values.Look(ref Delta, "delta");
        }
    }

    public sealed partial class Rift : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref A, "a");
            Scribe_Values.Look(ref B, "b");
            Scribe_Values.Look(ref Cause, "cause", RiftCause.Tension);
            Scribe_Values.Look(ref StartTick, "start");
            Scribe_Values.Look(ref Severity, "severity");
            Scribe_Values.Look(ref ResolvedTick, "resolved");
            Scribe_Values.Look(ref CalmSince, "calmSince");
            Scribe_Values.Look(ref LastTryTick, "lastTry");
        }
    }

    public sealed partial class TimeRecord : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref PersonId, "person");
            Scribe_Values.Look(ref Day, "day", int.MinValue);
            Scribe_Values.Look(ref Ticks, "ticks");
            Scribe_Values.Look(ref LastStoryTick, "lastStory", -1000000);
        }
    }

    public sealed partial class FamilyTie : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref LineageA, "a");
            Scribe_Values.Look(ref LineageB, "b");
            Scribe_Values.Look(ref Familiarity, "familiarity");
            Scribe_Values.Look(ref Goodwill, "goodwill");
            Scribe_Values.Look(ref Grievance, "grievance");
            Scribe_Values.Look(ref Shared, "shared");
            Scribe_Values.Look(ref LastTick, "last");
            Scribe_Values.Look(ref WrongedBy, "wrongedBy");
            Scribe_Values.Look(ref WrongedPerson, "wronged");
            Scribe_Values.Look(ref GrievanceTick, "grievanceTick");
            Scribe_Values.Look(ref LastGatheringTick, "lastGathering");
        }
    }

    public sealed partial class Commitment : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref FromId, "from");
            Scribe_Values.Look(ref ToId, "to");
            Scribe_Values.Look(ref EventId, "event", "");
            Scribe_Values.Look(ref CreatedTick, "created");
            Scribe_Values.Look(ref DueTick, "due");
            Scribe_Values.Look(ref Importance, "importance", 1);
            Scribe_Values.Look(ref State, "state");
        }
    }

    public sealed partial class EventRecord : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref EventId, "event", "");
            Scribe_Values.Look(ref A, "a");
            Scribe_Values.Look(ref B, "b");
            Scribe_Values.Look(ref Tick, "tick");
            Scribe_Values.Look(ref Tier, "tier", EventTier.Flat);
            Scribe_Values.Look(ref Weight, "weight", EventWeight.Ambient);
            Scribe_Values.Look(ref Setting, "setting");
            Scribe_Values.Look(ref Third, "third");
        }
    }

    public sealed partial class FamilyIncident : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref ActorId, "actorId");
            Scribe_Values.Look(ref VictimId, "victimId");
            Scribe_Values.Look(ref ActorFamily, "actorFamily");
            Scribe_Values.Look(ref VictimFamily, "victimFamily");
            Scribe_Values.Look(ref MapId, "mapId");
            Scribe_Values.Look(ref Tick, "tick");
            Scribe_Values.Look(ref ResolvedTick, "resolvedTick");
            Scribe_Values.Look(ref Amount, "amount");
            Scribe_Values.Look(ref Restored, "restored");
            Scribe_Values.Look(ref RepairSteps, "repairSteps");
            Scribe_Values.Look(ref LastRepairTick, "lastRepairTick", -1000000);
            Scribe_Values.Look(ref Resource, "resource", "");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Context, "context");
            Scribe_Values.Look(ref Severity, "severity");
            Scribe_Values.Look(ref Resolved, "resolved");
            Scribe_Collections.Look(ref KnownBy, "knownBy", LookMode.Value);
            Scribe_Collections.Look(ref FamiliesAffected, "familiesAffected", LookMode.Value);
        }
    }

    public sealed partial class RevengePlot : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref IncidentId, "incidentId");
            Scribe_Values.Look(ref OrganizerId, "organizerId");
            Scribe_Values.Look(ref TargetId, "targetId");
            Scribe_Values.Look(ref CreatedTick, "createdTick");
            Scribe_Values.Look(ref LastActionTick, "lastActionTick");
            Scribe_Values.Look(ref Action, "action");
            Scribe_Values.Look(ref State, "state");
            Scribe_Collections.Look(ref Recruits, "recruits", LookMode.Value);
            Scribe_Collections.Look(ref Asked, "asked", LookMode.Value);
            Scribe_Collections.Look(ref AttackJobs, "attackJobs", LookMode.Value);
        }
    }

    public sealed partial class HouseContact : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref PersonId,"personId");
            Scribe_Values.Look(ref FamilyId,"familyId");
            Scribe_Values.Look(ref ContactDays,"contactDays");
            Scribe_Values.Look(ref ServiceDays,"serviceDays");
            Scribe_Values.Look(ref LastContactDay,"lastContactDay",-1);
            Scribe_Values.Look(ref LastPositiveDay,"lastPositiveDay",-1);
            Scribe_Values.Look(ref LastTick,"lastTick");
            Scribe_Values.Look(ref Respect,"respect");
        }
    }
    public sealed partial class HousePetition : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id,"id");
            Scribe_Values.Look(ref PersonId,"personId");
            Scribe_Values.Look(ref FamilyId,"familyId");
            Scribe_Values.Look(ref CreatedTick,"createdTick");
            Scribe_Values.Look(ref HeardTick,"heardTick");
            Scribe_Values.Look(ref HeardBy,"heardBy");
            Scribe_Values.Look(ref SourceFamilyId,"sourceFamily");Scribe_Values.Look(ref AwaitingCouncil,"awaitingCouncil");
            Scribe_Values.Look(ref ReceivedBy,"receivedBy");
            Scribe_Values.Look(ref Kind,"kind");
            Scribe_Values.Look(ref State,"state");
        }
    }

    public sealed partial class AttentionRecord : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Giver, "giver");
            Scribe_Values.Look(ref Recipient, "recipient");
            Scribe_Values.Look(ref Day, "day");
            Scribe_Values.Look(ref Ticks, "ticks");
        }
    }

    public sealed partial class AgencyRecord : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref PersonId, "personId");
            Scribe_Values.Look(ref FamilyId, "familyId");
            Scribe_Values.Look(ref Included, "included");
            Scribe_Values.Look(ref Excluded, "excluded");
            Scribe_Values.Look(ref LastEvidenceTick, "lastEvidenceTick", -1);
            Scribe_Values.Look(ref StartedTick, "startedTick");
            Scribe_Values.Look(ref EndedTick, "endedTick");
            Scribe_Values.Look(ref Goal, "goal");
            Scribe_Values.Look(ref Progress, "progress");
            Scribe_Values.Look(ref LastEvent, "lastEvent", "");
            Scribe_Values.Look(ref LastActionTick, "lastActionTick", -1000000);
            Scribe_Values.Look(ref LastPersonalDay, "lastPersonalDay", -1);
            Scribe_Values.Look(ref PersonalSessions, "personalSessions");
        }
    }

    public sealed partial class StockClaim : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref ThingId, "thingId");
            Scribe_Values.Look(ref OwnerFamily, "ownerFamily");
            Scribe_Values.Look(ref HolderFamily, "holderFamily");
            Scribe_Values.Look(ref MapId, "mapId");
            Scribe_Values.Look(ref Kind, "kind");
        }
    }

    public sealed partial class SupplyRequest : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref Id, "id");
            Scribe_Values.Look(ref RequesterId, "requesterId");
            Scribe_Values.Look(ref DonorId, "donorId");
            Scribe_Values.Look(ref SourceId, "sourceId");
            Scribe_Values.Look(ref MapId, "mapId");
            Scribe_Values.Look(ref Count, "count");
            Scribe_Values.Look(ref CreatedTick, "createdTick");
            Scribe_Values.Look(ref DueTick, "dueTick");
            Scribe_Values.Look(ref Price, "price");
            Scribe_Values.Look(ref RootIncidentId, "rootIncidentId");
            Scribe_Values.Look(ref HomeX, "homeX");
            Scribe_Values.Look(ref HomeZ, "homeZ");
            Scribe_Values.Look(ref DoorX, "doorX");
            Scribe_Values.Look(ref DoorZ, "doorZ");
            Scribe_Values.Look(ref DebtId, "debtId");
            Scribe_Values.Look(ref RepaymentAttemptTick, "repaymentAttemptTick");
            Scribe_Values.Look(ref Kind, "kind");
            Scribe_Values.Look(ref Approach, "approach");
            Scribe_Values.Look(ref State, "state");
            Scribe_Values.Look(ref Definition, "definition");
            Scribe_Values.Look(ref Units, "units");
            Scribe_Values.Look(ref DebtPaid, "debtPaid");
        }
    }

    public sealed partial class FamilyRegistry : IExposable
    {
        public void ExposeData()
        {
            Scribe_Values.Look(ref DataVersion, "dataVersion", 1);
            Scribe_Values.Look(ref NextLineageId, "nextLineageId", 1);
            Scribe_Values.Look(ref NextHouseholdId, "nextHouseholdId", 1);
            Scribe_Collections.Look(ref Persons, "persons", LookMode.Deep);
            Scribe_Collections.Look(ref Lineages, "lineages", LookMode.Deep);
            Scribe_Collections.Look(ref Households, "households", LookMode.Deep);
            Scribe_Collections.Look(ref Chronicle, "chronicle", LookMode.Deep);
            Scribe_Collections.Look(ref Bonds, "bonds", LookMode.Deep);
            Scribe_Collections.Look(ref TimeRecords, "timeRecords", LookMode.Deep);
            Scribe_Collections.Look(ref Rifts, "rifts", LookMode.Deep);
            Scribe_Collections.Look(ref Memories, "memories", LookMode.Deep);
            Scribe_Collections.Look(ref GatheringRequests, "gatheringRequests", LookMode.Deep);
            Scribe_Collections.Look(ref Ceremonies,"ceremonies",LookMode.Deep);
            Scribe_Collections.Look(ref Prestige,"prestige",LookMode.Deep);
            Scribe_Collections.Look(ref EventLog, "eventLog", LookMode.Deep);
            Scribe_Collections.Look(ref Commitments, "commitments", LookMode.Deep);
            Scribe_Collections.Look(ref Ties, "familyTies", LookMode.Deep);
            Scribe_Collections.Look(ref HouseContacts,"houseContacts",LookMode.Deep);
            Scribe_Collections.Look(ref HousePetitions,"housePetitions",LookMode.Deep);
            Scribe_Collections.Look(ref StewardRecords,"stewardRecords",LookMode.Deep);
            Scribe_Collections.Look(ref MarriageAlliances,"marriageAlliances",LookMode.Deep);
            Scribe_Values.Look(ref NextMarriageAllianceId,"nextMarriageAllianceId",1);
            Scribe_Collections.Look(ref CeremonyPlans,"ceremonyPlans",LookMode.Deep);Scribe_Values.Look(ref NextPlanId,"nextPlanId",1);
            Scribe_Collections.Look(ref StewardDuties,"stewardDuties",LookMode.Deep);Scribe_Collections.Look(ref FamilyOffices,"familyOffices",LookMode.Deep);
            Scribe_Values.Look(ref NextStewardDutyId,"nextStewardDutyId",1);Scribe_Collections.Look(ref SuccessionCases,"successionCases",LookMode.Deep);
            Scribe_Values.Look(ref NextSuccessionCaseId,"nextSuccessionCaseId",1);Scribe_Collections.Look(ref Heists,"heists",LookMode.Deep);
            Scribe_Collections.Look(ref CrimeKnowledgeRecords,"crimeKnowledge",LookMode.Deep);Scribe_Collections.Look(ref ProtectionArrangements,"protectionArrangements",LookMode.Deep);
            Scribe_Values.Look(ref NextHeistId,"nextHeistId",1);Scribe_Values.Look(ref NextProtectionId,"nextProtectionId",1);
            Scribe_Deep.Look(ref Institution,"institution");Scribe_Collections.Look(ref Elections,"elections",LookMode.Deep);
            Scribe_Collections.Look(ref InstitutionMovements,"institutionMovements",LookMode.Deep);Scribe_Values.Look(ref NextInstitutionMovementId,"nextInstitutionMovementId",1);Scribe_Values.Look(ref LastInstitutionMovementTick,"lastInstitutionMovementTick",-1000000);
            Scribe_Collections.Look(ref Coups,"coups",LookMode.Deep);Scribe_Values.Look(ref NextElectionId,"nextElectionId",1);
            Scribe_Values.Look(ref NextCoupId,"nextCoupId",1);
            Scribe_Collections.Look(ref Favors,"favors",LookMode.Deep);Scribe_Values.Look(ref NextFavorId,"nextFavorId",1);
            Scribe_Collections.Look(ref CouncilMatters,"councilMatters",LookMode.Deep);Scribe_Values.Look(ref NextCouncilMatterId,"nextCouncilMatterId",1);Scribe_Values.Look(ref LastCoupPlanTick,"lastCoupPlanTick",-1000000);
            Scribe_Collections.Look(ref Offices,"communityOffices",LookMode.Deep);Scribe_Values.Look(ref NextOfficeId,"nextOfficeId",1);
            Scribe_Collections.Look(ref Assemblies,"assemblies",LookMode.Deep);Scribe_Values.Look(ref NextAssemblyId,"nextAssemblyId",1);Scribe_Values.Look(ref NextAssemblyTick,"nextAssemblyTick");Scribe_Values.Look(ref HallMapId,"hallMap",-1);Scribe_Values.Look(ref HallX,"hallX");Scribe_Values.Look(ref HallZ,"hallZ");
            Scribe_Collections.Look(ref KinVisits,"kinVisits",LookMode.Deep);Scribe_Values.Look(ref NextKinVisitId,"nextKinVisitId",1);
            Scribe_Collections.Look(ref CampTrips,"campTrips",LookMode.Deep);Scribe_Values.Look(ref NextCampId,"nextCampId",1);Scribe_Values.Look(ref CampCursor,"campCursor");Scribe_Values.Look(ref LastCampScanTick,"lastCampScanTick",-1000000);
            Scribe_Values.Look(ref NextPetitionId,"nextPetitionId",1);
            Scribe_Collections.Look(ref Attention, "attention", LookMode.Deep);
            Scribe_Collections.Look(ref Agency, "agency", LookMode.Deep);
            Scribe_Collections.Look(ref Incidents, "incidents", LookMode.Deep);
            Scribe_Collections.Look(ref Plots, "plots", LookMode.Deep);
            Scribe_Collections.Look(ref Claims, "stockClaims", LookMode.Deep);
            Scribe_Collections.Look(ref Requests, "supplyRequests", LookMode.Deep);
            Scribe_Values.Look(ref NextIncidentId, "nextIncidentId", 1);
            Scribe_Values.Look(ref NextPlotId, "nextPlotId", 1);
            Scribe_Values.Look(ref NextSupplyRequestId, "nextSupplyRequestId", 1);
            Scribe_Values.Look(ref NextCommitmentId, "nextCommitmentId", 1);
            Scribe_Values.Look(ref NextRiftId, "nextRiftId", 1);
            Scribe_Values.Look(ref NextRequestId, "nextRequestId", 1);
            Scribe_Values.Look(ref LastAnniversaryTick, "lastAnniversaryTick");

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (DataVersion > CurrentDataVersion)
                    Log.Warning("[Wil's Family Life] This save was written by a newer version of the mod (data v" + DataVersion
                        + ", this is v" + CurrentDataVersion + "). Some family data may be ignored.");
                RebuildIndexes();
                Ceremonies=Ceremonies??new List<CeremonyRecord>();Prestige=Prestige??new List<PrestigeRecord>();
                CeremonyPlans=CeremonyPlans??new List<CeremonyPlan>();StewardDuties=StewardDuties??new List<StewardDuty>();FamilyOffices=FamilyOffices??new List<FamilyOffice>();
                SuccessionCases=SuccessionCases??new List<SuccessionCase>();Heists=Heists??new List<Heist>();CrimeKnowledgeRecords=CrimeKnowledgeRecords??new List<CrimeKnowledge>();
                ProtectionArrangements=ProtectionArrangements??new List<ProtectionArrangement>();Institution=Institution??new ColonyInstitution();InstitutionMovements=InstitutionMovements??new List<InstitutionMovement>();
                Elections=Elections??new List<Election>();Coups=Coups??new List<CoupPlan>();
                Favors=Favors??new List<FavorDebt>();CouncilMatters=CouncilMatters??new List<CouncilMatter>();Offices=Offices??new List<CommunityOffice>();Assemblies=Assemblies??new List<CommunityAssembly>();CampTrips=CampTrips??new List<CampTrip>();KinVisits=KinVisits??new List<KinVisit>();
            }
        }
    }
}
