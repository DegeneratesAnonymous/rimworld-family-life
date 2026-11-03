# Wil's Family Life — design assessment and implementation plan

Status: Phases 1, 2 and 3 built (see `PHASE1.md`, `PHASE2.md`, `PHASE3.md`). Everything after Phase 3 is plan, not code.

## 1. Verified facts this design rests on

Checked against the installed game (1.7.4995), its decompiled assembly, the vanilla defs, and the mods installed on this machine.

| Fact | Consequence |
|---|---|
| Parentage is a `Parent` direct relation stored on the child; `Child` and `Sibling` are implied. `GetMother()/GetFather()` are gender-based. | Read parents from `DirectRelations`, never from gender. Same-sex and unusual parentage works. |
| Vanilla cannot distinguish a birth parent from an adoptive parent. | "Adopted" cannot be detected from vanilla data. A parent link that appears *after* a person was first recorded is treated as recognition/adoption. |
| Vanilla breakup weight is `0.02 × inverse-opinion × (0.4 if spouse)`. A romance attempt on a partnered pawn is weighted by opinion and a partner factor. | Relationship health patches these existing weights. Nothing is replaced. |
| Childcare work uses the **Social** skill. There is no childcare skill. | Story/teaching formulas use Social. |
| Books are vanilla (textbook = skill XP, novel = joy, schematic = research). Bookcases are vanilla. | A "library" is a room with bookcases. |
| `Pawn.Kill`, `Pawn_RelationsTracker.AddDirectRelation` and `TryRemoveDirectRelation` exist with the signatures patched here. | These are the event hooks. Other mods can change relations without calling them, so a periodic reconcile is the source of truth. |
| `PawnsFinder` is in the `RimWorld` namespace in 1.7. | Code imports both `RimWorld` and `Verse`. |
| 1.7 runs on .NET 10 + Harmony 3.0.0-rc.3; 1.6 on .NET Framework 4.7.2 + Harmony 2.4.3. | Two builds from shared source, exactly like the author's other mods. |
| Living Bonds hooks `Humanlike_PreMain` with a random pick of family interactions. | We never share that slot. Our recreation goes through `JoyGiver`s; our care intentions get their own think node. |
| Animal Traits are hediffs named `AnimalTrait_*`. Trait mods define ordinary `TraitDef`s with structured stat data. | Classification can use structured data, not description text. |

## 2. Critical assessment of the revised specification

### 2.1 Architectural weaknesses and fixes

1. **Persisting `Pawn` references is the first thing that breaks.** Dead, discarded, world and unloaded pawns turn into null references and save-time warnings. *Fix:* the persistent model stores integer pawn ids plus a name snapshot. Live `Pawn` objects are looked up at read time and are never saved by us.
2. **Hooks alone are not reliable.** Character editors, scenario loaders and other mods change relations without calling the methods we would patch. *Fix:* vanilla relations are the source of truth. Harmony hooks only mark the model dirty; a debounced and periodic reconcile compares vanilla to our model. The reconcile is idempotent, so a missed or duplicated hook cannot corrupt anything. This also makes the whole model unit-testable without the game.
3. **Directed bonds are O(n²) if stored naively.** *Fix (Phase 2):* store bonds only for pairs with a reason to exist (kin, partners, caregiver-child, repeated interaction), compact numeric fields, a per-pawn cap, and pruning when values decay to neutral.
4. **Four bond values plus four romance values only matter if each drives different behavior.** Phase 2 gives each a distinct job: attachment drives desire and grief weight; trust drives confiding, reconciliation success and caregiver choice; tension drives rifts and avoidance; familiarity gates teaching and storytelling effectiveness. Any value that ends up without a driver is dropped.
5. **A general intention scheduler is where this project can die.** *Fix (Phase 3):* intentions are scored by a small hourly planner into a per-pawn "desire" record. Doing something is then handled by **one** think node and **four** job archetypes (be-with, care-for, teach, converse) parameterised by flavor, plus vanilla `JoyGiver`s so family recreation consumes the joy slot instead of adding a schedule. Gatherings (traditions, weddings, funerals) use vanilla `Lord`s.
6. **Memory must not store text.** Entries store a structured kind, ids, tick and a numeric weight; text is rendered at display time from keyed language strings. This keeps saves small, translatable, and safe when the mod is removed. Ordinary activity is counted in aggregates; only entries over a significance threshold become defining memories, with a hard cap per lineage and per person.
7. **Culture drift is easy to make opaque.** Culture is a short fixed vector with bounded per-day change; every change records its reason so the UI can explain it.

