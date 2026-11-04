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
            Scribe_Values.Look(ref ZoneAreaId, "zoneAreaId");
            Scribe_Collections.Look(ref ZoneApplied, "zoneApplied", LookMode.Value);
            if (ZoneApplied == null) ZoneApplied = new List<int>();
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
        }
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
            Scribe_Collections.Look(ref EventLog, "eventLog", LookMode.Deep);
            Scribe_Collections.Look(ref Commitments, "commitments", LookMode.Deep);
            Scribe_Collections.Look(ref Ties, "familyTies", LookMode.Deep);
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
            }
        }
    }
}
