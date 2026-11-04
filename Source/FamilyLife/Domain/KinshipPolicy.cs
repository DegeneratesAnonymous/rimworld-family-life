using System;
using System.Collections.Generic;
using System.Linq;
namespace FamilyLife.Domain
{
    public sealed class KinshipAssessment
    {
        public int Distance = -1;
        public bool DirectAncestor, Guardian, Incomplete;
        public bool BlocksRomance => DirectAncestor || Guardian || Distance>=0 && Distance<=4 || Incomplete;
    }
    public sealed partial class FamilyRegistry
    {
        private readonly Dictionary<(int,int),KinshipAssessment> kinshipCache = new Dictionary<(int,int),KinshipAssessment>();
        public KinshipAssessment AssessKinship(int first,int second)
        {
            var key=(Math.Min(first,second),Math.Max(first,second));
            if(kinshipCache.TryGetValue(key,out var known)) return known;
            var result=new KinshipAssessment();var a=GetPerson(first);var b=GetPerson(second);
            if(a==null || b==null) {result.Incomplete=true;return result;}
            if(first==second) {result.Distance=0;return result;}
            var aa=Ancestors(first,out bool incompleteA);var bb=Ancestors(second,out bool incompleteB);
            result.Incomplete=incompleteA||incompleteB;
            result.DirectAncestor=aa.ContainsKey(second)||bb.ContainsKey(first);
            result.Guardian=a.PartnerIds().Any(id=>b.ParentIds.Contains(id)) || b.PartnerIds().Any(id=>a.ParentIds.Contains(id));
            foreach(var item in aa) if(bb.TryGetValue(item.Key,out int depth)) result.Distance=result.Distance<0?item.Value+depth:Math.Min(result.Distance,item.Value+depth);
            if(result.DirectAncestor) result.Distance=aa.TryGetValue(second,out int d)?d:bb[first];
            if(kinshipCache.Count>=4096) kinshipCache.Clear();kinshipCache[key]=result;return result;
        }
        private Dictionary<int,int> Ancestors(int id,out bool incomplete)
        {
            var found=new Dictionary<int,int>();var queue=new Queue<(int,int)>();queue.Enqueue((id,0));incomplete=false;
            while(queue.Count>0)
            {
                var item=queue.Dequeue();var p=GetPerson(item.Item1);if(p==null) continue;
                if(item.Item2>=32 && p.ParentIds.Count>0) {incomplete=true;continue;}
                foreach(int parent in p.ParentIds)
                {
                    if(parent==id) {incomplete=true;continue;}
                    if(found.ContainsKey(parent)) continue;
                    if(found.Count>=2048){incomplete=true;return found;}
                    found[parent]=item.Item2+1;queue.Enqueue((parent,item.Item2+1));
                }
            }
            return found;
        }
    }
}
