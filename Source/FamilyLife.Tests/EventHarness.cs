using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;

namespace FamilyLife.Tests
{
    /// <summary>
    /// A small world with two families and a stranger, and a generator of contexts for it. The generator sets facts at random, so a
    /// sweep over it finds out which events can really happen and which cannot because something they need can never be true.
    /// </summary>
    internal sealed class EventHarness
    {
        public const int Day = ActivityTuning.TicksPerDay;
        public readonly FamilyRegistry Registry = new FamilyRegistry();
        public readonly int Now = 10 * Day;
        public readonly int Smith, Jones;
        public readonly int[] Cast = { 1, 2, 10, 11, 12, 21, 22, 30, 31, 40 };
        private static readonly int[] Kids = { 10, 11, 12, 30, 31 };
        public readonly Random Rng;

        public EventHarness(int seed = 7)
        {
            Rng = new Random(seed);
            var obs = new List<PersonObservation>
            {
                T.P(1, "Mum", "Smith", spouses: new[] { 2 }), T.P(2, "Dad", "Smith", spouses: new[] { 1 }),
                T.Kid(10, new[] { 1, 2 }), T.Kid(11, new[] { 1, 2 }), T.Kid(12, new[] { 1, 2 }),
                T.P(21, "Jo", "Jones", spouses: new[] { 22 }), T.P(22, "Al", "Jones", spouses: new[] { 21 }),
                T.Kid(30, new[] { 21, 22 }, "Jones"), T.Kid(31, new[] { 21, 22 }, "Jones"),
                T.P(40, "Lee", "Lee")
            };
            Registry.Apply(obs, Now);
            Registry.UpdateBonds((a, b) => 0, Now, null);
            Smith = Registry.LineageIdFor(Registry.GetPerson(1));
            Jones = Registry.LineageIdFor(Registry.GetPerson(21));
            Registry.Remember(Smith, MemoryKind.Birth, Now, 10, 1, 1.5f);
            Registry.Remember(Smith, MemoryKind.Rupture, Now - 20 * Day, 1, 21, 1.5f);
            foreach (var pair in new[] { new[] { 1, 2 }, new[] { 10, 11 }, new[] { 1, 21 }, new[] { 10, 30 } })
                Registry.OpenRift(pair[0], pair[1], RiftCause.Tension, 40f, Now - 3 * Day);
        }

        private Actor MakeActor(int id)
        {
            var rec = Registry.GetPerson(id);
            var a = new Actor
            {
                Id = id, Name = rec.Name, Child = rec.Juvenile, AgeYears = rec.Juvenile ? 4 + Rng.Next(9) : 20 + Rng.Next(40),
                LineageId = Registry.LineageIdFor(rec) != 0 ? Registry.LineageIdFor(rec) : rec.PrimaryLineageId,
                Mood = (float)Rng.NextDouble(),
                Distressed = Rng.NextDouble() < 0.5, Grieving = Rng.NextDouble() < 0.2, Ill = Rng.NextDouble() < 0.3,
                Newcomer = Rng.NextDouble() < 0.3, Bedtime = rec.Juvenile && Rng.NextDouble() < 0.5,
                Achievement = Rng.NextDouble() < 0.5 ? "Artistic" : null,
                Profile = Enumerable.Range(0, DimInfo.Count).Select(_ => (float)(Rng.NextDouble() * 3 - 1.5)).ToArray()
            };
            foreach (var skill in new[] { "Social", "Intellectual", "Artistic", "Cooking", "Crafting", "Melee" })
                a.Skills[skill] = Rng.Next(0, 16);
            foreach (var other in Cast)
                if (other != id && Rng.NextDouble() < 0.25) a.Wronged.Add(other);
            return a;
        }

        private Bond MakeBond(int from, int to) => new Bond
        {
            From = from, To = to, Attachment = Rng.Next(0, 100), Trust = Rng.Next(0, 100), Tension = Rng.Next(0, 80), Familiarity = Rng.Next(0, 100),
            LastQualityTick = Rng.NextDouble() < 0.5 ? 0 : Now - Rng.Next(0, 400) * BondTuning.TicksPerHour
        };

