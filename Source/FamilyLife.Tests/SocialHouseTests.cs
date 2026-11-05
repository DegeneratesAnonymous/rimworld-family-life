using System;
using System.Linq;
using FamilyLife.Domain;
using Xunit;
namespace FamilyLife.Tests
{
    public class SocialHouseTests
    {
        const int Day=ActivityTuning.TicksPerDay;
        static PersonObservation[] People()=>new[]{T.P(1,spouses:new[]{2}),T.P(2,spouses:new[]{1}),T.Kid(3,new[]{1,2}),T.Kid(4,new[]{1,2}),T.P(11,last:"Jones",spouses:new[]{12}),T.P(12,last:"Jones",spouses:new[]{11})};
        static FamilyRegistry World(){var r=new FamilyRegistry();r.Apply(People(),10*Day);return r;}
        [Fact] public void Social_admission_and_withdrawal_preserve_ancestry_through_reconciliation()
        {var r=World();int old=r.GetPerson(3).PrimaryLineageId,target=r.FamilyOf(11);var parents=r.GetPerson(3).ParentIds.ToArray();Assert.True(r.SetSocialFamily(3,target,10*Day));r.Apply(People(),11*Day);Assert.Equal(target,r.FamilyOf(3));Assert.Equal(old,r.GetPerson(3).PrimaryLineageId);Assert.Equal(parents,r.GetPerson(3).ParentIds);Assert.Contains(r.GetPerson(3),r.MembersOf(target));Assert.DoesNotContain(r.GetPerson(3),r.MembersOf(old));r.SetSocialFamily(3,-1,11*Day);r.Apply(People(),12*Day);Assert.Equal(0,r.FamilyOf(3));}
        [Fact] public void Branches_inherit_identity_with_variation_without_rewriting_the_founder()
        {var r=World();int home=r.FamilyOf(1),blood=r.GetPerson(2).PrimaryLineageId;var source=r.GetLineage(home);source.CrestPrimary=2;source.CrestMark=4;var branch=r.CreateFamilyBranch(2,home,"  Independent  ",10*Day);Assert.NotNull(branch);Assert.Equal("Independent",branch.Name);Assert.Equal(home,branch.ParentHouseId);Assert.Equal(2,branch.CrestPrimary);Assert.Equal(4,branch.CrestMark);Assert.NotEqual(source.CrestPattern,branch.CrestPattern);Assert.Equal(blood,r.GetPerson(2).PrimaryLineageId);Assert.Equal(branch.Id,r.FamilyOf(2));Assert.True(r.IsListedFamily(branch.Id));Assert.Null(r.CreateFamilyBranch(3,home,"Child",10*Day));}
        [Fact] public void Union_moves_communal_property_and_retains_incident_history()
        {var r=World();int a=r.FamilyOf(1),b=r.FamilyOf(11);r.ClaimStock(100,a,a,7,SupplyKind.Food);var incident=r.RecordIncident(IncidentKind.Injury,11,1,7,10*Day,40,new[]{1});r.UniteFamilies(a,b,11*Day);Assert.All(r.Persons.Where(p=>p.InColony),p=>Assert.Equal(b,r.FamilyOf(p.Id)));Assert.Equal(b,r.ClaimFor(100).OwnerFamily);Assert.Equal(a,incident.VictimFamily);Assert.False(incident.Resolved);Assert.Equal(a,r.GetPerson(1).PrimaryLineageId);}
        [Fact] public void Absorbed_houses_keep_history_but_leave_active_simulation()
        {var r=World();int a=r.FamilyOf(1),b=r.FamilyOf(11);r.SetZone(a,7,3);r.GetLineage(a).Culture[(int)Dim.Hearth]=80;r.GetLineage(a).Traditions.Add(new TraditionRecord{Id="feast",AdoptedTick=Day});Assert.True(r.UniteFamilies(a,b,11*Day));Assert.Equal(b,r.GetLineage(a).AbsorbedIntoId);Assert.Equal(0,r.GetLineage(a).ZoneAreaId);Assert.Equal(7,r.GetLineage(b).ZoneAreaId);Assert.False(r.IsListedFamily(a));Assert.DoesNotContain(r.LineagesWithZones(),l=>l.Id==a);Assert.Contains(r.GetLineage(b).Traditions,t=>t.Id=="feast");Assert.False(r.SetSocialFamily(11,a,12*Day));}
        [Fact] public void New_children_follow_a_shared_explicit_social_house()
        {var r=World();int target=r.FamilyOf(11);r.UniteFamilies(r.FamilyOf(1),target,10*Day);r.Apply(People().Concat(new[]{T.Kid(5,new[]{1,2})}),11*Day);Assert.Equal(target,r.FamilyOf(5));Assert.Equal(r.GetPerson(1).PrimaryLineageId,r.GetPerson(5).PrimaryLineageId);}
        [Fact] public void Associates_are_earned_over_separate_days_and_keep_their_own_family()
        {var r=World();int family=r.FamilyOf(1),original=r.FamilyOf(11);for(int n=0;n<20;n++)r.NoteHouseService(11,family,100,10*Day);Assert.Equal(2,r.ContactOf(11,family).Respect);Assert.Equal(HouseRole.Outsider,r.RoleInHouse(11,family));r.NoteHouseService(11,family,2,11*Day);r.NoteHouseService(11,family,2,12*Day);Assert.Equal(HouseRole.Associate,r.RoleInHouse(11,family));Assert.Equal(original,r.FamilyOf(11));Assert.False(r.CanManageHouse(11,family));}
        [Fact] public void Friendship_and_service_are_distinct_paths_to_house_standing()
        {var r=World();int family=r.FamilyOf(1);for(int d=10;d<13;d++)r.NoteHouseContact(11,family,d*Day);Assert.Equal(HouseRole.FamilyFriend,r.RoleInHouse(11,family));Assert.Equal(0,r.ContactOf(11,family).ServiceDays);Assert.Equal(3,r.ContactOf(11,family).ContactDays);r.NoteHouseService(11,family,2,13*Day);Assert.Equal(HouseRole.FamilyFriend,r.RoleInHouse(11,family));}
        [Fact] public void Only_known_wrongdoing_reduces_family_standing_once()
        {var r=World();int family=r.FamilyOf(1);for(int d=10;d<13;d++)r.NoteHouseService(11,family,2,d*Day);var incident=r.RecordIncident(IncidentKind.Theft,11,1,7,13*Day,25,new[]{11});Assert.Equal(6,r.ContactOf(11,family).Respect);r.LearnIncident(incident.Id,1,13*Day);float after=r.ContactOf(11,family).Respect;Assert.True(after<6);r.LearnIncident(incident.Id,2,13*Day);Assert.Equal(after,r.ContactOf(11,family).Respect);}
        [Fact] public void Associates_do_not_automatically_become_administrators()
        {var r=World();int family=r.FamilyOf(1);for(int d=0;d<20;d++)r.NoteHouseService(11,family,2,(10+d)*Day);Assert.Equal(HouseRole.TrustedAssociate,r.RoleInHouse(11,family));Assert.False(r.CanManageHouse(11,family));Assert.True(r.DelegateHouse(1,2,family,true));Assert.Equal(HouseRole.Steward,r.RoleInHouse(2,family));r.SetSocialFamily(2,r.FamilyOf(11),31*Day);Assert.False(r.CanManageHouse(2,family));}
        [Fact] public void Petition_needs_actual_hearing_and_current_head_authority()
        {var r=World();int family=r.FamilyOf(1);var q=r.ProposePetition(11,family,PetitionKind.Admission,10*Day);Assert.NotNull(q);Assert.False(r.DecidePetition(q.Id,1,true,10*Day));Assert.False(r.HearPetition(q.Id,2,10*Day));Assert.True(r.HearPetition(q.Id,1,10*Day));Assert.False(r.DecidePetition(q.Id,2,true,10*Day));Assert.True(r.DecidePetition(q.Id,1,true,10*Day));Assert.Equal(family,r.FamilyOf(11));Assert.Equal(PetitionState.Accepted,q.State);}
        [Fact] public void House_union_requires_two_heads_and_retains_chosen_actor_house()
        {var r=World();int a=r.FamilyOf(1),b=r.FamilyOf(11);Assert.Null(r.ProposePetition(2,b,PetitionKind.Union,10*Day));var q=r.ProposePetition(1,b,PetitionKind.Union,10*Day);Assert.True(r.HearPetition(q.Id,11,10*Day));Assert.True(r.DecidePetition(q.Id,11,true,10*Day));Assert.Equal(a,r.FamilyOf(11));Assert.Equal(0,r.LivingCount(b,Presence.Colony));}
        [Fact] public void A_successor_must_hear_a_pending_request_again()
        {var r=World();int family=r.FamilyOf(1);var q=r.ProposePetition(11,family,PetitionKind.Admission,10*Day);r.HearPetition(q.Id,1,10*Day);r.GetPerson(1).Presence=Presence.Dead;Assert.Equal(2,r.HeadOf(family));Assert.False(r.DecidePetition(q.Id,2,true,10*Day));r.MaintainAgency(10*Day+1);Assert.Equal(PetitionState.Proposed,q.State);Assert.True(r.HearPetition(q.Id,2,10*Day+1));Assert.True(r.DecidePetition(q.Id,2,true,10*Day+1));}
        [Fact] public void Missing_or_invalid_houses_do_not_erase_membership()
        {var r=World();int old=r.FamilyOf(1);Assert.False(r.SetSocialFamily(1,999,10*Day));Assert.Equal(old,r.FamilyOf(1));}
        [Theory] [InlineData(-1,1,1)] [InlineData(100,1,4)] [InlineData(0,5,0)]
        public void Crest_style_has_stable_bounded_indices(int selected,int fallback,int expected)
        {Assert.Equal(expected,CrestStyle.Index(selected,fallback,8));}
        [Fact] public void Same_house_is_not_a_biological_romance_prohibition()
        {var r=World();r.SetSocialFamily(11,r.FamilyOf(1),10*Day);Assert.False(r.AssessKinship(1,11).BlocksRomance);Assert.True(r.AssessKinship(1,3).BlocksRomance);Assert.True(r.AssessKinship(3,4).BlocksRomance);}
        [Fact] public void First_cousins_are_excluded_but_distant_adults_are_not_excluded_by_house_name()
        {var r=new FamilyRegistry();r.Apply(new[]{T.P(1),T.P(2,parents:new[]{1}),T.P(3,parents:new[]{1}),T.P(4,parents:new[]{2}),T.P(5,parents:new[]{3}),T.P(6,parents:new[]{4}),T.P(7,parents:new[]{5})},10*Day);Assert.Equal(4,r.AssessKinship(4,5).Distance);Assert.True(r.AssessKinship(4,5).BlocksRomance);Assert.Equal(6,r.AssessKinship(6,7).Distance);Assert.False(r.AssessKinship(6,7).BlocksRomance);Assert.True(r.AssessKinship(1,7).DirectAncestor);}
        [Fact] public void Missing_parent_records_conservatively_block_romance()
        {var r=new FamilyRegistry();r.Apply(new[]{T.P(1,parents:new[]{999}),T.P(2)},10*Day);var assessment=r.AssessKinship(1,2);Assert.True(assessment.Incomplete);Assert.True(assessment.BlocksRomance);}
        [Fact] public void Stolen_goods_cannot_be_laundered_by_giving_them_away()
        {var r=World();int a=r.FamilyOf(1),b=r.FamilyOf(11);r.Apply(People().Concat(new[]{T.P(21,last:"Third",spouses:new[]{22}),T.P(22,last:"Third",spouses:new[]{21})}),10*Day);r.ClaimStock(100,a,b,7,SupplyKind.Food);var gift=r.OpenSupplyRequest(21,11,100,7,1,SupplyKind.Food,SupplyApproach.Ask,10*Day);gift.State=SupplyState.Granted;r.ExchangeSupply(gift.Id,100,1,new[]{1,11},10*Day);Assert.Equal(a,r.ClaimFor(100).OwnerFamily);}
        [Fact] public void Head_authority_does_not_reveal_secret_plots()
        {var r=World();var root=r.RecordIncident(IncidentKind.Death,11,1,7,10*Day,100,new[]{2});var plot=r.ProposeRevenge(root.Id,2,10*Day,PlotAction.Fight);Assert.NotNull(plot);Assert.Equal(HouseRole.Head,r.RoleInHouse(11,r.FamilyOf(11)));Assert.False(r.KnowsPlot(11,plot));Assert.True(r.KnowsPlot(2,plot));Assert.True(r.KnowsPlot(0,plot));var exposure=r.RecordIncident(IncidentKind.PlotExposed,2,11,7,10*Day+1,25,new[]{2});Assert.False(r.KnowsPlot(11,plot));r.LearnIncident(exposure.Id,11,10*Day+2);Assert.True(r.KnowsPlot(11,plot));}
        [Fact] public void A_sole_founder_and_admitted_crew_are_listed_as_a_social_family()
        {var r=World();int family=r.FamilyOf(11);r.SetSocialFamily(3,family,10*Day);Assert.True(r.IsListedFamily(family));}
        [Fact] public void Admitting_an_older_member_does_not_usurp_the_recognized_head()
        {var r=World();int family=r.FamilyOf(11);Assert.Equal(11,r.HeadOf(family));r.SetSocialFamily(1,family,10*Day);Assert.Equal(11,r.HeadOf(family));}
        [Fact] public void Earned_associates_receive_invitations_without_private_store_or_management_rights()
        {var r=World();int family=r.FamilyOf(1);for(int d=10;d<13;d++)r.NoteHouseService(11,family,2,d*Day);Assert.Contains(11,r.Invitees(family,new[]{1},(a,b)=>0));r.ClaimStock(100,family,family,7,SupplyKind.Food);Assert.False(r.MayUseStock(100,r.FamilyOf(11)));Assert.False(r.CanManageHouse(11,family));}
    }
}
