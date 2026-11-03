using System.Collections.Generic;

namespace FamilyLife.Domain
{
    /// <summary>
    /// One person the mod knows about. Keyed by the game's pawn id, never by a Pawn reference, so a record
    /// outlives its pawn (death, discard, leaving the world) and cannot produce a broken save reference.
    /// </summary>
    public sealed partial class PersonRecord
    {
        public int Id;
        public string Name = "";
        public string LastName = "";
        public long BirthAbsTick;
        public Presence Presence;
        public bool EverColonist;
        public bool Juvenile;
        public int FirstSeenTick;
        public int DeathTick = -1;           // -1 when unknown or alive

        public int PrimaryLineageId;         // 0 = none (peripheral relatives have no lineage)
        public int HouseholdId;              // 0 = none
        public int LastHouseholdId;          // where they lived before leaving or dying

        public List<int> ParentIds = new List<int>();
        public List<int> SpouseIds = new List<int>();
        public List<int> LoverIds = new List<int>();
        public List<int> ExIds = new List<int>();
        public List<int> SpouseSince = new List<int>();
        public string TopSkill = "";
        public int TopSkillLevel;
        public int AwayTick = -1;            // when they left the colony, -1 while they are here

        public bool IsAlive => Presence != Presence.Dead;
        public bool InColony => Presence == Presence.Colony;

        /// <summary>Spouses and lovers, in id order.</summary>
        public IEnumerable<int> PartnerIds()
        {
            var seen = new SortedSet<int>();
            foreach (var id in SpouseIds) seen.Add(id);
            foreach (var id in LoverIds) seen.Add(id);
            return seen;
        }

        public bool HasPartner(int id) => SpouseIds.Contains(id) || LoverIds.Contains(id);

        public string FullName => string.IsNullOrEmpty(LastName) ? Name : Name + " " + LastName;
    }

    /// <summary>A family identity that spans generations. It exists independently of any living member.</summary>
    public sealed partial class Lineage
    {
        public int Id;
        public string Name = "";             // the founder's surname at the time
        public int FounderId;
        public int FoundedTick;
        public List<Affiliation> Affiliations = new List<Affiliation>();

        public bool HasAffiliate(int personId)
        {
            for (int i = 0; i < Affiliations.Count; i++)
                if (Affiliations[i].PersonId == personId) return true;
            return false;
        }
    }

    /// <summary>A person who belongs to a lineage by marriage or recognition rather than by descent.</summary>
    public sealed partial class Affiliation
    {
        public int PersonId;
        public AffiliationKind Kind;
        public int SinceTick;

        public Affiliation() { }
        public Affiliation(int personId, AffiliationKind kind, int sinceTick)
        {
            PersonId = personId;
            Kind = kind;
            SinceTick = sinceTick;
        }
    }

    /// <summary>Pawns who live together. Several households can belong to one lineage and vice versa.</summary>
    public sealed partial class Household
    {
        public int Id;
        public int FormedTick;
        public List<int> ResidentIds = new List<int>();
    }
}
