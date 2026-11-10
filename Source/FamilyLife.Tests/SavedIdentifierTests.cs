using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// Nothing an existing save can name may disappear or change meaning when code is moved between modules. The baseline file lists every saved label, def name, XML class reference
    /// and saved enum as written by the last all-in-one version; these tests read the current tree and require all of it still to be there. They compare names and numbers only, so
    /// they say nothing about how a save loads in the game (that has to be played).
    /// </summary>
    public class SavedIdentifierTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Source", "FamilyLife", "FamilyLife.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new FileNotFoundException("repository root");
        }

        private static List<string> Baseline() =>
            File.ReadAllLines(Path.Combine(Root(), "Source", "FamilyLife.Tests", "Baselines", "saved-identifiers.txt")).Where(l => l.Length > 0 && !l.StartsWith("#")).ToList();

        private static HashSet<string> CurrentLabels()
        {
            var set = new HashSet<string>();
            string src = Path.Combine(Root(), "Source", "FamilyLife");
            var files = Directory.GetFiles(Path.Combine(src, "Persistence"), "*.cs").Concat(new[] { Path.Combine(src, "Game", "FamilyComponent.cs") });
            foreach (var f in files)
            {
                string text = File.ReadAllText(f);
                if (f.EndsWith("FamilyComponent.cs"))
                {
                    foreach (Match lm in Regex.Matches(text, @"Scribe_\w+\.Look\(\s*ref\s+[\w.]+\s*,\s*""(\w+)""")) set.Add("label FamilyComponent." + lm.Groups[1].Value);
                    continue;
                }
                foreach (Match cm in Regex.Matches(text, @"partial class (\w+)\s*:\s*IExposable(.*?)(?=\n    (?:public|internal)[^\n]*partial class \w+ *:|\z)", RegexOptions.Singleline))
                    foreach (Match lm in Regex.Matches(cm.Groups[2].Value, @"Scribe_\w+\.Look\(\s*ref\s+[\w.]+\s*,\s*""(\w+)"""))
                        set.Add("label " + cm.Groups[1].Value + "." + lm.Groups[1].Value);
            }
            return set;
        }

        private static string SourceText()
        {
            string src = Path.Combine(Root(), "Source", "FamilyLife");
            return string.Join("\n", Directory.GetFiles(src, "*.cs", SearchOption.AllDirectories).Where(f => !f.Replace('\\', '/').Contains("/obj/")).Select(File.ReadAllText));
        }

        [Fact]
        public void Every_saved_label_is_still_written()
        {
            var current = CurrentLabels();
            var missing = Baseline().Where(b => b.StartsWith("label ") && !current.Contains(b)).ToList();
            Assert.True(missing.Count == 0, "labels no longer written (an existing save would lose them): " + string.Join(", ", missing.Take(10)));
        }

        [Fact]
        public void Every_def_name_and_xml_class_reference_still_exists()
        {
            string root = Root();
            var defs = new HashSet<string>(); var classes = new HashSet<string>();
            foreach (var f in Directory.GetFiles(Path.Combine(root, "Defs"), "*.xml", SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(root, "Patches"), "*.xml")))
            {
                string text = File.ReadAllText(f);
                foreach (Match m in Regex.Matches(text, @"<(\w+)(?: [^>]*)?>\s*<defName>(\w+)</defName>")) defs.Add("def " + m.Groups[1].Value + " " + m.Groups[2].Value);
                foreach (Match m in Regex.Matches(text, @"(?:driverClass|letterClass|workerClass|thoughtClass|hediffClass)>\s*(FamilyLife\.\w+)")) classes.Add("class " + m.Groups[1].Value);
                foreach (Match m in Regex.Matches(text, @"Class=""(FamilyLife\.\w+)""")) classes.Add("class " + m.Groups[1].Value);
            }
            var missing = Baseline().Where(b => (b.StartsWith("def ") && !defs.Contains(b)) || (b.StartsWith("class ") && !classes.Contains(b))).ToList();
            Assert.True(missing.Count == 0, "defs or class references removed: " + string.Join(", ", missing.Take(10)));
        }

        [Fact]
        public void Every_class_the_xml_names_is_declared_in_the_source_so_no_job_points_at_absent_code()
        {
            string root = Root(); string source = SourceText();
            foreach (var f in Directory.GetFiles(Path.Combine(root, "Defs"), "*.xml", SearchOption.AllDirectories).Concat(Directory.GetFiles(Path.Combine(root, "Patches"), "*.xml")))
            {
                string text = File.ReadAllText(f);
                foreach (Match m in Regex.Matches(text, @"(?:driverClass|letterClass|workerClass|thoughtClass|hediffClass)>\s*FamilyLife\.(\w+)|Class=""FamilyLife\.(\w+)"""))
                {
                    string name = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                    Assert.True(Regex.IsMatch(source, @"\bclass\s+" + name + @"\b"), Path.GetFileName(f) + " names FamilyLife." + name + ", which is not declared");
                }
            }
        }

        [Fact]
        public void Saved_enums_keep_their_numbers_new_members_only_at_the_end()
        {
            string source = SourceText();
            foreach (var b in Baseline().Where(l => l.StartsWith("enum ")))
            {
                var parts = b.Split(new[] { ' ' }, 3); string name = parts[1];
                var old = parts[2].Split(',').Select(x => x.Trim()).ToList();
                var m = Regex.Match(source, @"enum " + name + @"[^{]*\{([^}]*)\}");
                Assert.True(m.Success, "enum " + name + " is gone");
                var now = m.Groups[1].Value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                Assert.True(now.Take(old.Count).SequenceEqual(old), "enum " + name + " changed order or lost a member: it is saved as a number");
            }
            var pending = Baseline().First(l => l.StartsWith("const PendingEvent "));
            Assert.Contains("None = 0, Checkups = 1, Illness = 2, Testing = 3", pending);
            Assert.Matches(@"None = 0, Checkups = 1, Illness = 2, Testing = 3", Regex.Match(source, @"static class PendingEvent\s*\{[^}]*\}", RegexOptions.Singleline).Value);
        }

        [Fact]
        public void The_registered_identifiers_are_unique_namespaced_and_inside_their_module()
        {
            string src = Path.Combine(Root(), "Source", "FamilyLife");
            var seen = new Dictionary<string, string>();
            foreach (var rel in new[] { "Game/FamilyModules.cs", "Game/FamilyLifeModule.cs", "Education/Game/EducationModule.cs", "Care/Game/CareModule.cs" })
            {
                string text = Regex.Replace(File.ReadAllText(Path.Combine(src, rel)), @"//[^\n]*", "");
                string module = Regex.Match(text, @"const string Id = ""(wils\.\w+)""").Groups[1].Value;
                foreach (Match m in Regex.Matches(text, @"""(wils(?:\.[a-z][a-z0-9]*)+)"""))
                {
                    string id = m.Groups[1].Value;
                    if (id == "wils.framework" || id == module) continue;          // the module's own id
                    if (module.Length > 0) Assert.StartsWith(module + ".", id);
                    if (seen.TryGetValue(id, out var where) && where != rel) Assert.Fail("identifier '" + id + "' is registered in both " + where + " and " + rel);
                    seen[id] = rel;
                }
            }
            Assert.True(seen.Count >= 15);
            Assert.Contains("wils.education.teacher", seen.Keys); Assert.Contains("wils.care.healer", seen.Keys);
        }
    }
}
