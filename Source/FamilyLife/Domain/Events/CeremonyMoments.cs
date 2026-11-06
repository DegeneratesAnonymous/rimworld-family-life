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
                Opportunity=new[]{new Cond(need,c=>Fits(need,c))},Outcomes=Outcomes(style),ConcernsPromise=need=="Promise",
                Effects=style=="Repair"?EventEffect.EasesRift:EventEffect.None,ValueAffinity=value
            };
        }

        private static bool Fits(string need,PairContext c)
        {
            switch(need)
            {
                case "Outside":return !c.SameFamily&&!c.Related;
                case "Supplies":return c.ResourcePressure;
                case "Unknown":return c.FamiliarityBetween<25;
                case "Grief":return c.Initiator.Grieving||c.Recipient.Grieving;
                case "Achievement":return c.Initiator.Achievement!=null||c.Recipient.Achievement!=null;
                case "Values":return new[]{Dim.Warmth,Dim.Steadiness,Dim.Devotion,Dim.Status}.Any(d=>Math.Abs(c.Initiator.Axis(d)-c.Recipient.Axis(d))>=.6f);
                case "Memory":return c.Registry.Memories.Any(m=>m.LineageId==c.Initiator.LineageId||m.LineageId==c.Recipient.LineageId);
                case "Promise":return c.Broken!=null;
                case "Rift":return c.Rift!=null;
                case "Wrongdoing":return c.Registry.Incidents.Any(i=>!i.Resolved&&i.ActorId==c.Initiator.Id&&i.KnownBy.Contains(c.Recipient.Id));
                case "Harm":return c.Registry.Incidents.Any(i=>(i.Kind==IncidentKind.Death||i.Kind==IncidentKind.Injury)&&i.KnownBy.Contains(c.Initiator.Id)&&i.KnownBy.Contains(c.Recipient.Id));
                case "Newcomer":return c.Initiator.Newcomer||c.Recipient.Newcomer;
                default:return true;
            }
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