### 2.2 Gameplay problems

- **Who participates?** Free colonists and slaves participate. Prisoners keep records but take no part. Guests, quest lodgers and visitors are never added. Animals and mechs are not family members; pets appear later as household companions through vanilla bonds.
- **Universal bonuses make close families dominant.** The design keeps bonuses small and situational and makes attachment double-edged (richer support, harder loss).
- **Adult children.** Adult children stay in their parents' household until they form a partnership, then move out with their dependents. The player can move anyone at any time.
- **Divorce.** Both partners stay in their current household until the player or later phases move them. Lineage and parent-child ties are untouched.
- **"Cook delivers every meal"** costs far more colonist time than it returns and fights food policy. Replaced by: members prefer to eat together when schedules line up; late arrivals join; the Cook role only prioritises preparing meals.
- **Home area.** A restrictive zone is a recurring source of stuck pawns. Replaced by a *derived* home: rooms of household members' owned beds and the dining table the household uses most. No painting. A player-set area can override it later.

### 2.3 Compatibility risks and handling

- Think-tree collisions: own node, own position, never `Humanlike_PreMain` first.
- Relation-method patches: apply per class; failure skips that hook and the reconcile still works.
- Multiplayer (installed here): the simulation uses only game ticks and the game's seeded RNG; no wall-clock or UI-dependent state in the model.
- Pawn id reuse: ids are unique inside a save. Pawn copies by editor mods become separate people, which is acceptable.
- Save compatibility: versioned data (`DataVersion`), additive changes only.
- Two game versions: shared source, separate builds, `#if RW_1_6/RW_1_7` for differences.

### 2.4 Performance

- No per-pawn tick comps. One `GameComponent`, event-dirty plus hourly staggered work.
- Reconcile cost is O(colonists × relations) with a hard cap on observed people; measured in tests (see `PHASE1.md`).
- Bounded storage: chronicle ring buffer, capped memories, pruned bonds.

### 2.5 Features cut, deferred or simplified

| Item | Decision |
|---|---|
| Heirlooms | Deferred past Phase 7. Narrative-only value; revisit once memories exist. |
| "Other Families" diplomacy / historic rivalries | Reduced to shared-ancestry and marriage links. No inter-family simulation. |
| Twelve binary trait tags | Reorganised (below). |
| Cook delivers meals | Replaced (see 2.2). |
| Restrictive home zone | Replaced by derived home. |
| Click-last-name opens the family screen | Needs a patch into the character card layout that must be verified in game. Phase 1 ships a Family inspector tab and a main-bar tab instead; the click-through is a Phase 7 item. |
| Household "lead" setting | Not shipped in Phase 1 because nothing consumes it yet. It arrives with the roles that use it. |

### 2.6 Trait taxonomy (replaces 12 binary tags)

Binary tags force "Forgiving" and "Volatile" to be separate boxes. Use **four bipolar temperament axes** and **seven interest affinities**:

- Axes: Warm↔Guarded, Even↔Volatile, Rooted↔Restless, Devoted↔Independent.
- Affinities: Hearth, Scholarly, Martial, Craft, Revelry, Faith, Status.

Classification precedence: explicit verified mapping → structured `TraitDef` effects (stat offsets, skill gains, disallowed work, thoughts) → conservative inference from known vanilla thought/relationship hooks → neutral. Results are cached by `defName` and degree. "Verified" means the defName exists in the installed mod, not that the mapping has been play-tested.

