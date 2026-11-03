using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class IdentityTests
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private const int Hour = BondTuning.TicksPerHour;

        private static PersonObservation Skilled(int id, string skill, int level, string last = "Smith", int[] parents = null,
            Presence presence = Presence.Colony, bool juvenile = false)
        {
            var o = T.P(id, last: last, parents: parents, presence: presence, juvenile: juvenile);
            o.TopSkill = skill;
            o.TopSkillLevel = level;
            return o;
        }

        /// <summary>Two parents and two children, all in the colony, all one family.</summary>
        private static FamilyRegistry Family(out int now, int children = 2)
        {
            var r = new FamilyRegistry();
            now = 3 * Day;
            var obs = new List<PersonObservation> { T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }) };
            for (int i = 0; i < children; i++) obs.Add(T.Kid(10 + i, new[] { 1, 2 }));
            r.Apply(obs, now);
            return r;
        }

        private static float[] Profile(Dim dim, float value)
        {
            var p = new float[DimInfo.Count];
            p[(int)dim] = value;
            return p;
        }

        // ---------------------------------------------------------------- traits

        [Fact]
        public void Known_traits_map_to_dimensions_and_the_degree_specific_entry_wins()
        {
            Assert.Contains(TraitTags.For("Kind", 0), c => c.Key == Dim.Warmth && c.Value > 0);
            Assert.Contains(TraitTags.For("Abrasive", 0), c => c.Key == Dim.Warmth && c.Value < 0);
            Assert.Contains(TraitTags.For("ST_Loyal", 0), c => c.Key == Dim.Devotion);
            Assert.Contains(TraitTags.For("VTE_Academian", 0), c => c.Key == Dim.Scholarly);
            Assert.Contains(TraitTags.For("RT_TalentChef", 0), c => c.Key == Dim.Hearth);
            Assert.True(TraitTags.For("Nerves", 2).First(c => c.Key == Dim.Steadiness).Value > 0);
            Assert.True(TraitTags.For("Nerves", -2).First(c => c.Key == Dim.Steadiness).Value < 0);
        }

        [Fact]
        public void An_unknown_trait_contributes_nothing_and_never_throws()
        {
            Assert.Null(TraitTags.For("SomeFutureMod_Trait", 0));
            Assert.Null(TraitTags.For("", 0));
            Assert.Null(TraitTags.For(null, 3));
            Assert.False(TraitTags.IsKnown("Nope", 0));
            Assert.All(TraitTags.ProfileOf(new KeyValuePair<Dim, float>[0]), v => Assert.Equal(0f, v));
        }

        [Fact]
        public void A_pile_of_similar_traits_cannot_dominate()
        {
            var many = Enumerable.Range(0, 20).Select(_ => new KeyValuePair<Dim, float>(Dim.Hearth, 1.5f));
            Assert.Equal(3f, TraitTags.ProfileOf(many)[(int)Dim.Hearth]);
            var opposite = Enumerable.Range(0, 20).Select(_ => new KeyValuePair<Dim, float>(Dim.Warmth, -1.5f));
            Assert.Equal(-3f, TraitTags.ProfileOf(opposite)[(int)Dim.Warmth]);
        }

        // ---------------------------------------------------------------- culture

        [Fact]
        public void A_new_familys_culture_starts_at_what_its_members_traits_point_to()
        {
            var r = Family(out int now);
            r.UpdateCulture(id => Profile(Dim.Hearth, 1f), now);
            var lineage = r.Lineages[0];

            Assert.Equal(CultureTuning.ProfileScale, lineage.CultureOf(Dim.Hearth), 3);
            Assert.Equal(0f, lineage.CultureOf(Dim.Martial), 3);
            Assert.Contains(Dim.Hearth, r.Values(lineage.Id));
        }

        [Fact]
        public void Culture_drifts_toward_new_tendencies_gradually_not_at_once()
        {
            var r = Family(out int now);
            r.UpdateCulture(id => Profile(Dim.Hearth, 1f), now);
            var lineage = r.Lineages[0];

            r.UpdateCulture(id => Profile(Dim.Hearth, -1f), now + Day);
            float after1 = lineage.CultureOf(Dim.Hearth);
            r.UpdateCulture(id => Profile(Dim.Hearth, -1f), now + 11 * Day);
            float after10 = lineage.CultureOf(Dim.Hearth);

            Assert.InRange(after1, -CultureTuning.ProfileScale, CultureTuning.ProfileScale);
            Assert.True(after1 > 20f);                    // one day moves it only a little
            Assert.True(after10 < after1);                // and more days move it further
            Assert.True(after10 > -CultureTuning.ProfileScale);    // but never overshoots
        }

        [Fact]
        public void Children_pull_the_culture_less_than_adults()
        {
            var r = Family(out int now);
            // Adults lean Hearth, children lean Martial; Hearth must win.
            r.UpdateCulture(id => r.GetPerson(id).Juvenile ? Profile(Dim.Martial, 1f) : Profile(Dim.Hearth, 1f), now);
            var lineage = r.Lineages[0];
            Assert.True(lineage.CultureOf(Dim.Hearth) > lineage.CultureOf(Dim.Martial));
            Assert.True(lineage.CultureOf(Dim.Martial) > 0f);
        }

        [Fact]
        public void Behavior_reinforces_culture_within_bounds()
        {
            var r = Family(out int now);
            r.UpdateCulture(id => new float[DimInfo.Count], now);
            var lineage = r.Lineages[0];

            r.Reinforce(lineage.Id, Dim.Scholarly, 15f);
            Assert.Equal(15f, lineage.CultureOf(Dim.Scholarly), 3);
            for (int i = 0; i < 100; i++) r.Reinforce(lineage.Id, Dim.Scholarly, 50f);
            Assert.Equal(100f, lineage.CultureOf(Dim.Scholarly));
            for (int i = 0; i < 100; i++) r.Reinforce(lineage.Id, Dim.Scholarly, -50f);
            Assert.Equal(-100f, lineage.CultureOf(Dim.Scholarly));
            r.Reinforce(9999, Dim.Hearth, 5f);                       // an unknown family is ignored
        }

        [Fact]
        public void A_family_with_nobody_in_the_colony_keeps_its_culture()
        {
            var r = Family(out int now);
            r.UpdateCulture(id => Profile(Dim.Craft, 1f), now);
            float before = r.Lineages[0].CultureOf(Dim.Craft);

            r.Apply(new[] { T.P(1, presence: Presence.Away), T.P(2, presence: Presence.Away) }, now + Day);
            r.UpdateCulture(id => Profile(Dim.Craft, -1f), now + 5 * Day);

            Assert.Equal(before, r.Lineages[0].CultureOf(Dim.Craft), 3);
        }

        [Fact]
        public void Two_people_who_reached_a_high_level_in_a_field_make_it_the_familys_legacy_even_when_one_has_died()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                Skilled(1, "Medicine", 14, presence: Presence.Dead),
                Skilled(2, "Medicine", 12, parents: new[] { 1 }),
                Skilled(3, "Shooting", 15, parents: new[] { 1 })
            }, 3 * Day);
            var lineage = r.GetLineage(r.GetPerson(2).PrimaryLineageId);

            Assert.Equal(new[] { "medicine" }, r.Legacies(lineage.Id));
        }

        [Fact]
        public void One_gifted_person_or_modest_skill_is_not_a_legacy()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                Skilled(1, "Medicine", 18), Skilled(2, "Medicine", 6, parents: new[] { 1 }), Skilled(3, "Unknown", 20, parents: new[] { 1 })
            }, 3 * Day);
            Assert.Empty(r.Legacies(r.GetPerson(1).PrimaryLineageId));
        }

        // ---------------------------------------------------------------- traditions

        private static FamilyRegistry FamilyWithCulture(Dim dim, float value, out int now, int children = 2)
        {
            var r = Family(out now, children);
            r.UpdateCulture(id => new float[DimInfo.Count], now);
            r.Reinforce(r.Lineages[0].Id, dim, value);
            r.UpdateBonds((a, b) => 20, now, null);
            return r;
        }

        [Fact]
        public void A_family_takes_up_the_tradition_its_culture_points_to_and_not_before()
        {
            var r = FamilyWithCulture(Dim.Hearth, TraditionCatalog.AdoptAt - 1f, out int now);
            Assert.Empty(r.UpdateTraditions(now));
            Assert.Empty(r.Lineages[0].Traditions);

            r.Reinforce(r.Lineages[0].Id, Dim.Hearth, 2f);
            var events = r.UpdateTraditions(now + 1);

            Assert.Single(events, e => e.Kind == FamilyEventKind.TraditionAdopted);
            Assert.Single(r.Lineages[0].Traditions, t => t.Id == "feast");
        }

        [Fact]
        public void A_tradition_needs_enough_people_and_is_let_go_when_the_culture_moves_on()
        {
            var tiny = FamilyWithCulture(Dim.Hearth, 60f, out int now, children: 0);          // only two people
            Assert.Empty(tiny.UpdateTraditions(now));

            var r = FamilyWithCulture(Dim.Hearth, 60f, out now);
            r.UpdateTraditions(now);
            Assert.Single(r.Lineages[0].Traditions);

            r.Reinforce(r.Lineages[0].Id, Dim.Hearth, -80f);
            var events = r.UpdateTraditions(now + 1);

            Assert.Single(events, e => e.Kind == FamilyEventKind.TraditionAbandoned);
            Assert.Empty(r.Lineages[0].Traditions);
        }

        [Fact]
        public void A_tradition_comes_due_on_its_rhythm_once_and_only_when_the_family_is_not_fractured()
        {
            var r = FamilyWithCulture(Dim.Hearth, 60f, out int now);
            r.UpdateTraditions(now);
            var def = TraditionCatalog.All.First(d => d.Id == "feast");

            r.UpdateTraditions(now + (def.IntervalDays - 1) * Day);
            Assert.Empty(r.GatheringRequests);

            r.UpdateTraditions(now + def.IntervalDays * Day);
            r.UpdateTraditions(now + def.IntervalDays * Day + 10);          // asking twice does not duplicate
            Assert.Single(r.GatheringRequests);

            var fractured = FamilyWithCulture(Dim.Hearth, 60f, out now);
            fractured.UpdateTraditions(now);
            foreach (var b in fractured.Bonds) { b.Attachment = 2f; b.Trust = 2f; b.Tension = 90f; }
            fractured.UpdateTraditions(now + def.IntervalDays * Day);
            Assert.Empty(fractured.GatheringRequests);
        }

        [Fact]
        public void A_muted_tradition_is_not_held_until_it_is_unmuted()
        {
            var r = FamilyWithCulture(Dim.Hearth, 60f, out int now);
            r.UpdateTraditions(now);
            r.SetTraditionMuted(r.Lineages[0].Id, "feast", true);
            r.UpdateTraditions(now + 30 * Day);
            Assert.Empty(r.GatheringRequests);

            r.SetTraditionMuted(r.Lineages[0].Id, "feast", false);
            r.UpdateTraditions(now + 30 * Day);
            Assert.Single(r.GatheringRequests);
        }

        [Fact]
        public void Holding_a_gathering_restarts_the_clock_deepens_the_culture_and_the_first_time_is_remembered()
        {
            var r = FamilyWithCulture(Dim.Hearth, 60f, out int now);
            r.UpdateTraditions(now);
            int later = now + 15 * Day;
            r.UpdateTraditions(later);
            var request = r.GatheringRequests.Single();
            float before = r.Lineages[0].CultureOf(Dim.Hearth);

            var held = r.MarkGatheringHeld(request.Id, later + 100);

            Assert.NotNull(held);
            Assert.Empty(r.GatheringRequests);
            var record = r.Lineages[0].Traditions.Single();
            Assert.Equal(1, record.TimesHeld);
            Assert.Equal(later + 100, record.LastHeldTick);
            Assert.True(r.Lineages[0].CultureOf(Dim.Hearth) > before);
            Assert.Contains(r.MemoriesOf(r.Lineages[0].Id), m => m.Kind == MemoryKind.Tradition);
            Assert.Null(r.MarkGatheringHeld(request.Id, later + 200));         // already done
        }

        [Fact]
        public void Gatherings_can_be_postponed_expire_and_one_off_requests_do_not_duplicate()
        {
            var r = FamilyWithCulture(Dim.Hearth, 60f, out int now);
            int lineage = r.Lineages[0].Id;
            r.RequestGathering("memorial", lineage, 1, now, Day, 5 * Day);
            r.RequestGathering("memorial", lineage, 1, now, Day, 5 * Day);
            r.RequestGathering("memorial", 9999, 1, now, Day, 5 * Day);
            Assert.Single(r.GatheringRequests);
            Assert.Empty(r.DueRequests(now));
            Assert.Single(r.DueRequests(now + Day));

            r.PostponeRequest(r.GatheringRequests[0].Id, now + Day, 3000);
            Assert.Empty(r.DueRequests(now + Day + 100));
            Assert.Equal(1, r.GatheringRequests[0].Attempts);

            r.UpdateTraditions(now + 10 * Day);                                  // long past its lifetime
            Assert.DoesNotContain(r.GatheringRequests, g => g.Kind == "memorial");
        }

        [Fact]
        public void The_guest_list_is_the_family_plus_anyone_who_thinks_well_of_every_host()
        {
            var r = Family(out int now);
            r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(10, new[] { 1, 2 }), T.Kid(11, new[] { 1, 2 }),
                T.P(20, "Friend", "Wolf"), T.P(21, "Fan of one", "Wolf"), T.P(22, "Stranger", "Wolf"), T.P(23, "Away", "Wolf", presence: Presence.Away)
            }, now + 1);
            Func<int, int, int?> opinion = (from, to) => from == 20 ? 50 : (from == 21 && to == 1 ? 50 : -10);

            var guests = r.Invitees(r.Lineages[0].Id, new[] { 1, 2 }, opinion);

            Assert.Contains(1, guests); Assert.Contains(2, guests); Assert.Contains(10, guests); Assert.Contains(11, guests);
            Assert.Contains(20, guests);                    // likes both hosts
            Assert.DoesNotContain(21, guests);              // likes only one
            Assert.DoesNotContain(22, guests);
            Assert.DoesNotContain(23, guests);              // not in the colony
        }

        [Fact]
        public void A_host_who_has_died_is_not_asked_for_and_a_missing_opinion_means_no_invitation()
        {
            var r = Family(out int now);
            r.Apply(new[] { T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }, presence: Presence.Dead), T.Kid(10, new[] { 1, 2 }), T.P(20, "Friend", "Wolf") }, now + 1);

            Assert.Contains(20, r.Invitees(r.Lineages[0].Id, new[] { 1, 2 }, (a, b) => b == 1 ? 40 : (int?)null));
            Assert.DoesNotContain(20, r.Invitees(r.Lineages[0].Id, new[] { 1, 2 }, (a, b) => null));
        }

        // ---------------------------------------------------------------- memories

        [Fact]
        public void Only_weighty_moments_are_kept_and_the_family_never_holds_more_than_its_limit()
        {
            var r = Family(out int now);
            int lineage = r.Lineages[0].Id;
            Assert.False(r.Remember(lineage, MemoryKind.Birth, now, 1, 0, CultureTuning.MemoryThreshold - 0.01f));
            Assert.False(r.Remember(9999, MemoryKind.Birth, now, 1, 0, 5f));

            for (int i = 0; i < CultureTuning.MaxMemoriesPerLineage + 20; i++) r.Remember(lineage, MemoryKind.Loss, now + i, 1, 0, 1f + i * 0.01f);

            Assert.Equal(CultureTuning.MaxMemoriesPerLineage, r.MemoriesOf(lineage).Count);
            Assert.DoesNotContain(r.MemoriesOf(lineage), m => m.Weight < 1.2f);     // the lightest were the ones dropped
        }

        [Fact]
        public void Events_become_memories_in_proportion_to_how_much_they_mattered()
        {
            var r = Family(out int now);
            r.UpdateBonds((a, b) => 0, now, null);
            r.GetBond(10, 1).Attachment = 90f;
            var events = r.Apply(new[]
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }, presence: Presence.Dead), T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(10, new[] { 1, 2 }), T.Kid(11, new[] { 1, 2 }), T.Kid(12, new[] { 1, 2 }, age: 200)
            }, now + 1);
            var griefs = r.UpdateBonds((a, b) => 0, now + 1, events);

            r.RecordMemories(events, griefs, now + 1);

            var memories = r.MemoriesOf(r.Lineages[0].Id);
            Assert.Contains(memories, m => m.Kind == MemoryKind.Loss && m.A == 1);
            Assert.Contains(memories, m => m.Kind == MemoryKind.Birth && m.A == 12);
            Assert.True(memories.First(m => m.Kind == MemoryKind.Loss).Weight >= 1.5f);          // a close loss weighs more than a birth
        }

        [Fact]
        public void A_loss_nobody_was_close_to_is_not_a_defining_memory()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Loner", "Smith") }, 3 * Day);
            var events = r.Apply(new[] { T.P(1, "Loner", "Smith", presence: Presence.Dead) }, 3 * Day + 10);
            r.RecordMemories(events, new Grief[0], 3 * Day + 10);
            Assert.Empty(r.Memories);
        }

        [Fact]
        public void Memories_shape_the_culture_that_remembers_them()
        {
            var r = Family(out int now);
            int lineage = r.Lineages[0].Id;
            float faith = r.Lineages[0].CultureOf(Dim.Faith);
            r.Remember(lineage, MemoryKind.Loss, now, 1, 0, 2f);
            Assert.True(r.Lineages[0].CultureOf(Dim.Faith) > faith);

            float devotion = r.Lineages[0].CultureOf(Dim.Devotion);
            r.Remember(lineage, MemoryKind.Parting, now, 1, 2, 2f);
            Assert.True(r.Lineages[0].CultureOf(Dim.Devotion) < devotion + 1f);
        }

        // ---------------------------------------------------------------- milestones

        [Fact]
        public void A_child_who_grows_up_is_recorded_once()
        {
            var r = Family(out int now);
            Assert.Empty(r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.Kid(10, new[] { 1, 2 }), T.Kid(11, new[] { 1, 2 }) }, now + 1)
                .Where(e => e.Kind == FamilyEventKind.CameOfAge));

            var grown = new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.P(10, parents: new[] { 1, 2 }), T.Kid(11, new[] { 1, 2 }) };
            var events = r.Apply(grown, now + 2);
            Assert.Single(events, e => e.Kind == FamilyEventKind.CameOfAge && e.A == 10);
            Assert.Empty(r.Apply(grown, now + 3).Where(e => e.Kind == FamilyEventKind.CameOfAge));
            Assert.Contains(events.Select(e => e), e => e.IsChronicle);
        }

        [Fact]
        public void Coming_home_after_a_long_absence_is_a_reunion_and_a_short_trip_is_not()
        {
            var r = Family(out int now);
            PersonObservation[] Household(bool parentAway)
            {
                var mum = T.P(1, spouses: new[] { 2 });
                if (parentAway) mum.Presence = Presence.Away;
                return new[] { mum, T.P(2, spouses: new[] { 1 }), T.Kid(10, new[] { 1, 2 }), T.Kid(11, new[] { 1, 2 }) };
            }

            r.Apply(Household(true), now + 10);                                // parent 1 leaves
            Assert.DoesNotContain(r.Apply(Household(false), now + 2 * Day), e => e.Kind == FamilyEventKind.Reunion);   // back after two days: not a reunion

            r.Apply(Household(true), now + 3 * Day);
            var events = r.Apply(Household(false), now + 3 * Day + ActivityTuning.ReunionAwayTicks);
            Assert.Single(events, e => e.Kind == FamilyEventKind.Reunion && e.A == 1);
        }

        [Fact]
        public void Someone_attached_to_a_person_who_has_been_away_a_long_time_is_missing_them()
        {
            var r = Family(out int now);
            r.UpdateBonds((a, b) => 0, now, null);
            r.GetBond(10, 1).Attachment = 80f;
            var obs = new[] { T.P(1, presence: Presence.Away, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }), T.Kid(10, new[] { 1, 2 }), T.Kid(11, new[] { 1, 2 }) };
            r.Apply(obs, now + 10);

            Assert.Empty(r.MissingPairs(now + 2 * Day));
            var pairs = r.MissingPairs(now + 6 * Day);
            Assert.Contains(pairs, p => p.Key == 10 && p.Value == 1);
            Assert.DoesNotContain(pairs, p => p.Key == 11 && r.GetBond(11, 1).Attachment < 50f);
        }

        private static List<int> Since(int since) => new List<int> { since };

        [Fact]
        public void A_wedding_anniversary_is_noticed_once_when_the_year_turns()
        {
            var r = new FamilyRegistry();
            int wed = 10 * Day;
            PersonObservation[] Couple()
            {
                var a = T.P(1, "A", "Smith", spouses: new[] { 2 }); a.SpouseSince = Since(wed);
                var b = T.P(2, "B", "Smith", spouses: new[] { 1 }); b.SpouseSince = Since(wed);
                return new[] { a, b };
            }
            r.Apply(Couple(), wed + 5 * Day);
            Assert.Empty(r.UpdateMilestones(wed + 5 * Day));                                    // the first call only sets the baseline
            Assert.Empty(r.UpdateMilestones(wed + CultureTuning.YearTicks - 100));

            var events = r.UpdateMilestones(wed + CultureTuning.YearTicks + 100);
            Assert.Single(events, e => e.Kind == FamilyEventKind.Anniversary && e.A == 1 && e.B == 2);
            Assert.Empty(r.UpdateMilestones(wed + CultureTuning.YearTicks + 200));              // not twice
            Assert.Single(r.UpdateMilestones(wed + 2 * CultureTuning.YearTicks + 100));         // and again the next year
        }

        [Fact]
        public void Marriage_dates_stay_with_the_right_spouse_when_the_game_lists_them_in_another_order()
        {
            var r = new FamilyRegistry();
            var a = T.P(5, "A", "Smith", spouses: new[] { 9, 3 });
            a.SpouseSince = new List<int> { 900, 300 };            // spouse 9 married at 900, spouse 3 at 300
            r.Apply(new[] { a, T.P(9, "B", "Wolf"), T.P(3, "C", "Lake") }, 1000);

            var person = r.GetPerson(5);
            Assert.Equal(new[] { 3, 9 }, person.SpouseIds);
            Assert.Equal(new[] { 300, 900 }, person.SpouseSince);
        }

        [Fact]
        public void A_spouse_who_died_keeps_the_date_that_was_known_for_the_marriage()
        {
            var r = new FamilyRegistry();
            var living = T.P(1, "A", "Smith", spouses: new[] { 2 }); living.SpouseSince = new List<int> { 777 };
            r.Apply(new[] { living, T.P(2, "B", "Smith", spouses: new[] { 1 }) }, 1000);
            r.Apply(new[] { T.P(1, "A", "Smith"), T.P(2, "B", "Smith", presence: Presence.Dead) }, 2000);

            Assert.Equal(new[] { 2 }, r.GetPerson(1).SpouseIds);
            Assert.Equal(new[] { 777 }, r.GetPerson(1).SpouseSince);
        }

        [Fact]
        public void An_unknown_marriage_date_or_a_departed_spouse_gives_no_anniversary()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, spouses: new[] { 2 }), T.P(2, spouses: new[] { 1 }) }, 10 * Day);
            r.UpdateMilestones(10 * Day);
            Assert.Empty(r.UpdateMilestones(10 * Day + 3 * CultureTuning.YearTicks));
        }
    }
}
