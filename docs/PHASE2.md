# Phase 2 report — relationships (bonds, closeness, grief)

Version 0.2.0. Includes the Phase 1 code-check fixes (see the bottom of this file).

## What was implemented

- **Directed bonds** (`Domain/Bonds.cs`, `FamilyRegistry.Bonds.cs`): how one person feels about another, with attachment, trust, tension and familiarity (0-100 each). A bond exists only for a colonist toward their parents, children, siblings, partners, former partners and housemates, capped at 24 per person.
- **Updates from observable things**: time spent in the same household or the same colony, the game's own opinion of that person (read-only), and family events (birth, partnership formed or ended, death).
- **Closeness** (`HouseholdCohesion`, `LineageCohesion`): derived on demand from the bonds, never stored. A household of one has none.
- **Left-out detection** (`LeftOutIn`): a household member the others are weakly attached to.
- **Grief**: when someone dies, living relatives who were closely attached get a small, temporary extra mood thought. The stage follows the attachment (-2, -4, -6 at most). It can be turned off in the mod settings.
- **UI**: closeness and any left-out member on each household in the Overview tab; a **Relationships** tab with bars for each bond and a tooltip with the numbers.

## What each value is for

| Value | Used now | Planned use |
|---|---|---|
| Attachment | Sets how deep grief goes; feeds closeness | Desire to spend time together; how hard a loss lands |
| Trust | Feeds closeness | Confiding, reconciliation, who is chosen to help |
| Tension | Feeds closeness; reduced by time | Avoidance and conflict |
| Familiarity | Pulls attachment and trust upward | Effectiveness of teaching and storytelling |

Only attachment changes gameplay today (grief). The Relationships tab shows the other values so you can watch them behave, but they will not change what pawns do until Phase 3. I have kept it that way rather than invent effects without the activity system to carry them.

## How a bond changes

- Familiarity grows quickly in a shared household, slowly in the same colony, and fades slowly when apart.
- Attachment moves toward a target set by the kind of relationship (a parent starts more attached than a child, a child more than a sibling), plus familiarity and the game's opinion. It moves a little each hour, never jumps.
- Trust moves toward a target built from familiarity and opinion, lowered by tension.
- Tension rises toward the strength of a negative opinion and fades when the opinion improves.
- A long gap between updates (a paused or time-skipped game) counts as at most two days.
- Events nudge bonds: a birth strengthens the child's bonds with both parents, a partnership strengthens both directions, a breakup lowers attachment and trust and adds tension.
- A bond toward someone who has died stops changing and is kept as history. The dead hold no feelings.

The numbers are in `BondTuning` and are tuning values, not rules. Tests check directions and bounds, not the exact numbers.

## What changed in the plan

- **The memory ledger moves from Phase 2 to Phase 5.** The only defining events detectable today (births, deaths, partnerships) are already in the chronicle, so a ledger now would only duplicate it. It arrives with the first events that add information (rescues, reunions, ceremonies).

## Not implemented

Everything in Phases 3 to 7. Notably, nothing yet makes pawns *do* anything with these values: no activities, meals, stories, traditions or conflicts.

## Tests

`dotnet test`: **83 passed**. 21 cover bonds: creation and non-creation, direction and starting points, familiarity at home and apart, opinion effects both ways, tension fading, bounds, extreme and missing opinions, no-time-passing, the two-day cap, birth, partnership, breakup, grief (who, how much, once only), simultaneous deaths, frozen bonds toward the dead, the per-person cap, cohesion and its absence for one person, left-out detection, damaged data, and a 400-person colony.

**Mutation check** (`scripts/mutation_check.py`): 12 deliberate breakages across both phases, **all caught**. The first run caught 9 of 12; the three it missed exposed weak tests (two deaths at once, a frozen bond that started at its own target so drift was invisible, and an extreme opinion that would overflow when negated). Those tests were strengthened and the script rerun.

**Static checks**: both versions build with 0 errors and 0 warnings; the offline Harmony check passes for 1.7 (installed game) and 1.6 (reference assemblies); every field in the new thought def exists on the game's `ThoughtDef` and `ThoughtStage`; all translation keys exist.

## Requires in-game verification

1. Open Families, Overview: a household of two or more shows a closeness word and number a short time after the colony starts. A lone colonist shows none.
2. Relationships tab: bars appear for relatives; values drift slowly over days, not minutes.
3. Kill a colonist who has an adult child or partner in the colony: within about a second the relative gets "missing someone", "grieving someone close" or "devastated by a loss". Compare against a distant sibling (no extra thought). Check the Mood tab and that it fades in about 8 days.
4. Turn the grief setting off: no new thoughts.
5. Save, reload, wait a day: bond values continue from where they were instead of resetting.
6. The mood penalty is small by design. If it feels wrong next to vanilla's own grief, the three values are in `Defs/ThoughtDefs.xml`.
7. Performance with a large colony: dev-mode profiler should show nothing from this mod outside the hourly sweep.

## Design compromises

- Only attachment has a gameplay effect so far (see above).
- Opinion is sampled once per sweep, so a short spike of anger between sweeps is not seen.
- Grief is computed from the bond at the time of the sweep after death. A bond that never had time to form has only its starting value, which is why siblings do not trigger grief at first.
- Closeness weights (60% attachment, 40% trust, minus half the tension) are a first guess and need watching in play.

---

## Code check of Phase 1 (done before building Phase 2)

| # | Finding | Result |
|---|---|---|
| 1 | The game clears every relation of a pawn it discards, so each hourly sweep would erase a child's recorded parents once a dead relative was discarded. Ancestry and lineage generations would erode over time. | Fixed: parents only accumulate. Test added. |
| 2 | A dead partner was forgotten when the body was discarded, and a partner who merely disappeared was reported as a breakup. | Fixed: dead partners are kept; only an explicit ex-relationship counts as a breakup. Tests added. |
| 3 | A colonist who died with no living relatives was never observed, so the death was recorded as "left the colony" and their household lingered. | Fixed in the game layer: recorded colonists are looked up directly. |
| 4 | The 500-person cap on the relative walk could have dropped colonists. | Fixed: the cap trims relatives only. |
| 5 | Clicking a surname while the Families window was already open did not change the selection. | Fixed. |
| 6 | Surname on the character card was not a link. | Done: hovering highlights it and clicking opens that family. The surname position is measured from the same text and font the game uses; **check it lines up in game**. |
| 7 | Harmony patch targets or parameter names could be wrong at runtime. | New offline check (`scripts/Check-Patches.ps1`, part of `Test.ps1`) verifies all four patches against the installed 1.7 game and the 1.6 reference assemblies, and was shown to fail when a parameter name is broken. |
| 8 | Surrogate births: Biotech adds a `ParentBirth` relation for the person who gave birth when they are not the genetic mother. | Verified in the game code; it is not read, because genetic parents are recorded. Surrogates are therefore not yet recorded as family. Planned with newborn bonding in Phase 5. |

Verified correct and left alone: the pawn inspector tab registration (the game reads `def.inspectorTabsResolved`), the death-relation behavior (only the Biotech overseer relation is removed on death), and the GameComponent lifecycle.

Known and accepted: removing the mod from a save makes the game log one "could not find class" error for the missing game component and then load normally, as with any mod that adds a game component.
