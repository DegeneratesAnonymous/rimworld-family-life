# Changelog

## Unreleased — family stories and supplies

- Add 48 tradition episodes with independent directed bond outcomes and real-context eligibility.
- Record witnessed harm, death, theft, refusals and restitution; share news through physical conversations.
- Add household supplies, ownership-preserving stack handling, collection, boundary negotiation, debts and desperate theft.
- Add adult revenge recruitment and social fights; optional lethal melee plots default off.
- Make family areas preferred homes with map-scoped identity and conservative legacy restriction migration.
- Add incident/plot/supply history, forgiveness controls, settings and bounded story planning.
- Domain tests and offline builds do not replace the live acceptance pass in docs/FAMILY-STORIES.md.

## Unreleased - performance at accelerated speeds
- Cache family-zone work per map, invalidate on relevant changes with debounce, retain a periodic fallback, and revalidate targets. Active visits stay responsive; discovery rotates maps/sites and enforces the three-visit limit.
- Use spatial social searches with rotating, bounded shared-activity candidates; reuse pair contexts and eligibility calculations.
- Compare observed facts before structural reconciliation, with a full six-hour fallback and forced manual resync. Continue hourly observation and relationship simulation.
- Group bond lookups once per sweep, stagger independent maintenance, cache family-zone member IDs and UI layouts/lists, and rank door openers before path checks.
- Add opt-in timing/allocation diagnostics, regression and mutation checks, and a documented 3× profiling procedure. No live TPS gain is claimed.

## Unreleased - grounded relationship moments
- Face-to-face moments require living, spawned participants on the same map; a pending third-person event cancels when its participant leaves.
- Ordinary adjacent labor no longer masquerades as a community aid day. Deliberate aid still needs a request and beneficiary mechanic.
- Two-person events are no longer multiplied by the number of nearby witnesses; duplicate contexts do not add selection weight.
- Meaningful moments respect both participants' daily limit, including during activities; ambient moments remain available. Pending conversations recheck current eligibility.
- Lessons follow their event's teacher and learner roles and select usable skills. Cross-family apprenticeships retain both their teaching and family-standing effects.
- Added regressions and event mutation checks; in-game validation is still required.

## 0.13.4 - find out why pawns stay in the zone
- New debug action 'Why is the selected pawn staying in their family zone?': prints the pawn's family, restriction, schedule, current job and work priorities, runs the work-time check once and prints the restriction afterwards.

## 0.13.3 - family zones no longer stop work
- Pawns restricted to a small family zone could only work inside it, so they stood around. The restriction is now lifted during work and 'anything' hours and put back for sleep and recreation (checked every couple of seconds). A setting turns this off. Only people holding the family's area (or none) are touched.
- Side effect: because workers are free during work hours, other families' workers also walk into a zone then, so the door/knock only matters outside work hours. Not yet verified in game.

