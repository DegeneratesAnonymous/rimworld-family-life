using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    [Flags] public enum MarriageAllianceReason : ushort
    { None=0, Continuity=1, Capability=2, Protection=4, Cooperation=8, Legacy=16 }
    [Flags] public enum MarriageAllianceObjection : ushort
    { None=0, MissingPerson=1, NotAdult=2, SameHouse=4, ExistingPartner=8, Kinship=16, VanillaRejected=32, HostileHouses=64, NoRoom=128, FoodInsecure=256, Ideology=512, PawnUnwilling=1024 }
    public enum MarriageAllianceState : byte { Opportunity, Negotiating, Supported, Opposed, Fulfilled, Expired, Withdrawn }

    /// <summary>Game facts that the pure domain cannot know. Supplying them never creates a relation.</summary>
    public sealed class MarriageCandidateContext
    {
        public bool VanillaEligible;
        public float OpinionA, OpinionB, IdeologyCompatibility, Potential;
        public float ContributionAToB, ContributionBToA;
    }

    public sealed class MarriageAllianceAssessment
    {
        public int PawnA, PawnB, FamilyA, FamilyB;
        public float HouseAInterest, HouseBInterest, PawnAWillingness, PawnBWillingness;
        public MarriageAllianceReason Reasons;
        public MarriageAllianceObjection ObjectionsA, ObjectionsB, HardObjections;
        public bool Eligible => HardObjections == MarriageAllianceObjection.None;
        public bool WorthConsidering => Eligible && HouseAInterest >= 40f && HouseBInterest >= 40f;
    }

    public sealed partial class MarriageAlliance
    {
        public int Id, PawnA, PawnB, FamilyA, FamilyB, CreatedTick, ExpiresTick, LastChangedTick;
        public float HouseAInterest, HouseBInterest, PawnAWillingness, PawnBWillingness;
        public MarriageAllianceReason Reasons;
        public MarriageAllianceObjection ObjectionsA, ObjectionsB;
        public MarriageAllianceState State;
    }

    public sealed partial class FamilyRegistry
    {
        public List<MarriageAlliance> MarriageAlliances = new List<MarriageAlliance>();
        public int NextMarriageAllianceId = 1;

        public MarriageAllianceAssessment AssessMarriageAlliance(int first, int second, MarriageCandidateContext context)
        {
            context = context ?? new MarriageCandidateContext();
            var result = new MarriageAllianceAssessment { PawnA=first, PawnB=second };
            var a=GetPerson(first);var b=GetPerson(second);
            if(a==null||b==null){result.HardObjections|=MarriageAllianceObjection.MissingPerson;return result;}
            result.FamilyA=FamilyOf(first);result.FamilyB=FamilyOf(second);
            if(a.Juvenile||b.Juvenile||!a.InColony||!b.InColony)result.HardObjections|=MarriageAllianceObjection.NotAdult;
            if(result.FamilyA==0||result.FamilyB==0||result.FamilyA==result.FamilyB)result.HardObjections|=MarriageAllianceObjection.SameHouse;
            if(a.PartnerIds().Any()||b.PartnerIds().Any())result.HardObjections|=MarriageAllianceObjection.ExistingPartner;
            if(AssessKinship(first,second).BlocksRomance)result.HardObjections|=MarriageAllianceObjection.Kinship;
            if(!context.VanillaEligible)result.HardObjections|=MarriageAllianceObjection.VanillaRejected;
            if(result.HardObjections!=MarriageAllianceObjection.None)return result;

            var houseA=GetLineage(result.FamilyA);var houseB=GetLineage(result.FamilyB);
            var sa=houseA?.Situation??new HouseSituation();var sb=houseB?.Situation??new HouseSituation();
            var tie=GetTie(result.FamilyA,result.FamilyB);float goodwill=tie?.Goodwill??0f,grievance=tie?.Grievance??0f;
            result.HouseAInterest=20f;result.HouseBInterest=20f;
            if(sa.Continuity<55f||sb.Continuity<55f){result.Reasons|=MarriageAllianceReason.Continuity;result.HouseAInterest+=Math.Max(0,55f-sa.Continuity)*.55f;result.HouseBInterest+=Math.Max(0,55f-sb.Continuity)*.55f;}
            if(context.ContributionAToB>0||context.ContributionBToA>0){result.Reasons|=MarriageAllianceReason.Capability;result.HouseAInterest+=Math.Max(0,context.ContributionBToA)*25f;result.HouseBInterest+=Math.Max(0,context.ContributionAToB)*25f;}
            if(sa.CooperativeFamilyId==result.FamilyB||sb.CooperativeFamilyId==result.FamilyA){result.Reasons|=MarriageAllianceReason.Protection;result.HouseAInterest+=15f;result.HouseBInterest+=15f;}
            if(goodwill>=20f){result.Reasons|=MarriageAllianceReason.Cooperation;result.HouseAInterest+=Math.Min(20f,goodwill*.25f);result.HouseBInterest+=Math.Min(20f,goodwill*.25f);}
            if(houseA?.Ambition?.Primary==HouseAmbitionKind.ExpandLegacy||houseB?.Ambition?.Primary==HouseAmbitionKind.ExpandLegacy){result.Reasons|=MarriageAllianceReason.Legacy;result.HouseAInterest+=12f;result.HouseBInterest+=12f;}
            ApplyHouseObjections(sa,grievance,context.IdeologyCompatibility,ref result.HouseAInterest,ref result.ObjectionsA);
            ApplyHouseObjections(sb,grievance,context.IdeologyCompatibility,ref result.HouseBInterest,ref result.ObjectionsB);
            var ab=GetBond(first,second);var ba=GetBond(second,first);
            result.PawnAWillingness=Bound(context.OpinionA*.45f+(ab?.Trust??0)*.2f+(ab?.Attachment??0)*.2f+context.IdeologyCompatibility*15f+context.Potential*20f);
            result.PawnBWillingness=Bound(context.OpinionB*.45f+(ba?.Trust??0)*.2f+(ba?.Attachment??0)*.2f+context.IdeologyCompatibility*15f+context.Potential*20f);
            if(result.PawnAWillingness<-20f)result.ObjectionsA|=MarriageAllianceObjection.PawnUnwilling;
            if(result.PawnBWillingness<-20f)result.ObjectionsB|=MarriageAllianceObjection.PawnUnwilling;
            result.HouseAInterest=Bound(result.HouseAInterest);result.HouseBInterest=Bound(result.HouseBInterest);
            return result;
        }

        public MarriageAlliance RecordMarriageOpportunity(MarriageAllianceAssessment a,int now)
        {
            if(a==null||!a.WorthConsidering)return null;
            var existing=MarriageAlliances.FirstOrDefault(x=>x.State<=MarriageAllianceState.Supported&&((x.PawnA==a.PawnA&&x.PawnB==a.PawnB)||(x.PawnA==a.PawnB&&x.PawnB==a.PawnA)));
            if(existing!=null)return existing;
            if(MarriageAlliances.Count>=64)MarriageAlliances.RemoveAll(x=>x.State>=MarriageAllianceState.Fulfilled);
            if(MarriageAlliances.Count>=64)return null;
            var record=new MarriageAlliance{Id=NextMarriageAllianceId++,PawnA=a.PawnA,PawnB=a.PawnB,FamilyA=a.FamilyA,FamilyB=a.FamilyB,
                CreatedTick=now,ExpiresTick=now+7*ActivityTuning.TicksPerDay,LastChangedTick=now,HouseAInterest=a.HouseAInterest,HouseBInterest=a.HouseBInterest,
                PawnAWillingness=a.PawnAWillingness,PawnBWillingness=a.PawnBWillingness,Reasons=a.Reasons,ObjectionsA=a.ObjectionsA,ObjectionsB=a.ObjectionsB};
            MarriageAlliances.Add(record);return record;
        }

        public bool CanUniteThroughMarriage(int firstFamily,int secondFamily)
        {
            if(firstFamily==secondFamily||!IsActiveHouse(firstFamily)||!IsActiveHouse(secondFamily))return false;
            foreach(var person in Persons)
            {
                int family=FamilyOf(person.Id);if(family!=firstFamily&&family!=secondFamily)continue;
                foreach(int spouse in person.SpouseIds)
                {int other=FamilyOf(spouse);if(family==firstFamily&&other==secondFamily||family==secondFamily&&other==firstFamily)return true;}
            }
            return false;
        }

        internal void MaintainMarriageAlliances(int now)
        {
            foreach(var a in MarriageAlliances.Where(x=>x.State<MarriageAllianceState.Fulfilled).ToList())
            {
                var pa=GetPerson(a.PawnA);var pb=GetPerson(a.PawnB);
                if(pa?.SpouseIds.Contains(a.PawnB)==true){a.State=MarriageAllianceState.Fulfilled;a.LastChangedTick=now;}
                else if(now>a.ExpiresTick||pa?.InColony!=true||pb?.InColony!=true){a.State=MarriageAllianceState.Expired;a.LastChangedTick=now;}
            }
        }

        private static void ApplyHouseObjections(HouseSituation s,float grievance,float ideology,ref float interest,ref MarriageAllianceObjection objections)
        {
            if(s.LivingSpace<45f){objections|=MarriageAllianceObjection.NoRoom;interest-=20f;}
            if(s.Sustenance<35f){objections|=MarriageAllianceObjection.FoodInsecure;interest-=25f;}
            if(grievance>=45f){objections|=MarriageAllianceObjection.HostileHouses;interest-=35f;}
            if(ideology<-.35f){objections|=MarriageAllianceObjection.Ideology;interest+=ideology*20f;}
        }
        private static float Bound(float value)=>Math.Max(-100f,Math.Min(100f,float.IsNaN(value)?0f:value));
    }
}
