# Modular architecture: audit, ownership and the extraction plan

This is the working document for splitting Wil's Family Life into a framework and optional modules. It records what was found in the code (not what earlier descriptions said), who owns what, what has been done and verified, and what has not. **Nothing here claims the modules are independently installable yet: that needs separate assemblies and packages, which are stages 3 and 4 below.**

## 1. Where this stands

| Stage | What | State |
|---|---|---|
| 0 | Audit and ownership (this document) | Done |
| 1 | Boundaries inside the repository: each owner has its own folders, registration contracts exist and are tested, Education and Community Care are reached only through them, and the boundary is enforced by tests | **Done and tested offline** |
| 2 | Move the module-owned *state* out of the shared registry (module payloads) so a module's code can leave the framework assembly | Not started (the real blocker, section 7) |
| 3 | Separate assemblies for Education and Community Care, then the framework, then Family Life | Not started |
| 4 | Separate mod packages, About.xml files, load order, the bundle, load-combination tests | Not started |
| 5 | Family Life extraction; future modules | Not started |

Verified so far (offline): 1,017 tests pass, including the boundary tests, the registration contracts, saved-identifier compatibility against the last all-in-one version, and the pre-existing domain behaviour. Both game versions build (1.6, 1.7) and the 25 patch classes check. **Nothing has been run in RimWorld** for this change.

## 2. Audit of the current architecture

Read from the source on this branch.

- **One project, one assembly** (`Source/FamilyLife/FamilyLife.csproj`, `FamilyLife.dll`, namespace `FamilyLife`), built once per game version (1.6 on .NET Framework 4.7.2, 1.7 on .NET 10) into `1.6/Assemblies` and `1.7/Assemblies`. About 32,000 lines: Domain about 13,000, Game about 12,600, UI about 5,300, Persistence about 1,000.
- **Domain is pure.** No file under `Domain/` references Verse, RimWorld or Unity. The test project compiles that folder directly and would stop building if it did. This is what makes the rest of the plan feasible.
- **The registry is one class.** `FamilyRegistry` is a `partial class` over about 40 files (people, houses, bonds, offices, estates, crime, institutions, ...). `CommunityOffice` is likewise partial. A partial class cannot span assemblies, so anything module-specific that is a member of those two classes cannot leave the framework assembly until it is turned into module-owned state (stage 2).
- **Saves.** One game component (`FamilyComponent`) scribes the registry (`Scribe_Deep.Look(ref Registry, "registry")`). About 790 labels across about 70 saved classes (`Persistence/Persist.cs`, `PersistGovernance.cs`). Enums are saved as numbers (`OfficeKind`, `OfficeEnd`, `FamilyRole`, `PendingEvent`).
- **Start-up.** `FamilyLifeMod` reads settings and patches Harmony by reflecting over the assembly once (`CreateClassProcessor` per patch class). Perspective Shift is bridged by reflection (`PerspectiveShiftBridge`), cached.
- **Patches.** 25 patch classes: relationship and death hooks, thing-story hooks, food and meal hooks, area restriction, the character card, the colonist bar, play-settings button, and one for building buttons. Only the last concerned a module.
- **Scheduling.** `FamilyComponent.GameComponentTick` runs a throttled sync, four staggered maintenance phases, and several drivers. Office work ran inside `Governance.Maintain` (phase 0).
- **Defs.** 110 defs: 52 `JobDef`, 49 `ThoughtDef`, one each of `HediffDef`, `LetterDef`, `GatheringDef`, `JoyKindDef`, `JoyGiverDef`, `QuestScriptDef`, `WorldObjectDef`, two `ScenarioDef`. XML names C# classes (`driverClass`, `letterClass`, `workerClass`, `Class=`) by `FamilyLife.<Name>`; RimWorld resolves them in any loaded assembly, so keeping the namespace keeps the XML valid when a class moves.
- **Content.** 11 English translation files; 14 role icons under `Textures/WilsFamilyLife/Roles`, one UI texture; no `AGENTS.md` in this repository (conventions come from `README.md`, `docs/`, and the existing tests and scripts).
- **Packaging.** `About/About.xml` (packageId `SpacemanWil.WilsFamilyLife`, v0.13.4), `scripts/Stage-ToRimWorld.ps1` (builds both versions, copies one folder set). One package only.

## 3. Ownership

Each existing system has exactly one owner. Systems that are not being extracted now keep their current home and are marked **existing feature**; they are not silently deleted or hidden in the framework.

