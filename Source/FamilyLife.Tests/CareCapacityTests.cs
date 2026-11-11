using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class CareCapacityTests : System.IDisposable
    {
        private readonly float saved = CheckupWorkload.TotalCapacity;
        public CareCapacityTests() { PractitionerLoad.Clear(); }
        public void Dispose() { CheckupWorkload.TotalCapacity = saved; PractitionerLoad.Clear(); }

        [Fact] public void One_worker_keeps_the_old_single_healer_budget()
        {
            CheckupWorkload.TotalCapacity = 1f;
            Assert.Equal(CheckupWorkload.VisitsPerDay(false), CheckupWorkload.CommunityLimit(false));
            Assert.Equal(CheckupWorkload.VisitsPerDay(false), CheckupWorkload.PersonalLimit(false, 1f));
        }

        [Fact] public void A_hospital_colony_sees_many_times_more_people_than_a_small_settlement()
        {
            CheckupWorkload.TotalCapacity = 1f; int small = CheckupWorkload.CommunityLimit(false); int smallDays = CheckupWorkload.WindowDays(60, false);
            CheckupWorkload.TotalCapacity = 8f; int big = CheckupWorkload.CommunityLimit(false); int bigDays = CheckupWorkload.WindowDays(60, false);
            Assert.Equal(small * 8, big); Assert.True(bigDays < smallDays);
        }

        [Fact] public void Capacity_below_one_worker_never_lowers_the_community_budget_but_does_lower_that_workers_own()
        {
            CheckupWorkload.TotalCapacity = .4f;
            Assert.Equal(CheckupWorkload.VisitsPerDay(false), CheckupWorkload.CommunityLimit(false));
            Assert.True(CheckupWorkload.PersonalLimit(false, .4f) < CheckupWorkload.PersonalLimit(false, 1f));
            Assert.True(CheckupWorkload.PersonalLimit(false, 0f) >= 1);
        }

        [Fact] public void Each_worker_is_held_to_their_own_limit_and_gap_while_the_community_total_scales()
        {
            CheckupWorkload.TotalCapacity = 3f; var r = new FamilyRegistry(); r.Apply(new[] { T.P(1, "A"), T.P(2, "B"), T.P(3, "C") }, 100);
            var o = new CommunityOffice { Id = 1, Kind = OfficeKind.Healer, HolderId = 1 }; r.Offices.Add(o);
            int now = 10000, limit = CheckupWorkload.PersonalLimit(false, 1f);
            for (int i = 0; i < limit; i++) { Assert.True(r.MayVisit(now, false, 1, 1f)); r.NoteVisit(now, 1); now += CheckupWorkload.MinGapTicks; }
            Assert.False(r.MayVisit(now, false, 1, 1f));                           // worker 1 is done for the day
            Assert.True(r.MayVisit(now, false, 2, 1f));                            // worker 2 still has theirs
            r.NoteVisit(now, 2); Assert.False(r.MayVisit(now + 1, false, 2, 1f)); // and their own gap applies
        }

        [Fact] public void The_community_limit_stops_everyone_when_it_is_reached()
        {
            CheckupWorkload.TotalCapacity = 1f; var r = new FamilyRegistry(); r.Apply(new[] { T.P(1, "A"), T.P(2, "B") }, 100);
            r.Offices.Add(new CommunityOffice { Id = 1, Kind = OfficeKind.Healer, HolderId = 1 });
            int now = 10000;
            for (int i = 0; i < CheckupWorkload.CommunityLimit(false); i++) { r.NoteVisit(now, i % 2 + 1); now += CheckupWorkload.MinGapTicks; }
            Assert.False(r.MayVisit(now, false, 1, 1f)); Assert.False(r.MayVisit(now, false, 2, 1f));
        }

        [Fact] public void The_old_single_holder_call_is_unchanged()
        {
            var r = new FamilyRegistry(); r.Apply(new[] { T.P(1, "A") }, 100);
            r.Offices.Add(new CommunityOffice { Id = 1, Kind = OfficeKind.Healer, HolderId = 1 });
            Assert.True(r.MayVisit(10000, false));
        }
    }
}
