using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>The same moment means different things to different people. These tests pin that for Strick, Ivan and Debby.</summary>
    public class StoryPolarityTests
    {
        private const int Strick = 1, Ivan = 10, Debby = 11, Sally = 12;
        private const string Friendship = "B1-01", Invitation = "C1-08", Defending = "A1-08";

        private static FamilyRegistry World()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(Strick, "Strick", last: "Jessop"),
                T.P(Ivan, "Ivan", last: "Northcott", spouses: new[] { Debby }), T.P(Debby, "Debby", last: "Northcott", spouses: new[] { Ivan }),
                T.Kid(Sally, new[] { Ivan, Debby }, last: "Northcott", name: "Sally")
            }, 1000);
            r.UpdateBonds((a, b) => (int?)0, 1000, null);
            return r;
        }

        private static Lineage Northcott(FamilyRegistry r) => r.Lineages.First(l => r.MembersOf(l.Id).Any(p => p.Id == Ivan));

        private static EventRecord Ev(string id, int a, int b, int tick, EventTier tier, int third = 0, int feelA = 0, int feelB = 0) =>
            new EventRecord { EventId = id, A = a, B = b, Third = third, Tick = tick, Tier = tier, Weight = EventWeight.Meaningful, FeelA = feelA, FeelB = feelB };

        private static void StricksBadDay(FamilyRegistry r)
        {
            r.EventLog.Add(Ev(Friendship, Debby, Strick, 1000, EventTier.Poor, 0, -40, -60));      // Debby's attempt at friendship ended badly
            r.EventLog.Add(Ev(Invitation, Ivan, Strick, 2000, EventTier.Flat));                     // Strick accepted Ivan's invitation, reluctantly: nothing changed
            r.EventLog.Add(Ev(Defending, Ivan, Debby, 3000, EventTier.Good, Strick, 50, 60));       // Ivan defended Debby against Strick
        }

        [Fact]
        public void Ivan_defending_Debby_is_good_for_them_and_bad_for_Strick()
        {
            var e = Ev(Defending, Ivan, Debby, 3000, EventTier.Good, Strick, 50, 60);
            var asStrick = StoryValence.Of(e, Strick);
            Assert.Equal(-1, asStrick.OnViewer);
            Assert.True(asStrick.Between > 0);
            Assert.Equal(1, StoryValence.Of(e, Ivan).OnViewer);
            Assert.Equal(1, StoryValence.Of(e, Debby).OnViewer);
        }

        [Fact]
        public void The_same_scene_has_a_different_colour_for_a_different_perspective()
        {
            var parts = new[] { Ev(Defending, Ivan, Debby, 3000, EventTier.Good, Strick, 50, 60) };
            Assert.Equal(-1, StoryValence.ForBeat(parts, Strick));      // red for Strick
            Assert.Equal(1, StoryValence.ForBeat(parts, Ivan));         // green for Ivan
            Assert.Equal(1, StoryValence.ForBeat(parts, 0));            // green between the pair, in the overview
        }

        [Fact]
        public void A_failed_friendship_is_negative_and_a_reluctant_acceptance_is_neutral()
        {
            Assert.Equal(-1, StoryValence.Of(Ev(Friendship, Debby, Strick, 1, EventTier.Poor, 0, -40, -60), Strick).OnViewer);
            Assert.Equal(0, StoryValence.Of(Ev(Invitation, Ivan, Strick, 1, EventTier.Flat), Strick).OnViewer);
            Assert.Equal(0, StoryValence.Of(Ev(Invitation, Ivan, Strick, 1, EventTier.Flat), Strick).ForPerspective);
        }

        [Fact]
        public void A_moment_the_viewer_was_not_part_of_has_no_effect_on_them()
        {
            var v = StoryValence.Of(Ev(Friendship, Ivan, Debby, 1, EventTier.Great), Strick);
            Assert.False(v.ViewerInvolved); Assert.Equal(0, v.OnViewer);
        }

        [Fact]
        public void Strick_sees_his_dealings_with_the_Northcotts_as_uneasy_not_warm()
        {
            var r = World(); var house = Northcott(r);
            StricksBadDay(r);
            var m = StoryModel.Build(r, house, Strick, 10000);
            Assert.Equal("WFL_FW_Ch_Dealings_Uneasy_Title", m.Chapter.Title.Key);
            Assert.DoesNotContain(m.Chapter.Body, b => b.Key.Contains("_Warm"));
            // The reasons shown are the ones that explain the tone: the neutral moment is not offered as a reason for unease.
            Assert.Equal(2, m.Chapter.Evidence.Count);
            Assert.Contains(m.Chapter.Evidence, n => n.Key == "WFL_FW_Evid_Against");
            Assert.DoesNotContain(m.Chapter.Evidence, n => n.Key == "WFL_FW_Evid_Neutral");
            Assert.Contains(m.Chapter.Evidence, n => n.Key == "WFL_FW_Evid_Bad");
        }

        [Fact]
        public void Summary_story_and_relationships_agree_about_the_same_events()
        {
            var r = World(); var house = Northcott(r);
            StricksBadDay(r);
            var history = Knowledge.HistoryWith(r, Strick, house);
            string tone = RelationshipWords.ToneOf(history, Strick);
            Assert.Equal("Uneasy", tone);
            var debby = RelationshipWords.With(r, Strick, Debby, new RelRow { Familiarity = 5f }, history);
            var ivan = RelationshipWords.With(r, Strick, Ivan, new RelRow { Familiarity = 5f }, history);
            var sally = RelationshipWords.With(r, Strick, Sally, new RelRow { Familiarity = 5f }, history);
            Assert.Equal("Strained", debby);
            Assert.Equal("Uneasy", ivan);
            Assert.Equal("Unfamiliar", sally);            // no shared moments: still a stranger
            Assert.NotEqual("Warm", debby); Assert.NotEqual("Warm", ivan);
        }

        [Fact]
        public void A_known_person_is_not_called_unfamiliar_only_because_the_exact_values_are_private()
        {
            var r = World(); var house = Northcott(r);
            StricksBadDay(r);
            var history = Knowledge.HistoryWith(r, Strick, house);
            Assert.NotEqual("Unfamiliar", RelationshipWords.With(r, Strick, Ivan, new RelRow { Familiarity = 0f, Tension = 0f }, history));
        }

        [Fact]
        public void Warm_dealings_still_read_as_warm()
        {
            var r = World(); var house = Northcott(r);
            r.EventLog.Add(Ev(Friendship, Debby, Strick, 1000, EventTier.Great, 0, 60, 70));
            r.EventLog.Add(Ev(Friendship, Ivan, Strick, 2000, EventTier.Good, 0, 40, 50));
            Assert.Equal("WFL_FW_Ch_Dealings_Warm_Title", StoryModel.Build(r, house, Strick, 10000).Chapter.Title.Key);
        }

        // ---- threads

        [Fact]
        public void A_capability_gap_and_the_ambition_to_fill_it_are_one_thread()
        {
            var r = World(); var house = Northcott(r);
            house.Situation.EvaluatedTick = 5; house.Situation.Capability = 9f; house.Situation.WeakestCapability = HouseCapability.Cooking; house.Situation.Cohesion = 60f;
            house.Ambition.Primary = HouseAmbitionKind.FillSkillGap; house.Ambition.PrimaryReason = HouseNeed.Capability;
            var threads = StoryModel.Build(r, house, Ivan, 10000).Threads;
            var gap = Assert.Single(threads, t => t.Id.StartsWith("capability_gap:"));
            Assert.Equal("capability_gap:cooking", gap.Id);
            Assert.Contains(gap.Facts, f => f.Key == "WFL_FW_Fact_AmbitionGap");
            Assert.DoesNotContain(threads, t => t.Kind == ThreadKind.Ambition);
            Assert.Equal("WFL_FW_CapTitle_Cooking", gap.Title.Key);
            Assert.Equal("WFL_FW_Detail_Score", gap.Detail.Key);      // the number is in the tooltip, not the prose
        }

        [Fact]
        public void A_meaningful_event_with_no_lasting_state_is_not_a_thread()
        {
            var r = World(); var house = Northcott(r);
            r.EventLog.Add(Ev(Friendship, Ivan, Debby, 9000, EventTier.Great, 0, 70, 70));
            var threads = StoryModel.Build(r, house, Ivan, 10000).Threads;
            Assert.DoesNotContain(threads, t => t.Id.StartsWith("turning"));
            Assert.Empty(threads);
            Assert.NotEmpty(StoryModel.Build(r, house, Ivan, 10000).Recent);        // it is in the timeline
        }

        // ---- suggestions

        private static StoryThread Thread(params string[] actions) => new StoryThread { Id = "t", People = { Debby, Ivan }, Actions = actions.Select(a => new Narrative(a)).ToList() };

        [Fact]
        public void An_apology_is_suggested_only_where_there_is_something_to_apologise_for()
        {
            var r = World(); var house = Northcott(r);
            var none = new List<StoryThread> { Thread("WFL_FW_Act_Apologize") };
            ActionAdvice.Apply(r, house, Strick, none, null, new List<EventRecord>());
            Assert.Empty(none[0].Actions);
            StricksBadDay(r);
            var some = new List<StoryThread> { Thread("WFL_FW_Act_Apologize") };
            ActionAdvice.Apply(r, house, Strick, some, null, Knowledge.HistoryWith(r, Strick, house));
            var apology = Assert.Single(some[0].Actions);
            Assert.Equal("WFL_FW_Act_ApologizeTo", apology.Key);
            Assert.Equal("Debby", apology.Args[0]);
        }

        [Fact]
        public void Time_together_names_a_person_and_the_overview_keeps_general_suggestions()
        {
            var r = World(); var house = Northcott(r);
            var named = new List<StoryThread> { Thread("WFL_FW_Act_TimeTogether") };
            ActionAdvice.Apply(r, house, Strick, named, null, new List<EventRecord>());
            Assert.Equal("WFL_FW_Act_TimeWith", named[0].Actions[0].Key);
            var overview = new List<StoryThread> { Thread("WFL_FW_Act_TimeTogether") };
            ActionAdvice.Apply(r, house, 0, overview, null, new List<EventRecord>());
            Assert.Equal("WFL_FW_Act_TimeTogether", overview[0].Actions[0].Key);
        }

        [Fact]
        public void Teaching_and_learning_are_suggested_only_when_the_skill_makes_them_possible()
        {
            var r = World(); var house = Northcott(r);
            house.Situation.WeakestCapability = HouseCapability.Cooking;
            List<StoryThread> Run(int viewer, SkillProbe probe) { var l = new List<StoryThread> { Thread("WFL_FW_Th_Capability_Act1") }; ActionAdvice.Apply(r, house, viewer, l, probe, new List<EventRecord>()); return l; }
            // Ivan cooks well, Debby does not: Ivan can teach Debby.
            var teach = Run(Ivan, (id, c) => id == Ivan ? 12 : 3);
            Assert.Contains(teach[0].Actions, a => a.Key == "WFL_FW_Act_Teach" && a.Args[0] == "Debby");
            // Debby cooks badly and Ivan well: she can learn from him, not teach.
            var learn = Run(Debby, (id, c) => id == Ivan ? 12 : 3);
            Assert.Contains(learn[0].Actions, a => a.Key == "WFL_FW_Act_Learn");
            Assert.DoesNotContain(learn[0].Actions, a => a.Key == "WFL_FW_Act_Teach");
            // Nobody can cook and the pawn cannot cook at all: nothing is suggested.
            Assert.Empty(Run(Ivan, (id, c) => -1)[0].Actions);
            // No skill information at all: nothing guessed.
            Assert.Empty(Run(Ivan, null)[0].Actions);
            // A skilled colonist outside the House can be befriended.
            var befriend = Run(Ivan, (id, c) => id == Strick ? 14 : 2);
            Assert.Contains(befriend[0].Actions, a => a.Key == "WFL_FW_Act_Befriend" && a.Args[0] == "Strick");
        }

        // ---- names, grammar, beds, layout

        [Fact]
        public void One_short_name_is_used_for_a_person_everywhere()
        {
            var p = new PersonRecord { Id = 1, Name = "Northcott", FirstName = "Ivan", LastName = "Northcott" };
            Assert.Equal("Ivan", HouseholdNames.Short(p));
            Assert.Equal("Ivan Northcott", p.FullName);
            Assert.Equal("Tiffy", HouseholdNames.Short(new PersonRecord { Name = "Tiffy", FirstName = "" }));
        }

        [Fact]
        public void Family_ties_read_as_sentences_about_both_people()
        {
            var r = World();
            Assert.Equal("WFL_FW_RelP_ParentOf", RelationPhrase.Of(r, Ivan, Sally).Key);
            Assert.Equal(new[] { "Ivan", "Sally" }, RelationPhrase.Of(r, Ivan, Sally).Args);
            Assert.Equal(new[] { "Debby", "Sally" }, RelationPhrase.Of(r, Sally, Debby).Args);     // order does not matter: the parent comes first
            Assert.Equal("WFL_FW_RelP_Partners", RelationPhrase.Of(r, Ivan, Debby).Key);
            Assert.Equal("WFL_FW_RelP_Known", RelationPhrase.Of(r, Strick, Ivan).Key);
        }

        [Fact]
        public void A_double_bed_is_one_bed_and_two_sleeping_spots()
        {
            var c = SleepCounts.Of(new[]
            {
                new BedObject { ThingId = 1, Slots = 2, OwnerId = 10, Kind = BedKind.Bed },
                new BedObject { ThingId = 1, Slots = 2, OwnerId = 11, Kind = BedKind.Bed },     // the partner shares it
                new BedObject { ThingId = 2, Slots = 1, OwnerId = 12, Kind = BedKind.Bed },
                new BedObject { ThingId = 3, Slots = 1, OwnerId = 13, Kind = BedKind.Bed }
            });
            Assert.Equal(3, c.Beds); Assert.Equal(4, c.SleepingSpots);
        }

        [Fact]
        public void Cribs_bedrolls_medical_and_unassigned_beds_are_counted_for_what_they_are()
        {
            var c = SleepCounts.Of(new[]
            {
                new BedObject { ThingId = 1, Slots = 1, OwnerId = 5, Kind = BedKind.Crib },
                new BedObject { ThingId = 2, Slots = 1, OwnerId = 6, Kind = BedKind.Bedroll },
                new BedObject { ThingId = 3, Slots = 1, OwnerId = 0, Kind = BedKind.Bed },
                new BedObject { ThingId = 4, Slots = 1, OwnerId = 0, Kind = BedKind.Medical },
                new BedObject { ThingId = 5, Slots = 0, OwnerId = 7, Kind = BedKind.Bed }
            });
            Assert.Equal(4, c.Beds); Assert.Equal(1, c.MedicalBeds); Assert.Equal(1, c.Unassigned); Assert.Equal(4, c.SleepingSpots);
        }

        [Fact]
        public void House_affairs_uses_two_columns_when_there_is_room()
        {
            Assert.Equal(2, HouseAffairsLayout.Columns(1400f)); Assert.Equal(1, HouseAffairsLayout.Columns(800f));
            Assert.True(HouseAffairsLayout.ColumnWidth(1400f, 2) <= HouseAffairsLayout.ColumnMax);
            Assert.Equal(700f, HouseAffairsLayout.ColumnWidth(700f, 1) > 560f ? 700f : 700f);
            Assert.True(2 * HouseAffairsLayout.ColumnWidth(1400f, 2) + WindowLayout.Gap <= 1400f);
        }

        [Fact]
        public void Every_new_story_key_exists_in_the_language_file()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Languages"))) dir = dir.Parent;
            var all = string.Concat(System.IO.Directory.GetFiles(System.IO.Path.Combine(dir.FullName, "Languages", "English", "Keyed"), "*.xml").Select(System.IO.File.ReadAllText));
            foreach (var key in new[] { "WFL_FW_Evid_Bad", "WFL_FW_Evid_Against", "WFL_FW_ChapterWhy", "WFL_FW_CapTitle_Cooking", "WFL_FW_CapSum_Animals", "WFL_FW_Act_Teach", "WFL_FW_Act_ApologizeTo",
                "WFL_FW_RelP_ParentOf", "WFL_FW_Unit_Bed_1", "WFL_FW_Unit_SleepingSpot_N", "WFL_FW_Unit_HouseMember_1", "WFL_FW_Unit_Matter_N", "WFL_FW_Fact_AmbitionGap", "WFL_FW_Detail_Score" })
                Assert.Contains("<" + key + ">", all);
        }
    }
}
