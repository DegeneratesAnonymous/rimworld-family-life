using System.Collections.Generic;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class ModuleRemovalTests
    {
        private static FamilyRegistry Colony()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Doc"), T.P(2, "Two", parents: new[] { 1 }) }, 1000);
            return r;
        }

        private static CommunityOffice Post(FamilyRegistry r, OfficeKind kind, int holder)
        {
            var o = new CommunityOffice { Id = (int)kind + 1, Kind = kind, HolderId = holder, HolderFamily = r.FamilyOf(holder), ElectedTick = 5000 };
            r.Offices.Add(o);
            return o;
        }

        [Fact]
        public void The_report_says_not_prepared_when_any_step_fails_but_still_runs_the_rest()
        {
            var rep = new CleanupReport("wils.test");
            rep.Step("first", () => { throw new System.InvalidOperationException("boom"); });
            rep.Step("second", () => "ok");
            Assert.False(rep.Prepared);
            Assert.Single(rep.Failed); Assert.Single(rep.Done);
            Assert.Contains("NOT prepared", rep.ToString());
        }

        [Fact]
        public void Ending_care_closes_the_round_clears_the_event_and_gives_up_the_post()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Healer, 1);
            var c = o.Care(); c.RoundStartTick = 6000; c.IllnessRound = true; c.Examined.Add(2);
            o.PendingKind = PendingEvent.Checkups;
            var done = r.EndCareActivity(9000, PendingEvent.Checkups, PendingEvent.Illness);
            Assert.NotEmpty(done);
            Assert.Equal(0, o.Care().RoundStartTick); Assert.False(o.Care().IllnessRound);
            Assert.Equal(0, o.PendingKind); Assert.Empty(o.Care().Examined);
            Assert.False(o.Held);
        }

        [Fact]
        public void Ending_education_empties_the_class_and_stops_the_assessment()
        {
            var r = Colony(); var o = Post(r, OfficeKind.Teacher, 1);
            o.SessionQueue.AddRange(new[] { 2 });
            o.PendingKind = PendingEvent.Testing;
            var done = r.EndEducationActivity(9000, PendingEvent.Testing);
            Assert.NotEmpty(done);
            Assert.Empty(o.SessionQueue); Assert.Equal(0, o.PendingKind); Assert.False(o.Held);
        }

        [Fact]
        public void Ending_activity_leaves_other_modules_posts_and_events_alone()
        {
            var r = Colony(); var teacher = Post(r, OfficeKind.Teacher, 1);
            teacher.PendingKind = PendingEvent.Testing;
            Assert.Empty(r.EndCareActivity(9000, PendingEvent.Checkups, PendingEvent.Illness));
            Assert.True(teacher.Held); Assert.Equal(PendingEvent.Testing, teacher.PendingKind);
        }

        [Fact]
        public void Ending_activity_twice_is_harmless()
        {
            var r = Colony(); Post(r, OfficeKind.Healer, 1);
            r.EndCareActivity(9000, PendingEvent.Checkups);
            Assert.Empty(r.EndCareActivity(9100, PendingEvent.Checkups));
        }
    }
}
