using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI.Group;
namespace FamilyLife
{
    [StaticConstructorOnStartup]
    public static class HospitalityInterop
    {
        private static readonly Type nativeLord=Resolve("Hospitality.LordJob_VisitColony"),guestComp=Resolve("Hospitality.CompGuest"),cashRegister=Resolve("CashRegister.Building_CashRegister"),restaurantUtility=Resolve("Gastronomy.Restaurant.RestaurantUtility");
        private static readonly MethodInfo dining=restaurantUtility?.GetMethods().FirstOrDefault(m=>m.Name=="GetRestaurantDining"&&m.GetParameters().Length==1&&m.GetParameters()[0].ParameterType==typeof(Pawn));
        private static readonly MethodInfo queued=restaurantUtility?.GetMethods().FirstOrDefault(m=>m.Name=="HasDiningQueued"&&m.GetParameters().Length==1);
        private static readonly MethodInfo restaurant=restaurantUtility?.GetMethods().FirstOrDefault(m=>m.Name=="GetRestaurant"&&m.GetParameters().Length==1);
        private static readonly Type controller=Resolve("Gastronomy.Restaurant.RestaurantController"),orderList=Resolve("Gastronomy.Restaurant.RestaurantOrders");
        private static readonly FieldInfo nativeOpen=controller?.GetField("openForBusiness");
        private static readonly PropertyInfo nativeOrders=controller?.GetProperty("Orders"),nativePatrons=controller?.GetProperty("Patrons"),allOrders=orderList?.GetProperty("AllOrders");
        private static readonly FieldInfo sentAway=guestComp?.GetField("sentAway"),leaving=nativeLord?.GetField("leaving");
        private static readonly PropertyInfo hasBed=guestComp?.GetProperty("HasBed");
        private static Type Resolve(string name)=>AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name,false)).FirstOrDefault(t=>t!=null);
        public static bool IsVisitorLord(Lord lord)=>lord?.LordJob is LordJob_EstablishmentVisit||lord?.LordJob is LordJob_TradeWithColony||lord?.LordJob is RimWorld.LordJob_VisitColony||nativeLord?.IsInstanceOfType(lord?.LordJob)==true;
        private static object Comp(Pawn p)=>guestComp==null?null:p?.AllComps.FirstOrDefault(c=>guestComp.IsInstanceOfType(c));
        public static bool MayExtend(Lord lord)
        {
            if(nativeLord?.IsInstanceOfType(lord?.LordJob)!=true)return true;
            if(leaving==null||sentAway==null||hasBed==null||!(leaving.GetValue(lord.LordJob) is bool exit)||exit)return false;
            return !lord.ownedPawns.Any(p=>Comp(p) is object c&&sentAway?.GetValue(c) is bool sent&&sent);
        }
        public static bool NativeBed(Pawn p)=>Comp(p) is object c&&hasBed?.GetValue(c,null) is bool owns&&owns;
        public static bool IsRegister(Thing t)=>cashRegister?.IsInstanceOfType(t)==true;
        public static bool CanUseCounter(Thing t)
        {
            if(!IsRegister(t))return true;if(restaurant==null)return controller==null;
            try
            {
                var r=restaurant.Invoke(null,new object[]{t});if(r==null)return true;
                if(!(nativeOpen?.GetValue(r) is bool open)||open)return false;
                if(!(nativePatrons?.GetValue(r,null) is IEnumerable patrons)||patrons.Cast<object>().Any())return false;
                var orders=nativeOrders?.GetValue(r,null);if(orders==null||!(allOrders?.GetValue(orders,null) is IEnumerable pending)||pending.Cast<object>().Any())return false;
                // A queued native diner must finish its handoff before the till changes systems.
                return queued==null||!t.Map.mapPawns.AllPawnsSpawned.Any(p=>queued.Invoke(null,new object[]{p}) is bool waiting&&waiting);
            }
            catch{return false;}
        }
        public static bool Busy(Pawn pawn)
        {
            if(pawn==null||controller!=null&&(dining==null||queued==null))return true;string driver=pawn.CurJob?.def?.driverClass?.FullName??"";
            if(driver=="Hospitality.JobDriver_BuyItem"||driver.StartsWith("Gastronomy.",StringComparison.Ordinal)||driver.StartsWith("CashRegister.",StringComparison.Ordinal))return true;
            try{return dining?.Invoke(null,new object[]{pawn})!=null||queued?.Invoke(null,new object[]{pawn}) is bool q&&q;}
            catch{return true;}
        }
        public static bool Permit(Pawn pawn,Thing context,string source)
        {
            var book=HospitalityService.Book;if(book==null)return true;
            if(context!=null&&book.Businesses.Any(e=>e.Counter==context.thingIDNumber||HospitalityService.InBusiness(context,e)))return false;
            if(pawn!=null&&book.Orders.Any(o=>o.Customer==pawn.thingIDNumber&&o.Stage<ServiceStage.Settled))return false;
            if(source.EndsWith(".purchase",StringComparison.Ordinal)&&pawn!=null&&HospitalityService.Component.Registry.VisitorWallets().Held(pawn.thingIDNumber)>0)return false;
            if(!source.EndsWith(".purchase",StringComparison.Ordinal)&&HospitalityService.Employer(pawn)!=null)return false;
            return true;
        }
    }
}
