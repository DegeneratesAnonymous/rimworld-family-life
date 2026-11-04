using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Turns two real pawns into the plain facts the event engine reasons about. Everything here is read from the game as it is:
    /// mood, health, skills, the social memories a pawn actually holds. If a circumstance is not really true, it is not set, and the
    /// events that need it cannot happen.
    /// </summary>
    internal static class SocialContext
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private const float WrongedAtOrBelow = -5f;          // a social memory at least this sour counts as having been treated badly
        private const int AchievementLifetime = 2 * Day;

        private sealed class Snapshot
        {
            public Dictionary<string, int> Levels = new Dictionary<string, int>();
            public string Achievement;
            public int AchievedTick;
        }

        // Not saved. After a load it simply learns the levels again, so a level gained before the load is not counted twice.
        private static readonly Dictionary<int, Snapshot> snapshots = new Dictionary<int, Snapshot>();

        /// <summary>Notices skill levels gained since the last look. Cheap; called for every colonist each time events are considered.</summary>
        public static void Track(Pawn p, int now)
        {
            var skills = p?.skills?.skills;
            if (skills == null) return;
            if (!snapshots.TryGetValue(p.thingIDNumber, out var snap))
            {
                snap = new Snapshot();
                snapshots[p.thingIDNumber] = snap;
                foreach (var s in skills) snap.Levels[s.def.defName] = s.Level;
                return;
            }
            foreach (var s in skills)
            {
                if (s.TotallyDisabled) continue;
                if (snap.Levels.TryGetValue(s.def.defName, out var before) && s.Level > before)
                {
                    snap.Achievement = s.def.defName;
                    snap.AchievedTick = now;
                }
                snap.Levels[s.def.defName] = s.Level;
            }
        }

        public static Actor ActorOf(FamilyComponent component, Pawn p, int now)
        {
            var registry = component.Registry;
            var record = registry.GetPerson(p.thingIDNumber);
            var actor = new Actor
            {
                Id = p.thingIDNumber,
                Name = p.LabelShort,
                Child = p.DevelopmentalStage.Juvenile() && !p.DevelopmentalStage.Baby(),
                AgeYears = p.ageTracker != null ? p.ageTracker.AgeBiologicalYearsFloat : 30f,
                LineageId = record != null ? registry.LineageIdFor(record) : 0,
                Profile = component.ProfileOf(p.thingIDNumber),
                Mood = p.needs?.mood != null ? p.needs.mood.CurLevel : 0.5f
            };
            if (actor.LineageId == 0 && record != null) actor.LineageId = record.PrimaryLineageId;

            if (p.skills != null)
                foreach (var s in p.skills.skills)
                    if (!s.TotallyDisabled) actor.Skills[s.def.defName] = s.Level;

            actor.Distressed = actor.Mood < 0.3f || p.InMentalState;
            actor.Ill = HealthAIUtility.ShouldSeekMedicalRest(p) || (p.health != null && p.health.HasHediffsNeedingTend());

            var memories = p.needs?.mood?.thoughts?.memories?.Memories;
            if (memories != null)
            {
                foreach (var m in memories)
                {
                    string name = m.def.defName;
                    if (name == "WFL_FamilyGrief" || name.IndexOf("Died", StringComparison.Ordinal) >= 0 || name.IndexOf("Grief", StringComparison.Ordinal) >= 0) actor.Grieving = true;
                    var social = m as Thought_MemorySocial;
                    if (social != null && social.otherPawn != null && social.OpinionOffset() <= WrongedAtOrBelow && !actor.Wronged.Contains(social.otherPawn.thingIDNumber))
                        actor.Wronged.Add(social.otherPawn.thingIDNumber);
                }
            }

            if (snapshots.TryGetValue(p.thingIDNumber, out var snap) && snap.Achievement != null && now - snap.AchievedTick < AchievementLifetime)
                actor.Achievement = snap.Achievement;

            float colonyDays = p.records != null ? p.records.GetValue(RecordDefOf.TimeAsColonistOrColonyAnimal) / (float)Day : 99f;
            actor.Newcomer = colonyDays < 5f;
            return actor;
        }

        /// <summary>Builds the circumstances for <paramref name="initiator"/> reaching toward <paramref name="recipient"/>. Null when either is unknown to the records.</summary>
        public static PairContext Build(FamilyComponent component, Pawn initiator, Pawn recipient, ActivityKind setting, bool fromActivity, Pawn third = null)
        {
            // Physical social moments must have both participants on the same active map.
            // Off-map relationships are maintained by the registry, not by face-to-face interactions.
            if (component == null || initiator == null || recipient == null ||
                !initiator.Spawned || !recipient.Spawned || initiator.Map == null ||
                initiator.Map != recipient.Map || initiator.Dead || recipient.Dead)
                return null;
            if (third != null && (!third.Spawned || third.Dead || third.Map != initiator.Map))
                return null;
            var registry = component.Registry;
            var ra = registry.GetPerson(initiator.thingIDNumber);
            var rb = registry.GetPerson(recipient.thingIDNumber);
            if (ra == null || rb == null) return null;
            int now = Activities.Now;
            var ia = ActorOf(component, initiator, now);
            var ib = ActorOf(component, recipient, now);

            var ctx = new PairContext
            {
                Registry = registry,
                Initiator = ia,
                Recipient = ib,
                Now = now,
                Setting = setting,
                FromActivity = fromActivity,
                RelationshipPace = 0.25f, // Provisional ordinary-event pace; incident shocks use their own path.
                ResourcePressure = component.Economy.Pressure(ia.LineageId) || component.Economy.Pressure(ib.LineageId),
                InitiatorBond = registry.GetBond(ia.Id, ib.Id),
                RecipientBond = registry.GetBond(ib.Id, ia.Id),
                Kind = registry.KindOf(ra, rb),
                SameFamily = ia.LineageId != 0 && ia.LineageId == ib.LineageId,
                Rift = registry.OpenRiftBetween(ia.Id, ib.Id),
                OpinionOfRecipient = component.OpinionBetween(ia.Id, ib.Id) ?? 0,
                OpinionOfInitiator = component.OpinionBetween(ib.Id, ia.Id) ?? 0,
                Broken = registry.BrokenPromise(ia.Id, ib.Id)
            };
            if (ia.LineageId != 0 && ib.LineageId != 0 && ia.LineageId != ib.LineageId) ctx.FamilyTie = registry.GetTie(ia.LineageId, ib.LineageId);

            if (!ia.Child && !ib.Child)
            {
                try { ctx.RomanticallyEligible = RelationsUtility.RomanceEligiblePair(initiator, recipient, forOpinionExplanation: false).Accepted; }
                catch (Exception) { ctx.RomanticallyEligible = false; }
            }

            if (third != null)
            {
                ctx.Third = ActorOf(component, third, now);
                ctx.ThirdAttachToRecipient = (int)(registry.GetBond(ctx.Third.Id, ib.Id)?.Attachment ?? -1f);
                ctx.ThirdAttachToInitiator = (int)(registry.GetBond(ctx.Third.Id, ia.Id)?.Attachment ?? -1f);
                ctx.ThirdOpinionOfInitiator = component.OpinionBetween(ctx.Third.Id, ia.Id) ?? 0;
            }
            return ctx;
        }

        public static void Reset() => snapshots.Clear();

        /// <summary>Read the pair once, then add only the facts belonging to each third person.</summary>
        public static PairContext AddThird(FamilyComponent component, PairContext pair, Pawn third, Map map)
        {
            if (third == null || !third.Spawned || third.Dead || third.Map != map || third.thingIDNumber == pair.Initiator.Id || third.thingIDNumber == pair.Recipient.Id) return null;
            var ctx = pair.Copy();
            ctx.Third = ActorOf(component, third, pair.Now);
            ctx.ThirdAttachToRecipient = (int)(component.Registry.GetBond(ctx.Third.Id, pair.Recipient.Id)?.Attachment ?? -1f);
            ctx.ThirdAttachToInitiator = (int)(component.Registry.GetBond(ctx.Third.Id, pair.Initiator.Id)?.Attachment ?? -1f);
            ctx.ThirdOpinionOfInitiator = component.OpinionBetween(ctx.Third.Id, pair.Initiator.Id) ?? 0;
            return ctx;
        }

        /// <summary>People who might be the third party in a moment between these two: whoever wronged either of them, and their children and parents.</summary>
        public static List<Pawn> ThirdCandidates(FamilyComponent component, Pawn initiator, Pawn recipient, Map map)
        {
            var registry = component.Registry;
            var ids = new List<int>();
            void AddRange(IEnumerable<int> some) { foreach (var id in some) if (id != initiator.thingIDNumber && id != recipient.thingIDNumber && !ids.Contains(id)) ids.Add(id); }

            var recipientActor = recipient.needs?.mood?.thoughts?.memories?.Memories;
            if (recipientActor != null)
                AddRange(recipientActor.OfType<Thought_MemorySocial>().Where(m => m.otherPawn != null && m.OpinionOffset() <= WrongedAtOrBelow).Select(m => m.otherPawn.thingIDNumber));
            AddRange(registry.ChildrenOf(initiator.thingIDNumber));
            AddRange(registry.ChildrenOf(recipient.thingIDNumber));
            var rb = registry.GetPerson(recipient.thingIDNumber);
            if (rb != null) AddRange(rb.ParentIds);
            var ra = registry.GetPerson(initiator.thingIDNumber);
            if (ra != null) AddRange(ra.ParentIds);

            var result = new List<Pawn>();
            foreach (var id in ids)
            {
                var pawn = component.TryGetPawn(id);
                if (pawn != null && pawn.Spawned && pawn.Map == map && !pawn.Dead && pawn.RaceProps.Humanlike) result.Add(pawn);
                if (result.Count >= 5) break;
            }
            // Someone else nearby who is from another family may act as the go-between.
            foreach (var other in map.mapPawns.FreeColonistsSpawned.InRandomOrder().Take(6))
            {
                if (result.Count >= 6) break;
                if (other == initiator || other == recipient || result.Contains(other) || other.DevelopmentalStage.Juvenile()) continue;
                result.Add(other);
            }
            return result;
        }
    }
}
