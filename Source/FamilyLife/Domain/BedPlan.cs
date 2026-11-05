using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum SleeperStage : byte { Baby = 0, Child = 1, Adult = 2 }

    /// <summary>Someone who needs a bed. Plain data: the game layer fills it in.</summary>
    public sealed class BedSleeper
    {
        public int Id;
        public SleeperStage Stage;
        public List<int> PartnerIds = new List<int>();
        public List<int> ParentIds = new List<int>();
    }

    /// <summary>A bed in the home. Capacity is how many can sleep in it; a crib is only for babies.</summary>
    public sealed class BedSlot
    {
        public int Id;
        public int Capacity = 1;
        public bool Crib;
        public float X, Z;
    }

    /// <summary>
    /// Who sleeps where when a family moves into a home. Couples get a double bed, or failing that two beds side by side; babies get the crib
    /// nearest a parent; children get the bed nearest a parent; everyone else gets a single. Nobody shares a bed the plan did not mean them
    /// to, and anyone left over simply gets no bed (the game finds them one). The same input always gives the same answer.
    /// </summary>
    public static class BedPlan
    {
        /// <param name="canUse">Whether this person can sleep in this bed at all (size, age, kind). Null means anyone can.</param>
        /// <returns>For each person who gets a bed, the bed's id. Two partners in a double bed have the same bed id.</returns>
        public static Dictionary<int, int> Deal(IList<BedSleeper> sleepers, IList<BedSlot> beds, Func<int, int, bool> canUse = null)
        {
            var result = new Dictionary<int, int>();
            var left = beds.OrderBy(b => b.X).ThenBy(b => b.Z).ThenBy(b => b.Id).ToList();
            var byId = sleepers.ToDictionary(s => s.Id);
            bool Usable(BedSleeper s, BedSlot b) => canUse == null || canUse(s.Id, b.Id);
            BedSlot Pos(int personId) => result.TryGetValue(personId, out int bedId) ? beds.FirstOrDefault(b => b.Id == bedId) : null;
            float Dist(BedSlot a, BedSlot b) => (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z);

            void Give(BedSleeper s, BedSlot b, bool takes) { result[s.Id] = b.Id; if (takes) left.Remove(b); }

            // Adults, in couples first.
            var adults = sleepers.Where(s => s.Stage == SleeperStage.Adult).OrderBy(s => s.Id).ToList();
            var paired = new HashSet<int>();
            var couples = new List<KeyValuePair<BedSleeper, BedSleeper>>();
            foreach (var a in adults)
            {
                if (paired.Contains(a.Id)) continue;
                var mate = adults.FirstOrDefault(b => b.Id != a.Id && !paired.Contains(b.Id) && (a.PartnerIds.Contains(b.Id) || b.PartnerIds.Contains(a.Id)));
                if (mate == null) continue;
                paired.Add(a.Id); paired.Add(mate.Id);
                couples.Add(new KeyValuePair<BedSleeper, BedSleeper>(a, mate));
            }
            foreach (var c in couples)
            {
                var double_ = left.FirstOrDefault(b => !b.Crib && b.Capacity >= 2 && Usable(c.Key, b) && Usable(c.Value, b));
                if (double_ != null) { Give(c.Key, double_, true); result[c.Value.Id] = double_.Id; continue; }
                var first = left.FirstOrDefault(b => !b.Crib && Usable(c.Key, b));
                if (first == null) { paired.Remove(c.Key.Id); paired.Remove(c.Value.Id); continue; }
                var next = left.Where(b => b != first && !b.Crib && Usable(c.Value, b)).OrderBy(b => Dist(first, b)).FirstOrDefault();
                if (next == null) { paired.Remove(c.Key.Id); paired.Remove(c.Value.Id); continue; }
                Give(c.Key, first, true); Give(c.Value, next, true);
            }

            // The rest of the adults: a single bed each, a double only when the singles have run out.
            foreach (var a in adults)
            {
                if (result.ContainsKey(a.Id)) continue;
                var bed = left.FirstOrDefault(b => !b.Crib && b.Capacity < 2 && Usable(a, b)) ?? left.FirstOrDefault(b => !b.Crib && Usable(a, b));
                if (bed != null) Give(a, bed, true);
            }

            // The young, nearest a parent.
            foreach (var kid in sleepers.Where(s => s.Stage != SleeperStage.Adult).OrderBy(s => s.Stage).ThenBy(s => s.Id))
            {
                var anchors = kid.ParentIds.Select(Pos).Where(b => b != null).ToList();
                float Near(BedSlot b) => anchors.Count == 0 ? 0f : anchors.Min(a => Dist(a, b));
                var options = left.Where(b => Usable(kid, b)).ToList();
                BedSlot pick = null;
                if (kid.Stage == SleeperStage.Baby) pick = options.Where(b => b.Crib).OrderBy(Near).FirstOrDefault();
                if (pick == null) pick = options.Where(b => !b.Crib && b.Capacity < 2).OrderBy(Near).FirstOrDefault() ?? options.Where(b => !b.Crib).OrderBy(Near).FirstOrDefault();
                if (pick != null) Give(kid, pick, true);
            }
            return result;
        }
    }
}
