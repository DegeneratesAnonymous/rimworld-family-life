using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    [CollectionDefinition("OfficeAvailability", DisableParallelization = true)]
    public class OfficeAvailabilityCollection { }

    /// <summary>
    /// What happens to the records when a module is not there, and the difference between a module that is merely absent and one that was prepared for removal. These change global
    /// switches (which posts are offered, the hand-over hook), so they run one at a time.
    /// </summary>
    [Collection("OfficeAvailability")]
    public class ModuleAbsenceTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry Colony()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Doc"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }), T.Kid(30, new[] { 1 }) }, 1000);
            return r;
        }

        private static CommunityOffice Post(FamilyRegistry r, OfficeKind kind, int holder)
        {
            var o = new CommunityOffice { Id = (int)kind + 1, Kind = kind, HolderId = holder, HolderFamily = r.FamilyOf(holder), ElectedTick = 5000 };
            r.Offices.Add(o);
            return o;
        }

        private static void WithOffered(Func<OfficeKind, bool> offered, Action body)
        {
            var before = OfficeAvailability.IsOffered;
            try { OfficeAvailability.IsOffered = offered; body(); } finally { OfficeAvailability.IsOffered = before; }
        }

        // ---------------------------------------------------------------- temporary absence

        [Fact]
        public void A_post_whose_module_is_absent_is_never_neglected_so_taking_a_module_out_does_not_cost_the_holder_their_post_or_house_standing()
        {
            var r = Colony(); var teacher = Post(r, OfficeKind.Teacher, 1);
            teacher.UnmetSinceTick = 100; teacher.LastWorkTick = 100;          // work was needed when the module stopped
            float standing = r.GetLineage(r.FamilyOf(1)).Standing;
            WithOffered(k => k != OfficeKind.Teacher, () => r.MaintainOffices(100 + 40 * Day));          // forty days with nobody able to do the work
            Assert.True(teacher.Held); Assert.Equal(0, teacher.UnmetSinceTick);
            Assert.Equal(OfficeEnd.None, teacher.EndReason); Assert.Equal(0, teacher.BarredId);
            Assert.Equal(standing, r.GetLineage(r.FamilyOf(1)).Standing);
        }

        [Fact]
        public void The_same_post_is_neglected_as_before_while_its_module_is_present()
        {
            var r = Colony(); var teacher = Post(r, OfficeKind.Teacher, 1);
            teacher.UnmetSinceTick = 100; teacher.LastWorkTick = 100;
            r.MaintainOffices(100 + 40 * Day);
            Assert.False(teacher.Held); Assert.Equal(OfficeEnd.Neglected, teacher.EndReason);          // unchanged behaviour with the module installed
        }

        [Fact]
        public void The_yearly_assembly_leaves_the_post_of_an_absent_module_exactly_as_it_was()
        {
            var r = Colony(); var teacher = Post(r, OfficeKind.Teacher, 1); var healer = Post(r, OfficeKind.Healer, 2);
            teacher.Education().LessonLog.Add(new LessonRecord { Pupil = 30, Skill = "Cooking", Xp = 100f, Ticks = 4000, Possible = 5000, Classes = 1 }.Encode());
            r.NextAssemblyTick = 100;
            WithOffered(k => k != OfficeKind.Teacher, () => r.CallAssembly(200, new OfficeContext()));
            Assert.True(teacher.Held && teacher.HolderId == 1);          // not stood down for want of candidates
            Assert.Single(teacher.Education().LessonLog);
            Assert.DoesNotContain(r.OpenAssembly?.Races ?? new List<AssemblyRace>(), x => x.Kind == OfficeKind.Teacher);          // and not put to a vote nobody can run
        }

        [Fact]
        public void A_missing_module_leaves_its_payloads_untouched_through_save_and_load_and_the_returning_module_validates_before_using_them()
        {
            var r = Colony(); var teacher = Post(r, OfficeKind.Teacher, 1);
            teacher.Education().LessonLog.Add(new LessonRecord { Pupil = 30, Skill = "Cooking", Xp = 100f, Ticks = 4000, Possible = 5000, Classes = 1 }.Encode());
            teacher.Assessment().TestingStartTick = 7000; teacher.Assessment().TestIds.Add(30);
            r.FlushAllState();          // what a save writes
            string before = string.Join("|", teacher.PayloadData);
            var loaded = SaveSim.Reload(SaveSim.Reload(SaveSim.Reload(r)));          // many saves with the module absent: nothing reads or writes its payloads
            Assert.Equal(before, string.Join("|", loaded.OfficeFor(OfficeKind.Teacher).PayloadData));
            Assert.Equal(teacher.PayloadIds, loaded.OfficeFor(OfficeKind.Teacher).PayloadIds);

            // the module returns with a payload it does not understand: it falls back to an empty record rather than throwing
            var damaged = loaded.OfficeFor(OfficeKind.Teacher);
            damaged.PayloadData[damaged.PayloadIds.IndexOf(EducationOfficeData.Id)] = "\u0001garbage\n=\n\n";
            damaged.DropStateCache();
            Assert.Empty(damaged.Education().LessonLog);
        }

        // ---------------------------------------------------------------- prepared removal

        [Fact]
        public void Prepared_removal_cancels_active_work_and_reinstalling_does_not_bring_it_back()
        {
            var r = Colony(); var teacher = Post(r, OfficeKind.Teacher, 1);
            teacher.SessionQueue.AddRange(new[] { 2, 3 }); teacher.PendingKind = PendingEvent.Testing; teacher.PendingTick = 9000;
            teacher.Assessment().TestingStartTick = 7000; teacher.Assessment().TestIds.AddRange(new[] { 30 }); teacher.Assessment().TestPass.Add(0);
            teacher.Education().LessonLog.Add(new LessonRecord { Pupil = 30, Skill = "Cooking", Xp = 100f, Ticks = 4000, Possible = 5000, Classes = 1 }.Encode());

            var done = r.EndEducationActivity(10000, PendingEvent.Testing);
            Assert.NotEmpty(done);
            var reloaded = SaveSim.Reload(r);          // the prepared save is saved, then the module is removed and later reinstalled
            var again = reloaded.OfficeFor(OfficeKind.Teacher);
            Assert.False(again.Held); Assert.Empty(again.SessionQueue); Assert.Equal(0, again.PendingKind);
            Assert.Equal(0, again.Assessment().TestingStartTick); Assert.Empty(again.Assessment().TestIds);          // the cancelled sitting is gone, not waiting to resume
            Assert.Single(again.Education().LessonLog);          // neutral history stays
            // reactivation starts new work from a clean state: a new teacher is a new holder with a fresh assessment
            again.HolderId = 3; again.HolderFamily = reloaded.FamilyOf(3);
            Assert.Equal(0, again.Assessment().TestingStartTick); Assert.Empty(again.SessionQueue);
        }

        [Fact]
        public void Preparing_twice_does_nothing_the_second_time()
        {
            var r = Colony(); var teacher = Post(r, OfficeKind.Teacher, 1); teacher.SessionQueue.Add(2);
            Assert.NotEmpty(r.EndEducationActivity(10000, PendingEvent.Testing));
            Assert.Empty(r.EndEducationActivity(10100, PendingEvent.Testing));
            var healer = Post(r, OfficeKind.Healer, 2); healer.Care().Examined.Add(3); healer.Care().IllnessRound = true;
            Assert.NotEmpty(r.EndCareActivity(10200, PendingEvent.Checkups, PendingEvent.Illness));
            Assert.Empty(r.EndCareActivity(10300, PendingEvent.Checkups, PendingEvent.Illness));
        }

        [Fact]
        public void Ending_care_keeps_the_communitys_record_of_who_was_missed_and_who_declined()
        {
            var r = Colony(); var healer = Post(r, OfficeKind.Healer, 2);
            healer.Care().Missed.Add(3); healer.Care().Refused.Add(30); healer.Care().RefusedTick.Add(1234); healer.Care().RoundStartTick = 6000; healer.Care().Examined.Add(3);
            r.EndCareActivity(10000, PendingEvent.Checkups, PendingEvent.Illness);
            Assert.Equal(0, healer.Care().RoundStartTick);          // the round in progress is closed
            Assert.Equal(new[] { 3 }, healer.Care().Missed);          // the missed still go first when care returns
            Assert.Equal(new[] { 30 }, healer.Care().Refused);          // and a refusal is still respected
        }

        // ---------------------------------------------------------------- the post changes hands

        [Fact]
        public void Every_change_of_holder_tells_the_hand_over_hook_who_left_and_who_came()
        {
            var before = FamilyRegistry.HolderChanged;
            var seen = new List<string>();
            try
            {
                FamilyRegistry.HolderChanged = (reg, office, old, now) => seen.Add(office.Kind + ":" + old + ">" + now);
                var r = Colony(); Post(r, OfficeKind.Teacher, 1);
                Assert.True(r.ResignOffice(OfficeKind.Teacher, 1, 9000));
                Assert.Equal(new[] { "Teacher:1>0" }, seen);          // the outgoing teacher is named, so their class can be ended safely
            }
            finally { FamilyRegistry.HolderChanged = before; }
        }

        [Fact]
        public void Without_a_hook_a_change_of_holder_still_works()
        {
            var before = FamilyRegistry.HolderChanged;
            try { FamilyRegistry.HolderChanged = null; var r = Colony(); Post(r, OfficeKind.Healer, 2); Assert.True(r.ResignOffice(OfficeKind.Healer, 2, 9000)); }
            finally { FamilyRegistry.HolderChanged = before; }
        }

        [Fact]
        public void A_new_teacher_inherits_the_pupils_record_but_not_the_old_teachers_assessment_and_a_new_healer_inherits_the_round_but_not_the_old_doctors_favouritism()
        {
            var r = Colony(); var teacher = Post(r, OfficeKind.Teacher, 1); var healer = Post(r, OfficeKind.Healer, 2);
            teacher.Education().LessonLog.Add(new LessonRecord { Pupil = 30, Skill = "Cooking", Xp = 100f, Ticks = 4000, Possible = 5000, Classes = 1 }.Encode());
            teacher.Assessment().TestingStartTick = 7000; teacher.Assessment().TestIds.Add(30);
            healer.Care().Examined.Add(3); healer.Care().RoundStartTick = 6000; healer.Care().KinSeen = 4; healer.Care().KinQuality = 8f;
            r.ResignOffice(OfficeKind.Teacher, 1, 9000); r.ResignOffice(OfficeKind.Healer, 2, 9000);
            teacher.HolderId = 3; teacher.HolderFamily = r.FamilyOf(3); healer.HolderId = 3; healer.HolderFamily = r.FamilyOf(3);
            Assert.Single(teacher.Education().LessonLog); Assert.Equal(0, teacher.Assessment().TestingStartTick);
            Assert.Equal(new[] { 3 }, healer.Care().Examined); Assert.Equal(6000, healer.Care().RoundStartTick); Assert.Equal(0, healer.Care().KinSeen);
        }
    }
}