## 0.13.2 - beds inside family zones
- A bed that is even partly inside a family zone now has all of it, and the cells beside it where a sleeper stands, added to the zone (or to the family's area if another zone owns the cell). A bed with a corner outside the area could not be claimed or walked to. Checked every sync, so zones painted earlier are repaired.
- New debug action 'Why can the selected pawn not use a bed?' lists every bed, whether it is outside the pawn's area, forbidden or unreachable, and who owns it.
- Not yet verified in game; if it still happens, run that debug action on a pawn and send me the log.

## 0.13.1 - knocking at the door
- A worker who needs into another family's zone now goes to the edge and knocks (a visible 'Knock, knock' every few seconds). Whoever is nearest in that family who is awake and able is called to the door and stops what they are doing to come; if they don't come, the next knock calls someone else. The worker waits a reasonable while after arriving, then gives up and carries on. Before, an adult was chosen once and the worker could be left waiting.
- Not yet verified in game.

## 0.13.0 - relationship events
- 90 relationship moments across nine groups (within a family, between friends and others, between families; adult with adult, adult with child, child with child), all built on one engine that separates opportunity, motivation, execution, reception and consequences. See docs/EVENTS.md.
- People outside the family now get bonds of their own once something has brought them together. Two families have a standing built from incidents and shared moments, never averaged from members. Promises the mod itself records can be kept or broken.
- Moments are real: two free people walk up and talk (or go for a short walk), or the moment happens during something already shared: a meal, a picnic, a gathering, a story, teaching, a bedside visit, a game, shared work.
- Two families with a real grievance are invited to a gathering to mend things.
- New Moments tab in the Families window, settings to turn groups off, set how often, and set how much you are told, and a debug action that explains why an event is or is not possible for two pawns.
- 22 new mutation checks. Not yet verified in game; see docs/EVENTS.md for what is smaller than the design.

## 0.12.3 - furniture counts
- Family zones can now be painted over beds, tables and other furniture (the game's own zone tool refuses them). Walls and fog are still excluded. Zones painted earlier need repainting over the furniture.

## 0.12.2 - roofs
- Building and removing roofs inside another family's zone now brings a builder to the door too (roofs are cells in the roof areas, not blueprints, so they were missed).

## 0.12.1 - doors find the work themselves
- The builder never approached another family's zone because the game never offers work outside a pawn's area, so my first hook never fired. Now the mod checks construction and repair jobs inside family zones every half second and sends an idle colonist from another family (who has Construction or Repair enabled) to the edge to be let in. Their own area is lifted for the visit and put back after, including across a save. Turn on Log events in the mod settings to see each step in the log.

## 0.12.0 - family doors, clearer zones, zone toggle
- A colonist stopped from building inside another family's zone now waits at its edge until a member of that family lets them in (an adult can let anyone in; a child can only let in another child). They build, and the pass ends when they leave. Only construction (frames and blueprints) triggers this.
- Family zones are now a faint tint (5% opacity) so everything underneath is clear.
- New toggle in the bottom-right map controls that shows or hides only family zones.
- Not yet verified in game.

## 0.11.0 - family zone is a real zone type
- New **Family zone** tool in Architect > Zone (paint, then Delete Zone to shrink or remove). Choose the family from the zone's button or the Families > Zone tab; every family member, including those born or married in later, is kept to it. The zone owns a matching allowed area that follows its cells.
- Not yet verified in game.

## 0.4.1 (unreleased) - bond rework from simulation
- New headless five-year simulation of three families (Source/FamilyLife.Tests/SimulationTests.cs; report in the test output folder). It showed the old math let every family settle at 61-76 attachment regardless of character, with no rifts.
- Attachment now settles mainly by how the two actually feel about each other; being related or sharing a house sets only a floor. Time together helps in proportion to liking, helps less the closer they already are, and grates (adds tension) when one dislikes the other. Result in the simulation: warm family about 57, mixed about 37, guarded about 33, and clashing pairs open lasting rifts.
- The Families list no longer shows families of one (unless opened directly from a surname or the Family button).
- 169 tests; mutation check 46 rules, all caught.

## 0.4.0 (unreleased) - Phases 4-7: identity, major events, conflict, polish
- Family culture (values drawn from members' traits and shared life), legacies, defining memories, and traditions the family takes up and holds as gatherings (mute or hold now in the Traditions tab).
- Gatherings through the game's own party system for traditions, coming of age and memorials; anniversaries, reunions and missing someone.
- Dates for partners with a trusted sitter for children, teaching between relatives, baby bonding (hold, sing, play), bedside visits.
- Rifts between relatives, sibling neglect and rivalry, reconciliation during family time, couple health that scales the game's breakup chance. Affairs are off by default; strict romance is available.
- New settings for all of it; chronicle, Overview, Traditions and Relationships screens extended. Mutation check covers 42 rules. Not yet tested in the game: see docs/PHASE4-7.md.

## 0.3.0 (unreleased) - Phase 3: everyday family life
- The Families screen opens from a Family button on the bio card (and the surname link or the pawn's Family tab) instead of the bottom bar. It is now a normal window you can drag and resize.
- Family time: relatives spend time together during joy time, chosen by attachment, how long since they were last together, and tension.
- A soft daily family-time budget (default 2 hours) that only keeps new activities from starting.
- Bedtime stories: in the hour before a child's bedtime the free parent with the best Social skill reads to them, fetching a book if one is nearby. The child learns a little.
- Eating together: relatives who finish a meal in the same room around the same time grow closer and get a small mood lift.
- Settings for all of it. Offline Harmony check now covers the meal hook; mutation check covers 23 rules.

## 0.2.0 (unreleased) - Phase 2: relationships
- Directed bonds (attachment, trust, tension, familiarity) between relatives, partners and housemates, driven by time together, the game's opinion, and family events.
- Household closeness (derived), left-out detection, and a Relationships tab.
- Deeper, temporary grief for close relatives when someone dies (setting, on by default).
- Surnames on character cards open the family.
- Fixes from the code check: ancestry no longer erodes when the game discards old pawns; dead partners are remembered; vanished partners are not reported as breakups; a colonist who dies with no relatives around is recorded as dead; the relative-walk cap never drops colonists; selection updates when the window is already open.
- Offline check that every Harmony patch matches the game (1.6 and 1.7); mutation check script.

## 0.1.0 (unreleased) - Phase 1: foundation
- Durable family records: people, lineages, households, a bounded chronicle.
- Lineages survive the death of their founder and are never recomputed from a changing surname.
- Households: partners who move in together form one, children stay with parents, adult children stay until they pair up, anyone can be moved by hand.
- Families window (bottom bar) and a Family tab on colonists.
- Builds for RimWorld 1.6 and 1.7. Requires Harmony only.

