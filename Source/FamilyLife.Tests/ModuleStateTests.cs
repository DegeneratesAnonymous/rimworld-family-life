using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class ModuleStateTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry Colony()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Doc"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }), T.P(4, "Four", parents: new[] { 1 }), T.P(20, "Solo", last: "Other"), T.Kid(30, new[] { 1 }) }, 1000);
            return r;
        }

        private static CommunityOffice Post(FamilyRegistry r, OfficeKind kind, int holder)
        {
            var o = new CommunityOffice { Id = (int)kind + 1, Kind = kind, HolderId = holder, HolderFamily = r.FamilyOf(holder), ElectedTick = 5000 };
            r.Offices.Add(o);
            return o;
        }

        // ---------------------------------------------------------------- the text format

        [Fact]
        public void The_state_format_round_trips_every_kind_of_value_including_awkward_text()
        {
            var w = new StateWriter().Int("a", -42).Bool("b", true).Float("c", 0.1234567f).Ints("d", new[] { 1, -2, 3 })
                .Strings("e", new[] { "plain", "has,comma", "line\nbreak", "back\\slash", "", "tab\tand \"quotes\" and ünïcode ✓" });
            string text = w.ToString();
            var r = new StateReader(text);
            Assert.Equal(-42, r.Int("a")); Assert.True(r.Bool("b")); Assert.Equal(0.1234567f, r.Float("c"));
            Assert.Equal(new[] { 1, -2, 3 }, r.Ints("d").ToArray());
            Assert.Equal(new[] { "plain", "has,comma", "line\nbreak", "back\\slash", "", "tab\tand \"quotes\" and ünïcode ✓" }, r.Strings("e").ToArray());
            // defaults are not written, and a missing key reads as its default
            var empty = new StateWriter().Int("x", 0).Bool("y", false).Float("z", 0f).Ints("l", new List<int>()).ToString();
            Assert.Equal("", empty);
            var none = new StateReader("");
            Assert.Equal(0, none.Int("a")); Assert.False(none.Bool("b")); Assert.Empty(none.Ints("d")); Assert.Empty(none.Strings("e")); Assert.Equal(7, none.Int("a", 7));
        }

        [Fact]
        public void A_damaged_payload_reads_as_defaults_rather_than_throwing()
        {
            var r = new StateReader("garbage\nnoequals\na=notanumber\nb=1,x,3\n=empty");
            Assert.Equal(0, r.Int("a")); Assert.Equal(new[] { 1, 3 }, r.Ints("b").ToArray()); Assert.False(r.Has("garbage"));
        }

        // ---------------------------------------------------------------- module state lives in payloads, not in the shared record

        [Fact]
        public void The_doctors_state_round_trips_through_a_save_and_a_load()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Healer, 1);
            var c = o.Care();
            c.RoundStartTick = 7000; c.RoundWindow = 20 * Day; c.IllnessRound = true; c.Examined.AddRange(new[] { 2, 3 }); c.Missed.Add(4); c.Refusals.Add(TestRefusals.Make(20, 1, CareRefusalKind.HealerSpecific, 6000));
            c.RoutineDay = 3; c.RoutineUsed = 2; c.LastVisitTick = 9000; c.KinSeen = 4; c.OtherSeen = 5; c.KinQuality = 9.5f; c.OtherQuality = 7f; c.LastIllnessTick = 1234;
            var back = SaveSim.Reload(r).OfficeFor(OfficeKind.Healer).Care();
            Assert.Equal(7000, back.RoundStartTick); Assert.Equal(20 * Day, back.RoundWindow); Assert.True(back.IllnessRound);
            Assert.Equal(new[] { 2, 3 }, back.Examined.ToArray()); Assert.Equal(new[] { 4 }, back.Missed.ToArray()); Assert.Equal(new[] { 20 }, back.Refusals.Select(x => x.Patient).ToArray()); Assert.Equal(6000, back.Refusals.Single().Tick); Assert.Equal(CareRefusalKind.HealerSpecific, back.Refusals.Single().Kind); Assert.Equal(1, back.Refusals.Single().Healer);
            Assert.Equal(2, back.RoutineUsed); Assert.Equal(9000, back.LastVisitTick); Assert.Equal(9.5f, back.KinQuality); Assert.Equal(5, back.OtherSeen); Assert.Equal(1234, back.LastIllnessTick);
        }

        [Fact]
        public void An_assessment_in_progress_survives_a_save_and_a_load_without_repeating_anything()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Teacher, 1);
            var a = o.Assessment();
            a.TestingStartTick = 8000; a.TestingSlot = 2; a.TestIds.AddRange(new[] { 30, 4 }); a.TestPass.AddRange(new[] { 0, 0 }); a.TestTry.AddRange(new[] { 2, 1 }); a.TestScore.AddRange(new[] { 5, -3 }); a.TestExcused.AddRange(new[] { 0, 1 });
            var e = o.Education();
            e.LessonLog.Add(new LessonRecord { Pupil = 30, Skill = "Cooking", Xp = 100f, Ticks = 4000, Possible = 5000, Classes = 1 }.Encode()); e.BookUse.Add("30|77|5000"); e.RecentSubjects.Add("Cooking"); e.LastTestingYear = 2027;
            var reloaded = SaveSim.Reload(r);
            var back = reloaded.OfficeFor(OfficeKind.Teacher);
            Assert.Equal(8000, back.Assessment().TestingStartTick); Assert.Equal(2, back.Assessment().TestingSlot);
            Assert.Equal(new[] { 30, 4 }, back.Assessment().TestIds.ToArray()); Assert.Equal(new[] { 2, 1 }, back.Assessment().TestTry.ToArray()); Assert.Equal(new[] { 5, -3 }, back.Assessment().TestScore.ToArray()); Assert.Equal(new[] { 0, 1 }, back.Assessment().TestExcused.ToArray());
            Assert.Single(back.Education().LessonLog); Assert.Equal("Cooking", back.Education().RecentSubjects[0]); Assert.Equal(2027, back.Education().LastTestingYear);
            // the book that already gave this child its experience still blocks a second time after the reload
            Assert.False(LessonMath.BookReady(back.Education().BookUse, 30, 77, 5000 + 2 * Day));
            Assert.Single(back.Education().BookUse);
        }

        [Fact]
        public void A_new_holder_starts_fresh_for_per_holder_state_but_what_the_children_studied_stays()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Teacher, 1);
            o.Assessment().TestingStartTick = 9000; o.Assessment().TestIds.Add(30);
            o.Education().LessonLog.Add(new LessonRecord { Pupil = 30, Skill = "Cooking", Xp = 100f, Ticks = 4000, Possible = 5000, Classes = 1 }.Encode()); o.Education().BookUse.Add("30|77|5000");
            Assert.True(r.ResignOffice(OfficeKind.Teacher, 1, 20000));
            o.HolderId = 3; o.HolderFamily = r.FamilyOf(3);          // someone else takes the post
            Assert.Equal(0, o.Assessment().TestingStartTick); Assert.Empty(o.Assessment().TestIds);          // the old teacher's assessment is not carried over
            Assert.Single(o.Education().LessonLog); Assert.Single(o.Education().BookUse);                    // the children's record is
            var healer = Post(r, OfficeKind.Healer, 1);
            healer.Care().Examined.Add(2); healer.Care().Refusals.Add(TestRefusals.Make(20, 0, CareRefusalKind.ServiceWide, 1)); healer.Care().KinSeen = 5; healer.Care().RoutineUsed = 3;
            healer.HolderId = 4; healer.HolderFamily = r.FamilyOf(4);
            Assert.Equal(new[] { 2 }, healer.Care().Examined); Assert.Equal(new[] { 20 }, healer.Care().Refusals.Select(x => x.Patient).ToArray());          // the community's care record is not the doctor's: it is inherited
            Assert.Equal(0, healer.Care().KinSeen); Assert.Equal(0, healer.Care().RoutineUsed);                           // the doctor's own working state starts fresh
        }

        [Fact]
        public void A_new_doctor_plans_the_first_round_a_day_after_taking_the_post()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Healer, 1);
            Assert.Equal(5000 + Day, o.Care().NextRoundTick);          // ElectedTick + a day, as the election used to set it
            o.Care().NextRoundTick = 99;
            Assert.Equal(99, o.Care().NextRoundTick);          // and only once
        }

        // ---------------------------------------------------------------- a module that is absent

        [Fact]
        public void A_modules_payload_is_left_untouched_by_a_registry_that_never_loads_the_module_and_survives_a_reload()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Healer, 1);
            o.Care().Examined.AddRange(new[] { 2, 3 }); o.Care().Refusals.Add(TestRefusals.Make(20, 1, CareRefusalKind.HealerSpecific, 1500));
            o.FlushState();
            string carePayload = o.PayloadData[o.PayloadIds.IndexOf(CareOfficeData.Id)];
            // a colony saved with Community Care, loaded without it: nothing reads or writes that payload...
            var reloaded = SaveSim.Reload(r);
            var onlyEducation = reloaded.OfficeFor(OfficeKind.Healer);
            onlyEducation.Education().LessonLog.Add("x");          // another module's state on the same post
            reloaded.FlushAllState();
            Assert.Equal(carePayload, onlyEducation.PayloadData[onlyEducation.PayloadIds.IndexOf(CareOfficeData.Id)]);
            // ...and when the module comes back, its data is exactly as it was
            var again = SaveSim.Reload(reloaded).OfficeFor(OfficeKind.Healer).Care();
            Assert.Equal(new[] { 2, 3 }, again.Examined.ToArray()); Assert.Equal(new[] { 20 }, again.Refusals.Select(x => x.Patient).ToArray());
        }

        [Fact]
        public void A_payload_nobody_in_this_build_knows_is_kept_whole()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Healer, 1);
            o.PayloadIds.Add("wils.future.thing"); o.PayloadData.Add("anything=at,all\nx=1"); o.PayloadHolders.Add(1); o.PayloadVersions.Add(7);
            var back = SaveSim.Reload(r).OfficeFor(OfficeKind.Healer);
            int i = back.PayloadIds.IndexOf("wils.future.thing");
            Assert.True(i >= 0); Assert.Equal("anything=at,all\nx=1", back.PayloadData[i]); Assert.Equal(7, back.PayloadVersions[i]);
        }

        [Fact]
        public void Uneven_payload_lists_in_a_damaged_save_are_made_consistent_rather_than_throwing()
        {
            var o = new CommunityOffice { Kind = OfficeKind.Healer, HolderId = 1 };
            o.PayloadIds.Add("wils.care"); o.PayloadIds.Add("wils.education");          // no data, holders or versions written
            var care = o.Care();
            Assert.NotNull(care);
            Assert.Equal(o.PayloadIds.Count, o.PayloadData.Count); Assert.Equal(o.PayloadIds.Count, o.PayloadHolders.Count); Assert.Equal(o.PayloadIds.Count, o.PayloadVersions.Count);
        }

        private sealed class V2State : IModuleState { public int N; public string Encode() => new StateWriter().Int("n", N).ToString(); }
        private sealed class V2Factory : IStateFactory<V2State>
        {
            public int Version => 2; public int SeenVersion;
            public V2State Create() => new V2State();
            public V2State Decode(StateReader reader, int version) { SeenVersion = version; return new V2State { N = reader.Int("n") * (version == 1 ? 10 : 1) }; }          // an older format is still readable
            public V2State FromLegacy(StateReader legacy) => new V2State();
        }

        [Fact]
        public void The_stored_format_version_is_handed_to_the_module_so_an_older_payload_can_be_read()
        {
            var o = new CommunityOffice { Kind = OfficeKind.Healer, HolderId = 1 };
            o.PayloadIds.Add("wils.test.thing"); o.PayloadData.Add("n=4"); o.PayloadHolders.Add(1); o.PayloadVersions.Add(1);
            var f = new V2Factory();
            Assert.Equal(40, o.StateOf("wils.test.thing", false, f).N);
            Assert.Equal(1, f.SeenVersion);
            o.StateOf("wils.test.thing", false, f).N = 41; o.FlushState();
            Assert.Equal("n=41", o.PayloadData[0]);          // the next save writes it with the current encoder
        }

        // ---------------------------------------------------------------- migration from an all-in-one save

        private static LegacyOfficeValues OldSave() => new LegacyOfficeValues
        {
            Ints = { ["nextRound"] = 90000, ["roundStart"] = 61000, ["lastIllness"] = 20000, ["lastTestingYear"] = 2026, ["testingStart"] = 70000, ["testingSlot"] = 2, ["roundWindow"] = 1260000, ["routineDay"] = 1, ["routineUsed"] = 3, ["lastVisit"] = 62000, ["kinSeen"] = 3, ["otherSeen"] = 5 },
            Bools = { ["illnessRound"] = true },
            Floats = { ["kinQuality"] = 6.5f, ["otherQuality"] = 8f },
            IntLists = { ["examined"] = new List<int> { 2, 3 }, ["testIds"] = new List<int> { 30, 4 }, ["testPass"] = new List<int> { 0, 0 }, ["testTry"] = new List<int> { 2, 1 }, ["missed"] = new List<int> { 4 }, ["refused"] = new List<int> { 20 }, ["refusedTick"] = new List<int> { 61500 }, ["testScore"] = new List<int> { 4, -2 }, ["testExcused"] = new List<int> { 0, 1 } },
            StringLists = { ["lessonLog"] = new List<string> { new LessonRecord { Pupil = 30, Skill = "Cooking", Xp = 250f, Ticks = 3000, Possible = 4000, Classes = 1 }.Encode() }, ["bookUse"] = new List<string> { "30|77|50000" }, ["recentSubjects"] = new List<string> { "Cooking", "Plants" } }
        };

        [Fact]
        public void An_old_all_in_one_posts_values_become_each_modules_own_state_exactly()
        {
            var r = Colony(); var healer = Post(r, OfficeKind.Healer, 1); var teacher = Post(r, OfficeKind.Teacher, 2);
            Assert.True(LegacyOfficeMigration.Capture(healer, OldSave())); Assert.True(LegacyOfficeMigration.Capture(teacher, OldSave()));
            var c = healer.Care();
            Assert.Equal(90000, c.NextRoundTick); Assert.Equal(61000, c.RoundStartTick); Assert.Equal(20000, c.LastIllnessTick); Assert.Equal(1260000, c.RoundWindow); Assert.True(c.IllnessRound);
            Assert.Equal(new[] { 2, 3 }, c.Examined.ToArray()); Assert.Equal(new[] { 4 }, c.Missed.ToArray()); var legacyRefusal = c.Refusals.Single(); Assert.Equal(20, legacyRefusal.Patient); Assert.Equal(61500, legacyRefusal.Tick); Assert.Equal(CareRefusalKind.ServiceWide, legacyRefusal.Kind); Assert.Equal(CareRefusalReason.Legacy, legacyRefusal.Reason); Assert.Equal(61500 + AppointmentBackoff.RefusalDays * ActivityTuning.TicksPerDay, legacyRefusal.Until);
            Assert.Equal(3, c.RoutineUsed); Assert.Equal(62000, c.LastVisitTick); Assert.Equal(6.5f, c.KinQuality); Assert.Equal(5, c.OtherSeen);
            Assert.True(c.Started);          // an old save's holder is not given a fresh day of grace
            Assert.Equal(90000, c.NextRoundTick);
            var e = teacher.Education(); var a = teacher.Assessment();
            Assert.Equal(2026, e.LastTestingYear); Assert.Single(e.LessonLog); Assert.Equal("30|77|50000", e.BookUse[0]); Assert.Equal(new[] { "Cooking", "Plants" }, e.RecentSubjects.ToArray());
            Assert.Equal(70000, a.TestingStartTick); Assert.Equal(2, a.TestingSlot); Assert.Equal(new[] { 30, 4 }, a.TestIds.ToArray()); Assert.Equal(new[] { 2, 1 }, a.TestTry.ToArray()); Assert.Equal(new[] { 4, -2 }, a.TestScore.ToArray()); Assert.Equal(new[] { 0, 1 }, a.TestExcused.ToArray());
        }

        [Fact]
        public void Migration_happens_once_and_never_doubles_a_reward_or_a_record()
        {
            var r = Colony(); var teacher = Post(r, OfficeKind.Teacher, 2);
            Assert.True(LegacyOfficeMigration.Capture(teacher, OldSave()));
            Assert.False(LegacyOfficeMigration.Capture(teacher, OldSave()));          // loading the same values again changes nothing
            var e = teacher.Education();
            e.BookUse.Add("31|78|60000");
            var reloaded = SaveSim.Reload(r).OfficeFor(OfficeKind.Teacher);
            Assert.False(LegacyOfficeMigration.Capture(reloaded, OldSave()));          // and a later load has the payloads already, so the old values are ignored
            Assert.Equal(2, reloaded.Education().BookUse.Count);                       // not three, not one
            Assert.Single(reloaded.Education().LessonLog);
            Assert.False(LessonMath.BookReady(reloaded.Education().BookUse, 30, 77, 50000 + 3 * Day));          // the book that already paid out still waits its twelve days
        }

        [Fact]
        public void An_old_save_with_nothing_to_migrate_makes_no_payload_and_a_module_added_later_still_finds_the_old_values()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Healer, 1);
            Assert.False(LegacyOfficeMigration.Capture(o, new LegacyOfficeValues()));          // a new save has no legacy values
            Assert.Empty(o.PayloadIds);
            // old values captured while no module asks for them are kept, and the module that arrives later reads them
            var o2 = Post(r, OfficeKind.Teacher, 2);
            Assert.True(LegacyOfficeMigration.Capture(o2, OldSave()));
            var kept = SaveSim.Reload(r).OfficeFor(OfficeKind.Teacher);
            Assert.Contains(LegacyOfficeMigration.PayloadId, kept.PayloadIds);
            Assert.Equal(2026, kept.Education().LastTestingYear);          // the module arrives, asks, and gets them
        }

        [Fact]
        public void The_legacy_label_lists_cover_every_value_the_all_in_one_version_wrote()
        {
            var all = LegacyOfficeLabels.Ints.Concat(LegacyOfficeLabels.Bools).Concat(LegacyOfficeLabels.Floats).Concat(LegacyOfficeLabels.IntLists).Concat(LegacyOfficeLabels.StringLists).ToList();
            Assert.Equal(all.Count, all.Distinct().Count());
            var old = OldSave();
            var oldLabels = old.Ints.Keys.Concat(old.Bools.Keys).Concat(old.Floats.Keys).Concat(old.IntLists.Keys).Concat(old.StringLists.Keys).ToList();
            Assert.Empty(all.Except(oldLabels));          // the fixture above mentions every label...
            Assert.Empty(oldLabels.Except(all));          // ...and nothing else
        }
    }
}
