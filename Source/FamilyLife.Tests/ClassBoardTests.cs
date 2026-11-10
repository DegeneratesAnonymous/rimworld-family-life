using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class ClassBoardTests
    {
        [Fact] public void Two_teachers_run_independent_classes_at_once_with_their_own_pupils()
        {
            var b = new ClassBoard();
            Assert.Equal(new[] { 10, 11, 12 }, b.Open(1, new[] { 10, 11, 12 }, "Melee", "room-a", 100).ToArray());
            Assert.Equal(new[] { 13, 14 }, b.Open(2, new[] { 13, 14 }, "Plants", "room-b", 100).ToArray());
            Assert.Equal(2, b.Sessions.Count());
            Assert.Equal("Melee", b.Of(1).Subject); Assert.Equal("Plants", b.Of(2).Subject);
        }

        [Fact] public void Nobody_is_in_two_classes_and_an_instructor_is_never_a_pupil_elsewhere()
        {
            var b = new ClassBoard();
            b.Open(1, new[] { 10, 11 }, "Melee", "a", 1);
            var second = b.Open(2, new[] { 10, 11, 12, 1 }, "Plants", "b", 1);
            Assert.Equal(new[] { 12 }, second.ToArray());                       // 10 and 11 are taken; 1 is leading
            Assert.Null(b.Open(1, new[] { 20 }, "x", "c", 1));                  // an instructor cannot open a second class
            Assert.Null(b.Open(10, new[] { 20 }, "x", "c", 1));                 // a pupil cannot lead one
            Assert.Null(b.Open(3, new[] { 21 }, "x", "a", 1));                  // the place is in use
        }

        [Fact] public void Closing_frees_everyone_and_the_place()
        {
            var b = new ClassBoard(); b.Open(1, new[] { 10, 11 }, "Melee", "a", 1, new[] { 5 });
            Assert.True(b.IsBusy(5)); b.Close(1);
            Assert.False(b.IsBusy(1) || b.IsBusy(5) || b.IsBusy(10)); Assert.False(b.PlaceBusy("a"));
            Assert.NotNull(b.Open(2, new[] { 10 }, "x", "a", 2));
        }

        [Fact] public void A_released_pupil_can_join_another_class_and_the_class_is_capped()
        {
            var b = new ClassBoard(); b.Open(1, new[] { 10 }, "Melee", "a", 1); b.Release(10);
            Assert.Equal(new[] { 10 }, b.Open(2, new[] { 10 }, "x", "b", 2).ToArray());
            var big = new ClassBoard(); Assert.Equal(ClassBoard.MaxPupils, big.Open(1, Enumerable.Range(100, 50), "x", "a", 1).Count);
        }

        [Fact] public void An_assistant_is_busy_with_the_class_and_can_take_over_as_instructor()
        {
            var b = new ClassBoard(); b.Open(1, new[] { 10, 11 }, "Melee", "a", 1, new[] { 5 });
            Assert.True(b.IsBusy(5));
            Assert.True(b.Substitute(1, 5));
            Assert.Equal(5, b.Of(5).Instructor); Assert.Null(b.Of(1)); Assert.False(b.IsBusy(1)); Assert.True(b.IsBusy(10));
            Assert.False(b.Substitute(5, 99));                                  // not an assistant
            b.Close(5); Assert.False(b.IsBusy(10) || b.IsBusy(5));
        }

        [Fact] public void An_assistant_cannot_be_someone_already_in_another_class()
        {
            var b = new ClassBoard(); b.Open(1, new[] { 10 }, "x", "a", 1);
            b.Open(2, new[] { 11 }, "x", "b", 1, new[] { 10, 3 });
            Assert.False(b.Of(2).Assistants.Contains(10)); Assert.True(b.Of(2).Assistants.Contains(3));
        }

        [Fact] public void Each_teacher_paces_their_own_classes_and_an_old_post_date_seeds_the_sitting_teacher_once()
        {
            var s = new TeacherSchedule(); s.Seed(1, 500); s.Seed(1, 9999); s.Set(2, 100);
            Assert.Equal(500, s.NextFor(1)); Assert.False(s.Due(1, 499)); Assert.True(s.Due(1, 500)); Assert.True(s.Due(3, 0));
            Assert.Equal(100, s.NextFor(2));
        }

        [Fact] public void The_schedule_survives_a_save_and_load_and_a_second_one()
        {
            var r = new FamilyRegistry(); r.Apply(new[] { T.P(1, "A") }, 10);
            r.TeacherSchedule().Set(1, 777);
            var back = SaveSim.Reload(SaveSim.Reload(r));
            Assert.Equal(777, back.TeacherSchedule().NextFor(1));
        }
    }
}
