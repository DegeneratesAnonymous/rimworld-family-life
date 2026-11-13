using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI.Group;
namespace FamilyLife
{
    public static class VisitorCommerce
    {
        private static VisitorWalletBook Book=>HospitalityService.Component?.Registry.VisitorWallets();
        public static int InventoryCash(Pawn p)=>p?.inventory?.innerContainer?.Where(t=>t.def==ThingDefOf.Silver).Sum(t=>t.stackCount)??0;
        public static List<Pawn> Payers(Pawn customer)
        {
            if(customer==null)return new List<Pawn>();var lord=customer.GetLord();
            if(customer.Faction==Faction.OfPlayer||lord==null||!HospitalityInterop.IsVisitorLord(lord))return new List<Pawn>{customer};
            return new[]{customer}.Concat(lord.ownedPawns.Where(p=>p!=customer&&p.Faction==customer.Faction&&!p.Dead&&p.Spawned&&p.Map==customer.Map)).Where(p=>p?.inventory!=null).Distinct().ToList();
        }
        public static IEnumerable<WalletMember> Members(Pawn p)=>Payers(p).Select(member=>new WalletMember(member.thingIDNumber,InventoryCash(member)));
        public static int Available(Pawn p)=>Book?.Available(Members(p))??InventoryCash(p);
        public static int Humans(Pawn p)=>Math.Max(1,Payers(p).Count(member=>member.RaceProps.Humanlike));
        public static bool Reserve(ServiceOrder order,Pawn customer)=>Book!=null&&Book.Reserve(order.Key,customer.thingIDNumber,Members(customer),order.Price);
        public static void Release(string key)=>Book?.Release(key);
        public static bool Move(Pawn customer,int amount,Establishment establishment,string reservationKey=null)
        {
            var counter=HospitalityService.Thing(establishment.Counter);var map=HospitalityService.Map(establishment);
            if(customer==null||!customer.Spawned||counter==null||!counter.Spawned||map==null||customer.Map!=map||amount<=0||Book==null)return false;
            var cell=CellFinder.StandableCellNear(counter.Position,map,4);if(!cell.IsValid)return false;
            string key=reservationKey??("visitor-immediate:"+customer.thingIDNumber+":"+HospitalityService.Now+":"+amount);
            var reservation=Book.Find(key);
            if(reservation==null&&!Book.Reserve(key,customer.thingIDNumber,Members(customer),amount))return false;
            reservation=Book.Find(key);
            if(reservation.Total!=amount||reservation.Customer!=customer.thingIDNumber)return false;
            bool funded=reservation.Pledges.All(p=>HospitalityService.Pawn(p.Pawn) is Pawn payer&&payer.Spawned&&payer.Map==map&&payer.Faction==customer.Faction&&InventoryCash(payer)>=p.Silver);
            if(!funded)
            {
                if(!Book.Replan(key,customer.thingIDNumber,Members(customer),amount))return false;reservation=Book.Find(key);
            }
            // Validate every contributor before changing any inventory. Physical delivery and account credit happen in the same game-thread callback.
            foreach(var pledge in reservation.Pledges)
            {
                var payer=HospitalityService.Pawn(pledge.Pawn);int left=pledge.Silver;
                foreach(var stack in payer.inventory.innerContainer.Where(t=>t.def==ThingDefOf.Silver).ToList()){int take=Math.Min(left,stack.stackCount);if(take<=0)break;var part=stack.SplitOff(take);payer.inventory.innerContainer.Remove(part);part.Destroy();left-=take;}
            }
            var payment=ThingMaker.MakeThing(ThingDefOf.Silver);payment.stackCount=amount;GenSpawn.Spawn(payment,cell,map);Book.Release(key);return true;
        }
        public static void Reconcile()
        {
            if(Book==null)return;foreach(var hold in Book.Reservations.ToList())if(!HospitalityService.Book.Orders.Any(o=>o.Key==hold.Key&&o.Visitor&&o.Stage<ServiceStage.Settled))Book.Release(hold.Key);
        }
    }
}
