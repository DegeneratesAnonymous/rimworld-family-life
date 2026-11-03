using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>One person of the starting colony, as the scenario sees them.</summary>
    public sealed class StartingPerson
    {
        public int Id;
        public string LastName = "";
        public bool IsChild;
        public bool IsMale;
    }

    public sealed class StartingFamilyPlan
    {
        public List<KeyValuePair<int, int>> Spouses = new List<KeyValuePair<int, int>>();       // two adults who are married
        public List<KeyValuePair<int, int>> ParentOf = new List<KeyValuePair<int, int>>();      // (parent, child)
    }

    /// <summary>
    /// Works out who in a starting colony is married to whom and whose child is whose, from nothing but the surname they were given and
    /// whether they are a child. It reads the colonists as they are when the game starts, so a player who rerolled or reordered the
    /// starting pawns on the setup page still gets the families the scenario promised.
    /// </summary>
    public static class StartingFamilies
    {
        /// <summary>Surnames to give the families. Distinct within one game; plain, varied and easy to tell apart.</summary>
        public static readonly string[] Surnames =
        {
            "Alder", "Ashby", "Barrow", "Bellamy", "Brandt", "Calloway", "Carver", "Chandler", "Dalton", "Delacroix", "Easton", "Ellery",
            "Farrow", "Fenwick", "Garrick", "Gray", "Hartley", "Hollis", "Ingram", "Jessop", "Kestrel", "Kincaid", "Lockhart", "Lowell",
            "Marlow", "Mercer", "Nolan", "Northcott", "Okafor", "Oakes", "Pryce", "Quill", "Redfern", "Rowan", "Sable", "Sterling",
            "Thorne", "Tillman", "Underhill", "Vance", "Wexford", "Whitlock", "Yardley", "Zeller"
        };

        public static List<string> PickSurnames(int count, Random rng)
        {
            return Surnames.OrderBy(_ => rng.Next()).Take(Math.Max(0, Math.Min(count, Surnames.Length))).ToList();
        }

        public static StartingFamilyPlan Plan(IEnumerable<StartingPerson> people)
        {
            var plan = new StartingFamilyPlan();
            foreach (var family in people.Where(p => !string.IsNullOrEmpty(p.LastName)).GroupBy(p => p.LastName))
            {
                // The two adults: a man and a woman if there are both, otherwise the lowest ids. Further adults are left unattached.
                var adults = family.Where(p => !p.IsChild).OrderBy(p => p.Id).ToList();
                var husband = adults.FirstOrDefault(p => p.IsMale);
                var wife = adults.FirstOrDefault(p => !p.IsMale);
                var parents = new List<StartingPerson>();
                if (husband != null && wife != null) parents.AddRange(new[] { husband, wife });
                else parents.AddRange(adults.Take(2));
                if (parents.Count == 2) plan.Spouses.Add(new KeyValuePair<int, int>(parents[0].Id, parents[1].Id));
                foreach (var child in family.Where(p => p.IsChild).OrderBy(p => p.Id))
                    foreach (var parent in parents)
                        plan.ParentOf.Add(new KeyValuePair<int, int>(parent.Id, child.Id));
            }
            return plan;
        }
    }
}
