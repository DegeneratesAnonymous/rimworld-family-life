using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The single entry point of the mod's simulation. No per-pawn tick work: it reconciles on a timer, and sooner
    /// (debounced) when a relationship hook or a death reports a change.
    /// </summary>
    public class FamilyComponent : GameComponent
    {
        private const int SyncInterval = 2500;     // about an in-game hour
        private const int DirtyDelay = 60;         // coalesce bursts of changes (a birth adds several relations)

        public FamilyRegistry Registry = new FamilyRegistry();

        /// <summary>Who finished a meal in the last few minutes; used to notice relatives eating together. Not saved.</summary>
        public readonly List<RecentMeal> RecentMeals = new List<RecentMeal>();

        private readonly Dictionary<int, Pawn> pawnIndex = new Dictionary<int, Pawn>();
        private readonly Dictionary<int, float[]> profiles = new Dictionary<int, float[]>();
        private bool dirty;
        private int dirtySince;
        private int lastSync = -SyncInterval;
        private int retryAfter;

        public FamilyComponent(Game game) { }

        private static Game cachedGame;
        private static FamilyComponent cachedComponent;

        /// <summary>The component of the running game. Hooks ask for it constantly, so it is looked up once per game.</summary>
        public static FamilyComponent Current
        {
            get
            {
                if (Verse.Current.ProgramState != ProgramState.Playing) return null;
                var game = Verse.Current.Game;
                if (game == null) return null;
                if (!ReferenceEquals(game, cachedGame))
                {
                    cachedGame = game;
                    cachedComponent = game.GetComponent<FamilyComponent>();
                }
                return cachedComponent;
            }
        }

        public static void NotifyChanged()
        {
            Current?.MarkDirty();
        }

        public void MarkDirty()
        {
            if (dirty) return;
            dirty = true;
            dirtySince = Find.TickManager.TicksGame;
        }

        public Pawn TryGetPawn(int id)
        {
            return pawnIndex.TryGetValue(id, out var p) && p != null && !p.Discarded ? p : null;
        }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref Registry, "registry");
            if (Registry == null) Registry = new FamilyRegistry();     // a game that did not have the mod yet
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now < retryAfter) return;
            if ((dirty && now - dirtySince >= DirtyDelay) || now - lastSync >= SyncInterval) Sync(now);
        }

        /// <summary>Reconciles with vanilla right now. Safe to call at any time.</summary>
        public void Sync(int now)
        {
            dirty = false;
            lastSync = now;
            try
            {
                var observations = PawnObserver.Observe(pawnIndex, Registry.Persons.Where(p => p.Presence == Presence.Colony).Select(p => p.Id).ToList(), profiles);
                var events = Registry.Apply(observations, now);
                var griefs = Registry.UpdateBonds(OpinionOf, now, events, ProfileOf);
                var s = FamilyLifeMod.Settings;
                Registry.UpdateCulture(ProfileOf, now);
                events.AddRange(Registry.UpdateMilestones(now));
                if (s == null || s.traditions) events.AddRange(Registry.UpdateTraditions(now));
                if (s == null || s.conflicts) events.AddRange(Registry.UpdateRifts(now, s == null || s.rivalry, events));
                events.AddRange(Registry.UpdateBondMilestones(now));
                Registry.RecordMemories(events, griefs, now);
                EventEffects.Apply(this, events, griefs, now);
                Registry.PruneTimeRecords(now);
                FamilyGatherings.Process(this, now);
                FamilyZones.Apply(this);
                if (FamilyLifeMod.Settings == null || FamilyLifeMod.Settings.griefAmplification)
                    GriefApplier.Apply(griefs, TryGetPawn);
                if (FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.logEvents)
                    foreach (var e in events)
                        if (e.Kind != FamilyEventKind.PersonRecorded)
                            Log.Message("[Wil's Family Life] " + e.Kind + " a=" + e.A + " b=" + e.B + " @" + e.Tick);
            }
            catch (Exception ex)
            {
                // Never let a bad sweep run every tick. Report once, then try again after a normal interval.
                retryAfter = now + SyncInterval;
                Log.Error("[Wil's Family Life] Family sweep failed; will retry later. " + ex);
            }
        }

        /// <summary>Character profile of a colony member from the last sweep (traits only), or null when unknown.</summary>
        public float[] ProfileOf(int personId)
        {
            return profiles.TryGetValue(personId, out var p) ? p : null;
        }

        public int? OpinionBetween(int fromId, int toId) => OpinionOf(fromId, toId);

        /// <summary>The game's own opinion of one pawn about another. Bonds read it; they never write it.</summary>
        private struct CachedOpinion
        {
            public int Value;
            public bool Known;
            public int Expires;
        }

        private readonly Dictionary<long, CachedOpinion> opinionCache = new Dictionary<long, CachedOpinion>();
        private const int OpinionLifetime = 15000;        // opinions move slowly, and bonds only follow them gradually
        private const int OpinionStagger = 5000;          // so that the cache does not all expire in the same sweep

        private void PruneOpinions(int now)
        {
            var expired = opinionCache.Where(kv => kv.Value.Expires <= now).Select(kv => kv.Key).ToList();
            foreach (var key in expired) opinionCache.Remove(key);
            if (opinionCache.Count > 6000) opinionCache.Clear();
        }

        private int? OpinionOf(int fromId, int toId)
        {
            int now = Find.TickManager.TicksGame;
            long key = ((long)fromId << 32) | (uint)toId;
            if (opinionCache.TryGetValue(key, out var cached) && cached.Expires > now && cached.Expires - now <= OpinionLifetime + OpinionStagger)
                return cached.Known ? cached.Value : (int?)null;

            int? value = null;
            if (pawnIndex.TryGetValue(fromId, out var from) && pawnIndex.TryGetValue(toId, out var to)
                && from != null && to != null && !from.Dead && !to.Dead && from.relations != null)
            {
                try { value = from.relations.OpinionOf(to); }
                catch (Exception) { value = null; }
            }
            if (opinionCache.Count > 6000) PruneOpinions(now);
            int stagger = (int)(((uint)(fromId * 31 + toId * 17)) % OpinionStagger);
            opinionCache[key] = new CachedOpinion { Value = value ?? 0, Known = value.HasValue, Expires = now + OpinionLifetime + stagger };
            return value;
        }

        public void MovePerson(int personId, int targetHouseholdId)
        {
            Registry.MoveToHousehold(personId, targetHouseholdId, Find.TickManager.TicksGame);
        }
    }
}
