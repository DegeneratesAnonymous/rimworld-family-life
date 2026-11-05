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
    /// Who sleeps where when a family moves into a home. No kind of bed is reserved for anyone: it is about comfort.
    /// - A baby sleeps in its parents' room, so a parent never has far to go in the night. Parents look first for the best room that has a bed
    ///   for the baby as well; only when there is no such room is the baby put as near as it can be.
    /// - Couples choose first and take the best place they can sleep together: a double bed, or two beds in one room.
    /// - Then each older child takes the most comfortable bed left (the nearer a parent, the better, when two are as good); a single parent
    ///   then picks to be with their child, in the same room if one is free, else the nearest bed; everyone else takes what is left.
    /// Anyone left over gets no bed, and the game finds them one. The same input always gives the same answer.
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

            // Babies of the people being housed, still without a bed.
            List<BedSleeper> BabiesOf(params BedSleeper[] parents) =>
                sleepers.Where(k => k.Stage == SleeperStage.Baby && !result.ContainsKey(k.Id) && parents.Any(p => k.ParentIds.Contains(p.Id))).ToList();
            // Whether a room still has a bed (other than those about to be taken) for every one of these babies.
            bool HoldsBabies(int roomId, List<BedSleeper> babies, params BedSlot[] taken)
            {
                if (babies.Count == 0) return true;
                if (roomId < 0) return false;
                var free = left.Where(b => b.RoomId == roomId && !taken.Contains(b)).ToList();
                foreach (var baby in babies)
                {
                    var bed = free.FirstOrDefault(b => Usable(baby, b));
                    if (bed == null) return false;
                    free.Remove(bed);
                }
                return true;
            }
            // Each baby takes a bed in a parent's room (the nearest first); one without a room goes to the pass at the end.
            void PlaceBabies(List<BedSleeper> babies)
            {
                foreach (var baby in babies)
                {
                    var anchors = baby.ParentIds.Select(BedOf).Where(b => b != null).ToList();
                    if (anchors.Count == 0) continue;
                    var bed = left.Where(b => b.RoomId >= 0 && anchors.Any(a => a.RoomId == b.RoomId) && Usable(baby, b))
                                  .OrderBy(b => anchors.Min(a => Dist(a, b))).ThenBy(b => b.Id).FirstOrDefault();
                    if (bed != null) { result[baby.Id] = bed.Id; left.Remove(bed); }
                }
            }

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
                var babies = BabiesOf(first, second);
                BedSlot bestShared = null, bestA = null, bestB = null;
                bool bestHolds = false, any = false;
                float bestScore = float.MinValue;
                // With a baby, a room that has space for it beats any room that does not; otherwise the most comfortable wins.
                bool Better(bool holds, float score) => !any || (babies.Count > 0 && holds != bestHolds ? holds : score > bestScore);
                foreach (var bed in left)
                {
                    if (bed.Capacity < 2 || !Usable(first, bed) || !Usable(second, bed)) continue;
                    bool holds = HoldsBabies(bed.RoomId, babies, bed);
                    if (Better(holds, bed.Comfort)) { any = true; bestScore = bed.Comfort; bestHolds = holds; bestShared = bed; bestA = bestB = null; }
                }
                foreach (var one in left)
                {
                    if (!Usable(first, one)) continue;
                    foreach (var other in left)
                    {
                        if (other == one || other.RoomId < 0 || other.RoomId != one.RoomId || !Usable(second, other)) continue;
                        bool holds = HoldsBabies(one.RoomId, babies, one, other);
                        float score = Math.Min(one.Comfort, other.Comfort);
                        if (Better(holds, score)) { any = true; bestScore = score; bestHolds = holds; bestShared = null; bestA = one; bestB = other; }
                    }
                }
                if (bestShared != null) { result[first.Id] = bestShared.Id; result[second.Id] = bestShared.Id; left.Remove(bestShared); }
                else if (bestA != null) { result[first.Id] = bestA.Id; result[second.Id] = bestB.Id; left.Remove(bestA); left.Remove(bestB); }
                PlaceBabies(babies);
            }

            // A parent on their own with a baby: the best room that has a bed for the baby too, then the baby beside them.
            foreach (var parent in adults.Where(a => !result.ContainsKey(a.Id)).ToList())
            {
                var babies = BabiesOf(parent);
                if (babies.Count == 0) continue;
                var pick = left.Where(b => Usable(parent, b)).OrderByDescending(b => HoldsBabies(b.RoomId, babies, b)).ThenByDescending(b => b.Comfort).FirstOrDefault();
                if (pick == null) continue;
                result[parent.Id] = pick.Id; left.Remove(pick);
                PlaceBabies(babies);
            }

            // Everyone else: the young first, then a single parent to be with them, then the rest.
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
                else if (s.Stage == SleeperStage.Baby)
                {
                    bool WithParent(BedSlot b) => b.RoomId >= 0 && anchors.Any(a => a.RoomId == b.RoomId);
                    pick = left.Where(b => Usable(s, b)).OrderByDescending(WithParent).ThenBy(Near).ThenByDescending(b => b.Comfort).FirstOrDefault();
                }
                else pick = left.Where(b => Usable(s, b)).OrderByDescending(b => b.Comfort).ThenBy(Near).FirstOrDefault();
                if (pick != null) { result[s.Id] = pick.Id; left.Remove(pick); }
            }
            return result;
        }
    }
}
