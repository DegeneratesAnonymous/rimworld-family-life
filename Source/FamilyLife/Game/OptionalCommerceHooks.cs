using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;
namespace FamilyLife
{
    /// <summary>Exact optional integration targets, resolved once. No assembly reference or late per-frame discovery.</summary>
    public static class OptionalCommerceHooks
    {
        private static readonly Dictionary<Type,PropertyInfo> registerLists=new Dictionary<Type,PropertyInfo>();
        public static readonly List<string> Status=new List<string>();
        public static void Install(Harmony harmony)
        {
            Bind(harmony,"Hospitality.Utilities.ItemUtility","IsBuyableNow",new[]{typeof(Pawn),typeof(Thing)},nameof(Purchase));
            Bind(harmony,"Hospitality.Trigger_TicksPassedAndOkayToLeave","ActivateOn",new[]{typeof(Lord),typeof(TriggerSignal)},nameof(Departure));
            Bind(harmony,"CashRegister.Building_CashRegister","IsAvailable",new[]{typeof(Pawn)},nameof(Staff));
            Bind(harmony,"CashRegister.Building_CashRegister","HasToWork",new[]{typeof(Pawn)},nameof(Staff));
            Bind(harmony,"Gastronomy.Restaurant.RestaurantController","HasToWork",new[]{typeof(Pawn)},nameof(Staff));
            Bind(harmony,"Gastronomy.Restaurant.RestaurantController","CanDineHere",new[]{typeof(Pawn)},nameof(Dining));
        }
        private static void Bind(Harmony harmony,string typeName,string name,Type[] parameters,string postfix)
        {
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(typeName,false)).FirstOrDefault(t=>t!=null);if(type==null)return;
            var method=type.GetMethod(name,BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static,null,parameters,null);
            if(method==null||method.ReturnType!=typeof(bool)){Status.Add(typeName+"."+name+": unsupported signature");Log.Warning("[Wil's Community Framework] Optional commerce target not supported: "+typeName+"."+name);return;}
            try
            {
                harmony.Patch(method,postfix:new HarmonyMethod(typeof(OptionalCommerceHooks),postfix));
                registerLists[type]=type.GetProperty("Registers",BindingFlags.Public|BindingFlags.Instance);
                Status.Add(typeName+"."+name+": attached");
            }
            catch(Exception ex){Status.Add(typeName+"."+name+": failed");Log.Warning("[Wil's Community Framework] Optional commerce adapter failed: "+ex.Message);}
        }
        private static bool Permitted(Pawn pawn,Thing context,string source)
        {
            foreach(var predicate in FamilyFramework.ExternalCommerceAllowed.Enabled())
                try{if(!predicate(pawn,context,source))return false;}catch(Exception ex){Log.Warning("[Wil's Community Framework] Commerce permission failed closed: "+ex.Message);return false;}
            return true;
        }
        private static void Purchase(object[] __args,ref bool __result)
        {
            if(__result)__result=Permitted(__args.OfType<Pawn>().FirstOrDefault(),__args.OfType<Thing>().FirstOrDefault(t=>!(t is Pawn)),"hospitality.purchase");
        }
        private static void Staff(object __instance,object[] __args,ref bool __result)
        {
            if(!__result)return;var pawn=__args.OfType<Pawn>().FirstOrDefault();
            if(__instance is Thing thing){__result=Permitted(pawn,thing,"register.service");return;}
            if(!Permitted(pawn,null,"gastronomy.staff")){__result=false;return;}
            if(registerLists.TryGetValue(__instance.GetType(),out var property)&&property?.GetValue(__instance,null) is IEnumerable registers)
                foreach(var register in registers)if(register is Thing counter&&!Permitted(pawn,counter,"register.service")){__result=false;return;}
        }
        private static void Dining(object __instance,object[] __args,ref bool __result)
        {
            if(!__result)return;var pawn=__args.OfType<Pawn>().FirstOrDefault();__result=Permitted(pawn,null,"gastronomy.purchase");
            if(__result&&registerLists.TryGetValue(__instance.GetType(),out var property)&&property?.GetValue(__instance,null) is IEnumerable registers)
                foreach(var register in registers)if(register is Thing counter&&!Permitted(pawn,counter,"gastronomy.purchase")){__result=false;return;}
        }
        private static void Departure(object[] __args,ref bool __result)
        {
            if(!__result)return;var lord=__args.OfType<Lord>().FirstOrDefault();if(lord==null)return;
            foreach(var predicate in FamilyFramework.TimedDepartureAllowed.Enabled())
                try{if(!predicate(lord)){__result=false;return;}}catch(Exception ex){Log.Warning("[Wil's Community Framework] Optional departure preference skipped: "+ex.Message);}
        }
    }
}
