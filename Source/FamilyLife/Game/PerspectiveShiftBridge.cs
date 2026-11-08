using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Optional, reflection-only integration with Perspective Shift. There is no assembly dependency:
    /// when that mod is absent these methods reduce to the normal selected-pawn behavior.
    /// </summary>
    internal static class PerspectiveShiftBridge
    {
        private static Type stateType;
        private static Pawn avatar;
        private static bool installed;
        private static int lastRefreshTick=-60;

        internal static void Install(Harmony harmony)
        {
            try
            {
                stateType=AccessTools.TypeByName("PerspectiveShift.State");
                if(stateType==null)return;
                installed=true;
                var setter=stateType.GetMethods(BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                    .FirstOrDefault(m=>m.Name=="SetAvatar");
                if(setter!=null)harmony.Patch(setter,postfix:new HarmonyMethod(typeof(PerspectiveShiftBridge),nameof(AfterSetAvatar)));
                Refresh();
                Log.Message("[Wil's Family Life] Perspective Shift character integration enabled.");
            }
            catch(Exception e){installed=false;avatar=null;Log.Warning("[Wil's Family Life] Perspective Shift integration was unavailable: "+e.Message);}
        }

        internal static bool TrySync(FamilyComponent component)
        {
            if(!installed||component==null)return false;
            int now=Find.TickManager?.TicksGame??0;
            // SetAvatar normally keeps this current. The bounded fallback covers versions that expose
            // state but not that method, without reflecting every UI frame in Director mode.
            if(avatar?.Discarded==true||avatar==null&&now-lastRefreshTick>=60)Refresh();
            if(avatar==null)return false; // Perspective Shift's director mode has no singular character.
            component.PerspectivePawnId=avatar.thingIDNumber;
            component.PerspectiveInitialized=true;
            return true;
        }

        internal static bool IsAvatar(Pawn pawn)=>installed&&pawn!=null&&avatar==pawn;
        /// <summary>The person being played, or 0 when Perspective Shift is absent or has no single character. Read from a field: safe in hot paths.</summary>
        internal static int AvatarId=>installed&&avatar!=null&&!avatar.Discarded?avatar.thingIDNumber:0;
        internal static bool CanActAs(Pawn pawn)=>pawn!=null&&(!installed||avatar==null||avatar==pawn);

        private static void AfterSetAvatar(object[] __args)
        {
            avatar=__args?.Select(ExtractPawn).FirstOrDefault(p=>p!=null);
            if(avatar==null)Refresh();
            TrySync(FamilyComponent.Current);
        }

        private static void Refresh()
        {
            lastRefreshTick=Find.TickManager?.TicksGame??0;
            if(stateType==null){avatar=null;return;}
            avatar=stateType.GetFields(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
                .Where(f=>LooksRelevant(f.Name)).Select(f=>Safe(()=>f.GetValue(null))).Select(ExtractPawn).FirstOrDefault(p=>p!=null);
            if(avatar!=null)return;
            avatar=stateType.GetProperties(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic)
                .Where(p=>p.GetIndexParameters().Length==0&&LooksRelevant(p.Name)).Select(p=>Safe(()=>p.GetValue(null,null))).Select(ExtractPawn).FirstOrDefault(p=>p!=null);
        }

        private static Pawn ExtractPawn(object value)
        {
            if(value is Pawn pawn)return pawn;
            if(value==null)return null;
            var type=value.GetType();
            if(type.Namespace?.StartsWith("PerspectiveShift",StringComparison.Ordinal)!=true)return null;
            var field=type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(f=>typeof(Pawn).IsAssignableFrom(f.FieldType)&&LooksRelevant(f.Name));
            if(field!=null)return Safe(()=>field.GetValue(value)) as Pawn;
            var property=type.GetProperties(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
                .FirstOrDefault(p=>p.GetIndexParameters().Length==0&&typeof(Pawn).IsAssignableFrom(p.PropertyType)&&LooksRelevant(p.Name));
            return property==null?null:Safe(()=>property.GetValue(value,null)) as Pawn;
        }

        private static bool LooksRelevant(string name)
        {
            name=(name??"").ToLowerInvariant();
            return name.Contains("avatar")||name.Contains("character")||name.Contains("pawn");
        }

        private static object Safe(Func<object> read){try{return read();}catch{return null;}}
    }
}