**Community Framework** (shared foundation; useful with no optional module installed)
- Pawn identity records and name snapshots, kinship, lineages/Houses, households, membership and Associates, bonds (Closeness, Trust, Tension, Familiarity), knowledge and disclosure, actor authority (`KnowledgeAndAuthority`, `PerspectivePolicy`, `ContextPolicies`, `OfficeAuthority`), culture and ambition infrastructure, event records and memories, narrative presentation, text variety, the office registry, elections and succession (`CommunityOffices`, `Succession`), planned-event notices, professional contact (`ProfessionalExchange`), results (`ProfessionalResults`), working hours, pawn readiness (`PawnReadiness`), shared indexes/caches/throttles/performance probe, the community window shell (navigation, layout, perspective selector), the pawn inspect tab, role icons and badges, settings shell, module registration (`Framework/`, `Game/FamilyModules.cs`).

**Family Life**
- Family meals and time, parenting, dates and sitters, traditions and gatherings, weddings, funerals, coming of age, reunions and visiting relatives, camping, bedtime stories, comfort, kin rescue, family zones and home areas, family doors; the hall keeper's meals and the chaplain post are held here today as part of the existing features.

**Education** (`Education/`)
- Teacher post, classes, subjects, attendance and breaks, book-supported lessons, the learning record, the annual assessment, rewards, classroom selection, the "Hold class" button and its queue, the one-to-one lesson, and their jobs, defs and strings.

**Community Care** (`Care/`)
- Healer post, check-up rounds and the appointment book, preventive-care conversations, medical bedside visits, the outbreak round, check-up benefits and memories (`WFL_CheckedUp`), the "begin check-ups" button, and their jobs, defs and strings.

**Existing features not yet extracted** (kept where they are; future module boundaries in section 9): House politics and affairs (estates and inheritance, crime families and heists, institutions and movements, council, favours, reparation, stewardship and steward duties, marriage alliances, house relations, household policy), the household economy (supplies, barter, claims), feuds and rifts (`Conflict`), the yearly assembly and voting (`AssemblyDriver`, `GovernanceDriver`).

Cross-cutting items and who holds them: the cooperative exchange and the professional-contact limiter are **framework** (both modules use them); `ExchangeTopic` and the topic lists stay in `GuidedExchanges` (framework) for now; `ResultQuality` and `PendingEvent` are framework; the topic phrasing strings are shared.

## 4. Dependency map

```
                    wils.framework   (Domain, shared Game/UI, Framework contracts)
                 /          |            \
        wils.familylife  wils.education  wils.care        (each requires the framework only)
```

Rules, all enforced by `ModuleCatalog.Validate()` at start-up and by `ArchitectureTests`:
- An optional module may require the framework and nothing else. Modules never require one another.
- The framework requires no module.
- No reference between Education and Community Care (zero today).
- Shared code names a module in exactly one place, the composition root `Game/FamilyModules.cs`.

Measured couplings left (the architecture tests fail if a new one appears):

| From | To | Where | Plan |
|---|---|---|---|
| shared | Education, Care | `Game/FamilyModules.cs` names `EducationModule` and `CareModule` | Expected: this is the composition root. It disappears when each module is its own assembly and registers itself. |
| Education, Care | shared | They use framework internals directly (registry, `FamilyComponent`, `Activities`, `PawnReadiness`, `Conversations`, `OfficeDriver` partial) | Allowed direction. Becomes `InternalsVisibleTo` or public framework API in stage 3. |
| Education, Care | `CommunityOffice` / `FamilyRegistry` partial members | Fields and methods live in `Education/Domain` and `Care/Domain` but are *members of framework classes* | The stage 2 blocker. See section 7. |

## 5. Registration contracts (built and tested)

