# Phases 4-7: identity, major events, conflict, polish (v0.4.0)

Everything here is statically verified only: the domain rules are unit-tested and mutation-checked, both game versions compile
with no warnings, and every Harmony patch matches the game's methods (offline). **None of it has been run in the game.** Treat
the "needs checking in the game" list at the end as the test plan.

## Phase 4 - family identity

- **Culture.** Each lineage has eleven slowly moving values (hearth, learning, fighting spirit, craftsmanship, merrymaking, faith,
  standing, and four character axes). They are pulled toward what the family's members are like, read from traits, and are nudged
  by what the family does (stories told, meals shared, teaching). Juveniles count 40%. It drifts about 4% of the gap per day.
- **Reading traits.** `TraitReader` knows the base game's traits by name (including degrees). A trait from another mod gets a small,
  conservative reading from the structured stat offsets it declares, never from description text; anything unclear is neutral.
  Nothing requires any other mod; trait mods only make the profile richer.
- **Traditions.** A family takes up a tradition (feast, reading night, hunt, craft fair, party, vigil, founders' day, reunion) when
  the matching value reaches 22 and at least three of its people are in the colony; it lets go below 8. A held tradition requests
  a gathering after its interval, only if the family is cohesive enough. Players can mute one or ask for it now (Traditions tab).
- **Legacies** (what the family is known for, from members' best skills) and **teaching/mentoring** during family time: an older
  relative clearly ahead in a skill teaches a younger one (small, bounded XP). Older relatives also tell family stories if the
  family has defining memories.

## Phase 5 - major events

- **Memory ledger.** Births, losses, weddings, partings, coming of age, reunions, anniversaries, traditions and reconciliations
  become bounded (40 per family) defining memories when weighty enough; the most weighty are shown on the Overview.
- **Gatherings** use the game's own party system (`GatheringWorker_Party` / `LordJob_Joinable_Party`) with a guest list: the
  family's colonists plus anyone who thinks well of the hosts. Used for traditions, a coming of age (a day or so after), and a
  memorial after a loss people felt. Attendees get a gathering-specific mood memory; relatives who attended together grow closer.
- **Anniversaries**, **reunions** (back after a long absence) and **missing someone** (far away, for a while) give small mood
  memories to the people concerned.
- **Dates**: partners can have an hour alone. If a baby or child lives with them, a trusted relative or friend (opinion >= 15 of both,
  best Social skill) is sent to mind the child; with nobody suitable, the couple just spend ordinary time together. The sitter's
  parents' gratitude and the sitter's own memory are recorded.
- **Newborn bonding in three ways**: parents hold (cradle), sing (lullaby at night) and play with babies, within the daily budget.
- **Bedside**: relatives close to someone downed or resting for illness sit with them.
- Not built: weddings/funerals as dedicated ceremonies (the game's own marriage ceremony is untouched; memorials use the gathering above).

## Phase 6 - conflict

- **Rifts** open when tension stays high for a day, when a parent gives one child far less attention than a sibling (neglect, then
  rivalry after three days, if enabled), or when a partnership ends. An open rift keeps tension up for ten days, then fades by
  itself after two calm days. Closed rifts are bounded (60).
- **Reconciliation**: relatives with a rift prefer an attempt to make peace during family time. The chance depends on both sides'
  trust and how forgiving their characters are; success eases tension and records a memory; a failed try waits a day.
- **Relationship health** for couples (attachment, trust, tension, recent quality time): Steady / Strained / Distant / Crisis.
  It scales the game's own breakup weight (x0.15 steady, up to x2.5 crisis). It never ends a relationship by itself.
- **Affairs** are off by default. When on, a partnered colonist in a Distant or Crisis relationship, low in devotion, and already
  familiar with someone may have the game's romance attempt allowed (at half weight). **Strict romance** forbids it outright.
  With both off, partnered colonists never start a romance with someone else (this changes vanilla's behaviour).

## Phase 7 - polish and integrations

- Settings for everything above; keyed text for all new UI, events, letters and thoughts (English).
- Chronicle entries for the new events; Traditions tab; culture, legacies and memories on the Overview; open rifts on Relationships.
- Integrations are limited to what the mod can read without depending on anything: trait names (base game and common trait mods),
  and books through the game's own book component. Other mods are not required and none is patched.
- **Not built** (scoped out): household roles (head, cook, teacher, maintenance), heirlooms, an inter-family tab, Observe/Encourage
  modes beyond mute and hold-now, translations beyond English.

## Tests and checks run

- 164 unit tests pass (domain: culture drift, traditions, memories, milestones, rifts, couple health, reconciliation, persistence
  coverage). Both game versions build with 0 warnings / 0 errors. The offline Harmony check passes for all seven hooks on 1.6 and 1.7.
- Mutation check: 42 deliberately broken rules, all caught.
- Not testable outside the game: every job driver, think-tree entry, gathering, mood memory and UI screen.

## Needs checking in the game

1. Load an existing save: no errors, the Families window opens, culture/traditions tabs draw, scrolling works.
2. A family with three or more colonists and a clear value: a tradition is adopted (message), later a gathering starts; guests walk
   to the party spot, get the mood memory, and nobody uninvited is dragged in. Muting stops it; Hold now starts it.
3. A child turning 13/18 (whichever the game uses for adulthood): the memory, and a coming-of-age gathering a day later.
4. A loss that people felt: a memorial request, the gathering, the mood effect.
5. Partners with a baby at home: a date starts, a sitter is sent, the sitter reaches the child and stays, the couple are not interrupted.
6. Bedside and baby bonding offered at sensible moments and not endlessly (cooldown channels).
7. Rifts open/close and the reconcile job runs; the Relationships tab lists them.
8. Breakup frequency feels right for steady vs. strained couples; affairs off means no new romances for partnered colonists.
9. Save, reload and continue: gathering lord jobs, jobs in progress and the Families window all survive.
10. Performance: no hitching on the hourly sweep in a large colony.

## Known compromises

- Gathering guests are chosen by opinion >= 20 of the hosts, not by deeper social logic.
- The hourly sweep now also updates culture, traditions, rifts and memories; each is bounded, but the cost has not been measured in a large colony.
- Anything touching the game's AI (jobs, lord jobs) was written against the verified 1.6/1.7 API shape but never executed.
