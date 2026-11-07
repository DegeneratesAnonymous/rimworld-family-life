# Performance at accelerated simulation speeds

The goal is to reduce simulation work without disabling family systems. These changes are implemented on the PR branch; live TPS gains have not been measured. All game access remains on the main thread.

## What changed

| Subsystem | Previous work | New work |
|---|---|---|
| Structural reconciliation | Full family/household reconciliation every 2,500 ticks | Observe and compare facts every 2,500 ticks; reconcile when facts change, with a 15,000-tick full fallback. Manual/debug resync still forces reconciliation. |
| Bonds | Search the whole bond list for each colonist | Group existing bonds by holder once per update, preserving priority and ordering. |
| Zone work | Rebuild construction/repair/roof candidates every 30 ticks | Map-local pending-work cache, 300-tick fallback refresh, invalidation on relevant spawn/despawn/damage/roof/zone changes. Invalidations coalesce with a 60-tick minimum refresh interval. |
| Active zone visits | Update every 30 ticks | Still every 30 ticks, independently of new-work discovery. Enforce the global three-visit cap before searching. |
| New zone visits | Potentially inspect every work site and all workers | Rotate through one map per 30-tick pass and at most four cached sites; reuse available workers within that pass. Revalidate map, position, zone, target existence and work need before use. |
| Door openers | Reachability-check every eligible relative before choosing the nearest | Rank by the same distance/adult preference first, then stop at the first reachable candidate. |
| Social sampling | Sample every home map together every 600 ticks | Rotate maps, targeting approximately one sample per map every 600 ticks; a 30-tick minimum interval applies with very many maps. |
| Shared recreation | Scan pawn pairs across the colony | Index by job and nearby spatial cells. Process at most 256 raw neighborhood candidates, eight nearby partners per initiator, and 16 expensive event attempts per sample, rotating both initiators and candidate order. Rejected distances also count against the global budget. |
| Conversations | Repeated whole-list distance searches | A spatial index of free pawns and a strict 16 attempted-pair cap per map sample. |
| Event contexts | Re-read pair facts for every possible third person | Build pair facts once and copy them while adding each third person's facts. Copies live only for that selection. |
| Event eligibility | Evaluate every definition in every context and recompute daily counts/motive | Filter pair/third-person definitions before evaluation; compute daily counts once per context and reuse motive for weighting. |
| Housekeeping | Ties, commitments, gatherings and zone application coincide with the core sweep | Four phases, 625 ticks apart, each normally once per 2,500 ticks. Structural/life-event consequences remain atomic. |
| Family UI | Rebuild layout on a 120-tick expiry; rebuild family list every draw | Cache layout and listed families by registry/reconciliation revision and selection. Household moves invalidate the revision. Live relationship/mood values are still drawn from current records. |
| Family-zone display | Scan zone visibility every 30 ticks | Update when toggled, registered, or resetting/loading a game. |
| House ambitions | No situation/goal layer | At most one house is considered per roughly hourly gate. Each house receives at most one full aggregate assessment per in-game day; results are saved. Skill coverage is linear in current adult members, cohesion samples at most 20 members, and inter-house comparison uses one compact record per active house. No map-cell grid or pawn-pair matrix is retained. Meaningful registry changes invalidate snapshots. |
| Marriage alliances | No alliance planner | Candidate discovery runs only during a relevant house's cached daily assessment, examines at most 48 same-map pairs, and retains at most one active opportunity per involved house. Consent adds one constant-time lookup while building an already-sampled social context; negotiations use ordinary interruptible jobs rather than a new tick scanner. |

In an unchanged map, the fallback work refresh rate is one tenth of its old rate. This is a count of discovery passes, not a claim of 90% less total CPU time. New work can be discovered later than before; stale tasks are never trusted merely because they are cached. Roof *designations* or mod edits that bypass the hooks are caught by the fallback refresh.

The observation cache compares IDs, names, birth dates, presence, juvenile status, relationships, marriage dates and top-skill facts. It owns copies of relation lists before reconciliation normalizes them. Ordinary AgeTicks changes do not invalidate it: that field only classifies newly recorded people, and new IDs always force reconciliation. Failed syncs clear the cache; loads rebuild it. Pawn observations, trait profiles, bond dynamics, culture and milestones still update hourly. A full fallback also repairs unexpected model changes.

