using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;
namespace FamilyLife.Domain
{
    public enum HospitalityStoryKind { Mentoring, CreditClaim, FalseBlame, Rivalry, Introduction, Confidence, Disclosure, HouseRules, FamilyPlan, OwnershipClaim, Coverage }
    public sealed class HospitalityStoryTask
    {
        public int Id,Business,Actor,Other,Third,Order,Confidence,Created,Deadline;public HospitalityStoryKind Kind;public bool Active,Completed;
    }
    public sealed class HospitalityConfidence
    {
        public int Id,Business,Confider,Keeper,Tick;public bool Disclosed;public readonly HashSet<int> Knows=new HashSet<int>();
    }
    public sealed class StoryEvidence
    {
        public bool RealConversation,OtherPresent,ThirdPresent,OwnsWork,OtherOwnsWork,ActorCausedFailure,OtherInnocent,ComparableWork,KnownConfidence,ConfiderCanHear,ActualDamage,AcceptedPromise,RecordedHeir,AcceptedCoverage;
        public bool Allows(HospitalityStoryKind kind)
        {
            if(!RealConversation||!OtherPresent)return false;
            switch(kind)
            {
                case HospitalityStoryKind.Mentoring:return OwnsWork;
                case HospitalityStoryKind.CreditClaim:return OtherOwnsWork&&ThirdPresent;
                case HospitalityStoryKind.FalseBlame:return ActorCausedFailure&&OtherInnocent;
                case HospitalityStoryKind.Rivalry:return ComparableWork;
                case HospitalityStoryKind.Introduction:return ThirdPresent;
                case HospitalityStoryKind.Confidence:return true;
                case HospitalityStoryKind.Disclosure:return KnownConfidence&&ConfiderCanHear&&ThirdPresent;
                case HospitalityStoryKind.HouseRules:return ActualDamage;
                case HospitalityStoryKind.FamilyPlan:return AcceptedPromise;
                case HospitalityStoryKind.OwnershipClaim:return RecordedHeir;
                case HospitalityStoryKind.Coverage:return AcceptedCoverage;
                default:return false;
            }
        }
    }
    public sealed class HospitalityNarrativeBook:IModuleState
    {
        public const string Id="wils.hospitality.stories";public int Next=1,NextConfidence=1;
        public readonly List<HospitalityStoryTask> Tasks=new List<HospitalityStoryTask>();
        public readonly List<HospitalityConfidence> Confidences=new List<HospitalityConfidence>();
        public readonly HashSet<int> WorkedPromises=new HashSet<int>(),ExcusedPromises=new HashSet<int>(),HandledPromises=new HashSet<int>();
        public readonly Dictionary<string,int> Counts=new Dictionary<string,int>();
        public HospitalityStoryTask Schedule(int business,int actor,int other,HospitalityStoryKind kind,int now,int order=0,int third=0,int confidence=0)
        {
            if(actor<=0||other<=0||actor==other||Tasks.Any(t=>!t.Completed&&t.Actor==actor))return null;
            var task=new HospitalityStoryTask{Id=Next++,Business=business,Actor=actor,Other=other,Kind=kind,Created=now,Deadline=now+10000,Order=order,Third=third,Confidence=confidence};Tasks.Add(task);return task;
        }
        public HospitalityConfidence Confide(int business,int confider,int keeper,int now)
        {
            var recent=Confidences.FirstOrDefault(c=>c.Business==business&&c.Confider==confider&&c.Keeper==keeper&&now-c.Tick<180000);if(recent!=null)return recent;
            var c=new HospitalityConfidence{Id=NextConfidence++,Business=business,Confider=confider,Keeper=keeper,Tick=now};c.Knows.Add(confider);c.Knows.Add(keeper);Confidences.Add(c);return c;
        }
        public bool Disclose(int confidence,int keeper,int listener,bool confiderCanHear)
        {
            var c=Confidences.FirstOrDefault(c=>c.Id==confidence);if(c==null||c.Disclosed||c.Keeper!=keeper||!c.Knows.Contains(keeper)||listener==c.Confider||listener==keeper||listener<=0||c.Knows.Contains(listener)||!confiderCanHear)return false;
            c.Knows.Add(listener);c.Disclosed=true;return true;
        }
        public bool MissedPromise(Commitment promise,bool workerActuallyWorked)
        {
            if(promise==null||promise.State!=2||!workerActuallyWorked||ExcusedPromises.Contains(promise.Id)||HandledPromises.Contains(promise.Id))return false;HandledPromises.Add(promise.Id);return true;
        }
        public int Count(string key)=>Counts.TryGetValue(key,out int n)?n:0;
        public void Increment(string key){Counts[key]=Count(key)+1;}
        public string Encode()
        {
            var w=new StateWriter().Int("next",Next).Int("secret",NextConfidence).Ints("worked",WorkedPromises.OrderBy(x=>x).ToList()).Ints("excused",ExcusedPromises.OrderBy(x=>x).ToList()).Ints("handled",HandledPromises.OrderBy(x=>x).ToList());
            w.Strings("tasks",Tasks.Select(t=>new StateWriter().Ints("v",new[]{t.Id,t.Business,t.Actor,t.Other,t.Third,t.Order,t.Confidence,t.Created,t.Deadline,(int)t.Kind}).Bool("active",t.Active).Bool("done",t.Completed).ToString()).ToList());
            w.Strings("confidence",Confidences.Select(c=>new StateWriter().Ints("v",new[]{c.Id,c.Business,c.Confider,c.Keeper,c.Tick}).Bool("disclosed",c.Disclosed).Ints("knows",c.Knows.OrderBy(x=>x).ToList()).ToString()).ToList());
            return w.Strings("counts",Counts.OrderBy(x=>x.Key,StringComparer.Ordinal).Select(x=>new StateWriter().Strings("k",new[]{x.Key}).Int("v",x.Value).ToString()).ToList()).ToString();
        }
        public static HospitalityNarrativeBook Decode(StateReader r)
        {
            var b=new HospitalityNarrativeBook{Next=r.Int("next",1),NextConfidence=r.Int("secret",1)};b.WorkedPromises.UnionWith(r.Ints("worked"));b.ExcusedPromises.UnionWith(r.Ints("excused"));b.HandledPromises.UnionWith(r.Ints("handled"));
            foreach(var row in r.Strings("tasks")){var x=new StateReader(row);var v=x.Ints("v");if(v.Count==10)b.Tasks.Add(new HospitalityStoryTask{Id=v[0],Business=v[1],Actor=v[2],Other=v[3],Third=v[4],Order=v[5],Confidence=v[6],Created=v[7],Deadline=v[8],Kind=(HospitalityStoryKind)v[9],Active=x.Bool("active"),Completed=x.Bool("done")});}
            foreach(var row in r.Strings("confidence")){var x=new StateReader(row);var v=x.Ints("v");if(v.Count!=5)continue;var c=new HospitalityConfidence{Id=v[0],Business=v[1],Confider=v[2],Keeper=v[3],Tick=v[4],Disclosed=x.Bool("disclosed")};c.Knows.UnionWith(x.Ints("knows"));b.Confidences.Add(c);}
            foreach(var row in r.Strings("counts")){var x=new StateReader(row);var key=x.Strings("k").FirstOrDefault();if(key!=null)b.Counts[key]=x.Int("v");}return b;
        }
    }
    public sealed class HospitalityNarrativeFactory:IStateFactory<HospitalityNarrativeBook>{public static readonly HospitalityNarrativeFactory Instance=new HospitalityNarrativeFactory();public int Version=>1;public HospitalityNarrativeBook Create()=>new HospitalityNarrativeBook();public HospitalityNarrativeBook Decode(StateReader r,int v)=>HospitalityNarrativeBook.Decode(r);public HospitalityNarrativeBook FromLegacy(StateReader r)=>Create();}
    public static class HospitalityNarrativeAccess {public static HospitalityNarrativeBook HospitalityStories(this FamilyRegistry r)=>r.Shared.Of(HospitalityNarrativeBook.Id,HospitalityNarrativeFactory.Instance);}
}
