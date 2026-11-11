using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class KinRescueTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry World(out KinVisit visit, out GatheringRequest request, bool offer = true)
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Elder"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }),
                T.P(10, "Far", parents: new[] { 1 }, presence: Presence.Away), T.P(11, "Farther", parents: new[] { 1 }, presence: Presence.Away)
            }, 1000);
            int family = r.FamilyOf(1);
            request = r.RequestCeremony(CeremonyKind.GrandReunion, family, 0, 0, 2000, 2 * Day, 12 * Day);
            request.VisitorIds.AddRange(new[] { 10, 11 });
            visit = r.PlanKinVisit(request.Id, new[] { 10, 11 }, 2000);
            visit.Complication = true;
            Assert.True(r.StrandKinVisit(visit.Id, 77, visit.ArriveTick));
            if (offer) Assert.True(r.OfferKinRescue(visit.Id, RescueComplication.Raiders, visit.ArriveTick));
            return r;
        }

        private static int Offered(KinVisit v) => v.ArriveTick;

        // ---------- the offer ----------

        [Fact]
        public void A_held_up_trip_becomes_an_offer_with_its_own_expiry_inside_the_overall_time_limit()
        {
            var r = World(out var v, out _);
            Assert.Equal(RescueStage.Offered, v.Rescue);
            Assert.Equal(RescueComplication.Raiders, v.Trouble);
            Assert.True(v.OfferExpiryTick > Offered(v) && v.OfferExpiryTick <= v.RescueDeadlineTick);
            Assert.True(v.RescueOpen);
            Assert.True(r.KinVisitBlocks(v.RequestId));                       // the reunion waits while the offer stands
        }

        [Fact]
        public void An_offer_is_made_once_and_only_for_a_stranded_party()
        {
            var r = World(out var v, out _);
            Assert.False(r.OfferKinRescue(v.Id, RescueComplication.Weather, Offered(v)));
            var other = new FamilyRegistry();
            Assert.False(other.OfferKinRescue(1, RescueComplication.Weather, 0));
        }

        [Fact]
        public void The_trouble_is_chosen_from_what_fits_and_the_same_seed_always_gives_the_same_answer()
        {
            var weights = new Dictionary<RescueComplication, int> { [RescueComplication.Weather] = 3, [RescueComplication.Animals] = 0, [RescueComplication.Injury] = 1 };
            var seen = new HashSet<RescueComplication>();
            for (int seed = 0; seed < 40; seed++)
            {
                var a = FamilyRegistry.ChooseTrouble(seed, weights);
                Assert.Equal(a, FamilyRegistry.ChooseTrouble(seed, weights));
                seen.Add(a);
            }
            Assert.Contains(RescueComplication.Weather, seen); Assert.Contains(RescueComplication.Injury, seen);
            Assert.DoesNotContain(RescueComplication.Animals, seen);          // weight zero: it does not fit here
            Assert.Equal(RescueComplication.BrokenTransport, FamilyRegistry.ChooseTrouble(5, null));
            Assert.Equal(RescueComplication.BrokenTransport, FamilyRegistry.ChooseTrouble(5, new Dictionary<RescueComplication, int>()));
        }

        // ---------- accepting, ignoring, declining ----------

        [Fact]
        public void Accepting_starts_the_clock_for_the_caravan_and_is_not_possible_twice_or_after_the_offer_lapsed()
        {
            var r = World(out var v, out _);
            int t = Offered(v) + Day;
            Assert.True(r.AcceptKinRescue(v.Id, 500, t));
            Assert.Equal(RescueStage.Accepted, v.Rescue);
            Assert.Equal(t + FamilyRegistry.KinSiteTicks, v.RescueDeadlineTick);
            Assert.False(r.AcceptKinRescue(v.Id, 501, t + 10));
            var r2 = World(out var late, out _);
            Assert.False(r2.AcceptKinRescue(late.Id, 500, late.OfferExpiryTick + 1));
        }

        [Fact]
        public void Accepting_at_the_last_moment_still_leaves_a_full_window_and_a_site_that_resolves()
        {
            var r = World(out var v, out var request);
            Assert.True(r.AcceptKinRescue(v.Id, 500, v.OfferExpiryTick));
            Assert.Equal(v.OfferExpiryTick + FamilyRegistry.KinSiteTicks, v.RescueDeadlineTick);
            Assert.True(request.Expires > v.RescueDeadlineTick);              // the reunion cannot lapse underneath the rescue
            r.MaintainKinVisits(v.RescueDeadlineTick + 1);
            Assert.Equal(KinVisitState.Failed, v.State);
        }

        [Fact]
        public void Ignoring_the_offer_ends_it_and_lets_the_reunion_go_ahead_without_them()
        {
            var r = World(out var v, out var request);
            r.MaintainKinVisits(v.OfferExpiryTick);
            Assert.Equal(KinVisitState.Stranded, v.State);                    // still open on the last tick
            r.MaintainKinVisits(v.OfferExpiryTick + 1);
            Assert.Equal(KinVisitState.Failed, v.State);
            Assert.Equal(RescueStage.Expired, v.Rescue);
            Assert.Empty(request.VisitorIds);
            Assert.False(r.KinVisitBlocks(request.Id));
            Assert.Contains(r.DueRequests(request.NotBefore + 10), x => x.Id == request.Id);
        }

        [Fact]
        public void Declining_ends_it_once_and_unblocks_the_reunion()
        {
            var r = World(out var v, out var request);
            Assert.True(r.FailKinRescue(v.Id, RescueStage.Declined, Offered(v) + 100));
            Assert.Equal(RescueStage.Declined, v.Rescue);
            Assert.Empty(request.VisitorIds);
            Assert.False(r.FailKinRescue(v.Id, RescueStage.Abandoned, Offered(v) + 200));      // never a second ending
            Assert.Equal(RescueStage.Declined, v.Rescue);
            Assert.False(r.AcceptKinRescue(v.Id, 1, Offered(v) + 300));
        }

        // ---------- on site ----------

        [Fact]
        public void Arriving_before_the_deadline_gives_time_to_put_things_right_and_arriving_after_does_not_count()
        {
            var r = World(out var v, out _);
            r.AcceptKinRescue(v.Id, 500, Offered(v));
            int almost = v.RescueDeadlineTick - 5;
            Assert.True(r.ReachKinRescue(v.Id, almost));
            Assert.Equal(RescueStage.OnSite, v.Rescue);
            Assert.True(v.RescueDeadlineTick >= almost + FamilyRegistry.KinOnSiteTicks - 1);
            Assert.True(r.ReachKinRescue(v.Id, almost + 10));                  // re-entering is harmless
            Assert.Equal(1, v.SiteVisits);

            var r2 = World(out var late, out _);
            r2.AcceptKinRescue(late.Id, 500, Offered(late));
            Assert.False(r2.ReachKinRescue(late.Id, late.RescueDeadlineTick + 1));
        }

        [Fact]
        public void Leaving_without_helping_keeps_them_waiting_until_the_time_runs_out_then_it_is_abandonment()
        {
            var r = World(out var v, out var request);
            r.AcceptKinRescue(v.Id, 500, Offered(v));
            r.ReachKinRescue(v.Id, Offered(v) + 100);
            Assert.True(r.LeaveKinRescue(v.Id));
            Assert.Equal(RescueStage.Accepted, v.Rescue);
            Assert.False(r.LeaveKinRescue(v.Id));
            r.MaintainKinVisits(v.RescueDeadlineTick + 1);
            Assert.Equal(KinVisitState.Failed, v.State);
            Assert.Equal(RescueStage.Abandoned, v.Rescue);
            Assert.Empty(request.VisitorIds);
        }

        [Fact]
        public void A_rescue_credits_the_actual_rescuers_improves_trust_and_sets_a_believable_delay()
        {
            var r = World(out var v, out var request);
            r.AcceptKinRescue(v.Id, 500, Offered(v));
            r.ReachKinRescue(v.Id, Offered(v) + 100);
            float before = r.GetBond(10, 2)?.Trust ?? 0f;
            int now = Offered(v) + Day;
            Assert.True(r.CompleteKinRescue(v.Id, new[] { 2, 99 }, null, now));         // 99 is nobody in the colony
            Assert.Equal(KinVisitState.Travelling, v.State);
            Assert.Equal(RescueStage.Succeeded, v.Rescue);
            Assert.Equal(new[] { 2 }, v.RescuerIds);
            Assert.True(r.GetBond(10, 2).Trust > before);
            Assert.Null(r.GetBond(10, 3));                                     // someone who did not come gets nothing
            Assert.True(v.ArriveTick >= now + Day);
            Assert.True(request.NotBefore >= v.ArriveTick);
            Assert.NotEmpty(r.Incidents.Where(i => i.Kind == IncidentKind.Gift && i.ActorId == 2));
        }

        [Fact]
        public void A_rescue_resolves_exactly_once_whatever_reports_it()
        {
            var r = World(out var v, out _);
            r.AcceptKinRescue(v.Id, 500, Offered(v));
            int now = Offered(v) + Day;
            Assert.True(r.CompleteKinRescue(v.Id, new[] { 2 }, null, now));
            Assert.False(r.CompleteKinRescue(v.Id, new[] { 3 }, null, now + 1));
            Assert.False(r.RescueKinVisit(v.Id, new[] { 3 }, now + 1));
            Assert.False(r.FailKinRescue(v.Id, RescueStage.Abandoned, now + 1));
            Assert.Equal(new[] { 2 }, v.RescuerIds);
            r.MaintainKinVisits(now + 50 * Day);
            Assert.Equal(KinVisitState.Travelling, v.State);
        }

        [Fact]
        public void A_rescue_after_the_deadline_or_before_acceptance_does_not_count()
        {
            var r = World(out var v, out _);
            Assert.False(r.CompleteKinRescue(v.Id, new[] { 2 }, null, Offered(v) + 100));           // still only an offer
            r.AcceptKinRescue(v.Id, 500, Offered(v));
            Assert.False(r.CompleteKinRescue(v.Id, new[] { 2 }, null, v.RescueDeadlineTick + 1));
        }

        [Fact]
        public void Partial_survival_removes_the_dead_from_the_guest_list_and_the_rest_carry_on()
        {
            var r = World(out var v, out var request);
            r.AcceptKinRescue(v.Id, 500, Offered(v));
            Assert.True(r.CompleteKinRescue(v.Id, new[] { 2 }, new[] { 11 }, Offered(v) + Day));
            Assert.Equal(new[] { 10 }, v.KinIds);
            Assert.Equal(new[] { 10 }, request.VisitorIds);
            Assert.Contains(11, v.LostIds);
            Assert.Null(r.GetBond(11, 2));                                      // no thanks owed by someone who did not survive
        }

        [Fact]
        public void If_everyone_died_the_rescue_ends_as_a_loss_and_the_reunion_continues()
        {
            var r = World(out var v, out var request);
            r.AcceptKinRescue(v.Id, 500, Offered(v));
            Assert.True(r.CompleteKinRescue(v.Id, new[] { 2 }, new[] { 10, 11 }, Offered(v) + Day));
            Assert.Equal(KinVisitState.Failed, v.State);
            Assert.Equal(RescueStage.Lost, v.Rescue);
            Assert.Empty(request.VisitorIds);
            Assert.False(r.KinVisitBlocks(request.Id));
        }

        [Fact]
        public void Relatives_who_die_while_the_offer_stands_are_taken_off_and_a_dead_party_ends_the_offer()
        {
            var r = World(out var v, out var request);
            r.Apply(new[] { T.P(1, "Elder"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }), T.P(10, "Far", parents: new[] { 1 }, presence: Presence.Dead), T.P(11, "Farther", parents: new[] { 1 }, presence: Presence.Away) }, Offered(v) + 100);
            r.MaintainKinVisits(Offered(v) + 200);
            Assert.Equal(new[] { 11 }, v.KinIds);
            Assert.Equal(new[] { 11 }, request.VisitorIds);
            Assert.Equal(KinVisitState.Stranded, v.State);
            r.Apply(new[] { T.P(1, "Elder"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }), T.P(10, "Far", parents: new[] { 1 }, presence: Presence.Dead), T.P(11, "Farther", parents: new[] { 1 }, presence: Presence.Dead) }, Offered(v) + 300);
            r.MaintainKinVisits(Offered(v) + 400);
            Assert.Equal(KinVisitState.Failed, v.State);
            Assert.Equal(RescueStage.Lost, v.Rescue);
            Assert.Empty(request.VisitorIds);
        }

        [Fact]
        public void The_game_can_remove_relatives_who_became_hostile_or_were_captured_without_touching_the_others()
        {
            var r = World(out var v, out var request);
            Assert.True(r.RemoveKinFromVisit(v.Id, new[] { 10 }, Offered(v) + 10));
            Assert.Equal(new[] { 11 }, v.KinIds);
            Assert.False(r.RemoveKinFromVisit(v.Id, new[] { 10 }, Offered(v) + 20));
            Assert.True(r.RemoveKinFromVisit(v.Id, new[] { 11 }, Offered(v) + 30));
            Assert.Equal(KinVisitState.Failed, v.State);
            Assert.Empty(request.VisitorIds);
        }

        [Fact]
        public void A_cancelled_reunion_ends_the_rescue_too()
        {
            var r = World(out var v, out var request);
            request.State = CeremonyState.Cancelled;
            r.MaintainKinVisits(Offered(v) + 100);
            Assert.False(v.RescueOpen);
            Assert.Equal(KinVisitState.Expired, v.State);
        }

        [Fact]
        public void A_party_that_reaches_the_colony_after_a_rescue_still_arrives_by_road_or_shuttle()
        {
            var r = World(out var v, out var request);
            r.AcceptKinRescue(v.Id, 500, Offered(v));
            r.CompleteKinRescue(v.Id, new[] { 2 }, null, Offered(v) + Day);
            Assert.False(r.MarkKinArrived(v.Id, new[] { 10, 11 }, v.ArriveTick - 1));
            Assert.True(r.MarkKinLanding(v.Id, new[] { 10, 11 }, new[] { 10, 11 }, v.ArriveTick));        // the shuttle path
            Assert.Equal(KinVisitState.Landing, v.State);
            Assert.True(r.MarkKinArrived(v.Id, new[] { 10, 11 }, v.ArriveTick + 100));
            Assert.Equal(KinVisitState.Arrived, v.State);

            var r2 = World(out var v2, out _);
            r2.AcceptKinRescue(v2.Id, 500, Offered(v2));
            r2.CompleteKinRescue(v2.Id, new[] { 2 }, null, Offered(v2) + Day);
            Assert.True(r2.MarkKinArrived(v2.Id, new[] { 10, 11 }, v2.ArriveTick));                      // the road path
        }

        // ---------- older saves ----------

        [Fact]
        public void A_party_stranded_before_quests_existed_still_resolves_by_the_old_rules()
        {
            var r = World(out var v, out var request, offer: false);
            Assert.Equal(RescueStage.None, v.Rescue);
            Assert.True(v.State == KinVisitState.Stranded);
            Assert.True(r.RescueKinVisit(v.Id, new[] { 2 }, Offered(v) + Day));
            Assert.Equal(KinVisitState.Travelling, v.State);

            var r2 = World(out var v2, out var req2, offer: false);
            r2.MaintainKinVisits(v2.RescueDeadlineTick + 1);
            Assert.Equal(KinVisitState.Failed, v2.State);
            Assert.Empty(req2.VisitorIds);
        }

        [Fact]
        public void Binding_a_quest_is_idempotent_and_only_for_an_open_rescue()
        {
            var r = World(out var v, out _);
            Assert.True(r.BindKinRescueQuest(v.Id, 42));
            Assert.True(r.BindKinRescueQuest(v.Id, 42));
            Assert.False(r.BindKinRescueQuest(v.Id, 43));
            r.FailKinRescue(v.Id, RescueStage.Declined, Offered(v) + 1);
            Assert.False(r.BindKinRescueQuest(v.Id, 42));
        }

        [Fact]
        public void Maintenance_is_idempotent()
        {
            var r = World(out var v, out _);
            r.AcceptKinRescue(v.Id, 500, Offered(v));
            string Snap() => $"{v.State}/{v.Rescue}/{v.KinIds.Count}/{v.RescueDeadlineTick}";
            r.MaintainKinVisits(Offered(v) + 50);
            string once = Snap();
            for (int i = 0; i < 4; i++) r.MaintainKinVisits(Offered(v) + 50);
            Assert.Equal(once, Snap());
        }
    }
}
