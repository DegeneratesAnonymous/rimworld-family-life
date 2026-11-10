using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// The module boundaries, enforced on the source tree. The game code cannot run outside RimWorld, so these read the files: every type is owned by the folder that declares it
    /// (Education/, Care/, or the shared remainder), and a reference from one owner to another is a failure unless it is listed here. The list is the honest record of what is still
    /// coupled; it should only ever shrink.
    /// </summary>
    public class ArchitectureTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Source", "FamilyLife", "FamilyLife.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new FileNotFoundException("repository root");
        }

        private static string Strip(string text)
        {
            text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
            text = Regex.Replace(text, @"//[^\n]*", "");
            return Regex.Replace(text, "\"(?:\\\\.|[^\"\\\\])*\"", "\"\"");
        }

        private static string Owner(string relative)
        {
            relative = relative.Replace('\\', '/');
            return relative.StartsWith("Education/") ? "education" : relative.StartsWith("Care/") ? "care" : "shared";
        }

        private sealed class Source { public string Path, Owner; public HashSet<string> Words; public HashSet<string> Declared; }

        private static List<Source> Load()
        {
            string src = System.IO.Path.Combine(Root(), "Source", "FamilyLife");
            var list = new List<Source>();
            foreach (var file in Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories))
            {
                string rel = file.Substring(src.Length + 1).Replace('\\', '/');
                if (rel.StartsWith("obj/")) continue;
                string text = Strip(File.ReadAllText(file));
                list.Add(new Source
                {
                    Path = rel, Owner = Owner(rel),
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
        public void Shared_code_reaches_a_module_only_through_the_composition_root()
        {
            var all = Load(); var owned = Owned(all);
            // FamilyModules is where modules are registered; that is the one place the shared remainder is allowed to name them.
            var expected = new[] { "Game/FamilyModules.cs -> CareModule", "Game/FamilyModules.cs -> EducationModule" };
            var found = Violations(all, "shared", "education", owned).Concat(Violations(all, "shared", "care", owned)).OrderBy(x => x).ToList();
            Assert.Equal(expected.OrderBy(x => x).ToList(), found);
        }

        [Fact]
        public void The_framework_contracts_are_free_of_the_game_and_of_every_module()
        {
            string dir = Path.Combine(Root(), "Source", "FamilyLife", "Framework");
            foreach (var file in Directory.GetFiles(dir, "*.cs"))
            {
                string text = Strip(File.ReadAllText(file));
                Assert.DoesNotMatch(@"\busing\s+(Verse|RimWorld|UnityEngine|HarmonyLib)", text);
                foreach (var banned in new[] { "Education", "Teacher", "Healer", "Community Care", "ClassSessions", "ClinicVisits" })
                    Assert.False(Regex.IsMatch(text, @"\b" + banned + @"\b"), Path.GetFileName(file) + " mentions " + banned);
            }
        }

        [Fact]
        public void Every_module_folder_keeps_pure_domain_code_apart_from_game_code()
        {
            foreach (var module in new[] { "Education", "Care" })
            {
                string domain = Path.Combine(Root(), "Source", "FamilyLife", module, "Domain");
                Assert.True(Directory.Exists(domain));
                foreach (var file in Directory.GetFiles(domain, "*.cs"))
                    Assert.DoesNotMatch(@"\busing\s+(Verse|RimWorld|UnityEngine|HarmonyLib)", Strip(File.ReadAllText(file)));
            }
        }

        [Fact]
        public void Modules_hold_no_members_of_the_shared_records_their_state_is_a_payload()
        {
            string src = Path.Combine(Root(), "Source", "FamilyLife");
            foreach (var module in new[] { "Education", "Care" })
                foreach (var file in Directory.GetFiles(Path.Combine(src, module), "*.cs", SearchOption.AllDirectories))
                {
                    string text = Strip(File.ReadAllText(file));
                    Assert.DoesNotMatch(@"partial\s+class\s+(FamilyRegistry|CommunityOffice)", text);          // a partial class cannot leave its assembly; module state lives in payloads instead
                }
        }

        [Fact]
        public void The_modules_pure_code_builds_against_the_shared_pure_code_alone()
        {
            // the isolation projects are the proof (scripts/Test.ps1 builds them); this checks they exist and compile exactly the module's own folder and nothing else
            string iso = Path.Combine(Root(), "Source", "Isolation");
            foreach (var module in new[] { "Education", "Care" })
            {
                string proj = File.ReadAllText(Path.Combine(iso, module + ".Domain.csproj"));
                Assert.Contains(@"..\FamilyLife\" + module + @"\Domain\**\*.cs", proj);
                Assert.Contains("Shared.Domain.csproj", proj);
                Assert.Equal(1, Regex.Matches(proj, @"<Compile Include=").Count);
            }
            string shared = File.ReadAllText(Path.Combine(iso, "Shared.Domain.csproj"));
            Assert.Contains(@"..\FamilyLife\Domain\**\*.cs", shared); Assert.Contains(@"..\FamilyLife\Framework\**\*.cs", shared);
            Assert.Equal(2, Regex.Matches(shared, @"<Compile Include=").Count);          // the shared assembly compiles the shared folders and nothing of any module
            Assert.DoesNotContain("ProjectReference", shared);
        }

        [Fact]
        public void Every_module_registers_itself_once_and_the_framework_is_not_a_module_of_its_own()
        {
            string game = Path.Combine(Root(), "Source", "FamilyLife");
            foreach (var module in new[] { "Education/Game/EducationModule.cs", "Care/Game/CareModule.cs", "Game/FamilyLifeModule.cs" })
            {
                string text = Strip(File.ReadAllText(Path.Combine(game, module)));
                Assert.Single(Regex.Matches(text, @"catalog\.Register\(new ModuleInfo", RegexOptions.IgnoreCase));
                Assert.Contains("ModuleCatalog.FrameworkId", text);          // each requires the framework and nothing else
                Assert.DoesNotMatch(@"Requires\s*=\s*new\[\]\s*\{[^}]*Module\b", text);
            }
            string host = Strip(File.ReadAllText(Path.Combine(game, "Game", "FamilyModules.cs")));
            Assert.Equal(1, Regex.Matches(host, @"IsFramework = true").Count);
        }

        [Fact]
        public void No_module_defines_a_harmony_patch_of_its_own_beyond_the_shared_one_and_none_is_added_by_reflection_per_tick()
        {
            var all = Load();
            string src = Path.Combine(Root(), "Source", "FamilyLife");
            foreach (var s in all.Where(a => a.Owner != "shared"))
                Assert.DoesNotContain("HarmonyPatch", File.ReadAllText(Path.Combine(src, s.Path)));          // Education and Care add buttons through the shared contribution list, not a patch of their own
        }
    }
}
