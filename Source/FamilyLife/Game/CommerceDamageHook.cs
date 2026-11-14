using System;
using HarmonyLib;
using Verse;
namespace FamilyLife
{
    /// <summary>A read-only snapshot of real damage, including position before destruction.</summary>
    public sealed class PropertyDamageReport
    {
        public Thing Target;public Pawn Actor;public Map Map;public IntVec3 Cell;public int Before,Lost;
    }
    [HarmonyPatch(typeof(Thing),nameof(Thing.TakeDamage))]
    internal static class Hook_CommerceDamage
    {
        private static void Prefix(Thing __instance,DamageInfo dinfo,out PropertyDamageReport __state)
        {
            __state=null;
            if(__instance.Spawned&&__instance.def.useHitPoints&&!(__instance is Pawn))__state=new PropertyDamageReport{Target=__instance,Actor=dinfo.Instigator as Pawn,Map=__instance.Map,Cell=__instance.Position,Before=__instance.HitPoints};
        }
        private static void Postfix(Thing __instance,PropertyDamageReport __state)
        {
            if(__state==null)return;__state.Lost=__instance.Destroyed?__state.Before:Math.Max(0,__state.Before-__instance.HitPoints);if(__state.Lost<=0)return;
            foreach(var observer in FamilyFramework.PropertyDamaged.Enabled())try{observer(__state);}catch(Exception ex){HookSafety.Report("property damage observer",ex);}
        }
    }
}
