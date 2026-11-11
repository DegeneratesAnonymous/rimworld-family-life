using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Reads the built assemblies (not the source) and checks the boundaries between them: who references whom, that no type is shipped twice, that the framework offers its internals to
    /// no one, and that the packages hold what they should. Only runs against assemblies that exist, so a machine that has not built a game version is not failed for it; scripts/Test.ps1
    /// builds both versions first. This is static inspection of metadata: it does not load or run anything, and says nothing about how the game behaves.
    /// </summary>
    public class AssemblyBoundaryTests
    {
        private sealed class Asm { public string Path, Name; public HashSet<string> References, Types, Attributes; }

        private static Asm Read(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var pe = new PEReader(stream))
            {
                var md = pe.GetMetadataReader();
                var a = new Asm { Path = path, Name = md.GetString(md.GetAssemblyDefinition().Name), References = new HashSet<string>(), Types = new HashSet<string>(), Attributes = new HashSet<string>() };
                foreach (var h in md.AssemblyReferences) a.References.Add(md.GetString(md.GetAssemblyReference(h).Name));
                foreach (var h in md.TypeDefinitions)
                {
                    var t = md.GetTypeDefinition(h);
                    string ns = md.GetString(t.Namespace), name = md.GetString(t.Name);
                    if (name == "<Module>" || name.StartsWith("<") || !t.GetDeclaringType().IsNil) continue;          // top-level types only: nested types belong to their owner
                    if (ns.StartsWith("Microsoft.CodeAnalysis") || ns.StartsWith("System.Runtime.CompilerServices")) continue;          // compiler support types every assembly carries
                    a.Types.Add((ns.Length > 0 ? ns + "." : "") + name);
                }
                foreach (var h in md.GetAssemblyDefinition().GetCustomAttributes())
                {
                    var ca = md.GetCustomAttribute(h);
                    if (ca.Constructor.Kind == HandleKind.MemberReference)
                    {
                        var parent = md.GetMemberReference((MemberReferenceHandle)ca.Constructor).Parent;
                        if (parent.Kind == HandleKind.TypeReference) a.Attributes.Add(md.GetString(md.GetTypeReference((TypeReferenceHandle)parent).Name));
                    }
                }
                return a;
            }
        }

        private static IEnumerable<string> Versions() => new[] { "1.6", "1.7" };

        private static string Framework(string v) => Path.Combine(RepoPaths.Root(), v, "Assemblies", "FamilyLife.dll");
        private static string Module(string v, string package, string assembly) => Path.Combine(RepoPaths.Root(), "Packages", package, v, "Assemblies", assembly + ".dll");

        private static bool Built(string v) => File.Exists(Framework(v)) && File.Exists(Module(v, "WilsEducation", "WilsEducation")) && File.Exists(Module(v, "WilsCommunityCare", "WilsCommunityCare"));

        [Fact]
        public void The_framework_assembly_references_no_module_and_the_modules_reference_the_framework_and_never_each_other()
        {
            foreach (var v in Versions().Where(Built))
            {
                var fw = Read(Framework(v)); var edu = Read(Module(v, "WilsEducation", "WilsEducation")); var care = Read(Module(v, "WilsCommunityCare", "WilsCommunityCare"));
                Assert.DoesNotContain("WilsEducation", fw.References); Assert.DoesNotContain("WilsCommunityCare", fw.References);
                Assert.Contains("FamilyLife", edu.References); Assert.DoesNotContain("WilsCommunityCare", edu.References);
                Assert.Contains("FamilyLife", care.References); Assert.DoesNotContain("WilsEducation", care.References);
            }
        }

        [Fact]
        public void No_type_is_shipped_in_two_assemblies()
        {
            foreach (var v in Versions().Where(Built))
            {
                var all = new[] { Read(Framework(v)), Read(Module(v, "WilsEducation", "WilsEducation")), Read(Module(v, "WilsCommunityCare", "WilsCommunityCare")) };
                var dupes = all.SelectMany(a => a.Types.Select(t => new { a.Name, t })).GroupBy(x => x.t).Where(g => g.Count() > 1).Select(g => g.Key + " in " + string.Join(" and ", g.Select(x => x.Name))).ToList();
                Assert.Empty(dupes);
            }
        }

        [Fact]
        public void The_modules_own_their_types_and_the_framework_holds_none_of_them()
        {
            foreach (var v in Versions().Where(Built))
            {
                var fw = Read(Framework(v)); var edu = Read(Module(v, "WilsEducation", "WilsEducation")); var care = Read(Module(v, "WilsCommunityCare", "WilsCommunityCare"));
                foreach (var t in new[] { "FamilyLife.EducationModule", "FamilyLife.ClassSessions", "FamilyLife.JobDriver_ClassTeacher", "FamilyLife.JobDriver_ClassStudent", "FamilyLife.JobDriver_TestStudent", "FamilyLife.EducationDefOf", "FamilyLife.EducationMod" })
                { Assert.Contains(t, edu.Types); Assert.DoesNotContain(t, fw.Types); }
                foreach (var t in new[] { "FamilyLife.CareModule", "FamilyLife.ClinicVisits", "FamilyLife.JobDriver_ClinicDoctor", "FamilyLife.JobDriver_ClinicPatient", "FamilyLife.CareDefOf", "FamilyLife.CareMod" })
                { Assert.Contains(t, care.Types); Assert.DoesNotContain(t, fw.Types); }
                Assert.Contains("FamilyLife.FamilyFramework", fw.Types); Assert.Contains("FamilyLife.FrameworkServices", fw.Types);
            }
        }

        [Fact]
        public void The_framework_offers_its_internals_to_no_assembly()
        {
            foreach (var v in Versions().Where(Built)) Assert.DoesNotContain("InternalsVisibleToAttribute", Read(Framework(v)).Attributes);
        }

        [Fact]
        public void Each_module_assembly_has_exactly_one_mod_entry_point_and_the_framework_one_of_its_own()
        {
            foreach (var v in Versions().Where(Built))
            {
                Assert.Contains("FamilyLife.FamilyLifeMod", Read(Framework(v)).Types);
                Assert.Contains("FamilyLife.EducationMod", Read(Module(v, "WilsEducation", "WilsEducation")).Types);
                Assert.Contains("FamilyLife.CareMod", Read(Module(v, "WilsCommunityCare", "WilsCommunityCare")).Types);
            }
        }

        [Fact]
        public void The_module_packages_ship_their_own_assembly_only_and_never_a_copy_of_the_framework_or_a_dependency()
        {
            foreach (var v in Versions())
                foreach (var pkg in new[] { "WilsEducation", "WilsCommunityCare" })
                {
                    string dir = Path.Combine(RepoPaths.Root(), "Packages", pkg, v, "Assemblies");
                    if (!Directory.Exists(dir)) continue;
                    var dlls = Directory.GetFiles(dir, "*.dll").Select(Path.GetFileName).ToList();
                    Assert.Equal(new[] { pkg + ".dll" }, dlls);          // no FamilyLife.dll, no 0Harmony.dll
                }
            foreach (var v in Versions())
            {
                string dir = Path.Combine(RepoPaths.Root(), v, "Assemblies");
                if (!Directory.Exists(dir)) continue;
                Assert.DoesNotContain(Directory.GetFiles(dir, "*.dll").Select(Path.GetFileName), n => n.StartsWith("Wils"));          // the framework package carries no module assembly
            }
        }
    }
}
