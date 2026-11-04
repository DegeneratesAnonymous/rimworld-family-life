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
    }
    public sealed partial class FamilyRegistry
    {
        public bool BelongsToFamily(PersonRecord person,int family)
        {
            if(person==null || family<=0) return false;
            if(person.SocialFamilyId!=0) return person.SocialFamilyId==family;
            return person.PrimaryLineageId==family || GetLineage(family)?.HasAffiliate(person.Id)==true;
        }
        public bool SetSocialFamily(int person,int family,int now,bool maintain=true)
        {
            var p=GetPerson(person);
            if(p?.InColony!=true || family < -1 || family>0 && GetLineage(family)==null) return false;
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
            if(person?.InColony!=true || person.Juvenile || source==null || !BelongsToFamily(person,parent)) return null;
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
            if(source==target || GetLineage(source)==null || GetLineage(target)==null) return false;
            var members=Persons.Where(p=>p.InColony && BelongsToFamily(p,source)).Select(p=>p.Id).ToList();
            if(members.Count==0) return false;
            foreach(int id in members) SetSocialFamily(id,target,now,false);
            foreach(var claim in Claims) {if(claim.OwnerFamily==source) claim.OwnerFamily=target;if(claim.HolderFamily==source) claim.HolderFamily=target;}
            MaintainAgency(now);
            // Cancel in-flight agreements whose parties now belong to one house; there is no fictitious handover.
            foreach(var request in Requests.Where(r=>r.Open && FamilyOf(r.RequesterId)==FamilyOf(r.DonorId))) request.State=SupplyState.Cancelled;
            return true;
        }
    }
}
