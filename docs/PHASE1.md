# Phase 1 report — foundation

Version 0.1.0 (now 0.2.0 with Phase 2; see `PHASE2.md`, which also lists the code-check fixes made to Phase 1 afterwards). Built for RimWorld 1.6 and 1.7. Not committed to git and not pushed anywhere.

## What was implemented

- **Family records** (`Domain/`): people, lineages, households and a bounded chronicle, in pure C# with no game types.
- **Reconcile** (`FamilyRegistry.Apply`): takes what the game currently shows and works out what changed. Idempotent and order-independent.
- **Game layer**: one `GameComponent`, a pawn observer that reads vanilla relations, three Harmony hooks that only mark the model dirty, an hourly sweep plus a 60-tick debounce after a change.
- **Persistence**: save/load for every domain class, by id and number only. No pawn references, no stored text.
- **UI**: a Families window (bottom bar) and a Family tab on every humanlike pawn.
- **Debug actions** (dev mode): *Resync family records now*, *Log family records*. One setting: log family events.
- **Build**: 1.6 and 1.7 from shared source, same layout as the author's other mods. `scripts/Test.ps1`, `scripts/Stage-ToRimWorld.ps1`.

## Behaviors that are actually functional

**Who is recorded.** Free colonists and slaves are "in the colony". Their parents, children and partners/exes, and ancestors/descendants up to three generations (at most 500 people), are recorded too. Prisoners, lodgers, guests, animals and mechs are never in the colony. Anyone not in the colony is recorded but never housed.

**Lineages.**
- A lineage exists for colonists, their ancestors, and everyone descended from those ancestors. Ex-partners and in-laws outside the colony have records but no lineage.
- A child inherits the lineage of the parent whose surname matches, else the lower-id parent. A person with no known parents founds a lineage.
- A person with no known parents whose spouse has the same surname joins the spouse's family by marriage instead of founding a duplicate. (Found by the tests: a founding couple used to produce two "Smith" families.)
- A spouse from a different surname is affiliated by marriage in both directions.
- A parent link that appears after someone was first recorded is recorded as recognition (adoption or found family); it does not change descent.
- The assignment is stored and never recomputed from a name, so marriage name changes move nobody. A lineage survives the death of its founder.

**Households.**
- Lone colonist: own household. Parents and children: one household. A juvenile follows a parent (preferring the household that holds both), then a sibling.
- Adult children stay with their parents until they pair up.
- Two adults who become partners: if one household would be left empty, it grows; if they already share a home with another partner, the new partner joins it; otherwise they and their dependent children start a new household.
- Break-ups keep both people where they are. Death and leaving remove a resident and empty households dissolve. Someone who returns goes back to their old household if it still exists.
- The Move... button in the Households tab (and the same logic through `MoveToHousehold`) moves anyone; an adult takes the juvenile children living with them.

**Chronicle.** Records births (a person first seen under a day old with a parent), arrivals, departures, deaths, new lineages and affiliations, partnerships formed and ended, new households. Capped at 400. Text is built from keyed language strings when displayed. When the mod is added to a game already in progress it writes a single "began keeping records" entry instead of fake arrivals.

**Safety.** Dead, discarded and missing pawns cannot cause null references (everything is by id). Cyclic or dangling parent data cannot hang or throw. A failed sweep logs once and retries after an hour instead of every tick. A damaged save is repaired on load. Null and duplicate input is tolerated.

## What is not implemented

Everything after Phase 1: relationship bonds, memories, culture and identity, traditions, intentions and family activities, meals, stories, roles, weddings and funerals, rifts, affairs, divorce handling beyond records, heirlooms, and every optional integration.

Not shipped on purpose because nothing could use it yet: household lead, home area, cohesion, Values/Relationships/Traditions/Other-Families tabs. There are no placeholder screens for them.

