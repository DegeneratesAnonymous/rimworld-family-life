using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class EstablishmentPersonalityTests
    {
        [Fact] public void Traits_combine_instead_of_selecting_one_owner_stereotype()
        {
            var kind=BusinessTemperament.FromTraits(new[]{"Kind"});var greedy=BusinessTemperament.FromTraits(new[]{"Greedy"});var mixed=BusinessTemperament.FromTraits(new[]{"Kind","Greedy"});
            Assert.True(kind.Warmth>50);Assert.True(greedy.Frugality>50);Assert.True(mixed.Warmth>50);Assert.True(mixed.Frugality>50);Assert.True(mixed.Frugality<greedy.Frugality);
        }
        [Theory][InlineData(-2,14)][InlineData(-1,32)][InlineData(1,68)][InlineData(2,86)]
        public void Industriousness_degrees_distinguish_laid_back_and_strict_workplaces(int degree,int expected)=>Assert.Equal(expected,BusinessTemperament.FromTraits(new string[0],degree).Discipline);
        [Fact] public void Missing_modded_traits_are_harmless_and_all_axes_stay_bounded()
        {
            Assert.Equal(new[]{50,50,50,50,50},BusinessTemperament.FromTraits(new[]{"UninstalledTrait"}).Values);
            Assert.All(BusinessTemperament.FromTraits(new[]{"Kind","Abrasive","Psychopath","Greedy","Ascetic","Gourmand","Brawler","ChemicalFascination"},99).Values,v=>Assert.InRange(v,0,100));
        }
        [Fact] public void Culture_evolves_once_per_day_and_has_no_load_catchup_jump()
        {
            var v=new VenuePersonality();var owner=BusinessTemperament.FromTraits(new[]{"Kind"});v.Develop(owner,new BusinessTemperament[0],new BusinessTemperament[0],100,false);Assert.Equal(53,v.Culture.Warmth);
            v.Develop(owner,new BusinessTemperament[0],new BusinessTemperament[0],100,false);Assert.Equal(53,v.Culture.Warmth);v.Develop(owner,new BusinessTemperament[0],new BusinessTemperament[0],1000,false);Assert.Equal(56,v.Culture.Warmth);
        }
        [Fact] public void Staff_and_actual_patrons_shape_community_owned_venues()
        {
            var a=new VenuePersonality();var b=new VenuePersonality();
            for(int d=0;d<12;d++){a.Develop(null,new[]{BusinessTemperament.FromTraits(new[]{"Kind"})},new[]{BusinessTemperament.FromTraits(new[]{"Ascetic"})},d,true);b.Develop(null,new[]{BusinessTemperament.FromTraits(new[]{"Abrasive"})},new[]{BusinessTemperament.FromTraits(new[]{"Brawler"})},d,false);}
            Assert.True(a.Culture.Warmth>b.Culture.Warmth);Assert.True(a.Culture.Revelry<b.Culture.Revelry);Assert.NotEqual(a.Description,b.Description);
        }
        [Fact] public void A_new_owner_does_not_instantly_erase_the_existing_character()
        {
            var v=new VenuePersonality();for(int d=0;d<12;d++)v.Develop(BusinessTemperament.FromTraits(new[]{"Kind"}),new BusinessTemperament[0],new BusinessTemperament[0],d,false);
            int before=v.Culture.Warmth;v.Develop(BusinessTemperament.FromTraits(new[]{"Abrasive"}),new BusinessTemperament[0],new BusinessTemperament[0],12,false);Assert.Equal(before-3,v.Culture.Warmth);
        }
        [Fact] public void Low_mood_changes_owner_responses_but_never_changes_stable_trait_values()
        {
            var c=new OwnerSituation{Owner=BusinessTemperament.FromTraits(new[]{"Abrasive"}),Backlog=true,Mood=.9f};var original=c.Owner.Values;float calm=OwnerDecisions.Weights(c)[OwnerApproach.Criticize];c.Mood=.1f;Assert.True(OwnerDecisions.Weights(c)[OwnerApproach.Criticize]>calm);Assert.Equal(original,c.Owner.Values);
        }
        [Fact] public void Criticism_needs_an_actual_problem_and_bonuses_need_recorded_work_and_funding()
        {
            var c=new OwnerSituation{Mood=0};Assert.DoesNotContain(OwnerApproach.Criticize,OwnerDecisions.Weights(c).Keys);Assert.DoesNotContain(OwnerApproach.Bonus,OwnerDecisions.Weights(c).Keys);
            c.Productive=true;c.CanBonus=true;Assert.Contains(OwnerApproach.Bonus,OwnerDecisions.Weights(c).Keys);c.CanBonus=false;Assert.DoesNotContain(OwnerApproach.Bonus,OwnerDecisions.Weights(c).Keys);
        }
        [Fact] public void Equal_treatment_policy_blocks_favoritism_even_for_a_favorite_relative()
        {
            var c=new OwnerSituation{Relative=true,Productive=true,OtherStaffPresent=true,Opinion=100};Assert.Contains(OwnerApproach.FavorRelative,OwnerDecisions.Weights(c).Keys);c.EqualTreatment=true;Assert.DoesNotContain(OwnerApproach.FavorRelative,OwnerDecisions.Weights(c).Keys);
        }
        [Fact] public void Kindness_favors_support_and_greed_favors_profit_discussion()
        {
            var kind=new OwnerSituation{Owner=BusinessTemperament.FromTraits(new[]{"Kind"}),EmployeeDistressed=true,Profitable=true};var greedy=new OwnerSituation{Owner=BusinessTemperament.FromTraits(new[]{"Greedy"}),EmployeeDistressed=true,Profitable=true};
            Assert.True(OwnerDecisions.Weights(kind)[OwnerApproach.Support]>OwnerDecisions.Weights(greedy)[OwnerApproach.Support]);Assert.True(OwnerDecisions.Weights(greedy)[OwnerApproach.DiscussMargins]>OwnerDecisions.Weights(kind)[OwnerApproach.DiscussMargins]);
        }
        [Fact] public void Empty_context_cannot_manufacture_an_owner_incident()=>Assert.Null(OwnerDecisions.Choose(new OwnerSituation(),.5f));
        [Fact] public void Preferences_change_appeal_without_overriding_affordability()
        {
            var e=new Establishment();e.Personality.Culture=BusinessTemperament.FromTraits(new[]{"Kind","Ascetic"});var match=new VisitorPreference{Taste=BusinessTemperament.FromTraits(new[]{"Kind","Ascetic"}),Budget=0,NightPrice=10};var mismatch=new VisitorPreference{Taste=BusinessTemperament.FromTraits(new[]{"Abrasive","Brawler","Greedy"})};Assert.True(match.Appeal(e)>mismatch.Appeal(e));Assert.False(match.CanExtend);
        }
        [Fact] public void Personality_policy_climate_and_cooldowns_roundtrip_with_establishments()
        {
            var book=new EstablishmentBook();var e=book.Create(1,EstablishmentKind.Tavern);e.Personality.Culture=BusinessTemperament.FromTraits(new[]{"Brawler","Greedy"});e.Personality.LastOwnerDay=12;e.Personality.LastCultureDay=12;e.Personality.OwnerAutonomy=false;e.Personality.AllowBonuses=false;e.Personality.EqualTreatment=true;e.Personality.StaffClimate=31;e.Personality.LastReason="An owner conversation | with a worker, after service.";
            var copy=EstablishmentBook.Decode(new StateReader(book.Encode()));Assert.Equal(book.Encode(),copy.Encode());Assert.False(copy.Businesses[0].Personality.OwnerAutonomy);
        }
        [Fact] public void Older_saves_get_neutral_character_and_safe_controls()
        {
            var old=VenuePersonality.Decode(new StateReader(""));Assert.Equal(50,old.Culture.Warmth);Assert.True(old.OwnerAutonomy);Assert.True(old.AllowBonuses);Assert.Equal(-1,old.LastOwnerDay);
        }
        [Fact] public void Owner_conversations_have_real_mood_and_relationship_definitions()
        {
            foreach(string fact in new[]{"OwnerPraise","OwnerSupport","OwnerCriticism","OwnerMargins","OwnerBonus","OwnerFamilyPraise","OwnerFavorWitness","OwnerCelebration"}){var e=HospitalityEvents.All.Single(e=>e.Fact==fact);Assert.NotEqual(0,e.Mood);Assert.NotEqual(0,e.Opinion);}
        }
        [Fact] public void Discretionary_pay_uses_the_existing_payday_book_and_preserves_prior_earnings()
        {
            var book=new EstablishmentBook();var e=book.Create(1,EstablishmentKind.Inn);book.Earn(e,7,4,BusinessCalendar.Next(1));book.Earn(e,7,1,BusinessCalendar.Next(1));Assert.Equal(5,book.Earnings.Single().Silver);Assert.Equal(7,book.Earnings.Single().DueDay);Assert.Empty(e.Points);
        }
        [Theory][InlineData(-1,25)][InlineData(1,62)][InlineData(2,74)]
        public void Vanilla_drug_desire_degrees_shape_venue_preferences(int degree,int expected)=>Assert.Equal(expected,BusinessTemperament.FromTraits(new[]{"DrugDesire"},0,degree).Revelry);
    }
}
