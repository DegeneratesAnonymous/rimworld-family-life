using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum CeremonyKind : byte { None, Tradition, ComingOfAge, Funeral, Reconciliation, Wedding, GrandReunion }
    public enum CeremonyState : byte { Planned, Active, Completed, Cancelled, Expired }

    [Flags]
    public enum CeremonyInfluence : ushort
    {
        None=0, Engagement=1, RecentDeath=2, MissingKin=4, FamilyCulture=8,
        StrongRelationships=16, HeadSupport=32, SuitableVenue=64, Hospitality=128
    }

    public sealed partial class CeremonyRecord
    {
        public int Id, LineageId, SecondLineageId, SubjectId, SubjectBId, OrganizerId;
        public int StartedTick, CompletedTick, Attendees, Visitors, Moments;
        public CeremonyKind Kind;
        public CeremonyInfluence Influences;
        public float Outcome;
    }

    public sealed partial class PrestigeRecord
    {
        public int PersonId, LastTick, CeremoniesOrganized;
        public float Value;
    }

    public sealed partial class FamilyRegistry
    {
        public const int GrandReunionInterval = 60 * ActivityTuning.TicksPerDay;
        public List<CeremonyRecord> Ceremonies = new List<CeremonyRecord>();
        public List<PrestigeRecord> Prestige = new List<PrestigeRecord>();

        public PrestigeRecord PrestigeOf(int person) => Prestige.FirstOrDefault(p=>p.PersonId==person);

        private PrestigeRecord EnsurePrestige(int person)
        {
            var record=PrestigeOf(person);
            if(record==null && GetPerson(person)!=null){record=new PrestigeRecord{PersonId=person};Prestige.Add(record);}
            return record;
        }

        public void NotePublicService(int person,int family,float amount,int now)
        {
            if(amount<=0 || GetPerson(person)?.InColony!=true || FamilyOf(person)!=family) return;
            var prestige=EnsurePrestige(person);if(prestige==null)return;
            prestige.Value=Math.Min(100,prestige.Value+Math.Min(4,amount));prestige.LastTick=now;
            var house=GetLineage(family);if(house!=null)house.Standing=Math.Min(100,house.Standing+Math.Min(1,amount*.2f));
        }

        public void UpdateOrganicCeremonies(int now,bool visitingKin=true)
        {
            EnsureIndexes();
            foreach(var request in GatheringRequests.Where(r=>r.State==CeremonyState.Planned && r.Expires<now).ToList())
            {request.State=CeremonyState.Expired;GatheringRequests.Remove(request);}
            foreach(var request in GatheringRequests.Where(r=>r.State==CeremonyState.Active&&r.StartedTick>0&&now-r.StartedTick>2*ActivityTuning.TicksPerDay).ToList())
            {request.State=CeremonyState.Cancelled;GatheringRequests.Remove(request);}
            if(visitingKin)PlanGrandReunions(now);
        }

        private void PlanGrandReunions(int now)
        {
            foreach(var house in Lineages.Where(h=>IsActiveHouse(h.Id)))
            {
                if(house.NextCeremonyCheckTick==0)
                {
                    int spread=unchecked(house.Id*1103515245)&int.MaxValue;
                    spread%=20*ActivityTuning.TicksPerDay;
                    house.NextCeremonyCheckTick=now+15*ActivityTuning.TicksPerDay+spread;
                }
                if(now<house.NextCeremonyCheckTick || now-house.LastGrandReunionTick<GrandReunionInterval)continue;
                if(GatheringRequests.Any(r=>r.LineageId==house.Id && r.Ceremony==CeremonyKind.GrandReunion)){house.NextCeremonyCheckTick=now+ActivityTuning.TicksPerDay;continue;}
                var present=Persons.Where(p=>p.InColony && BelongsToFamily(p,house.Id)).ToList();
                var away=Persons.Where(p=>p.Presence==Presence.Away && p.IsAlive && BelongsToFamily(p,house.Id)).ToList();
                if(present.Count<2 || away.Count==0){house.NextCeremonyCheckTick=now+10*ActivityTuning.TicksPerDay;continue;}
                int missing=MissingPairs(now).Count(p=>present.Any(m=>m.Id==p.Key)&&away.Any(a=>a.Id==p.Value));
                bool cultural=house.CultureOf(Dim.Devotion)>=10 || house.CultureOf(Dim.Hearth)>=15 || house.Traditions.Any(t=>t.Id=="reunion");
                if(!cultural && missing==0){house.NextCeremonyCheckTick=now+10*ActivityTuning.TicksPerDay;continue;}
                var visitors=away.OrderByDescending(v=>present.Max(p=>GetBond(p.Id,v.Id)?.Attachment??0)).ThenBy(v=>v.Id).Take(6).Select(v=>v.Id).ToList();
                var request=RequestCeremony(CeremonyKind.GrandReunion,house.Id,0,0,now,2*ActivityTuning.TicksPerDay,8*ActivityTuning.TicksPerDay,
                    CeremonyInfluence.MissingKin|(cultural?CeremonyInfluence.FamilyCulture:CeremonyInfluence.None));
                if(request!=null)
                {
                    request.VisitorIds.AddRange(visitors);
                    house.LastGrandReunionTick=now;
                    house.NextCeremonyCheckTick=now+GrandReunionInterval;
                }
                else house.NextCeremonyCheckTick=now+ActivityTuning.TicksPerDay;
            }
        }

        private static bool SameSubjects(GatheringRequest r,int a,int b) => r.SubjectId==a&&r.SubjectBId==b || r.SubjectId==b&&r.SubjectBId==a;
        private static bool SameSubjects(CeremonyRecord r,int a,int b) => r.SubjectId==a&&r.SubjectBId==b || r.SubjectId==b&&r.SubjectBId==a;

        public GatheringRequest RequestCeremony(CeremonyKind ceremony,int lineageId,int subjectId,int subjectBId,int now,int delayTicks,int lifetimeTicks,
            CeremonyInfluence influences=CeremonyInfluence.None,int secondLineageId=0)
        {
            EnsureIndexes();if(!IsActiveHouse(lineageId))return null;
            if(GatheringRequests.Any(r=>r.Ceremony==ceremony&&r.LineageId==lineageId&&r.SubjectId==subjectId&&r.SubjectBId==subjectBId&&
                (r.State==CeremonyState.Planned||r.State==CeremonyState.Active)))return null;
            var request=new GatheringRequest{Id=NextRequestId++,Kind=KindName(ceremony),Ceremony=ceremony,State=CeremonyState.Planned,LineageId=lineageId,
                SecondLineageId=secondLineageId,SubjectId=subjectId,SubjectBId=subjectBId,CreatedTick=now,NotBefore=now+delayTicks,Expires=now+delayTicks+lifetimeTicks,
                Influences=influences,Seed=unchecked(lineageId*397^subjectId*31^subjectBId^now/ActivityTuning.TicksPerDay)};
            GatheringRequests.Add(request);return request;
        }

        public static string KindName(CeremonyKind kind)
        {
            switch(kind){case CeremonyKind.ComingOfAge:return "comingofage";case CeremonyKind.Funeral:return "funeral";case CeremonyKind.Reconciliation:return "reconciliation";
                case CeremonyKind.Wedding:return "wedding";case CeremonyKind.GrandReunion:return "grandreunion";default:return "tradition";}
        }

        public static CeremonyKind CeremonyOf(string kind)
        {
            switch(kind){case "comingofage":return CeremonyKind.ComingOfAge;case "memorial":case "funeral":return CeremonyKind.Funeral;case "reconciliation":return CeremonyKind.Reconciliation;
                case "wedding":return CeremonyKind.Wedding;case "grandreunion":return CeremonyKind.GrandReunion;default:return CeremonyKind.Tradition;}
        }

        public bool MarkCeremonyStarted(int requestId,int organizer,int mapId,int now,IEnumerable<int> invited)
        {
            var r=GatheringRequests.FirstOrDefault(x=>x.Id==requestId);
            if(r==null||r.State!=CeremonyState.Planned)return false;
            r.State=CeremonyState.Active;r.StartedTick=now;r.OrganizerId=organizer;r.MapId=mapId;
            r.InvitedIds=(invited??Enumerable.Empty<int>()).Distinct().Take(64).ToList();
            r.MomentBudget=Math.Max(2,Math.Min(6,2+r.InvitedIds.Count/4));return true;
        }

        public CeremonyRecord CompleteCeremony(int requestId,int now,int attendees,int moments,float outcome)
        {
            var r=GatheringRequests.FirstOrDefault(x=>x.Id==requestId);if(r==null)return null;
            r.State=CeremonyState.Completed;GatheringRequests.Remove(r);
            var record=new CeremonyRecord{Id=r.Id,Kind=r.Ceremony,LineageId=r.LineageId,SecondLineageId=r.SecondLineageId,SubjectId=r.SubjectId,SubjectBId=r.SubjectBId,
                OrganizerId=r.OrganizerId,StartedTick=r.StartedTick,CompletedTick=now,Attendees=attendees,Visitors=r.VisitorIds.Count,Moments=moments,
                Influences=r.Influences,Outcome=Math.Max(-1,Math.Min(1,outcome))};
            Ceremonies.Add(record);if(Ceremonies.Count>128)Ceremonies.RemoveRange(0,Ceremonies.Count-128);
            if(record.OrganizerId!=0){var p=EnsurePrestige(record.OrganizerId);if(p!=null){p.CeremoniesOrganized++;NotePublicService(record.OrganizerId,record.LineageId,1+Math.Max(0,record.Outcome)*2,now);}}
            var house=GetLineage(record.LineageId);if(house!=null)house.Standing=Math.Max(-100,Math.Min(100,house.Standing+record.Outcome));
            if(record.Kind==CeremonyKind.Wedding)RememberCeremony(record.LineageId,MemoryKind.Wedding,now,record.SubjectId,record.SubjectBId);
            else if(record.Kind==CeremonyKind.Funeral)RememberCeremony(record.LineageId,MemoryKind.Loss,now,record.SubjectId,0);
            else if(record.Kind==CeremonyKind.GrandReunion)RememberCeremony(record.LineageId,MemoryKind.Reunion,now,record.SubjectId,0);
            return record;
        }

        private void RememberCeremony(int family,MemoryKind kind,int now,int a,int b)
        {
            bool recent=Memories.Any(m=>m.LineageId==family&&m.Kind==kind&&m.A==a&&m.B==b&&now-m.Tick<10*ActivityTuning.TicksPerDay);
            if(!recent)Remember(family,kind,now,a,b,1.5f);
        }
    }
}