Known gaps within Phase 1:
- **Clicking a pawn's last name** now opens the family screen (added after the code check; position needs an in-game check).
- **Adopted vs born** cannot be distinguished: vanilla stores both as a Parent relation. Only parents added *after* first recording count as recognized.
- **Death time** is when the sweep noticed it (within about a second if the hook fires, up to an hour if a mod bypasses it).
- **Adult siblings with no partner and no parents in the colony** each get their own household. Use Move... to combine them.
- **Names** on records are snapshots updated whenever the person is observed; a relative who is no longer observed keeps their last known name.

## Tests executed

`dotnet test`: **56 tests, 56 passed** (about 0.1 s).

| Area | Count | Covers |
|---|---|---|
| Lineage rules | 17 | surname inheritance, lower-id fallback, marriage affiliation both ways, same-surname couples, yielding to a spouse with ancestry in either id order, divorce stability, sticky assignment after a name change, founder death, dead ancestors, late parent links, cycles, missing parents, same-sex parents, half-siblings |
| Households | 21 | lone, two-parent, single-parent, adult child, orphans, away people, partner move-in cases (alone, both busy, with dependents, blended, second partner), break-up, death, dissolution, leave-and-return, missing from input, user moves and no-op moves |
| Properties | 11 | idempotence, order independence (25 shuffles), bootstrap behavior, births, arrivals, bounded chronicle, revival, damaged-data repair, null/duplicate input, 400 people × 31 sweeps in well under the 5 s limit, id uniqueness |
| Save coverage | 7 | every model field appears in a save call; no duplicate labels |

**Mutation check.** I deliberately broke four rules and confirmed the suite fails each time (5, 9, 1 and 4 failures): households never dissolving, lineage recomputed instead of sticky, a field forgotten in the save code, and a leaving colonist keeping their household. A fifth attempt was a malformed edit that did not compile and is not counted. All files were restored and the suite re-run green.

**Builds.** 1.7 (.NET 10, Harmony 3.0.0-rc.3) and 1.6 (.NET Framework 4.7.2, Harmony 2.4.3): 0 errors, 0 warnings.

**Static checks on the staged mod.** XML well-formed; every class named in the defs exists in both DLLs; all 46 translation keys used in code exist in the language file (plus the two dynamic key families).

Staged to `B:\SteamLibrary\steamapps\common\RimWorld\Mods\WilsFamilyLife` with the game closed.

## Requires in-game verification

None of this has run inside RimWorld. Please check, in order:

1. The game starts with the mod enabled and the log shows `Loaded (3 hooks)` and `Family tab added to N humanlike race(s)`, with no red errors. Test with the mod only plus Harmony, then with your normal list.
2. Start a new colony. After the first moments, *Log family records* (dev mode → Wil's Family Life) shows lineages and households matching the starting colonists.
3. Open **Families** (small button group at the bottom right, next to History/Factions) and the **Family** tab on a colonist.
4. **Save, quit, reload.** Records and households must be identical. This is the main thing the unit tests cannot cover.
5. Add the mod to an existing save. You should get exactly one "began keeping records" line, no invented births.
6. Let a baby be born, form a couple, break a couple up, kill a colonist. Each should appear in History within about a second, and households should follow the rules above.
7. Recruit a colonist who has relatives, and send a pawn away and back.
8. Remove nothing else, but confirm the mod can be disabled and the save still loads.
9. Check Multiplayer, Character Editor and EdB Prepare Carefully are unaffected (they edit relations directly; the hourly sweep is what catches those).
10. Check the Families window layout at your usual resolution and UI scale.

## Design compromises

- Reconcile-first instead of hook-first, trading a short delay for correctness against mods that bypass vanilla methods.
- Lineage is decided once and stored, so a name edit never reshuffles families; the cost is that a mistaken first assignment is not self-correcting.
- Households are explicit stored entities, not recomputed from relations, so the player's moves persist.
- The Families window recomputes its lists every frame it is open. This is fine at normal sizes; if it shows up in profiling with very large colonies it can be cached per sweep.
