using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    /// <summary>
    /// The game's save code cannot run outside RimWorld, but what it saves is exactly the public fields of each record (PersistenceCoverageTests checks that none is missed).
    /// So a save and a load can be imitated faithfully enough: copy every public field into a fresh registry, drop everything that is only a cache, and carry on.
    /// If anything the rules need lived somewhere else, the copy would behave differently from the original, and these tests would show it.
    /// This does not exercise Scribe, XML, or the game; it checks that the model has nowhere to hide state.
    /// </summary>
    internal static class SaveSim
    {
        // Indexes and caches that the real load rebuilds.
        private static readonly string[] Skip = { "personById", "lineageById", "householdById", "childrenByParent", "indexed", "bondByPair", "timeByPerson", "deadThisSweep", "claimByThing", "agencyByPerson", "kinshipCache" };

        public static FamilyRegistry Reload(FamilyRegistry original)
        {
            var copy = (FamilyRegistry)Copy(original);
            copy.RebuildIndexes();
            return copy;
        }

        private static object Copy(object value)
        {
            if (value == null) return null;
            var type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal) return value;
            if (value is IList list)
            {
                var elementType = type.IsArray ? type.GetElementType() : type.GetGenericArguments()[0];
                var result = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType));
                foreach (var item in list) result.Add(Copy(item));
                return result;
            }
            var clone = Activator.CreateInstance(type, true);
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                if (Skip.Contains(f.Name)) continue;
                if (f.IsInitOnly && !f.FieldType.IsGenericType) continue;
                if (f.Name.EndsWith("k__BackingField")) continue;
                if (f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(Dictionary<,>)) continue;
                if (typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                if (f.IsInitOnly) { f.SetValue(clone, Copy(f.GetValue(value))); continue; }
                f.SetValue(clone, Copy(f.GetValue(value)));
            }
            return clone;
        }
    }

    public class SaveReloadTests
    {
        private const int Day = ActivityTuning.TicksPerDay;

        private static PersonObservation[] Roster(Presence elder) => new[]
        {
            T.P(1, "Elder", presence: elder, spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }),
            T.P(3, "Kid", parents: new[] { 1, 2 }), T.P(4, "Kid2", parents: new[] { 1, 2 }), T.P(5, "Lodger")
        };

        private static string EstateSnapshot(FamilyRegistry r) => string.Join("|", r.Estates.Select(e =>
            e.Id + ":" + e.Stage + ":" + e.MediatorId + ":" + e.Compromised + ":" + string.Join(",", e.Items.Select(i => i.ThingId + "/" + i.HeirId + "/" + i.Outcome + "/" + i.Contested + "/" + i.Lost + "/" + i.Attempts)) +
            ":asks=" + string.Join(",", e.Asks.Select(a => a.PersonId + "/" + a.Kind + "/" + a.Answered + "/" + a.Expired))) + "#" + string.Join(",", r.PersonalItems.Select(i => i.ThingId + "/" + i.OwnerId)));

        private static FamilyRegistry Dispute()
        {
            var r = new FamilyRegistry();
            r.Apply(Roster(Presence.Colony), 1000);
            r.SetSocialFamily(5, r.FamilyOf(1), 1100);
            for (int i = 0; i < 2; i++) { r.RecordPersonalItem(400 + i, "Sword", "sword " + i, PersonalItemKind.Weapon, 1, 600 - i * 100, 4, 1000); r.MarkItemMemory(400 + i); }
            r.ChangeBond(2, 1, new BondChange(40, 20, 0, 20), 1000); r.ChangeBond(3, 1, new BondChange(45, 20, 0, 20), 1000);
            r.ChangeBond(3, 2, new BondChange(0, 0, 60, 0), 1000); r.ChangeBond(2, 3, new BondChange(0, 0, 60, 0), 1000);
            r.DesignateBeneficiary(1, 4, 400, new[] { 4 }, 1500);
            r.Apply(Roster(Presence.Dead), 5000);
            r.OpenEstate(1, null, 5000);
            return r;
        }

        private static void Run(FamilyRegistry r, int from, int to, EstateContext ctx = null) { for (int t = from; t <= to; t += Day / 4) r.AdvanceEstates(t, ctx); }

        [Theory]
        [InlineData(0)] [InlineData(1)] [InlineData(3)] [InlineData(5)] [InlineData(8)]
        public void An_estate_reloaded_at_any_point_carries_on_exactly_like_the_original(int daysIn)
        {
            var original = Dispute();
            Run(original, 5000, 5000 + daysIn * Day);
            var reloaded = SaveSim.Reload(original);
            Assert.NotSame(original.Estates[0], reloaded.Estates[0]);        // a real copy, not the same objects
            for (int i = 0; i < original.Estates[0].Items.Count; i++) Assert.Equal(original.Estates[0].Items[i].Claims.Select(c => c.ClaimantId + "/" + c.Basis + "/" + c.Strength).ToList(), reloaded.Estates[0].Items[i].Claims.Select(c => c.ClaimantId + "/" + c.Basis + "/" + c.Strength).ToList());
            Assert.Equal(EstateSnapshot(original), EstateSnapshot(reloaded));
            Run(original, 5000 + daysIn * Day + 1, 5000 + 14 * Day);
            Run(reloaded, 5000 + daysIn * Day + 1, 5000 + 14 * Day);
            Assert.Equal(EstateSnapshot(original), EstateSnapshot(reloaded));
            Assert.Equal(EstateStage.Delivery, original.Estates[0].Stage);
        }

        [Fact]
        public void An_open_question_for_the_person_playing_survives_a_reload_and_is_still_theirs_to_answer()
        {
            var original = Dispute();
            var ctx = new EstateContext { IsAvatar = id => id == 5 };
            Run(original, 5000, 5000 + 8 * Day, ctx);
            Assert.Single(original.EstateAsksFor(5), a => a.Ask.Kind == EstateAskKind.Mediate);
            var reloaded = SaveSim.Reload(original);
            var ask = Assert.Single(reloaded.EstateAsksFor(5));
            Assert.Equal(EstateAskKind.Mediate, ask.Ask.Kind);
            Assert.True(reloaded.AnswerMediation(reloaded.Estates[0].Id, 5, MediationChoice.Approve, 0, 0, 5000 + 8 * Day, ctx));
            Assert.Equal(EstateStage.Delivery, reloaded.Estates[0].Stage);
            Assert.Equal(EstateStage.Mediation, original.Estates[0].Stage);          // the original, never answered, is unchanged
        }

        [Fact]
        public void A_delivery_under_way_is_not_lost_not_repeated_and_can_finish_after_a_reload()
        {
            var original = Dispute();
            Run(original, 5000, 5000 + 14 * Day);
            var (c, item) = original.DeliveriesDue(5000 + 14 * Day, 5).First();
            Assert.True(original.BeginDelivery(c.Id, item.ThingId, 5000 + 14 * Day));
            var reloaded = SaveSim.Reload(original);
            var again = reloaded.Estates[0].Items.First(i => i.ThingId == item.ThingId);
            Assert.Equal(ItemOutcome.Delivering, again.Outcome);
            Assert.DoesNotContain(reloaded.DeliveriesDue(5000 + 14 * Day + 10, 5), d => d.Item.ThingId == item.ThingId);       // one job at a time
            Assert.True(reloaded.CompleteInheritance(c.Id, item.ThingId, again.HeirId, 5000 + 14 * Day + 100));
            Assert.False(reloaded.CompleteInheritance(c.Id, item.ThingId, again.HeirId, 5000 + 14 * Day + 200));
            Assert.Equal(again.HeirId, reloaded.PersonalItemFor(item.ThingId).OwnerId);
        }

        [Fact]
        public void A_finished_estate_stays_finished_after_a_reload()
        {
            var r = Dispute();
            Run(r, 5000, 5000 + 14 * Day);
            foreach (var (c, item) in r.DeliveriesDue(5000 + 14 * Day, 5).ToList()) r.CompleteInheritance(c.Id, item.ThingId, item.HeirId, 5000 + 14 * Day + 10);
            Assert.Equal(EstateStage.Resolved, r.Estates[0].Stage);
            var reloaded = SaveSim.Reload(r);
            Run(reloaded, 5000 + 15 * Day, 5000 + 40 * Day);
            Assert.Equal(EstateSnapshot(r), EstateSnapshot(reloaded));
        }

        // ---------- the rescue ----------

        private static FamilyRegistry Rescue(out int visitId)
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Elder"), T.P(2, "Two", parents: new[] { 1 }), T.P(10, "Far", parents: new[] { 1 }, presence: Presence.Away), T.P(11, "Farther", parents: new[] { 1 }, presence: Presence.Away) }, 1000);
            var request = r.RequestCeremony(CeremonyKind.GrandReunion, r.FamilyOf(1), 0, 0, 2000, 2 * Day, 12 * Day);
            request.VisitorIds.AddRange(new[] { 10, 11 });
            var visit = r.PlanKinVisit(request.Id, new[] { 10, 11 }, 2000);
            visit.Complication = true;
            r.StrandKinVisit(visit.Id, 77, visit.ArriveTick);
            r.OfferKinRescue(visit.Id, RescueComplication.Weather, visit.ArriveTick);
            r.BindKinRescueQuest(visit.Id, 42);
            visitId = visit.Id;
            return r;
        }

        private static string RescueSnapshot(FamilyRegistry r) => string.Join("|", r.KinVisits.Select(v =>
            v.Id + ":" + v.State + ":" + v.Rescue + ":" + v.Trouble + ":" + v.QuestId + ":" + v.SiteId + ":" + v.RescueDeadlineTick + ":" + v.ArriveTick + ":" + string.Join(",", v.KinIds) + ":" + string.Join(",", v.RescuerIds) + ":" + string.Join(",", v.LostIds))
            + "#" + string.Join(",", r.GatheringRequests.Select(q => q.Id + "/" + string.Join(",", q.VisitorIds) + "/" + q.NotBefore + "/" + q.Expires)));

        [Theory]
        [InlineData("offered")] [InlineData("accepted")] [InlineData("onsite")] [InlineData("resumed")] [InlineData("landing")]
        public void A_rescue_reloaded_at_every_stage_finishes_the_same_way_as_the_original(string stage)
        {
            var original = Rescue(out int id);
            var visit = original.KinVisitFor(id);
            int t = visit.ArriveTick + Day / 2;
            if (stage != "offered") original.AcceptKinRescue(id, 500, t);
            if (stage == "onsite") original.ReachKinRescue(id, t + 100);
            if (stage == "resumed" || stage == "landing") { original.CompleteKinRescue(id, new[] { 2 }, new[] { 11 }, t + 200); }
            if (stage == "landing") original.MarkKinLanding(id, new[] { 10 }, new[] { 10 }, visit.ArriveTick);
            var reloaded = SaveSim.Reload(original);
            Assert.Equal(RescueSnapshot(original), RescueSnapshot(reloaded));
            foreach (var r in new[] { original, reloaded })
            {
                var v = r.KinVisitFor(id);
                switch (stage)
                {
                    case "offered": Assert.True(r.AcceptKinRescue(id, 500, t + 300)); r.ReachKinRescue(id, t + 400); Assert.True(r.CompleteKinRescue(id, new[] { 2 }, null, t + 500)); break;
                    case "accepted": r.ReachKinRescue(id, t + 400); Assert.True(r.CompleteKinRescue(id, new[] { 2 }, new[] { 10 }, t + 500)); break;
                    case "onsite": Assert.True(r.CompleteKinRescue(id, new[] { 2 }, null, t + 500)); break;
                    case "resumed": Assert.True(r.MarkKinArrived(id, new[] { 10 }, v.ArriveTick)); break;
                    default: Assert.True(r.MarkKinArrived(id, new[] { 10 }, v.ArriveTick + 10)); break;
                }
            }
            Assert.Equal(RescueSnapshot(original), RescueSnapshot(reloaded));
        }

        [Fact]
        public void A_rescue_that_ended_before_a_reload_cannot_end_again_after_it()
        {
            var original = Rescue(out int id);
            original.FailKinRescue(id, RescueStage.Declined, original.KinVisitFor(id).ArriveTick + 50);
            var reloaded = SaveSim.Reload(original);
            Assert.False(reloaded.FailKinRescue(id, RescueStage.Abandoned, original.KinVisitFor(id).ArriveTick + 60));
            Assert.False(reloaded.AcceptKinRescue(id, 1, original.KinVisitFor(id).ArriveTick + 60));
            Assert.Equal(RescueStage.Declined, reloaded.KinVisitFor(id).Rescue);
            reloaded.MaintainKinVisits(original.KinVisitFor(id).ArriveTick + 100 * Day);
            Assert.Equal(RescueSnapshot(original), RescueSnapshot(reloaded));
        }

        [Fact]
        public void A_party_saved_by_an_earlier_version_with_no_quest_fields_still_loads_and_resolves()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Elder"), T.P(10, "Far", parents: new[] { 1 }, presence: Presence.Away) }, 1000);
            var request = r.RequestCeremony(CeremonyKind.GrandReunion, r.FamilyOf(1), 0, 0, 2000, 2 * Day, 12 * Day);
            request.VisitorIds.Add(10);
            var visit = r.PlanKinVisit(request.Id, new[] { 10 }, 2000);
            visit.Complication = true;
            r.StrandKinVisit(visit.Id, 77, visit.ArriveTick);
            // An older save has none of the new fields: they are all at their defaults, which is exactly what a missing value loads as.
            Assert.Equal(RescueStage.None, visit.Rescue);
            Assert.Equal(0, visit.QuestId);
            var reloaded = SaveSim.Reload(r);
            Assert.True(reloaded.OfferKinRescue(visit.Id, RescueComplication.Injury, visit.ArriveTick + 10));         // it is offered as a quest the next time the game looks
            reloaded.MaintainKinVisits(visit.RescueDeadlineTick + 1);
            Assert.Equal(KinVisitState.Failed, reloaded.KinVisitFor(visit.Id).State);
        }
    }
}
