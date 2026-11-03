using System.Collections.Generic;
using System.Linq;
using System.Text;
using FamilyLife.Domain;

namespace FamilyLife.Tests
{
    internal static class T
    {
        /// <summary>Builds an observation. By default people are adult colonists; a lower id means an older person.</summary>
        public static PersonObservation P(int id, string name = null, string last = "Smith", Presence presence = Presence.Colony,
            bool juvenile = false, long? birth = null, int[] parents = null, int[] spouses = null, int[] lovers = null,
            int[] exes = null, int age = 5_000_000)
        {
            return new PersonObservation
            {
                Id = id,
                Name = name ?? ("P" + id),
                LastName = last,
                Presence = presence,
                Juvenile = juvenile,
                BirthAbsTick = birth ?? (-10_000_000L + id),
                AgeTicks = age,
                ParentIds = (parents ?? new int[0]).ToList(),
                SpouseIds = (spouses ?? new int[0]).ToList(),
                LoverIds = (lovers ?? new int[0]).ToList(),
                ExIds = (exes ?? new int[0]).ToList()
            };
        }

        public static PersonObservation Kid(int id, int[] parents, string last = "Smith", string name = null, int age = 2_000_000)
        {
            return P(id, name, last, juvenile: true, birth: 1000 + id, parents: parents, age: age);
        }

        /// <summary>A stable text description of the registry, used to compare states.</summary>
        public static string Summary(FamilyRegistry r)
        {
            var sb = new StringBuilder();
            foreach (var p in r.Persons.OrderBy(x => x.Id))
                sb.Append($"P{p.Id}:{p.Presence}:L{p.PrimaryLineageId}:H{p.HouseholdId}:last{p.LastHouseholdId}|");
            sb.Append('\n');
            foreach (var l in r.Lineages.OrderBy(x => x.Id))
                sb.Append($"L{l.Id}:{l.Name}:F{l.FounderId}:A[{string.Join(",", l.Affiliations.OrderBy(a => a.PersonId).Select(a => a.PersonId + "/" + a.Kind))}]|");
            sb.Append('\n');
            foreach (var h in r.Households.OrderBy(x => x.Id))
                sb.Append($"H{h.Id}:[{string.Join(",", h.ResidentIds.OrderBy(i => i))}]|");
            return sb.ToString();
        }

        public static Household HouseholdOf(FamilyRegistry r, int personId) => r.GetHousehold(r.GetPerson(personId).HouseholdId);

        public static bool SameHousehold(FamilyRegistry r, int a, int b)
        {
            var pa = r.GetPerson(a);
            var pb = r.GetPerson(b);
            return pa.HouseholdId != 0 && pa.HouseholdId == pb.HouseholdId;
        }

        public static bool Has(this List<FamilyEvent> events, FamilyEventKind kind) => events.Any(e => e.Kind == kind);
    }
}
