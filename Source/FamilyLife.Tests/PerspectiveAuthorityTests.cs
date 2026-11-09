using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>Strick Jessop looks at House Northcott: what he may know, what he may do, and what the numbers say.</summary>
    public class PerspectiveAuthorityTests
    {
        private const int Strick = 1, Ivan = 10, Debby = 11;

        private static FamilyRegistry World()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(Strick, "Strick", last: "Jessop"),
                T.P(Ivan, "Ivan", last: "Northcott", spouses: new[] { Debby }), T.P(Debby, "Debby", last: "Northcott", spouses: new[] { Ivan }),
                T.Kid(12, new[] { Ivan, Debby }, last: "Northcott", name: "Sally")
            }, 1000);
            r.UpdateBonds((a, b) => (int?)0, 1000, null);
            return r;
        }

        private static Lineage Northcott(FamilyRegistry r) => r.Lineages.First(l => r.MembersOf(l.Id).Any(p => p.Id == Ivan));

        private static EventRecord Moment(int a, int b, int tick, EventTier tier, int third = 0, int feelB = 0) =>
            new EventRecord { EventId = "A2-04", A = a, B = b, Third = third, Tick = tick, Tier = tier, Weight = EventWeight.Meaningful, FeelB = feelB };

        // ---- the story an outsider can tell

        [Fact]
        public void A_visitors_chapter_is_built_from_their_own_history_with_the_house()
        {
            var r = World(); var house = Northcott(r);
            r.EventLog.Add(Moment(Debby, Strick, 5000, EventTier.Poor, 0, -50));
            r.EventLog.Add(Moment(Ivan, Debby, 6000, EventTier.Good, Strick, 40));
            var m = StoryModel.Build(r, house, Strick, 10000);
            Assert.Equal(Acquaintance.Acquainted, m.Acquaintance);
            Assert.StartsWith("WFL_FW_Ch_Dealings_", m.Chapter.Title.Key);
            Assert.Contains(m.Chapter.Body, b => b.Key == "WFL_FW_Clause_Moment");
            Assert.Contains(m.Chapter.Body, b => b.Key == "WFL_FW_Ch_DealingsPrivate");
            Assert.Contains(m.Threads, t => t.Kind == ThreadKind.Dealings);
        }

        [Fact]
        public void The_houses_private_chapter_stays_hidden_from_a_visitor()
        {
            var r = World(); var house = Northcott(r);
            house.Situation.EvaluatedTick = 5; house.Situation.Sustenance = 5f; house.Situation.Cohesion = 90f;
            house.Ambition.Primary = HouseAmbitionKind.IncreaseInfluence; house.Ambition.PrimaryReason = HouseNeed.Influence;
            r.EventLog.Add(Moment(Debby, Strick, 5000, EventTier.Poor, 0, -50));
            var m = StoryModel.Build(r, house, Strick, 10000);
            Assert.DoesNotContain(m.Threads, t => t.Kind == ThreadKind.Ambition || t.Kind == ThreadKind.Food);
            var all = m.Chapter.Body.Concat(m.Chapter.Supporting).Select(n => n.Key).Concat(new[] { m.Chapter.Title.Key });
            Assert.DoesNotContain(all, k => k.Contains("Amb") || k.Contains("Why_") || k.Contains("Food") || k.Contains("_Is_"));
        }

        [Fact]
        public void With_no_history_a_visitor_knows_the_house_by_reputation()
        {
            var r = World(); var house = Northcott(r);
            var m = StoryModel.Build(r, house, Strick, 10000);
            Assert.Equal(Acquaintance.Reputation, m.Acquaintance);
            Assert.Equal("WFL_FW_Ch_RepTitle", m.Chapter.Title.Key);
            Assert.Contains("Ivan", m.Chapter.Body[0].Args);
            Assert.Empty(m.Threads);
        }

        [Fact]
        public void The_overview_and_insiders_still_get_the_internal_chapter()
        {
            var r = World(); var house = Northcott(r);
            house.Situation.EvaluatedTick = 5; house.Situation.Sustenance = 5f;
            Assert.Contains(StoryModel.Build(r, house, 0, 10000).Threads, t => t.Kind == ThreadKind.Food);
            Assert.Contains(StoryModel.Build(r, house, Ivan, 10000).Threads, t => t.Kind == ThreadKind.Food);
        }

        [Fact]
        public void A_visitor_sees_a_promise_they_are_part_of_but_not_two_other_peoples()
        {
            var r = World(); var house = Northcott(r);
            r.MakePromise(Debby, Strick, "A2-04", 1000, 1, 1);
            r.MakePromise(Ivan, Debby, "A2-04", 1000, 1, 1);
            var promises = StoryModel.Build(r, house, Strick, 1000).Threads.Where(t => t.Kind == ThreadKind.Promise).ToList();
            Assert.Single(promises);
            Assert.Contains(Strick, promises[0].People);
        }

        [Fact]
        public void A_visitors_timeline_and_chronicle_leave_out_the_houses_private_chronicle()
        {
            Assert.True(StoryVisibility.PublicKind(FamilyEventKind.Birth));
            Assert.True(StoryVisibility.PublicKind(FamilyEventKind.MarriageFormed));
            Assert.False(StoryVisibility.PublicKind(FamilyEventKind.RiftOpened));
            Assert.False(StoryVisibility.PublicKind(FamilyEventKind.BondCooled));
            var r = World(); var house = Northcott(r);
            var rift = new TimelineEntry { Kind = TimelineKind.Chronicle, Chronicle = new FamilyEvent { Kind = FamilyEventKind.RiftOpened } };
            Assert.False(StoryVisibility.Visible(r, house, Strick, rift));
            Assert.True(StoryVisibility.Visible(r, house, Ivan, rift));
            Assert.True(StoryVisibility.Visible(r, house, 0, rift));
        }

        // ---- the knowledge policy

        [Fact]
        public void Knowledge_scopes_follow_who_is_asking()
        {
            var r = World(); var house = Northcott(r);
            foreach (var viewer in new[] { Strick, Ivan, 0 }) Assert.True(Knowledge.Allows(r, viewer, house.Id, InfoScope.Public));
            Assert.False(Knowledge.Allows(r, Strick, house.Id, InfoScope.HousePrivate));
            Assert.True(Knowledge.Allows(r, Ivan, house.Id, InfoScope.HousePrivate));
            Assert.False(Knowledge.Allows(r, Ivan, house.Id, InfoScope.Secret));
            Assert.True(Knowledge.Allows(r, 0, house.Id, InfoScope.Secret));
            Assert.False(Knowledge.Allows(r, Strick, house.Id, InfoScope.Observable, Debby));
            r.EventLog.Add(Moment(Debby, Strick, 5000, EventTier.Poor));
            Assert.True(Knowledge.Allows(r, Strick, house.Id, InfoScope.Observable, Debby));
            Assert.True(Knowledge.Allows(r, Strick, house.Id, InfoScope.Shared, Strick));
            Assert.False(Knowledge.Allows(r, Strick, house.Id, InfoScope.Shared, Ivan));
        }

        [Fact]
        public void Feelings_are_described_in_words_and_public_ties_are_only_kin()
        {
            Assert.Equal("Strained", Knowledge.Describe(new RelRow { Tension = 60f }));
            Assert.Equal("Uneasy", Knowledge.Describe(new RelRow { Tension = 35f, Attachment = 80f }));
            Assert.Equal("Warm", Knowledge.Describe(new RelRow { Attachment = 70f, Trust = 20f, Familiarity = 80f }));
            Assert.Equal("Trusting", Knowledge.Describe(new RelRow { Attachment = 70f, Trust = 70f, Familiarity = 80f }));
            Assert.Equal("Unfamiliar", Knowledge.Describe(new RelRow { Attachment = 10f, Familiarity = 5f }));
            var r = World(); var house = Northcott(r);
            var ties = Knowledge.PublicTies(r, house);
            Assert.Contains(ties, t => (t.Key == Ivan && t.Value == Debby) || (t.Key == Debby && t.Value == Ivan));
            Assert.All(ties, t => Assert.NotNull(r.KindOf(r.GetPerson(t.Key), r.GetPerson(t.Value))));
        }

        // ---- authority

        [Fact]
        public void A_visitor_cannot_declare_an_heir_for_another_house()
        {
            var r = World(); var house = Northcott(r);
            var auth = HouseAuthority.Check(r, Strick, house.Id, HouseAct.DeclareHeir);
            Assert.False(auth.Allowed); Assert.True(auth.Hidden);
            Assert.False(HouseAuthority.DeclareHeir(r, Strick, house.Id, 12, null, 1));
            Assert.Equal(0, house.DesignatedHeirId);
        }

        [Fact]
        public void Only_the_head_declares_an_heir_and_the_overview_acts_as_the_head()
        {
            var r = World(); var house = Northcott(r);
            Assert.True(HouseAuthority.Check(r, Ivan, house.Id, HouseAct.DeclareHeir).Allowed);
            var spouse = HouseAuthority.Check(r, Debby, house.Id, HouseAct.DeclareHeir);
            Assert.False(spouse.Allowed); Assert.False(spouse.Hidden);
            Assert.Equal("WFL_FW_Auth_DeclareHeir", spouse.Why.Key);
            Assert.False(HouseAuthority.DeclareHeir(r, Debby, house.Id, 12, null, 1));
            Assert.True(HouseAuthority.DeclareHeir(r, 0, house.Id, Debby, null, 1));
            Assert.Equal(Debby, house.DesignatedHeirId);
        }

        [Fact]
        public void A_visitor_cannot_edit_the_crest_create_a_branch_or_appoint_a_steward()
        {
            var r = World(); var house = Northcott(r);
            int primary = house.CrestPrimary, mark = house.CrestMark;
            Assert.False(HouseAuthority.EditCrest(r, Strick, house.Id, 3, 4, 2, 1));
            Assert.Equal(primary, house.CrestPrimary); Assert.Equal(mark, house.CrestMark);
            int before = r.Lineages.Count;
            Assert.Null(HouseAuthority.CreateBranch(r, Strick, house.Id, Ivan, "Rival", 1));
            Assert.Equal(before, r.Lineages.Count);
            Assert.False(HouseAuthority.AppointSteward(r, Strick, house.Id, Debby, true, 1));
            Assert.Empty(house.Stewards);
        }

        [Fact]
        public void The_head_can_edit_the_crest_and_the_check_runs_again_inside_the_action()
        {
            var r = World(); var house = Northcott(r);
            Assert.True(HouseAuthority.EditCrest(r, Ivan, house.Id, 3, 4, 2, 1));
            Assert.Equal(3, house.CrestPrimary);
            // Called directly, without the window: the same refusal.
            Assert.False(HouseAuthority.EditCrest(r, Strick, house.Id, 5, 5, 5, 0));
            Assert.Equal(3, house.CrestPrimary);
            Assert.False(HouseAuthority.Check(r, Strick, house.Id, HouseAct.ChangeHouseholdPolicy).Allowed);
            Assert.False(HouseAuthority.Check(r, 9999, house.Id, HouseAct.PlanPrivateCeremony).Allowed);
        }

        [Fact]
        public void A_visitor_cannot_designate_the_heirloom_of_another_house()
        {
            var r = World();
            Assert.False(HouseAuthority.DesignateHeirloom(r, Strick, 424242, 1));
        }

        // ---- offices, counts and freshness

        [Fact]
        public void House_protector_is_not_listed_while_nothing_can_hold_it()
        {
            var r = World(); var house = Northcott(r);
            Assert.DoesNotContain(RoleCards.For(r, house, Strick), c => c.Role == FamilyRole.HouseProtector);
            Assert.DoesNotContain(RoleCards.For(r, house, 0), c => c.Role == FamilyRole.HouseProtector);
        }

        [Fact]
        public void The_sidebar_header_and_home_count_from_the_same_definitions()
        {
            var r = World(); var house = Northcott(r);
            var counts = HouseCountsOf.Of(r, house);
            var summary = FamilySummaries.Of(r, house, 0, 1000);
            Assert.Equal(counts.HouseMembers, summary.Members);
            Assert.Equal(counts.Residents, summary.Residents);
            Assert.Equal(counts.Households, summary.Households);
            Assert.Equal(r.Persons.Count(p => p.InColony && r.BelongsToFamily(p, house.Id)), counts.HouseMembers);
        }

        [Fact]
        public void A_cached_view_is_stale_when_anything_it_depends_on_changes()
        {
            var reg = new object();
            bool Fresh(object built, object now, int bl, int l, int br, int rev, int bv, int v, int bt, int t) => ViewFreshness.IsFresh(built, now, bl, l, br, rev, bv, v, bt, t, 600);
            Assert.True(Fresh(reg, reg, 1, 1, 5, 5, 0, 0, 100, 200));
            Assert.False(Fresh(reg, reg, 1, 2, 5, 5, 0, 0, 100, 200));       // another House selected
            Assert.False(Fresh(reg, reg, 1, 1, 5, 5, 0, 7, 100, 200));       // another person being played
            Assert.False(Fresh(reg, reg, 1, 1, 5, 6, 0, 0, 100, 200));       // records changed: a new event, a bond, membership, household, role, disclosure
            Assert.False(Fresh(reg, new object(), 1, 1, 5, 5, 0, 0, 100, 200)); // a loaded save is a new registry
            Assert.False(Fresh(reg, reg, 1, 1, 5, 5, 0, 0, 100, 800));       // too old
            Assert.False(Fresh(reg, reg, 1, 1, 5, 5, 0, 0, 900, 200));       // time went backwards: an earlier save
        }

        [Fact]
        public void Authority_refusals_have_words_for_every_act()
        {
            var text = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "Languages", "English", "Keyed", "FamilyStory.xml"));
            foreach (var key in new[] { "WFL_FW_Auth_Outsider", "WFL_FW_Auth_Manage", "WFL_FW_Auth_DeclareHeir", "WFL_FW_Auth_AppointSteward", "WFL_FW_Auth_AppointOfficer", "WFL_FW_Auth_ChangeSuccessionCustom" })
                Assert.Contains("<" + key + ">", text);
        }

        private static string RepoRoot()
        {
            var dir = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
            while (dir != null && !System.IO.Directory.Exists(System.IO.Path.Combine(dir.FullName, "Languages"))) dir = dir.Parent;
            return dir?.FullName;
        }
    }
}