### 2.7 Missing edge cases the plan now covers

Founders with no known parents; pawns that arrive already married with children; adoption after the fact; same-sex parents; multiple spouses; half-siblings; stepchildren; children of divorced parents; orphans; pawns who leave and return; revived pawns; corrupt or cyclic parent data; unreadable names; relatives on other maps and in caravans; Biotech absent; Ideology marriage rules; very large colonies; mod added to or removed from an existing save.

## 3. Architecture

```
FamilyComponent (GameComponent, single entry point)
 ├─ FamilyRegistry          pure model, no game types, unit-tested
 │    ├─ PersonRecord       id, name snapshot, ancestry, presence, household
 │    ├─ Lineage            identity across generations; affiliations
 │    ├─ Household          who lives together; survives lineage changes
 │    └─ FamilyEvent        structured events (also the bounded chronicle)
 ├─ PawnObserver            reads vanilla relations into observations
 ├─ Harmony hooks           mark dirty only
 └─ UI                      main-bar tab, pawn inspector tab
Phase 2+: RelationshipGraph, MemoryLedger, CultureModel, IntentionPlanner, EventProcessor, IntegrationAdapters
```

Rules: the `Domain` folder may not reference Verse/Unity. The test project compiles it directly, so a violation fails the build. Persistence lives in `Persistence/` as partial classes.

### Lineage, household and relationship rules

- **Lineage:** a person's primary lineage is inherited from the parent whose surname matches theirs, else the lower-id parent. A person with no known parent founds a lineage. The assignment is stored and never recomputed from a name, so marriage name changes do not move people. People with no colonist-descendant link (ex-partners, in-laws, NPC relatives) get a record but no lineage.
- **Affiliation:** a spouse from another lineage is affiliated by marriage; a parent link added to an already-recorded person is recorded as recognition. Affiliations are never removed.
- **Household:** see `PHASE1.md` for the exact rules.
- **Relationships (Phase 2):** directed bonds complement vanilla opinion and are never used to overwrite it.

## 4. Phases

| Phase | Scope | Exit criteria |
|---|---|---|
| 1 | Records, lineages, households, persistence, event framework, basic UI, tests | Families form, change, survive deaths, and persist; domain tests pass; builds for 1.6 and 1.7. In-game save/load verification outstanding. |
| 2 | Directed bonds, derived closeness, grief. **Built.** (The memory ledger moved to Phase 5: today's defining events are already in the chronicle, so a ledger would only duplicate it.) | Bonds evolve from observable events without touching vanilla opinion. |
| 3 | **Built (partly).** Companion ranking, family-time budget, family time as joy, bedtime stories, eating together. Not yet: cook role, activity variety, children's own play, a general intention planner. | Measurable work-time cost under the budget in long simulated runs: still needs in-game measurement. |
| 4 | Culture, identity, traditions, storytelling, mentorship | Visibly different families after in-game years. |
| 5 | **Memory ledger**, weddings, funerals, dates, reunions, coming of age, newborn bonding (including surrogate birth parents), emergencies | Events reuse vanilla gatherings and write memories. |
| 6 | Rifts, rivalry, reconciliation, relationship health, divorce handling, optional affairs | Every conflict has a visible cause. |
| 7 | Integrations (each inspected first, tested present and absent), polish, balance, profiling, name-click | Core works with no optional mod. |

The memory ledger was first planned for Phase 2, then moved to Phase 5 once it was clear it would have no new sources of events until then.

## 5. Testing strategy

- Unit tests for every deterministic rule in `Domain`.
- Property-style tests: idempotence (applying the same observations twice changes nothing), order independence (shuffled input gives the same result), no crash on cyclic or dangling references, bounded growth.
- Scale test: hundreds of people applied repeatedly with a time budget.
- Not testable outside the game: save/load round trip through RimWorld's Scribe, harmony hooks, UI rendering, 1.7/1.6 runtime behavior. These are listed explicitly in every phase report.
