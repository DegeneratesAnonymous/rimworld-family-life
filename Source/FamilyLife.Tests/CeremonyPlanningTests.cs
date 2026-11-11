using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class CeremonyPlanningTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry World(out int family, out GatheringRequest request, int now = 10000)
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Alex", spouses: new[] { 2 }), T.P(2, "Morgan", spouses: new[] { 1 }),
                T.P(3, "Casey", parents: new[] { 1, 2 }), T.P(4, "Riley", parents: new[] { 1, 2 }), T.P(5, "Sam", parents: new[] { 1, 2 }),
                T.P(6, "Far", parents: new[] { 1, 2 }, presence: Presence.Away),
                T.P(30, "Foe", last: "Jones"), T.P(40, "Stranger", last: "Other")
            }, 1000);
            family = r.FamilyOf(1);
            request = r.RequestCeremony(CeremonyKind.GrandReunion, family, 0, 0, now, Day / 2, 8 * Day);
            request.VisitorIds.Add(6);
            return r;
        }

        private static PlanContext Ctx(int avatar = -1, int arrival6 = 3000) => new PlanContext
        {
            ArrivalEstimate = id => id == 6 ? arrival6 : 0,
            IsAvatar = id => id == avatar,
            DefaultMapId = 0
        };

        [Fact]
        public void The_card_starts_with_the_familys_own_choices()
        {
            var r = World(out int fam, out var req);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, Ctx());

            Assert.NotNull(plan);
            Assert.Equal(1, plan.OrganizerId);                              // the head, no Steward yet
            Assert.Equal(req.NotBefore, plan.ScheduledTick);
            Assert.Contains(plan.Invitees, i => i.PersonId == 3 && i.Source == InviteSource.Family);
            var far = plan.Invitees.Single(i => i.PersonId == 6);
            Assert.Equal(InviteSource.Distant, far.Source);
            Assert.Equal(10000 + 3000, far.ArrivalTick);
            Assert.Same(plan, r.OpenCeremonyPlan(req.Id, 10050, Ctx()));    // idempotent
        }

        [Fact]
        public void An_active_steward_organizes_by_default_and_the_subject_never_does()
        {
            var r = World(out int fam, out var req);
            r.DelegateHouse(1, 4, fam, true); r.BeginStewardship(4, fam, 5000);
            Assert.Equal(4, r.OpenCeremonyPlan(req.Id, 10000, Ctx()).OrganizerId);

            var funeral = r.RequestCeremony(CeremonyKind.Funeral, fam, 4, 0, 10000, Day / 2, 4 * Day);
            Assert.NotEqual(4, r.DefaultOrganizer(funeral, Ctx()));
        }

        [Fact]
        public void Ignoring_the_card_never_stalls_the_occasion()
        {
            var r = World(out int fam, out var req);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, Ctx());
            Assert.DoesNotContain(r.DueRequests(req.NotBefore + 10), x => x.Id == req.Id);   // held while the card is open

            r.AdvanceCeremonyPlans(plan.DeadlineTick, Ctx());

            Assert.Equal(PlanState.Defaulted, plan.State);
            Assert.Contains(r.DueRequests(plan.ScheduledTick + 1), x => x.Id == req.Id);
        }

        [Fact]
        public void Scheduling_is_limited_to_the_window_and_applied_on_confirm()
        {
            var r = World(out int fam, out var req);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, Ctx());
            Assert.False(r.SetPlanSchedule(plan.Id, plan.EarliestTick - 1, 10000));
            Assert.False(r.SetPlanSchedule(plan.Id, plan.LatestTick + 1, 10000));
            int chosen = plan.EarliestTick + Day;
            Assert.True(r.SetPlanSchedule(plan.Id, chosen, 10000));
            Assert.True(r.ConfirmPlan(plan.Id, 10100, Ctx()));
            Assert.Equal(PlanState.Confirmed, plan.State);
            Assert.DoesNotContain(r.DueRequests(chosen - 1), x => x.Id == req.Id);
            Assert.Contains(r.DueRequests(chosen), x => x.Id == req.Id);
            Assert.False(r.SetPlanSchedule(plan.Id, chosen, 10100));                 // closed once confirmed
        }

        [Fact]
        public void The_closed_reunion_card_is_the_single_source_of_truth_for_travelling_kin()
        {
            var r = World(out _, out var req);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, Ctx());
            Assert.True(r.UninvitePlanPerson(plan.Id, 6));
            Assert.True(r.ConfirmPlan(plan.Id, 10100, Ctx()));
            Assert.Empty(req.VisitorIds);
        }

        [Fact]
        public void A_distant_guest_who_cannot_arrive_in_time_is_listed_with_the_reason()
        {
            var r = World(out int fam, out var req);
            var ctx = Ctx(arrival6: 10 * Day);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, ctx);

            var candidate = r.PlanCandidates(req, plan, 10000, ctx).Single(c => c.PersonId == 6);
            Assert.True(candidate.Barriers.HasFlag(InviteBarrier.TooFarForWindow));
            Assert.DoesNotContain(plan.Invitees, i => i.PersonId == 6);
            Assert.False(r.InvitePlanPerson(plan.Id, 6, 10000, ctx));

            var none = new PlanContext { DefaultMapId = 0 };                          // no travel estimate at all
            Assert.True(r.PlanCandidates(req, plan, 10000, none).Single(c => c.PersonId == 6).Barriers.HasFlag(InviteBarrier.CannotTravel));
        }

        [Fact]
        public void A_hostile_house_is_not_invited()
        {
            var r = World(out int fam, out var req);
            int foe = r.FamilyOf(30);
            r.Ties.Add(new FamilyTie { LineageA = System.Math.Min(fam, foe), LineageB = System.Math.Max(fam, foe), Grievance = 70f });
            var plan = r.OpenCeremonyPlan(req.Id, 10000, Ctx());
            r.HouseContacts.Add(new HouseContact { PersonId = 30, FamilyId = fam, Respect = 10f });
            var item = r.PlanCandidates(req, plan, 10000, Ctx()).Single(c => c.PersonId == 30);
            Assert.True(item.Barriers.HasFlag(InviteBarrier.Hostile));
            Assert.DoesNotContain(plan.Invitees, i => i.PersonId == 30);
        }

        [Fact]
        public void A_lost_organizer_a_dead_guest_and_a_bad_room_recover_without_stopping()
        {
            var r = World(out int fam, out var req);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, Ctx());
            Assert.True(r.SetPlanVenue(plan.Id, 7, 3, new PlanContext { DefaultMapId = 0 }));
            r.ConfirmPlan(plan.Id, 10100, Ctx());

            r.Apply(new[]
            {
                T.P(1, "Alex", spouses: new[] { 2 }), T.P(2, "Morgan", spouses: new[] { 1 }), T.P(3, "Casey", parents: new[] { 1, 2 }, presence: Presence.Dead),
                T.P(4, "Riley", parents: new[] { 1, 2 }), T.P(5, "Sam", parents: new[] { 1, 2 }), T.P(6, "Far", parents: new[] { 1, 2 }, presence: Presence.Away)
            }, 11000);
            var changed = new PlanContext { Available = id => id != 1, VenueValid = (m, a) => false, ArrivalEstimate = id => 100, DefaultMapId = 0 };
            r.AdvanceCeremonyPlans(11000, changed);

            Assert.NotEqual(1, plan.OrganizerId);
            Assert.NotEqual(0, plan.OrganizerId);
            Assert.Equal(0, plan.VenueMapId);
            Assert.Equal(InviteStatus.Unavailable, plan.Invitees.Single(i => i.PersonId == 3).Status);
            Assert.Contains(r.DueRequests(plan.ScheduledTick + 1), x => x.Id == req.Id);
        }

        [Fact]
        public void A_schedule_that_passes_moves_forward_or_the_request_ends_cleanly()
        {
            var r = World(out int fam, out var req);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, Ctx());
            r.ConfirmPlan(plan.Id, 10100, Ctx());
            int late = plan.ScheduledTick + 2 * Day;
            r.AdvanceCeremonyPlans(late, Ctx());
            Assert.True(plan.ScheduledTick >= late);

            r.AdvanceCeremonyPlans(req.Expires + 1, Ctx());
            Assert.DoesNotContain(r.GatheringRequests, x => x.Id == req.Id);
            Assert.Null(r.PlanFor(req.Id));
        }

        [Fact]
        public void The_avatar_is_asked_never_assumed()
        {
            var r = World(out int fam, out var req);
            var ctx = Ctx(avatar: 3);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, ctx);
            Assert.NotEqual(3, plan.OrganizerId);                          // never chosen on the avatar's behalf
            Assert.False(r.SetPlanOrganizer(plan.Id, 3, ctx));             // not even by the player, before the avatar agrees
            Assert.True(r.AskAvatarToLead(plan.Id, 3, ctx));
            Assert.False(r.SetPlanOrganizer(plan.Id, 3, ctx));             // asked is not agreed

            r.AdvanceCeremonyPlans(plan.DeadlineTick, ctx);                // silence
            Assert.Equal(AvatarAsk.Unanswered, plan.Ask);
            Assert.NotEqual(3, plan.OrganizerId);
            Assert.False(r.PlanMayDirect(plan, 3, ctx));                   // the game never moves the avatar
            Assert.True(r.PlanMayDirect(plan, 4, ctx));
        }

        [Fact]
        public void The_avatar_can_choose_to_lead()
        {
            var r = World(out int fam, out var req);
            var ctx = Ctx(avatar: 3);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, ctx);
            r.AskAvatarToLead(plan.Id, 3, ctx);
            Assert.True(r.AnswerAvatar(plan.Id, 3, true, ctx));
            Assert.Equal(3, plan.OrganizerId);
            Assert.True(r.PlanMayDirect(plan, 3, ctx));
        }

        [Fact]
        public void A_declined_request_changes_nothing()
        {
            var r = World(out int fam, out var req);
            var ctx = Ctx(avatar: 3);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, ctx);
            int before = plan.OrganizerId;
            r.AskAvatarToLead(plan.Id, 3, ctx);
            Assert.True(r.AnswerAvatar(plan.Id, 3, false, ctx));
            Assert.Equal(before, plan.OrganizerId);
            Assert.Equal(AvatarAsk.Declined, plan.Ask);
        }

        [Fact]
        public void Invitations_are_only_visible_to_those_who_were_told()
        {
            var r = World(out int fam, out var req);
            var plan = r.OpenCeremonyPlan(req.Id, 10000, Ctx());
            Assert.True(r.PlanVisibleTo(plan, 0));                         // the omniscient view
            Assert.True(r.PlanVisibleTo(plan, 1));
            Assert.True(r.PlanVisibleTo(plan, 3));
            Assert.False(r.PlanVisibleTo(plan, 40));
            Assert.True(r.InvitePlanPerson(plan.Id, 40, 10000, new PlanContext { DefaultMapId = 0, ArrivalEstimate = id => 0 }));
            Assert.True(r.PlanVisibleTo(plan, 40));
        }

        [Fact]
        public void Edits_to_a_closed_card_and_unknown_cards_are_refused()
        {
            var r = World(out int fam, out var req);
            Assert.False(r.SetPlanSchedule(999, 20000, 10000));
            Assert.False(r.ConfirmPlan(999, 10000));
            var plan = r.OpenCeremonyPlan(req.Id, 10000, Ctx());
            r.ConfirmPlan(plan.Id, 10100, Ctx());
            Assert.False(r.UninvitePlanPerson(plan.Id, 3));
            Assert.False(r.InvitePlanPerson(plan.Id, 5, 10100, Ctx()));
            Assert.False(r.SetPlanOrganizer(plan.Id, 5, Ctx()));
        }
    }
}
