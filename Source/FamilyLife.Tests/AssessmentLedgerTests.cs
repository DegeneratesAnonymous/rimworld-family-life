using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class AssessmentLedgerTests
    {
        private static FamilyRegistry Colony() { var r = new FamilyRegistry(); r.Apply(new[] { T.P(1, "T1"), T.P(2, "T2"), T.P(10, "K1"), T.P(11, "K2"), T.P(12, "K3") }, 1000); return r; }
        private static LessonRecord Rec(int pupil, string skill, float xp, int teacher) => new LessonRecord { Pupil = pupil, Skill = skill, Xp = xp, Ticks = 100, Possible = 200, Classes = 1, Teacher = teacher };
        private static List<string> Log(params LessonRecord[] recs) { var l = new List<string>(); foreach (var x in recs) LessonRecord.Add(l, x); return l; }

        [Fact] public void Lesson_records_remember_who_taught_and_old_records_without_a_teacher_still_read()
        {
            var r = Rec(10, "Melee", 5f, 2);
            Assert.Equal(2, LessonRecord.Parse(r.Encode()).Teacher);
            Assert.Equal(0, LessonRecord.Parse("10|Melee|5|1|0|100|200|0|1").Teacher);
        }

        [Fact] public void Two_teachers_of_one_subject_keep_separate_records_until_assessed_together()
        {
            var log = Log(Rec(10, "Melee", 5f, 1), Rec(10, "Melee", 7f, 2), Rec(10, "Melee", 1f, 1));
            Assert.Equal(2, log.Count);
            var merged = LessonRecord.Merge(LessonRecord.For(log, 10));
            Assert.Equal(13f, merged.Xp); Assert.Equal(3, merged.Classes);
        }

        [Fact] public void The_owner_is_the_teacher_who_gave_most_and_is_stable_and_falls_back_to_the_lowest_current_instructor()
        {
            var recs = new[] { Rec(10, "Melee", 5f, 1), Rec(10, "Melee", 7f, 2) };
            Assert.Equal(2, AssessmentOwnership.OwnerOf(recs, new[] { 1, 2 }));
            Assert.Equal(2, AssessmentOwnership.OwnerOf(recs, new[] { 2, 1 }));
            Assert.Equal(1, AssessmentOwnership.OwnerOf(recs, new[] { 1 }));                                   // teacher 2 is gone
            Assert.Equal(1, AssessmentOwnership.OwnerOf(new[] { Rec(10, "Melee", 5f, 0) }, new[] { 3, 1 }));  // an old record with no teacher
            Assert.Equal(1, AssessmentOwnership.OwnerOf(new[] { Rec(10, "Melee", 5f, 1), Rec(10, "Melee", 5f, 2) }, new[] { 1, 2 }));          // a tie goes to the lowest id
            Assert.Equal(0, AssessmentOwnership.OwnerOf(recs, new int[0]));
        }

        [Fact] public void Each_teacher_assesses_their_own_pupils_and_nobody_is_assessed_twice_for_the_same_subject_and_year()
        {
            var l = new AssessmentLedger(); var instr = new[] { 1, 2 };
            var log = Log(Rec(10, "Melee", 9f, 1), Rec(11, "Plants", 9f, 2), Rec(12, "Melee", 3f, 1), Rec(12, "Melee", 4f, 2));
            Assert.Equal(new[] { 10 }, AssessmentOwnership.PupilsFor(1, log, instr, 5500, l).ToArray().Take(1).ToArray());
            var a = AssessmentOwnership.PupilsFor(1, log, instr, 5500, l); var b = AssessmentOwnership.PupilsFor(2, log, instr, 5500, l);
            Assert.Empty(a.Intersect(b)); Assert.Equal(new[] { 10, 11, 12 }, a.Concat(b).OrderBy(x => x).ToArray());
            l.MarkDone(10, "Melee", 5500);
            Assert.DoesNotContain(10, AssessmentOwnership.PupilsFor(1, log, instr, 5500, l));
            Assert.Contains(10, AssessmentOwnership.PupilsFor(1, log, instr, 5501, l));                         // next year is a new period
        }

        [Fact] public void A_pupil_sits_for_one_teacher_at_a_time_and_a_teacher_runs_one_assessment()
        {
            var l = new AssessmentLedger();
            Assert.NotNull(l.Begin(1, 5500, 100, new[] { 10, 11 }));
            Assert.Null(l.Begin(1, 5500, 100, new[] { 12 }));
            var second = l.Begin(2, 5500, 100, new[] { 11, 12 });
            Assert.Equal(new[] { 12 }, second.Ids.ToArray());
            Assert.Null(l.Begin(3, 5500, 100, new[] { 10 }));
        }

        [Fact] public void Assessments_survive_a_save_and_a_second_one_and_a_teacher_leaving_does_not_lose_the_pupils_record()
        {
            var r = Colony(); var l = r.Assessments(); var s = l.Begin(1, 5500, 777, new[] { 10, 11 }); s.Try[0] = 1; s.Score[0] = -3; s.Slot = 2; l.MarkDone(10, "Melee", 5499);
            r.EducationRecord().LessonLog.AddRange(Log(Rec(10, "Melee", 5f, 1)));
            var back = SaveSim.Reload(SaveSim.Reload(r));
            var bs = back.Assessments().Of(1);
            Assert.Equal(777, bs.StartTick); Assert.Equal(2, bs.Slot); Assert.Equal(1, bs.Try[0]); Assert.Equal(-3, bs.Score[0]); Assert.Equal(new[] { 10, 11 }, bs.Ids.ToArray());
            Assert.True(back.Assessments().WasDone(10, "Melee", 5499)); Assert.Equal(5500, back.Assessments().LastYearOf(1));
            Assert.Single(back.EducationRecord().LessonLog);
            Assert.Equal(1, AssessmentOwnership.OwnerOf(LessonRecord.For(back.EducationRecord().LessonLog, 10), new[] { 1 }));
            Assert.Equal(2, AssessmentOwnership.OwnerOf(LessonRecord.For(back.EducationRecord().LessonLog, 10), new[] { 2 }));          // reassigned: the other teacher takes it
        }

        [Fact] public void The_old_teacher_posts_record_is_copied_into_the_community_record_once()
        {
            var r = Colony();
            var o = new CommunityOffice { Id = 1, Kind = OfficeKind.Teacher, HolderId = 1 }; r.Offices.Add(o);
            o.Education().LessonLog.Add(Rec(10, "Melee", 5f, 0).Encode()); o.Education().RecentSubjects.Add("Melee"); o.Education().BookUse.Add("10|5|100");
            var rec = r.EducationRecord();
            Assert.Single(rec.LessonLog); Assert.Equal(new[] { "Melee" }, rec.RecentSubjects.ToArray()); Assert.True(rec.ImportedFromOffice);
            o.Education().LessonLog.Add(Rec(11, "Plants", 5f, 0).Encode());                                    // later edits of the old record do not leak in
            Assert.Single(SaveSim.Reload(r).EducationRecord().LessonLog);
        }

        [Fact] public void An_assessment_under_way_in_the_old_post_moves_to_its_holder_exactly_once()
        {
            var r = Colony();
            var o = new CommunityOffice { Id = 1, Kind = OfficeKind.Teacher, HolderId = 1 }; r.Offices.Add(o);
            var a = o.Assessment(); a.TestingStartTick = 900; a.TestingSlot = 1; a.TestIds = new List<int> { 10, 11 }; a.TestTry = new List<int> { 1, 0 }; a.TestScore = new List<int> { 4, 0 };
            o.Education().LastTestingYear = 5500;
            Assert.True(r.MigrateLegacyAssessment());
            var s = r.Assessments().Of(1);
            Assert.Equal(900, s.StartTick); Assert.Equal(new[] { 10, 11 }, s.Ids.ToArray()); Assert.Equal(1, s.Try[0]); Assert.Equal(5500, r.Assessments().LastYearOf(1));
            Assert.False(r.MigrateLegacyAssessment());
            var back = SaveSim.Reload(r); Assert.False(back.MigrateLegacyAssessment()); Assert.Single(back.Assessments().Active);
            back.Assessments().End(1);
            Assert.False(SaveSim.Reload(back).MigrateLegacyAssessment());                                      // finished and gone: not resurrected
            Assert.Empty(SaveSim.Reload(back).Assessments().Active);
        }

        [Fact] public void A_new_holder_of_the_old_post_is_migrated_on_their_own_and_a_vacant_post_migrates_nothing()
        {
            var r = Colony(); var o = new CommunityOffice { Id = 1, Kind = OfficeKind.Teacher, HolderId = 0 }; r.Offices.Add(o);
            Assert.False(r.MigrateLegacyAssessment());
            o.HolderId = 2; Assert.True(r.MigrateLegacyAssessment()); Assert.False(r.MigrateLegacyAssessment());
        }

        [Fact] public void The_done_list_is_bounded()
        {
            var l = new AssessmentLedger(); for (int i = 0; i < AssessmentLedger.MaxDone + 50; i++) l.MarkDone(i + 1, "Melee", 1);
            Assert.False(l.WasDone(1, "Melee", 1)); Assert.True(l.WasDone(AssessmentLedger.MaxDone + 50, "Melee", 1));
        }
    }
}
