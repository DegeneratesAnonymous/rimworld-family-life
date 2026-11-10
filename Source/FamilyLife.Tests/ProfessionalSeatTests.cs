using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class ProfessionalSeatTests
    {
        private static FamilyRegistry Colony() { var r = new FamilyRegistry(); r.Apply(new[] { T.P(1, "A"), T.P(2, "B"), T.P(3, "C") }, 1000); return r; }

        [Fact] public void The_sitting_professional_stays_while_assigned_and_present()
        {
            Assert.Equal(2, ProfessionalSeating.Pick(new[] { 1, 2, 3 }, 2, id => true));
        }

        [Fact] public void A_colleague_covers_when_the_sitting_one_is_gone_or_no_longer_assigned()
        {
            Assert.Equal(3, ProfessionalSeating.Pick(new[] { 1, 3 }, 2, id => id != 1));          // 2 is no longer assigned, 1 is away
            Assert.Equal(3, ProfessionalSeating.Pick(new[] { 2, 3 }, 2, id => id != 2));          // 2 left
            Assert.Equal(0, ProfessionalSeating.Pick(new int[0], 2, id => true));
            Assert.Equal(0, ProfessionalSeating.Pick(new[] { 1 }, 0, id => false));
        }

        [Fact] public void Seating_works_without_any_election_and_reports_hand_overs_like_a_change_of_post()
        {
            var r = Colony(); var calls = new List<string>();
            FamilyRegistry.HolderChanged = (reg, o, a, b) => calls.Add(o.Kind + ":" + a + ">" + b);
            try
            {
                Assert.True(r.SeatService(OfficeKind.Teacher, 1, 10));
                Assert.Equal(1, r.OfficeFor(OfficeKind.Teacher).HolderId);
                Assert.False(r.SeatService(OfficeKind.Teacher, 1, 11));
                Assert.True(r.SeatService(OfficeKind.Teacher, 2, 12));
                Assert.True(r.SeatService(OfficeKind.Teacher, 0, 13));
                Assert.Equal(new[] { "Teacher:0>1", "Teacher:1>0", "Teacher:0>2", "Teacher:2>0" }, calls.ToArray());
                Assert.Empty(r.Assemblies);
            }
            finally { FamilyRegistry.HolderChanged = null; }
        }

        [Fact] public void Seating_one_person_does_not_end_their_other_posts_and_ignores_people_not_in_the_colony()
        {
            var r = Colony();
            r.AppointOffice(OfficeKind.Chaplain, 1, 5);
            r.SeatService(OfficeKind.Teacher, 1, 6);
            Assert.Equal(1, r.OfficeFor(OfficeKind.Chaplain).HolderId); Assert.Equal(1, r.OfficeFor(OfficeKind.Teacher).HolderId);
            Assert.False(r.SeatService(OfficeKind.Healer, 999, 7));
            Assert.Equal(0, r.OfficeFor(OfficeKind.Healer)?.HolderId ?? 0);
        }

        [Fact] public void Seating_a_person_in_a_post_never_changes_their_qualification_or_other_assignments()
        {
            var r = Colony(); r.Careers().Grandfather(1, "c"); r.SeatService(OfficeKind.Teacher, 1, 1); r.SeatService(OfficeKind.Teacher, 2, 2);
            Assert.True(r.Careers().IsQualified(1, "c"));
        }
    }
}
