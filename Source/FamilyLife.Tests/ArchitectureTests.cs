using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// The module boundaries, enforced on the source tree. The game code cannot run outside RimWorld, so these read files: every type is owned by the folder that declares it
    /// (Source/Education, Source/Care or the framework), a reference from one owner to another is a failure unless it is listed here, and the optional modules are separate
    /// assemblies whose projects may reference only the framework project. The lists below are the honest record of what is still coupled; they should only ever shrink.
    /// The compiler is the other half of the proof: scripts/Test.ps1 builds every module against the framework assembly's public surface for both game versions.
    /// </summary>
    public class ArchitectureTests
    {
        private static string Strip(string text)
        {
            text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"//[^\n]*", "");
            return Regex.Replace(text, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
        }

        private static string Owner(string file)
        {
            string f = file.Replace('\\', '/');
            string edu = RepoPaths.ModuleSource("Education").Replace('\\', '/') + "/", care = RepoPaths.ModuleSource("Care").Replace('\\', '/') + "/";
            string fw = RepoPaths.Framework.Replace('\\', '/') + "/";
            if (f.StartsWith(edu)) return "education";
            if (f.StartsWith(care) || f.StartsWith(fw + "Care/")) return "care";
            return "shared";
        }

        private sealed class Source { public string Path, Owner; public HashSet<string> Words, Declared; }

        private static List<Source> Load()
        {
            var list = new List<Source>();
            string root = RepoPaths.Root().Replace('\\', '/') + "/Source/";
            foreach (var file in RepoPaths.AllSource())
            {
                string text = Strip(File.ReadAllText(file));
                list.Add(new Source
                {
                    Path = file.Replace('\\', '/').Substring(root.Length), Owner = Owner(file),
                    Words = new HashSet<string>(Regex.Matches(text, @"\b\w+\b").Cast<Match>().Select(m => m.Value)),
                    Declared = new HashSet<string>(Regex.Matches(text, @"\b(?:class|enum|struct|interface)\s+(\w+)").Cast<Match>().Select(m => m.Groups[1].Value))
                });
            }
            return list;
        }

        /// <summary>Type name -> owner, for names declared in exactly one owner's folders (a partial class spread over several owners belongs to none of them).</summary>
        private static Dictionary<string, string> Owned(List<Source> all)
        {
            var byName = new Dictionary<string, HashSet<string>>();
            foreach (var s in all) foreach (var n in s.Declared) { if (!byName.TryGetValue(n, out var set)) byName[n] = set = new HashSet<string>(); set.Add(s.Owner); }
            return byName.Where(kv => kv.Value.Count == 1 && kv.Value.First() != "shared").ToDictionary(kv => kv.Key, kv => kv.Value.First());
        }

        private static List<string> Violations(List<Source> all, string fromOwner, string toOwner, Dictionary<string, string> owned) =>
            all.Where(s => s.Owner == fromOwner)
               .SelectMany(s => owned.Where(o => o.Value == toOwner && s.Words.Contains(o.Key)).Select(o => s.Path + " -> " + o.Key)).OrderBy(x => x).ToList();

        [Fact]
        public void The_modules_are_found_and_own_real_code()
        {
            var all = Load(); var owned = Owned(all);
            Assert.True(owned.Count(o => o.Value == "education") >= 15, "Education should own its types");
            Assert.True(owned.Count(o => o.Value == "care") >= 15, "Community Care should own its types");
            Assert.Contains(owned, o => o.Key == "ClassSessions" && o.Value == "education");
            Assert.Contains(owned, o => o.Key == "ClinicVisits" && o.Value == "care");
        }

        [Fact]
        public void Education_and_Community_Care_do_not_reference_each_other()
        {
            var all = Load(); var owned = Owned(all);
            Assert.Empty(Violations(all, "education", "care", owned));
            Assert.Empty(Violations(all, "care", "education", owned));
        }

        [Fact]
        public void The_framework_never_names_a_module()
        {
            var all = Load(); var owned = Owned(all);
            Assert.Empty(Violations(all, "shared", "education", owned));          // both optional modules are separate assemblies: nothing in the framework may name them
            Assert.Empty(Violations(all, "shared", "care", owned));
        }

        [Fact]
        public void The_framework_contracts_are_free_of_the_game_and_of_every_module()
        {
            string dir = Path.Combine(RepoPaths.Framework, "Framework");
            foreach (var file in Directory.GetFiles(dir, "*.cs"))
            {
                string text = Strip(File.ReadAllText(file));
                Assert.DoesNotMatch(@"\busing\s+(Verse|RimWorld|UnityEngine|HarmonyLib)", text);
                foreach (var banned in new[] { "Education", "Teacher", "Healer", "Community Care", "ClassSessions", "ClinicVisits" })
                    Assert.False(Regex.IsMatch(text, @"\b" + banned + @"\b"), Path.GetFileName(file) + " mentions " + banned);
            }
        }

        [Fact]
        public void Teacher_and_Healer_remain_in_the_framework_only_as_saved_identity_presentation_and_policy_tables()
        {
            // Not execution: the saved enum, role presentation (icons and names for historic holders), the election's work-needed/house-wish tables, a camp moment that credits a
            // teacher, and the save reader's default. Each is a known coupling to remove when the policy tables become part of a post's definition. The list must only shrink.
            var found = new SortedSet<string>();
            foreach (var file in Directory.GetFiles(RepoPaths.Framework, "*.cs", SearchOption.AllDirectories))
            {
                string f = file.Replace('\\', '/');
                if (f.Contains("/obj/") || f.Contains("/Care/")) continue;
                if (Regex.IsMatch(Strip(File.ReadAllText(file)), @"\bOfficeKind\.(Teacher|Healer)\b|\bFamilyRole\.(Teacher|Healer)\b|\b(Teacher|Healer)\s*[,}]"))
                    found.Add(f.Substring(RepoPaths.Framework.Replace('\\', '/').Length + 1));
            }
            Assert.Equal(new[] { "Domain/CommunityOffices.cs", "Domain/RolePresentation.cs", "Game/Flavors.cs", "Persistence/PersistGovernance.cs" }, found.ToArray());
        }

        [Fact]
        public void Every_module_keeps_pure_domain_code_apart_from_game_code()
        {
            foreach (var domain in new[] { RepoPaths.Resolve("Education/Domain"), RepoPaths.Resolve("Care/Domain") })
            {
                Assert.True(Directory.Exists(domain), domain);
                foreach (var file in Directory.GetFiles(domain, "*.cs"))
                    Assert.DoesNotMatch(@"\busing\s+(Verse|RimWorld|UnityEngine|HarmonyLib)", Strip(File.ReadAllText(file)));
            }
        }

        [Fact]
        public void Modules_hold_no_members_of_the_shared_records_their_state_is_a_payload()
        {
            foreach (var file in RepoPaths.AllSource().Where(f => Owner(f) != "shared"))
                Assert.DoesNotMatch(@"partial\s+class\s+(FamilyRegistry|CommunityOffice)", Strip(File.ReadAllText(file)));          // a partial class cannot leave its assembly; module state lives in payloads
        }

        [Fact]
        public void The_modules_pure_code_builds_against_the_shared_pure_code_alone()
        {
            // the isolation projects are the proof (scripts/Test.ps1 builds them); this checks they exist and compile exactly the module's own folder and nothing else
            string iso = Path.Combine(RepoPaths.Root(), "Source", "Isolation");
            foreach (var module in new[] { "Education", "Care" })
            {
                string proj = File.ReadAllText(Path.Combine(iso, module + ".Domain.csproj"));
                Assert.Contains(module + @"\Domain\**\*.cs", proj);
                Assert.Contains("Shared.Domain.csproj", proj);
                Assert.Equal(1, Regex.Matches(proj, @"<Compile Include=").Count);
            }
            string shared = File.ReadAllText(Path.Combine(iso, "Shared.Domain.csproj"));
            Assert.Contains(@"..\FamilyLife\Domain\**\*.cs", shared); Assert.Contains(@"..\FamilyLife\Framework\**\*.cs", shared);
            Assert.Equal(2, Regex.Matches(shared, @"<Compile Include=").Count);          // the shared assembly compiles the shared folders and nothing of any module
            Assert.DoesNotContain("ProjectReference", shared);
        }

        [Fact]
        public void Every_module_installs_through_the_one_interface_and_registers_itself_once()
        {
            foreach (var module in new[] { "Education/Game/EducationModule.cs", "Care/Game/CareModule.cs", "Game/FamilyLifeModule.cs" })
            {
                string text = Strip(RepoPaths.Read(module));
                Assert.Contains(": IFamilyModule", text);
                Assert.Single(Regex.Matches(text, @"new ModuleInfo", RegexOptions.IgnoreCase));
                Assert.Contains("ModuleCatalog.FrameworkId", text);          // each requires the framework and nothing else
                Assert.Contains("ApiVersion = FamilyFramework.ApiVersion", text);          // and declares the API it was built for
                Assert.DoesNotMatch(@"Requires\s*=\s*new\[\]\s*\{[^}]*Module\b", text);
            }
            string host = Strip(RepoPaths.Read("Game/FamilyFramework.cs"));
            Assert.Equal(1, Regex.Matches(host, @"IsFramework = true").Count);
        }

        [Fact]
        public void No_module_defines_a_harmony_patch_of_its_own()
        {
            foreach (var file in RepoPaths.AllSource().Where(f => Owner(f) != "shared"))
                Assert.DoesNotContain("HarmonyPatch", File.ReadAllText(file));          // modules add buttons through the shared contribution lists, not a patch of their own
        }

        // ------------------------------------------------------------------ the module is a real assembly

        [Theory]
        [InlineData("Education", "WilsEducation")]
        [InlineData("Care", "WilsCommunityCare")]
        public void A_module_is_its_own_project_that_references_only_the_framework_project(string folder, string assembly)
        {
            string proj = File.ReadAllText(Path.Combine(RepoPaths.ModuleSource(folder), assembly + ".csproj"));
            Assert.Contains("<AssemblyName>" + assembly + "</AssemblyName>", proj);
            var refs = Regex.Matches(proj, @"<ProjectReference Include=""([^""]+)""").Cast<Match>().Select(m => m.Groups[1].Value).ToList();
            Assert.Equal(new[] { @"..\FamilyLife\FamilyLife.csproj" }, refs);          // the framework, and nothing else (in particular never the other module)
            Assert.DoesNotMatch(@"<Compile Include=""\.\.", proj);          // it compiles its own folder only, never the framework's source
            string framework = File.ReadAllText(Path.Combine(RepoPaths.Framework, "FamilyLife.csproj"));
            Assert.DoesNotContain(assembly, framework);          // and the framework knows nothing of it
        }

        [Fact]
        public void No_assembly_relies_on_InternalsVisibleTo_the_public_boundary_is_complete()
        {
            foreach (var file in RepoPaths.AllSource().Concat(Directory.GetFiles(Path.Combine(RepoPaths.Root(), "Source"), "*.csproj", SearchOption.AllDirectories)).Where(f => !f.Replace('\\', '/').Contains("/obj/")))
                Assert.DoesNotContain("InternalsVisibleTo", File.ReadAllText(file));
        }

        [Theory]
        [InlineData("education")]
        [InlineData("care")]
        public void A_module_uses_no_framework_internal_type(string owner)
        {
            var all = Load();
            var internalTypes = new HashSet<string>();
            foreach (var s in all.Where(a => a.Owner == "shared"))
            {
                string text = Strip(File.ReadAllText(Path.Combine(RepoPaths.Root(), "Source", s.Path)));
                foreach (Match m in Regex.Matches(text, @"(?m)^\s*internal\s+(?:static\s+|sealed\s+|abstract\s+|partial\s+)*(?:class|struct|enum|interface)\s+(\w+)")) internalTypes.Add(m.Groups[1].Value);
            }
            var own = new HashSet<string>(all.Where(a => a.Owner == owner).SelectMany(a => a.Declared));
            internalTypes.ExceptWith(own);
            var leaks = all.Where(a => a.Owner == owner).SelectMany(a => internalTypes.Where(t => a.Words.Contains(t)).Select(t => a.Path + " -> " + t)).OrderBy(x => x).ToList();
            Assert.Empty(leaks);          // the compiler says the same; this names the culprit without a game install
        }

        // ------------------------------------------------------------------ packages

        private static HashSet<string> DefNames(string folder)
        {
            var names = new HashSet<string>();
            string defs = Path.Combine(folder, "Defs");
            if (!Directory.Exists(defs)) return names;
            foreach (var f in Directory.GetFiles(defs, "*.xml", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"<defName>(\w+)</defName>")) names.Add(m.Groups[1].Value);
            return names;
        }

        [Theory]
        [InlineData("WilsEducation", "SpacemanWil.WilsEducation", "SpacemanWil.WilsCommunityCare")]
        [InlineData("WilsCommunityCare", "SpacemanWil.WilsCommunityCare", "SpacemanWil.WilsEducation")]
        public void A_module_package_declares_its_identity_dependencies_and_load_order(string folder, string packageId, string other)
        {
            string xml = File.ReadAllText(Path.Combine(RepoPaths.Root(), "Packages", folder, "About", "About.xml"));
            Assert.Contains("<packageId>" + packageId + "</packageId>", xml);
            Assert.Contains("<li>1.6</li>", xml); Assert.Contains("<li>1.7</li>", xml);
            var deps = Regex.Match(xml, @"<modDependencies>.*?</modDependencies>", RegexOptions.Singleline).Value;
            Assert.Contains("brrainz.harmony", deps); Assert.Contains("SpacemanWil.WilsFamilyLife", deps);
            Assert.DoesNotContain(other, xml.Substring(0, xml.IndexOf("<description>")));          // never requires, or even orders itself against, the other module
            var after = Regex.Match(xml, @"<loadAfter>.*?</loadAfter>", RegexOptions.Singleline).Value;
            Assert.Contains("SpacemanWil.WilsFamilyLife", after);          // loads after the framework, so the framework is ready when the module announces itself
            string framework = File.ReadAllText(Path.Combine(RepoPaths.Root(), "About", "About.xml"));
            Assert.DoesNotContain(packageId, framework);          // the framework package does not depend on a module
        }

        [Fact]
        public void Each_package_ships_the_defs_it_owns_and_none_twice()
        {
            var framework = DefNames(RepoPaths.Root());
            var education = DefNames(Path.Combine(RepoPaths.Root(), "Packages", "WilsEducation"));
            var care = DefNames(Path.Combine(RepoPaths.Root(), "Packages", "WilsCommunityCare"));
            Assert.Empty(framework.Intersect(education)); Assert.Empty(framework.Intersect(care)); Assert.Empty(education.Intersect(care));          // no def in two packages
            foreach (var d in new[] { "WFL_ClassTeacher", "WFL_ClassStudent", "WFL_OfficeLesson", "WFL_TestStudent", "WFL_InspiringLesson", "WFL_BoringLesson", "WFL_TestingProud", "WFL_TestingDone", "WFL_TestingDisappointed" })
            { Assert.Contains(d, education); Assert.DoesNotContain(d, framework); }
            foreach (var d in new[] { "WFL_Checkup", "WFL_ClinicDoctor", "WFL_ClinicPatient", "WFL_CheckedUp", "WFL_ReassuringCheckup", "WFL_DismissiveCheckup" })
            { Assert.Contains(d, care); Assert.DoesNotContain(d, framework); }
        }

        [Fact]
        public void Framework_defs_of_defs_never_name_a_class_that_only_a_module_declares()
        {
            var all = Load(); var owned = Owned(all);
            foreach (var f in Directory.GetFiles(Path.Combine(RepoPaths.Root(), "Defs"), "*.xml", SearchOption.AllDirectories))
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"FamilyLife\.(\w+)"))
                    Assert.False(owned.TryGetValue(m.Groups[1].Value, out var owner) && owner == "education", Path.GetFileName(f) + " names Education's " + m.Groups[1].Value);
        }

        [Fact]
        public void The_framework_def_references_name_only_defs_the_framework_package_ships()
        {
            // With only the framework installed a [DefOf] field that names a def from another package is a red error at startup. Every WFL_ field of the framework's own DefOf
            // classes must be defined in the framework package.
            var framework = DefNames(RepoPaths.Root());
            foreach (var file in Directory.GetFiles(RepoPaths.Framework, "*.cs", SearchOption.AllDirectories).Where(f => !f.Replace('\\', '/').Contains("/obj/")))
            {
                string text = Strip(File.ReadAllText(file));
                if (!text.Contains("[DefOf]")) continue;
                foreach (Match m in Regex.Matches(text, @"public static \w*Def\s+([\w, ]+);"))
                    foreach (var name in m.Groups[1].Value.Split(',').Select(n => n.Trim()).Where(n => n.StartsWith("WFL_")))
                        Assert.True(framework.Contains(name), Path.GetFileName(file) + " expects def " + name + ", which the framework package does not ship");
            }
        }

        [Theory]
        [InlineData("Education", "WilsEducation")]
        [InlineData("Care", "WilsCommunityCare")]
        public void A_modules_def_references_name_only_defs_its_package_ships(string folder, string package)
        {
            var shipped = DefNames(Path.Combine(RepoPaths.Root(), "Packages", package));
            foreach (var file in Directory.GetFiles(RepoPaths.ModuleSource(folder), "*.cs", SearchOption.AllDirectories).Where(f => !f.Replace('\\', '/').Contains("/obj/")))
            {
                string text = Strip(File.ReadAllText(file));
                if (!text.Contains("[DefOf]")) continue;
                foreach (Match m in Regex.Matches(text, @"public static \w*Def\s+([\w, ]+);"))
                    foreach (var name in m.Groups[1].Value.Split(',').Select(n => n.Trim()).Where(n => n.StartsWith("WFL_")))
                        Assert.True(shipped.Contains(name), folder + " expects def " + name + ", which its package does not ship");
            }
        }

        [Theory]
        [InlineData("Education")]
        [InlineData("Care")]
        public void Every_translation_key_a_module_uses_exists_in_its_package_or_the_framework(string folder)
        {
            var known = new HashSet<string>();
            foreach (var pkg in RepoPaths.PackageFolders())
            {
                string lang = Path.Combine(pkg, "Languages", "English", "Keyed");
                if (!Directory.Exists(lang)) continue;
                foreach (var f in Directory.GetFiles(lang, "*.xml")) foreach (Match m in Regex.Matches(File.ReadAllText(f), @"<(WFL_\w+)>")) known.Add(m.Groups[1].Value);
            }
            var missing = new List<string>();
            foreach (var file in Directory.GetFiles(RepoPaths.ModuleSource(folder), "*.cs", SearchOption.AllDirectories).Where(f => !f.Replace('\\', '/').Contains("/obj/")))
                foreach (Match m in Regex.Matches(File.ReadAllText(file), @"""(WFL_\w+)""\.Translate"))
                    if (!known.Contains(m.Groups[1].Value)) missing.Add(Path.GetFileName(file) + ":" + m.Groups[1].Value);
            Assert.Empty(missing);
        }

        [Fact]
        public void A_modules_keys_live_only_in_its_package()
        {
            string frameworkLang = Path.Combine(RepoPaths.Root(), "Languages", "English", "Keyed");
            string text = string.Join("\n", Directory.GetFiles(frameworkLang, "*.xml").Select(File.ReadAllText));
            var education = new[] { "WFL_HoldClass_Label", "WFL_Testing_Begins", "WFL_Event_Testing_Title", "WFL_Event_NoPupils", "WFL_Class_Begins" };
            var care = new[] { "WFL_Event_Checkups_Title", "WFL_Event_Illness_Text", "WFL_Event_NoPost_Doctor", "WFL_Checkup_Result" };
            foreach (var key in education.Concat(care)) Assert.DoesNotContain("<" + key + ">", text);
            string edu = File.ReadAllText(Path.Combine(RepoPaths.Root(), "Packages", "WilsEducation", "Languages", "English", "Keyed", "Education.xml"));
            foreach (var key in education) Assert.Contains("<" + key + ">", edu);
            string cr = File.ReadAllText(Path.Combine(RepoPaths.Root(), "Packages", "WilsCommunityCare", "Languages", "English", "Keyed", "Care.xml"));
            foreach (var key in care) Assert.Contains("<" + key + ">", cr);
            Assert.Contains("<WFL_OfficeKind_Teacher>", text); Assert.Contains("<WFL_OfficeKind_Healer>", text);          // the framework still labels a saved post for the history when its module is absent
        }
    }
}
