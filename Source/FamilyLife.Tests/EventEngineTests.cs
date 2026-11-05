using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class EventEngineTests
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private const int Hour = BondTuning.TicksPerHour;

        private static FamilyRegistry World(out int now)
        {
            var r = new FamilyRegistry();
            now = 8 * Day;
            var obs = new List<PersonObservation>
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(10, new[] { 1, 2 }), T.Kid(11, new[] { 1, 2 }),
                T.P(21, "Jo", "Jones", spouses: new[] { 22 }), T.P(22, "Al", "Jones", spouses: new[] { 21 }), T.Kid(30, new[] { 21, 22 }, "Jones")
            };
            r.Apply(obs, now);
            r.UpdateBonds((a, b) => 0, now, null);
            return r;
        }

        private static Actor Person(FamilyRegistry r, int id, bool child = false) => new Actor
        {
            Id = id, Child = child, AgeYears = child ? 8 : 35, LineageId = r.LineageIdFor(r.GetPerson(id)),
            Profile = new float[DimInfo.Count], Skills = new Dictionary<string, int> { ["Social"] = 8, ["Intellectual"] = 8 }
        };

        private static PairContext Pair(FamilyRegistry r, int now, int a, int b, bool childA = false, bool childB = false)
        {
            var ia = Person(r, a, childA);
            var ib = Person(r, b, childB);
            return new PairContext
            {
                Registry = r, Initiator = ia, Recipient = ib, Now = now,
                InitiatorBond = r.GetBond(a, b), RecipientBond = r.GetBond(b, a),
                Kind = r.KindOf(r.GetPerson(a), r.GetPerson(b)),
                SameFamily = ia.LineageId != 0 && ia.LineageId == ib.LineageId,
                Rift = r.OpenRiftBetween(a, b)
            };
        }

        private static EventResult Force(FamilyRegistry r, EventDef d, PairContext c, EventTier tier)
        {
            return new EventResult { Def = d, Context = c, Tier = tier };
        }

        private sealed class FixedRandom : Random
        {
            private int calls;
            public override double NextDouble() => calls++ == 0 ? 0.6 : 0.0;
        }

        [Fact]
        public void Third_party_candidates_do_not_duplicate_pair_only_events()
        {
            var r = World(out int now);
            var pairOnly = new EventDef { Id = "pair", Bucket = EventBucket.Individual, Motive = new Drive(0.9f) };
            var withThird = new EventDef { Id = "third", Bucket = EventBucket.Individual, Motive = new Drive(0.9f), NeedsThird = true };
            var plain = Pair(r, now, 1, 21);
            var witness = Pair(r, now, 1, 21);
            witness.Third = Person(r, 2);
            var contexts = new[] { plain, witness };
            // Two equal candidates: 60% selects the third-party event, not a duplicate pair event.
            var pick = r.PickEventAmong(contexts, new[] { pairOnly, withThird }, new FixedRandom(), true);
            Assert.True(pick.HasValue);
            Assert.Same(withThird, pick.Value.Key);
            Assert.Same(witness, pick.Value.Value);
            var duplicate = r.PickEventAmong(new[] { plain, plain, plain, plain, witness }, new[] { pairOnly, withThird }, new FixedRandom(), true);
            Assert.Same(withThird, duplicate.Value.Key);
            for (int seed = 0; seed < 100; seed++)
            {
                var original = r.PickEventAmong(new[] { plain }, new[] { pairOnly }, new Random(seed), true);
                var crowded = r.PickEventAmong(contexts, new[] { pairOnly }, new Random(seed), true);
                Assert.Equal(original.HasValue, crowded.HasValue);
                if (crowded.HasValue) Assert.Null(crowded.Value.Value.Third);
            }
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, false)]
        [InlineData(1, true)]
        [InlineData(2, true)]
        public void Daily_limit_protects_both_people_but_leaves_ambient_moments_available(int exhausted, bool fromActivity)
        {
            var r = World(out int now);
            for (int i = 0; i < EventTuning.MaxPerPersonPerDay; i++)
                r.EventLog.Add(new EventRecord { EventId = "other", A = exhausted, B = 21, Tick = now, Weight = EventWeight.Meaningful });
            var c = Pair(r, now, 1, 2);
            c.FromActivity = fromActivity;
            var meaningful = new EventDef { Id = "meaningful", Weight = EventWeight.Meaningful, Motive = new Drive(0.9f) };
            var ambient = new EventDef { Id = "ambient", Weight = EventWeight.Ambient, Motive = new Drive(0.9f) };
            Assert.False(r.Explain(c, new[] { meaningful }, true)[0].Eligible);
            Assert.True(r.Explain(c, new[] { ambient }, true)[0].Eligible);
            c.Now += Day;
            Assert.True(r.Explain(c, new[] { meaningful }, true)[0].Eligible);
        }

        [Fact]
        public void Repeating_an_event_with_other_partners_reduces_its_selection_weight()
        {
            var r=World(out int now);var c=Pair(r,now,1,2);
            var d=new EventDef{Id="repeatable",Weight=EventWeight.Meaningful,Motive=new Drive(.9f)};
            float fresh=r.Explain(c,new[]{d},true)[0].Weight;
            r.EventLog.Add(new EventRecord{EventId=d.Id,A=1,B=21,Tick=now-2*Day,Weight=EventWeight.Meaningful});
            float repeated=r.Explain(c,new[]{d},true)[0].Weight;
            Assert.True(repeated<fresh);
        }

        [Theory]
        [InlineData("A2-04", 1, 10, false)]
        [InlineData("B2-01", 1, 30, false)]
        [InlineData("C2-01", 1, 30, false)]
        [InlineData("A3-09", 10, 11, true)]
        public void Teaching_follows_the_event_roles_despite_an_unrelated_higher_skill(string eventId, int a, int b, bool recipientTeaches)
        {
            var r = World(out int now);
            var c = Pair(r, now, a, b, a == 10, b == 10 || b == 11 || b == 30);
            var teacher = recipientTeaches ? c.Recipient : c.Initiator;
            var learner = recipientTeaches ? c.Initiator : c.Recipient;
            teacher.Skills.Clear(); learner.Skills.Clear();
            teacher.Skills["Cooking"] = 12;
            learner.Skills["Cooking"] = 2;
            learner.Skills["Artistic"] = 20;
            var result = Force(r, EventCatalog.Get(eventId), c, EventTier.Good);
            r.ApplyEvent(result, now);
            Assert.Equal("Cooking", result.XpSkill);
            Assert.Equal(learner.Id, result.XpTo);
            Assert.True(result.XpAmount > 0);
        }

        [Fact]
        public void Lessons_choose_a_skill_the_learner_can_use_including_level_zero()
        {
            var r = World(out int now);
            var c = Pair(r, now, 1, 2);
            c.Initiator.Skills.Clear(); c.Recipient.Skills.Clear();
            c.Initiator.Skills["Artistic"] = 20; // learner has this skill disabled
            c.Initiator.Skills["Cooking"] = 10;
            c.Recipient.Skills["Cooking"] = 0; // enabled, even without experience
            var result = Force(r, EventCatalog.Get("A1-06"), c, EventTier.Good);
            r.ApplyEvent(result, now);
            Assert.Equal("Cooking", result.XpSkill);
            Assert.Equal(2, result.XpTo);
        }

        // ---------------------------------------------------------------- what an outcome does

        [Fact]
        public void A_good_moment_brings_two_people_closer_and_a_poor_one_drives_them_apart()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A1-04");
            var c = Pair(r, now, 1, 2);
            var before = r.GetBond(1, 2).Attachment;
            var trust = r.GetBond(2, 1).Trust;
            var tension = r.GetBond(2, 1).Tension;
            r.ApplyEvent(Force(r, def, c, EventTier.Great), now);
            Assert.True(r.GetBond(1, 2).Attachment > before);
            Assert.True(r.GetBond(2, 1).Trust > trust);

            var def2 = EventCatalog.Get("A1-03");
            var c2 = Pair(r, now, 1, 2);
            float att = r.GetBond(2, 1).Attachment;
            float trust2 = r.GetBond(2, 1).Trust;
            float tension2 = r.GetBond(2, 1).Tension;
            r.ApplyEvent(Force(r, def2, c2, EventTier.Poor), now);
            Assert.True(r.GetBond(2, 1).Attachment < att);
            Assert.True(r.GetBond(2, 1).Trust < trust2);
            Assert.True(r.GetBond(2, 1).Tension > tension2);
        }

        [Fact]
        public void The_person_who_reaches_out_can_feel_closer_while_the_other_is_uncomfortable()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("B1-05");                // a growing attraction: the arc is keen
            Assert.True(def.Arc.Keen);
            var c = Pair(r, now, 1, 21);
            c.InitiatorBond = null; c.RecipientBond = null;
            r.ApplyEvent(Force(r, def, c, EventTier.Poor), now);
            var reaching = r.GetBond(1, 21);
            var receiving = r.GetBond(21, 1);
            Assert.True(reaching.Attachment > receiving.Attachment);          // one feels closer, the other does not
            Assert.True(receiving.Tension > reaching.Tension);                  // and the discomfort is the receiver's
            Assert.True(receiving.Trust < 25f);
        }

        [Fact]
        public void Repeating_the_same_event_gives_less_each_time()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A1-04");
            var gains = new List<float>();
            for (int i = 0; i < 4; i++)
            {
                var c = Pair(r, now + i, 1, 2);
                float before = r.GetBond(2, 1).Attachment;
                r.GetBond(2, 1).Attachment = 30f;                    // keep the starting point equal so only repetition differs
                r.ApplyEvent(Force(r, def, c, EventTier.Great), now + i);
                gains.Add(r.GetBond(2, 1).Attachment - 30f);
            }
            Assert.True(gains[1] < gains[0] && gains[2] < gains[1] && gains[3] <= gains[2]);
        }

        [Fact]
        public void Gains_shrink_as_a_bond_grows_and_nothing_leaves_zero_to_one_hundred()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A1-04");
            var low = Pair(r, now, 1, 2);
            r.GetBond(2, 1).Attachment = 20f;
            r.ApplyEvent(Force(r, def, low, EventTier.Great), now);
            float gainLow = r.GetBond(2, 1).Attachment - 20f;

            r.EventLog.Clear();
            r.GetBond(2, 1).Attachment = 90f;
            r.ApplyEvent(Force(r, def, Pair(r, now + Day * 8, 1, 2), EventTier.Great), now + Day * 8);
            float gainHigh = r.GetBond(2, 1).Attachment - 90f;
            Assert.True(gainHigh < gainLow * 0.6f, gainHigh + " vs " + gainLow);

            var rng = new Random(4);
            var world = new EventHarness(11);
            for (int i = 0; i < 4000; i++)
            {
                var ctx = world.RandomContext();
                var pick = EventCatalog.All.Where(e => e.NeedsChild || true).OrderBy(e => rng.Next()).First();
                world.Registry.ApplyEvent(world.Registry.ResolveEvent(pick, ctx, rng), world.Now + i);
            }
            foreach (var b in world.Registry.Bonds)
            {
                Assert.InRange(b.Attachment, 0f, 100f); Assert.InRange(b.Trust, 0f, 100f);
                Assert.InRange(b.Tension, 0f, 100f); Assert.InRange(b.Familiarity, 0f, 100f);
            }
            foreach (var t in world.Registry.Ties)
            {
                Assert.InRange(t.Goodwill, 0f, 100f); Assert.InRange(t.Grievance, 0f, 100f); Assert.InRange(t.Familiarity, 0f, 100f);
            }
        }

        // ---------------------------------------------------------------- rifts

        [Fact]
        public void A_single_poor_moment_between_calm_people_never_opens_a_rift()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A1-03");                 // it can strain a rift
            Assert.True(def.Effects.HasFlag(EventEffect.StrainsRift));
            var c = Pair(r, now, 1, 2);
            r.GetBond(1, 2).Tension = 5f; r.GetBond(2, 1).Tension = 5f;
            r.ApplyEvent(Force(r, def, c, EventTier.Poor), now);
            Assert.Null(r.OpenRiftBetween(1, 2));
        }

        [Fact]
        public void A_poor_moment_between_people_already_at_odds_can_open_a_rift_once()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A1-03");
            r.GetBond(1, 2).Tension = 60f; r.GetBond(2, 1).Tension = 50f;
            var c = Pair(r, now, 1, 2);
            r.ApplyEvent(Force(r, def, c, EventTier.Poor), now);
            Assert.NotNull(r.OpenRiftBetween(1, 2));
            int count = r.Rifts.Count;
            r.ApplyEvent(Force(r, def, Pair(r, now + Day, 1, 2), EventTier.Poor), now + Day);
            Assert.Equal(count, r.Rifts.Count);                   // never a second rift for the same pair
        }

        [Fact]
        public void A_great_reconciliation_closes_the_rift_through_the_ordinary_path_and_leaves_a_memory()
        {
            var r = World(out int now);
            r.OpenRift(1, 2, RiftCause.Tension, 40f, now);
            var rift = r.OpenRiftBetween(1, 2);
            var def = EventCatalog.Get("A1-10");
            var c = Pair(r, now, 1, 2);
            int memories = r.Memories.Count;
            r.ApplyEvent(Force(r, def, c, EventTier.Great), now);
            Assert.False(rift.IsOpen);
            Assert.True(r.Chronicle.Any(e => e.Kind == FamilyEventKind.RiftResolved));
            Assert.True(r.Memories.Count >= memories);
            Assert.True(r.GetBond(1, 2).Tension < 40f);
        }

        [Fact]
        public void A_good_but_not_great_attempt_softens_a_rift_without_ending_it_and_a_poor_one_tires_both()
        {
            var r = World(out int now);
            r.OpenRift(1, 2, RiftCause.Tension, 50f, now);
            var rift = r.OpenRiftBetween(1, 2);
            var def = EventCatalog.Get("A1-10");
            r.ApplyEvent(Force(r, def, Pair(r, now, 1, 2), EventTier.Good), now);
            Assert.True(rift.IsOpen);
            Assert.True(rift.Severity < 50f);

            float tension = r.GetBond(1, 2).Tension;
            r.ApplyEvent(Force(r, def, Pair(r, now + 2 * Day, 1, 2), EventTier.Poor), now + 2 * Day);
            Assert.True(r.GetBond(1, 2).Tension > tension);
            Assert.Equal(now + 2 * Day, rift.LastTryTick);
        }

        [Fact]
        public void A_conversation_cannot_close_a_neglect_rift_because_that_needs_the_attention_to_be_real()
        {
            var r = World(out int now);
            r.OpenRift(1, 10, RiftCause.Neglect, 30f, now);
            var rift = r.OpenRiftBetween(1, 10);
            var def = EventCatalog.Get("A2-10");
            Assert.True(def.Effects.HasFlag(EventEffect.EasesRift));
            var c = Pair(r, now, 1, 10, false, true);
            r.ApplyEvent(Force(r, def, c, EventTier.Great), now);
            Assert.True(rift.IsOpen);                              // the rule that closes it is attention restored
            Assert.True(rift.Severity < 30f);
            Assert.True(r.GetBond(1, 10).QualityRecent > 0f);      // but the time given counts toward that
        }

        // ---------------------------------------------------------------- promises

        [Fact]
        public void A_promise_is_made_only_by_a_good_outcome_and_is_kept_by_spending_the_time()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A2-10");
            r.ApplyEvent(Force(r, def, Pair(r, now, 1, 10, false, true), EventTier.Flat), now);
            Assert.Empty(r.Commitments);

            var made = Force(r, def, Pair(r, now + Day, 1, 10, false, true), EventTier.Good);
            r.ApplyEvent(made, now + Day);
            Assert.NotEqual(0, made.PromiseMade);
            var promise = r.Commitments.Single();
            float trust = r.GetBond(10, 1).Trust;
            r.RecordTogether(1, 10, 1500, now + Day + 3000);
            Assert.Equal((byte)1, promise.State);
            Assert.True(r.GetBond(10, 1).Trust > trust);
        }

        [Fact]
        public void A_promise_that_goes_unkept_is_broken_and_costs_trust_and_only_then_can_it_be_spoken_about()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A2-10");
            r.ApplyEvent(Force(r, def, Pair(r, now, 1, 10, false, true), EventTier.Great), now);
            var promise = r.Commitments.Single();
            Assert.True(promise.IsOpen);
            Assert.Empty(r.UpdateCommitments(now + Day / 2));                         // not yet due

            var broken = EventCatalog.Get("A2-07");
            var early = Pair(r, now + Day / 2, 1, 10, false, true);
            Assert.Null(early.Broken);                                                  // never invented ahead of time
            float trust = r.GetBond(10, 1).Trust;
            Assert.Single(r.UpdateCommitments(now + 4 * Day));
            Assert.Equal((byte)2, promise.State);
            Assert.True(r.GetBond(10, 1).Trust < trust);

            var ctx = Pair(r, now + 4 * Day, 1, 10, false, true);
            ctx.Broken = r.BrokenPromise(1, 10);
            Assert.NotNull(ctx.Broken);
            var lines = r.Explain(ctx, new[] { broken }, true);
            Assert.True(lines[0].Eligible, lines[0].Reason);
            r.ApplyEvent(Force(r, broken, ctx, EventTier.Great), now + 4 * Day);
            Assert.Equal((byte)3, promise.State);
            Assert.Null(r.BrokenPromise(1, 10));                                        // it is not raised twice
        }

        [Fact]
        public void The_broken_promise_event_does_not_exist_without_a_recorded_promise()
        {
            var r = World(out int now);
            var ctx = Pair(r, now, 1, 10, false, true);
            var lines = r.Explain(ctx, new[] { EventCatalog.Get("A2-07") }, true);
            Assert.False(lines[0].Eligible);
            Assert.Contains("promise", lines[0].Reason);
        }

        // ---------------------------------------------------------------- families

        [Fact]
        public void Standing_between_families_comes_from_incidents_and_moments_not_from_averaging_everyone()
        {
            var r = World(out int now);
            int smith = r.LineageIdFor(r.GetPerson(1));
            int jones = r.LineageIdFor(r.GetPerson(21));
            Assert.Null(r.GetTie(smith, jones));

            var slight = EventCatalog.Get("C1-03");
            var c = Pair(r, now, 1, 21);
            r.ApplyEvent(Force(r, slight, c, EventTier.Poor), now);
            var tie = r.GetTie(smith, jones);
            Assert.NotNull(tie);
            Assert.True(tie.Grievance > 0f);
            Assert.Equal(1, tie.WrongedBy);
            Assert.Equal(21, tie.WrongedPerson);

            // Another member of either family is untouched by it: only the pair's own bonds moved.
            Assert.Null(r.GetBond(2, 22));
            Assert.Null(r.GetBond(10, 30));

            float grievance = tie.Grievance;
            var gift = EventCatalog.Get("C1-01");
            r.ApplyEvent(Force(r, gift, Pair(r, now + Day, 21, 1), EventTier.Great), now + Day);
            Assert.True(tie.Grievance < grievance);
            Assert.True(tie.Goodwill > 0f);
        }

        [Fact]
        public void Children_soften_a_strained_standing_more_than_adults_do_but_never_decide_it()
        {
            var r = World(out int now);
            int smith = r.LineageIdFor(r.GetPerson(1));
            int jones = r.LineageIdFor(r.GetPerson(21));
            r.SetTie(smith, jones, 30f, 10f, 40f, 3, now);
            var bridge = EventCatalog.Get("C3-01");
            Assert.True(bridge.Effects.HasFlag(EventEffect.Bridge));
            r.ApplyEvent(Force(r, bridge, Pair(r, now, 10, 30, true, true), EventTier.Great), now);
            var tie = r.GetTie(smith, jones);
            Assert.True(tie.Grievance < 40f && tie.Grievance > 25f);          // a dent, not a cure
            Assert.NotEqual(FamilyTieState.Allied, tie.State);

            // The same good outcome from adults, in an event that does not bridge, softens it less.
            r.SetTie(smith, jones, 30f, 10f, 40f, 3, now);
            r.ApplyEvent(Force(r, EventCatalog.Get("C1-01"), Pair(r, now + 20 * Day, 1, 21), EventTier.Great), now + 20 * Day);
            float adultsEased = 40f - r.GetTie(smith, jones).Grievance;
            r.SetTie(smith, jones, 30f, 10f, 40f, 3, now);
            r.ApplyEvent(Force(r, bridge, Pair(r, now + 30 * Day, 10, 30, true, true), EventTier.Great), now + 30 * Day);
            float childrenEased = 40f - r.GetTie(smith, jones).Grievance;
            Assert.True(childrenEased > adultsEased * 1.2f && adultsEased > 0f, childrenEased + " vs " + adultsEased);
        }

        [Fact]
        public void A_personal_event_between_outsiders_does_not_involve_their_families()
        {
            var r = World(out int now);
            var friend = EventCatalog.Get("B1-01");
            Assert.False(friend.Effects.HasFlag(EventEffect.TiesFamilies));
            r.ApplyEvent(Force(r, friend, Pair(r, now, 1, 21), EventTier.Great), now);
            Assert.Empty(r.Ties);
            Assert.NotNull(r.GetBond(1, 21));                                    // they now have a bond of their own
            Assert.Equal(BondKind.Acquaintance, r.RelevantPairs().Any(p => p.Key == 1 && p.Value == 21) ? BondKind.Acquaintance : BondKind.Housemate);
        }

        [Fact]
        public void Goodwill_fades_and_a_grievance_eases_slowly_with_no_new_events()
        {
            var r = World(out int now);
            int smith = r.LineageIdFor(r.GetPerson(1));
            int jones = r.LineageIdFor(r.GetPerson(21));
            r.SetTie(smith, jones, 40f, 50f, 30f, 3, now);
            r.UpdateTies(now + 5 * Day);
            var tie = r.GetTie(smith, jones);
            Assert.True(tie.Goodwill < 50f && tie.Goodwill > 40f);
            Assert.True(tie.Grievance < 30f && tie.Grievance > 20f);
        }

        // ---------------------------------------------------------------- picking and cooldowns

        [Fact]
        public void The_same_event_between_the_same_pair_waits_out_its_cooldown()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A1-04");
            r.ApplyEvent(Force(r, def, Pair(r, now, 1, 2), EventTier.Good), now);
            var again = r.Explain(Pair(r, now + Day, 1, 2), new[] { def }, true)[0];
            Assert.False(again.Eligible);
            Assert.Contains("cooldown", again.Reason);
            Assert.NotEqual("cooldown", r.Explain(Pair(r, now + 3 * Day, 1, 2), new[] { def }, true)[0].Reason);
        }

        [Fact]
        public void Picking_is_repeatable_with_a_seed_and_never_returns_something_ineligible()
        {
            var world = new EventHarness(31);
            var a = new List<string>();
            var b = new List<string>();
            for (int i = 0; i < 300; i++)
            {
                var ctx = world.RandomContext();
                var pickA = world.Registry.PickEvent(ctx, EventCatalog.All, new Random(i), true);
                var pickB = world.Registry.PickEvent(ctx, EventCatalog.All, new Random(i), true);
                a.Add(pickA?.Id); b.Add(pickB?.Id);
                if (pickA != null) Assert.True(world.Registry.Explain(ctx, new[] { pickA }, true)[0].Eligible);
            }
            Assert.Equal(a, b);
            Assert.True(a.Any(x => x != null));
        }

        [Fact]
        public void Ambient_events_do_not_use_up_the_daily_allowance_of_meaningful_ones()
        {
            var r = World(out int now);
            r.ApplyEvent(Force(r, EventCatalog.Get("A1-04"), Pair(r, now, 1, 2), EventTier.Good), now);        // ambient
            Assert.Equal(0, r.EventsTodayFor(1, now));
            r.ApplyEvent(Force(r, EventCatalog.Get("A1-02"), Pair(r, now, 1, 2), EventTier.Good), now);        // meaningful
            Assert.Equal(1, r.EventsTodayFor(1, now));
            Assert.Equal(1, r.EventsTodayFor(2, now));
        }

        [Fact]
        public void A_defining_moment_leaves_a_memory_for_the_family_only_when_it_went_very_well_or_very_badly()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("C1-02");
            Assert.Equal(EventWeight.Defining, def.Weight);
            int before = r.Memories.Count;
            r.ApplyEvent(Force(r, def, Pair(r, now, 1, 21), EventTier.Good), now);
            Assert.Equal(before, r.Memories.Count);
            var great = Force(r, def, Pair(r, now + 15 * Day, 1, 21), EventTier.Great);
            r.ApplyEvent(great, now + 15 * Day);
            Assert.True(great.BecameMemory);
            Assert.True(r.Memories.Any(m => m.Kind == MemoryKind.Bond));
        }

        [Fact]
        public void Lessons_pass_on_experience_only_from_the_one_who_knows_more_and_only_for_something_they_can_teach()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A1-06");
            var c = Pair(r, now, 1, 2);
            c.Initiator.Skills["Intellectual"] = 12;
            c.Recipient.Skills["Intellectual"] = 4;
            var res = Force(r, def, c, EventTier.Good);
            r.ApplyEvent(res, now);
            Assert.Equal("Intellectual", res.XpSkill);
            Assert.Equal(2, res.XpTo);
            Assert.True(res.XpAmount > 0f);

            var none = Pair(r, now, 1, 2);
            none.Initiator.Skills["Intellectual"] = 5; none.Recipient.Skills["Intellectual"] = 5;
            none.Initiator.Skills["Social"] = 5; none.Recipient.Skills["Social"] = 5;
            var res2 = Force(r, def, none, EventTier.Good);
            r.ApplyEvent(res2, now + 10 * Day);
            Assert.Null(res2.XpSkill);
        }

        [Fact]
        public void Confronting_a_third_person_heats_things_up_more_when_it_goes_badly()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A1-08");
            var c = Pair(r, now, 1, 2);
            c.Third = Person(r, 21);
            r.ApplyEvent(Force(r, def, c, EventTier.Poor), now);
            float poor = r.GetBond(21, 1).Tension;
            var r2 = World(out int now2);
            var c2 = Pair(r2, now2, 1, 2);
            c2.Third = Person(r2, 21);
            r2.ApplyEvent(Force(r2, def, c2, EventTier.Great), now2);
            Assert.True(poor > r2.GetBond(21, 1).Tension);
        }

        [Fact]
        public void An_event_that_involves_a_third_person_says_so_when_there_is_none()
        {
            var r = World(out int now);
            var line = r.Explain(Pair(r, now, 1, 2), new[] { EventCatalog.Get("A1-08") }, true)[0];
            Assert.False(line.Eligible);
            Assert.Equal("needs a third person involved", line.Reason);
        }

        [Fact]
        public void How_an_event_is_received_depends_on_how_much_the_other_person_trusts_the_one_who_reached_out()
        {
            var r = World(out int now);
            var def = EventCatalog.Get("A1-10");
            var wary = Pair(r, now, 1, 2);
            wary.RecipientBond = new Bond { From = 2, To = 1, Trust = 5f, Attachment = 40f, Tension = 10f };
            var trusting = Pair(r, now, 1, 2);
            trusting.RecipientBond = new Bond { From = 2, To = 1, Trust = 95f, Attachment = 40f, Tension = 10f };
            Assert.True(def.Reception.Value(trusting, def.Skill) > def.Reception.Value(wary, def.Skill) + 0.1f);
        }

        [Fact]
        public void Two_families_with_a_real_grievance_are_asked_to_a_meal_to_mend_it_but_not_over_and_over()
        {
            var r = World(out int now);
            int smith = r.LineageIdFor(r.GetPerson(1));
            int jones = r.LineageIdFor(r.GetPerson(21));
            var tie = r.SetTie(smith, jones, 40f, 5f, 40f, 3, now);
            tie.WrongedBy = 21;                                  // the Joneses gave the offence, so they hold the dinner
            var asked = r.RequestReconciliations(now);
            var request = Assert.Single(asked);
            Assert.Equal("reconciliation", request.Kind);
            Assert.Equal(jones, request.LineageId);
            Assert.Equal(smith, r.LineageIdFor(r.GetPerson(request.SubjectId)));          // and the guest asked is from the other family
            Assert.Empty(r.RequestReconciliations(now + Day));                               // not again at once
            r.GatheringRequests.Clear();                                                      // it was held, or it lapsed
            Assert.Single(r.RequestReconciliations(now + 13 * Day));                         // and after a fortnight it can be tried again

            var calm = World(out int now2);
            calm.SetTie(calm.LineageIdFor(calm.GetPerson(1)), calm.LineageIdFor(calm.GetPerson(21)), 40f, 40f, 5f, 3, now2);
            Assert.Empty(calm.RequestReconciliations(now2));                                 // nothing to mend
        }
    }
}
