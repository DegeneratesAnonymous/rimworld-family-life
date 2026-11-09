using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// The Families window's storytelling layer: how events are ranked and grouped, which situations become story threads, what the Current Chapter says
    /// and what a viewer is allowed to know. It reads the registry and changes nothing in it.
    /// </summary>
    public class StoryPresentationTests
    {
        private const int Day = 60000;

        // Strick (1) and Madeline (2, born a Garrick) with children Enrique (3) and Kelvin (4); Pat (9) is a colonist outside the family.
        private static FamilyRegistry Jessops()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Strick", last: "Jessop", spouses: new[] { 2 }), T.P(2, "Madeline", last: "Garrick", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }, last: "Jessop", name: "Enrique"), T.Kid(4, new[] { 1, 2 }, last: "Jessop", name: "Kelvin"),
                T.P(9, "Pat", last: "Northcott")
            }, 1000);
            r.UpdateBonds((a, b) => (int?)0, 1000, null);
            return r;
        }

        private static Lineage House(FamilyRegistry r) => r.Lineages.First(l => r.MembersOf(l.Id).Any(p => p.Id == 1));

        private static EventRecord Lesson(int teacher, int pupil, int tick, EventTier tier = EventTier.Good, int feelB = 40, int flags = 0) =>
            new EventRecord { EventId = "A2-04", A = teacher, B = pupil, Tick = tick, Tier = tier, Weight = EventWeight.Meaningful, FeelA = 30, FeelB = feelB, Aftermath = flags };

        private static TimelineEntry Entry(EventRecord e) => new TimelineEntry { Kind = TimelineKind.Moment, Tick = e.Tick, Category = EventCategory.Relationships, Moment = e };

        // ---- family, House and household are different counts

        [Fact]
        public void House_members_and_household_residents_are_counted_apart()
        {
            var r = Jessops();
            var house = House(r);
            var c = HouseCountsOf.Of(r, house);
            Assert.Equal(r.Persons.Count(p => p.InColony && r.BelongsToFamily(p, house.Id)), c.HouseMembers);
            Assert.True(c.Residents >= 1);
            Assert.True(c.Households >= 1);
            Assert.Equal(c.Residents != c.HouseMembers, c.Differ);
        }

        [Fact]
        public void A_resident_who_belongs_to_another_house_is_counted_as_a_resident_not_a_member()
        {
            var r = Jessops();
            var house = House(r);
            var members = new HashSet<int>(r.Persons.Where(p => r.BelongsToFamily(p, house.Id)).Select(p => p.Id));
            var c = HouseCountsOf.Of(r, house);
            var households = r.HouseholdsOfLineage(house.Id);
            var strangers = households.SelectMany(h => h.ResidentIds).Distinct().Count(id => !members.Contains(id) && r.GetPerson(id).InColony);
            Assert.Equal(strangers, c.ResidentsFromOtherHouses);
            Assert.Equal(c.HouseMembers + c.ResidentsFromOtherHouses - c.MembersLivingElsewhere, c.Residents);
        }

        [Fact]
        public void A_household_has_a_player_facing_name_never_an_id()
        {
            var r = Jessops();
            var h = r.HouseholdsOfLineage(House(r).Id).First();
            var n = HouseholdNames.NameOf(r, h);
            Assert.StartsWith("WFL_FW_HH_", n.Key);
            Assert.DoesNotContain(h.Id.ToString() + "}", n.Key);
            Assert.All(n.Args, a => Assert.False(int.TryParse(a, out _)));
            Assert.True(n.Args.Length >= 1 && n.Args.All(a => a.Length > 0));
        }

        [Fact]
        public void A_household_of_two_partners_from_different_houses_is_named_for_both()
        {
            var r = Jessops();
            var h = r.HouseholdsOfLineage(House(r).Id).First(x => x.ResidentIds.Contains(1));
            var n = HouseholdNames.NameOf(r, h);
            Assert.Equal("WFL_FW_HH_Pair", n.Key);
            Assert.Contains("Strick", n.Args); Assert.Contains("Madeline", n.Args);
        }

        // ---- importance

        [Fact]
        public void Importance_orders_defining_over_major_over_meaningful_over_routine()
        {
            var defining = new EventRecord { Weight = EventWeight.Defining };
            var major = new EventRecord { Weight = EventWeight.Meaningful, Aftermath = (int)AftermathFlags.RiftOpened };
            var meaningful = new EventRecord { Weight = EventWeight.Meaningful };
            var routine = new EventRecord { Weight = EventWeight.Ambient };
            Assert.True(StoryRelevance.Of(defining) > StoryRelevance.Of(major));
            Assert.True(StoryRelevance.Of(major) > StoryRelevance.Of(meaningful));
            Assert.True(StoryRelevance.Of(meaningful) > StoryRelevance.Of(routine));
        }

        [Fact]
        public void Births_deaths_and_marriages_are_defining_and_bond_drift_is_routine()
        {
            foreach (var k in new[] { FamilyEventKind.Birth, FamilyEventKind.Died, FamilyEventKind.MarriageFormed, FamilyEventKind.LineageFounded, FamilyEventKind.RiftResolved })
                Assert.Equal(StoryImportance.Defining, StoryRelevance.Of(new FamilyEvent { Kind = k }));
            foreach (var k in new[] { FamilyEventKind.BondDeepened, FamilyEventKind.HouseholdJoined, FamilyEventKind.JoinedColony })
                Assert.Equal(StoryImportance.Routine, StoryRelevance.Of(new FamilyEvent { Kind = k }));
        }

        // ---- consolidation

        [Fact]
        public void A_parent_teaching_two_children_is_one_scene()
        {
            var entries = new List<TimelineEntry> { Entry(Lesson(1, 4, 5000)), Entry(Lesson(1, 3, 4000)) };
            var beats = StoryConsolidation.Build(entries, false);
            var beat = Assert.Single(beats);
            Assert.True(beat.Grouped);
            Assert.Equal(2, beat.Parts.Count);
            Assert.Contains(3, beat.Others); Assert.Contains(4, beat.Others);
            Assert.True(beat.Teaching);
        }

        [Fact]
        public void Moments_that_went_differently_are_not_told_as_one()
        {
            var entries = new List<TimelineEntry> { Entry(Lesson(1, 4, 5000, EventTier.Poor, -60)), Entry(Lesson(1, 3, 4000, EventTier.Great, 70)) };
            Assert.Equal(2, StoryConsolidation.Build(entries, false).Count);
        }

        [Fact]
        public void A_moment_that_opened_a_rift_stays_on_its_own()
        {
            var entries = new List<TimelineEntry> { Entry(Lesson(1, 4, 5000, EventTier.Good, 40, (int)AftermathFlags.RiftOpened)), Entry(Lesson(1, 3, 4000)) };
            Assert.Equal(2, StoryConsolidation.Build(entries, false).Count);
        }

        [Fact]
        public void Moments_far_apart_or_from_different_people_are_not_grouped()
        {
            var far = new List<TimelineEntry> { Entry(Lesson(1, 4, 5000 + 3 * Day)), Entry(Lesson(1, 3, 4000)) };
            Assert.Equal(2, StoryConsolidation.Build(far, false).Count);
            var other = new List<TimelineEntry> { Entry(Lesson(1, 4, 5000)), Entry(Lesson(2, 3, 4000)) };
            Assert.Equal(2, StoryConsolidation.Build(other, false).Count);
        }

        [Fact]
        public void Routine_entries_are_folded_into_one_line_unless_asked_for()
        {
            var bond = new TimelineEntry { Kind = TimelineKind.Chronicle, Tick = 100, Chronicle = new FamilyEvent { Kind = FamilyEventKind.HouseholdJoined, Tick = 100 } };
            var hidden = StoryConsolidation.Build(new List<TimelineEntry> { bond, bond }, false);
            var line = Assert.Single(hidden);
            Assert.True(line.IsRoutineSummary); Assert.Equal(2, line.RoutineCount);
            Assert.Equal(2, StoryConsolidation.Build(new List<TimelineEntry> { bond, bond }, true).Count(b => !b.IsRoutineSummary));
        }

        [Fact]
        public void Highlights_rank_defining_first_and_never_lead_with_routine()
        {
            var routine = new StoryBeat { Importance = StoryImportance.Routine, Tick = 900 };
            var meaningful = new StoryBeat { Importance = StoryImportance.Meaningful, Tick = 800, Entry = new TimelineEntry() };
            var defining = new StoryBeat { Importance = StoryImportance.Defining, Tick = 100, Entry = new TimelineEntry() };
            var top = StoryConsolidation.Highlights(new[] { routine, meaningful, defining }, 3);
            Assert.Equal(2, top.Count);
            Assert.Same(defining, top[0]);
        }

        [Fact]
        public void The_chronicle_holds_only_defining_history()
        {
            var r = Jessops();
            var house = House(r);
            r.EventLog.Add(new EventRecord { EventId = "B1-01", A = 1, B = 3, Tick = 2000, Weight = EventWeight.Meaningful });
            r.EventLog.Add(new EventRecord { EventId = "B1-02", A = 1, B = 4, Tick = 3000, Weight = EventWeight.Defining, Tier = EventTier.Great });
            var chronicle = StoryTimeline.Chronicle(r, house, 0, 0);
            Assert.All(chronicle.Where(b => !b.IsRoutineSummary), b => Assert.Equal(StoryImportance.Defining, b.Importance));
            Assert.Contains(chronicle, b => b.Parts.Any(p => p.Tick == 3000));
            Assert.DoesNotContain(chronicle, b => b.Parts.Any(p => p.Tick == 2000));
        }

        // ---- threads and the Current Chapter

        [Fact]
        public void An_assessed_ambition_becomes_a_thread_with_its_reason()
        {
            var r = Jessops();
            var house = House(r);
            house.Situation.EvaluatedTick = 5; house.Situation.Influence = 20f; house.Situation.Cohesion = 70f;
            house.Ambition.Primary = HouseAmbitionKind.IncreaseInfluence; house.Ambition.PrimaryReason = HouseNeed.Influence;
            var m = StoryModel.Build(r, house, 0, 10000);
            var t = Assert.Single(m.Threads, x => x.Kind == ThreadKind.Ambition);
            Assert.Equal("WFL_FW_Amb_IncreaseInfluence_Title", t.Title.Key);
            Assert.Equal("WFL_FW_Why_Influence", t.Why.Key);
            Assert.NotEmpty(t.Paths); Assert.NotEmpty(t.Actions);
        }

        [Fact]
        public void An_open_rift_is_a_thread_that_leads_the_chapter_when_serious()
        {
            var r = Jessops();
            var house = House(r);
            r.OpenRift(1, 2, RiftCause.Tension, 80f, 5000);
            var m = StoryModel.Build(r, house, 0, 10000);
            var rift = Assert.Single(m.Threads, x => x.Kind == ThreadKind.Rift);
            Assert.Equal(1, rift.Priority);
            Assert.Same(rift, m.Chapter.Lead);
            Assert.Equal(rift.Title.Key, m.Chapter.Title.Key);
        }

        [Fact]
        public void A_promise_coming_due_is_a_thread_for_the_people_who_made_it()
        {
            var r = Jessops();
            var house = House(r);
            r.MakePromise(1, 3, "B1-01", 1000, 1, 1);
            var m = StoryModel.Build(r, house, 0, 1000 + Day);
            Assert.Contains(m.Threads, t => t.Kind == ThreadKind.Promise && t.People.Contains(1) && t.People.Contains(3));
        }

        [Fact]
        public void Low_food_is_a_thread_with_a_reason()
        {
            var r = Jessops();
            var house = House(r);
            house.Situation.EvaluatedTick = 5; house.Situation.Sustenance = 10f;
            var m = StoryModel.Build(r, house, 0, 10000);
            var food = Assert.Single(m.Threads, t => t.Kind == ThreadKind.Food);
            Assert.Equal(6, food.Priority);
            Assert.Equal("WFL_FW_Th_Food_Why", food.Why.Key);
            Assert.Equal("10", food.Why.Args[0]);
        }

        [Fact]
        public void Threads_are_ordered_crisis_first()
        {
            var r = Jessops();
            var house = House(r);
            house.Situation.EvaluatedTick = 5; house.Situation.Sustenance = 10f; house.Situation.Cohesion = 60f;
            house.Ambition.Primary = HouseAmbitionKind.SecureProvisions; house.Ambition.PrimaryReason = HouseNeed.Sustenance;
            r.OpenRift(1, 2, RiftCause.Tension, 80f, 5000);
            var priorities = StoryModel.Build(r, house, 0, 10000).Threads.Select(t => t.Priority).ToList();
            Assert.Equal(priorities.OrderBy(p => p).ToList(), priorities);
            Assert.Equal(1, priorities[0]);
        }

        [Fact]
        public void Nothing_pressing_gives_a_quiet_positive_chapter()
        {
            var r = Jessops();
            var house = House(r);
            var m = StoryModel.Build(r, house, 0, 10000);
            Assert.True(m.Chapter.Quiet);
            Assert.Equal("WFL_FW_Ch_QuietTitle", m.Chapter.Title.Key);
            Assert.Contains(m.Chapter.Body, b => b.Key == "WFL_FW_Ch_Quiet");
        }

        [Fact]
        public void An_ordinary_teaching_scene_shows_in_the_chapter_and_a_thread()
        {
            var r = Jessops();
            var house = House(r);
            r.EventLog.Add(Lesson(1, 3, 9000)); r.EventLog.Add(Lesson(1, 4, 9100));
            var m = StoryModel.Build(r, house, 0, 10000);
            Assert.Contains(m.Threads, t => t.Kind == ThreadKind.Teaching);
            Assert.Contains(m.Chapter.Body, b => b.Key == "WFL_FW_Clause_Teaching");
        }

        // ---- who may know what

        [Fact]
        public void A_viewer_outside_the_house_is_told_it_is_unknown_and_sees_no_threads()
        {
            var r = Jessops();
            var house = House(r);
            house.Situation.EvaluatedTick = 5; house.Situation.Sustenance = 5f;
            r.OpenRift(1, 2, RiftCause.Tension, 80f, 5000);
            var m = StoryModel.Build(r, house, 9, 10000);
            Assert.Empty(m.Threads);
            Assert.True(m.Chapter.Unknown);
            Assert.DoesNotContain(m.Chapter.Body, b => b.Key.Contains("Rift") || b.Key.Contains("Food"));
        }

        [Fact]
        public void A_private_rift_between_two_people_is_not_in_a_third_persons_story()
        {
            var r = Jessops();
            var house = House(r);
            r.OpenRift(1, 2, RiftCause.Tension, 80f, 5000);
            var asKid = StoryModel.Build(r, house, 3, 10000);
            Assert.DoesNotContain(asKid.Threads, t => t.Kind == ThreadKind.Rift);
            var asStrick = StoryModel.Build(r, house, 1, 10000);
            Assert.Contains(asStrick.Threads, t => t.Kind == ThreadKind.Rift);
        }

        [Fact]
        public void The_acting_pawn_is_addressed_as_you_and_the_overview_names_them()
        {
            var r = Jessops();
            var house = House(r);
            r.EventLog.Add(Lesson(1, 3, 9000));
            var asStrick = StoryModel.Build(r, house, 1, 10000).Threads.First(t => t.Kind == ThreadKind.Teaching);
            Assert.EndsWith("_You", asStrick.Summary.Key);
            var overview = StoryModel.Build(r, house, 0, 10000).Threads.First(t => t.Kind == ThreadKind.Teaching);
            Assert.DoesNotContain("_You", overview.Summary.Key);
            Assert.Equal("Strick", overview.Summary.Args[0]);
        }

        [Fact]
        public void A_missing_or_discarded_person_is_an_empty_argument_not_a_crash()
        {
            var r = Jessops();
            var house = House(r);
            r.EventLog.Add(Lesson(1, 777, 9000));
            var m = StoryModel.Build(r, house, 0, 10000);
            Assert.NotNull(m);
            Assert.Equal("", HouseholdNames.Short(null));
        }

        [Fact]
        public void Building_the_story_changes_nothing_in_the_records()
        {
            var r = Jessops();
            var house = House(r);
            r.OpenRift(1, 2, RiftCause.Tension, 80f, 5000);
            r.EventLog.Add(Lesson(1, 3, 9000));
            int log = r.EventLog.Count, promises = r.Commitments.Count, chronicle = r.Chronicle.Count, rifts = r.OpenRiftsOf(1).Count;
            float cohesion = house.Situation.Cohesion;
            StoryModel.Build(r, house, 0, 10000); StoryModel.Build(r, house, 1, 10000); StoryModel.Build(r, house, 9, 10000);
            Assert.Equal(log, r.EventLog.Count); Assert.Equal(promises, r.Commitments.Count);
            Assert.Equal(chronicle, r.Chronicle.Count); Assert.Equal(rifts, r.OpenRiftsOf(1).Count);
            Assert.Equal(cohesion, house.Situation.Cohesion);
        }

        [Fact]
        public void The_same_records_give_the_same_story_every_time()
        {
            var r = Jessops();
            var house = House(r);
            house.Situation.EvaluatedTick = 5; house.Situation.Sustenance = 10f;
            r.EventLog.Add(Lesson(1, 3, 9000));
            string Sig(StoryModel m) => string.Join("|", m.Threads.Select(t => t.Id + ":" + t.Title.Key)) + "#" + m.Chapter.Title.Key;
            Assert.Equal(Sig(StoryModel.Build(r, house, 0, 10000)), Sig(StoryModel.Build(r, house, 0, 10000)));
        }

        // ---- layout

        [Theory]
        [InlineData(1200f, 3)]
        [InlineData(1040f, 3)]
        [InlineData(800f, 2)]
        [InlineData(600f, 1)]
        public void The_dashboard_collapses_from_three_columns_to_two_to_one(float width, int columns) =>
            Assert.Equal(columns, WindowLayout.DashboardColumns(width));

        [Fact]
        public void Cards_stay_within_their_width_limits_and_extra_room_becomes_columns()
        {
            foreach (float width in new[] { 300f, 500f, 900f, 1600f, 2400f })
            {
                int cols = WindowLayout.CardColumns(width);
                float w = WindowLayout.CardWidth(width, cols);
                Assert.True(w <= WindowLayout.MaxCard + 0.01f, width + " -> " + w);
                Assert.True(w >= Math.Min(WindowLayout.MinCard, width) - 0.01f, width + " -> " + w);
                Assert.True(cols * w + (cols - 1) * WindowLayout.Gap <= width + 0.5f);
            }
            Assert.True(WindowLayout.CardColumns(2000f) > WindowLayout.CardColumns(700f));
        }

        [Fact]
        public void Prose_lists_sidebar_drawer_and_tabs_have_readable_limits()
        {
            Assert.Equal(WindowLayout.ProseMax, WindowLayout.ProseWidth(2000f));
            Assert.Equal(400f, WindowLayout.ProseWidth(400f));
            Assert.InRange(WindowLayout.SidebarWidth(1900f), 210f, 240f);
            Assert.InRange(WindowLayout.DrawerWidth(1700f), 360f, 440f);
            Assert.True(WindowLayout.DrawerBeside(1200f)); Assert.False(WindowLayout.DrawerBeside(700f));
            Assert.True(WindowLayout.ListWidth(1700f, true) <= 620f);
            Assert.Equal(150f, WindowLayout.TabWidth(1700f, 6));
            Assert.True(WindowLayout.TabWidth(600f, 6) <= 100.01f);
            Assert.Equal(4, WindowLayout.OfficeColumns(1700f));
        }

        // ---- the words

        [Fact]
        public void Every_story_key_the_layer_can_produce_exists_in_the_language_file()
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "Languages", "English", "Keyed", "FamilyStory.xml"));
            var r = Jessops();
            var house = House(r);
            house.Situation.EvaluatedTick = 5; house.Situation.Sustenance = 10f; house.Situation.LivingSpace = 10f; house.Situation.Safety = 10f; house.Situation.Capability = 10f;
            house.Situation.WeakestCapability = HouseCapability.Cooking; house.Situation.Cohesion = 70f;
            r.OpenRift(1, 2, RiftCause.Tension, 80f, 5000);
            r.MakePromise(1, 3, "B1-01", 1000, 1, 1);
            r.EventLog.Add(Lesson(1, 3, 9000));
            foreach (HouseAmbitionKind kind in Enum.GetValues(typeof(HouseAmbitionKind)))
            {
                if (kind == HouseAmbitionKind.None) continue;
                house.Ambition.Primary = kind; house.Ambition.PrimaryReason = HouseNeed.Influence;
                var m = StoryModel.Build(r, house, 0, 1000 + Day);
                foreach (var n in KeysOf(m)) Assert.True(text.Contains("<" + n + ">") || OtherFile(n), "missing " + n);
            }
        }

        [Fact]
        public void Every_literal_translation_key_used_by_the_window_exists()
        {
            var keys = new System.Text.StringBuilder();
            foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(RepoRoot(), "Languages", "English", "Keyed"), "*.xml")) keys.Append(System.IO.File.ReadAllText(f));
            var all = keys.ToString();
            var missing = new List<string>();
            foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(RepoRoot(), "Source", "FamilyLife", "UI"), "*.cs"))
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(System.IO.File.ReadAllText(f), "\"(WFL_[A-Za-z0-9_]+)\"[.]Translate"))
                    if (!all.Contains("<" + m.Groups[1].Value + ">")) missing.Add(System.IO.Path.GetFileName(f) + ": " + m.Groups[1].Value);
            Assert.True(missing.Count == 0, string.Join(", ", missing.Distinct()));
        }

        private static IEnumerable<string> KeysOf(StoryModel m)
        {
            foreach (var t in m.Threads)
                foreach (var n in new[] { t.Title, t.Summary, t.Line, t.Why, t.Recent, t.State }.Concat(t.Paths).Concat(t.Actions))
                    if (!n.Empty) yield return n.Key;
            foreach (var n in m.Chapter.Body.Concat(m.Chapter.Supporting)) if (!n.Empty) yield return n.Key;
            if (!m.Chapter.Title.Empty) yield return m.Chapter.Title.Key;
        }

        private static bool OtherFile(string key)
        {
            foreach (var f in System.IO.Directory.GetFiles(System.IO.Path.Combine(RepoRoot(), "Languages", "English", "Keyed"), "*.xml"))
                if (System.IO.File.ReadAllText(f).Contains("<" + key + ">")) return true;
            return false;
        }

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Languages"))) dir = dir.Parent;
            return dir?.FullName;
        }
    }
}