The new caches are transient, scoped to a game/map/registry, and cleared or rebuilt on load. There is no save-format change. Existing unscoped family-area identity and player-restriction ownership problems remain tracked in issue #2; a map-local work cache does not resolve that policy issue.

## Measurement already available

The existing Release benchmark uses 120 colonists, 456 total records, 960 bonds and 24 households. It measures domain operations only, with a warm-up and repeated calls; it excludes pawn observation, pathfinding, job drivers, zones, rendering and the new invalidation hooks.

Three local runs of the timed domain operations gave:

| Measurement | Before (`eacd9ee`) | After this pass |
|---|---:|---:|
| Sum of the existing full-sweep timings | 8.60–10.95 ms | 7.54–8.65 ms |
| Raw structural Apply, unchanged facts | 3.62–4.68 ms | 3.64–4.15 ms |
| Observation comparison that can skip Apply | Not present | 0.19–0.26 ms |

These are noisy local microbenchmarks, not an in-game FPS/TPS comparison. The full-sweep sum retains its original definition: it includes raw Apply and does not substitute the observation-cache fast path. Do not infer an overall TPS percentage from these numbers.

Run the benchmark with:

```
dotnet test Source/FamilyLife.Tests/FamilyLife.Tests.csproj -c Release --filter FullyQualifiedName~PerformanceTests --logger "console;verbosity=detailed"
```

## Capturing a real game at 3×

Developer actions under **Wil's Family Life**:

1. **Start performance capture** clears previous measurements and enables recording.
2. Run unpaused at 3× for a consistent interval, such as two minutes.
3. **Stop and report performance capture** stops recording and writes a report to the game log.

The report includes elapsed ticks/wall seconds, observed TPS, calls, total/mean/max elapsed milliseconds per named section, and allocated bytes on RimWorld 1.7. Allocation measurement is unavailable on 1.6. Recording is off by default; a unit test verifies disabled scopes allocate zero bytes across 10,000 calls. Enabled recording has its own measurement overhead.

Timing and allocation rows are inclusive: **do not add nested rows**. For average instrumented simulation cost per tick, use `(Family tick total + Zone tick total) / elapsed game ticks`. Inspect the Families window separately. A maximum is the slowest observed invocation, not a percentile. Observed TPS includes wall time spent paused or in menus, so keep the test interval unpaused.

For a controlled comparison, use a copy of the same save, identical mods/settings and the same TPS monitor for the before/after builds. Warm up first and repeat runs. Compare:

- Twelve-person Three Families start, a 50+ pawn colony, and a larger established colony.
- No family zones, extensive family zones, and construction/repair/roof-heavy work.
- One home map and several maps.
- Families window closed and open.
- Average TPS, average section cost, worst observed calls, allocations, and visible hitching.

If both builds already sustain the speed cap, compare CPU headroom and spikes rather than expecting higher displayed TPS.

## Regression checklist

- New construction, damage, completed/removed frames and roof work refresh the correct map's cache; deleted/moved/repaired targets do not arrange stale visits.
- Newly painted or reassigned zones wake discovery; heavy editing coalesces refreshes. Workers still gain and release access correctly.
- Three active visits never become four. Expiration, refusal, drafting, interruption and reload still end/restore visits correctly.
- Dense crowds do not permanently favor the same initiating pawns or nearby candidates. Shared recreation remains within six cells and uses the same job.
- Adults/children, stories, meals, teaching, reconciliation and forced/debug events still use the correct opportunities and participants.
- Births, deaths, relationships, name/skill changes, becoming adult, departures and returns still reconcile promptly. Debug resync always performs a full reconciliation.
- House ambitions remain stable for ordinary changes, severe hunger/danger can interrupt them, and a large number of houses does not cause all snapshots to run in one tick. Housing follows actual bed/zone assignments and Better Beds locks; skill-gap mentoring does not override a mentor who lacks the needed advantage.
- Promises and gathering requests survive saving between maintenance phases and apply consequences once. No pending life-event queue is left unsaved.
- UI trees update on reconciliation and household moves; bars and histories remain live. Loading a different game cannot reuse an old registry's layout.
- Check performance with event logging and capture off as well as with capture on.

The helper tests cover tick gates, invalidation/debounce, clock rewind, independent schedules, rotating cursors, exact spatial-radius queries, dense candidate rotation, raw-candidate budget accounting, context-copy isolation, observation field coverage/ownership, and diagnostic restart/disabled allocation behavior. In-game behavior and TPS require the live checks above.
