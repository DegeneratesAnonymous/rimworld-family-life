using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>
    /// Transient exact comparison of observed facts, before reconciliation normalizes its input. Ordinary aging is excluded:
    /// AgeTicks is only used to classify a newly recorded person, whose new ID always forces reconciliation.
    /// </summary>
    public sealed class ObservationCache
    {
        private readonly Dictionary<int, PersonObservation> previous = new Dictionary<int, PersonObservation>();
        private bool initialized;
        private readonly HashSet<int> ids = new HashSet<int>();
        public void Clear() { previous.Clear(); initialized = false; }
        public bool Update(IList<PersonObservation> observations)
        {
            bool same = initialized && observations != null && observations.Count == previous.Count;
            ids.Clear();
            if (observations == null) { Clear(); return true; }
            foreach (var o in observations)
            {
                if (o == null || !ids.Add(o.Id)) { Clear(); return true; }
                if (!previous.TryGetValue(o.Id, out var p) || !Same(p, o)) same = false;
            }
            if (same) return false;
            previous.Clear();
            foreach (var o in observations)
                previous[o.Id] = new PersonObservation
                {
                    Id = o.Id, Name = o.Name, LastName = o.LastName, BirthAbsTick = o.BirthAbsTick,
                    Presence = o.Presence, Juvenile = o.Juvenile, TopSkill = o.TopSkill, TopSkillLevel = o.TopSkillLevel,
                    ParentIds = Copy(o.ParentIds), SpouseIds = Copy(o.SpouseIds), SpouseSince = Copy(o.SpouseSince),
                    LoverIds = Copy(o.LoverIds), ExIds = Copy(o.ExIds)
                };
            initialized = true;
            return true;
        }
        private static List<int> Copy(List<int> values) => values == null ? null : new List<int>(values);
        private static bool Equal(List<int> a, List<int> b) => a == null ? b == null : b != null && a.SequenceEqual(b);
        private static bool Same(PersonObservation a, PersonObservation b) =>
            a.Name == b.Name && a.LastName == b.LastName && a.BirthAbsTick == b.BirthAbsTick &&
            a.Presence == b.Presence && a.Juvenile == b.Juvenile && a.TopSkill == b.TopSkill && a.TopSkillLevel == b.TopSkillLevel &&
            Equal(a.ParentIds, b.ParentIds) && Equal(a.SpouseIds, b.SpouseIds) && Equal(a.SpouseSince, b.SpouseSince) &&
            Equal(a.LoverIds, b.LoverIds) && Equal(a.ExIds, b.ExIds);
    }
}
