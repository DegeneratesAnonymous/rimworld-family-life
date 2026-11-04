using System;
using System.Collections.Generic;

namespace FamilyLife.Domain
{
    /// <summary>Map-local IDs are never enough to choose between multiple candidate areas.</summary>
    public static class FamilyAreaPolicy
    {
        public static T Resolve<T>(IEnumerable<T> candidates, int area, int map, Func<T,int> areaId, Func<T,int> mapId) where T : class
        {
            T found = null;
            foreach (var candidate in candidates)
            {
                if (areaId(candidate) != area || map >= 0 && mapId(candidate) != map) continue;
                if (found != null) return null;
                found = candidate;
            }
            return found;
        }
    }
    public static class SharedActivityPolicy
    {
        public static bool SameTarget(int first, int second) => first > 0 && first == second;

        // Nearby labor is not an intentional favor. It never becomes CommunityAid via discovery.
        public static ActivityKind Discover(string joyKind)
        {
            if (string.IsNullOrEmpty(joyKind) || joyKind == "WFL_FamilyTime") return ActivityKind.None;
            return joyKind == "Gaming_Cerebral" || joyKind == "Gaming_Dexterity" ? ActivityKind.Competition : ActivityKind.GameNight;
        }
    }
}
