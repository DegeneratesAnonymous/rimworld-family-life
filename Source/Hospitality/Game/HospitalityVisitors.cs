using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace FamilyLife
{
    /// <summary>Purposeful outside customers have finite physical silver, a saved stay and one group departure decision. Existing caravans are never detached from their lord.</summary>
    public static class HospitalityVisitors
    {
        private static EstablishmentBook Book=>HospitalityService.Book;
        public static void Tick()
        {
            if(Book==null)return;
            if(FamilyFramework.Commerce.Bank==null){ReleaseGenerated();return;}
            if(HospitalityService.Day>=Book.NextVisitorDay)
            {
                Book.NextVisitorDay=HospitalityService.Day+Rand.RangeInclusive(2,5);
                var options=Book.Businesses.Where(e=>e.Enabled&&e.Visitors&&e.Reputation>=25&&HospitalityService.Map(e)!=null&&HospitalityService.Thing(e.Counter)!=null&&e.ServiceCells.Count>0).ToList();
                if(options.Count>0&&Book.Guests.Count(g=>g.Generated&&!g.Leaving)<12)
                {
                    var chosen=options.RandomElement();if(Rand.Chance(chosen.Reputation/100f))Arrive(chosen);
                }
            }
            foreach(var stay in Book.Guests.ToList())
            {
                var p=HospitalityService.Pawn(stay.Pawn);var e=Book.ById(stay.Business);
                if(p==null||!p.Spawned||p.Dead){Book.Guests.Remove(stay);continue;}
                if(stay.Leaving)continue;
                if(e==null||!e.Enabled||PawnReadinessApi.Danger(p.Map)||p.Downed||p.HostileTo(Faction.OfPlayer)){LeaveGroup(stay.Group);continue;}
                var bed=HospitalityService.Thing(stay.Bed) as Building_Bed;
                if(stay.Bed>0&&(bed==null||!bed.Spawned||bed.Map!=p.Map)){stay.Bed=0;HospitalityService.Event(p,null,"LodgingFailed");}
                if(bed!=null&&p.needs.rest!=null&&p.needs.rest.CurLevelPercentage<.3f&&!p.InMentalState&&!p.Downed&&p.CurJob?.def!=JobDefOf.LayDown)
                    p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.LayDown,bed));
                if(bed!=null&&p.CurJob?.def==JobDefOf.LayDown&&p.CurJob.targetA.Thing==bed)
                    stay.SleepTicks+=600;
                if(stay.SleepTicks>=7500&&stay.LastLodgingDay<HospitalityService.Day&&GenLocalDate.HourOfDay(p)>=8)
                {
                    var bank=FamilyFramework.Commerce.Bank;
                    string key="lodging:"+stay.Pawn+":"+stay.ArrivalDay+":"+HospitalityService.Day;
                    if(bank.ExternalSale(key,bank.Business(e.Id),e.RoomPrice,HospitalityService.Day,()=>HospitalityService.MoveCash(p,e.RoomPrice,e)))
                    {
                        Book.Sale(e,e.RoomPrice);stay.LastLodgingDay=HospitalityService.Day;stay.SleepTicks=0;
                        var host=HospitalityService.Coverage(e).FirstOrDefault();if(host!=null){Book.Credit(e,host.thingIDNumber,ContributionKind.Service,key,HospitalityService.Day);HospitalityService.Event(host,p,"CheckIn");}
                    }
                    else {HospitalityService.Event(p,null,"LodgingFailed");LeaveGroup(stay.Group);}
                }
            }
            foreach(var group in Book.Guests.Where(g=>!g.Leaving).GroupBy(g=>g.Group).ToList())
            {
                var leader=group.First();var e=Book.ById(leader.Business);if(e==null)continue;
                if(group.All(g=>HospitalityService.Day<g.UntilDay)||leader.LastDecisionDay==HospitalityService.Day||GenLocalDate.HourOfDay(HospitalityService.Map(e))<8)continue;
                var members=group.Select(g=>HospitalityService.Pawn(g.Pawn)).ToList();
                var preferences=group.Select(g=>Preference(g,e)).ToList();foreach(var g in group)g.LastDecisionDay=HospitalityService.Day;
                if(e.Kind==EstablishmentKind.Inn&&VisitorDecisions.ExtendGroup(e,preferences,Rand.Value))
                {
                    foreach(var g in group){g.UntilDay++;g.Extensions++;}Book.Note(e.Name+": a visiting group extended its stay; atmosphere, experience and journey reserves permitted it.");
                }
                else LeaveGroup(leader.Group);
            }
        }
        public static bool AllowDeparture(Lord lord)
        {
            if(Book==null||FamilyFramework.Commerce.Bank==null)return true;
            var members=lord.ownedPawns.Where(p=>p.RaceProps.Humanlike&&!p.Dead).ToList();if(members.Count==0||members.Any(p=>!p.Spawned||p.Downed||p.InMentalState||p.HostileTo(Faction.OfPlayer)||PawnReadinessApi.Danger(p.Map)))return true;
            int group=-lord.loadID;var existing=Book.Guests.Where(g=>g.Group==group).ToList();
            if(existing.Count>0)
            {
                if(existing.Any(g=>g.Leaving))return true;
                return false; // the saved group decision in Tick owns the extension until it explicitly ends
            }
            string decision="caravan-extension:"+lord.loadID;if(!Book.Once(decision,HospitalityService.Day))return true;
            var e=Book.Businesses.Where(b=>b.Kind==EstablishmentKind.Inn&&b.Enabled&&b.Visitors&&b.Map==members[0].Map.uniqueID&&b.ServiceCells.Count>0).OrderByDescending(b=>b.Reputation).FirstOrDefault();if(e==null)return true;
            // Members must actually have visited or be close enough to know the place.
            if(!members.Any(p=>Book.Seen.ContainsKey("regular:"+e.Id+":"+p.thingIDNumber)||HospitalityService.Thing(e.Counter)!=null&&p.Position.DistanceTo(HospitalityService.Thing(e.Counter).Position)<20))return true;
            var beds=e.Beds.Select(HospitalityService.Thing).OfType<Building_Bed>().Where(b=>b.Spawned&&!b.ForPrisoners&&!b.OwnersForReading.Any()&&!Book.Guests.Any(g=>g.Bed==b.thingIDNumber&&!g.Leaving)).ToList();if(beds.Count<members.Count)return true;
            var stays=members.Select((p,i)=>new GuestStay{Pawn=p.thingIDNumber,Business=e.Id,Group=group,ArrivalDay=HospitalityService.Day,UntilDay=HospitalityService.Day+1,Bed=beds[i].thingIDNumber,Extensions=1,LastDecisionDay=HospitalityService.Day,Generated=false}).ToList();
            if(!VisitorDecisions.ExtendGroup(e,stays.Select(g=>Preference(g,e)).ToList(),Rand.Value))return true;
            Book.Guests.AddRange(stays);Book.Note(e.Name+": a caravan unanimously opted to stay another night; its original lord and emergency departures remain intact.");return false;
        }
        public static int JourneyReserve(Pawn p)
        {
            var stay=Book?.Guests.FirstOrDefault(g=>g.Pawn==p.thingIDNumber&&!g.Leaving);var e=stay==null?null:Book.ById(stay.Business);return 10+(e!=null&&stay.Bed>0&&stay.LastLodgingDay<HospitalityService.Day?e.RoomPrice:0);
        }
        private static VisitorPreference Preference(GuestStay g,Establishment e)
        {
            var p=HospitalityService.Pawn(g.Pawn);int connections=0;
            if(p!=null&&HospitalityService.Map(e)!=null)connections=HospitalityService.Map(e).mapPawns.FreeColonistsSpawned.Count(other=>p.relations.OpinionOf(other)>30);
            return new VisitorPreference{Taste=EstablishmentPersonalityService.Temperament(p),Sociable=HospitalityService.Trait(p,"Kind")||HospitalityService.Trait(p,"Abrasive"),Quiet=HospitalityService.Trait(p,"Ascetic"),Tough=HospitalityService.Trait(p,"Tough")||HospitalityService.Trait(p,"Brawler"),Refined=HospitalityService.Trait(p,"Greedy"),Thrifty=HospitalityService.Trait(p,"Ascetic"),Budget=HospitalityService.Cash(p),NightPrice=e.RoomPrice,FoodReserve=15,TravelReserve=15,DaysStayed=HospitalityService.Day-g.ArrivalDay,MaxDays=e.MaxStay,Experience=g.PersonalExperience,Connection=Math.Min(.5f,connections*.1f),Urgent=p==null||p.HostileTo(Faction.OfPlayer),Injured=p==null||p.Downed};
        }
        private static void Arrive(Establishment e)
        {
            var map=HospitalityService.Map(e);if(map==null||PawnReadinessApi.Danger(map))return;
            var factions=Find.FactionManager.AllFactionsVisible.Where(f=>!f.IsPlayer&&!f.HostileTo(Faction.OfPlayer)&&f.def.humanlikeFaction).ToList();if(factions.Count==0)return;
            if(!CellFinder.TryFindRandomEdgeCellWith(c=>c.Standable(map)&&map.reachability.CanReachColony(c),map,CellFinder.EdgeRoadChance_Neutral,out var entry))return;
            var faction=factions.RandomElement();var pawns=new List<Pawn>();
            var beds=e.Beds.Select(HospitalityService.Thing).OfType<Building_Bed>().Where(b=>b.Spawned&&!b.ForPrisoners&&!b.OwnersForReading.Any()&&!Book.Guests.Any(g=>g.Bed==b.thingIDNumber&&!g.Leaving)).ToList();
            if(e.Kind==EstablishmentKind.Inn&&beds.Count==0)return;
            int count=e.Kind==EstablishmentKind.Inn?Math.Min(beds.Count,Rand.RangeInclusive(1,3)):Rand.RangeInclusive(1,2);
            for(int i=0;i<count;i++)
            {
                var p=PawnGenerator.GeneratePawn(PawnKindDefOf.Villager,faction);GenSpawn.Spawn(p,CellFinder.RandomClosewalkCellNear(entry,map,4),map);
                var money=ThingMaker.MakeThing(ThingDefOf.Silver);money.stackCount=Rand.RangeInclusive(60,140);p.inventory.innerContainer.TryAdd(money);
                pawns.Add(p);
            }
            var counter=HospitalityService.Thing(e.Counter);var lord=LordMaker.MakeNewLord(faction,new LordJob_EstablishmentVisit(counter.Position),map,pawns);
            int group=pawns[0].thingIDNumber;
            for(int i=0;i<pawns.Count;i++)Book.Guests.Add(new GuestStay{Pawn=pawns[i].thingIDNumber,Business=e.Id,Group=group,ArrivalDay=HospitalityService.Day,UntilDay=HospitalityService.Day+1,Bed=e.Kind==EstablishmentKind.Inn?beds[i].thingIDNumber:0,Generated=true});
            Book.Note(e.Name+": "+count+" visitors arrived intending to "+(e.Kind==EstablishmentKind.Inn?"stay and visit local businesses":"shop and visit")+".");
        }
        private static void LeaveGroup(int group)
        {
            foreach(var stay in Book.Guests.Where(g=>g.Group==group))
            {
                stay.Leaving=true;var p=HospitalityService.Pawn(stay.Pawn);if(p==null||!p.Spawned)continue;
                // Only our own lord owns this departure policy. Never commandeer a trade caravan or Hospitality group.
                var lord=p.GetLord();if(lord?.LordJob is LordJob_EstablishmentVisit)lord.ReceiveMemo("WFL_H_Leave");
            }
        }
        public static void ReleaseGenerated(){if(Book==null)return;foreach(var group in Book.Guests.Where(g=>g.Generated).Select(g=>g.Group).Distinct().ToList())LeaveGroup(group);}
    }
    public sealed class LordJob_EstablishmentVisit : LordJob
    {
        private IntVec3 center;
        public LordJob_EstablishmentVisit(){}
        public LordJob_EstablishmentVisit(IntVec3 center){this.center=center;}
        public override void ExposeData(){Scribe_Values.Look(ref center,"center");}
        public override StateGraph CreateGraph()
        {
            var graph=new StateGraph();var visit=new LordToil_DefendPoint(center,20f,15f);var exit=new LordToil_ExitMap(LocomotionUrgency.Walk,false,true);
            graph.AddToil(visit);graph.AddToil(exit);var transition=new Transition(visit,exit);transition.AddTrigger(new Trigger_Memo("WFL_H_Leave"));graph.AddTransition(transition);return graph;
        }
    }
}