        private void Reshuffle()
        {
            foreach (var bond in Registry.Bonds) bond.QualityRecent = (float)(Rng.NextDouble() * (Rng.NextDouble() < 0.5 ? 1 : 8));
            Registry.EventLog.Clear();
            foreach (var id in new[] { "B2-02", "C2-03", "B2-08", "B3-01", "C3-01", "C3-07" })
                for (int i = 0; i < Rng.Next(0, 4); i++)
                {
                    int a = Cast[Rng.Next(Cast.Length)], b = Cast[Rng.Next(Cast.Length)];
                    if (a != b) Registry.EventLog.Add(new EventRecord { EventId = id, A = a, B = b, Tick = Now - Rng.Next(0, 6 * Day), Tier = (EventTier)Rng.Next(0, 4), Weight = EventWeight.Meaningful });
                }
            var kids = new[] { 10, 11, 12 };
            for (int i = 0; i < 6; i++)
                Registry.EventLog.Add(new EventRecord { EventId = "C3-01", A = kids[Rng.Next(3)], B = new[] { 30, 31 }[Rng.Next(2)], Tick = Now - Rng.Next(0, 10 * Day), Tier = EventTier.Good, Weight = EventWeight.Meaningful });
            Registry.SetTie(Smith, Jones, Rng.Next(0, 90), Rng.Next(0, 90), Rng.Next(0, 90), Rng.Next(0, 8), Now - Rng.Next(0, 5) * Day);
            Registry.GetTie(Smith, Jones).GrievanceTick = Rng.NextDouble() < 0.5 ? 0 : Now - Rng.Next(0, 20) * Day;
        }

        public PairContext RandomContext()
        {
            Reshuffle();
            int a = Cast[Rng.Next(Cast.Length)];
            int b = Cast[Rng.Next(Cast.Length)];
            while (b == a) b = Cast[Rng.Next(Cast.Length)];
            int t = Rng.NextDouble() < 0.6 ? Cast[Rng.Next(Cast.Length)] : 0;
            if (t == a || t == b) t = 0;

            var ra = Registry.GetPerson(a);
            var rb = Registry.GetPerson(b);
            var ia = MakeActor(a);
            var ib = MakeActor(b);
            var ctx = new PairContext
            {
                Registry = Registry, Initiator = ia, Recipient = ib, Now = Now,
                InitiatorBond = MakeBond(a, b), RecipientBond = MakeBond(b, a),
                Kind = Registry.KindOf(ra, rb),
                SameFamily = ia.LineageId != 0 && ia.LineageId == ib.LineageId,
                Rift = Rng.NextDouble() < 0.7 ? Registry.OpenRiftBetween(a, b) : null,
                Setting = (ActivityKind)Rng.Next(0, 13),
                FromActivity = Rng.NextDouble() < 0.5,
                OpinionOfRecipient = Rng.Next(-60, 60), OpinionOfInitiator = Rng.Next(-60, 60),
                RomanticallyEligible = Rng.NextDouble() < 0.4,
                ThirdAttachToRecipient = Rng.Next(0, 100), ThirdAttachToInitiator = Rng.Next(0, 100), ThirdOpinionOfInitiator = Rng.Next(-30, 60),
                Broken = Rng.NextDouble() < 0.5 ? new Commitment { Id = 999, FromId = a, ToId = b, State = 2 } : null
            };
            if (t != 0) ctx.Third = MakeActor(t);
            if (ia.LineageId != 0 && ib.LineageId != 0 && ia.LineageId != ib.LineageId && Rng.NextDouble() < 0.9) ctx.FamilyTie = Registry.GetTie(ia.LineageId, ib.LineageId);
            return ctx;
        }

        /// <summary>The ids of children in the cast.</summary>
        public static bool IsKid(int id) => Kids.Contains(id);
    }
}
