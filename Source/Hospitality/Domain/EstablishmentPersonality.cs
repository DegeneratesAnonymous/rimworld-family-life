using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    /// <summary>Stable preferences; mood is deliberately separate so a bad afternoon cannot rewrite a venue's culture.</summary>
    public sealed class BusinessTemperament
    {
        public int Warmth=50, Refinement=50, Frugality=50, Discipline=50, Revelry=50;
        public int[] Values=>new[]{Warmth,Refinement,Frugality,Discipline,Revelry};
        public static BusinessTemperament FromTraits(IEnumerable<string> names,int industriousness=0,int drugDesire=0)
        {
            var traits=new HashSet<string>(names??Enumerable.Empty<string>());var p=new BusinessTemperament();
            if(traits.Contains("Kind")){p.Warmth+=35;p.Frugality-=20;}
            if(traits.Contains("Greedy")){p.Frugality+=35;p.Refinement+=15;}
            if(traits.Contains("Abrasive"))p.Warmth-=30;
            if(traits.Contains("Psychopath"))p.Warmth-=30;
            if(traits.Contains("Ascetic")){p.Refinement-=30;p.Frugality+=25;p.Revelry-=20;}
            if(traits.Contains("Gourmand"))p.Refinement+=25;
            if(traits.Contains("Brawler")){p.Revelry+=30;p.Refinement-=10;}
            if(traits.Contains("Teetotaler"))p.Revelry-=25;
            if(traits.Contains("ChemicalInterest"))p.Revelry+=15;
            if(traits.Contains("ChemicalFascination"))p.Revelry+=25;
            if(drugDesire<0)p.Revelry-=25;else if(drugDesire>0)p.Revelry+=Math.Min(2,drugDesire)*12;
            p.Discipline+=Math.Max(-2,Math.Min(2,industriousness))*18;
            return FromValues(p.Values);
        }
        public static BusinessTemperament FromValues(IList<int> v)=>new BusinessTemperament{Warmth=Clamp(v[0]),Refinement=Clamp(v[1]),Frugality=Clamp(v[2]),Discipline=Clamp(v[3]),Revelry=Clamp(v[4])};
        public static BusinessTemperament Average(IEnumerable<BusinessTemperament> people)
        {
            var list=people.ToList();if(list.Count==0)return new BusinessTemperament();return FromValues(Enumerable.Range(0,5).Select(i=>(int)Math.Round(list.Average(p=>p.Values[i]))).ToArray());
        }
        public float Affinity(BusinessTemperament other)=>1f-Values.Zip(other.Values,(a,b)=>Math.Abs(a-b)).Sum()/500f;
        public static int Clamp(int n)=>Math.Max(0,Math.Min(100,n));
    }
    public enum OwnerApproach { Praise, Support, Criticize, DiscussMargins, Bonus, FavorRelative, Celebrate }
    public sealed class OwnerSituation
    {
        public BusinessTemperament Owner=new BusinessTemperament();
        public float Mood=.5f;
        public int Opinion;
        public bool Productive, EmployeeDistressed, Backlog, WageDebt, Profitable, CanBonus, Relative, OtherStaffPresent, EqualTreatment;
        public bool Nervous;
    }
    public static class OwnerDecisions
    {
        public static Dictionary<OwnerApproach,float> Weights(OwnerSituation c)
        {
            float warmth=c.Owner.Warmth/100f, thrift=c.Owner.Frugality/100f;
            float stress=Math.Max(0,.45f-c.Mood)*(c.Nervous?1.5f:1f);
            var w=new Dictionary<OwnerApproach,float>();
            if(c.Productive)w[OwnerApproach.Praise]=.15f+warmth*.8f+Math.Max(0,c.Opinion)/250f;
            if(c.EmployeeDistressed)w[OwnerApproach.Support]=.05f+warmth*1.2f;
            if(c.Backlog)w[OwnerApproach.Criticize]=.05f+(1-warmth)*.8f+stress*1.5f+c.Owner.Discipline/250f;
            if(c.WageDebt||c.Profitable)w[OwnerApproach.DiscussMargins]=.05f+thrift*.9f+stress;
            if(c.CanBonus&&c.Productive)w[OwnerApproach.Bonus]=.05f+(1-thrift)*warmth;
            if(!c.EqualTreatment&&c.Relative&&c.OtherStaffPresent&&c.Productive)w[OwnerApproach.FavorRelative]=.1f+thrift*.35f+Math.Max(0,c.Opinion)/180f;
            if(c.Profitable&&c.Productive)w[OwnerApproach.Celebrate]=.1f+Math.Max(0,c.Mood-.5f)*.8f+c.Owner.Revelry/200f;
            return w;
        }
        public static OwnerApproach? Choose(OwnerSituation c,float roll)
        {
            var weights=Weights(c);float sum=weights.Values.Sum();if(sum<=0)return null;
            float pick=Math.Max(0,Math.Min(.999999f,roll))*sum;
            foreach(var pair in weights.OrderBy(x=>(int)x.Key)){pick-=pair.Value;if(pick<0)return pair.Key;}
            return weights.Keys.Last();
        }
    }
    public sealed class VenuePersonality
    {
        public BusinessTemperament Culture=new BusinessTemperament();
        public int LastCultureDay=-1, LastOwnerDay=-1, StaffClimate=50;
        public bool OwnerAutonomy=true, AllowBonuses=true, EqualTreatment;
        public string LastReason="Character develops through its owner, staff and visiting customers.";
        public void Develop(BusinessTemperament owner,IList<BusinessTemperament> staff,IList<BusinessTemperament> patrons,int day,bool quiet)
        {
            if(day<=LastCultureDay)return;
            LastCultureDay=day;
            var sources=new List<Tuple<BusinessTemperament,int>>();
            if(owner!=null)sources.Add(Tuple.Create(owner,5));
            if(staff.Count>0)sources.Add(Tuple.Create(BusinessTemperament.Average(staff),3));
            if(patrons.Count>0)sources.Add(Tuple.Create(BusinessTemperament.Average(patrons),2));
            if(sources.Count==0)return;
            int total=sources.Sum(s=>s.Item2);var values=Culture.Values;
            for(int i=0;i<5;i++){int target=(int)Math.Round(sources.Sum(s=>(double)s.Item1.Values[i]*s.Item2)/total);if(i==4&&quiet)target=Math.Min(target,25);values[i]+=Math.Max(-3,Math.Min(3,target-values[i]));}
            Culture=BusinessTemperament.FromValues(values);StaffClimate+=Math.Sign(50-StaffClimate);
        }
        public string Description
        {
            get
            {
                var tags=new List<string>();
                if(Culture.Warmth>=65)tags.Add("Welcoming");else if(Culture.Warmth<=35)tags.Add("Hard-edged");
                if(Culture.Refinement>=65)tags.Add("Refined");else if(Culture.Refinement<=35)tags.Add("Plain and practical");
                if(Culture.Frugality>=65)tags.Add("Profit-conscious");else if(Culture.Frugality<=35)tags.Add("Generous");
                if(Culture.Discipline>=65)tags.Add("Orderly");else if(Culture.Discipline<=35)tags.Add("Easygoing");
                if(Culture.Revelry>=65)tags.Add("Lively");else if(Culture.Revelry<=35)tags.Add("Quiet");
                return tags.Count==0?"Still finding its character":string.Join(" · ",tags);
            }
        }
        public string Encode()=>new StateWriter().Ints("culture",Culture.Values).Int("day",LastCultureDay+1).Int("ownerday",LastOwnerDay+1).Int("climate",StaffClimate).Bool("autonomy",OwnerAutonomy).Bool("bonuses",AllowBonuses).Bool("equal",EqualTreatment).Strings("reason",new[]{LastReason}).ToString();
        public static VenuePersonality Decode(StateReader r)
        {
            var v=r.Ints("culture");if(v.Count!=5)return new VenuePersonality();
            return new VenuePersonality{Culture=BusinessTemperament.FromValues(v),LastCultureDay=r.Int("day")-1,LastOwnerDay=r.Int("ownerday")-1,StaffClimate=BusinessTemperament.Clamp(r.Int("climate",50)),OwnerAutonomy=r.Bool("autonomy"),AllowBonuses=r.Bool("bonuses"),EqualTreatment=r.Bool("equal"),LastReason=r.Strings("reason").FirstOrDefault()??""};
        }
    }
}
