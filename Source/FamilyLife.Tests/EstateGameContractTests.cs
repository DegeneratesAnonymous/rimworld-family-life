using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// The game layer cannot run outside RimWorld, so these read its source and Defs and check the promises that matter most: no item is ever made or taken from
    /// someone who is alive, nothing runs per tick without a gate, the player is never answered for, every definition has its class, and every piece of static state
    /// is cleared on a new or loaded game. They are contracts on the text of the code, not a substitute for playing it.
    /// </summary>
    public class EstateGameContractTests
    {
        private static string Root()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Source", "FamilyLife", "FamilyLife.csproj"))) dir = dir.Parent;
            return dir?.FullName ?? throw new FileNotFoundException("repository root");
        }

        private static string Game(string name) => File.ReadAllText(Path.Combine(Root(), "Source", "FamilyLife", "Game", name));
        private static string Defs(string name) => File.ReadAllText(Path.Combine(Root(), "Defs", name));

        private static string Body(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.True(start >= 0, signature + " not found");
            int open = source.IndexOf('{', start), depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(open, i - open + 1);
            }
            throw new InvalidOperationException("unbalanced " + signature);
        }

        [Fact]
        public void The_estate_driver_never_makes_destroys_or_removes_the_gear_of_a_living_pawn()
        {
            string src = Game("EstateDriver.cs");
            string code = Regex.Replace(src, @"//.*", "");
            Assert.DoesNotContain("ThingMaker", code);                       // nothing is created to satisfy anyone
            Assert.DoesNotContain("GenSpawn.Spawn", code);                   // nothing is teleported onto a map
            Assert.DoesNotContain(".Destroy(", code);
            Assert.DoesNotContain("apparel.Remove", code);                   // worn gear is read, never taken
            Assert.DoesNotContain("equipment.Remove", code);
            Assert.DoesNotContain("TryDropAll", code);
            Assert.DoesNotContain("Strip", code);                            // a body is never stripped by the mod
        }

        [Fact]
        public void Delivery_walks_a_pawn_to_the_item_and_only_then_records_it()
        {
            string job = Body(Game("EstateDriver.cs"), "class JobDriver_FetchHeirloom");
            Assert.Contains("Toils_Goto.GotoThing", job);
            Assert.Contains("StartCarryThing", job);
            // The record is made inside the toil that has the item in hand, after the carry.
            Assert.True(job.IndexOf("StartCarryThing", StringComparison.Ordinal) < job.IndexOf("CompleteInheritance", StringComparison.Ordinal));
        }

        [Fact]
        public void The_person_playing_a_pawn_is_never_driven_by_the_inheritance_code()
        {
            string deliver = Body(Game("EstateDriver.cs"), "private static void DeliverOne");
            Assert.Contains("IsAvatar(c, carrier.thingIDNumber)", deliver);
            string ui = File.ReadAllText(Path.Combine(Root(), "Source", "FamilyLife", "UI", "Window_Families.Governance.cs"));
            Assert.Contains("EstateAsksFor(viewer)", ui);
            Assert.Contains("ReadyToCollect(viewer)", ui);
        }

        [Fact]
        public void Estate_work_runs_on_the_hourly_governance_pass_and_never_on_every_tick()
        {
            string component = Game("FamilyComponent.cs");
            Assert.DoesNotContain("EstateDriver.Run", component);
            Assert.Contains("EstateDriver.Run", Game("Governance.cs"));
            string run = Body(Game("EstateDriver.cs"), "internal static void Run");
            Assert.Contains("Discover(", run);
            string discover = Body(Game("EstateDriver.cs"), "private static void Discover");
            Assert.Contains("PawnsPerPass", discover);                       // a few colonists per pass, not all of them
            Assert.DoesNotContain("AllThings", Game("EstateDriver.cs"));      // no map-wide item scan
        }

        [Fact]
        public void The_rescue_site_poll_is_gated_and_only_looks_at_open_rescues()
        {
            string tick = Body(Game("KinRescueDriver.cs"), "internal static void Tick");
            int gate = tick.IndexOf("siteGate.Take", StringComparison.Ordinal), loop = tick.IndexOf("foreach", StringComparison.Ordinal);
            Assert.True(gate >= 0 && loop > gate, "the gate must come before any loop");
            Assert.Contains("Rescue == RescueStage.OnSite", tick);
        }

        [Fact]
        public void Relatives_are_moved_between_the_world_and_the_map_one_instance_at_a_time()
        {
            string populate = Body(Game("KinRescueDriver.cs"), "internal static void Populate");
            Assert.Contains("Find.WorldPawns.RemovePawn", populate);
            Assert.Contains("GenSpawn.Spawn", populate);
            Assert.Contains("PassToWorld", populate);                         // a failed placement gives the pawn back
            Assert.Contains("KinVisitDriver.Eligible", populate);             // hostile, captured, leaders, caravans and quest lodgers are left alone
            string back = Body(Game("KinRescueDriver.cs"), "internal static void HandBack");
            Assert.Contains("DeSpawn", back);
            Assert.Contains("PassToWorld", back);
            Assert.Contains("PawnDiscardDecideMode.KeepForever", back);
            Assert.DoesNotContain("PawnGenerator", populate);                 // no relative is ever generated: only the existing pawn is moved
        }

        [Fact]
        public void The_rescue_quest_is_never_accepted_by_the_mod_outside_the_developer_tools()
        {
            foreach (var file in Directory.GetFiles(Path.Combine(Root(), "Source", "FamilyLife", "Game"), "*.cs"))
            {
                if (file.EndsWith("TestToolsEstates.cs") || file.EndsWith("TestTools.cs")) continue;
                string code = Regex.Replace(File.ReadAllText(file), @"//.*", "");
                if (Path.GetFileName(file) == "KinRescueDriver.cs") code = code.Replace(Body(code, "internal static string Force"), "");
                Assert.DoesNotMatch(@"quest\w*\.Accept\(", code);
            }
        }

        [Fact]
        public void Every_driver_that_keeps_static_state_clears_it_and_is_reset_on_new_and_loaded_games()
        {
            foreach (var (file, cls) in new[] { ("EstateDriver.cs", "EstateDriver"), ("KinRescueDriver.cs", "KinRescueDriver") })
            {
                string src = Game(file);
                string reset = Body(src, "internal static void Reset");
                foreach (Match m in Regex.Matches(src, @"private static readonly \w+(?:<[^>]*>)? (\w+) = new"))
                    Assert.Contains(m.Groups[1].Value, reset);
                Assert.Contains(cls + ".Reset()", Game("GuestPasses.cs"));
            }
            string passes = Game("GuestPasses.cs");
            Assert.Contains("StartedNewGame() => GuestPasses.Reset()", passes);
            Assert.Contains("LoadedGame() => GuestPasses.Reset()", passes);
        }

        [Fact]
        public void The_quest_only_reports_and_the_registry_decides()
        {
            string part = Body(Game("KinRescueDriver.cs"), "class QuestPart_KinRescue");
            Assert.DoesNotContain("CompleteKinRescue", part);
            Assert.DoesNotContain("AcceptKinRescue", part);
            string ended = Body(Game("KinRescueDriver.cs"), "internal static void OnQuestEnded");
            Assert.Contains("RescueOpen", ended);                             // a second ending finds the rescue already closed
            Assert.Contains("FailKinRescue", ended);
        }

        [Fact]
        public void Every_definition_has_its_class_and_every_def_of_has_its_definition()
        {
            string jobs = Defs("FamilyStoryJobs.xml"), rescue = Defs("EstateAndRescueDefs.xml");
            Assert.Contains("<defName>WFL_FetchHeirloom</defName>", jobs);
            Assert.Contains("FamilyLife.JobDriver_FetchHeirloom", jobs);
            Assert.Contains("class JobDriver_FetchHeirloom : JobDriver", Game("EstateDriver.cs"));
            Assert.Contains("WFL_FetchHeirloom", Game("FamilyEconomy.cs"));
            Assert.Contains("<defName>WFL_KinRescueSite</defName>", rescue);
            Assert.Contains("<worldObjectClass>FamilyLife.WorldObject_KinRescue</worldObjectClass>", rescue);
            Assert.Contains("class WorldObject_KinRescue : MapParent", Game("KinRescueDriver.cs"));
            Assert.Contains("WFL_KinRescueSite", Game("KinRescueDriver.cs"));
            Assert.Contains("<defName>WFL_KinRescue</defName>", rescue);
            Assert.Contains("GetNamedSilentFail(\"WFL_KinRescue\")", Game("KinRescueDriver.cs"));
            Assert.Contains("<defName>WFL_InheritedKeepsake</defName>", rescue);
        }

        [Fact]
        public void Every_text_key_the_new_code_uses_exists()
        {
            var have = new System.Collections.Generic.HashSet<string>();
            foreach (var f in Directory.GetFiles(Path.Combine(Root(), "Languages", "English", "Keyed"), "*.xml"))
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"<(WFL_\w+)>")) have.Add(m.Groups[1].Value);
            var files = new[] { Path.Combine("Game", "EstateDriver.cs"), Path.Combine("Game", "KinRescueDriver.cs"), Path.Combine("UI", "Window_Families.Estates.cs") }
                .Select(f => File.ReadAllText(Path.Combine(Root(), "Source", "FamilyLife", f))).ToList();
            foreach (var src in files)
                foreach (Match m in Regex.Matches(src, "\"(WFL_\\w+)\"\\.Translate"))
                    Assert.True(have.Contains(m.Groups[1].Value), "missing text key " + m.Groups[1].Value);
            // Keys built from an enum: every value must have its text.
            foreach (var stage in new[] { "Mourning", "Claims", "Discussion", "Mediation", "Delivery", "Resolved", "Unresolved" }) Assert.True(have.Contains("WFL_EstateStage_" + stage), stage);
            foreach (var reason in new[] { "None", "Destroyed", "Missing", "Unreachable", "Forbidden", "Held", "Buried", "Unanswered", "NoHeir" }) Assert.True(have.Contains("WFL_EstateLost_" + reason), reason);
            foreach (var stage in new[] { "None", "Offered", "Accepted", "OnSite", "Succeeded", "Declined", "Expired", "Abandoned", "Lost", "SiteLost" }) Assert.True(have.Contains("WFL_RescueStage_" + stage), stage);
            foreach (var trouble in new[] { "BrokenTransport", "Injury", "Animals", "Raiders", "Weather" })
                foreach (var tech in new[] { "low", "high" }) Assert.True(have.Contains("WFL_Rescue_Trouble_" + trouble + "_" + tech), trouble + tech);
            foreach (var why in new[] { "Declined", "Expired", "Abandoned", "Lost", "SiteLost" }) Assert.True(have.Contains("WFL_Kin_Failed_" + why), why);
        }
    }
}
