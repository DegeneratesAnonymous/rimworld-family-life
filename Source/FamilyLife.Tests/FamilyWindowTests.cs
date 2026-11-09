using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// The Families window's logic: where each section lives, what needs attention, how relationships and events are filtered, what an observer may see.
    /// The drawing itself needs the game; these tests cover everything the drawing reads.
    /// </summary>
    public class FamilyWindowTests
    {
        // Elder (1) heads a house with Wife (2), Kid (3) and Kid2 (4); Friend (9) is in the colony but in no house of it.
        private static FamilyRegistry Home()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Elder", spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }),
                T.P(3, "Kid", parents: new[] { 1, 2 }), T.P(4, "Kid2", parents: new[] { 1, 2 }),
                T.P(9, "Friend")
            }, 1000);
            r.UpdateBonds((a, b) => (int?)0, 1000, null);
            return r;
        }

        private static Lineage House(FamilyRegistry r) => r.Lineages.First(l => r.MembersOf(l.Id).Any(p => p.Id == 1));

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Languages"))) dir = dir.Parent;
            return dir?.FullName;
        }

        // ---- the six tabs and where the old content went

        [Fact]
        public void There_are_six_tabs_in_the_requested_order()
        {
            Assert.Equal(new[] { "Summary", "People", "Relationships", "Story", "Home", "HouseAffairs" }, Enum.GetNames(typeof(FamilyTab)));
        }

        [Fact]
        public void Every_section_of_the_old_window_has_a_place_and_nothing_is_lost()
        {
            foreach (var section in FamilyWindowLayout.LegacySections)
            {
                Assert.True(FamilyWindowLayout.TryPlace(section, out var place), section);
                Assert.True(Enum.IsDefined(typeof(FamilyTab), place.Tab), section);
                Assert.InRange(place.Sub, 0, Math.Max(0, FamilyWindowLayout.SubCount(place.Tab) - 1));
            }
            // The old eight tabs each still reach their content.
            foreach (var old in new[] { "Overview", "Relationships", "Moments", "Households", "Manage", "Zone", "Traditions", "History" })
                Assert.True(FamilyWindowLayout.LegacySections.Any(s => s.StartsWith(old.ToLowerInvariant()) || (old == "Manage" && s.StartsWith("manage"))), old);
        }

        [Fact]
        public void Old_tabs_map_to_their_new_homes()
        {
            Assert.Equal(FamilyTab.Summary, FamilyWindowLayout.FromLegacyTab("Overview").Tab);
            Assert.Equal(FamilyTab.Relationships, FamilyWindowLayout.FromLegacyTab("Relationships").Tab);
            var moments = FamilyWindowLayout.FromLegacyTab("Moments");
            Assert.Equal(FamilyTab.Story, moments.Tab); Assert.Equal((int)StorySub.Timeline, moments.Sub);
            var trad = FamilyWindowLayout.FromLegacyTab("Traditions");
            Assert.Equal(FamilyTab.Story, trad.Tab); Assert.Equal((int)StorySub.Current, trad.Sub);
            var hist = FamilyWindowLayout.FromLegacyTab("History");
            Assert.Equal(FamilyTab.Story, hist.Tab); Assert.Equal((int)StorySub.Chronicle, hist.Sub);
            Assert.Equal(FamilyTab.Home, FamilyWindowLayout.FromLegacyTab("Households").Tab);
            var zone = FamilyWindowLayout.FromLegacyTab("Zone");
            Assert.Equal(FamilyTab.Home, zone.Tab); Assert.Equal((int)HomeSub.Area, zone.Sub);
            Assert.Equal(FamilyTab.HouseAffairs, FamilyWindowLayout.FromLegacyTab("Manage").Tab);
            Assert.Equal(FamilyTab.Summary, FamilyWindowLayout.FromLegacyTab("nothing").Tab);
        }

        // ---- remembered state, navigation

        [Fact]
        public void The_window_remembers_family_tab_and_subtab()
        {
            var state = new FamilyWindowState();
            state.Select(7);
            state.Navigate(FamilyTab.Home, (int)HomeSub.Supplies);
            // A new window reads the same state.
            Assert.Equal(7, state.LineageId);
            Assert.Equal(FamilyTab.Home, state.Tab);
            Assert.Equal((int)HomeSub.Supplies, state.Sub(FamilyTab.Home));
            state.Navigate(FamilyTab.Story, 99);        // out of range clamps to the last sub-tab
            Assert.Equal((int)StorySub.Chronicle, state.EventsSub);
            state.Navigate(FamilyTab.Story, -4);
            Assert.Equal((int)StorySub.Current, state.EventsSub);
        }

        [Fact]
        public void Choosing_another_family_forgets_the_open_person_but_choosing_the_same_one_keeps_it()
        {
            var state = new FamilyWindowState();
            state.Select(1); state.SelectedPerson = 5; state.SelectedRelation = 6;
            state.Select(1);
            Assert.Equal(5, state.SelectedPerson);
            state.Select(2);
            Assert.Equal(0, state.SelectedPerson); Assert.Equal(0, state.SelectedRelation);
        }

        [Fact]
        public void Following_an_alert_opens_the_content_it_is_about()
        {
            var state = new FamilyWindowState();
            state.Go(new FamilyAlert { Kind = AlertKind.FoodLow, Tab = FamilyTab.Home, Sub = (int)HomeSub.Supplies });
            Assert.Equal(FamilyTab.Home, state.Tab); Assert.Equal((int)HomeSub.Supplies, state.HomeSub);
            state.Go(new FamilyAlert { Kind = AlertKind.OpenRift, Tab = FamilyTab.Relationships, PersonId = 3 });
            Assert.Equal(FamilyTab.Relationships, state.Tab); Assert.Equal(3, state.SelectedPerson);
            state.Go(null);                                 // nothing to follow: nothing changes
            Assert.Equal(FamilyTab.Relationships, state.Tab);
        }

        // ---- what needs attention

        [Fact]
        public void A_healthy_family_with_no_records_to_worry_about_has_no_alerts()
        {
            var r = Home(); var h = House(r);
            Assert.DoesNotContain(FamilyAlerts.Build(r, h, 0, 5000), a => a.Kind == AlertKind.FoodLow || a.Kind == AlertKind.HousingProblem);
        }

        [Fact]
        public void Low_food_and_housing_are_alerts_that_lead_to_the_right_place()
        {
            var r = Home(); var h = House(r);
            h.Situation = new HouseSituation { EvaluatedTick = 100, Sustenance = 10f, LivingSpace = 20f, Safety = 80f, Capability = 80f, Cohesion = 70f };
            var alerts = FamilyAlerts.Build(r, h, 0, 5000);
            var food = alerts.Single(a => a.Kind == AlertKind.FoodLow);
            Assert.True(food.Urgent);
            Assert.Equal(FamilyTab.Home, food.Tab); Assert.Equal((int)HomeSub.Supplies, food.Sub);
            var space = alerts.Single(a => a.Kind == AlertKind.HousingProblem);
            Assert.Equal(FamilyTab.Home, space.Tab); Assert.Equal((int)HomeSub.Households, space.Sub);
            Assert.Equal(AlertKind.FoodLow, alerts[0].Kind);               // most urgent first
        }

        [Fact]
        public void A_house_that_was_never_assessed_makes_up_no_concerns()
        {
            var r = Home(); var h = House(r);
            h.Situation = new HouseSituation();                              // EvaluatedTick 0: nothing was measured
            Assert.DoesNotContain(FamilyAlerts.Build(r, h, 0, 5000), a => a.Kind == AlertKind.FoodLow);
        }

        [Fact]
        public void An_old_save_with_missing_house_records_opens_without_error()
        {
            var r = Home(); var h = House(r);
            h.Situation = null; h.Ambition = null; h.Stewards = new System.Collections.Generic.List<int>();
            var alerts = FamilyAlerts.Build(r, h, 0, 5000);
            Assert.NotNull(alerts);
            var summary = FamilySummaries.Of(r, h, 0, 5000);
            Assert.Null(summary.Tier);
            Assert.Equal(-1f, summary.Cohesion);
            Assert.NotEmpty(RoleCards.For(r, h, 0));
        }

        [Fact]
        public void An_outsider_is_shown_nothing_about_how_the_house_is_doing()
        {
            var r = Home(); var h = House(r);
            h.Situation = new HouseSituation { EvaluatedTick = 100, Sustenance = 5f, Cohesion = 90f };
            Assert.NotEmpty(FamilyAlerts.Build(r, h, 0, 5000));
            Assert.Empty(FamilyAlerts.Build(r, h, 9, 5000));                // Friend belongs to no house here
            var s = FamilySummaries.Of(r, h, 9, 5000);
            Assert.Null(s.Tier); Assert.Equal(0, s.Alerts);
            Assert.False(FamilyWindowFacts.IsInsider(r, h.Id, 9));
            Assert.True(FamilyWindowFacts.IsInsider(r, h.Id, 2));
        }

        [Fact]
        public void An_open_rift_is_a_private_matter_between_its_two_people()
        {
            var r = Home(); var h = House(r);
            r.Rifts.Add(new Rift { Id = 1, A = 3, B = 4, Cause = RiftCause.Rivalry, StartTick = 1200, Severity = 40f });
            Assert.Contains(FamilyAlerts.Build(r, h, 0, 5000), a => a.Kind == AlertKind.OpenRift);
            Assert.Contains(FamilyAlerts.Build(r, h, 3, 5000), a => a.Kind == AlertKind.OpenRift);
            Assert.DoesNotContain(FamilyAlerts.Build(r, h, 1, 5000), a => a.Kind == AlertKind.OpenRift);
        }

        [Fact]
        public void A_promise_due_shows_to_the_two_it_concerns_only()
        {
            var r = Home(); var h = House(r);
            r.Commitments.Add(new Commitment { Id = 1, FromId = 1, ToId = 3, CreatedTick = 0, DueTick = 6000, State = 0 });
            Assert.Contains(FamilyAlerts.Build(r, h, 0, 5000), a => a.Kind == AlertKind.PromiseDue && a.Count == 1);
            Assert.Contains(FamilyAlerts.Build(r, h, 1, 5000), a => a.Kind == AlertKind.PromiseDue);
            Assert.DoesNotContain(FamilyAlerts.Build(r, h, 2, 5000), a => a.Kind == AlertKind.PromiseDue);
        }

        [Fact]
        public void A_missing_head_is_urgent_and_leads_to_leadership()
        {
            var r = Home(); var h = House(r);
            h.VacantHeadId = 1;
            var alert = FamilyAlerts.Build(r, h, 0, 5000).First(a => a.Kind == AlertKind.NoHead);
            Assert.True(alert.Urgent); Assert.Equal(FamilyTab.HouseAffairs, alert.Tab);
        }

        // ---- the sidebar

        [Fact]
        public void The_family_list_sorts_by_name_size_standing_and_attention_and_searches()
        {
            var rows = new[]
            {
                new FamilySummary { LineageId = 1, Name = "Northcott", Members = 4, Cohesion = 70, Alerts = 0 },
                new FamilySummary { LineageId = 2, Name = "Garrick", Members = 4, Cohesion = 30, Alerts = 2, Urgent = true },
                new FamilySummary { LineageId = 3, Name = "Jessop", Members = 6, Cohesion = 55, Alerts = 1 }
            };
            Assert.Equal(new[] { "Garrick", "Jessop", "Northcott" }, FamilySummaries.Sort(rows, FamilySort.Name, "").Select(x => x.Name));
            Assert.Equal("Jessop", FamilySummaries.Sort(rows, FamilySort.Size, null).First().Name);
            Assert.Equal("Northcott", FamilySummaries.Sort(rows, FamilySort.Standing, "").First().Name);
            Assert.Equal(new[] { "Garrick", "Jessop", "Northcott" }, FamilySummaries.Sort(rows, FamilySort.Attention, "").Select(x => x.Name));
            Assert.Single(FamilySummaries.Sort(rows, FamilySort.Name, "ess"));
            Assert.Empty(FamilySummaries.Sort(rows, FamilySort.Name, "zzz"));
        }

        [Fact]
        public void A_family_summary_reports_its_members_households_and_cohesion_tier()
        {
            var r = Home(); var h = House(r);
            h.Situation = new HouseSituation { EvaluatedTick = 50, Cohesion = 70f };
            var s = FamilySummaries.Of(r, h, 0, 100);
            Assert.Equal(4, s.Members);
            Assert.True(s.Households >= 1);
            Assert.Equal("Close", s.Tier);
        }

        // ---- relationships

        private static void Bond(FamilyRegistry r, int from, int to, float attach, float trust, float tension, int tick)
        {
            var b = r.GetBond(from, to);
            Assert.NotNull(b);
            b.Attachment = attach; b.Trust = trust; b.Tension = tension; b.LastTick = tick;
        }

        [Fact]
        public void Relationships_filter_to_family_associates_or_everyone()
        {
            var r = Home(); var h = House(r);
            Bond(r, 1, 2, 80, 70, 5, 100); Bond(r, 1, 3, 60, 60, 20, 300);
            var family = RelationshipView.Build(r, 1, h.Id, RelFilter.Family, RelSort.Closest, null);
            Assert.All(family, row => Assert.True(row.Family));
            var everyone = RelationshipView.Build(r, 1, h.Id, RelFilter.Everyone, RelSort.Closest, null);
            Assert.True(everyone.Count >= family.Count);
            Assert.All(RelationshipView.Build(r, 1, h.Id, RelFilter.Associates, RelSort.Closest, null), row => Assert.False(row.Family));
        }

        [Fact]
        public void Relationships_sort_by_closest_least_trusted_most_tense_and_recent()
        {
            var r = Home(); var h = House(r);
            Bond(r, 1, 2, 90, 80, 5, 100); Bond(r, 1, 3, 50, 20, 60, 500); Bond(r, 1, 4, 70, 40, 30, 300);
            var closest = RelationshipView.Build(r, 1, h.Id, RelFilter.Family, RelSort.Closest, null);
            Assert.Equal(new[] { 2, 4, 3 }, closest.Where(x => x.OtherId != 0 && new[] { 2, 3, 4 }.Contains(x.OtherId)).Select(x => x.OtherId));
            Assert.Equal(3, RelationshipView.Build(r, 1, h.Id, RelFilter.Family, RelSort.LeastTrusted, null).First().OtherId);
            Assert.Equal(3, RelationshipView.Build(r, 1, h.Id, RelFilter.Family, RelSort.MostTense, null).First().OtherId);
            Assert.Equal(3, RelationshipView.Build(r, 1, h.Id, RelFilter.Family, RelSort.Recent, null).First().OtherId);
        }

        [Fact]
        public void Relationship_search_matches_names_without_regard_to_case()
        {
            var r = Home(); var h = House(r);
            var found = RelationshipView.Build(r, 1, h.Id, RelFilter.Everyone, RelSort.Closest, "kid2");
            Assert.Single(found); Assert.Equal(4, found[0].OtherId);
            Assert.Empty(RelationshipView.Build(r, 1, h.Id, RelFilter.Everyone, RelSort.Closest, "nobody"));
        }

        [Fact]
        public void Relationship_trend_comes_from_recent_causes_and_the_dead_are_marked()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "A", spouses: new[] { 2 }), T.P(2, "B", spouses: new[] { 1 }) }, 100);
            r.UpdateBonds((x, y) => (int?)0, 100, null);
            var b = r.GetBond(1, 2);
            b.Reasons = new System.Collections.Generic.List<BondReason>();
            Assert.Equal(0, RelationshipView.TrendOf(b));
            b.Reasons.Add(new BondReason { Cause = BondCause.Together, Delta = 8f });
            Assert.Equal(1, RelationshipView.TrendOf(b));
            b.Reasons.Clear(); b.Reasons.Add(new BondReason { Cause = BondCause.Together, Delta = -8f });
            Assert.Equal(-1, RelationshipView.TrendOf(b));
            r.Apply(new[] { T.P(1, "A", spouses: new[] { 2 }), T.P(2, "B", presence: Presence.Dead, spouses: new[] { 1 }) }, 200);
            r.UpdateBonds((x, y) => (int?)0, 200, null);
            Assert.True(RelationshipView.Build(r, 1, r.FamilyOf(1), RelFilter.Everyone, RelSort.Closest, null).Single(x => x.OtherId == 2).Dead);
        }

        [Fact]
        public void Relationships_of_a_person_who_is_missing_are_empty_not_an_error()
        {
            var r = Home();
            Assert.Empty(RelationshipView.Build(r, 999, 1, RelFilter.Everyone, RelSort.Closest, null));
            Assert.Empty(RelationshipView.Build(null, 1, 1, RelFilter.Everyone, RelSort.Closest, null));
        }

        // ---- events

        [Fact]
        public void Event_filters_classify_and_combine()
        {
            Assert.Equal(EventCategory.BirthsDeaths, EventFilters.Of(FamilyEventKind.Birth));
            Assert.Equal(EventCategory.Conflict, EventFilters.Of(FamilyEventKind.RiftOpened));
            Assert.Equal(EventCategory.Traditions, EventFilters.Of(FamilyEventKind.TraditionHeld));
            Assert.Equal(EventCategory.Household, EventFilters.Of(FamilyEventKind.HouseholdFormed));
            Assert.True(EventFilters.Matches(0, EventCategory.Conflict));                        // no filter shows everything
            int mask = EventFilters.Toggle(0, EventCategory.Conflict);
            Assert.True(EventFilters.Matches(mask, EventCategory.Conflict));
            Assert.False(EventFilters.Matches(mask, EventCategory.Traditions));
            mask = EventFilters.Toggle(mask, EventCategory.Traditions);
            Assert.True(EventFilters.Matches(mask, EventCategory.Traditions));
            Assert.Equal((int)EventCategory.Conflict, EventFilters.Toggle(mask, EventCategory.Traditions));
            Assert.True(EventFilters.OfMoment(false, false, EventTier.Poor).HasFlag(EventCategory.Conflict));
            Assert.False(EventFilters.OfMoment(false, false, EventTier.Good).HasFlag(EventCategory.Conflict));
        }

        [Fact]
        public void The_chronicle_holds_only_permanent_history_and_follows_the_filter()
        {
            var r = Home(); var h = House(r);
            var all = FamilyTimeline.Chronicle(r, h, 0, 0);
            Assert.All(all, e => Assert.NotEqual(TimelineKind.Incident, e.Kind));
            var births = FamilyTimeline.Chronicle(r, h, 0, (int)EventCategory.BirthsDeaths);
            Assert.All(births, e => Assert.True((e.Category & EventCategory.BirthsDeaths) != 0));
            Assert.True(births.Count <= all.Count);
        }

        [Fact]
        public void Recent_events_leave_out_old_history()
        {
            var r = Home(); var h = House(r);
            r.Chronicle.Add(new FamilyEvent(FamilyEventKind.Birth, 100, 3, 1));
            r.Chronicle.Add(new FamilyEvent(FamilyEventKind.Died, 5_000_000, 3, 0));
            var recent = FamilyTimeline.Recent(r, h, 0, 5_000_100, 0);
            Assert.Contains(recent, e => e.Chronicle != null && e.Chronicle.Kind == FamilyEventKind.Died);
            Assert.DoesNotContain(recent, e => e.Chronicle != null && e.Chronicle.Tick == 100);
        }

        [Fact]
        public void Upcoming_lists_promises_and_orders_the_overdue_first()
        {
            var r = Home(); var h = House(r);
            r.Commitments.Add(new Commitment { Id = 1, FromId = 1, ToId = 3, DueTick = 9000, State = 0 });
            r.Commitments.Add(new Commitment { Id = 2, FromId = 2, ToId = 4, DueTick = 1000, State = 0 });
            var list = FamilyTimeline.Upcoming(r, h, 0, 5000);
            Assert.Equal(2, list.Count(u => u.Kind == UpcomingKind.Promise));
            Assert.True(list[0].Overdue); Assert.Equal(2, list[0].RefId);
            Assert.Empty(FamilyTimeline.Upcoming(r, h, 9, 5000));                               // not this person's family
            Assert.Single(FamilyTimeline.Upcoming(r, h, 3, 5000));                              // only the promise that is theirs
        }

        [Fact]
        public void An_empty_family_has_empty_timelines_and_does_not_fail()
        {
            var r = new FamilyRegistry();
            var h = new Lineage { Id = 77, Name = "Ghost" };
            Assert.Empty(FamilyTimeline.Upcoming(r, h, 0, 1));
            Assert.Empty(FamilyTimeline.Recent(r, h, 0, 1, 0));
            Assert.Empty(FamilyTimeline.Chronicle(r, h, 0, 0));
            Assert.Empty(FamilyAlerts.Build(r, h, 0, 1));
        }

        // ---- supplies

        [Fact]
        public void A_supply_reading_compares_current_with_what_the_family_wants()
        {
            Assert.Equal(SupplyLevel.Unknown, SupplyReading.Level(false, 0f, 1f));
            Assert.Equal(SupplyLevel.Short, SupplyReading.Level(true, 0.2f, 1f));
            Assert.Equal(SupplyLevel.Low, SupplyReading.Level(true, 0.7f, 1f));
            Assert.Equal(SupplyLevel.Enough, SupplyReading.Level(true, 2.4f, 2.0f));
            Assert.Equal(SupplyLevel.Enough, SupplyReading.Level(true, 0f, 0f));
        }

        // ---- offices and perspective

        [Fact]
        public void Offices_name_the_head_and_leave_the_unfilled_vacant()
        {
            var r = Home(); var h = House(r);
            var cards = RoleCards.For(r, h, 0);
            var head = cards.Single(c => c.Role == FamilyRole.HouseHead);
            Assert.Equal(r.HeadOf(h.Id), head.HolderId); Assert.False(head.Vacant);
            Assert.True(cards.Single(c => c.Role == FamilyRole.Steward).Vacant);
            Assert.True(cards.Single(c => c.Role == FamilyRole.PublicHeir).Vacant);
        }

        [Fact]
        public void Only_offices_the_game_can_assign_are_listed()
        {
            var r = Home(); var h = House(r);
            foreach (var card in RoleCards.For(r, h, 0)) Assert.True(RoleCatalog.Get(card.Role).Implemented, card.Role.ToString());
            Assert.DoesNotContain(RoleCards.For(r, h, 0), c => c.Role == FamilyRole.HouseProtector);
        }

        [Fact]
        public void A_secret_heir_stays_secret_to_anyone_who_was_not_told()
        {
            var r = Home(); var h = House(r);
            int head = r.HeadOf(h.Id);
            Assert.True(r.DesignateHeir(h.Id, head, 2, null, 2000));                  // named in private: only the head and the heir know
            var asKid = RoleCards.For(r, h, 3).Single(c => c.Role == FamilyRole.PublicHeir);
            Assert.True(asKid.Hidden); Assert.Equal(0, asKid.HolderId);
            var asHeir = RoleCards.For(r, h, 2).Single(c => c.Role == FamilyRole.PublicHeir);
            Assert.False(asHeir.Hidden); Assert.Equal(2, asHeir.HolderId);
            var asHead = RoleCards.For(r, h, head).Single(c => c.Role == FamilyRole.PublicHeir);
            Assert.Equal(2, asHead.HolderId);
            var overview = RoleCards.For(r, h, 0).Single(c => c.Role == FamilyRole.PublicHeir);
            Assert.Equal(2, overview.HolderId);                                       // the overview is the administrative view
        }

        [Fact]
        public void A_missing_colony_leader_is_none_not_someone()
        {
            var r = Home();
            r.Institution.LeaderId = 0;
            Assert.Equal(0, FamilyWindowFacts.RecognizedLeader(r));
            r.Institution.LeaderId = 424242;                                          // someone the records no longer hold
            Assert.Equal(0, FamilyWindowFacts.RecognizedLeader(r));
            r.Institution.LeaderId = 1;
            r.Institution.Kind = InstitutionKind.Consensus;
            Assert.Equal(0, FamilyWindowFacts.RecognizedLeader(r));
            r.Institution.Kind = InstitutionKind.Council;
            Assert.Equal(1, FamilyWindowFacts.RecognizedLeader(r));
            r.Apply(new[] { T.P(1, "Elder", presence: Presence.Dead, spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }) }, 3000);
            Assert.Equal(0, FamilyWindowFacts.RecognizedLeader(r));
        }

        // ---- dead, away and unloaded relatives; several generations

        [Fact]
        public void Dead_and_away_relatives_do_not_break_the_family_views()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Gran", presence: Presence.Dead, spouses: new[] { 2 }), T.P(2, "Grandpa", spouses: new[] { 1 }),
                T.P(3, "Mum", parents: new[] { 1, 2 }, spouses: new[] { 4 }), T.P(4, "Dad", spouses: new[] { 3 }),
                T.P(5, "Aunt", parents: new[] { 1, 2 }, presence: Presence.Away),
                T.Kid(6, new[] { 3, 4 }), T.Kid(7, new[] { 3, 4 })
            }, 1000);
            var house = r.Lineages.First();
            Assert.NotNull(FamilyAlerts.Build(r, house, 0, 2000));
            Assert.NotNull(FamilySummaries.Of(r, house, 0, 2000));
            Assert.NotNull(FamilyTimeline.Recent(r, house, 0, 2000, 0));
            Assert.NotNull(RelationshipView.Build(r, 3, house.Id, RelFilter.Everyone, RelSort.Closest, null));
            var tree = r.BuildTree(house.Id);
            Assert.True(tree.Generations >= 3);                                      // grandparents, parents, children
            Assert.NotEmpty(tree.Units);
        }

        [Fact]
        public void Several_households_in_one_family_are_all_counted()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Elder", spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }),
                T.P(3, "Grown", parents: new[] { 1, 2 }, spouses: new[] { 4 }), T.P(4, "InLaw", spouses: new[] { 3 })
            }, 1000);
            var house = r.Lineages.First();
            Assert.Equal(r.HouseholdsOfLineage(house.Id).Count, FamilySummaries.Of(r, house, 0, 1000).Households);
        }

        // ---- the text and the performance rules

        [Fact]
        public void Every_text_key_the_window_uses_exists()
        {
            string root = RepoRoot();
            Assert.NotNull(root);
            var have = new System.Collections.Generic.HashSet<string>();
            foreach (var f in Directory.GetFiles(Path.Combine(root, "Languages", "English", "Keyed"), "*.xml"))
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"<(WFL_\w+)>")) have.Add(m.Groups[1].Value);
            var dynamicPrefixes = have.Select(k => k).ToList();
            foreach (var f in Directory.GetFiles(Path.Combine(root, "Source", "FamilyLife", "UI"), "*.cs"))
            {
                string src = File.ReadAllText(f);
                foreach (Match m in Regex.Matches(src, "\"(WFL_\\w+)\"\\s*\\.Translate"))
                    Assert.True(have.Contains(m.Groups[1].Value), Path.GetFileName(f) + ": missing text key " + m.Groups[1].Value);
                // Keys built from a prefix and an enum or name: at least one text must exist for the prefix.
                foreach (Match m in Regex.Matches(src, "\"(WFL_\\w+_)\"\\s*\\+"))
                    Assert.True(dynamicPrefixes.Any(k => k.StartsWith(m.Groups[1].Value)), Path.GetFileName(f) + ": no text for prefix " + m.Groups[1].Value);
            }
        }

        [Fact]
        public void Every_tab_alert_and_enum_driven_text_has_its_words()
        {
            string root = RepoRoot();
            var have = new System.Collections.Generic.HashSet<string>();
            foreach (var f in Directory.GetFiles(Path.Combine(root, "Languages", "English", "Keyed"), "*.xml"))
                foreach (Match m in Regex.Matches(File.ReadAllText(f), @"<(WFL_\w+)>")) have.Add(m.Groups[1].Value);
            foreach (FamilyTab t in Enum.GetValues(typeof(FamilyTab))) { Assert.Contains("WFL_FW_Tab_" + t, have); Assert.Contains("WFL_FW_TabTip_" + t, have); }
            foreach (AlertKind k in Enum.GetValues(typeof(AlertKind))) { Assert.Contains("WFL_FW_Alert_" + k, have); Assert.Contains("WFL_FW_AlertShort_" + k, have); Assert.Contains("WFL_FW_AlertTip_" + k, have); }
            foreach (UpcomingKind k in Enum.GetValues(typeof(UpcomingKind))) Assert.Contains("WFL_FW_UpKind_" + k, have);
            foreach (EventTier t in Enum.GetValues(typeof(EventTier))) Assert.Contains("WFL_FW_Tier_" + t, have);
            foreach (SupplyLevel l in Enum.GetValues(typeof(SupplyLevel))) Assert.Contains("WFL_FW_Level_" + l, have);
            foreach (FamilySort s in Enum.GetValues(typeof(FamilySort))) Assert.Contains("WFL_FW_Sort_" + s, have);
            foreach (BondKind k in Enum.GetValues(typeof(BondKind))) Assert.Contains("WFL_FW_Rel_" + k, have);
            foreach (HouseAmbitionKind k in Enum.GetValues(typeof(HouseAmbitionKind))) Assert.Contains("WFL_FW_Approach_" + k, have);
            foreach (var c in EventFilters.All) Assert.Contains("WFL_FW_Cat_" + c, have);
        }

        [Fact]
        public void Drawing_never_loads_textures_reflects_or_scans_the_whole_registry_per_row()
        {
            string ui = Path.Combine(RepoRoot(), "Source", "FamilyLife", "UI");
            foreach (var f in Directory.GetFiles(ui, "Window_Families*.cs").Concat(new[] { Path.Combine(ui, "FamilyKit.cs") }))
            {
                string src = File.ReadAllText(f);
                Assert.DoesNotContain("ContentFinder<Texture2D>", src);                // pictures are loaded once by RoleIcons, never while drawing
                Assert.DoesNotContain("AccessTools", src);                              // optional-mod reflection is cached in the bridge
                Assert.DoesNotContain("Resources.Load", src);
            }
            // The window reads the cached integration state, never reflecting itself.
            Assert.DoesNotContain("GetField", File.ReadAllText(Path.Combine(ui, "Window_Families.cs")));
        }

        [Fact]
        public void The_old_eight_tabs_are_gone_from_the_window()
        {
            string src = File.ReadAllText(Path.Combine(RepoRoot(), "Source", "FamilyLife", "UI", "Window_Families.cs"));
            Assert.DoesNotContain("Tab.Overview", src); Assert.DoesNotContain("Tab.Moments", src); Assert.DoesNotContain("Tab.Zone", src);
            Assert.Contains("FamilyTab", src);
        }

        [Fact]
        public void A_nickname_that_matches_the_surname_is_not_shown_twice()
        {
            Assert.Equal("Garrick", new PersonRecord { Name = "Garrick", LastName = "Garrick" }.FullName);
            Assert.Equal("Hiroshi Garrick", new PersonRecord { Name = "Garrick", FirstName = "Hiroshi", LastName = "Garrick" }.FullName);
            Assert.Equal("Bob Smith", new PersonRecord { Name = "Bob", LastName = "Smith" }.FullName);
            Assert.Equal("Bob", new PersonRecord { Name = "Bob" }.FullName);
        }

        [Fact]
        public void A_moment_tells_how_it_landed_and_old_moments_still_load_empty()
        {
            var e = new EventRecord { EventId = "x", FeelA = 100, FeelB = -70, Aftermath = (int)(AftermathFlags.RiftOpened | AftermathFlags.Memory) };
            Assert.Equal(AftermathFlags.RiftOpened | AftermathFlags.Memory, (AftermathFlags)e.Aftermath);
            var old = new EventRecord { EventId = "x" };
            Assert.Equal(0, old.FeelA); Assert.Equal(0, old.FeelB); Assert.Equal(0, old.Aftermath);
            string xml = File.ReadAllText(Path.Combine(RepoRoot(), "Languages", "English", "Keyed", "FamilyWindow.xml"));
            foreach (var k in new[] { "Feel_Both_Glad", "Feel_One_Hurt", "After_RiftOpened", "After_PromiseMade", "After_Memory" })
                Assert.Contains("WFL_FW_" + k + ">", xml);
        }
    }
}
