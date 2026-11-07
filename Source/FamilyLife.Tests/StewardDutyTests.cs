using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class StewardDutyTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry House(out int family)
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Alex", spouses: new[] { 2 }), T.P(2, "Morgan", spouses: new[] { 1 }),
                T.P(3, "Casey", parents: new[] { 1, 2 }), T.P(4, "Riley", parents: new[] { 1, 2 }),
                T.P(9, "Outsider", last: "Other")
            }, 1000);
            family = r.FamilyOf(1);
            r.DelegateHouse(1, 3, family, true);
            r.BeginStewardship(3, family, 1000);
            return r;
        }

        [Fact]
        public void The_steward_does_the_work_and_the_head_covers_when_they_cannot()
        {
            var r = House(out int fam);
            Assert.Equal(3, r.DutyHolder(fam, null, out bool head));
            Assert.False(head);
            Assert.Equal(1, r.DutyHolder(fam, id => id != 3, out head));
            Assert.True(head);
            // Drafted, ordered, or an avatar the player is playing: never given duties.
            Assert.Equal(0, r.DutyHolder(fam, id => id != 3 && id != 1, out head));
        }

        [Fact]
        public void A_ceremony_duty_counts_only_after_the_ceremony_really_finished()
        {
            var r = House(out int fam);
            var request = r.RequestCeremony(CeremonyKind.GrandReunion, fam, 0, 0, 2000, 100, 5 * Day);
            Assert.True(r.TryChooseDuty(fam, 0, out var kind, out int target));
            Assert.Equal(StewardDutyKind.OrganizeCeremony, kind);
            Assert.Equal(request.Id, target);

            var duty = r.BeginStewardDuty(fam, kind, target, null, 2000);
            Assert.NotNull(duty);
            Assert.False(r.CompleteStewardDuty(duty.Id, 0, 3000));                 // nothing happened yet
            Assert.Equal(0, r.StewardshipOf(3, fam, true).Successes);

            r.MarkCeremonyStarted(request.Id, 3, 0, 3000, new[] { 1, 2, 3 });
            r.CompleteCeremony(request.Id, 4000, 3, 1, .5f);
            Assert.True(r.CompleteStewardDuty(duty.Id, 0, 4000));
            Assert.Equal(1, r.StewardshipOf(3, fam, true).CeremoniesOrganized);
        }

        [Fact]
        public void A_ceremony_organized_by_someone_else_is_not_the_stewards_success()
        {
            var r = House(out int fam);
            var request = r.RequestCeremony(CeremonyKind.GrandReunion, fam, 0, 0, 2000, 100, 5 * Day);
            var duty = r.BeginStewardDuty(fam, StewardDutyKind.OrganizeCeremony, request.Id, null, 2000);
            r.MarkCeremonyStarted(request.Id, 4, 0, 3000, new[] { 1, 2, 3, 4 });
            r.CompleteCeremony(request.Id, 4000, 4, 1, .5f);
            Assert.False(r.CompleteStewardDuty(duty.Id, 0, 4000));
            Assert.Equal(0, r.StewardshipOf(3, fam, true).Successes);
        }

        [Fact]
        public void Interrupted_and_failed_duties_never_count_as_success()
        {
            var r = House(out int fam);
            var a = r.BeginStewardDuty(fam, StewardDutyKind.GuestCare, 0, null, 2000);
            Assert.True(r.InterruptStewardDuty(a.Id, 2500));
            var term = r.StewardshipOf(3, fam, true);
            Assert.Equal(0, term.Successes); Assert.Equal(1, term.Interruptions); Assert.Equal(0, term.Failures);

            var b = r.BeginStewardDuty(fam, StewardDutyKind.GuestCare, 0, null, 2000 + Day);
            Assert.True(r.FailStewardDuty(b.Id, 2000 + Day + 10));
            Assert.Equal(0, term.Successes); Assert.Equal(1, term.Failures);
            Assert.False(r.CompleteStewardDuty(b.Id, 77, 2000 + Day + 20));            // a closed duty cannot be completed later
        }

        [Fact]
        public void Physical_work_needs_a_one_time_token()
        {
            var r = House(out int fam);
            var a = r.BeginStewardDuty(fam, StewardDutyKind.GuestCare, 0, null, 2000);
            Assert.False(r.CompleteStewardDuty(a.Id, 0, 2100));
            Assert.True(r.CompleteStewardDuty(a.Id, 555, 2100));
            var b = r.BeginStewardDuty(fam, StewardDutyKind.GuestCare, 0, null, 2000 + Day);
            Assert.False(r.CompleteStewardDuty(b.Id, 555, 2100 + Day));                // the same completion cannot be counted twice
            Assert.True(r.CompleteStewardDuty(b.Id, 556, 2100 + Day));
            Assert.Equal(2, r.StewardshipOf(3, fam, true).Successes);
        }

        [Fact]
        public void A_head_covering_for_the_steward_adds_nothing_to_the_stewards_term()
        {
            var r = House(out int fam);
            var duty = r.BeginStewardDuty(fam, StewardDutyKind.GuestCare, 0, id => id != 3, 2000);
            Assert.True(duty.HeadActed);
            Assert.Equal(1, duty.HolderId);
            Assert.True(r.CompleteStewardDuty(duty.Id, 9, 2100));
            Assert.Equal(0, r.StewardshipOf(3, fam, true).Successes);
        }

        [Fact]
        public void One_open_duty_per_holder_and_a_pause_between_them()
        {
            var r = House(out int fam);
            Assert.NotNull(r.BeginStewardDuty(fam, StewardDutyKind.GuestCare, 0, null, 2000));
            Assert.Null(r.BeginStewardDuty(fam, StewardDutyKind.SupplyShortage, 0, null, 2100));
            r.MaintainStewardDuties(2000 + 3 * Day);                                  // the first one lapses unfinished
            Assert.Equal(StewardDutyState.Interrupted, r.StewardDuties[0].State);
            Assert.NotNull(r.BeginStewardDuty(fam, StewardDutyKind.SupplyShortage, 0, null, 2000 + 3 * Day));
        }

        [Fact]
        public void A_duty_whose_holder_becomes_busy_is_interrupted_not_forced()
        {
            var r = House(out int fam);
            var d = r.BeginStewardDuty(fam, StewardDutyKind.GuestCare, 0, null, 2000);
            r.MaintainStewardDuties(2100, id => id != 3);                             // drafted
            Assert.Equal(StewardDutyState.Interrupted, d.State);
        }

        [Fact]
        public void The_steward_prepares_but_only_the_head_decides_major_matters()
        {
            var r = House(out int fam);
            Assert.False(r.MayDecide(3, fam, HouseDecision.Membership));
            Assert.False(r.MayDecide(3, fam, HouseDecision.MarriageUnion));
            Assert.False(r.MayDecide(3, fam, HouseDecision.Succession));
            Assert.True(r.MayDecide(3, fam, HouseDecision.Routine));
            Assert.True(r.MayDecide(1, fam, HouseDecision.Succession));

            var petition = r.ProposePetition(9, fam, PetitionKind.Admission, 2000);
            Assert.NotNull(petition);
            Assert.True(r.TryChooseDuty(fam, 0, out var kind, out int target));
            Assert.Equal(StewardDutyKind.ReceivePetition, kind);
            Assert.True(r.ReceivePetitionAsOffice(petition.Id, 3, 2100));
            Assert.False(r.HearPetition(petition.Id, 3, 2200));                       // hearing and deciding stay with the head
            Assert.False(r.DecidePetition(petition.Id, 3, true, 2300));
            var duty = r.BeginStewardDuty(fam, kind, target, null, 2100);
            Assert.True(r.CompleteStewardDuty(duty.Id, 0, 2400));
            Assert.Equal(1, r.StewardshipOf(3, fam, true).PetitionsHeard);
        }

        [Fact]
        public void The_office_is_optional_and_never_required()
        {
            var r = House(out int fam);
            Assert.Null(r.OfficeOf(fam));
            Assert.Null(r.UsableOffice(fam, null));
            Assert.NotNull(r.BeginStewardDuty(fam, StewardDutyKind.GuestCare, 0, null, 2000));      // duties work with no office at all
            Assert.False(r.SetFamilyOffice(fam, -1, 3, 4, 2000));
            Assert.True(r.SetFamilyOffice(fam, 2, 10, 12, 2000));
            Assert.NotNull(r.UsableOffice(fam, (m, x, z) => m == 2));
            Assert.Null(r.UsableOffice(fam, (m, x, z) => false));                     // blocked or demolished: callers use any family area
            r.PruneOffices(map => map != 2);                                          // map no longer exists
            Assert.Null(r.OfficeOf(fam));
            Assert.False(r.ClearFamilyOffice(fam));
        }

        [Fact]
        public void No_duty_is_invented_when_nothing_calls_for_one()
        {
            var r = House(out int fam);
            Assert.False(r.TryChooseDuty(fam, 0, out _, out _));
            Assert.True(r.TryChooseDuty(fam, 2, out var kind, out _));
            Assert.Equal(StewardDutyKind.GuestCare, kind);
        }

        [Fact]
        public void A_competent_steward_gains_public_regard_without_a_guaranteed_throne()
        {
            var r = House(out int fam);
            for (int i = 0; i < 5; i++)
            {
                var d = r.BeginStewardDuty(fam, StewardDutyKind.GuestCare, 0, null, 2000 + i * Day);
                Assert.True(r.CompleteStewardDuty(d.Id, 100 + i, 2100 + i * Day));
            }
            Assert.True((r.PrestigeOf(3)?.Value ?? 0f) > 0f);
            Assert.NotEqual(3, r.HeadOf(fam));                                        // service alone does not appoint anyone head
        }
    }
}
