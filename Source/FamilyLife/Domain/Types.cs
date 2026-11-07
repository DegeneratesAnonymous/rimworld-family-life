using System.Collections.Generic;

namespace FamilyLife.Domain
{
    /// <summary>Where a person is right now, as far as the colony is concerned.</summary>
    public enum Presence : byte
    {
        Away = 0,      // alive but not part of the colony (other faction, prisoner, lodger, world pawn)
        Colony = 1,    // a free colonist or slave of the colony
        Dead = 2
    }

    public enum AffiliationKind : byte
    {
        Spouse = 0,       // belongs through marriage
        Recognized = 1    // a parent link was added after the person was first recorded (adoption, found family)
    }

    public enum FamilyEventKind : byte
    {
        RecordsBegan = 0,       // the mod started keeping records in an existing game
        PersonRecorded = 1,     // not shown in the chronicle
        Birth = 2,              // A = child, B = a parent
        JoinedColony = 3,       // A = person
        LeftColony = 4,         // A = person
        Died = 5,               // A = person
        LineageFounded = 6,     // A = founder, B = lineage id
        LineageAffiliated = 7,  // A = person, B = lineage id
        PartnershipFormed = 8,  // A, B = the partners
        PartnershipEnded = 9,   // A, B = the former partners
        HouseholdFormed = 10,   // A = a first resident, B = household id
        HouseholdDissolved = 11,// B = household id
        HouseholdJoined = 12,   // A = person, B = household id
        HouseholdLeft = 13,     // A = person, B = household id
        CameOfAge = 14,         // A = person
        Reunion = 15,           // A = person who came back after a long absence
        Anniversary = 16,       // A, B = the couple
        TraditionAdopted = 17,  // A = lineage id, B = tradition index
        TraditionAbandoned = 18,// A = lineage id, B = tradition index
        TraditionHeld = 19,     // A = lineage id, B = tradition index
        RiftOpened = 20,        // A, B = the two people
        RiftResolved = 21,
        BondDeepened = 22,      // A = the one who feels it, B = the other; announced, not in the chronicle
        BondCooled = 23,
        FamiliesJoined = 24,    // A, B = the partners whose two families were joined by it
        SocialMembership = 26, SocialBranch = 27,
        MarriageFormed = 28,     // A, B became spouses; bookkeeping for an organic wedding reception
        StepFamilyFormed = 25   // A, B = the partners; at least one of them already had children who are not the other's       // A, B = the two people
    }

    /// <summary>
    /// A structured fact about the family. Also the entry type of the bounded chronicle: no text is stored,
    /// so saves stay small, translatable, and safe if the mod is removed.
    /// </summary>
    public sealed partial class FamilyEvent
    {
        public FamilyEventKind Kind;
        public int Tick;
        public int A;
        public int B;

        public FamilyEvent() { }

        public FamilyEvent(FamilyEventKind kind, int tick, int a = 0, int b = 0)
        {
            Kind = kind;
            Tick = tick;
            A = a;
            B = b;
        }

        /// <summary>Events that appear in the chronicle (the rest are bookkeeping).</summary>
        public bool IsChronicle
        {
            get
            {
                switch (Kind)
                {
                    case FamilyEventKind.RecordsBegan:
                    case FamilyEventKind.Birth:
                    case FamilyEventKind.JoinedColony:
                    case FamilyEventKind.LeftColony:
                    case FamilyEventKind.Died:
                    case FamilyEventKind.LineageFounded:
                    case FamilyEventKind.LineageAffiliated:
                    case FamilyEventKind.PartnershipFormed:
                    case FamilyEventKind.PartnershipEnded:
                    case FamilyEventKind.HouseholdFormed:
                    case FamilyEventKind.CameOfAge:
                    case FamilyEventKind.Reunion:
                    case FamilyEventKind.Anniversary:
                    case FamilyEventKind.TraditionAdopted:
                    case FamilyEventKind.TraditionAbandoned:
                    case FamilyEventKind.TraditionHeld:
                    case FamilyEventKind.RiftOpened:
                    case FamilyEventKind.RiftResolved:
                    case FamilyEventKind.FamiliesJoined:
                    case FamilyEventKind.SocialMembership:
                    case FamilyEventKind.SocialBranch:
                    case FamilyEventKind.StepFamilyFormed:
                        return true;
                    default:
                        return false;
                }
            }
        }
    }

    /// <summary>What the game layer saw about one person during a sweep. Plain data, no game types.</summary>
    public sealed class PersonObservation
    {
        public int Id;
        public string Name;          // short name, e.g. "Bob"
        public string LastName;      // surname, "" when none
        public long BirthAbsTick;
        public int AgeTicks;
        public Presence Presence;
        public bool Juvenile;        // a baby or child
        public List<int> ParentIds = new List<int>();
        public List<int> SpouseIds = new List<int>();
        public List<int> LoverIds = new List<int>();   // lovers and fiances
        public List<int> ExIds = new List<int>();      // ex-spouses and ex-lovers
        public List<int> SpouseSince = new List<int>(); // game tick each marriage began, parallel to SpouseIds (0 = unknown)
        public string TopSkill = "";                   // the skill this person is best at
        public int TopSkillLevel;
    }
}
