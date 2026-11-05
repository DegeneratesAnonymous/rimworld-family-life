using System;
using System.Collections.Generic;
namespace FamilyLife.Domain
{
    public static class GatheringBudget
    {
        /// <summary>At most four forward neighbors, not an all-pairs expansion. Rotation chooses a fair starting point.</summary>
        public static List<KeyValuePair<T,T>> Pairs<T>(IList<T> people, int maxPairs, int rotation)
        {
            var result=new List<KeyValuePair<T,T>>();int n=people.Count;
            if(n<2 || maxPairs<=0) return result;
            int start=(rotation & int.MaxValue)%n;
            var seen=new HashSet<long>();
            for(int offset=0;offset<n && result.Count<maxPairs;offset++)
            {
                int a=(start+offset)%n;
                for(int step=1;step<=Math.Min(4,n-1) && result.Count<maxPairs;step++)
                {
                    int b=(a+step)%n;long key=((long)Math.Min(a,b)<<32)|(uint)Math.Max(a,b);
                    if(seen.Add(key)) result.Add(new KeyValuePair<T,T>(people[a],people[b]));
                }
            }
            return result;
        }
    }
}
