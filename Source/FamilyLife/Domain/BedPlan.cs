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

    /// <summary>A bed in the home. Comfort is how good it is to sleep there (the room and the bed together); RoomId groups beds in one room, -1 for none.</summary>
    public sealed class BedSlot
    {
        public int Id;
        public int Capacity = 1;
        public int RoomId = -1;
        public float Comfort;
        public float X, Z;
    }

    /// <summary>
    /// Who sleeps where when a family moves into a home. No kind of bed is reserved for anyone: it is about comfort. Couples choose first and
    /// take the best place they can sleep together: a double bed, or two beds in the same room, whichever is more comfortable. Then each child
    /// takes the most comfortable bed left (the nearer a parent, the better, when two are as good), then everyone else. Anyone left over gets
    /// no bed, and the game finds them one. The same input always gives the same answer.
    /// </summary>
    public static class BedPlan
    {
        /// <param name="canUse">Whether this person can sleep in this bed at all (size, age, kind). Null means anyone can.</param>
        /// <returns>For each person who gets a bed, the bed's id. Two partners in a double bed have the same bed id.</returns>
        public static Dictionary<int, int> Deal(IList<BedSleeper> sleepers, IList<BedSlot> beds, Func<int, int, bool> canUse = null)
        {
            var result = new Dictionary<int, int>();
            var left = beds.OrderBy(b => b.X).ThenBy(b => b.Z).ThenBy(b => b.Id).ToList();
            bool Usable(BedSleeper s, BedSlot b) => canUse == null || canUse(s.Id, b.Id);
            BedSlot BedOf(int personId) => result.TryGetValue(personId, out int bedId) ? beds.FirstOrDefault(b => b.Id == bedId) : null;
            float Dist(BedSlot a, BedSlot b) => (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z);

            // Couples, in order of id.
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
                var first = c.Key; var second = c.Value;
                BedSlot bestShared = null, bestA = null, bestB = null;
                float bestScore = float.MinValue;
                foreach (var bed in left)
                {
                    if (bed.Capacity >= 2 && Usable(first, bed) && Usable(second, bed) && bed.Comfort > bestScore)
                    { bestScore = bed.Comfort; bestShared = bed; bestA = bestB = null; }
                }
                foreach (var one in left)
                {
                    if (!Usable(first, one)) continue;
                    foreach (var other in left)
                    {
                        if (other == one || other.RoomId < 0 || other.RoomId != one.RoomId || !Usable(second, other)) continue;
                        float score = Math.Min(one.Comfort, other.Comfort);
                        // Equal comfort: beds that are together beat beds that are apart, a shared bed beats two.
                        if (score > bestScore) { bestScore = score; bestShared = null; bestA = one; bestB = other; }
                    }
                }
                if (bestShared != null) { result[first.Id] = bestShared.Id; result[second.Id] = bestShared.Id; left.Remove(bestShared); }
                else if (bestA != null) { result[first.Id] = bestA.Id; result[second.Id] = bestB.Id; left.Remove(bestA); left.Remove(bestB); }
            }

            // The young next, then everyone else: the most comfortable bed left, the nearer a parent the better when it is a tie.
            // Children pick first. A parent on their own then picks to be with them (the same room, else the nearest bed, else the most comfortable); other adults pick last.
            var parentIds = new HashSet<int>(sleepers.Where(k => k.Stage != SleeperStage.Adult).SelectMany(k => k.ParentIds));
            int Turn(BedSleeper s) => s.Stage != SleeperStage.Adult ? 0 : parentIds.Contains(s.Id) ? 1 : 2;
            var rest = sleepers.Where(s => !result.ContainsKey(s.Id)).OrderBy(Turn).ThenBy(s => s.Stage).ThenBy(s => s.Id).ToList();
            foreach (var s in rest)
            {
                var anchors = s.ParentIds.Select(BedOf).Where(b => b != null).ToList();
                float Near(BedSlot b) => anchors.Count == 0 ? 0f : anchors.Min(a => Dist(a, b));
                var children = sleepers.Where(k => k.Stage != SleeperStage.Adult && k.ParentIds.Contains(s.Id)).Select(k => BedOf(k.Id)).Where(b => b != null).ToList();
                BedSlot pick;
                if (s.Stage == SleeperStage.Adult && children.Count > 0)
                {
                    float Closest(BedSlot b) => children.Min(c => Dist(c, b));
                    bool WithChild(BedSlot b) => b.RoomId >= 0 && children.Any(c => c.RoomId == b.RoomId);
                    pick = left.Where(b => Usable(s, b)).OrderByDescending(WithChild).ThenBy(Closest).ThenByDescending(b => b.Comfort).FirstOrDefault();
                }
                else pick = left.Where(b => Usable(s, b)).OrderByDescending(b => b.Comfort).ThenBy(Near).FirstOrDefault();
                if (pick != null) { result[s.Id] = pick.Id; left.Remove(pick); }
            }
            return result;
        }
    }
}
