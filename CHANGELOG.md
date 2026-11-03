# Changelog

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

