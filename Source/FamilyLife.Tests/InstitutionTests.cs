using System;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class InstitutionTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        private static FamilyRegistry Colony(out int houseA, out int houseB, out int houseC)
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Elder"), T.P(2, "Two", parents: new[] { 1 }), T.P(3, "Three", parents: new[] { 1 }), T.P(4, "Four", parents: new[] { 1 }), T.P(5, "Five", parents: new[] { 1 }),
                T.P(20, "Solo", last: "Other"), T.P(22, "Third", last: "Third"),
                T.P(31, "Six", parents: new[] { 1 }), T.P(32, "Seven", parents: new[] { 1 }), T.P(33, "Eight", parents: new[] { 1 })
            }, 1000);
            houseA = r.FamilyOf(1); houseB = r.FamilyOf(20); houseC = r.FamilyOf(22);
            return r;
        }

        [Fact]
        public void Conviction_and_family_attachment_are_kept_apart()
        {
            var r = Colony(out int a, out _, out _);
            r.ChangeBond(2, 3, new BondChange(80f, 40f, 0f, 0f), 2000);
            var disagree = r.Support(2, 3, new PoliticalContext { IdeologyFit = (v, c) => -.8f });
            Assert.Equal(-.8f, disagree.Dims.Ideology, 3);
            Assert.True(disagree.Dims.Family >= .4f);                           // attachment is untouched by disagreement
            var agree = r.Support(2, 3, new PoliticalContext { IdeologyFit = (v, c) => .8f });
            Assert.Equal(disagree.Dims.Family, agree.Dims.Family, 3);
            Assert.True(agree.Total > disagree.Total);
        }

        [Fact]
        public void Fear_helps_a_ruler_under_authority_and_hurts_a_candidate_in_an_election()
        {
            var r = Colony(out _, out _, out _);
            r.ChangeBond(2, 3, new BondChange(0f, 0f, 80f, 0f), 2000);
            r.SetInstitution(InstitutionKind.Authoritarian, 3, 3000);
            float ruled = r.Support(2, 3, null).Total;
            r.SetInstitution(InstitutionKind.Election, 3, 3000);
            float elected = r.Support(2, 3, null).Total;
            Assert.True(ruled > elected);
        }

        [Fact]
        public void More_relatives_do_not_guarantee_an_election()
        {
            var r = Colony(out int a, out int b, out _);
            r.SetInstitution(InstitutionKind.Election, 1, 3000);
            var election = r.CallElection(new[] { 3, 20 }, 3000);
            Assert.NotNull(election);
            var ctx = new PoliticalContext { IdeologyFit = (voter, cand) => cand == 3 ? -.9f : .9f };   // the large house disagrees with its own candidate
            r.AdvanceElections(3000 + Day * 3, ctx);

            Assert.Equal(ElectionState.Counted, election.State);
            Assert.Equal(20, election.WinnerId);
            Assert.Equal(20, r.Institution.LeaderId);
            Assert.True(r.MembersOf(a).Count > r.MembersOf(b).Count);
            Assert.True(r.Institution.TermEndTick > 3000);
        }

        [Fact]
        public void A_tight_race_goes_to_the_better_supported_and_abstentions_are_possible()
        {
            var r = Colony(out _, out _, out _);
            r.SetInstitution(InstitutionKind.Election, 1, 3000);
            var election = r.CallElection(new[] { 3, 20 }, 3000);
            r.AdvanceElections(3000 + Day * 3, new PoliticalContext { IdeologyFit = (v, c) => 0f });
            Assert.Equal(ElectionState.Counted, election.State);
            Assert.True(election.Abstained >= 0);
            Assert.Equal(election.Votes.Count, election.Votes.Count(v => v.CandidateId == 3 || v.CandidateId == 20));
        }

        [Fact]
        public void An_endorsement_persuades_through_trust_not_house_size()
        {
            var r = Colony(out int a, out int b, out int c);
            r.SetInstitution(InstitutionKind.Election, 1, 3000);
            var election = r.CallElection(new[] { 3, 20 }, 3000);
            r.ChangeBond(4, 22, new BondChange(0f, 80f, 0f, 0f), 3000);
            Assert.True(r.EndorseCandidate(election.Id, c, 22, 20));
            Assert.False(r.EndorseCandidate(election.Id, c, 1, 20));            // only the house's own head can speak for it
            float trusting = r.Support(4, 20, null, election).Dims.Endorsement;
            float stranger = r.Support(5, 20, null, election).Dims.Endorsement;
            Assert.True(trusting > stranger);
            Assert.True(stranger <= .001f);
        }

        [Fact]
        public void An_election_opens_when_a_term_ends_and_not_before()
        {
            var r = Colony(out _, out _, out _);
            r.SetInstitution(InstitutionKind.Election, 3, 3000);
            int end = r.Institution.TermEndTick;
            r.AdvanceElections(end - 5, null);
            Assert.Empty(r.Elections);
            r.AdvanceElections(end + 5, null);
            Assert.True(r.Elections.Any() || r.Institution.TermEndTick > end);
        }

        [Fact]
        public void Votes_are_secret_but_reasons_in_total_are_public()
        {
            var r = Colony(out _, out _, out _);
            r.SetInstitution(InstitutionKind.Election, 1, 3000);
            var election = r.CallElection(new[] { 3, 20 }, 3000);
            r.AdvanceElections(3000 + Day * 3, new PoliticalContext { IdeologyFit = (v, c) => c == 20 ? .8f : -.2f });
            var summary = r.ExplainElection(election.Id, 2, out var own);
            Assert.NotEmpty(summary);
            Assert.Equal(election.Votes.Count, summary.Sum(s => s.Value));
            Assert.NotNull(own);
            Assert.Equal(2, own.VoterId);
            r.ExplainElection(election.Id, 0, out var omniscient);
            Assert.Null(omniscient);                                             // the overview shows totals only, not individual ballots
        }

        [Fact]
        public void A_council_gives_every_house_a_seat_ranked_by_standing_not_headcount()
        {
            var r = Colony(out int a, out int b, out int c);
            r.GetLineage(b).Standing = 40f; r.GetLineage(a).Standing = 0f; r.GetLineage(c).Standing = 10f;
            Assert.Equal(new[] { b }, r.FormCouncil(0, 1));
            var all = r.FormCouncil(0, 7);
            Assert.Equal(b, all[0]);
            Assert.Contains(a, all); Assert.Contains(c, all);
            Assert.True(r.SetInstitution(InstitutionKind.Council, 0, 3000));
            Assert.Equal(all.Count, r.Institution.CouncilHouseIds.Count);
        }

        [Fact]
        public void Authority_needs_a_real_adult_ruler()
        {
            var r = Colony(out _, out _, out _);
            Assert.False(r.SetInstitution(InstitutionKind.Authoritarian, 999, 3000));
            Assert.False(r.SetInstitution(InstitutionKind.Authoritarian, 0, 3000));
            Assert.True(r.SetInstitution(InstitutionKind.Consensus, 0, 3000));
        }

        [Fact]
        public void Political_change_needs_a_majority_of_houses_not_an_overseer_toggle()
        {
            var r = Colony(out int a, out int b, out int c);
            var movement = r.BeginInstitutionMovement(InstitutionKind.Council, r.HeadOf(a), 40 * Day);
            Assert.NotNull(movement); Assert.Equal(InstitutionKind.Consensus, r.Institution.Kind);
            Assert.True(r.AnswerInstitutionMovement(movement.Id, b, r.HeadOf(b), true, 40 * Day + 10));
            Assert.Equal(InstitutionMovementState.Adopted, movement.State);
            Assert.Equal(InstitutionKind.Council, r.Institution.Kind);
            Assert.False(r.AnswerInstitutionMovement(movement.Id, c, r.HeadOf(c), false, 40 * Day + 20));
        }

        [Fact]
        public void A_growing_consensus_colony_organically_asks_for_a_council()
        {
            var r = Colony(out _, out _, out _);
            r.Institution.SinceTick = 1000;
            var movement = r.ConsiderInstitutionMovement(40 * Day);
            Assert.NotNull(movement); Assert.Equal(InstitutionKind.Council, movement.Target);
            Assert.Equal(InstitutionKind.Consensus, r.Institution.Kind);                 // proposing does not enact it
        }

        [Fact]
        public void An_unanswered_movement_expires_without_changing_government()
        {
            var r = Colony(out int a, out _, out _);
            var movement = r.BeginInstitutionMovement(InstitutionKind.Council, r.HeadOf(a), 40 * Day);
            r.AdvanceInstitutionMovements(movement.DeadlineTick + 1);
            Assert.Equal(InstitutionMovementState.Rejected, movement.State);
            Assert.Equal(InstitutionKind.Consensus, r.Institution.Kind);
        }

        // ---------------- coups ----------------

        private static FamilyRegistry Ruled(out CoupPlan coup, int start = 5 * Day)
        {
            var r = Colony(out _, out _, out _);
            r.SetInstitution(InstitutionKind.Authoritarian, 1, 1000);
            coup = r.PlanCoup(2, CoupGoal.ReplaceLeader, 0, null, start);
            Assert.NotNull(coup);
            foreach (int p in new[] { 3, 4, 5 }) Assert.True(r.RecruitForCoup(coup.Id, p, RecruitAnswer.Join, start));
            return r;
        }

        [Fact]
        public void A_coup_needs_a_concrete_goal_and_a_ruler_to_oppose()
        {
            var r = Colony(out _, out _, out _);
            Assert.Null(r.PlanCoup(2, CoupGoal.ReplaceLeader, 0, null, 5 * Day));       // nobody rules yet
            r.SetInstitution(InstitutionKind.Authoritarian, 1, 1000);
            Assert.Null(r.PlanCoup(2, CoupGoal.ReversePolicy, 0, null, 5 * Day));       // a policy coup must name the policy
            Assert.Null(r.PlanCoup(1, CoupGoal.ReplaceLeader, 0, null, 5 * Day));       // the ruler cannot depose themself
            var coup = r.PlanCoup(2, CoupGoal.ReversePolicy, 7, null, 5 * Day);
            Assert.NotNull(coup);
            Assert.Null(r.PlanCoup(3, CoupGoal.ReplaceLeader, 0, null, 5 * Day + 10));   // scheming is spaced out, not constant
        }

        [Fact]
        public void Too_few_backers_and_the_plan_lapses()
        {
            var r = Colony(out _, out _, out _);
            r.SetInstitution(InstitutionKind.Authoritarian, 1, 1000);
            var coup = r.PlanCoup(2, CoupGoal.ReplaceLeader, 0, null, 5 * Day);
            r.AdvanceCoup(coup.Id, null, 5 * Day + 10);
            Assert.Equal(CoupStage.Recruiting, coup.Stage);
            r.AdvanceCoup(coup.Id, null, 12 * Day);
            Assert.Equal(CoupState.Abandoned, coup.State);
            Assert.Equal(1, r.Institution.LeaderId);
        }

        [Fact]
        public void Numbers_without_the_means_never_win()
        {
            var r = Ruled(out var coup);
            foreach (int p in new[] { 31, 32, 33 }) r.RecruitForCoup(coup.Id, p, RecruitAnswer.Join, 5 * Day);
            var weak = new PoliticalContext { MaterialCapacity = id => .05f, DefenceCapacity = id => 1f };
            for (int d = 0; d < 30 && coup.Open; d++) r.AdvanceCoup(coup.Id, weak, 5 * Day + d * (Day + 10));
            Assert.NotEqual(CoupState.Succeeded, coup.State);
            Assert.Equal(1, r.Institution.LeaderId);
            Assert.True(coup.WillingCount >= 6);                                  // plenty of people, not enough capacity
        }

        [Fact]
        public void People_who_are_drafted_or_ordered_elsewhere_cannot_carry_a_coup()
        {
            var r = Ruled(out var coup);
            var busy = new PoliticalContext { MaterialCapacity = id => 1f, Free = id => false };
            for (int d = 0; d < 30 && coup.Open; d++) r.AdvanceCoup(coup.Id, busy, 5 * Day + d * (Day + 10));
            Assert.NotEqual(CoupState.Succeeded, coup.State);
        }

        [Fact]
        public void With_real_capacity_a_coup_can_succeed_or_be_discovered_and_never_just_inevitably()
        {
            int succeeded = 0, discovered = 0;
            for (int start = 5; start < 60; start++)
            {
                var r = Ruled(out var coup, start * Day);
                var strong = new PoliticalContext { MaterialCapacity = id => .6f, DefenceCapacity = id => 1f };
                for (int d = 0; d < 12 && coup.Open; d++) r.AdvanceCoup(coup.Id, strong, start * Day + d * (Day + 10));
                if (coup.State == CoupState.Succeeded) { succeeded++; Assert.Equal(2, r.Institution.LeaderId); }
                if (coup.State == CoupState.Failed) { discovered++; Assert.Equal(1, r.Institution.LeaderId); }
            }
            Assert.True(succeeded > 0);
            Assert.True(discovered > 0);
        }

        [Fact]
        public void A_participant_can_expose_the_plan()
        {
            var r = Colony(out _, out _, out _);
            r.SetInstitution(InstitutionKind.Authoritarian, 1, 1000);
            var coup = r.PlanCoup(2, CoupGoal.ReplaceLeader, 0, null, 5 * Day);
            Assert.True(r.RecruitForCoup(coup.Id, 3, RecruitAnswer.Expose, 5 * Day));
            Assert.Equal(CoupState.Failed, coup.State);
            Assert.Equal(3, coup.DiscoveredBy);
            Assert.True(r.CoupVisibleTo(coup, 1));                                 // the ruler now knows
            Assert.False(r.CoupVisibleTo(coup, 4));                                // an uninvolved bystander does not
        }

        [Fact]
        public void A_plan_can_end_by_reconciliation()
        {
            var r = Ruled(out var coup);
            r.AdvanceCoup(coup.Id, null, 5 * Day + 10);
            Assert.True(r.ReconcileCoup(coup.Id, 5 * Day + 20));
            Assert.Equal(CoupState.Reconciled, coup.State);
            Assert.False(r.ReconcileCoup(coup.Id, 5 * Day + 30));
            Assert.Equal(1, r.Institution.LeaderId);
        }

        [Fact]
        public void Declining_a_confrontation_never_counts_as_consent_or_a_remote_coup_result()
        {
            var r=Ruled(out var coup);coup.Stage=CoupStage.Executing;coup.Setbacks=2;
            Assert.True(r.AvoidCoupConfrontation(coup.Id,6*Day));
            Assert.Equal(CoupState.Abandoned,coup.State);Assert.Equal(1,r.Institution.LeaderId);
        }

        [Fact]
        public void A_plan_against_a_ruler_who_is_already_gone_is_dropped()
        {
            var r = Ruled(out var coup);
            r.SetInstitution(InstitutionKind.Authoritarian, 3, 5 * Day + 5);
            r.AdvanceCoup(coup.Id, null, 5 * Day + 20);
            Assert.Equal(CoupState.Abandoned, coup.State);
        }

        [Fact]
        public void Only_members_of_a_plan_can_see_it_before_it_is_found_out()
        {
            var r = Ruled(out var coup);
            Assert.True(r.CoupVisibleTo(coup, 3));
            Assert.True(r.CoupVisibleTo(coup, 0));
            Assert.False(r.CoupVisibleTo(coup, 1));
            Assert.False(r.CoupVisibleTo(coup, 22));
        }

        [Fact]
        public void Physical_voting_counts_only_the_people_who_reached_the_polling_place()
        {
            var r = Colony(out _, out _, out _);
            r.SetInstitution(InstitutionKind.Election, 1, 3000);
            var e = r.CallElection(new[] { 3, 20 }, 3000);
            int voting = 3000 + Day * 2 + 10;
            r.AdvanceElections(voting, null, true);
            Assert.Equal(ElectionState.Voting, e.State);                          // nothing is counted until people vote
            var ctx = new PoliticalContext { IdeologyFit = (v, c) => c == 20 ? .9f : -.9f };
            Assert.True(r.NeedsToVote(2));
            Assert.True(r.CastBallot(e.Id, 2, ctx, voting + 5));
            Assert.False(r.CastBallot(e.Id, 2, ctx, voting + 6));                  // once each
            Assert.False(r.NeedsToVote(2));
            r.AdvanceElections(voting + 10, null, true);
            Assert.Equal(ElectionState.Voting, e.State);
            r.AdvanceElections(voting + Day + 10, null, true);                     // the window closes; absent people simply did not vote
            Assert.Equal(ElectionState.Counted, e.State);
            Assert.Single(e.Votes);
            Assert.Equal(20, e.WinnerId);
        }

        [Fact]
        public void Physical_voting_ends_early_when_everyone_has_voted()
        {
            var r = Colony(out _, out _, out _);
            r.SetInstitution(InstitutionKind.Election, 1, 3000);
            var e = r.CallElection(new[] { 3, 20 }, 3000);
            int voting = 3000 + Day * 2 + 10;
            r.AdvanceElections(voting, null, true);
            foreach (int v in r.EligibleVoters(e)) r.CastBallot(e.Id, v, null, voting + 1);
            r.AdvanceElections(voting + 2, null, true);
            Assert.Equal(ElectionState.Counted, e.State);
        }

        [Fact]
        public void In_the_world_a_coup_waits_for_the_confrontation()
        {
            bool sawExecuting = false, resolved = false;
            for (int start = 5; start < 80 && !resolved; start++)
            {
                var r = Ruled(out var coup, start * Day);
                var strong = new PoliticalContext { MaterialCapacity = id => .6f, DefenceCapacity = id => 1f };
                for (int d = 0; d < 12 && coup.Open && coup.Stage != CoupStage.Executing; d++) r.AdvanceCoup(coup.Id, strong, start * Day + d * (Day + 10), true);
                if (coup.Stage != CoupStage.Executing || !coup.Open) continue;
                sawExecuting = true;
                int t = start * Day + 13 * (Day + 10);
                r.AdvanceCoup(coup.Id, strong, t, true);
                Assert.True(coup.Open && coup.Stage == CoupStage.Executing);                // nothing happens without the confrontation
                Assert.Equal(1, r.Institution.LeaderId);
                r.ResolveCoup(coup.Id, strong, t + Day + 10);
                Assert.True(coup.State != CoupState.Active || coup.Stage != CoupStage.Executing);
                resolved = true;
            }
            Assert.True(sawExecuting && resolved);
        }
        [Fact]
        public void A_player_controlled_pawn_answers_a_scheme_for_themselves()
        {
            var r = Colony(out _, out _, out _);
            r.SetInstitution(InstitutionKind.Authoritarian, 1, 1000);
            var coup = r.PlanCoup(2, CoupGoal.ReplaceLeader, 0, null, 5 * Day);
            Assert.True(r.AskCoupMember(coup.Id, 3));
            Assert.False(r.AskCoupMember(coup.Id, 3));
            Assert.Equal(1, coup.WillingCount);                                      // asking decides nothing
            Assert.True(r.AnswerCoup(coup.Id, 3, RecruitAnswer.Join, 5 * Day));
            Assert.Equal(2, coup.WillingCount);
            Assert.False(r.AnswerCoup(coup.Id, 3, RecruitAnswer.Refuse, 5 * Day));  // an answer is final
            Assert.True(r.AskCoupMember(coup.Id, 4));
            Assert.True(r.AnswerCoup(coup.Id, 4, RecruitAnswer.Expose, 5 * Day));
            Assert.Equal(CoupState.Failed, coup.State);
        }

        [Fact]
        public void A_council_names_the_leader_by_seat_not_headcount()
        {
            var r = Colony(out int a, out int b, out int c);
            r.GetLineage(b).Standing = 40f; r.GetLineage(c).Standing = 30f; r.GetLineage(a).Standing = 0f;
            r.SetInstitution(InstitutionKind.Council, 0, 3000);
            Assert.Equal(0, r.Institution.LeaderId);
            var seat = r.ConveneCouncil(3000, new PoliticalContext { IdeologyFit = (v, cand) => cand == 20 ? .9f : -.1f });
            Assert.NotNull(seat);
            Assert.Equal(ElectionState.Counted, seat.State);
            Assert.Equal(3, seat.Votes.Count);                                       // one vote per seated house, not per person
            Assert.Equal(20, r.Institution.LeaderId);
            Assert.Null(r.ConveneCouncil(3100));                                      // nothing to decide until the term ends or the post empties
            Assert.NotNull(r.ConveneCouncil(r.Institution.TermEndTick + 1));
        }
        private static FamilyRegistry Council(out int a, out int b, out int c)
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Elder"), T.P(2, "Two", parents: new[] { 1 }),
                T.P(20, "Solo", last: "Other"),
                T.P(22, "Third", last: "Third"), T.P(23, "T2", parents: new[] { 22 }), T.P(24, "T3", parents: new[] { 22 }), T.P(25, "T4", parents: new[] { 22 })
            }, 1000);
            a = r.FamilyOf(1); b = r.FamilyOf(20); c = r.FamilyOf(22);
            r.SetInstitution(InstitutionKind.Council, 0, 3000);
            return r;
        }

        [Fact]
        public void Under_a_council_a_union_waits_for_approval_then_goes_through_once()
        {
            var r = Council(out int a, out int b, out int c);
            Assert.False(r.UniteFamilies(b, a, 3000));                               // first attempt only raises the question
            var matter = Assert.Single(r.CouncilMatters);
            Assert.Equal(CouncilMatterKind.Union, matter.Kind);
            Assert.False(r.UniteFamilies(b, a, 3010));                               // and does not open a second one
            Assert.Single(r.CouncilMatters);
            r.AdvanceCouncil(3100);
            Assert.Equal(CouncilMatterState.Approved, matter.State);
            Assert.True(r.UniteFamilies(b, a, 3200));
            Assert.Equal(CouncilMatterState.Used, matter.State);
        }

        [Fact]
        public void A_council_refuses_a_union_that_would_hold_most_of_the_colony()
        {
            var r = Council(out int a, out int b, out int c);
            Assert.False(r.UniteFamilies(c, a, 3000));                               // four of seven people in one block
            r.AdvanceCouncil(3100);
            Assert.Equal(CouncilMatterState.Rejected, r.CouncilMatters.Single().State);
            Assert.False(r.UniteFamilies(c, a, 3200));
        }

        [Fact]
        public void Without_a_council_a_union_needs_no_one_s_leave()
        {
            var r = Council(out int a, out int b, out int c);
            r.SetInstitution(InstitutionKind.Consensus, 0, 3100);
            Assert.True(r.CouncilClears(a, b, 3200));
            Assert.Empty(r.CouncilMatters);
        }

        [Fact]
        public void A_player_controlled_head_has_a_window_to_answer_and_silence_counts_for_nothing()
        {
            var r = Council(out int a, out int b, out int c);
            r.UniteFamilies(b, a, 3000);
            var matter = r.CouncilMatters.Single();
            Func<int, bool> player = id => id == 22;
            r.AdvanceCouncil(3100, player);
            Assert.True(matter.Open);                                                   // waiting for them
            Assert.True(r.AnswerCouncil(matter.Id, 22, false, 3200));
            Assert.False(r.AnswerCouncil(matter.Id, 22, true, 3201));
            r.AdvanceCouncil(3300, player);
            Assert.Equal(CouncilMatterState.Rejected, matter.State);

            var r2 = Council(out a, out b, out c);
            r2.UniteFamilies(b, a, 3000);
            r2.AdvanceCouncil(3000 + ActivityTuning.TicksPerDay * 3, player);          // the window passed with no word
            Assert.Equal(CouncilMatterState.Rejected, r2.CouncilMatters.Single().State);   // nobody voted: nothing changes
        }

        [Fact]
        public void A_censure_needs_heads_who_actually_know_of_the_conduct()
        {
            var r = Council(out int a, out int b, out int c);
            r.GetLineage(a).Crime.Heat = 60f;
            r.RecordIncident(IncidentKind.Theft, 2, 20, 0, 3000, 40f, new[] { 22 });     // only the third house's head saw it (and the victim learned of it)
            var matter = r.RequestCensure(a, 3100);
            Assert.NotNull(matter);
            Assert.Null(r.RequestCensure(a, 3200));                                      // not again straight away
            float before = r.GetLineage(a).Standing;
            r.AdvanceCouncil(3300);
            Assert.True(matter.Yes >= 1);
            Assert.Equal(CouncilMatterState.Used, matter.State);
            Assert.Equal(before - 10f, r.GetLineage(a).Standing);
        }

        [Fact]
        public void A_censure_with_no_one_who_knows_changes_nothing()
        {
            var r = Council(out int a, out int b, out int c);
            r.GetLineage(a).Crime.Heat = 60f;
            var matter = r.RequestCensure(a, 3100);
            float before = r.GetLineage(a).Standing;
            r.AdvanceCouncil(3300);
            Assert.Equal(CouncilMatterState.Rejected, matter.State);
            Assert.Equal(before, r.GetLineage(a).Standing);
        }
        [Fact]
        public void A_rejected_union_is_not_re_asked_every_attempt()
        {
            var r = Council(out int a, out int b, out int c);
            r.UniteFamilies(c, a, 3000);
            r.AdvanceCouncil(3100);
            Assert.Equal(CouncilMatterState.Rejected, r.CouncilMatters.Single().State);
            for (int i = 0; i < 5; i++) Assert.False(r.UniteFamilies(c, a, 3200 + i));
            Assert.Single(r.CouncilMatters);
        }

        [Fact]
        public void An_election_with_every_candidate_gone_ends_instead_of_failing()
        {
            var r = Colony(out _, out _, out _);
            r.SetInstitution(InstitutionKind.Election, 1, 3000);
            var e = r.CallElection(new[] { 3, 20 }, 3000);
            e.State = ElectionState.Voting; e.Candidates.Clear();
            Assert.False(r.CastBallot(e.Id, 2, null, 3100));
            r.AdvanceElections(3200, null, false);
            Assert.Equal(ElectionState.Aborted, e.State);
        }
        [Fact]
        public void An_approved_union_is_finished_without_running_the_negotiation_again()
        {
            var r = Council(out int a, out int b, out int c);
            r.MarriageAlliances.Add(new MarriageAlliance { Id = 1, FamilyA = a, FamilyB = b, State = MarriageAllianceState.Fulfilled, UnionState = HouseUnionState.Eligible, UnionTargetFamilyId = a });
            Assert.False(r.UniteFamilies(b, a, 3000));
            r.AdvanceCouncil(3100);
            Assert.Equal(1, r.CompleteApprovedUnions(3200));
            Assert.Equal(HouseUnionState.Completed, r.MarriageAlliances.Single().UnionState);
            Assert.NotEqual(0, r.GetLineage(b).AbsorbedIntoId);
            Assert.Equal(0, r.CompleteApprovedUnions(3300));
        }

        [Fact]
        public void A_player_controlled_head_is_asked_for_their_leadership_ballot_not_voted_for()
        {
            var r = Colony(out int a, out int b, out int c);
            r.SetInstitution(InstitutionKind.Council, 0, 3000);
            var seat = r.ConveneCouncil(3000, new PoliticalContext(), id => id == 20);
            Assert.NotNull(seat);
            Assert.Equal(ElectionState.Voting, seat.State);                                  // not counted until they answer
            Assert.Contains(20, seat.Pending);
            Assert.DoesNotContain(seat.Votes, v => v.VoterId == 20);
            Assert.Null(r.ConveneCouncil(3010, new PoliticalContext(), id => id == 20));      // no second vote opened meanwhile
            Assert.False(r.CastCouncilBallot(seat.Id, 1, seat.Candidates[0], 3100));          // only the one asked may answer
            Assert.True(r.CastCouncilBallot(seat.Id, 20, seat.Candidates[0], 3100));
            Assert.Equal(ElectionState.Counted, seat.State);
            Assert.False(r.CastCouncilBallot(seat.Id, 20, seat.Candidates[0], 3101));
        }

        [Fact]
        public void An_unanswered_council_ballot_is_absent_after_the_window()
        {
            var r = Colony(out int a, out int b, out int c);
            r.SetInstitution(InstitutionKind.Council, 0, 3000);
            var seat = r.ConveneCouncil(3000, new PoliticalContext(), id => id == 20);
            r.AdvanceElections(3000 + ActivityTuning.TicksPerDay, new PoliticalContext(), true);
            Assert.Equal(ElectionState.Voting, seat.State);
            r.AdvanceElections(3000 + 2 * ActivityTuning.TicksPerDay, new PoliticalContext(), true);
            Assert.NotEqual(ElectionState.Voting, seat.State);
            Assert.DoesNotContain(seat.Votes, v => v.VoterId == 20);
        }

        [Fact]
        public void A_union_a_head_accepted_is_finished_when_the_council_approves_and_ends_if_it_refuses()
        {
            var r = Council(out int a, out int b, out int c);
            var petition = new HousePetition { Id = 1, PersonId = 20, FamilyId = a, SourceFamilyId = b, Kind = PetitionKind.Union, State = PetitionState.Accepted, AwaitingCouncil = true, CreatedTick = 3000 };
            r.HousePetitions.Add(petition);
            Assert.True(r.CouncilWouldBlock(a, b));
            r.CouncilClears(a, b, 3000);                                                      // the head's yes put the question to the council
            r.CompleteApprovedUnions(3050);
            Assert.True(petition.AwaitingCouncil);                                            // still being weighed
            r.AdvanceCouncil(3100);
            Assert.Equal(CouncilMatterState.Approved, r.CouncilMatters.Single().State);
            Assert.Equal(1, r.CompleteApprovedUnions(3200));
            Assert.False(petition.AwaitingCouncil);
            Assert.Equal(r.FamilyOf(1), r.FamilyOf(20));

            var r2 = Council(out a, out b, out c);
            var refused = new HousePetition { Id = 1, PersonId = 1, FamilyId = c, SourceFamilyId = a, Kind = PetitionKind.Union, State = PetitionState.Accepted, AwaitingCouncil = true, CreatedTick = 3000 };
            r2.HousePetitions.Add(refused);
            r2.CouncilClears(c, a, 3000);
            r2.AdvanceCouncil(3100);                                                           // four of seven in one block: refused
            r2.CompleteApprovedUnions(3200);
            Assert.False(refused.AwaitingCouncil);
            Assert.Equal(PetitionState.Denied, refused.State);
        }
    }
}
