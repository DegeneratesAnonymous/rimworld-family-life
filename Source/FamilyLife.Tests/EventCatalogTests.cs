using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;
using Xunit.Abstractions;

namespace FamilyLife.Tests
{
    public class EventCatalogTests
    {
        private readonly ITestOutputHelper output;
        public EventCatalogTests(ITestOutputHelper output) { this.output = output; }

        private static IEnumerable<string> ExpectedIds()
        {
            foreach (var group in new[] { "A", "B", "C" })
                foreach (var n in new[] { 1, 2, 3 })
                    for (int i = 1; i <= 10; i++) yield return group + n + "-" + i.ToString("00");
        }

        [Fact]
        public void There_are_ninety_events_ten_for_each_bucket_and_scenario()
        {
            Assert.Equal(90, EventCatalog.All.Count);
            Assert.Equal(ExpectedIds().OrderBy(x => x), EventCatalog.All.Select(e => e.Id).OrderBy(x => x));
            foreach (var bucket in Enum.GetValues(typeof(EventBucket)).Cast<EventBucket>())
                foreach (var scenario in Enum.GetValues(typeof(EventScenario)).Cast<EventScenario>())
                    Assert.Equal(10, EventCatalog.All.Count(e => e.Bucket == bucket && e.Scenario == scenario));
        }

        [Fact]
        public void Every_event_names_what_must_be_true_and_what_moves_a_person_to_do_it()
        {
            foreach (var e in EventCatalog.All)
            {
                Assert.False(string.IsNullOrEmpty(e.Key), e.Id);
                Assert.True(e.Opportunity.Length > 0 || e.Needs.Length > 0 || e.ActivityOnly, e.Id + " has no opportunity");
                Assert.True(e.Motive.Terms.Count > 0 || e.Motive.Base > 0.4f, e.Id + " has no motivation");
                Assert.True(e.Reception.Terms.Count > 0, e.Id);
                Assert.True(e.CooldownDays >= 1f, e.Id + " can repeat too quickly");
                Assert.True(e.Opportunity.All(o => !string.IsNullOrEmpty(o.Name)), e.Id);
            }
            Assert.Equal(90, EventCatalog.All.Select(e => e.Key).Distinct().Count());
        }

        [Fact]
        public void Adding_family_effects_does_not_remove_teaching_from_an_apprenticeship()
        {
            Assert.All(EventCatalog.All.Where(e => e.TeachSkill != null), e => Assert.True(e.Effects.HasFlag(EventEffect.Teaches), e.Id));
            Assert.True(EventCatalog.Get("C2-01").Effects.HasFlag(EventEffect.TiesFamilies));
        }

        [Fact]
        public void The_chapters_follow_the_design_in_where_each_kind_of_moment_can_happen()
        {
            Assert.All(EventCatalog.All.Where(e => e.Bucket == EventBucket.InterFamily), e => Assert.True(e.Effects.HasFlag(EventEffect.TiesFamilies) || e.Effects.HasFlag(EventEffect.Incident) || e.Effects.HasFlag(EventEffect.Bridge), e.Id));
            Assert.All(EventCatalog.All.Where(e => e.Bucket == EventBucket.Individual), e => Assert.False(e.Effects.HasFlag(EventEffect.Bridge), e.Id + " is an outsider event that moves families"));
            Assert.Equal("B1-05", EventCatalog.All.Single(e => e.Effects.HasFlag(EventEffect.Romance)).Id);      // only one event can ever reach for the game's romance rules
            Assert.All(EventCatalog.All.Where(e => e.Effects.HasFlag(EventEffect.Romance)), e => Assert.Equal(EventScenario.AdultAdult, e.Scenario));
        }

