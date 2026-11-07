using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class CrimeFamilyTests
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private const int Now = 20 * Day;

        private static FamilyRegistry World(out int family, out int target, bool desperate = true, bool martial = true)
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Elder", spouses: new[] { 2 }), T.P(2, "Spouse", spouses: new[] { 1 }),
                T.P(3, "Ash", parents: new[] { 1, 2 }), T.P(4, "Birch", parents: new[] { 1, 2 }), T.P(5, "Cedar", parents: new[] { 1, 2 }),
                T.P(20, "Mark", last: "Other"), T.P(21, "Bystander", last: "Other"), T.P(22, "Watcher", last: "Third")
            }, 1000);
            family = r.FamilyOf(1); target = r.FamilyOf(20);
            var house = r.GetLineage(family);
            house.Culture = Enumerable.Repeat(0f, 11).ToList();
            if (martial) house.Culture[(int)Dim.Martial] = 20f;
            house.Situation = new HouseSituation { Sustenance = desperate ? 20f : 80f };
            return r;
        }

        private static void Precedent(FamilyRegistry r)
        {
            r.RecordIncident(IncidentKind.Theft, 3, 20, 0, Now - 5 * Day, 40f, new[] { 3 });
            r.RecordIncident(IncidentKind.Intimidation, 4, 20, 0, Now - 3 * Day, 30f, new[] { 4 });
        }

        private static CrimeContext Open(int targets = 1) => new CrimeContext { Opportunities = targets, TargetValid = h => true };

        private static Heist Active(FamilyRegistry r, int fam, int target, int now = Now)
        {
            Precedent(r);
            Assert.True(r.AdoptCrimeDirection(fam, CrimeDirection.Theft, Open(), now));
            return r.PlanHeist(fam, 3, target, 0, 501, Open(), now);
        }

        [Fact]
        public void Size_culture_and_need_alone_never_make_a_house_criminal()
        {
            var r = World(out int fam, out _);
            var a = r.AssessCrimeDirection(fam, Open(), Now);
            Assert.False(a.Eligible);
            Assert.True(a.Barriers.HasFlag(CrimeBarrier.NoPrecedent));
            Assert.True(a.Barriers.HasFlag(CrimeBarrier.TooFewWilling));
            Assert.False(r.AdoptCrimeDirection(fam, CrimeDirection.Theft, Open(), Now));
            Assert.NotEmpty(a.Alternatives);                                 // a lawful way to meet the need is always shown
            Assert.Contains(LawfulAlternative.Aid, a.Alternatives);
        }

        [Fact]
        public void Each_missing_condition_blocks_it()
        {
            var r = World(out int fam, out _, desperate: false);
            Precedent(r);
            Assert.True(r.AssessCrimeDirection(fam, Open(), Now).Barriers.HasFlag(CrimeBarrier.NoNeed));
            r.GetLineage(fam).Situation.Sustenance = 10f;
            Assert.True(r.AssessCrimeDirection(fam, Open(0), Now).Barriers.HasFlag(CrimeBarrier.NoOpportunity));
            r.GetLineage(fam).Culture[(int)Dim.Martial] = 0f; r.GetLineage(fam).Culture[(int)Dim.Faith] = 20f;
            Assert.True(r.AssessCrimeDirection(fam, Open(), Now).Barriers.HasFlag(CrimeBarrier.ValuesAgainst));
        }

        [Fact]
        public void Need_opportunity_values_willing_people_and_precedent_together_allow_it()
        {
            var r = World(out int fam, out _);
            Precedent(r);
            var a = r.AssessCrimeDirection(fam, Open(), Now);
            Assert.True(a.Eligible, a.Barriers.ToString());
            Assert.True(a.WillingMembers >= 2);
            Assert.True(r.AdoptCrimeDirection(fam, CrimeDirection.Theft, Open(), Now));
            Assert.Equal(CrimeStatus.Active, r.GetLineage(fam).Crime.Status);
            Assert.NotEmpty(a.Alternatives);
        }

        [Fact]
        public void Accidents_and_breakdowns_are_not_precedent()
        {
            var r = World(out int fam, out _);
            r.RecordIncident(IncidentKind.Theft, 3, 20, 0, Now - Day, 40f, new[] { 3 }, IncidentContext.MentalBreak);
            r.RecordIncident(IncidentKind.Intimidation, 4, 20, 0, Now - Day, 40f, new[] { 4 }, IncidentContext.Accident);
            Assert.Equal(0, r.AssessCrimeDirection(fam, Open(), Now).Precedents);
        }

        [Fact]
        public void A_job_needs_a_valid_target_and_is_planned_infrequently()
        {
            var r = World(out int fam, out int target);
            Precedent(r);
            r.AdoptCrimeDirection(fam, CrimeDirection.Theft, Open(), Now);
            Assert.Null(r.PlanHeist(fam, 3, target, 0, 501, new CrimeContext { TargetValid = h => false }, Now));
            Assert.Null(r.PlanHeist(fam, 3, fam, 0, 501, Open(), Now));              // never its own house
            Assert.Null(r.PlanHeist(fam, 3, target, 0, 0, Open(), Now));             // no real target thing
            Assert.Null(r.PlanHeist(fam, 21, target, 0, 501, Open(), Now));          // organizer must belong to the house
            var heist = r.PlanHeist(fam, 3, target, 0, 501, Open(), Now);
            Assert.NotNull(heist);
            Assert.Null(r.PlanHeist(fam, 4, target, 0, 502, Open(), Now + 10));      // one at a time
            r.AbortHeist(heist.Id, Now + 20);
            Assert.Null(r.PlanHeist(fam, 4, target, 0, 502, Open(), Now + 30));      // and not again for a couple of days
            Assert.NotNull(r.PlanHeist(fam, 4, target, 0, 502, Open(), Now + 3 * Day));
        }

        [Fact]
        public void Refusing_is_a_real_option_and_exposing_is_too()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            Assert.True(r.RecruitForHeist(heist.Id, 4, RecruitAnswer.Join, Now));
            Assert.True(r.RecruitForHeist(heist.Id, 5, RecruitAnswer.Refuse, Now));
            Assert.Equal(CrimeStance.Refused, heist.Participants.Single(p => p.PersonId == 5).Stance);
            Assert.False(r.RecruitForHeist(heist.Id, 5, RecruitAnswer.Join, Now));   // an answer is final
            Assert.Equal(HeistState.Planning, heist.State);

            Assert.True(r.RecruitForHeist(heist.Id, 2, RecruitAnswer.Expose, Now));
            Assert.Equal(HeistState.Exposed, heist.State);
            Assert.Equal(CrimeKnowledgeLevel.Fact, r.CrimeKnowledgeOf(2, heist.Id));
            Assert.Equal(CrimeKnowledgeLevel.Report, r.CrimeKnowledgeOf(20, heist.Id));    // the victim house hears a report, not the facts
            Assert.True(r.GetLineage(fam).Crime.Heat > 0f);
        }

        [Fact]
        public void A_job_runs_only_with_enough_willing_free_people_and_a_still_valid_target()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            Assert.False(r.ReadyHeist(heist.Id, Open(), Now));                       // only the organizer so far
            r.RecruitForHeist(heist.Id, 4, RecruitAnswer.Join, Now);
            Assert.True(r.ReadyHeist(heist.Id, Open(), Now));
            Assert.False(r.BeginHeist(heist.Id, new CrimeContext { TargetValid = h => true, Free = id => false }, Now));   // everyone drafted or busy
            Assert.Equal(HeistState.Interrupted, heist.State);
            Assert.True(r.CleanupHeist(heist.Id, Now + 10));
            Assert.Equal(HeistState.CleanedUp, heist.State);
        }

        [Fact]
        public void A_target_that_disappears_stops_the_job()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            r.RecruitForHeist(heist.Id, 4, RecruitAnswer.Join, Now);
            Assert.False(r.ReadyHeist(heist.Id, new CrimeContext { TargetValid = h => false }, Now));
            Assert.Equal(HeistState.Aborted, heist.State);
        }

        [Fact]
        public void Goods_exist_only_with_physical_evidence_and_a_real_victim()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            r.RecruitForHeist(heist.Id, 4, RecruitAnswer.Join, Now);
            r.ReadyHeist(heist.Id, Open(), Now); r.BeginHeist(heist.Id, Open(), Now);
            Assert.False(r.ResolveHeist(heist.Id, 40, 0, false, null, Now + 100));  // goods with no token never happened
            Assert.True(r.ResolveHeist(heist.Id, 40, 9001, false, new[] { 22 }, Now + 100));
            Assert.Equal(HeistState.Completed, heist.State);
            Assert.Equal(40, heist.GoodsMoved);
            var incident = r.Incidents.Single(i => i.Id == heist.IncidentId);
            Assert.Equal(3, incident.ActorId); Assert.Equal(20, incident.VictimId);
            Assert.False(r.ResolveHeist(heist.Id, 40, 9001, false, null, Now + 200));    // not twice
        }

        [Fact]
        public void An_evidence_token_cannot_pay_out_two_jobs()
        {
            var r = World(out int fam, out int target);
            var first = Active(r, fam, target);
            r.RecruitForHeist(first.Id, 4, RecruitAnswer.Join, Now);
            r.ReadyHeist(first.Id, Open(), Now); r.BeginHeist(first.Id, Open(), Now);
            Assert.True(r.ResolveHeist(first.Id, 10, 777, false, null, Now + 10));
            r.CleanupHeist(first.Id, Now + 20);

            int later = Now + 4 * Day;
            var second = r.PlanHeist(fam, 3, target, 0, 502, Open(), later);
            r.RecruitForHeist(second.Id, 4, RecruitAnswer.Join, later);
            r.ReadyHeist(second.Id, Open(), later); r.BeginHeist(second.Id, Open(), later);
            Assert.False(r.ResolveHeist(second.Id, 10, 777, false, null, later + 10));
            Assert.True(r.ResolveHeist(second.Id, 10, 778, false, null, later + 10));
        }

        [Fact]
        public void An_unwitnessed_job_leaves_the_perpetrator_unknown()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            r.RecruitForHeist(heist.Id, 4, RecruitAnswer.Join, Now);
            r.ReadyHeist(heist.Id, Open(), Now); r.BeginHeist(heist.Id, Open(), Now);
            r.ResolveHeist(heist.Id, 0, 0, false, null, Now + 5);

            Assert.False(r.CanAccuse(20, heist.Id));                                // the victim saw nothing
            Assert.False(r.PerpetratorKnownTo(21, heist.Id));
            Assert.Equal(CrimeKnowledgeLevel.None, r.CrimeKnowledgeOf(22, heist.Id));
            Assert.True(r.CanAccuse(3, heist.Id));                                  // only those who were there know
        }

        [Fact]
        public void Reports_and_suspicion_are_not_proof()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            r.RecruitForHeist(heist.Id, 4, RecruitAnswer.Join, Now);
            r.ReadyHeist(heist.Id, Open(), Now); r.BeginHeist(heist.Id, Open(), Now);
            r.ResolveHeist(heist.Id, 0, 0, false, new[] { 22 }, Now + 5);
            Assert.True(r.CanAccuse(22, heist.Id));                                 // a witness
            Assert.True(r.ReportCrime(heist.Id, 22, 21));
            Assert.Equal(CrimeKnowledgeLevel.Report, r.CrimeKnowledgeOf(21, heist.Id));
            Assert.False(r.CanAccuse(21, heist.Id));
            Assert.False(r.ReportCrime(heist.Id, 21, 20));                          // hearsay cannot be passed on as fact
            r.SuspectCrime(heist.Id, 20);
            Assert.False(r.CanAccuse(20, heist.Id));
            Assert.Equal(CrimeKnowledgeLevel.Suspicion, r.CrimeKnowledgeOf(20, heist.Id));
        }

        [Fact]
        public void Being_caught_tells_the_victim_and_raises_outside_pressure()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            r.RecruitForHeist(heist.Id, 4, RecruitAnswer.Join, Now);
            r.ReadyHeist(heist.Id, Open(), Now); r.BeginHeist(heist.Id, Open(), Now);
            float before = r.GetBond(3, 4)?.Attachment ?? 0f;
            r.ResolveHeist(heist.Id, 5, 3003, true, null, Now + 5);
            Assert.Equal(HeistState.Exposed, heist.State);
            Assert.True(r.CanAccuse(20, heist.Id));
            Assert.True(r.GetLineage(fam).Crime.Heat >= 25f);
            Assert.True(r.RivalResponseLikely(fam) || r.GetLineage(fam).Crime.Heat > 0f);
            Assert.True((r.GetBond(3, 4)?.Attachment ?? 0f) > before);              // working together builds loyalty inside the group
        }

        [Fact]
        public void A_protection_deal_collects_nothing_until_the_protected_head_agrees()
        {
            var r = World(out int fam, out int target);
            var deal = r.ProposeProtection(fam, target, 30, Now);
            Assert.NotNull(deal);
            Assert.Null(r.ProposeProtection(fam, target, 30, Now));
            Assert.False(r.RecordTribute(deal.Id, 30, 1, Now));                     // no agreement, no tribute
            Assert.False(r.AgreeProtection(deal.Id, 1, Now));                       // wrong house's head
            Assert.True(r.AgreeProtection(deal.Id, 20, Now));
            Assert.False(r.RecordTribute(deal.Id, 30, 0, Now));
            Assert.True(r.RecordTribute(deal.Id, 30, 1, Now));
            Assert.False(r.RecordTribute(deal.Id, 30, 1, Now));                     // the same payment is not counted again
            Assert.Equal(30, deal.PaymentsRecorded);
            Assert.True(r.EndProtection(deal.Id, Now + 10));
            Assert.False(r.RecordTribute(deal.Id, 30, 2, Now + 20));
        }

        [Fact]
        public void Reform_is_mechanically_possible_through_real_amends()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            Assert.True(r.BeginReform(fam, Now + 10));
            Assert.Equal(HeistState.Aborted, heist.State);                          // the current job stops
            Assert.False(r.RecordAmends(fam, 21, 20, 50, Now + 20));                // only the house's own people can make amends
            for (int i = 0; i < 6; i++) r.RecordAmends(fam, 3, 20, 100, Now + 30 + i);
            Assert.Equal(CrimeStatus.Reformed, r.GetLineage(fam).Crime.Status);
            Assert.Equal(CrimeDirection.None, r.GetLineage(fam).Crime.Direction);
            Assert.Contains(r.Incidents, i => i.Kind == IncidentKind.Restitution && i.ActorId == 3);
        }

        [Fact]
        public void A_house_can_simply_walk_away()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            Assert.True(r.AbandonCrimeDirection(fam, Now + 10));
            Assert.Equal(CrimeStatus.None, r.GetLineage(fam).Crime.Status);
            Assert.False(heist.Active);
            Assert.False(r.AbandonCrimeDirection(fam, Now + 20));
        }

        [Fact]
        public void Pressure_fades_and_stalled_plans_end_on_their_own()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            r.GetLineage(fam).Crime.Heat = 50f;
            for (int i = 0; i < 100; i++) r.MaintainCrime(Now + i);
            Assert.True(r.GetLineage(fam).Crime.Heat < 50f);
            r.MaintainCrime(Now + 6 * Day);
            Assert.Equal(HeistState.Aborted, heist.State);
        }

        [Fact]
        public void A_theft_the_supply_system_already_recorded_is_not_recorded_twice()
        {
            var r = World(out int fam, out int target);
            var heist = Active(r, fam, target);
            r.RecruitForHeist(heist.Id, 4, RecruitAnswer.Join, Now);
            r.ReadyHeist(heist.Id, Open(), Now); r.BeginHeist(heist.Id, Open(), Now);
            var real = r.RecordIncident(IncidentKind.Theft, 3, 20, 0, Now + 5, 25f, new[] { 3 }, amount: 12);
            int before = r.Incidents.Count;
            Assert.True(r.ResolveHeist(heist.Id, 12, 4004, false, null, Now + 10, real.Id));
            Assert.Equal(before, r.Incidents.Count);
            Assert.Equal(real.Id, heist.IncidentId);
        }

        [Fact]
        public void Restitution_already_recorded_counts_toward_reform_but_only_after_reform_began()
        {
            var r = World(out int fam, out int target);
            Active(r, fam, target);
            var early = r.RecordIncident(IncidentKind.Restitution, 3, 20, 0, Now + 1, 20f, new[] { 3, 20 }, amount: 40);
            Assert.True(r.BeginReform(fam, Now + 10));
            Assert.False(r.NoteAmends(fam, early.Id, Now + 11));                       // before reform began
            var later = r.RecordIncident(IncidentKind.Restitution, 3, 20, 0, Now + 20, 20f, new[] { 3, 20 }, amount: 40);
            float before = r.GetLineage(fam).Crime.ReformProgress;
            Assert.True(r.NoteAmends(fam, later.Id, Now + 21));
            Assert.True(r.GetLineage(fam).Crime.ReformProgress > before);
            Assert.False(r.NoteAmends(fam + 99, later.Id, Now + 22));
        }    }
}
