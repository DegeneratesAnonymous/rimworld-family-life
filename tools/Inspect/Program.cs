using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

// Usage: Inspect <core: mscorlib|System.Private.CoreLib> <folder> [folder...] -- TypeName [TypeName...]
// Prints every field, property and method of the named types (declared on the type itself).
internal static class Program
{
    private static int Main(string[] args)
    {
        int split = Array.IndexOf(args, "--");
        if (args.Length < 3 || split < 2) { Console.Error.WriteLine("usage: Inspect <core> <folder>... -- <type>..."); return 2; }
        string core = args[0];
        var folders = args.Skip(1).Take(split - 1).ToList();
        var names = args.Skip(split + 1).ToList();
        var paths = new List<string>();
        if (core != "mscorlib") paths.AddRange(Directory.GetFiles(Path.GetDirectoryName(typeof(object).Assembly.Location), "*.dll"));
        foreach (var f in folders) paths.AddRange(Directory.GetFiles(f, "*.dll"));
        var unique = paths.Where(p => core == "mscorlib" || Path.GetFileName(p) != "mscorlib.dll")
            .GroupBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).Select(g => g.Last()).ToList();
        using (var context = new MetadataLoadContext(new PathAssemblyResolver(unique), core))
        {
            var assemblies = new List<Assembly>();
            foreach (var p in unique)
            {
                var fn = Path.GetFileName(p);
                if (fn.StartsWith("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) || fn.StartsWith("RimWorld", StringComparison.OrdinalIgnoreCase) || fn.StartsWith("Krafs", StringComparison.OrdinalIgnoreCase))
                    try { assemblies.Add(context.LoadFromAssemblyPath(p)); } catch { }
            }
            foreach (var name in names)
            {
                Type type = null;
                foreach (var a in assemblies)
                {
                    try { type = a.GetTypes().FirstOrDefault(t => t.Name == name || t.FullName == name); } catch (ReflectionTypeLoadException e) { type = e.Types.Where(t => t != null).FirstOrDefault(t => t.Name == name); }
                    if (type != null) break;
                }
                Console.WriteLine("=== " + (type == null ? name + " (NOT FOUND)" : type.FullName + (type.BaseType != null ? " : " + type.BaseType.Name : "")));
                if (type == null) continue;
                const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (var f in type.GetFields(all)) Console.WriteLine("  field  " + (f.IsStatic ? "static " : "") + f.FieldType.Name + " " + f.Name);
                foreach (var m in type.GetMethods(all).Where(m => !m.IsSpecialName))
                    Console.WriteLine("  method " + (m.IsStatic ? "static " : "") + (m.IsVirtual ? "virtual " : "") + m.ReturnType.Name + " " + m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)) + ")");
            }
        }
        return 0;
    }
}
