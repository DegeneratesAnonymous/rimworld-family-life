using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Where the source of each package lives. The framework is Source/FamilyLife; each optional module has its own folder and project beside it (Source/Education, Source/Care once
    /// extracted). Tests that read the source tree go through here so they follow a module when it moves.
    /// </summary>
    internal static class RepoPaths
    {
        public static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Source", "FamilyLife", "FamilyLife.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new FileNotFoundException("repository root");
        }

        public static string Framework => Path.Combine(Root(), "Source", "FamilyLife");
        public static string ModuleSource(string module) => Path.Combine(Root(), "Source", module);

        /// <summary>Every source root that is an assembly of its own, framework first.</summary>
        public static IEnumerable<string> Roots()
        {
            yield return Framework;
            foreach (var m in new[] { "Education", "Care" }) if (Directory.Exists(ModuleSource(m))) yield return ModuleSource(m);
        }

        private static bool IsBuildOutput(string file) => file.Replace('\\', '/').Contains("/obj/");

        public static string[] AllSource() => Roots().SelectMany(r => Directory.GetFiles(r, "*.cs", SearchOption.AllDirectories)).Where(f => !IsBuildOutput(f)).ToArray();

        /// <summary>A path written relative to the old all-in-one layout ("Education/Game/Classes.cs", "Care/...", "Game/...") resolved to where it lives now.</summary>
        public static string Resolve(string rel)
        {
            rel = rel.Replace('\\', '/');
            foreach (var m in new[] { "Education", "Care" })
                if (rel.StartsWith(m + "/") && Directory.Exists(ModuleSource(m))) return Path.Combine(ModuleSource(m), rel.Substring(m.Length + 1));
            return Path.Combine(Framework, rel);
        }

        public static string Read(string rel) => File.ReadAllText(Resolve(rel));

        /// <summary>The package folders that ship Defs and Patches: the framework package (the repository root) and every module package.</summary>
        public static IEnumerable<string> PackageFolders()
        {
            yield return Root();
            string packages = Path.Combine(Root(), "Packages");
            if (Directory.Exists(packages)) foreach (var p in Directory.GetDirectories(packages)) yield return p;
        }

        public static IEnumerable<string> DefAndPatchFiles()
        {
            foreach (var pkg in PackageFolders())
            {
                string defs = Path.Combine(pkg, "Defs"), patches = Path.Combine(pkg, "Patches");
                if (Directory.Exists(defs)) foreach (var f in Directory.GetFiles(defs, "*.xml", SearchOption.AllDirectories)) yield return f;
                if (Directory.Exists(patches)) foreach (var f in Directory.GetFiles(patches, "*.xml")) yield return f;
            }
        }
    }
}
