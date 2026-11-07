namespace FamilyLife.Domain
{
    public sealed partial class Lineage
    {
        public int CrestPrimary = -1, CrestSecondary = 7, CrestMark = -1, CrestPattern;
    }
    public static class CrestStyle
    {
        public const int Colors = 8, Marks = 6, Patterns = 3;
        public static int Index(int value,int fallback,int count)
        {int chosen=value<0?fallback:value;return (chosen%count+count)%count;}

        /// <summary>A stable number from text and a salt. Not string.GetHashCode, which differs between runs.</summary>
        public static int Mix(string text,int salt)
        {
            unchecked
            {
                uint h=2166136261u;
                foreach(char c in text??"")h=(h^c)*16777619u;
                h^=(uint)salt*2654435761u;h^=h>>15;h*=2246822519u;h^=h>>13;
                return (int)(h&0x7fffffff);
            }
        }

        /// <summary>
        /// A new house's crest: its own colors, mark and pattern, chosen from the house's name and when it was founded, preferring a main color
        /// no other house is using so houses are told apart at a glance.
        /// </summary>
        public static void Randomize(Lineage house,System.Collections.Generic.IEnumerable<int> usedPrimaries)
        {
            int seed=Mix(house.Name,house.Id*7919+house.FoundedTick);
            var used=new System.Collections.Generic.HashSet<int>(usedPrimaries??System.Linq.Enumerable.Empty<int>());
            var free=new System.Collections.Generic.List<int>();
            for(int c=0;c<Colors;c++)if(!used.Contains(c))free.Add(c);
            if(free.Count==0)for(int c=0;c<Colors;c++)free.Add(c);
            int primary=free[seed%free.Count];
            house.CrestPrimary=primary;
            house.CrestSecondary=(primary+1+Mix("secondary",seed)%(Colors-1))%Colors;
            house.CrestMark=Mix("mark",seed)%Marks;
            house.CrestPattern=Mix("pattern",seed)%Patterns;
        }

        /// <summary>
        /// A branch keeps its parent's main color and mark, so it is plainly of the same house, but its second color and its pattern change, so it is
        /// plainly a different branch.
        /// </summary>
        public static void Branch(Lineage parent,Lineage branch)
        {
            int seed=Mix(branch.Name,branch.Id*104729+parent.Id);
            branch.CrestPrimary=Index(parent.CrestPrimary,parent.Id,Colors);
            branch.CrestMark=Index(parent.CrestMark,parent.Id/Colors,Marks);
            branch.CrestSecondary=(Index(parent.CrestSecondary,7,Colors)+1+seed%(Colors-2))%Colors;
            if(branch.CrestSecondary==branch.CrestPrimary)branch.CrestSecondary=(branch.CrestSecondary+1)%Colors;
            branch.CrestPattern=(Index(parent.CrestPattern,0,Patterns)+1+(seed>>4)%(Patterns-1))%Patterns;
        }
    }
}
