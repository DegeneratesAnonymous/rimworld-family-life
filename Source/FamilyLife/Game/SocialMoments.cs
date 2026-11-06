using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    [DefOf]
    public static class WFL_MomentDefOf
    {
        public static JobDef WFL_SocialMoment;
        public static JobDef WFL_SocialListen;

        static WFL_MomentDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(WFL_MomentDefOf));
    }

    /// <summary>
    /// The relationship events in the game. The engine decides what could happen between two people and how it turns out; this finds
    /// two people who are really free and near each other (or in the middle of a shared activity), has them actually do it, and shows
    /// what came of it. A moment is not a log line: a conversation is two pawns walking up to each other and talking.
    /// </summary>
    internal static class SocialMoments
    {
        private const int SampleInterval = 600;
        private const int PawnLookCooldown = 2500;        // how often one person is considered as someone who might reach out
        private const int StartedCooldown = 7500;         // after something starts, a person is left alone for a while
        private const int ActivityCooldown = 3000;
        private const int LookChannel = 6, StartChannel = 5, ActivityChannel = 4;
        private const float MaxDistance = 14f;
        private const int MaxPairsPerSample = 16;

        private static FamilySettings S => FamilyLifeMod.Settings;
        public static bool Enabled => S == null || S.socialEvents;
        public static float Frequency => S == null ? 1f : S.eventFrequency;
        private static int NoticeLevel => S == null ? 1 : S.eventNotices;
        private static bool ChildrenSupported => ModsConfig.BiotechActive;

        private sealed class Pending
        {
            public string DefId;
            public int RecipientId;
            public int ThirdId;
            public ActivityKind Setting;
            public int Tick;
        }

        private static readonly Dictionary<int, Pending> pending = new Dictionary<int, Pending>();
        private static int lastSample;
        private static readonly SearchCursor mapCursor = new SearchCursor();
        private sealed class SharedSearch { public readonly SearchCursor Cursor = new SearchCursor(); public int Round; }
        private static readonly Dictionary<Map, SharedSearch> sharedSearch = new Dictionary<Map, SharedSearch>();
        public static void Reset() { pending.Clear(); sharedSearch.Clear(); mapCursor.Reset(); lastSample = 0; SocialContext.Reset(); }

        public static bool EventAllowed(EventDef d)
        {
            if (S == null) return true;
            switch (d.Bucket)
            {
                case EventBucket.IntraFamily: return S.eventsFamily;
                case EventBucket.Individual: return S.eventsIndividual;
                default: return S.eventsInterFamily;
            }
        }

        // ------------------------------------------------------------------ finding two people

        public static void Tick(FamilyComponent component, int now)
        {
            int interval = Math.Max(30, SampleInterval / Math.Max(1, Find.Maps.Count));
            if (!Enabled || (now >= lastSample && now - lastSample < interval)) return;
            lastSample = now;
            // A conversation that never got going (the pawn was called away on the way) must not keep them out of the next one.
            foreach (var stale in pending.Where(kv => now - kv.Value.Tick > 2 * PawnLookCooldown).Select(kv => kv.Key).ToList()) pending.Remove(stale);
            foreach (var gone in sharedSearch.Keys.Where(m => !Find.Maps.Contains(m)).ToList()) sharedSearch.Remove(gone);
            if (Find.Maps.Count == 0) return;
            var map = Find.Maps[mapCursor.Take(Find.Maps.Count)];
            if (!map.IsPlayerHome) return;
            try { SampleMap(component, map, now); }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Could not look for a social moment: " + ex.Message); }
        }

        private static void SampleMap(FamilyComponent component, Map map, int now)
        {
            var registry = component.Registry;
            var everyone = map.mapPawns.FreeColonistsSpawned;
            foreach (var p in everyone) SocialContext.Track(p, now);
            if (everyone.Count < 2) return;

            // Two people in the middle of the same activity: a game, a shared task. They are already together, so nothing needs to start.
            Pawn together;
            using (component.Performance.Measure("Shared recreation search")) together = FindShared(component, everyone, now);
            if (together != null) return;

            // Perspective Shift owns the avatar's immediate actions. Family Life can still observe moments
            // from activities they chose, but does not pull that pawn into an unsolicited conversation.
            var free = everyone.Where(p => !PerspectiveShiftBridge.IsAvatar(p) && Activities.IsFree(p) && Activities.CanSpareAttention(p) && !pending.ContainsKey(p.thingIDNumber)).InRandomOrder().ToList();
            var nearby = new NearbyIndex<Pawn>((int)MaxDistance);
            foreach (var pawn in free) nearby.Add(pawn, pawn.Position.x, pawn.Position.z);
            int tried = 0;
            foreach (var a in free)
            {
                if (tried >= MaxPairsPerSample) break;
                if (!Activities.AttemptAllowed(a.thingIDNumber, now, PawnLookCooldown, LookChannel) || !Activities.AttemptAllowed(a.thingIDNumber, now, StartedCooldown, StartChannel)) continue;
                Activities.NoteAttempt(a.thingIDNumber, now, LookChannel);
                if (Rand.Value > Mathf.Clamp01(0.6f * Frequency)) continue;

                foreach (var b in nearby.Within(a.Position.x, a.Position.z, (int)MaxDistance).Where(o => o != a && !pending.ContainsKey(o.thingIDNumber)).Take(3))
                {
                    if (tried++ >= MaxPairsPerSample) return;
                    if (!a.CanReach(b, PathEndMode.Touch, Danger.None)) continue;
                    if (TryStart(component, map, a, b, ActivityKind.None, now)) return;
                    if (TryStart(component, map, b, a, ActivityKind.None, now)) return;
                }
            }
        }

        /// <summary>Two children together are at play; otherwise there is no particular setting.</summary>
        private static ActivityKind SettingBetween(Pawn a, Pawn b)
        {
            return a.DevelopmentalStage.Juvenile() && b.DevelopmentalStage.Juvenile() ? ActivityKind.Playdate : ActivityKind.None;
        }

        private static bool WalkIsSafe(Map map, Pawn initiator, Pawn recipient, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            var weather = map.weatherManager;
            float temp = map.mapTemperature.OutdoorTemp;
            if (weather.RainRate > 0.1f || weather.SnowRate > 0.1f || temp < -5f || temp > 35f) return false;
            if (GenLocalDate.DayPercent(map) < 0.2f || GenLocalDate.DayPercent(map) > 0.8f) return false;
            return CellFinder.TryFindRandomCellNear(initiator.Position, map, 16,
                c => c.InBounds(map) && c.Standable(map) && !c.Roofed(map) && map.areaManager.Home[c] && !c.IsForbidden(initiator) && !c.IsForbidden(recipient)
                     && c.DistanceTo(initiator.Position) >= 6f
                     && initiator.CanReach(c, PathEndMode.OnCell, Danger.None) && recipient.CanReach(c, PathEndMode.OnCell, Danger.None), out cell);
        }

        /// <summary>Asks the engine whether <paramref name="initiator"/> reaching toward <paramref name="recipient"/> would lead to something, and starts it.</summary>
        private static bool TryStart(FamilyComponent component, Map map, Pawn initiator, Pawn recipient, ActivityKind setting, int now)
        {
            using var measurement = component.Performance.Measure("Conversation selection");
            var registry = component.Registry;

            // Sometimes the two go for a short walk in the home area instead of standing where they are; that is a different setting for what follows.
            var walkCell = IntVec3.Invalid;
            if (setting == ActivityKind.None)
                setting = Rand.Chance(0.2f) && WalkIsSafe(map, initiator, recipient, out walkCell) ? ActivityKind.NatureWalk : SettingBetween(initiator, recipient);
            var contexts = new List<PairContext>();
            var first = SocialContext.Build(component, initiator, recipient, setting, false);
            if (first == null) return false;
            contexts.Add(first);
            foreach (var third in SocialContext.ThirdCandidates(component, initiator, recipient, map))
            {
                var ctx = SocialContext.AddThird(component, first, third, map);
                if (ctx != null) contexts.Add(ctx);
            }

            var rng = new System.Random(Rand.Int);
            var pick = registry.PickEventAmong(contexts, EventCatalog.All, rng, ChildrenSupported, EventAllowed);
            if (pick == null) return false;
            var def = pick.Value.Key;
            var chosen = pick.Value.Value;

            if (def.ActivityOnly) return false;
            if (def.Needs.Length > 0 && !def.Needs.Contains(chosen.Setting)) return false;

            Begin(initiator, recipient, def, chosen, setting == ActivityKind.NatureWalk ? walkCell : IntVec3.Invalid);
            Activities.NoteAttempt(initiator.thingIDNumber, now, StartChannel);
            return true;
        }

        /// <summary>The played character deliberately reaches out. The actual scene and outcome still come from current relationships and circumstances.</summary>
        public static bool TryPlayerMoment(Pawn initiator,Pawn recipient)
        {
            var component=FamilyComponent.Current;
            if(component==null||!Enabled||initiator==null||recipient==null||initiator==recipient||!PerspectiveShiftBridge.CanActAs(initiator)
                ||!initiator.Spawned||!recipient.Spawned||initiator.Map==null||initiator.Map!=recipient.Map||initiator.Dead||recipient.Dead
                ||!Activities.IsFree(recipient)||recipient.CurJob?.playerForced==true||!Activities.CanSpareAttention(recipient)
                ||pending.ContainsKey(initiator.thingIDNumber)||pending.ContainsKey(recipient.thingIDNumber)
                ||!initiator.CanReach(recipient,PathEndMode.Touch,Danger.None))return false;
            return TryStart(component,initiator.Map,initiator,recipient,ActivityKind.None,Activities.Now);
        }

        private static void Begin(Pawn initiator, Pawn recipient, EventDef def, PairContext ctx, IntVec3 walkCell)
        {
            pending[initiator.thingIDNumber] = new Pending { DefId = def.Id, RecipientId = recipient.thingIDNumber, ThirdId = ctx.Third?.Id ?? 0, Setting = ctx.Setting, Tick = Activities.Now };
            var talk = JobMaker.MakeJob(WFL_MomentDefOf.WFL_SocialMoment, recipient);
            if (walkCell.IsValid) talk.targetB = walkCell;
            var listen = JobMaker.MakeJob(WFL_MomentDefOf.WFL_SocialListen, initiator);
            recipient.jobs.TryTakeOrderedJob(listen, JobTag.Misc);
            initiator.jobs.TryTakeOrderedJob(talk, JobTag.Misc);
            if (S != null && S.logEvents) Log.Message("[Wil's Family Life] moment " + def.Id + " " + initiator.LabelShort + " -> " + recipient.LabelShort + " (" + ctx.Setting + ")");
        }

        /// <summary>The conversation has happened. Finds out how it went and shows what came of it.</summary>
        public static void Complete(Pawn initiator, Pawn recipient)
        {
            if (!pending.TryGetValue(initiator.thingIDNumber, out var p) || p.RecipientId != recipient.thingIDNumber) return;
            pending.Remove(initiator.thingIDNumber);
            var component = FamilyComponent.Current;
            var def = EventCatalog.Get(p.DefId);
            if (component == null || def == null) return;
            var third = p.ThirdId != 0 ? component.TryGetPawn(p.ThirdId) : null;
            // A third-party event cannot finish after its required witness or participant has left.
            if (p.ThirdId != 0 && (third == null || !third.Spawned || third.Map != initiator.Map || third.Dead)) return;
            var ctx = SocialContext.Build(component, initiator, recipient, p.Setting, false, third);
            if (ctx == null || !component.Registry.Explain(ctx, new[] { def }, ChildrenSupported, EventAllowed)[0].Eligible) return;
            Perform(component, def, ctx);
        }

        public static void Abandon(Pawn initiator) => pending.Remove(initiator.thingIDNumber);

        // ------------------------------------------------------------------ people already doing something together


        /// <summary>
        /// Looks for two people who are, right now, doing the same thing side by side: a recreation activity. They are already
        /// together, so the moment happens in place without sending anyone anywhere.
        /// </summary>
        private static Pawn FindShared(FamilyComponent component, List<Pawn> everyone, int now)
        {
            if (S != null && !S.sharedActivityEvents) return null;
            if (everyone.Count == 0) return null;
            var groups = new Dictionary<JobDef, NearbyIndex<Pawn>>();
            foreach (var pawn in everyone)
            {
                var job = pawn.CurJob;
                if (pawn.Drafted || pawn.InMentalState || SharedActivityPolicy.Discover(job?.def.joyKind?.defName) == ActivityKind.None) continue;
                if (!groups.TryGetValue(job.def, out var group)) groups[job.def] = group = new NearbyIndex<Pawn>(6);
                group.Add(pawn, pawn.Position.x, pawn.Position.z);
            }
            if (groups.Count == 0) return null;
            if (!sharedSearch.TryGetValue(everyone[0].Map, out var search)) sharedSearch[everyone[0].Map] = search = new SharedSearch();
            int start = search.Cursor.Take(everyone.Count), checks = 0, eventAttempts = 0;
            int rotation = search.Round++ & int.MaxValue;
            for (int offset = 0; offset < everyone.Count; offset++)
            {
                int index = (start + offset) % everyone.Count;
                var a = everyone[index];
                search.Cursor.ResumeAfter(index, everyone.Count);
                var ja = a.CurJob;
                if (ja == null || a.Drafted || a.InMentalState || !groups.TryGetValue(ja.def, out var group)) continue;
                if (!Activities.AttemptAllowed(a.thingIDNumber, now, ActivityCooldown, ActivityChannel)) continue;
                int perPawn = 0;
                foreach (var b in group.Candidates(a.Position.x, a.Position.z, 6, rotation))
                {
                    if (++checks > 256) return null;
                    if (b == a || a.Position.DistanceToSquared(b.Position) > 36f) continue;
                    if (++perPawn > 8) break;
                    if (b.CurJobDef != ja.def || b.Drafted || b.InMentalState) continue;
                    if (!SharedActivityPolicy.SameTarget(ja.targetA.Thing?.thingIDNumber ?? 0, b.CurJob.targetA.Thing?.thingIDNumber ?? 0)) continue;
                    var kind = SharedActivityPolicy.Discover(ja.def.joyKind.defName);
                    Activities.NoteAttempt(a.thingIDNumber, now, ActivityChannel);
                    Activities.NoteAttempt(b.thingIDNumber, now, ActivityChannel);
                    if (Rand.Value > 0.5f * Frequency) continue;
                    if (++eventAttempts > MaxPairsPerSample) return null;
                    if (OnActivity(kind, a, b)) return a;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------ an activity reports that two people shared it

        /// <summary>
        /// Called when a shared activity has really taken place between two people (a story told, a meal eaten together, a gathering,
        /// time spent teaching or at a bedside). The moment happens as part of it, so the activity supplies the opportunity and no
        /// one is sent anywhere. Returns true when a moment took place.
        /// </summary>
        public static bool OnActivity(ActivityKind kind, Pawn a, Pawn b, string forced = null)
        {
            try { return OnActivityCore(kind, a, b, forced); }
            catch (Exception ex)
            {
                Log.Warning("[Wil's Family Life] A shared activity could not become a moment: " + ex.Message);
                return false;
            }
        }

        private static bool OnActivityCore(ActivityKind kind, Pawn a, Pawn b, string forced)
        {
            if (!Enabled || a == null || b == null || a == b || !a.Spawned || !b.Spawned ||
                a.Map == null || a.Map != b.Map || a.Dead || b.Dead) return false;
            var component = FamilyComponent.Current;
            if (component == null) return false;
            using var measurement = component.Performance.Measure("Activity moment selection");
            if (forced == null && Rand.Value > Mathf.Clamp01(0.6f * Frequency)) return false;       // most shared activities pass without any particular moment
            var registry = component.Registry;
            int now = Activities.Now;
            var rng = new System.Random(Rand.Int);

            foreach (var order in rng.NextDouble() < 0.5 ? new[] { 0, 1 } : new[] { 1, 0 })
            {
                var initiator = order == 0 ? a : b;
                var recipient = order == 0 ? b : a;
                var contexts = new List<PairContext>();
                var plain = SocialContext.Build(component, initiator, recipient, kind, true);
                if (plain == null) continue;
                if (kind == ActivityKind.Storytelling && forced != null) plain.Recipient.Bedtime = true;
                contexts.Add(plain);
                foreach (var third in SocialContext.ThirdCandidates(component, initiator, recipient, a.Map))
                {
                    var ctx = SocialContext.AddThird(component, plain, third, a.Map);
                    if (ctx != null) contexts.Add(ctx);
                }

                if (forced != null)
                {
                    var def = EventCatalog.Get(forced);
                    var fits = def == null ? null : contexts.FirstOrDefault(c => registry.Explain(c, new[] { def }, ChildrenSupported, EventAllowed)[0].Eligible);
                    if (fits == null) continue;
                    Perform(component, def, fits);
                    return true;
                }

                var pick = registry.PickEventAmong(contexts, EventCatalog.All.Where(d => !d.ActivityOnly), rng, ChildrenSupported, EventAllowed);
                if (pick == null) continue;
                Perform(component, pick.Value.Key, pick.Value.Value);
                return true;
            }
            return false;
        }

        /// <summary>Someone has just eaten. If others ate nearby a moment ago, the meal (or the picnic, outdoors) is the setting for one moment between two of them.</summary>
        public static void OnMeal(Pawn pawn, int roomKey, List<RecentMeal> ring, int now)
        {
            try { OnMealCore(pawn, roomKey, ring, now); }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] A meal could not become a moment: " + ex.Message); }
        }

        private static void OnMealCore(Pawn pawn, int roomKey, List<RecentMeal> ring, int now)
        {
            if (!Enabled || (S != null && !S.sharedActivityEvents) || pawn?.Map == null) return;
            if (Rand.Value > Mathf.Clamp01(0.35f * Frequency)) return;
            var component = FamilyComponent.Current;
            if (component == null) return;
            var near = new List<Pawn>();
            foreach (var meal in ring)
            {
                if (meal.PersonId == pawn.thingIDNumber || Mathf.Abs(now - meal.Tick) > ActivityTuning.SharedMealWindowTicks) continue;
                bool sameRoom = roomKey >= 0 && meal.RoomKey == roomKey;
                bool outdoorsToo = roomKey < 0 && meal.RoomKey < 0;
                if (!sameRoom && !outdoorsToo) continue;
                var other = component.TryGetPawn(meal.PersonId);
                if (other != null && other.Spawned && other.Map == pawn.Map && other.Position.DistanceTo(pawn.Position) <= (sameRoom ? 14f : 8f)) near.Add(other);
            }
            if (near.Count == 0) return;
            OnActivity(roomKey < 0 ? ActivityKind.Picnic : ActivityKind.Meal, pawn, near.RandomElement());
        }

        /// <summary>A gathering is over. A few pairs among those who were there have a moment, most likely those from different families who might yet be reconciled.</summary>
        public static int OnGathering(List<Pawn> people,ActivityKind kind,string tradition=null,int family=0,int secondFamily=0,
            int subject=0,int subjectB=0,IReadOnlyCollection<int> visitors=null)
        {
            if (!Enabled || people == null || people.Count < 2) return 0;
            var component = FamilyComponent.Current;
            if (component == null) return 0;
            var registry = component.Registry;
            var pairs = GatheringBudget.Pairs(people.InRandomOrder().ToList(),60,0);
            int wanted = Mathf.Clamp(Mathf.RoundToInt(3 * Frequency), 1, 5);
            int completed=0;
            foreach (var pair in pairs.InRandomOrder().OrderByDescending(p => CrossFamily(registry, p.Key, p.Value) ? 1 : 0).Take(wanted * 3))
            {
                if (wanted <= 0) break;
                if (tradition != null && (S == null || S.familyStories))
                {
                    var ctx = SocialContext.Build(component, pair.Key, pair.Value, kind, true);
                    if (ctx == null) continue;
                    ctx.TraditionId = tradition;
                    ctx.OccasionFamilyId=family;
                    ctx.OccasionSecondFamilyId=secondFamily;
                    ctx.OccasionSubjectId=subject;
                    ctx.OccasionSubjectBId=subjectB;
                    ctx.OccasionVisitorIds=visitors??System.Array.Empty<int>();
                    var catalog=CeremonyMoments.Supports(tradition)?CeremonyMoments.For(tradition):TraditionMoments.All;
                    var def = registry.PickEvent(ctx, catalog, new System.Random(Rand.Int), ChildrenSupported, d => ctx.SameFamily ? S.eventsFamily : S.eventsInterFamily);
                    if (def != null) { Perform(component, def, ctx); wanted--;completed++; continue; }
                }
                if (OnActivity(kind, pair.Key, pair.Value)){wanted--;completed++;}
            }
            return completed;
        }

        private static bool CrossFamily(FamilyRegistry registry, Pawn a, Pawn b)
        {
            var ra = registry.GetPerson(a.thingIDNumber);
            var rb = registry.GetPerson(b.thingIDNumber);
            if (ra == null || rb == null) return false;
            int la = registry.LineageIdFor(ra), lb = registry.LineageIdFor(rb);
            return la != 0 && lb != 0 && la != lb;
        }

        /// <summary>Promises that went unkept have already cost trust. The one who was let down also carries it as a memory of the other person.</summary>
        public static void NoteBrokenPromises(FamilyComponent component, List<Commitment> broken, int now)
        {
            if (broken == null) return;
            foreach (var promise in broken)
            {
                var promiser = component.TryGetPawn(promise.FromId);
                var promisee = component.TryGetPawn(promise.ToId);
                if (promiser != null && promisee != null) Activities.GainMemory(promisee, "WFL_MomentSore", promiser);
                if (promise.EventId == "B3-10")
                    component.Registry.EventLog.Add(new EventRecord { EventId = "B3-10", A = promise.FromId, B = promise.ToId, Tick = now, Tier = EventTier.Poor, Weight = EventWeight.Meaningful });
            }
        }

        // ------------------------------------------------------------------ what came of it

        private static void Perform(FamilyComponent component, EventDef def, PairContext ctx)
        {
            var registry = component.Registry;
            int now = Activities.Now;
            var rng = new System.Random(Rand.Int);
            var result = registry.ResolveEvent(def, ctx, rng);
            registry.ApplyEvent(result, now);

            var initiator = component.TryGetPawn(ctx.Initiator.Id);
            var recipient = component.TryGetPawn(ctx.Recipient.Id);
            var third = ctx.Third != null ? component.TryGetPawn(ctx.Third.Id) : null;
            if (initiator == null || recipient == null) return;

            if (!ReferenceEquals(def.Arc, Arc.Story))        // a bedtime story already gives its own memories; the event does not add a third
            {
                Feel(initiator, recipient, result.InitiatorFeeling);
                Feel(recipient, initiator, result.RecipientFeeling);
            }
            if (def.Bucket == EventBucket.IntraFamily && result.Tier >= EventTier.Good)
            {
                registry.AddFamilyTime(initiator.thingIDNumber, 600, now);
                registry.AddFamilyTime(recipient.thingIDNumber, 600, now);
            }

            if (result.XpSkill != null)
            {
                var learner = component.TryGetPawn(result.XpTo);
                var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(result.XpSkill);
                var skill = learner?.skills?.GetSkill(skillDef);
                if (skill != null && !skill.TotallyDisabled) skill.Learn(result.XpAmount);
            }

            if ((def.Effects & EventEffect.Romance) != 0 && result.Tier >= EventTier.Good && ctx.RomanticallyEligible)
                initiator.interactions.TryInteractWith(recipient, InteractionDefOf.RomanceAttempt);       // the game's own rules decide whether anything comes of it

            Announce(def, result, initiator, recipient, third);
            if (S != null && S.logEvents)
                Log.Message("[Wil's Family Life] " + def.Id + " " + result.Tier + " " + initiator.LabelShort + " / " + recipient.LabelShort + " score " + result.Score.ToString("0.00"));
        }

        private static void Feel(Pawn pawn, Pawn other, float feeling)
        {
            string thought = feeling >= 0.9f ? "WFL_MomentGreat" : (feeling >= 0.4f ? "WFL_MomentGood" : (feeling <= -0.6f ? "WFL_MomentSore" : (feeling <= -0.05f ? "WFL_MomentAwkward" : null)));
            if (thought != null) Activities.GainMemory(pawn, thought, other);
        }

        public static string Describe(EventDef def, EventTier tier, string a, string b, string c)
        {
            return ("WFL_Ev_" + def.Id.Replace('-', '_') + "_" + tier).Translate(a, b, c ?? "").ToString();
        }

        /// <summary>Quiet by default: routine moments only reach the history; a letter is kept for those that change a family.</summary>
        private static void Announce(EventDef def, EventResult r, Pawn initiator, Pawn recipient, Pawn third)
        {
            if (NoticeLevel <= 0) return;
            string text = Describe(def, r.Tier, initiator.LabelShort, recipient.LabelShort, third?.LabelShort);
            bool good = r.Tier >= EventTier.Good;
            if (r.BecameMemory || (def.Weight == EventWeight.Defining && (r.Tier == EventTier.Great || r.Tier == EventTier.Poor)))
            {
                Find.LetterStack.ReceiveLetter(("WFL_Ev_" + def.Id.Replace('-', '_') + "_Title").Translate(), text,
                    good ? LetterDefOf.PositiveEvent : LetterDefOf.NegativeEvent, new LookTargets(new[] { initiator, recipient }));
                return;
            }
            if (def.Weight == EventWeight.Ambient) return;
            bool notable = r.Tier == EventTier.Great || r.Tier == EventTier.Poor || (NoticeLevel >= 2 && r.Tier == EventTier.Good);
            if (!notable) return;
            Messages.Message(text, new LookTargets(new[] { initiator, recipient }), good ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, false);
        }
    }

    /// <summary>One person walks up to another and talks. When it has gone on long enough, the event is settled and shown.</summary>
    public class JobDriver_SocialMoment : JobDriver
    {
        private const int TalkTicks = EventTuning.ConversationTicks;
        private int talkedTicks;
        private bool greeted;

        private Pawn Other => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref talkedTicks, "talkedTicks");
            Scribe_Values.Look(ref greeted, "greeted");
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Other == null || Other.Dead || Other.Downed || Other.Drafted || Other.InMentalState);

            if (job.targetB.IsValid) yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            yield return Toils_Interpersonal.GotoInteractablePosition(TargetIndex.A);

            var talk = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = TalkTicks,
                socialMode = RandomSocialMode.Off
            };
            talk.initAction = () => pawn.pather.StopDead();
            talk.tickAction = () =>
            {
                var other = Other;
                pawn.rotationTracker.FaceTarget(other);
                talkedTicks++;
                if (!greeted && talkedTicks == 60)
                {
                    greeted = true;
                    pawn.interactions.TryInteractWith(other, InteractionDefOf.Chitchat);
                }
                if (!pawn.Position.InHorDistOf(other.Position, 6f)) EndJobWith(JobCondition.Incompletable);
            };
            talk.AddFinishAction(() =>
            {
                var other = Other;
                if (talkedTicks >= TalkTicks - 5 && other != null) SocialMoments.Complete(pawn, other);
                else SocialMoments.Abandon(pawn);
            });
            yield return talk;
        }

        public override string GetReport()
        {
            var other = Other;
            return other == null ? base.GetReport() : "WFL_Report_SocialMoment".Translate(other.LabelShort).ToString();
        }
    }

    /// <summary>The other person in a conversation. They give their attention, and follow along if the other is going for a walk.</summary>
    public class JobDriver_SocialListen : JobDriver
    {
        private const int MaxTicks = 3000;
        private const int GraceTicks = 240;
        private int started = -1;

        private Pawn Speaker => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref started, "started", -1);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Speaker == null || Speaker.Dead || Speaker.Downed || Speaker.Drafted);

            var wait = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = MaxTicks,
                socialMode = RandomSocialMode.Off
            };
            wait.initAction = () => { started = Activities.Now; pawn.pather.StopDead(); };
            wait.tickAction = () =>
            {
                var speaker = Speaker;
                int now = Activities.Now;
                if (now - started > GraceTicks && speaker.CurJobDef != WFL_MomentDefOf.WFL_SocialMoment) { EndJobWith(JobCondition.Succeeded); return; }
                if (pawn.Position.InHorDistOf(speaker.Position, 3f)) pawn.rotationTracker.FaceTarget(speaker);
                else if (!pawn.pather.Moving && pawn.IsHashIntervalTick(60)) pawn.pather.StartPath(speaker, PathEndMode.Touch);
            };
            yield return wait;
        }

        public override string GetReport()
        {
            var speaker = Speaker;
            return speaker == null ? base.GetReport() : "WFL_Report_SocialListen".Translate(speaker.LabelShort).ToString();
        }
    }
}
