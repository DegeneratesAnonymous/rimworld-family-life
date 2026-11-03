using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class BondTests
    {
        private const int Hour = BondTuning.TicksPerHour;

        private static FamilyRegistry Family(out int now)
        {
            var r = new FamilyRegistry();
            now = 1000;
            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }),
                T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }),
                T.P(9, "Stranger", "Wolf")
            }, now);
            return r;
        }

        private static Func<int, int, int?> Opinion(int value) => (a, b) => value;

        [Fact]
        public void Bonds_exist_between_kin_partners_and_housemates_and_not_strangers()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(0), now, null);

            Assert.NotNull(r.GetBond(1, 3));       // parent to child
            Assert.NotNull(r.GetBond(3, 1));       // child to parent
            Assert.NotNull(r.GetBond(1, 2));       // partners
            Assert.Null(r.GetBond(1, 9));          // a stranger in another household
            Assert.Null(r.GetBond(9, 1));
        }

        [Fact]
        public void Bonds_are_directed_and_start_from_the_kind_of_relationship()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(0), now, null);

            Assert.True(r.GetBond(1, 3).Attachment > r.GetBond(3, 1).Attachment);   // a parent starts more attached than a child
            Assert.True(r.GetBond(1, 2).Attachment > BondTuning.BaseAttachment(BondKind.Housemate));
        }

        [Fact]
        public void Familiarity_grows_at_home_and_fades_when_apart()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(0), now, null);
            float start = r.GetBond(1, 3).Familiarity;

            r.UpdateBonds(Opinion(0), now + 10 * Hour, null);
            float afterHome = r.GetBond(1, 3).Familiarity;
            Assert.True(afterHome > start);

            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.P(3, "Kid", "Smith", presence: Presence.Away, juvenile: true, parents: new[] { 1, 2 })
            }, now + 10 * Hour);
            r.UpdateBonds(Opinion(0), now + 20 * Hour, null);
            Assert.True(r.GetBond(1, 3).Familiarity < afterHome);
        }

        [Fact]
        public void Good_opinion_raises_attachment_and_trust_and_bad_opinion_raises_tension()
        {
            var good = Family(out int now);
            good.UpdateBonds(Opinion(0), now, null);
            good.UpdateBonds(Opinion(80), now + 40 * Hour, null);

            var bad = Family(out _);
            bad.UpdateBonds(Opinion(0), now, null);
            bad.UpdateBonds(Opinion(-80), now + 40 * Hour, null);

            Assert.True(good.GetBond(1, 2).Attachment > bad.GetBond(1, 2).Attachment);
            Assert.True(good.GetBond(1, 2).Trust > bad.GetBond(1, 2).Trust);
            Assert.True(bad.GetBond(1, 2).Tension > good.GetBond(1, 2).Tension);
            Assert.Equal(0f, good.GetBond(1, 2).Tension, 3);
        }

        [Fact]
        public void Tension_fades_when_the_cause_is_gone()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(-90), now, null);
            r.UpdateBonds(Opinion(-90), now + 30 * Hour, null);
            float high = r.GetBond(1, 2).Tension;
            Assert.True(high > 20f);

            r.UpdateBonds(Opinion(10), now + 100 * Hour, null);
            r.UpdateBonds(Opinion(10), now + 150 * Hour, null);
            Assert.True(r.GetBond(1, 2).Tension < high / 2f);
        }

        [Fact]
        public void Values_always_stay_between_0_and_100()
        {
            var r = Family(out int now);
            for (int i = 0; i < 400; i++) r.UpdateBonds(Opinion(i % 2 == 0 ? 100 : -100), now + i * 10 * Hour, null);

            Assert.All(r.Bonds, b =>
            {
                Assert.InRange(b.Attachment, 0f, 100f);
                Assert.InRange(b.Trust, 0f, 100f);
                Assert.InRange(b.Tension, 0f, 100f);
                Assert.InRange(b.Familiarity, 0f, 100f);
            });
        }

        [Fact]
        public void An_unknown_or_extreme_opinion_cannot_break_anything()
        {
            var r = Family(out int now);
            r.UpdateBonds((a, b) => null, now, null);
            r.UpdateBonds((a, b) => 99999, now + 20 * Hour, null);
            r.UpdateBonds((a, b) => int.MinValue, now + 80 * Hour, null);

            Assert.All(r.Bonds, b => Assert.InRange(b.Attachment, 0f, 100f));
            // The most hostile possible opinion must read as hostile (negating int.MinValue would overflow and read as friendly).
            Assert.True(r.GetBond(1, 2).Tension > 20f);
        }

        [Fact]
        public void No_time_passing_changes_nothing()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(30), now, null);
            var before = r.Bonds.Select(b => (b.From, b.To, b.Attachment, b.Trust, b.Tension, b.Familiarity)).ToList();

            r.UpdateBonds(Opinion(30), now, null);

            Assert.Equal(before, r.Bonds.Select(b => (b.From, b.To, b.Attachment, b.Trust, b.Tension, b.Familiarity)).ToList());
        }

        [Fact]
        public void A_long_gap_counts_as_at_most_two_days()
        {
            var a = Family(out int now);
            a.UpdateBonds(Opinion(0), now, null);
            a.UpdateBonds(Opinion(0), now + 48 * Hour, null);
            var b = Family(out _);
            b.UpdateBonds(Opinion(0), now, null);
            b.UpdateBonds(Opinion(0), now + 5000 * Hour, null);

            Assert.Equal(a.GetBond(1, 3).Familiarity, b.GetBond(1, 3).Familiarity, 3);
        }

        // ---------------------------------------------------------------- events

        [Fact]
        public void A_birth_strengthens_the_bond_between_the_child_and_both_parents()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(0), now, null);
            float before = r.GetBond(1, 3).Attachment;

            var events = r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }), T.Kid(4, new[] { 1, 2 }, age: 300)
            }, now + 1);
            r.UpdateBonds(Opinion(0), now + 1, events);

            Assert.True(r.GetBond(1, 4).Attachment > BondTuning.BaseAttachment(BondKind.Parent));
            Assert.NotNull(r.GetBond(4, 2));
            Assert.Equal(before, r.GetBond(1, 3).Attachment, 2);       // the older child is unaffected
        }

        [Fact]
        public void Becoming_partners_strengthens_the_bond_and_a_breakup_leaves_tension()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1), T.P(2) }, 1000);
            r.UpdateBonds(Opinion(0), 1000, null);

            var formed = r.Apply(new[] { T.P(1, lovers: new[] { 2 }), T.P(2, lovers: new[] { 1 }) }, 2000);
            r.UpdateBonds(Opinion(0), 2000, formed);
            float together = r.GetBond(1, 2).Attachment;
            Assert.True(together >= BondTuning.PartnerBump);

            var ended = r.Apply(new[] { T.P(1, exes: new[] { 2 }), T.P(2, exes: new[] { 1 }) }, 3000);
            r.UpdateBonds(Opinion(0), 3000, ended);

            Assert.True(r.GetBond(1, 2).Tension >= BondTuning.BreakupTension - 0.01f);
            Assert.True(r.GetBond(1, 2).Attachment < together);
        }

        [Fact]
        public void A_death_causes_grief_for_the_living_and_close_only()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(0), now, null);

            var events = r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }, presence: Presence.Dead), T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(3, new[] { 1, 2 }), T.P(9, "Stranger", "Wolf")
            }, now + Hour);
            var griefs = r.UpdateBonds(Opinion(0), now + Hour, events);

            Assert.Contains(griefs, g => g.PersonId == 3 && g.LostId == 1);
            Assert.Contains(griefs, g => g.PersonId == 2 && g.LostId == 1);
            Assert.DoesNotContain(griefs, g => g.PersonId == 9);
            Assert.DoesNotContain(griefs, g => g.PersonId == 1);                   // the dead do not grieve
            Assert.All(griefs, g => Assert.True(g.Attachment >= BondTuning.GriefMinAttachment));
        }

        [Fact]
        public void Grief_is_reported_once_and_the_bond_to_the_dead_stops_changing()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(0), now, null);
            r.GetBond(3, 1).Attachment = 90f;      // far from where it would settle by itself, so any drift would show
            var dead = new[] { T.P(1, presence: Presence.Dead, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.Kid(3, new[] { 1, 2 }) };
            var events = r.Apply(dead, now + Hour);
            Assert.NotEmpty(r.UpdateBonds(Opinion(0), now + Hour, events));
            float frozen = r.GetBond(3, 1).Attachment;
            Assert.Equal(90f, frozen, 1);

            var later = r.Apply(dead, now + 50 * Hour);
            var again = r.UpdateBonds(Opinion(100), now + 50 * Hour, later);

            Assert.Empty(again);
            Assert.Equal(frozen, r.GetBond(3, 1).Attachment, 3);
        }

        [Fact]
        public void People_who_die_together_do_not_grieve_for_each_other()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(0), now, null);

            var events = r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }, presence: Presence.Dead),
                T.P(2, "Dad", "Smith", spouses: new[] { 1 }, presence: Presence.Dead),
                T.Kid(3, new[] { 1, 2 })
            }, now + Hour);
            var griefs = r.UpdateBonds(Opinion(0), now + Hour, events);

            Assert.All(griefs, g => Assert.Equal(3, g.PersonId));      // only the surviving child mourns
            Assert.Equal(2, griefs.Count);
        }

        [Fact]
        public void A_sibling_starts_below_the_grief_threshold_so_a_distant_loss_is_quiet()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Sib", "Smith", parents: new[] { 50 }), T.P(2, "Other", "Smith", parents: new[] { 50 }),
                T.P(50, "Parent", "Smith", presence: Presence.Dead)
            }, 1000);
            r.UpdateBonds(Opinion(0), 1000, null);
            var events = r.Apply(new[]
            {
                T.P(1, "Sib", "Smith", parents: new[] { 50 }), T.P(2, "Other", "Smith", presence: Presence.Dead, parents: new[] { 50 }),
                T.P(50, "Parent", "Smith", presence: Presence.Dead)
            }, 2000);

            Assert.Empty(r.UpdateBonds(Opinion(0), 2000, events));
        }

        // ---------------------------------------------------------------- bounds and cohesion

        [Fact]
        public void Bonds_per_person_are_capped_and_dead_holders_keep_none()
        {
            var obs = new List<PersonObservation> { T.P(1000, "Parent", "Smith") };
            for (int i = 0; i < 60; i++) obs.Add(T.Kid(1 + i, new[] { 1000 }));
            var r = new FamilyRegistry();
            r.Apply(obs, 1000);
            r.UpdateBonds(Opinion(0), 1000, null);

            Assert.All(r.Persons.Where(p => p.InColony), p => Assert.True(r.Bonds.Count(b => b.From == p.Id) <= BondTuning.MaxBondsPerPerson));

            obs[0] = T.P(1000, "Parent", "Smith", presence: Presence.Dead);
            r.Apply(obs, 2000);
            r.UpdateBonds(Opinion(0), 2000, null);
            Assert.DoesNotContain(r.Bonds, b => b.From == 1000);
        }

        [Fact]
        public void Cohesion_summarises_bonds_and_is_not_defined_for_one_person()
        {
            var warmFamily = Family(out int now);
            warmFamily.UpdateBonds(Opinion(60), now, null);
            warmFamily.UpdateBonds(Opinion(60), now + 100 * Hour, null);
            float? warm = warmFamily.HouseholdCohesion(T.HouseholdOf(warmFamily, 1));
            Assert.NotNull(warm);

            var coldFamily = Family(out _);
            coldFamily.UpdateBonds(Opinion(-60), now, null);
            coldFamily.UpdateBonds(Opinion(-60), now + 100 * Hour, null);

            Assert.True(warm > coldFamily.HouseholdCohesion(T.HouseholdOf(coldFamily, 1)));
            Assert.Null(warmFamily.HouseholdCohesion(T.HouseholdOf(warmFamily, 9)));       // a household of one
            Assert.InRange(warm.Value, 0f, 100f);
        }

        [Fact]
        public void A_household_member_nobody_is_attached_to_is_reported_as_left_out()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(0), now, null);
            foreach (var b in r.Bonds.Where(b => b.To == 3)) b.Attachment = 5f;
            foreach (var b in r.Bonds.Where(b => b.To != 3)) b.Attachment = 70f;

            Assert.Equal(new[] { 3 }, r.LeftOutIn(T.HouseholdOf(r, 1)));
        }

        [Fact]
        public void Damaged_bond_data_is_repaired()
        {
            var r = Family(out int now);
            r.UpdateBonds(Opinion(0), now, null);
            r.Bonds.Add(null);
            r.Bonds.Add(new Bond { From = 1, To = 3, Attachment = 500f, Trust = -5f });     // a duplicate with impossible values
            r.Bonds.Add(new Bond { From = 77, To = 78 });                                    // people who do not exist

            r.RebuildIndexes();
            r.UpdateBonds(Opinion(0), now + Hour, null);

            Assert.DoesNotContain(r.Bonds, b => b.From == 77);
            Assert.Single(r.Bonds, b => b.From == 1 && b.To == 3);
            Assert.All(r.Bonds, b => Assert.InRange(b.Attachment, 0f, 100f));
        }

        [Fact]
        public void A_large_colony_updates_bonds_quickly()
        {
            var obs = new List<PersonObservation>();
            int id = 1;
            for (int f = 0; f < 80; f++)
            {
                int a = id++, b = id++;
                obs.Add(T.P(a, "A" + f, "Fam" + f, spouses: new[] { b }));
                obs.Add(T.P(b, "B" + f, "Fam" + f, spouses: new[] { a }));
                for (int k = 0; k < 3; k++) obs.Add(T.Kid(id++, new[] { a, b }, "Fam" + f));
            }
            var r = new FamilyRegistry();
            r.Apply(obs, 1000);
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 30; i++) r.UpdateBonds(Opinion(10), 1000 + i * Hour, null);
            clock.Stop();

            Assert.True(clock.ElapsedMilliseconds < 5000, "30 bond updates of 400 people took " + clock.ElapsedMilliseconds + " ms");
            Assert.True(r.Bonds.Count > 400);
        }
    }
}
