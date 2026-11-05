using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class StoryTests
    {
        private const int Now = 600000;
        private static FamilyRegistry World()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.Kid(3, new[] { 1, 2 }),
                T.P(11, last: "Jones", spouses: new[] { 12 }), T.P(12, last: "Jones", spouses: new[] { 11 }) }, Now);
            r.UpdateBonds((a,b) => 0, Now, null);
            Assert.NotEqual(r.FamilyOf(1), r.FamilyOf(11));
            return r;
        }
        private static Bond Bond(FamilyRegistry r, int a, int b)
        { r.ChangeBond(a,b,new BondChange(0,0,0,0),Now); return r.GetBond(a,b); }
        private static FamilyIncident Wrong(FamilyRegistry r, IncidentContext context = IncidentContext.None, int[] witnesses = null)
            => r.RecordIncident(IncidentKind.Death, 11, 1, 7, Now, 100, witnesses ?? new[] { 1, 2 }, context);
        private static SupplyRequest Supply(FamilyRegistry r, SupplyApproach approach = SupplyApproach.Ask)
        {
            r.ClaimStock(100, r.FamilyOf(11), r.FamilyOf(11), 7, SupplyKind.Food);
            var q = r.OpenSupplyRequest(1, 11, 100, 7, 2, SupplyKind.Food, approach, Now);
            q.Definition = "MealSimple"; q.Units = 1.8f; return q;
        }
        [Fact] public void Knowledge_is_local_and_family_blame_only_applies_once()
        {
            var r = World(); var i = r.RecordIncident(IncidentKind.Injury,11,1,7,Now,100,new[] { 1 });
            Assert.DoesNotContain(2, i.KnownBy);
            float trust = Bond(r, 2, 11).Trust;
            float grievance = r.GetTie(r.FamilyOf(1), r.FamilyOf(11)).Grievance;
            Assert.True(r.LearnIncident(i.Id, 2, Now + 1));
            Assert.True(r.GetBond(2, 11).Trust < trust);
            Assert.Equal(grievance, r.GetTie(r.FamilyOf(1), r.FamilyOf(11)).Grievance);
            float after = r.GetBond(2, 11).Trust;
            Assert.False(r.LearnIncident(i.Id, 2, Now + 2)); Assert.Equal(after, r.GetBond(2, 11).Trust);
        }
        [Theory] [InlineData(IncidentContext.Accident, 24)] [InlineData(IncidentContext.MentalBreak,39)] [InlineData(IncidentContext.None,60)]
        public void Circumstances_reduce_blame(IncidentContext context, float expected)
        { var r = World(); Wrong(r, context); Assert.Equal(expected, r.GetTie(r.FamilyOf(1), r.FamilyOf(11)).Grievance, 3); }
        [Fact] public void Unknown_killer_never_creates_a_suspect()
        { var r = World(); var i = r.RecordIncident(IncidentKind.Death, 999, 1, 7, Now,100,new[]{2}); Assert.Equal(0,i.ActorId); Assert.Empty(r.Ties); Assert.Null(r.ProposeRevenge(i.Id,2,Now,PlotAction.Lethal)); }
        [Fact] public void A_dead_victim_cannot_broadcast_the_killers_identity()
        { var r=World(); var i=Wrong(r,witnesses:new[]{1}); Assert.DoesNotContain(1,i.KnownBy); Assert.Empty(r.Ties); }
        [Fact] public void Multiple_bullets_are_one_incident()
        {
            var r=World(); var a=r.RecordIncident(IncidentKind.Injury,11,1,7,Now,30,new[]{1});
            var b=r.RecordIncident(IncidentKind.Injury,11,1,7,Now+100,30,new[]{2});
            Assert.Same(a,b); Assert.Single(r.Incidents); Assert.Contains(2,a.KnownBy);
            Assert.NotSame(a,r.RecordIncident(IncidentKind.Injury,11,1,7,Now+16000,30,new[]{1}));
        }
        [Fact] public void Plots_require_a_known_severe_cause_and_adult_organizer()
        {
            var r=World(); var i=Wrong(r,witnesses:new[]{1});
            Assert.Null(r.ProposeRevenge(i.Id,2,Now,PlotAction.Lethal));
            r.LearnIncident(i.Id,3,Now); Assert.Null(r.ProposeRevenge(i.Id,3,Now,PlotAction.Lethal));
            r.LearnIncident(i.Id,2,Now); Assert.NotNull(r.ProposeRevenge(i.Id,2,Now,PlotAction.Lethal));
            Assert.Null(r.ProposeRevenge(i.Id,2,Now+1,PlotAction.Fight));
        }
        [Theory] [InlineData(-0.5f, -0.2f, RecruitResponse.Join, PlotState.Ready)] [InlineData(0.8f,0f,RecruitResponse.Warn,PlotState.Exposed)] [InlineData(0.2f,0.8f,RecruitResponse.Refuse,PlotState.Abandoned)]
        public void Recruits_make_individual_decisions(float warmth,float steadiness,RecruitResponse response,PlotState state)
        {
            var r=World();var i=Wrong(r); var p=r.ProposeRevenge(i.Id,2,Now,PlotAction.Fight);
            Bond(r,1,2).Trust=60;
            Assert.Equal(response,r.AnswerRecruitment(p,1,warmth,steadiness,Now+1)); Assert.Equal(state,p.State);
        }
        [Fact] public void Friendship_with_target_overrides_loyalty_to_organizer()
        {var r=World();var i=Wrong(r);var p=r.ProposeRevenge(i.Id,2,Now,PlotAction.Lethal);Bond(r,1,11).Attachment=70;Bond(r,1,2).Trust=80;Assert.Equal(RecruitResponse.Warn,r.AnswerRecruitment(p,1,-1,-1,Now));}
        [Fact] public void Forgiveness_stops_plots_but_wrong_family_cannot_forgive()
        {var r=World();var i=Wrong(r);var p=r.ProposeRevenge(i.Id,2,Now,PlotAction.Fight);r.ForgiveIncident(i.Id,r.FamilyOf(11));Assert.False(i.Resolved);r.ForgiveIncident(i.Id,r.FamilyOf(1));Assert.True(i.Resolved);Assert.Equal(PlotState.Abandoned,p.State);}
        [Fact] public void Unrelated_death_is_not_a_successful_assassination()
        {var r=World();var i=Wrong(r);var p=r.ProposeRevenge(i.Id,2,Now,PlotAction.Lethal);p.State=PlotState.Attempted;r.GetPerson(11).Presence=Presence.Dead;r.MaintainStories(Now+1);Assert.Equal(PlotState.Abandoned,p.State);}
        [Fact] public void Ownership_survives_splitting_and_blocks_cross_family_merging()
        {var r=World();Supply(r);r.SplitStock(100,101);Assert.True(r.SameOwnership(100,101));Assert.False(r.SameOwnership(100,102));Assert.False(r.MayUseStock(101,r.FamilyOf(1)));Assert.True(r.MayUseStock(101,r.FamilyOf(11)));r.ForgetStock(100);Assert.Null(r.ClaimFor(100));Assert.NotNull(r.ClaimFor(101));}
        [Fact] public void Negotiation_does_not_create_goods_or_goodwill()
        {var r=World();var q=Supply(r);Assert.True(r.NegotiateSupply(q,1,0,false,Now));Assert.Empty(r.Incidents);Assert.Equal(r.FamilyOf(11),r.ClaimFor(100).HolderFamily);Assert.False(r.ExchangeSupply(q.Id,100,1,new[]{1,11},Now));Assert.Equal(SupplyState.Granted,q.State);Assert.True(r.ExchangeSupply(q.Id,100,2,new[]{1,11},Now));Assert.Equal(r.FamilyOf(1),r.ClaimFor(100).HolderFamily);Assert.Single(r.Incidents);Assert.False(r.ExchangeSupply(q.Id,100,2,new[]{1,11},Now));}
        [Fact] public void Public_claim_cannot_take_private_stock()
        {var r=World();Supply(r).State=SupplyState.Cancelled;var q=r.OpenSupplyRequest(1,0,100,7,2,SupplyKind.Food,SupplyApproach.PublicClaim,Now);q.State=SupplyState.Granted;Assert.False(r.ExchangeSupply(q.Id,100,2,new[]{1},Now));}
        [Fact] public void Relocation_preserves_ownership_without_a_new_social_incident()
        {var r=World();r.ClaimStock(100,r.FamilyOf(11),r.FamilyOf(1),7,SupplyKind.Food);var q=r.OpenSupplyRequest(1,0,100,7,2,SupplyKind.Food,SupplyApproach.Relocate,Now);q.State=SupplyState.Granted;Assert.True(r.ExchangeSupply(q.Id,100,2,new[]{1},Now));Assert.Equal(r.FamilyOf(11),r.ClaimFor(100).OwnerFamily);Assert.Empty(r.Incidents);}
        [Fact] public void Unseen_theft_preserves_owner_without_broadcasting_blame()
        {var r=World();var q=Supply(r,SupplyApproach.Steal);q.State=SupplyState.Granted;Assert.True(r.ExchangeSupply(q.Id,100,2,new[]{1},Now));Assert.Equal(r.FamilyOf(11),r.ClaimFor(100).OwnerFamily);Assert.Equal(r.FamilyOf(1),r.ClaimFor(100).HolderFamily);Assert.DoesNotContain(11,r.Incidents.Single().KnownBy);Assert.Empty(r.Ties);}
        [Fact] public void Loan_default_penalty_is_applied_once()
        {var r=World();var q=Supply(r,SupplyApproach.Loan);Bond(r,11,1).Trust=60;Assert.True(r.NegotiateSupply(q,1,0,false,Now));Assert.True(r.ExchangeSupply(q.Id,100,2,new[]{1,11},Now));r.MaintainSupplies(q.DueTick+1);Assert.Equal(SupplyState.Defaulted,q.State);float trust=r.GetBond(11,1).Trust;r.MaintainSupplies(q.DueTick+100);Assert.Equal(trust,r.GetBond(11,1).Trust);}
        [Fact] public void Restitution_requires_the_same_resource_and_full_quantity()
        {
            var r=World();var q=Supply(r,SupplyApproach.Steal);q.State=SupplyState.Granted;r.ExchangeSupply(q.Id,100,2,new[]{1,11},Now);q.State=SupplyState.Stored;
            var root=r.Incidents.Single();var back=r.OpenSupplyRequest(11,1,100,7,1,SupplyKind.Food,SupplyApproach.Return,Now+1);back.RootIncidentId=root.Id;back.Definition="OtherMeal";back.State=SupplyState.Granted;
            r.SplitStock(100,101);r.ExchangeSupply(back.Id,101,1,new[]{1,11},Now+1);Assert.False(root.Resolved);Assert.Equal(0,root.Restored);
        }
        [Fact] public void Refusal_for_essential_donor_needs_is_a_smaller_slight()
        {var r=World();var q=Supply(r);Assert.False(r.NegotiateSupply(q,1,1,false,Now));Assert.Equal(2,r.Incidents.Single().Severity);Assert.False(r.ExchangeSupply(q.Id,100,2,new[]{1},Now));}
        [Theory] [InlineData(SupplyApproach.Ask)] [InlineData(SupplyApproach.Steal)] [InlineData(SupplyApproach.Barter)]
        public void Only_an_explicit_matching_repayment_repays_a_loan(SupplyApproach approach)
        {
            var r=World();var loan=Supply(r,SupplyApproach.Loan);loan.State=SupplyState.Granted;
            r.ExchangeSupply(loan.Id,100,2,new[]{1,11},Now);loan.State=SupplyState.Stored;
            r.ClaimStock(200,r.FamilyOf(1),r.FamilyOf(1),7,SupplyKind.Food);
            var back=r.OpenSupplyRequest(11,1,200,7,3,SupplyKind.Food,approach,Now+1);
            back.Definition="MealFine";back.Units=2.7f;back.State=SupplyState.Granted;
            Assert.True(r.ExchangeSupply(back.Id,200,3,new[]{1,11},Now+1));
            Assert.False(loan.DebtPaid);Assert.Equal(SupplyState.Stored,loan.State);
        }
        [Fact] public void Two_matching_partial_returns_resolve_the_original_theft()
        {
            var r=World();var q=Supply(r,SupplyApproach.Steal);q.State=SupplyState.Granted;
            r.ExchangeSupply(q.Id,100,2,new[]{1,11},Now);q.State=SupplyState.Stored;var root=r.Incidents.Single();
            for(int n=0;n<2;n++)
            {
                int thing=200+n;r.ClaimStock(thing,r.FamilyOf(11),r.FamilyOf(1),7,SupplyKind.Food);
                var back=r.OpenSupplyRequest(11,1,thing,7,1,SupplyKind.Food,SupplyApproach.Return,Now+n+1);
                back.Definition="MealSimple";back.RootIncidentId=root.Id;back.State=SupplyState.Granted;
                Assert.True(r.ExchangeSupply(back.Id,thing,1,new[]{1,11},Now+n+1));
                Assert.Equal(n==1,root.Resolved);
            }
            Assert.Equal(2,root.Restored);
        }
        [Fact] public void A_planned_killing_by_the_conspirator_completes_the_plot()
        {
            var r=World();var root=Wrong(r);var plot=r.ProposeRevenge(root.Id,2,Now,PlotAction.Lethal);
            plot.State=PlotState.Attempted;r.RecordIncident(IncidentKind.Death,2,11,7,Now+1,100,new[]{2},IncidentContext.Planned);
            r.GetPerson(11).Presence=Presence.Dead;r.MaintainStories(Now+2);Assert.Equal(PlotState.Completed,plot.State);
        }
        private static PairContext Pair(FamilyRegistry r)
        {
            Actor A(int id)=>new Actor{Id=id,AgeYears=30,LineageId=r.FamilyOf(id),Profile=new float[DimInfo.Count],Skills=new Dictionary<string,int>{{"Social",8}}};
            return new PairContext{Registry=r,Initiator=A(1),Recipient=A(2),Now=Now,SameFamily=true,InitiatorBond=r.GetBond(1,2),RecipientBond=r.GetBond(2,1),TraditionId="reading"};
        }
        [Fact] public void Tradition_catalog_has_six_distinct_scenes_per_value()
        {Assert.Equal(48,TraditionMoments.All.Count);Assert.Equal(48,TraditionMoments.All.Select(d=>d.Id).Distinct().Count());Assert.All(TraditionMoments.All.GroupBy(d=>d.RequiredTradition),g=>Assert.Equal(6,g.Count()));Assert.All(TraditionMoments.All,d=>Assert.Equal(4,d.Outcomes.Length));Assert.Equal(3,TraditionMoments.All.Count(d=>(d.Effects&EventEffect.Promise)!=0));}
        [Fact] public void Scenes_require_the_matching_gathering_and_real_context()
        {var r=World();var c=Pair(r);var d=TraditionMoments.Get("T1-02");c.ResourcePressure=true;Assert.False(r.Explain(c,new[]{d},true)[0].Eligible);c.TraditionId="feast";Assert.True(r.Explain(c,new[]{d},true)[0].Eligible);c.ResourcePressure=false;Assert.False(r.Explain(c,new[]{d},true)[0].Eligible);}
        [Fact] public void Getting_acquainted_increases_familiarity_without_forcing_closeness()
        {var r=World();var c=Pair(r);float love=c.InitiatorBond.Attachment,familiar=c.InitiatorBond.Familiarity;r.ApplyEvent(new EventResult{Def=TraditionMoments.Get("T2-02"),Context=c,Tier=EventTier.Great},Now);Assert.Equal(love,r.GetBond(1,2).Attachment);Assert.True(r.GetBond(1,2).Familiarity>familiar);}
        [Fact] public void Admission_can_ease_tension_and_cost_recipient_trust()
        {var r=World();var c=Pair(r);c.RecipientBond.Trust=50;c.RecipientBond.Tension=30;r.ApplyEvent(new EventResult{Def=TraditionMoments.Get("T2-06"),Context=c,Tier=EventTier.Great},Now);Assert.Equal(48,c.RecipientBond.Trust);Assert.Equal(29,c.RecipientBond.Tension);}
        [Fact] public void Incident_history_is_bounded_and_active_causes_are_retained()
        {var r=World();var i=Wrong(r);var p=r.ProposeRevenge(i.Id,2,Now,PlotAction.Fight);for(int n=0;n<600;n++)r.RecordIncident(IncidentKind.Refusal,11,1,7,Now+n,2,new[]{1});Assert.Equal(512,r.Incidents.Count);Assert.Contains(i,r.Incidents);Assert.Contains(p,r.Plots);}
    }
}
