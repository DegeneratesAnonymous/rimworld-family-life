using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Observes family facts and updates relationships hourly, and sooner after debounced relationship/death hooks.
    /// Unchanged facts skip structural reconciliation; independent maintenance is staggered.
    /// </summary>
    public class FamilyComponent : GameComponent
    {
        private const int SyncInterval = 2500;     // about an in-game hour
        private const int DirtyDelay = 60;         // coalesce bursts of changes (a birth adds several relations)

        public FamilyRegistry Registry = new FamilyRegistry();
        internal readonly FamilyEconomy Economy;
        internal readonly PerformanceProbe Performance = new PerformanceProbe();
        internal int Revision { get; private set; }
        private readonly TickGate maintenance = new TickGate(625);
        private int maintenancePhase;
        private readonly ObservationCache observationCache = new ObservationCache();
        private readonly TickGate fullReconcile = new TickGate(15000);
        private readonly Dictionary<int, int[]> zoneMembers = new Dictionary<int, int[]>();
        private int zoneMemberRevision = -1;
        internal int[] ZoneMemberIds(Lineage lineage)
        {
            if (zoneMemberRevision != Revision) { zoneMembers.Clear(); zoneMemberRevision = Revision; }
            if (!zoneMembers.TryGetValue(lineage.Id, out var ids))
                zoneMembers[lineage.Id] = ids = Registry.MembersOf(lineage.Id).Concat(Registry.AffiliatesOf(lineage.Id)).Select(p => p.Id).Distinct().ToArray();
            return ids;
        }
        private static readonly string[] MaintenanceNames = { "Maintain ties", "Maintain promises", "Maintain gatherings", "Maintain zones" };

        /// <summary>Who finished a meal in the last few minutes; used to notice relatives eating together. Not saved.</summary>
        public readonly List<RecentMeal> RecentMeals = new List<RecentMeal>();

        private readonly Dictionary<int, Pawn> pawnIndex = new Dictionary<int, Pawn>();
        private readonly Dictionary<int, float[]> profiles = new Dictionary<int, float[]>();
        private bool dirty;
        private int dirtySince;
        private int lastSync = -SyncInterval;
        private int retryAfter;

        public FamilyComponent(Game game) { Economy = new FamilyEconomy(this); }

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

        public bool PerspectiveInitialized;
        public int PerspectivePawnId;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref PerspectivePawnId,"perspectivePawn");
            Scribe_Values.Look(ref PerspectiveInitialized,"perspectiveInitialized");
            Scribe_Deep.Look(ref Registry, "registry");
            if (Registry == null) Registry = new FamilyRegistry();     // a game that did not have the mod yet
        }

        public override void GameComponentTick()
        {
            using var measurement = Performance.Measure("Family tick");
            int now = Find.TickManager.TicksGame;
            if (now < retryAfter) return;
            if ((dirty && now - dirtySince >= DirtyDelay) || now - lastSync >= SyncInterval) Sync(now, false);
            // Keep event effects atomic; independent housekeeping runs in four staggered phases.
            if (now != lastSync && maintenance.Take(now)) Maintain(now);
            using (Performance.Measure("Social sampling")) SocialMoments.Tick(this, now);
            Economy.Tick(now);
            HouseAgency.Tick(this, now);
        }

        /// <summary>Reconciles with vanilla right now. Safe to call at any time.</summary>
        public void Sync(int now, bool forceReconcile = true)
        {
            using var measurement = Performance.Measure("Core sync");
            dirty = false;
            lastSync = now;
            try
            {
                List<PersonObservation> observations;
                using (Performance.Measure("Observe pawns")) observations = PawnObserver.Observe(pawnIndex, Registry.Persons.Where(p => p.Presence == Presence.Colony).Select(p => p.Id).ToList(), profiles);
                List<FamilyEvent> events;
                bool factsChanged;
                using (Performance.Measure("Compare observations")) factsChanged = observationCache.Update(observations);
                if (forceReconcile || factsChanged || fullReconcile.Take(now))
                {
                    using (Performance.Measure("Reconcile records")) events = Registry.Apply(observations, now);
                    fullReconcile.Invalidate(); fullReconcile.Take(now);
                    Revision++;
                }
                else events = new List<FamilyEvent>();
                List<Grief> griefs;
                using (Performance.Measure("Update bonds")) griefs = Registry.UpdateBonds(OpinionOf, now, events, ProfileOf);
                var s = FamilyLifeMod.Settings;
                Registry.UpdateCulture(ProfileOf, now);
                if(s==null||s.ceremonies)Registry.UpdateOrganicCeremonies(now,s==null||s.visitingKin);
                events.AddRange(Registry.UpdateMilestones(now));
                if (s == null || s.traditions) events.AddRange(Registry.UpdateTraditions(now));
                if (s == null || s.conflicts) events.AddRange(Registry.UpdateRifts(now, s == null || s.rivalry, events));
                events.AddRange(Registry.UpdateBondMilestones(now));
                Registry.RecordMemories(events, griefs, now);
                EventEffects.Apply(this, events, griefs, now);
                if (forceReconcile) FamilyZones.Apply(this);
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
                observationCache.Clear();
                fullReconcile.Invalidate();
                retryAfter = now + SyncInterval;
                Log.Error("[Wil's Family Life] Family sweep failed; will retry later. " + ex);
            }
        }

        private void Maintain(int now)
        {
            int phase = maintenancePhase;
            maintenancePhase = (maintenancePhase + 1) % 4;
            using var measurement = Performance.Measure(MaintenanceNames[phase]);
            try
            {
                switch (phase)
                {
                    case 0:
                        Registry.PruneTimeRecords(now);
                        Registry.MaintainAgency(now);
                        Registry.UpdateTies(now);
                        var s = FamilyLifeMod.Settings;
                        if (s == null || (s.traditions && s.eventsInterFamily)) Registry.RequestReconciliations(now);
                        break;
                    case 1: SocialMoments.NoteBrokenPromises(this, Registry.UpdateCommitments(now), now); break;
                    case 2: FamilyGatherings.Process(this, now); break;
                    case 3: FamilyZones.Apply(this); ZoneBeds.Upkeep(this); ZoneBeds.GrowOut(this); break;
                }
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Maintenance phase " + phase + " failed; will retry next cycle: " + ex); }
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

        public void FamilyMembershipChanged()
        { Revision++; FamilyZones.Apply(this); }

        public void MovePerson(int personId, int targetHouseholdId)
        {
            Registry.MoveToHousehold(personId, targetHouseholdId, Find.TickManager.TicksGame);
            Revision++;
        }
    }
}
