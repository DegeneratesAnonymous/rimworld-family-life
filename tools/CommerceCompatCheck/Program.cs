using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// Usage: CommerceCompatCheck <game ref directory> <Hospitality assemblies> <CashRegister assemblies> <Gastronomy assemblies>
// Optional mods are inspected as metadata only; nothing from them is executed or shipped.
internal static class Program
{
    private static int Main(string[] args)
    {
        if(args.Length!=4){Console.Error.WriteLine("usage: CommerceCompatCheck <game refs> <Hospitality assemblies> <CashRegister assemblies> <Gastronomy assemblies>");return 2;}
        var paths=args.SelectMany(p=>Directory.GetFiles(p,"*.dll")).GroupBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList();
        using var context=new MetadataLoadContext(new PathAssemblyResolver(paths),"mscorlib");
        var assemblies=new Dictionary<string,Assembly>();foreach(var name in new[]{"Hospitality","CashRegister","Gastronomy"})assemblies[name]=context.LoadFromAssemblyPath(paths.Single(p=>Path.GetFileNameWithoutExtension(p)==name));
        int errors=0;
        void Method(string assembly,string type,string name,bool isStatic,string returns,params string[] parameters)
        {
            var target=assemblies[assembly].GetType(type);var method=target?.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static|BindingFlags.Instance).FirstOrDefault(m=>m.Name==name&&m.IsStatic==isStatic&&m.GetParameters().Select(p=>p.ParameterType.FullName).SequenceEqual(parameters));
            bool ok=method!=null&&method.ReturnType.FullName==returns;Console.WriteLine((ok?"ok   ":"FAIL ")+type+"."+name);if(!ok)errors++;
        }
        void Member(string assembly,string type,string name,bool property,string memberType)
        {
            var target=assemblies[assembly].GetType(type);var actual=property?target?.GetProperty(name)?.PropertyType:target?.GetField(name)?.FieldType;bool ok=actual?.FullName==memberType;Console.WriteLine((ok?"ok   ":"FAIL ")+type+"."+name);if(!ok)errors++;
        }
        Method("Hospitality","Hospitality.Utilities.ItemUtility","IsBuyableNow",true,"System.Boolean","Verse.Pawn","Verse.Thing");
        Method("Hospitality","Hospitality.Trigger_TicksPassedAndOkayToLeave","ActivateOn",false,"System.Boolean","Verse.AI.Group.Lord","Verse.AI.Group.TriggerSignal");
        Method("CashRegister","CashRegister.Building_CashRegister","IsAvailable",false,"System.Boolean","Verse.Pawn");
        Method("CashRegister","CashRegister.Building_CashRegister","HasToWork",false,"System.Boolean","Verse.Pawn");
        Method("Gastronomy","Gastronomy.Restaurant.RestaurantController","HasToWork",false,"System.Boolean","Verse.Pawn");
        Method("Gastronomy","Gastronomy.Restaurant.RestaurantController","CanDineHere",false,"System.Boolean","Verse.Pawn");
        Method("Gastronomy","Gastronomy.Restaurant.RestaurantUtility","GetRestaurantDining",true,"Gastronomy.Restaurant.RestaurantController","Verse.Pawn");
        Method("Gastronomy","Gastronomy.Restaurant.RestaurantUtility","HasDiningQueued",true,"System.Boolean","Verse.Pawn");
        Method("Gastronomy","Gastronomy.Restaurant.RestaurantUtility","GetRestaurant",true,"Gastronomy.Restaurant.RestaurantController","CashRegister.Building_CashRegister");
        Member("Hospitality","Hospitality.CompGuest","sentAway",false,"System.Boolean");
        Member("Hospitality","Hospitality.CompGuest","HasBed",true,"System.Boolean");
        Member("Hospitality","Hospitality.LordJob_VisitColony","leaving",false,"System.Boolean");
        Member("Gastronomy","Gastronomy.Restaurant.RestaurantController","IsOpenedRightNow",true,"System.Boolean");
        Member("Gastronomy","Gastronomy.Restaurant.RestaurantController","openForBusiness",false,"System.Boolean");
        var control=assemblies["Gastronomy"].GetType("Gastronomy.Restaurant.RestaurantController");
        foreach(var property in new[]{"Orders","Patrons"}){bool found=control?.GetProperty(property)!=null;if(!found)errors++;Console.WriteLine((found?"ok   ":"FAIL ")+"RestaurantController."+property);}
        bool all=assemblies["Gastronomy"].GetType("Gastronomy.Restaurant.RestaurantOrders")?.GetProperty("AllOrders")!=null;if(!all)errors++;Console.WriteLine((all?"ok   ":"FAIL ")+"RestaurantOrders.AllOrders");
        // The register list is needed to prevent two systems from managing the same till.
        var registers=assemblies["Gastronomy"].GetType("Gastronomy.Restaurant.RestaurantController")?.GetProperty("Registers");
        bool enumerable=registers!=null&&registers.PropertyType.GetInterfaces().Any(i=>i.FullName=="System.Collections.IEnumerable");if(!enumerable)errors++;
        Console.WriteLine((enumerable?"ok   ":"FAIL ")+"RestaurantController.Registers");Console.WriteLine("18 optional contracts checked, "+errors+" failed.");return errors==0?0:1;
    }
}
