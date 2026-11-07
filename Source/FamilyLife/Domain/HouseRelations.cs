using System;
using System.Collections.Generic;
using System.Linq;
namespace FamilyLife.Domain
{
    public enum HouseRole : byte { Outsider, FamilyFriend, Associate, TrustedAssociate, Member, Steward, Head }
    public enum PetitionKind : byte { Admission, Recognition, Union }
    public enum PetitionState : byte { Proposed, Heard, Accepted, Denied, Expired }
    public sealed partial class HouseContact
    {
        public int PersonId, FamilyId, ContactDays, ServiceDays, LastContactDay = -1, LastPositiveDay = -1, LastTick;
        public float Respect;
    }
    public sealed partial class HousePetition
    {
        public int Id, PersonId, FamilyId, CreatedTick, HeardTick, HeardBy, SourceFamilyId, ReceivedBy;
        public PetitionKind Kind;
        public PetitionState State;
    }
    public sealed partial class Lineage
    {
        public int HeadId, VacantHeadId, VacantSeenTick;
        public List<int> Stewards = new List<int>();
    }
    public sealed partial class FamilyRegistry
    {
        public List<HouseContact> HouseContacts = new List<HouseContact>();
        public List<HousePetition> HousePetitions = new List<HousePetition>();
        public int NextPetitionId = 1;
        public HouseContact ContactOf(int person,int family) => HouseContacts.FirstOrDefault(c=>c.PersonId==person && c.FamilyId==family);
        private HouseContact EnsureContact(int person,int family)
        {
            var c=ContactOf(person,family);
            if(c!=null) return c;
            if(HouseContacts.Count>=1024) return null;
            c=new HouseContact{PersonId=person,FamilyId=family};HouseContacts.Add(c);return c;
        }
        public void NoteHouseContact(int person,int family,int now)
        {
            if(GetPerson(person)?.IsAlive!=true || !IsActiveHouse(family) || BelongsToFamily(GetPerson(person),family)) return;
            var c=EnsureContact(person,family);if(c==null)return;int day=now/ActivityTuning.TicksPerDay;
            if(c.LastContactDay<day){c.LastContactDay=day;c.ContactDays++;}c.LastTick=now;
        }
        public void NoteHouseService(int person,int family,float value,int now)
        {
            if(GetPerson(person)?.IsAlive!=true || !IsActiveHouse(family) || BelongsToFamily(GetPerson(person),family) || value==0 || float.IsNaN(value)) return;
            var c=EnsureContact(person,family);if(c==null)return;
            if(value>0)
            {
                int day=now/ActivityTuning.TicksPerDay;
                if(c.LastContactDay<day){c.LastContactDay=day;c.ContactDays++;}
                if(c.LastPositiveDay>=day) return;
                c.LastPositiveDay=day;c.ServiceDays++;c.Respect=Math.Min(100,c.Respect+Math.Min(2,value));
            }
            else c.Respect=Math.Max(0,c.Respect+Math.Max(-20,value));
            c.LastTick=now;
        }
        public int HeadOf(int family)
        {
            var house=GetLineage(family);if(house==null) return 0;
            var incumbent=GetPerson(house.HeadId);
            if(incumbent?.InColony==true && !incumbent.Juvenile && BelongsToFamily(incumbent,family)) return incumbent.Id;
            if(house.HeadId!=0 && house.VacantHeadId==0) house.VacantHeadId=house.HeadId;
            var members=Persons.Where(p=>p.InColony && !p.Juvenile && BelongsToFamily(p,family)).ToList();
            house.HeadId=members.FirstOrDefault(p=>p.Id==house.FounderId)?.Id ?? members.OrderBy(p=>p.BirthAbsTick).ThenBy(p=>p.Id).FirstOrDefault()?.Id ?? 0;
            return house.HeadId;
        }
        public HouseRole RoleInHouse(int person,int family)
        {
            var p=GetPerson(person);var house=GetLineage(family);if(p==null || house==null) return HouseRole.Outsider;
            if(BelongsToFamily(p,family))
            {
                if(!p.Juvenile && p.InColony && HeadOf(family)==person) return HouseRole.Head;
                if(!p.Juvenile && p.InColony && house.Stewards.Contains(person)) return HouseRole.Steward;
                return HouseRole.Member;
            }
            var contact=ContactOf(person,family);
            return contact?.Respect>=20 && contact.ServiceDays>=10 ? HouseRole.TrustedAssociate : contact?.Respect>=6 && contact.ServiceDays>=3 ? HouseRole.Associate : contact?.ContactDays>=3 ? HouseRole.FamilyFriend : HouseRole.Outsider;
        }
        public bool CanManageHouse(int person,int family) => GetPerson(person)?.InColony==true && RoleInHouse(person,family)>=HouseRole.Steward;
        public bool DelegateHouse(int leader,int member,int family,bool appoint,int now=0)
        {
            var house=GetLineage(family);var p=GetPerson(member);
            if(HeadOf(family)!=leader || p?.InColony!=true || p.Juvenile || !BelongsToFamily(p,family) || member==leader) return false;
            house.Stewards.Remove(member);
            if(appoint){house.Stewards.Add(member);BeginStewardship(member,family,now);}
            else EndStewardship(member,family,now);
            return true;
        }
        public HousePetition ProposePetition(int person,int family,PetitionKind kind,int now)
        {
            var p=GetPerson(person);
            if(!Enum.IsDefined(typeof(PetitionKind),kind) || p?.InColony!=true || GetLineage(family)==null || HeadOf(family)==0 || HeadOf(family)==person ||
                kind==PetitionKind.Recognition && !BelongsToFamily(p,family) || kind==PetitionKind.Admission && BelongsToFamily(p,family) ||
                kind==PetitionKind.Union && (FamilyOf(person)==0 || HeadOf(FamilyOf(person))!=person) ||
                HousePetitions.Any(q=>q.PersonId==person && q.FamilyId==family && (q.State<=PetitionState.Heard || now-q.CreatedTick<ActivityTuning.TicksPerDay))) return null;
            if(HousePetitions.Count>=128)
            {var old=HousePetitions.FirstOrDefault(q=>q.State>PetitionState.Heard);if(old==null)return null;HousePetitions.Remove(old);}
            var petition=new HousePetition{Id=NextPetitionId++,PersonId=person,FamilyId=family,Kind=kind,CreatedTick=now,SourceFamilyId=FamilyOf(person)};HousePetitions.Add(petition);return petition;
        }
        public bool HearPetition(int petition,int leader,int now)
        {
            var q=HousePetitions.FirstOrDefault(p=>p.Id==petition);
            if(q==null || q.State!=PetitionState.Proposed || HeadOf(q.FamilyId)!=leader || now-q.CreatedTick>3*ActivityTuning.TicksPerDay) return false;
            q.State=PetitionState.Heard;q.HeardTick=now;q.HeardBy=leader;return true;
        }
        public bool DecidePetition(int petition,int leader,bool accept,int now)
        {
            var q=HousePetitions.FirstOrDefault(p=>p.Id==petition);
            if(q==null || q.State!=PetitionState.Heard || q.HeardBy!=leader || HeadOf(q.FamilyId)!=leader || GetPerson(q.PersonId)?.InColony!=true || now-q.CreatedTick>3*ActivityTuning.TicksPerDay) return false;
            int heardAt=q.HeardTick;
            if(accept)
            {
                if(q.Kind==PetitionKind.Admission && !SetSocialFamily(q.PersonId,q.FamilyId,now)) return false;
                if(q.Kind==PetitionKind.Recognition && !DelegateHouse(leader,q.PersonId,q.FamilyId,true,now)) return false;
                if(q.Kind==PetitionKind.Union && (FamilyOf(q.PersonId)!=q.SourceFamilyId || HeadOf(q.SourceFamilyId)!=q.PersonId || !CanUniteThroughMarriage(q.FamilyId,q.SourceFamilyId) || !UniteFamilies(q.FamilyId,FamilyOf(q.PersonId),now))) return false;
            }
            q.State=accept?PetitionState.Accepted:PetitionState.Denied;q.HeardBy=leader;q.HeardTick=heardAt;return true;
        }
        public bool WouldAcceptPetition(HousePetition q,int leader)
        {
            if(q==null || HeadOf(q.FamilyId)!=leader) return false;
            float trust=GetBond(leader,q.PersonId)?.Trust??0;
            float respect=ContactOf(q.PersonId,q.FamilyId)?.Respect??0;
            if(q.Kind==PetitionKind.Admission) return trust>=40 || respect>=6;
            if(q.Kind==PetitionKind.Recognition) return DeservesStewardship(q.PersonId,q.FamilyId,leader);
            return trust>=55 && respect>=10;
        }
        /// <summary>Stewardship follows demonstrated competence and trust; it is not standing a member can purchase by asking.</summary>
        public bool DeservesStewardship(int person,int family,int leader=0)
        {
            if(leader==0)leader=HeadOf(family);
            var member=GetPerson(person);
            if(member?.InColony!=true||member.Juvenile||!BelongsToFamily(member,family)||leader==person)return false;
            float trust=GetBond(leader,person)?.Trust??0;
            float service=PrestigeOf(person)?.Value??0;
            int connections=AssessBelonging(person).TrustedConnections;
            return trust>=50&&connections>=2&&(service>=2||member.TopSkillLevel>=10);
        }
        private void MaintainHouseRelations(int now)
        {
            foreach(var q in HousePetitions.Where(q=>q.State<=PetitionState.Heard))
            {
                if(q.State==PetitionState.Heard && q.HeardBy!=HeadOf(q.FamilyId)) {q.State=PetitionState.Proposed;q.HeardTick=0;q.HeardBy=0;}
                if(GetPerson(q.PersonId)?.InColony!=true || now-q.CreatedTick>3*ActivityTuning.TicksPerDay) q.State=PetitionState.Expired;
            }
        }
    }
}