        /// <summary>
        /// The test that matters most: an event that can never be eligible, or that always goes the same way, is a definition and not
        /// gameplay. A sweep over random but consistent circumstances must show every event can happen and can turn out differently.
        /// </summary>
        [Fact]
        public void Every_event_can_happen_and_can_go_more_than_one_way()
        {
            var world = new EventHarness();
            var eligibleCount = EventCatalog.All.ToDictionary(e => e.Id, e => 0);
            var samples = EventCatalog.All.ToDictionary(e => e.Id, e => new List<PairContext>());
            for (int trial = 0; trial < 30000; trial++)
            {
                var ctx = world.RandomContext();
                foreach (var line in world.Registry.Explain(ctx, EventCatalog.All, childrenEnabled: true).Where(l => l.Eligible))
                {
                    eligibleCount[line.Def.Id]++;
                    if (samples[line.Def.Id].Count < 80) samples[line.Def.Id].Add(ctx);
                }
            }
            var never = eligibleCount.Where(kv => kv.Value == 0).Select(kv => kv.Key).ToList();
            output.WriteLine("eligible counts: " + string.Join(" ", eligibleCount.OrderBy(kv => kv.Key).Select(kv => kv.Key + "=" + kv.Value)));
            Assert.True(never.Count == 0, "never possible: " + string.Join(", ", never));

            var rng = new Random(99);
            var narrow = new List<string>();
            foreach (var def in EventCatalog.All)
            {
                // Across the circumstances in which it can happen (not in one fixed circumstance), it must be able to go well and go badly.
                var tiers = new HashSet<EventTier>();
                foreach (var ctx in samples[def.Id])
                    for (int i = 0; i < 12; i++) tiers.Add(world.Registry.ResolveEvent(def, ctx, rng).Tier);
                if (tiers.Count < 3 || !tiers.Contains(EventTier.Poor) || !(tiers.Contains(EventTier.Good) || tiers.Contains(EventTier.Great)))
                    narrow.Add(def.Id + ":" + string.Join("/", tiers));
            }
            Assert.True(narrow.Count == 0, "outcomes too narrow: " + string.Join(", ", narrow));
        }

        [Fact]
        public void No_event_is_possible_for_the_wrong_kind_of_pair()
        {
            var world = new EventHarness(21);
            for (int trial = 0; trial < 6000; trial++)
            {
                var ctx = world.RandomContext();
                foreach (var line in world.Registry.Explain(ctx, EventCatalog.All, childrenEnabled: true).Where(l => l.Eligible))
                {
                    bool family = ctx.SameFamily || ctx.Related;
                    var d = line.Def;
                    Assert.True(EventEngineHelpers.BucketFits(d, ctx), d.Id);
                    if (d.Bucket == EventBucket.IntraFamily) Assert.True(family, d.Id);
                    else Assert.False(family, d.Id);
                    if (d.Bucket == EventBucket.InterFamily) Assert.True(ctx.DifferentFamilies, d.Id);
                    if (d.NeedsThird) Assert.NotNull(ctx.Third);
                    if (d.Needs.Length > 0) Assert.Contains(ctx.Setting, d.Needs);
                    if (d.ActivityOnly) Assert.True(ctx.FromActivity);
                }
            }
        }

        [Fact]
        public void Child_events_do_not_happen_without_children_being_supported()
        {
            var world = new EventHarness(5);
            for (int trial = 0; trial < 3000; trial++)
            {
                var ctx = world.RandomContext();
                foreach (var line in world.Registry.Explain(ctx, EventCatalog.All, childrenEnabled: false).Where(l => l.Eligible))
                    Assert.False(line.Def.NeedsChild, line.Def.Id);
            }
        }

        [Fact]
        public void Reasons_for_not_happening_are_always_given()
        {
            var world = new EventHarness(3);
            for (int trial = 0; trial < 200; trial++)
            {
                var ctx = world.RandomContext();
                foreach (var line in world.Registry.Explain(ctx, EventCatalog.All, true))
                    Assert.False(string.IsNullOrEmpty(line.Reason), line.Def.Id);
            }
        }

        private static Dictionary<string, string> KeyedText()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var file = System.IO.Path.Combine(dir.FullName, "Languages", "English", "Keyed", "FamilyEvents.xml");
                if (System.IO.File.Exists(file))
                    return System.Xml.Linq.XDocument.Load(file).Root.Elements().ToDictionary(x => x.Name.LocalName, x => x.Value);
                dir = dir.Parent;
            }
            throw new System.IO.FileNotFoundException("FamilyEvents.xml not found above " + AppContext.BaseDirectory);
        }

        [Fact]
        public void Every_event_has_a_title_and_a_line_for_each_way_it_can_go()
        {
            var text = KeyedText();
            foreach (var e in EventCatalog.All)
            {
                string key = "WFL_Ev_" + e.Id.Replace('-', '_');
                foreach (var part in new[] { "Title", "Poor", "Flat", "Good", "Great" })
                    Assert.True(text.ContainsKey(key + "_" + part) && text[key + "_" + part].Length > 5, key + "_" + part);
                foreach (var part in new[] { "Poor", "Flat", "Good", "Great" })
                {
                    string line = text[key + "_" + part];
                    if (e.Id != "C3-10") Assert.Contains("{0}", line.Replace("{1}", "{0}"));    // it says who (the next-generation moment is about many children)
                    Assert.DoesNotContain("{3}", line);
                    if (!e.NeedsThird) Assert.DoesNotContain("{2}", line);              // a third person is only named where there is one
                }
            }
            Assert.Equal(90 * 5, text.Count);
        }
    }

    internal static class EventEngineHelpers
    {
        public static bool BucketFits(EventDef d, PairContext c) => FamilyRegistry.BucketAllows(d.Bucket, c);
    }
}
