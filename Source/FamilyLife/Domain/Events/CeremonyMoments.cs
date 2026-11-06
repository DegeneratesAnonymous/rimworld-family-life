using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>Bounded, occasion-specific moments selected during autonomous public ceremonies.</summary>
    public static class CeremonyMoments
    {
        public static readonly IReadOnlyList<EventDef> All=Build();
        public static bool Supports(string kind)=>kind=="wedding"||kind=="funeral"||kind=="grandreunion";
        public static IEnumerable<EventDef> For(string kind)=>All.Where(e=>e.RequiredTradition==kind);
        public static EventDef Get(string id)=>All.FirstOrDefault(e=>e.Id==id);

        private static List<EventDef> Build()
        {
            var r=new List<EventDef>();
            Add(r,"CW","wedding",Dim.Devotion,new[]{"Unknown","Outside","Rift","Values","Grief","Promise","Newcomer","Memory"},
                new[]{"Familiar","Warm","Repair","Clash","Warm","Disclosure","Warm","Warm"});
            Add(r,"CF","funeral",Dim.Devotion,new[]{"Grief","Memory","Rift","Wrongdoing","Values","Harm","Outside","None"},
                new[]{"Warm","Familiar","Repair","Disclosure","Clash","Clash","Trust","Warm"});
            Add(r,"CR","grandreunion",Dim.Hearth,new[]{"Unknown","Outside","Memory","Rift","Values","Supplies","Promise","Newcomer","Achievement","None"},
                new[]{"Familiar","Warm","Warm","Repair","Clash","Clash","Disclosure","Warm","Trust","Warm"});
            return r;
        }

        private static void Add(List<EventDef> into,string prefix,string kind,Dim value,string[] needs,string[] styles)
        {
            for(int i=0;i<needs.Length;i++)into.Add(Scene(prefix+"-"+(i+1).ToString("00"),kind,value,needs[i],styles[i]));
        }

        private static EventDef Scene(string id,string kind,Dim value,string need,string style)
        {
            return new EventDef
            {
                Id=id,Key=id,RequiredTradition=kind,AnyParticipants=true,Bucket=EventBucket.IntraFamily,Scenario=EventScenario.AdultAdult,
                Weight=EventWeight.Meaningful,CooldownDays=5,Skill="Social",SkillShare=.25f,Motive=new Drive(.5f),
                Reception=new Drive(.45f,Factor.RecipientTrust,.25f,Factor.RecipientTension,-.2f,Factor.RecipientWarmth,.1f),
                Opportunity=new[]{new Cond("both people have a reason to be part of this scene",c=>ParticipantsFit(id,kind,c)),new Cond(need,c=>Fits(id,kind,need,c))},Outcomes=Outcomes(style),ConcernsPromise=need=="Promise",
                Effects=style=="Repair"?EventEffect.EasesRift:EventEffect.None,ValueAffinity=value
            };
        }

        private static bool ParticipantsFit(string id,string kind,PairContext c)
        {
            bool a=Connected(c,c.Initiator.Id),b=Connected(c,c.Recipient.Id);
            // The welcome scenes deliberately pair a host with a guest. Other scenes stay among the
            // people whose relationship to the occasion can be explained from the records.
            if(id=="CW-02") return InFamily(c,c.Initiator.Id,c.OccasionFamilyId)&&InFamily(c,c.Recipient.Id,c.OccasionSecondFamilyId)
                || InFamily(c,c.Recipient.Id,c.OccasionFamilyId)&&InFamily(c,c.Initiator.Id,c.OccasionSecondFamilyId);
            if(id=="CF-07") return a!=b;
            if(id=="CR-02") return a&&b&&(IsVisitor(c,c.Initiator.Id)!=IsVisitor(c,c.Recipient.Id));
            return a&&b;
        }

        private static bool Connected(PairContext c,int person)
        {
            if(person==0||c.Registry==null)return false;
            if(InFamily(c,person,c.OccasionFamilyId)||InFamily(c,person,c.OccasionSecondFamilyId)||IsVisitor(c,person))return true;
            if(person==c.OccasionSubjectId||person==c.OccasionSubjectBId)return true;
            return RelatedTo(c,person,c.OccasionSubjectId)||RelatedTo(c,person,c.OccasionSubjectBId);
        }

        private static bool InFamily(PairContext c,int person,int family)
        {
            var record=c.Registry?.GetPerson(person);
            return family>0&&record!=null&&c.Registry.BelongsToFamily(record,family);
        }

        private static bool IsVisitor(PairContext c,int person)=>c.OccasionVisitorIds?.Contains(person)==true;

        private static bool RelatedTo(PairContext c,int person,int subject)
        {
            if(subject==0||person==subject)return false;
            var a=c.Registry.GetPerson(person);var b=c.Registry.GetPerson(subject);
            return a!=null&&b!=null&&(c.Registry.KindOf(a,b).HasValue||c.Registry.KindOf(b,a).HasValue);
        }

        private static bool Fits(string id,string kind,string need,PairContext c)
        {
            switch(need)
            {
                case "Outside":return id=="CR-02"?IsVisitor(c,c.Initiator.Id)!=IsVisitor(c,c.Recipient.Id):!c.SameFamily&&!c.Related;
                case "Supplies":return c.ResourcePressure;
                case "Unknown":return c.FamiliarityBetween<25;
                case "Grief":return kind=="funeral"?c.Initiator.Grieving&&c.Recipient.Grieving:c.Initiator.Grieving||c.Recipient.Grieving;
                case "Achievement":return c.Initiator.Achievement!=null||c.Recipient.Achievement!=null;
                case "Values":return new[]{Dim.Warmth,Dim.Steadiness,Dim.Devotion,Dim.Status}.Any(d=>Math.Abs(c.Initiator.Axis(d)-c.Recipient.Axis(d))>=.6f);
                case "Memory":return c.Registry.Memories.Any(m=>MemoryFits(kind,m,c));
                case "Promise":return c.Broken!=null;
                case "Rift":return c.Rift!=null;
                case "Wrongdoing":return c.Registry.Incidents.Any(i=>!i.Resolved&&i.VictimId==c.OccasionSubjectId&&
                    (i.ActorId==c.Initiator.Id||i.ActorId==c.Recipient.Id)&&i.KnownBy.Contains(c.Initiator.Id)&&i.KnownBy.Contains(c.Recipient.Id));
                case "Harm":return c.Registry.Incidents.Any(i=>i.Kind==IncidentKind.Death&&i.VictimId==c.OccasionSubjectId&&
                    i.KnownBy.Contains(c.Initiator.Id)&&i.KnownBy.Contains(c.Recipient.Id));
                case "Newcomer":return c.Initiator.Newcomer||c.Recipient.Newcomer;
                default:return true;
            }
        }

        private static bool MemoryFits(string kind,Memory memory,PairContext c)
        {
            if(memory==null)return false;
            if(kind=="wedding")return memory.Kind==MemoryKind.Wedding&&
                (memory.A==c.OccasionSubjectId||memory.B==c.OccasionSubjectId||memory.A==c.OccasionSubjectBId||memory.B==c.OccasionSubjectBId);
            if(kind=="funeral")return memory.Kind==MemoryKind.Loss&&
                (memory.A==c.OccasionSubjectId||memory.B==c.OccasionSubjectId);
            return memory.LineageId==c.OccasionFamilyId&&memory.Kind!=MemoryKind.Rupture;
        }

        private static MomentOutcome[] Outcomes(string style)
        {
            var poor=new BondChange(-1,-2,4,1);var flat=new BondChange(0,0,1,1);var good=new BondChange(1,1,-1,2);var great=new BondChange(3,2,-3,2);var receiving=great;
            if(style=="Trust"){good=new BondChange(0,2,0,1);great=receiving=new BondChange(1,5,-1,1);}
            if(style=="Familiar"){good=new BondChange(0,0,0,3);great=receiving=new BondChange(0,1,0,5);}
            if(style=="Clash"||style=="Repair"){poor=new BondChange(-2,-3,6,2);flat=new BondChange(0,-1,2,1);great=receiving=new BondChange(1,1,-5,2);}
            if(style=="Disclosure"){great=new BondChange(2,1,-3,1);receiving=new BondChange(0,-2,-1,3);}
            return new[]{new MomentOutcome(poor,poor),new MomentOutcome(flat,flat),new MomentOutcome(good,good),new MomentOutcome(great,receiving)};
        }
    }
}
