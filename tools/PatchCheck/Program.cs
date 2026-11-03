using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// Usage: PatchCheck <mod dll> <folder with the game's (or reference) assemblies> [<more reference folders>...]
internal static class Program
{
    private static int Main(string[] args)
    {
        // PatchCheck <mod.dll> <core: mscorlib|System.Private.CoreLib> <assembly folder> [more folders]
        if (args.Length < 3) { Console.Error.WriteLine("usage: PatchCheck <mod.dll> <mscorlib|System.Private.CoreLib> <folder> [folder...]"); return 2; }
        string core = args[1];
        var paths = new List<string>();
        if (core != "mscorlib") paths.AddRange(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location), "*.dll"));
        foreach (var folder in args.Skip(2)) paths.AddRange(Directory.GetFiles(folder, "*.dll"));
        paths.Add(args[0]);
        // One file per assembly name, or the resolver refuses to load a second copy.
        var unique = paths
            .Where(p => core == "mscorlib" || Path.GetFileName(p) != "mscorlib.dll")
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();

        using (var context = new MetadataLoadContext(new PathAssemblyResolver(unique), core))
        {
            var mod = context.LoadFromAssemblyPath(args[0]);
            int failures = 0, checkedCount = 0;
            foreach (var type in mod.GetTypes())
            {
                var patch = type.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.Name == "HarmonyPatch");
                if (patch == null) continue;
                checkedCount++;
                string error = Check(type, patch);
                Console.WriteLine((error == null ? "ok   " : "FAIL ") + type.Name + (error == null ? "" : " - " + error));
                if (error != null) failures++;
            }
            Console.WriteLine(checkedCount + " patch class(es) checked, " + failures + " failed.");
            return failures == 0 ? 0 : 1;
        }
    }

    private static string Check(Type patchType, CustomAttributeData attribute)
    {
        var ctorArgs = attribute.ConstructorArguments;
        if (ctorArgs.Count < 2 || !(ctorArgs[0].Value is Type targetType) || !(ctorArgs[1].Value is string methodName))
            return "unsupported HarmonyPatch attribute shape";
        Type[] parameterTypes = null;
        if (ctorArgs.Count >= 3 && ctorArgs[2].Value is IReadOnlyCollection<CustomAttributeTypedArgument> list)
            parameterTypes = list.Select(a => (Type)a.Value).ToArray();

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        MethodBase original = null;
        for (var t = targetType; t != null && original == null; t = t.BaseType)
        {
            var candidates = t.GetMethods(all).Where(m => m.Name == methodName).ToList();
            if (parameterTypes != null)
                candidates = candidates.Where(m => m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(parameterTypes.Select(p => p.FullName))).ToList();
            if (candidates.Count > 1) return "ambiguous target " + targetType.Name + "." + methodName + " (" + candidates.Count + " overloads)";
            original = candidates.FirstOrDefault();
        }
        if (original == null) return "target " + targetType.Name + "." + methodName + " not found";

        var originalNames = new HashSet<string>(original.GetParameters().Select(p => p.Name));
        foreach (var patchMethod in patchType.GetMethods(all).Where(m => m.Name == "Postfix" || m.Name == "Prefix" || m.Name == "Transpiler" || m.Name == "Finalizer"))
        {
            foreach (var parameter in patchMethod.GetParameters())
            {
                string n = parameter.Name;
                if (n.StartsWith("__") || n == "___") continue;      // Harmony's injected arguments
                if (!originalNames.Contains(n))
                    return patchMethod.Name + " parameter '" + n + "' is not a parameter of " + original.DeclaringType.Name + "." + original.Name
                        + " (" + string.Join(", ", originalNames) + ")";
            }
        }
        return null;
    }
}
