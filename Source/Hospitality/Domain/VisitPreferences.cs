using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public sealed class VisitorPreference
    {
        public bool Sociable, Quiet, Tough, Refined, Thrifty, Urgent, Injured;
        public int Budget, NightPrice, FoodReserve, TravelReserve, DaysStayed, MaxDays = 5;
        public float Experience = .5f, Connection;
        public float Appeal(Establishment e)
        {
            float score = e.Reputation / 100f * .4f + Experience * .4f + Connection * .15f;
            score += Sociable ? e.Rowdiness / 500f : 0;
            score += Quiet ? (50 - e.Rowdiness) / 250f : 0;
            score += Tough ? e.Rowdiness / 1000f : 0;
            score += Refined ? (e.Food + e.Comfort - 100) / 500f : 0;
            score -= Thrifty ? Math.Max(0,e.PricePercent-100)/300f : 0;
            return Math.Max(0,Math.Min(.9f,score));
        }
        public bool CanExtend => !Urgent && !Injured && DaysStayed < MaxDays && Budget >= NightPrice + FoodReserve + TravelReserve;
    }
    public static class VisitorDecisions
    {
        // All caravan members consent; a single keen member cannot detain the group.
        public static bool ExtendGroup(Establishment e, IList<VisitorPreference> group, double roll) => group.Count > 0 && group.All(p => p.CanExtend) && roll < group.Min(p => p.Appeal(e));
        public static float VisitUtility(float hunger, float recreation, float social, float preference, int price, int budget, float distance, bool freeMealAvailable)
        {
            if (price > budget || budget <= 0) return -1;
            return hunger*.3f + recreation*.25f + social*.2f + preference*.4f - (float)price/Math.Max(1,budget)*.35f - distance*.2f - (freeMealAvailable ? .15f : 0);
        }
    }
}
