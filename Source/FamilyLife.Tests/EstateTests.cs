using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class EstateTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        // Elder (1) and Wife (2) with two grown children (3, 4); Elder is the head of the house.
        private static FamilyRegistry Home()
        {
            var r = new FamilyRegistry();
            r.Apply(Roster(Presence.Colony), 1000);
            return r;
        }

        private static PersonObservation[] Roster(Presence elder, bool juvenileKid = false, Presence kid3 = Presence.Colony) => new[]
        {
            T.P(1, "Elder", presence: elder, spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }),
            T.P(3, "Kid", parents: new[] { 1, 2 }, presence: kid3, juvenile: juvenileKid), T.P(4, "Kid2", parents: new[] { 1, 2 }),
        };

        private static void ElderDies(FamilyRegistry r, int now) => r.Apply(Roster(Presence.Dead), now);

        private static void Own(FamilyRegistry r, int id, PersonalItemKind kind = PersonalItemKind.Weapon, float value = 300, int quality = 4, int owner = 1, bool heirloom = true)
        {
            Assert.True(r.RecordPersonalItem(id, "Def" + id, "Item " + id, kind, owner, value, quality, 1000));
            if (heirloom) r.MarkItemMemory(id);
        }

        private static EstateCase Estate(FamilyRegistry r, int now = 5000) { ElderDies(r, now); return r.OpenEstate(1, null, now); }

        private static void RunAll(FamilyRegistry r, int from, int to, EstateContext ctx = null) { for (int t = from; t <= to; t += Day / 4) r.AdvanceEstates(t, ctx); }

        // ---------- what counts as a personal possession ----------

        [Fact]
        public void Ordinary_stacked_goods_are_never_personal_and_only_colonists_own_things()
        {
            var r = Home();
            Assert.False(r.RecordPersonalItem(50, "Silver", "silver", PersonalItemKind.Other, 1, 5, -1, 1000));       // a cheap, stackable thing
            r.Apply(Roster(Presence.Colony).Concat(new[] { T.P(9, "Far", presence: Presence.Away) }).ToArray(), 1100);
            Assert.False(r.RecordPersonalItem(51, "Sword", "sword", PersonalItemKind.Weapon, 9, 500, 3, 1100));        // not in the colony
            Assert.True(r.RecordPersonalItem(52, "Sword", "sword", PersonalItemKind.Weapon, 1, 50, 2, 1100));
            Assert.Single(r.PersonalItems);
        }

        [Fact]
        public void Plain_cheap_clothes_and_scrap_tools_are_nobodys_own()
        {
            var r = Home();
            Assert.False(r.RecordPersonalItem(70, "Shirt", "plain shirt", PersonalItemKind.Apparel, 1, 18, 1, 1000));
            Assert.False(r.RecordPersonalItem(71, "Knife", "scrap knife", PersonalItemKind.Weapon, 1, 25, 0, 1000));
            Assert.True(r.RecordPersonalItem(72, "Knife", "good knife", PersonalItemKind.Weapon, 1, 25, 3, 1000));        // a well-made thing is worth keeping even if cheap
            Assert.True(r.RecordPersonalItem(73, "Coat", "coat", PersonalItemKind.Apparel, 1, 60, 1, 1000));
        }

        [Fact]
        public void Sustained_use_makes_a_worthwhile_item_an_heirloom_but_a_cheap_one_stays_ordinary()
        {
            var r = Home();
            r.RecordPersonalItem(60, "Rifle", "rifle", PersonalItemKind.Weapon, 1, 200, 3, 1000);
            r.RecordPersonalItem(61, "Shirt", "shirt", PersonalItemKind.Apparel, 1, 50, 1, 1000);
            for (int i = 0; i < 20; i++) { r.NoteItemHeld(60, 1, 24, 2000 + i); r.NoteItemHeld(61, 1, 24, 2000 + i); }
            Assert.True(r.PersonalItemFor(60).Heirloom);
            Assert.Equal(HeirloomBasis.Use, r.PersonalItemFor(60).Basis);
            Assert.False(r.PersonalItemFor(61).Heirloom);
        }

        [Fact]
        public void One_long_gap_between_visits_cannot_fake_a_history_and_a_stranger_cannot_add_hours()
        {
            var r = Home();
            r.RecordPersonalItem(60, "Rifle", "rifle", PersonalItemKind.Weapon, 1, 200, 3, 1000);
            r.NoteItemHeld(60, 1, 100000, 2000);
            Assert.Equal(EstateTuning.MaxNotedHoursPerVisit, r.PersonalItemFor(60).HeldHours);
            Assert.False(r.NoteItemHeld(60, 2, 24, 2100));
            Assert.Equal(EstateTuning.MaxNotedHoursPerVisit, r.PersonalItemFor(60).HeldHours);
        }

        [Fact]
        public void A_very_fine_or_valuable_item_is_a_legacy_item_from_the_start()
        {
            Assert.True(FamilyRegistry.QualifiesAsLegacy(PersonalItemKind.Weapon, 900, 2));
            Assert.True(FamilyRegistry.QualifiesAsLegacy(PersonalItemKind.Art, 450, 2));
            Assert.True(FamilyRegistry.QualifiesAsLegacy(PersonalItemKind.Apparel, 160, 4));
            Assert.False(FamilyRegistry.QualifiesAsLegacy(PersonalItemKind.Apparel, 100, 4));
            var r = Home();
            r.RecordPersonalItem(70, "Blade", "blade", PersonalItemKind.Weapon, 1, 900, 5, 1000);
            Assert.True(r.PersonalItemFor(70).Heirloom);
        }

        [Fact]
        public void Only_the_owner_or_the_head_of_the_house_can_declare_a_family_heirloom()
        {
            var r = Home();
            Own(r, 80, owner: 3, heirloom: false);
            Assert.False(r.DesignateHeirloom(80, 4, 1100));                // a sibling cannot
            Assert.True(r.DesignateHeirloom(80, 3, 1100));                 // the owner can
            Own(r, 81, owner: 3, heirloom: false);
            Assert.True(r.DesignateHeirloom(81, r.HeadOf(r.FamilyOf(3)), 1100));
            Assert.Equal(HeirloomBasis.Designated, r.PersonalItemFor(81).Basis);
        }

        [Fact]
        public void The_item_list_is_bounded_and_heirlooms_are_not_pushed_out_by_ordinary_things()
        {
            var r = Home();
            for (int i = 0; i < EstateTuning.MaxItemsPerOwner; i++) r.RecordPersonalItem(100 + i, "D", "d", PersonalItemKind.Weapon, 1, 100, 2, 1000 + i);
            r.MarkItemMemory(100);
            Assert.True(r.RecordPersonalItem(200, "New", "new", PersonalItemKind.Weapon, 1, 100, 2, 2000));
            Assert.Equal(EstateTuning.MaxItemsPerOwner, r.PersonalItemsOf(1).Count());
            Assert.NotNull(r.PersonalItemFor(100));                        // the heirloom stayed
            Assert.Null(r.PersonalItemFor(101));                           // the oldest ordinary one made room
        }

        // ---------- opening the estate ----------

        [Fact]
        public void An_estate_holds_only_real_things_and_drops_the_rest()
        {
            var r = Home(); Own(r, 300); Own(r, 301); Own(r, 302);
            ElderDies(r, 5000);
            var estate = r.OpenEstate(1, id => id != 301, 5000);
            Assert.Equal(new[] { 300, 302 }.OrderBy(x => x), estate.Items.Select(i => i.ThingId).OrderBy(x => x));
            Assert.Empty(r.PersonalItems.Where(i => i.OwnerId == 1));
        }

        [Fact]
        public void Nothing_resolvable_means_no_estate_and_nothing_is_invented()
        {
            var r = Home(); Own(r, 300);
            ElderDies(r, 5000);
            Assert.Null(r.OpenEstate(1, id => false, 5000));
            Assert.Empty(r.Estates);
        }

        [Fact]
        public void Opening_an_estate_twice_gives_the_same_case_and_a_living_person_has_no_estate()
        {
            var r = Home(); Own(r, 300);
            Assert.Null(r.OpenEstate(1, null, 2000));                      // still alive
            ElderDies(r, 5000);
            var a = r.OpenEstate(1, null, 5000); var b = r.OpenEstate(1, null, 5100);
            Assert.Same(a, b);
            Assert.Single(r.Estates);
        }

        [Fact]
        public void Owners_awaiting_an_estate_are_found_once()
        {
            var r = Home(); Own(r, 300); Own(r, 301);
            ElderDies(r, 5000);
            Assert.Equal(new[] { 1 }, r.OwnersAwaitingEstate());
            r.OpenEstate(1, null, 5000);
            Assert.Empty(r.OwnersAwaitingEstate());
        }

        // ---------- who inherits ----------

        [Fact]
        public void The_default_order_prefers_the_spouse_and_an_ordinary_item_needs_no_dispute()
        {
            var r = Home(); Own(r, 300, heirloom: false, value: 90, quality: 2);
            var estate = Estate(r);
            RunAll(r, 5000, 5000 + 3 * Day);
            var item = estate.Items[0];
            Assert.Equal(2, item.HeirId);
            Assert.False(item.Contested);
            Assert.Equal(EstateStage.Delivery, estate.Stage);
        }

        [Fact]
        public void A_known_designation_beats_the_default_order_but_a_secret_one_does_not()
        {
            var known = Home(); Own(known, 300);
            Assert.True(known.DesignateBeneficiary(1, 4, 300, new[] { 4 }, 2000));
            var estate = Estate(known);
            RunAll(known, 5000, 5000 + 3 * Day);
            Assert.Equal(4, estate.Items[0].HeirId);
            Assert.False(estate.Items[0].Contested);

            var secret = Home(); Own(secret, 300);
            Assert.True(secret.DesignateBeneficiary(1, 4, 300, null, 2000));      // only the owner knew
            Assert.Null(secret.ValidBeneficiary(1, 300));
            var e2 = Estate(secret);
            RunAll(secret, 5000, 5000 + 3 * Day);
            Assert.NotEqual(4, e2.Items[0].HeirId);
        }

        [Fact]
        public void A_designated_heir_who_is_gone_or_too_young_is_not_valid()
        {
            var r = Home(); Own(r, 300);
            r.DesignateBeneficiary(1, 3, 0, new[] { 3 }, 2000);
            r.Apply(Roster(Presence.Colony, kid3: Presence.Away), 2500);
            Assert.Null(r.ValidBeneficiary(1, 300));
            Assert.False(r.DesignateBeneficiary(1, 99, 0, null, 2600));
            Assert.False(r.DesignateBeneficiary(1, 1, 0, null, 2600));
        }

        [Fact]
        public void A_specific_designation_outranks_a_general_one()
        {
            var r = Home(); Own(r, 300);
            r.DesignateBeneficiary(1, 3, 0, new[] { 3 }, 2000);
            r.DesignateBeneficiary(1, 4, 300, new[] { 4 }, 2100);
            Assert.Equal(4, r.ValidBeneficiary(1, 300).HeirId);
        }

        [Fact]
        public void Spouse_a_biological_child_and_an_adopted_child_are_all_eligible_and_a_stranger_is_not()
        {
            var r = Home();
            // 5 joins the house later by a recognised parent link (adoption); 6 is unrelated.
            r.Apply(new[] { T.P(1, "Elder", spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }), T.P(3, "Kid", parents: new[] { 1, 2 }), T.P(4, "Kid2", parents: new[] { 1, 2 }),
                T.P(5, "Adopted", parents: new[] { 1 }), T.P(6, "Stranger", last: "Jones") }, 1500);
            Own(r, 300);
            ElderDies(r, 5000);
            r.Apply(new[] { T.P(1, "Elder", presence: Presence.Dead, spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }), T.P(3, "Kid", parents: new[] { 1, 2 }), T.P(4, "Kid2", parents: new[] { 1, 2 }),
                T.P(5, "Adopted", parents: new[] { 1 }), T.P(6, "Stranger", last: "Jones") }, 5000);
            var estate = r.OpenEstate(1, null, 5000);
            var claimants = r.WeighClaims(estate, estate.Items[0]).Select(c => c.ClaimantId).ToList();
            Assert.Contains(2, claimants); Assert.Contains(3, claimants); Assert.Contains(5, claimants);
            Assert.DoesNotContain(6, claimants);
        }

        [Fact]
        public void Children_who_are_too_young_or_away_cannot_claim_and_a_house_takes_what_no_one_can_receive()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Elder", presence: Presence.Colony), T.Kid(3, new[] { 1 }), T.P(4, "Far", parents: new[] { 1 }, presence: Presence.Away) }, 1000);
            Own(r, 300);
            r.Apply(new[] { T.P(1, "Elder", presence: Presence.Dead), T.Kid(3, new[] { 1 }), T.P(4, "Far", parents: new[] { 1 }, presence: Presence.Away) }, 5000);
            var estate = r.OpenEstate(1, null, 5000);
            Assert.Empty(r.WeighClaims(estate, estate.Items[0]));
        }

        [Fact]
        public void Becoming_head_does_not_award_the_possessions_only_a_claim_on_house_heirlooms()
        {
            var r = Home();
            Own(r, 300, heirloom: false, value: 90, quality: 2);        // an ordinary personal item
            Own(r, 301);                                                  // a house heirloom
            r.DesignateHeirloom(301, 1, 1100);
            var estate = Estate(r);
            int newHead = r.HeadOf(r.FamilyOf(2));
            Assert.NotEqual(1, newHead);
            var ordinary = estate.Items.First(i => i.ThingId == 300);
            var heirloom = estate.Items.First(i => i.ThingId == 301);
            Assert.DoesNotContain(r.WeighClaims(estate, ordinary), c => c.Basis.HasFlag(ClaimBasis.HouseHeirloom));
            Assert.Contains(r.WeighClaims(estate, heirloom), c => c.Basis.HasFlag(ClaimBasis.HouseHeirloom));
            Assert.Equal(0, ordinary.HeirId);                              // nothing is awarded merely by the office changing hands
            Assert.Equal(EstateStage.Mourning, estate.Stage);
        }

        // ---------- disputes ----------

        private static EstateCase Contested(out FamilyRegistry r, int items = 2)
        {
            r = Home();
            for (int i = 0; i < items; i++) Own(r, 400 + i, value: 600 - i * 100);
            // The widow and the eldest child both loved the deceased and both used the first item.
            r.ChangeBond(2, 1, new BondChange(40, 20, 0, 20), 1000); r.ChangeBond(3, 1, new BondChange(45, 20, 0, 20), 1000);
            r.ChangeBond(3, 2, new BondChange(0, 0, 60, 0), 1000); r.ChangeBond(2, 3, new BondChange(0, 0, 60, 0), 1000);      // mother and child are at odds
            var estate = Estate(r);
            RunAll(r, 5000, 5000 + EstateTuning.MournTicks + Day / 2);
            return estate;
        }

        [Fact]
        public void Close_claims_on_an_heirloom_raise_a_case_with_claimants_and_publicly_known_reasons()
        {
            var estate = Contested(out var r);
            var item = estate.Items[0];
            Assert.True(item.Contested);
            Assert.True(item.Claims.Count >= 2);
            Assert.All(item.Claims, c => Assert.NotEqual(ClaimBasis.None, c.Basis));
            Assert.Equal(EstateStage.Claims, estate.Stage);
        }

        [Fact]
        public void The_dispute_moves_through_discussion_and_mediation_and_ends_with_different_heirlooms_for_different_heirs()
        {
            var estate = Contested(out var r, items: 2);
            Assert.True(estate.Items.All(i => i.Contested));
            RunAll(r, 5000 + EstateTuning.MournTicks + Day / 2, 5000 + 12 * Day);
            Assert.Equal(EstateStage.Delivery, estate.Stage);
            Assert.All(estate.Items, i => { Assert.False(i.Contested); Assert.Equal(ItemOutcome.Assigned, i.Outcome); Assert.NotEqual(0, i.HeirId); });
            Assert.Equal(2, estate.Items.Select(i => i.HeirId).Distinct().Count());           // a compromise, not a winner-takes-all
            Assert.NotEqual(0, estate.MediatorId);             // every adult in the house has a stake, so the family settles it themselves
        }

        [Fact]
        public void A_single_contested_heirloom_goes_to_one_claimant_and_the_other_is_either_given_standing_or_keeps_a_grievance()
        {
            var estate = Contested(out var r, items: 1);
            RunAll(r, 5000 + EstateTuning.MournTicks + Day / 2, 5000 + 12 * Day);
            var item = estate.Items[0];
            Assert.NotEqual(0, item.HeirId);
            Assert.True(estate.StewardAppointedId != 0 || estate.GrievanceCount > 0 || item.Claims.Count(c => !c.Relinquished) <= 1);
        }

        [Fact]
        public void Nothing_is_invented_to_satisfy_everyone()
        {
            var estate = Contested(out var r, items: 1);
            RunAll(r, 5000 + EstateTuning.MournTicks + Day / 2, 5000 + 20 * Day);
            Assert.Single(estate.Items);
            Assert.Equal(1, estate.Items.Count(i => i.HeirId != 0));
        }

        [Fact]
        public void A_rival_with_a_somewhat_weaker_claim_still_contests()
        {
            var r = Home(); Own(r, 400, value: 600);
            r.ChangeBond(2, 1, new BondChange(70, 20, 0, 20), 1000); r.ChangeBond(4, 1, new BondChange(10, 20, 0, 20), 1000);
            r.ChangeBond(4, 2, new BondChange(0, 0, 60, 0), 1000);              // the child is at odds with the widow
            var estate = Estate(r);
            RunAll(r, 5000, 5000 + EstateTuning.MournTicks + Day / 2);
            var lead = estate.Items[0].Claims[0]; var second = estate.Items[0].Claims.Skip(1).First();
            Assert.True(lead.Strength - second.Strength >= EstateTuning.ContestMargin);
            Assert.True(estate.Items[0].Contested);
        }

        [Fact]
        public void Compromise_never_gives_the_same_item_to_two_people_or_lets_the_widow_and_child_take_every_item()
        {
            var estate = Contested(out var r, items: 3);
            RunAll(r, 5000 + EstateTuning.MournTicks + Day / 2, 5000 + 12 * Day);
            var heirs = estate.Items.Select(i => i.HeirId).ToList();
            Assert.Equal(3, heirs.Count);
            Assert.True(heirs.GroupBy(x => x).Max(g => g.Count()) <= 2);
        }

        // ---------- the person playing a character ----------

        private static EstateContext Avatar(int id) => new EstateContext { IsAvatar = x => x == id };

        [Fact]
        public void The_avatar_is_asked_and_silence_is_neither_consent_nor_a_gift_to_someone_else()
        {
            var r = Home(); Own(r, 300, heirloom: false, value: 90, quality: 2);
            var estate = Estate(r);
            var ctx = Avatar(2);
            RunAll(r, 5000, 5000 + EstateTuning.MournTicks + Day / 2, ctx);
            var ask = Assert.Single(r.EstateAsksFor(2));
            Assert.Equal(EstateAskKind.Heir, ask.Ask.Kind);
            Assert.Empty(r.DeliveriesDue(5000 + 3 * Day, 10));             // nothing is delivered before they say yes
            RunAll(r, 5000 + 3 * Day, 5000 + 12 * Day, ctx);
            Assert.True(estate.Asks[0].Expired);
            Assert.Empty(r.DeliveriesDue(5000 + 12 * Day, 10));            // lapsed: still not consent
            Assert.Equal(2, estate.Items[0].HeirId);                        // and not given to a child either
            Assert.NotEqual(ItemOutcome.Delivered, estate.Items[0].Outcome);
        }

        [Fact]
        public void A_late_answer_still_counts_while_the_case_is_open_and_relinquishing_passes_the_item_on()
        {
            var r = Home(); Own(r, 300, heirloom: false, value: 90, quality: 2);
            var estate = Estate(r);
            var ctx = Avatar(2);
            RunAll(r, 5000, 5000 + 12 * Day, ctx);
            Assert.True(r.AnswerHeirAsk(estate.Id, 300, 2, EstateAnswer.Accept, 5000 + 13 * Day, ctx));
            Assert.Single(r.DeliveriesDue(5000 + 14 * Day, 10));

            var r2 = Home(); Own(r2, 300, heirloom: false, value: 90, quality: 2);
            var e2 = Estate(r2);
            RunAll(r2, 5000, 5000 + EstateTuning.MournTicks + Day / 2, ctx);
            Assert.True(r2.AnswerHeirAsk(e2.Id, 300, 2, EstateAnswer.Relinquish, 5000 + 3 * Day, ctx));
            Assert.NotEqual(2, e2.Items[0].HeirId);
            Assert.NotEqual(0, e2.Items[0].HeirId);                         // the next in line, not the house
        }

        [Fact]
        public void A_question_that_lapsed_is_still_shown_to_the_person_playing_while_the_thing_is_unsettled()
        {
            var r = Home(); Own(r, 300, heirloom: false, value: 90, quality: 2);
            var estate = Estate(r);
            var ctx = Avatar(2);
            RunAll(r, 5000, 5000 + 12 * Day, ctx);
            Assert.True(estate.Asks[0].Expired);
            var shown = Assert.Single(r.EstateAsksFor(2));
            Assert.True(shown.Ask.Expired);
            Assert.Empty(r.ReadyToCollect(2));
            Assert.True(r.AnswerHeirAsk(estate.Id, 300, 2, EstateAnswer.Accept, 5000 + 13 * Day, ctx));
            Assert.Empty(r.EstateAsksFor(2));
            Assert.Single(r.ReadyToCollect(2));
        }

        [Fact]
        public void Accepting_what_is_decided_does_not_press_a_claim_or_change_the_heir()
        {
            var r = Home(); Own(r, 300);
            r.ChangeBond(2, 1, new BondChange(70, 20, 0, 20), 1000); r.ChangeBond(4, 1, new BondChange(10, 20, 0, 20), 1000);
            r.ChangeBond(4, 2, new BondChange(0, 0, 60, 0), 1000);
            var estate = Estate(r);
            var ctx = Avatar(4);
            RunAll(r, 5000, 5000 + EstateTuning.MournTicks + Day / 2, ctx);
            var ask = r.EstateAsksFor(4).FirstOrDefault();
            Assert.NotNull(ask.Ask);
            Assert.Equal(EstateAskKind.Press, ask.Ask.Kind);
            int heirBefore = estate.Items[0].HeirId;
            Assert.True(r.AnswerHeirAsk(estate.Id, 300, 4, EstateAnswer.Accept, 5000 + 3 * Day, ctx));
            Assert.False(estate.Items[0].Claims.First(c => c.ClaimantId == 4).Pressed);
            Assert.Equal(heirBefore, estate.Items[0].HeirId);
        }

        [Fact]
        public void An_ordinary_item_asks_no_one_to_press_a_claim_it_cannot_win()
        {
            var r = Home(); Own(r, 300, heirloom: false, value: 90, quality: 2);
            Estate(r);
            RunAll(r, 5000, 5000 + EstateTuning.MournTicks + Day / 2, Avatar(4));
            Assert.Empty(r.EstateAsksFor(4));
        }

        [Fact]
        public void Only_the_person_named_can_answer_and_only_their_own_question()
        {
            var r = Home(); Own(r, 300, heirloom: false, value: 90, quality: 2);
            var estate = Estate(r);
            RunAll(r, 5000, 5000 + EstateTuning.MournTicks + Day / 2, Avatar(2));
            Assert.False(r.AnswerHeirAsk(estate.Id, 300, 3, EstateAnswer.Accept, 6000));        // another heir has no question
            Assert.False(r.AnswerHeirAsk(estate.Id, 999, 2, EstateAnswer.Accept, 6000));
            Assert.False(r.AnswerMediation(estate.Id, 2, MediationChoice.Approve, 0, 0, 6000));
        }

        // A bystander (5) lives in the house without being kin, so they can mediate between the widow and the children.
        private static EstateCase Dispute(out FamilyRegistry r, int items, EstateContext ctx)
        {
            r = Home();
            r.Apply(new[] { T.P(1, "Elder", spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }), T.P(3, "Kid", parents: new[] { 1, 2 }), T.P(4, "Kid2", parents: new[] { 1, 2 }), T.P(5, "Lodger") }, 1100);
            r.SetSocialFamily(5, r.FamilyOf(1), 1100);
            for (int i = 0; i < items; i++) Own(r, 400 + i, value: 600 - i * 100);
            r.ChangeBond(2, 1, new BondChange(40, 20, 0, 20), 1000); r.ChangeBond(3, 1, new BondChange(45, 20, 0, 20), 1000);
            r.ChangeBond(3, 2, new BondChange(0, 0, 60, 0), 1000); r.ChangeBond(2, 3, new BondChange(0, 0, 60, 0), 1000);
            ElderDies2(r, 5000);
            var estate = r.OpenEstate(1, null, 5000);
            RunAll(r, 5000, 5000 + EstateTuning.MournTicks + EstateTuning.ClaimTicks + EstateTuning.DiscussionTicks + Day / 2, ctx);
            return estate;
        }

        private static void ElderDies2(FamilyRegistry r, int now) => r.Apply(new[] { T.P(1, "Elder", presence: Presence.Dead, spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }), T.P(3, "Kid", parents: new[] { 1, 2 }), T.P(4, "Kid2", parents: new[] { 1, 2 }), T.P(5, "Lodger") }, now);

        [Fact]
        public void An_avatar_mediator_is_asked_and_the_dispute_waits_for_them()
        {
            var estate = Dispute(out var r, 2, Avatar(5));
            Assert.Equal(EstateStage.Mediation, estate.Stage);
            Assert.Equal(5, estate.MediatorId);
            Assert.Single(r.EstateAsksFor(5), a => a.Ask.Kind == EstateAskKind.Mediate);
            RunAll(r, 5000 + 6 * Day, 5000 + 7 * Day, Avatar(5));
            Assert.Equal(EstateStage.Mediation, estate.Stage);               // not settled behind their back
            Assert.All(estate.Items, i => Assert.Equal(0, i.HeirId));
        }

        [Fact]
        public void An_avatar_mediator_can_approve_the_suggested_spread()
        {
            var estate = Dispute(out var r, 2, Avatar(5));
            var suggested = r.Proposal(estate);
            Assert.True(r.AnswerMediation(estate.Id, 5, MediationChoice.Approve, 0, 0, 5000 + 7 * Day, Avatar(5)));
            Assert.Equal(EstateStage.Delivery, estate.Stage);
            foreach (var item in estate.Items) Assert.Equal(suggested[item.ThingId], item.HeirId);
            Assert.False(r.AnswerMediation(estate.Id, 5, MediationChoice.Approve, 0, 0, 5000 + 7 * Day, Avatar(5)));      // not twice
        }

        [Fact]
        public void An_avatar_mediator_can_give_one_item_to_a_claimant_but_not_to_a_stranger()
        {
            var estate = Dispute(out var r, 2, Avatar(5));
            var first = estate.Items[0]; var second = estate.Items[1];
            int firstHeir = first.Claims.First(c => !c.Relinquished).ClaimantId;
            Assert.False(r.AnswerMediation(estate.Id, 5, MediationChoice.Give, first.ThingId, 99, 5000 + 7 * Day, Avatar(5)));
            Assert.True(r.AnswerMediation(estate.Id, 5, MediationChoice.Give, first.ThingId, firstHeir, 5000 + 7 * Day, Avatar(5)));
            Assert.Equal(firstHeir, first.HeirId);
            Assert.Equal(EstateStage.Mediation, estate.Stage);               // the other item is still to be settled
            Assert.True(r.AnswerMediation(estate.Id, 5, MediationChoice.Give, second.ThingId, second.Claims.First(c => !c.Relinquished).ClaimantId, 5000 + 7 * Day, Avatar(5)));
            Assert.Equal(EstateStage.Delivery, estate.Stage);
        }

        [Fact]
        public void If_the_avatar_mediator_never_answers_the_family_settles_it_without_them()
        {
            var estate = Dispute(out var r, 2, Avatar(5));
            RunAll(r, 5000 + 6 * Day, 5000 + 14 * Day, Avatar(5));
            Assert.Equal(EstateStage.Delivery, estate.Stage);
            Assert.True(estate.Asks.Any(a => a.Kind == EstateAskKind.Mediate && a.Expired && !a.Answered));
            Assert.All(estate.Items, i => Assert.NotEqual(0, i.HeirId));
            Assert.False(estate.Compromised);                                // nobody approved a compromise
        }

        [Fact]
        public void An_ordinary_pawn_mediator_settles_the_dispute_by_themselves()
        {
            var estate = Dispute(out var r, 2, new EstateContext());
            RunAll(r, 5000 + 6 * Day, 5000 + 9 * Day);
            Assert.Equal(EstateStage.Delivery, estate.Stage);
            Assert.Equal(5, estate.MediatorId);
            Assert.True(estate.Compromised);
        }

        // ---------- physical delivery ----------

        private const int Late = 5000 + 4 * Day;

        private static EstateCase Assigned(out FamilyRegistry r, int thing = 300)
        {
            r = Home(); Own(r, thing, heirloom: false, value: 90, quality: 2);
            var estate = Estate(r);
            RunAll(r, 5000, Late);
            Assert.Equal(EstateStage.Delivery, estate.Stage);
            return estate;
        }

        [Fact]
        public void An_assigned_item_is_delivered_only_when_the_game_reports_the_heir_actually_holds_it()
        {
            var estate = Assigned(out var r);
            var (c, item) = Assert.Single(r.DeliveriesDue(Late, 5));
            Assert.True(r.BeginDelivery(c.Id, item.ThingId, Late));
            Assert.False(r.CompleteInheritance(c.Id, item.ThingId, 3, Late + 1000));        // someone else holds it: no remote transfer
            Assert.False(r.CompleteInheritance(c.Id, item.ThingId, 0, Late + 1000));
            Assert.NotEqual(ItemOutcome.Delivered, item.Outcome);
            Assert.True(r.CompleteInheritance(c.Id, item.ThingId, item.HeirId, Late + 1000));
            Assert.Equal(ItemOutcome.Delivered, item.Outcome);
            Assert.Equal(EstateStage.Resolved, estate.Stage);
            Assert.False(r.CompleteInheritance(c.Id, item.ThingId, item.HeirId, Late + 2000));    // never twice
            Assert.Equal(item.HeirId, r.PersonalItemFor(item.ThingId).OwnerId);
            Assert.Contains(1, r.PersonalItemFor(item.ThingId).UsedBy);
        }

        [Fact]
        public void A_dead_or_absent_heir_cannot_receive()
        {
            var estate = Assigned(out var r);
            var item = estate.Items[0];
            r.Apply(new[] { T.P(1, "Elder", presence: Presence.Dead, spouses: new[] { 2 }), T.P(2, "Wife", presence: Presence.Away, spouses: new[] { 1 }), T.P(3, "Kid", parents: new[] { 1, 2 }), T.P(4, "Kid2", parents: new[] { 1, 2 }) }, Late + 500);
            Assert.False(r.CompleteInheritance(estate.Id, item.ThingId, 2, Late + 600));
        }

        [Fact]
        public void A_destroyed_item_is_lost_never_replaced_and_closes_the_case()
        {
            var estate = Assigned(out var r);
            var item = estate.Items[0];
            Assert.True(r.ReportItemLost(estate.Id, item.ThingId, LostReason.Destroyed, Late + 500));
            Assert.Equal(ItemOutcome.Lost, item.Outcome);
            Assert.Equal(EstateStage.Resolved, estate.Stage);
            Assert.False(r.ReportItemLost(estate.Id, item.ThingId, LostReason.Destroyed, Late + 600));
            Assert.Null(r.PersonalItemFor(item.ThingId));
            Assert.Empty(r.DeliveriesDue(Late + 20000, 5));
        }

        [Fact]
        public void An_unreachable_item_is_retried_with_longer_waits_and_then_recorded_as_unfulfilled()
        {
            var estate = Assigned(out var r);
            var item = estate.Items[0];
            int t = Late; int last = 0;
            for (int i = 0; i < EstateTuning.MaxAttempts; i++)
            {
                Assert.Contains(r.DeliveriesDue(t, 5), x => x.Item == item);
                Assert.True(r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.Unreachable, true, t));
                if (item.Terminal) break;
                Assert.Empty(r.DeliveriesDue(t + 1, 5));                    // not tried again straight away
                Assert.True(item.NextTryTick - t >= last); last = item.NextTryTick - t;
                t = item.NextTryTick;
            }
            Assert.Equal(ItemOutcome.Lost, item.Outcome);
            Assert.Equal(LostReason.Unreachable, item.Lost);
        }

        [Fact]
        public void A_forbidden_or_held_item_waits_without_spending_tries_and_then_gives_up_by_time()
        {
            var estate = Assigned(out var r);
            var item = estate.Items[0];
            Assert.True(r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.Forbidden, false, Late));
            Assert.Equal(0, item.Attempts);
            Assert.True(r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.Forbidden, false, Late + EstateTuning.ForbiddenWaitTicks + 10));
            Assert.Equal(ItemOutcome.Lost, item.Outcome);
            Assert.Equal(LostReason.Forbidden, item.Lost);
        }

        [Fact]
        public void An_item_on_a_body_waits_for_the_burial_and_is_then_recorded_as_buried()
        {
            var estate = Assigned(out var r);
            var item = estate.Items[0];
            r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.Buried, false, Late);
            Assert.False(item.Terminal);
            r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.Buried, false, Late + EstateTuning.CorpseWaitTicks + 1);
            Assert.Equal(LostReason.Buried, item.Lost);
        }

        [Fact]
        public void A_house_receives_what_no_person_can_and_a_house_item_is_kept_as_a_house_heirloom()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Elder"), T.P(2, "Other", last: "Jones") }, 1000);
            Own(r, 300);
            r.Apply(new[] { T.P(1, "Elder", presence: Presence.Dead), T.P(2, "Other", last: "Jones") }, 5000);
            var estate = r.OpenEstate(1, null, 5000);
            RunAll(r, 5000, Late);
            var item = estate.Items[0];
            Assert.Equal(0, item.HeirId);
            Assert.Equal(EstateStage.Delivery, estate.Stage);
            if (item.Outcome == ItemOutcome.Lost) { Assert.Equal(LostReason.NoHeir, item.Lost); return; }
            Assert.Equal(r.FamilyOf(1), item.RecipientHouseId);
            Assert.True(r.CompleteInheritance(estate.Id, item.ThingId, 0, Late + 1000));
            Assert.Single(r.HouseHeirlooms(r.FamilyOf(1)));
        }

        // ---------- the rest ----------

        [Fact]
        public void Communal_stock_claims_are_not_changed_by_an_estate()
        {
            var r = Home(); Own(r, 300);
            r.ClaimStock(900, r.FamilyOf(1), r.FamilyOf(1), 1, SupplyKind.Food);
            Estate(r);
            RunAll(r, 5000, 5000 + 12 * Day);
            Assert.Single(r.Claims);
            Assert.Equal(r.FamilyOf(1), r.ClaimFor(900).HolderFamily);
        }

        [Fact]
        public void Maintenance_is_idempotent_and_a_resolved_case_is_left_alone()
        {
            var estate = Assigned(out var r);
            string Snapshot() => string.Join("|", r.Estates.Select(e => e.Id + ":" + e.Stage + ":" + string.Join(",", e.Items.Select(i => i.ThingId + "/" + i.HeirId + "/" + i.Outcome + "/" + i.Contested))));
            r.AdvanceEstates(Late);
            string before = Snapshot();
            for (int i = 0; i < 5; i++) r.AdvanceEstates(Late);
            Assert.Equal(before, Snapshot());
            r.CompleteInheritance(estate.Id, 300, estate.Items[0].HeirId, Late + 1000);
            string done = Snapshot();
            for (int i = 0; i < 5; i++) r.AdvanceEstates(Late + 20000 + i);
            Assert.Equal(done, Snapshot());
        }

        [Fact]
        public void History_is_bounded()
        {
            var r = new FamilyRegistry();
            for (int n = 0; n < EstateTuning.MaxEstateHistory + 8; n++)
            {
                int id = 100 + n;
                r.Apply(new[] { T.P(id, "P" + id, last: "L" + id), T.P(id + 1000, "Heir" + id, last: "L" + id, parents: new[] { id }) }, 1000 + n);
                r.RecordPersonalItem(5000 + n, "Sword", "sword", PersonalItemKind.Weapon, id, 400, 4, 1000 + n);
                r.Apply(new[] { T.P(id, "P" + id, last: "L" + id, presence: Presence.Dead), T.P(id + 1000, "Heir" + id, last: "L" + id, parents: new[] { id }) }, 2000 + n);
                var estate = r.OpenEstate(id, null, 2000 + n);
                r.ReportItemLost(estate.Id, 5000 + n, LostReason.Destroyed, 2500 + n);
            }
            Assert.True(r.Estates.Count <= EstateTuning.MaxEstateHistory + 1);
        }

        [Fact]
        public void Advancing_many_open_estates_stays_cheap_and_does_not_grow_with_finished_ones()
        {
            var r = new FamilyRegistry();
            for (int n = 0; n < 24; n++)
            {
                int id = 100 + n;
                r.Apply(new[] { T.P(id, "P" + id, last: "L" + id), T.P(id + 1000, "Heir" + id, last: "L" + id, parents: new[] { id }) }, 1000 + n);
                for (int k = 0; k < EstateTuning.MaxItemsPerOwner; k++) r.RecordPersonalItem(10000 + n * 100 + k, "Sword", "sword", PersonalItemKind.Weapon, id, 400, 4, 1000 + n);
                r.Apply(new[] { T.P(id, "P" + id, last: "L" + id, presence: Presence.Dead), T.P(id + 1000, "Heir" + id, last: "L" + id, parents: new[] { id }) }, 2000 + n);
                Assert.NotNull(r.OpenEstate(id, null, 2000 + n));
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int t = 0; t < 400; t++) r.AdvanceEstates(3000 + t * 500);
            watch.Stop();
            Assert.True(watch.ElapsedMilliseconds < 4000, "advancing took " + watch.ElapsedMilliseconds + " ms");     // a loose sanity bound, not a TPS measurement
        }

        [Fact]
        public void An_estate_is_visible_to_the_family_and_claimants_but_not_to_a_stranger()
        {
            var estate = Contested(out var r, items: 1);
            Assert.True(r.EstateVisibleTo(estate, 0));
            Assert.True(r.EstateVisibleTo(estate, 2));
            Assert.True(r.EstateVisibleTo(estate, 4));
            r.Apply(new[] { T.P(1, "Elder", presence: Presence.Dead, spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }), T.P(3, "Kid", parents: new[] { 1, 2 }), T.P(4, "Kid2", parents: new[] { 1, 2 }), T.P(9, "Outsider", last: "Jones") }, 9000);
            Assert.False(r.EstateVisibleTo(estate, 9));
        }

        [Fact]
        public void Naming_an_heir_is_done_by_pawns_but_never_for_the_avatar()
        {
            var r = Home(); Own(r, 300);
            Assert.Equal(0, r.NameBeneficiariesNow(2000, id => id == 1));
            Assert.Empty(r.Beneficiaries);
            Assert.Equal(1, r.NameBeneficiariesNow(2000, null));
            Assert.Single(r.Beneficiaries);
            Assert.Equal(0, r.NameBeneficiariesNow(2100, null));            // once is enough
        }
    }
}
