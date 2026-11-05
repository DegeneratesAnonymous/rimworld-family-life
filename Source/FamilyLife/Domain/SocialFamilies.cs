using System;
using System.Collections.Generic;
using System.Linq;
namespace FamilyLife.Domain
{
    public sealed partial class PersonRecord
    {
        // 0 = original automatic lineage/affiliation rules; -1 = explicitly unaffiliated; positive = chosen social house.
        public int SocialFamilyId;
    }
    public sealed partial class Lineage
    {
        public int ParentHouseId;
        // An absorbed house remains in the chronicle and family tree, but no longer participates in active simulation.
        public int AbsorbedIntoId;
        public int RetiredTick;
    }
    public sealed partial class FamilyRegistry
    {
        public bool BelongsToFamily(PersonRecord person,int family)
        {
            if(person==null || family<=0) return false;
            if(!IsActiveHouse(family)) return false;
            if(person.SocialFamilyId!=0) return person.SocialFamilyId>0 && ActiveHouseIdIndexed(person.SocialFamilyId)==family;
            return ActiveHouseIdIndexed(person.PrimaryLineageId)==family || GetLineage(family)?.HasAffiliate(person.Id)==true;
        }
        public bool IsActiveHouse(int family)
        {
            var house=GetLineage(family);
            return house!=null && house.AbsorbedIntoId==0;
        }
        public int ActiveHouseId(int family)
        {
            EnsureIndexes();
            return ActiveHouseIdIndexed(family);
        }
        private int ActiveHouseIdIndexed(int family)
        {
            var seen=new HashSet<int>();
            while(family>0 && seen.Add(family))
            {
                if(!lineageById.TryGetValue(family,out var house)) return 0;
                if(house.AbsorbedIntoId==0) return house.Id;
                family=house.AbsorbedIntoId;
            }
            return 0;
        }
        public bool SetSocialFamily(int person,int family,int now,bool maintain=true)
        {
            var p=GetPerson(person);
            if(p?.InColony!=true || family < -1 || family>0 && !IsActiveHouse(family)) return false;
            if(p.SocialFamilyId==family) return false;
            p.SocialFamilyId=family;
            foreach(var request in Requests.Where(q=>q.Open && (q.RequesterId==person || q.DonorId==person))) request.State=SupplyState.Cancelled;
            foreach(var house in Lineages) if(!BelongsToFamily(p,house.Id)) house.Stewards.Remove(person);
            Chronicle.Add(new FamilyEvent(FamilyEventKind.SocialMembership,now,person,family));
            if(Chronicle.Count>MaxChronicle) Chronicle.RemoveAt(0);
            if(maintain) MaintainAgency(now); return true;
        }
        public Lineage CreateFamilyBranch(int founder,int parent,string name,int now)
        {
            var person=GetPerson(founder); var source=GetLineage(parent);
            if(person?.InColony!=true || person.Juvenile || source==null || !IsActiveHouse(parent) || !BelongsToFamily(person,parent)) return null;
            string label=(name??"").Trim();if(label.Length==0) label=source.Name+" branch";if(label.Length>48) label=label.Substring(0,48);
            var branch=new Lineage{Id=NextLineageId++,Name=label,FounderId=founder,FoundedTick=now,ParentHouseId=parent,
                CrestPrimary=CrestStyle.Index(source.CrestPrimary,source.Id,CrestStyle.Colors),CrestSecondary=source.CrestSecondary,
                CrestMark=CrestStyle.Index(source.CrestMark,source.Id/CrestStyle.Colors,CrestStyle.Marks),CrestPattern=(source.CrestPattern+1)%CrestStyle.Patterns};
            Lineages.Add(branch);lineageById[branch.Id]=branch;
            SetSocialFamily(founder,branch.Id,now);
            Chronicle.Add(new FamilyEvent(FamilyEventKind.SocialBranch,now,founder,branch.Id));
            if(Chronicle.Count>MaxChronicle) Chronicle.RemoveAt(0);
            return branch;
        }
        /// <summary>Explicit player-directed house union. Ancestry, bonds and existing incident causes are retained.</summary>
        public bool UniteFamilies(int source,int target,int now)
        {
            if(source==target || !IsActiveHouse(source) || !IsActiveHouse(target)) return false;
            var sourceHouse=GetLineage(source);var targetHouse=GetLineage(target);
            int sourceColonyMembers=Persons.Count(p=>p.InColony && BelongsToFamily(p,source));
            if(sourceColonyMembers==0) return false;
            var members=Persons.Where(p=>BelongsToFamily(p,source)).Select(p=>p.Id).ToList();
            int targetMembers=Persons.Count(p=>p.InColony && BelongsToFamily(p,target));
            int total=Math.Max(1,sourceColonyMembers+targetMembers);
            while(sourceHouse.Culture.Count<DimInfo.Count) sourceHouse.Culture.Add(0);
            while(targetHouse.Culture.Count<DimInfo.Count) targetHouse.Culture.Add(0);
            for(int i=0;i<DimInfo.Count;i++) targetHouse.Culture[i]=(targetHouse.Culture[i]*targetMembers+sourceHouse.Culture[i]*sourceColonyMembers)/total;
            foreach(var tradition in sourceHouse.Traditions)
            {
                var held=targetHouse.Traditions.FirstOrDefault(t=>t.Id==tradition.Id);
                if(held==null) targetHouse.Traditions.Add(new TraditionRecord{Id=tradition.Id,AdoptedTick=tradition.AdoptedTick,LastHeldTick=tradition.LastHeldTick,TimesHeld=tradition.TimesHeld,Muted=tradition.Muted});
                else {held.AdoptedTick=Math.Min(held.AdoptedTick,tradition.AdoptedTick);held.LastHeldTick=Math.Max(held.LastHeldTick,tradition.LastHeldTick);held.TimesHeld+=tradition.TimesHeld;held.Muted&=tradition.Muted;}
            }
            foreach(int id in members)
            {
                var member=GetPerson(id);
                if(member.InColony) SetSocialFamily(id,target,now,false);
                else member.SocialFamilyId=target;
            }
            foreach(var claim in Claims) {if(claim.OwnerFamily==source) claim.OwnerFamily=target;if(claim.HolderFamily==source) claim.HolderFamily=target;}
            MergeAbsorbedHouseRelations(source,target);
            foreach(var contact in HouseContacts.Where(c=>c.FamilyId==source).ToList())
            {
                var existing=ContactOf(contact.PersonId,target);
                if(existing==null) contact.FamilyId=target;
                else {existing.Respect=Math.Max(existing.Respect,contact.Respect);existing.ContactDays=Math.Max(existing.ContactDays,contact.ContactDays);existing.ServiceDays=Math.Max(existing.ServiceDays,contact.ServiceDays);existing.LastContactDay=Math.Max(existing.LastContactDay,contact.LastContactDay);existing.LastPositiveDay=Math.Max(existing.LastPositiveDay,contact.LastPositiveDay);existing.LastTick=Math.Max(existing.LastTick,contact.LastTick);HouseContacts.Remove(contact);}
            }
            GatheringRequests.RemoveAll(g=>g.LineageId==source);
            foreach(var petition in HousePetitions.Where(q=>q.State<=PetitionState.Heard && (q.FamilyId==source || q.SourceFamilyId==source))) petition.State=PetitionState.Expired;
            if(targetHouse.ZoneAreaId==0 && sourceHouse.ZoneAreaId!=0)
            {targetHouse.ZoneAreaId=sourceHouse.ZoneAreaId;targetHouse.ZoneMapId=sourceHouse.ZoneMapId;targetHouse.ZoneApplied.Clear();}
            sourceHouse.AbsorbedIntoId=target;sourceHouse.RetiredTick=now;sourceHouse.HeadId=0;sourceHouse.Stewards.Clear();
            sourceHouse.ZoneAreaId=0;sourceHouse.ZoneMapId=-1;sourceHouse.ZoneApplied.Clear();
            MaintainAgency(now);
            // Cancel in-flight agreements whose parties now belong to one house; there is no fictitious handover.
            foreach(var request in Requests.Where(r=>r.Open && FamilyOf(r.RequesterId)==FamilyOf(r.DonorId))) request.State=SupplyState.Cancelled;
            return true;
        }
        private void MergeAbsorbedHouseRelations(int source,int target)
        {
            foreach(var tie in Ties.Where(t=>t.LineageA==source || t.LineageB==source).ToList())
            {
                int other=tie.LineageA==source?tie.LineageB:tie.LineageA;
                Ties.Remove(tie);
                if(other==target) continue;
                var merged=EnsureTie(target,other);
                if(merged==null) continue;
                merged.Familiarity=Math.Max(merged.Familiarity,tie.Familiarity);
                merged.Goodwill=Math.Max(merged.Goodwill,tie.Goodwill);
                merged.Grievance=Math.Max(merged.Grievance,tie.Grievance);
                merged.Shared+=tie.Shared;merged.LastTick=Math.Max(merged.LastTick,tie.LastTick);
            }
        }
    }
}