All in `Source/FamilyLife/Framework/` (plain C#, no game types, tested directly) plus the host `Game/FamilyModules.cs`.

| Contract | Purpose | Identifier form |
|---|---|---|
| `ModuleCatalog` | Modules, capabilities, enable/disable, whole-set validation (one framework, required modules present, optional modules require only the framework, no cycles) | `wils.<area>` and `wils.<area>.<name>` |
| `OfficeCatalog` / `OfficeDefinition` | Posts a module defines; the stable id is an alias for the already-saved `OfficeKind` byte, so a save needs no change | `wils.care.healer`, `wils.education.teacher`, `wils.familylife.hallkeeper`, `wils.familylife.chaplain` |
| `OfficeAvailability` | Posts of an absent module are not elected, not wished for, not shown as vacant (a holder already in a save stays visible) | |
| `WorkRoster` / `WorkItem` | Autonomous work from one shared roster: staggered, budgeted, rotated, skipped for a disabled module, cancellable, one failure never stops the rest | `wils.education.office.teacher`, ... |
| `PlannedEventCatalog` | Routine events (check-up round, outbreak round, annual assessment): saved kind, notice key, begin/skip behaviour. A waiting event of an absent module is cleared, never run | `wils.care.checkups`, `wils.care.outbreak`, `wils.education.assessment` |
| `ContributionList<T>` | Things modules add to the interface; a disabled module's are skipped. Used for building buttons, developer-menu diagnostics and state resets | `wils.education.buttons`, `wils.care.diagnostics`, `wils.education.reset`, ... |
| Capabilities | What other code asks about instead of asking whether a module is installed | `wils.education.tutoring`, `wils.education.classes`, `wils.education.assessment`, `wils.care.checkups`, `wils.care.outbreak`, `wils.familylife.meals` |

Registration happens once, when `FamilyModules` is first used (the mod constructor touches it), and is validated once; any problem is logged as an error naming the identifier. Nothing is discovered by reflection while the game runs. The existing Harmony patch loader reflects once at start-up; Education and Community Care now add no patch of their own (a test enforces it): their buttons go through the one shared building patch and the contribution list.

What was *not* built, on purpose: registration for narrative templates, Story Thread providers, settings sections, window tab contributions and module-owned persistence. Those need a real second consumer to design against; the stage 2 payload work will drive persistence, and the window contributions are stage 5.

## 6. Serialized identifiers and compatibility

`Source/FamilyLife.Tests/Baselines/saved-identifiers.txt` lists, as written by the last all-in-one commit (`fc0bd3d`): every saved label of every saved class (787), every def name (110), every class named from XML, the saved enums (`OfficeKind`, `OfficeEnd`, `FamilyRole`) and the `PendingEvent` numbers. `SavedIdentifierTests` requires every line to still exist, enum members to keep their order (they are saved as numbers), and every class XML names to still be declared. Result: a save written before this work reads exactly the same labels and numbers; **no save migration was needed for stage 1**, because no type, label, def, namespace or enum value changed.

Why this holds, and when it will stop holding:
- Moving a C# type between *files* is invisible to a save. Moving it between *assemblies* is not guaranteed to be: RimWorld writes a class name only for polymorphic `Scribe_Deep` entries, and resolves it by name across loaded assemblies, so keeping the namespace `FamilyLife` and the class name keeps those valid. This must be tested per type before each assembly move.
- The module-owned *fields* (education's learning record, care's appointment state) are scribed inside `CommunityOffice`. When they move to module payloads (stage 2) the old labels must still be read on load (a migration), and a save with a module removed must still load. That is the part that needs a real migration and real save testing.
- The unnamed identifiers a module adds (its job, hediff, thought and letter defs) are in the module's own XML once packaged; the def names stay, so an old save resolves them.

Education/Care identifiers that appear in saves today: jobs `WFL_ClassTeacher`, `WFL_ClassStudent`, `WFL_TestStudent`, `WFL_OfficeLesson`, `WFL_ClinicDoctor`, `WFL_ClinicPatient`, `WFL_Checkup`; hediff `WFL_CheckedUp`; letter `WFL_OfficeEvent` / class `ChoiceLetter_OfficeEvent`; thoughts `WFL_ReassuringCheckup`, `WFL_DismissiveCheckup`, `WFL_InspiringLesson`, `WFL_BoringLesson`, `WFL_Testing*`; office field labels (`pendingKind`, `testIds`, `lessonLog`, `missed`, `refused`, ...).

## 7. The real blocker, and the extraction order

`FamilyRegistry` and `CommunityOffice` are partial classes, and module-specific members were added to them. Stage 1 put those members in `Education/Domain` and `Care/Domain` so ownership is visible, but they still compile into the framework assembly. To let the code leave:

1. **Module payloads on the office record.** Give `CommunityOffice` one neutral, versioned payload slot per module (`Dictionary<string, string>` keyed by module id, plus a schema version) that the module reads and writes itself. Education's `LessonLog`, `BookUse`, `RecentSubjects`, `TestScore`, `TestExcused`; Care's `Missed`, `Refused`, `RoundWindow`, `RoutineDay`, favouritism counters. Fields that are genuinely shared (`PendingKind`, `PendingTick`, the session queue, `Examined`) stay.
2. **Load-time migration.** On load, if the old labels are present and the payload is empty, fill the payload from them once (idempotent); keep reading the old labels for a release; do not write them again. A save made after migration written without the module installed keeps the payload as an opaque string, untouched.
3. **Registry methods become module services** (extension methods or module classes) over the framework's public API (`OfficeFor`, `Persons`, `ChangeBond`...), which needs that API made public (or `InternalsVisibleTo`).
4. Only then move `Education/` to its own project, then `Care/`, then the framework.

Order: Education first (it is the smaller state and had no dependency on Care), then Community Care, then Family Life. Family Life last because it is the largest and its code is the most entangled with the existing house features.

## 8. Packaging plan (not yet built)

| Package | packageId | Requires | Ships |
|---|---|---|---|
| Wil's Community Framework | `SpacemanWil.WilsCommunityFramework` | Harmony | framework assembly, shared defs, role icons, shared strings |
| Wil's Family Life | `SpacemanWil.WilsFamilyLife` (**kept**: existing installs and saves know it) | Framework | family-life assembly, its defs/strings; carries the existing house features until they have their own packages |
| Wil's Education | `SpacemanWil.WilsEducation` | Framework | education assembly, its defs/strings, the teacher icon |
| Wil's Community Care | `SpacemanWil.WilsCommunityCare` | Framework | care assembly, its defs/strings, the healer icon |

All `loadAfter` the framework and Harmony. Each declares 1.6 and 1.7 with the matching `1.6/Assemblies` and `1.7/Assemblies`. The "all modules" bundle is a folder of the four packages (no duplicate monolithic assembly). Install sequence for an existing player: install the Framework, then the modules wanted, then update Family Life; **a save from the all-in-one version needs all four to behave as before.** The shared role icons stay in the framework, and a module's own icon ships with the module, loaded by the same texture path, so nothing is duplicated. None of this is built; do not publish or merge.

## 9. Absence and removal, honestly

- **New game without a module:** supported by the design (no post, no work, no button, no event; capability checks say no). Verified offline only through the registries and the architecture tests.
- **Adding a module to an existing save:** designed to work (posts become available, their work starts, history appears); not tested in the game.
- **Disabling an activity in settings:** already worked (`holdClasses`, `healthChecks`); unchanged.
- **Removing a module from a save:** **not safe today and not claimed.** A removed module leaves: hediffs on pawns, thoughts, a waiting planned event, jobs on pawns mid-activity, and office payload. A "prepare for removal" step (end sessions, release jobs and reservations, clear queues, cancel pending events, handle the hediff and memories explicitly, keep the neutral history) is **not implemented**; the contribution list `Resets` and the clearing of a waiting event of an absent module are the first pieces. Until it exists, do not remove a module from a save you care about.

## 10. Integrations

Ideology (chaplain via the moral guide), Biotech (children, babies), Royalty, Perspective Shift (reflection bridge, cached, optional), Better Bedrooms, hygiene/toilet mods (read through need names, no reference). None is required. Each stays with the module that needs it when the assemblies split; the framework keeps only the Perspective Shift bridge (shared authority) and the need-name readers (`PawnReadiness`).

## 11. Possible later modules (documented, not built)

- **House Affairs:** stewardship and steward duties, house relations and petitions, marriage alliances, household policy. Needs from the framework: house records, authority checks, planned-event notices, contribution lists for window sections.
- **Community Governance:** the yearly assembly, voting, campaign, succession politics, institutions and movements, the council. Needs: the office registry and election infrastructure (already framework), authority, notices. Optional with Education and Care: their posts are elected through it.
- **Household Economy:** supplies, claims, barter, favours and debts. Needs: houses, households, work roster, contributions.
- **Underworld:** crime families, heists, protection arrangements, reparation, coups. Needs: knowledge and secrets (framework), incidents, authority. Optional with Governance.

No new election, justice, crime or economy mechanics were added or are planned in this refactor.

## 12. Verification status

Offline, done: `dotnet test` 1,017 passed (3 consecutive runs; one earlier run had a single unrelated failure in `StoryPolarityTests`, which passed alone five times, consistent with the existing shared static narrative hook, and was not investigated further); both builds; 25 patch classes; boundary, contract and saved-identifier tests.

Not done, and not claimed: separate assemblies and packages; load-combination tests (they need the packages); old-save migration of module state (nothing to migrate yet); module cleanup workflow; any in-game check. **Manual checklist for when the packages exist:** framework alone (family window, People/Relationships/Story/Home/House Affairs tabs, no Teacher/Healer cards, no class or check-up buttons, no errors in `Player.log`); each module added to the same save; an old all-in-one save loaded with all four; a class saved and loaded mid-session; an appointment saved and loaded; the Teacher/Healer replaced; Perspective Shift on and off; removal with the cleanup step.

## 13. Stage 2 status (module state and removal)

- **Done, verified offline:** module state is an opaque versioned payload on the office record (`Framework/ModuleState.cs`); legacy labels are migrated lazily; Care and Education use extension-method services over public framework API; isolation projects (`Source/Isolation/`) build each module's pure code against the shared pure code alone; a pure `CleanupReport` and `EndCareActivity` / `EndEducationActivity`, tested; game-level `PrepareForRemoval` per module, reachable from the developer menu.
- **Removal limits:** neutral records stay in the save. Role icons and defs ship with the module, so a saved post shows a plain label once it is gone. Preparation reflects the game as it is now: run it again if the module ran after. It was not observed in RimWorld.
- **Stage 3 blockers:** about fifteen shared Game types that module Game code uses are internal (need public API or `InternalsVisibleTo`), and the composition root disappears once modules are separate assemblies. Packages, load-combination tests and in-game checks remain.
