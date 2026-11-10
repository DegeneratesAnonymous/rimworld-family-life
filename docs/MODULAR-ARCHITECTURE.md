# Modular architecture: framework, Education and Community Care

This is the working document for splitting Wil's Family Life into a framework and optional modules. It records what the code actually does, who owns what, what has been verified and what has not. Installation and migration steps are in [INSTALLATION.md](INSTALLATION.md); the checks that need the game are in [MODULE-VERIFICATION.md](MODULE-VERIFICATION.md).

**Read this first: the extraction is built and checked offline; it has not been run in RimWorld.** Compilation, unit tests, assembly inspection and package layout checks are real evidence of structure. They are not evidence that a class starts, a pupil is released, or a save loads. Section 11 lists exactly which is which.

## 1. Where this stands

| Stage | What | State |
|---|---|---|
| 0 | Audit and ownership | Done |
| 1 | Boundaries inside the repository, registration contracts, tests | Done |
| 2 | Module state out of the shared registry (versioned payloads), legacy migration, isolation builds | Done; revised here (holder vs community records, section 5) |
| 3 | Separate assemblies for Education and Community Care, built against the framework's public surface only | **Done and compiled for 1.6 and 1.7; no `InternalsVisibleTo`** |
| 4 | Separate packages with their own About.xml, defs, strings, load order | **Built (`scripts/Build-Packages.ps1`) and statically checked; not loaded in RimWorld** |
| 5 | Persistent module lifecycle, retryable removal preparation, hand-over and load reconciliation | **Built and unit-tested for the pure parts; the game-side cleanup is unobserved** |
| 6 | Family Life extraction; future modules (Training Instructor, Counselor, others) | Not started, on purpose |

## 2. What was audited, and what changed

Read from the code, not from earlier descriptions.

- Before: one assembly (`FamilyLife.dll`), `FamilyRegistry`/`CommunityOffice` partial classes carrying module members, `OfficeDriver` and `OfficeEvents` as `partial` classes with the Teacher's and Healer's code interleaved, and `GovernanceMeetings` holding Teacher and Healer branches.
- Now: three assemblies per game version.

| Assembly | Project | Contains |
|---|---|---|
| `FamilyLife.dll` (package `SpacemanWil.WilsFamilyLife`) | `Source/FamilyLife` | Domain, shared Game/UI, Persistence, the registration contracts (`Framework/`), `FamilyFramework`, `FrameworkServices`, Family Life's own posts (hall keeper, chaplain) |
| `WilsEducation.dll` (package `SpacemanWil.WilsEducation`) | `Source/Education` | Teacher registration, classes, lessons, attendance, subjects, learning record, assessments, lectern button, `EducationMod`, `EducationDefOf`, removal step |
| `WilsCommunityCare.dll` (package `SpacemanWil.WilsCommunityCare`) | `Source/Care` | Healer registration, check-up rounds, clinic and appointments, outbreak round, check-up condition, `CareMod`, `CareDefOf`, removal step |

Each module project has exactly one `ProjectReference`, to the framework, and compiles only its own folder. `AssemblyBoundaryTests` reads the built assemblies and checks that the framework references no module, the modules never reference each other, no top-level type is in two assemblies, the framework carries no `InternalsVisibleTo`, and the module packages hold only their own DLL.

## 3. The dependency audit

The first audit (a scan of what each module's game code named) found eleven internal shared game types used by Education and nine by Community Care, eleven distinct names in all, plus the framework-owned DefOf and settings types. Each was handled by the smallest change that works; none was made public just so that code would compile.

| Type | Used by | Members needed | Why it was in shared code | Decision |
|---|---|---|---|---|
| `Activities` | both | `Now`, `GainMemory` | Game clock and mood helper used by dozens of Family Life files | Stays internal. `FrameworkServices.Now` and `GainMemory` forward to it |
| `Conversations` | both | `Run` | The cooperative professional exchange, used by both posts | Stays internal. `FrameworkServices.RunConversation`; its three-field result `ExchangeResult` became public |
| `DiagnosticSection` | both | the record | Nested in the old host | Public, nested in `FamilyFramework` |
| `FamilyModules` | both | the registries | The composition root, naming both modules | Replaced by public `FamilyFramework`; it names no module (section 4) |
| `Governance` | Education | `Free` | Governance helper (is this person free to be called away) | Stays internal. `FrameworkServices.IsFree` |
| `GovernanceMeetings` | both | `NextToken`, `Start`, plus **Teacher and Healer branches** | The shared "one pawn walks to another" job | Branches removed. `MeetingCatalog`/`MeetingDefinition` (a module registers its job, duration, preparation and completion). `FrameworkServices.StartMeeting`, `NextToken` |
| `HallDriver` | Education | `LooksLikeSchool` | The hall keeper tells a school room from a dining hall by furniture name | Stays internal (the hall keeper owns it). `FrameworkServices.LooksLikeSchool`; candidate to move into Education when the hall keeper leaves the framework |
| `OfficeDriver` (partial) | both | `Trace`, `Forced`, `Ready`, `Available`, `FreeFor`, `ForceUntil`, `WorkNow`, `Diagnose`, `NeedsReason`, `Served`, **plus the Teacher's and Healer's own halves of the class** | Interleaved partial class | Module halves moved out as `EducationDriver` and `CareDriver`; the shared members are reached through `FrameworkServices`. The post's fitness and passion skills are declared by the module (`RegisterFitness`, `OfficeDefinition.PassionSkills`) instead of a switch on Teacher/Healer in the framework |
| `OfficeEvents` (partial) | both | planning, beginning, notices, year | Interleaved partial class | Module halves moved out as `EducationEvents` and `CareEvents`; `FrameworkServices.PlanEvent`, `BeginPlannedEvent`, `RemoveNotices`, `Year`, `IsAprimay` |
| `PawnReadiness` | both | `StopReason`, `Level`, `Fatigue`, `Excusable`, `Danger` | Cached "should this pawn stop what they are doing" | Stays internal. `PawnReadinessApi` is a four-method public facade |
| `PerspectiveShiftBridge` | both | `AvatarId`, `IsAvatar` | Reflection bridge to the optional Perspective Shift mod | Stays internal. `FrameworkServices.AvatarId`, `IsAvatar` |
| `WFL_StoryDefOf` job fields | both | the module's own jobs | One DefOf for everything | Module jobs moved to `EducationDefOf` and `CareDefOf`, shipped with the module's own defs |
| `FamilyComponent`, `FamilyLifeMod.Settings`, `FamilySettings` | both | | Already public | Unchanged |

**Remaining couplings, all recorded in `ArchitectureTests` (they must only shrink):** the saved `OfficeKind` enum still names Teacher and Healer (a saved number); `RolePresentation` carries the role names and icons so a historic holder reads correctly with the module absent; the election's work-needed and house-wish tables in `CommunityOffices.cs` have a case per post; the camp "lesson" moment in `Flavors.cs` credits a teacher; the save reader's default kind. None of these executes Teacher or Healer behaviour. Moving the election tables into a post's definition is the next step before a third post can be added by a module without editing the framework.

`FrameworkServices` is deliberately small and says so in its header: if only one module needs something, it belongs in that module.

## 4. Initialization

1. **Framework** (`FamilyFramework`, first use): builds its registries, registers the framework module, and installs Family Life's own module (hall keeper, chaplain; still hosted here).
2. **Each optional module** announces itself from its own `Mod` constructor (`EducationMod`, `CareMod`) with one call, `FamilyFramework.Install(module)`. The game constructs mod classes in load order, and each module package declares `loadAfter` the framework, so the framework is ready. The framework never references a module assembly.
3. **Freeze**: after every mod constructor has run (a `[StaticConstructorOnStartup]` class), `FamilyFramework.Freeze()` validates the whole set once and closes registration. Nothing is added or discovered afterwards, and nothing is looked up by reflection per tick or per frame (the Harmony loader still reflects once, at start-up, over the framework assembly only).

`ModuleHost` (pure, tested) enforces: duplicate module ids are refused (the first is untouched); a module arriving after the freeze is refused; a module whose registration throws part way is marked broken, so everything it did register is ignored for the session; a module built for a different `ApiVersion`, one needing a capability nothing offers, and one requiring another optional module are switched off, not half-loaded. Duplicate offices, saved kinds, capabilities, planned events, meetings, work items and contributions throw at registration, naming both owners. Every problem is logged once with the identifier.

What a module registers: its post (`OfficeDefinition`, with the saved kind as its stable alias), autonomous work (one `WorkRoster` item; no loop of its own), planned events with their notices, a meeting (the job, duration, placement and completion), building buttons, developer diagnostics, a hand-over handler, a load reconciler, a reset, and a removal step. **Not built, because no second consumer exists to design against:** registration for Story Thread providers, settings sections, window tabs and narrative templates.

## 5. Record ownership by lifetime

State is kept where its lifetime says, not on whoever currently holds the post. Per-holder state starts fresh for a new holder (stamped with the holder id; nothing needs resetting when the post changes hands, even if the module was absent at that moment).

| Record | Lives | Why |
|---|---|---|
| Education: what each child studied (`LessonLog`), books used, recent subjects, last assessment year | Community (`wils.education`) | Belongs to the children; the next teacher inherits it |
| Education: the assessment in progress (ids, sittings, results of this sitting) | Holder (`wils.education.assessment`) | The outgoing teacher's sitting ends with them |
| Care: the round in progress, who was seen, missed, declined and until when, the last outbreak | Community (`wils.care.record`) | A new doctor continues an outstanding round; refusals stay respected; nobody is examined twice because the doctor changed |
| Care: when the next round is planned, the daily routine budget, the doctor's own favouritism record | Holder (`wils.care`) | About that doctor's own conduct |

(The earlier revision kept all of Care's state per holder; that discarded everyone's refusals and the missed list when the doctor changed. It is corrected and tested.)

**Hand-over** (`FamilyFramework.Handoffs`, raised by the registry whenever a post's holder changes or is lost): Education ends the outgoing teacher's class safely (attendance settled once, pupils released) and drops a queued class; Care closes the outgoing doctor's clinic. The former holder stops scheduling because the post no longer lists them.

**Absent module and the framework's own bookkeeping.** A post of a module that is not running can neither be done nor neglected. The framework now (a) does not count its work as unmet, so removing a module for a while does not end the holder's post as "Neglected", bar them or lower their house's standing, and (b) leaves it out of the yearly assembly instead of standing the holder down for want of candidates. Both are tested.

## 6. Lifecycle

Per save, in the family records (`FamilyRegistry.Lifecycle`, written for modules that are not installed too):

| State | Meaning | New work | Survives save/restart |
|---|---|---|---|
| Active | Running (also: no record) | yes | n/a |
| PreparationRequested | A preparation is under way | **no, from that moment** | If the save lands here it is read back as PreparationFailed |
| PreparationFailed | The last attempt did not finish; the report says why | no | yes, with the report and attempt count |
| Prepared | Cleaned; safe to remove | no | yes, until explicitly reactivated |
| Absent | The module's code is not installed (computed, not stored) | no | its saved state is untouched |

- **A prepared module stays inactive across restarts and reinstalls.** Loading, restarting or reinstalling never reactivates it; only `FamilyFramework.Reactivate` (the developer action "Reactivate ...") does, and it starts valid new work from a clean in-memory state.
- **Absence is not preparation.** Preparation needs the module installed (only its code knows what it owns); `PrepareForRemoval` on an absent module reports failure. A module removed without preparing leaves no claim that it was clean.
- Unknown lifecycle states from a newer version are read as stopped.

## 7. Removal preparation

Developer actions "Prepare Education / Community Care for removal" run `FamilyFramework.PrepareForRemoval`, which stops new work first and then runs the module's steps through a `CleanupReport`:

1. End every module job (current and queued) on everyone and release their reservations (`ModuleCleanup.EndJobs`); drop the queued class; reset in-memory sessions.
2. Close the round / stop the assessment, empty the class list, clear the waiting planned event and its letter, and give up the post (pure, tested: `EndEducationActivity`, `EndCareActivity`).
3. Remove only the module's own hediff and memories (`WFL_CheckedUp`, the lesson/assessment and check-up memories).
4. **Verify**: count what is left (pawns on module jobs, memories, hediffs, notices, post still holding work). Any remainder throws, so the report says "NOT prepared", the lifecycle becomes PreparationFailed, the report is stored in the save, and the step can simply be run again. A repeated run on a prepared module changes nothing; if a repeat fails, the module is reported as not ready.

Not undone, by design, and listed in each report: skill experience already given (ordinary skill progress); health already changed; the neutral records (what was studied, who was seen); the framework's role icon/name for a historic post; a class cut short is settled once for the time attended, never paid twice.

## 8. Absence, preparation and arbitrary removal

| | Temporary absence (module not installed, not prepared) | Prepared removal |
|---|---|---|
| Saved payloads | Preserved untouched (round-tripped in tests) | Active work cleared; neutral records kept |
| Framework records | Deserialize without module code (payloads are opaque text; saved letters carry only an int kind) | Same |
| The post and its holder | Kept as history; not neglected, not put to the vote | Post given up |
| Returning module | Validates its payload (a damaged payload falls back to an empty record, tested); reconciles on load: clears the class list and in-memory sessions, prunes people no longer present from the care record; never resumes a session blindly | Stays stopped until explicitly reactivated; cancelled sessions do not return |
| Defs in the save | Unresolvable job, hediff and thought defs of an absent module are dropped by RimWorld with warnings (expected, **not observed**) | Avoided: they were removed first |

**Arbitrary removal of a module from a running save without preparation is unsupported.** It is expected to load, with warnings for the defs, but pawns mid-class, memories and the check-up condition are exactly what preparation exists to clear. Say so to users (INSTALLATION.md does).

## 9. Packages

| Package folder | packageId | Requires | Ships |
|---|---|---|---|
| repository root, built to `dist/WilsFamilyLife` | `SpacemanWil.WilsFamilyLife` (**name kept**: existing installs know it) | Harmony | `FamilyLife.dll`, shared defs, role icons, shared strings, patches. This is the framework **and** Family Life until Family Life is extracted |
| `Packages/WilsEducation` | `SpacemanWil.WilsEducation` | Harmony, the framework | `WilsEducation.dll`, its 4 jobs and 5 thoughts, 30 strings |
| `Packages/WilsCommunityCare` | `SpacemanWil.WilsCommunityCare` | Harmony, the framework | `WilsCommunityCare.dll`, its 3 jobs, 1 hediff, 2 thoughts, 11 strings |

All three declare 1.6 and 1.7 and `loadAfter` Harmony and the framework; the module packages never name each other. A def lives in exactly one package (tested). `scripts/Build-Packages.ps1` assembles and checks `dist/`; `scripts/Stage-ToRimWorld.ps1` copies chosen packages into the game (`-FrameworkOnly`, `-Only`). A separate `SpacemanWil.WilsCommunityFramework` package id is deferred until Family Life is extracted; introducing it now would break every existing mod list for no gain.

## 10. Performance

- A stopped or absent module schedules nothing: its work items stay on the shared roster but are skipped by an enabled check; an absent module has no items at all. There is no second scheduler, no extra colony scan, and no per-tick module discovery.
- Measured offline on this build machine (not in the game, so indicative only): one roster pass with every module stopped costs about 1 microsecond.
- No TPS improvement is claimed. Moving code into assemblies changes where it is compiled, not how much work runs. Active modules do the same work as before. Payload growth is bounded as before (the lesson log and assessment lists keep their existing caps); no new unbounded history was added.

## 11. Verification: what is evidence of what

**Pure/domain tests (1,096 on this branch, `dotnet test`).** Registration order and refusals (`ModuleHostTests`: framework only, +Education, +Care, both in either order, duplicates, late arrival, wrong API, missing capability, failure part way); lifecycle persistence, failure, retry, idempotence, reload (`ModuleLifecycleTests`); absence versus preparation, hand-over, record ownership (`ModuleAbsenceTests`, `ModuleStateTests`); migration of synthetic legacy data; the architecture tests.

**Game-assembly compilation.** `FamilyLife`, `WilsEducation` and `WilsCommunityCare` build for 1.6 (net472) and 1.7 (net10) with no `InternalsVisibleTo`; the 25 patch classes check against the game's assemblies. This proves the modules use only the framework's public surface.

**Package/static validation.** `Build-Packages.ps1` and `AssemblyBoundaryTests` (assembly references, type ownership, no duplicates, package contents). Static inspection; nothing is loaded.

**A real pre-extraction save, offline.** `RealSaveMigrationTests` (opt-in via `WFL_REAL_SAVE`) read the office records of five real saves from this machine (written before module payloads existed): the Healer's legacy fields (`nextRound`, `roundStart`, `examined`, ...) and, in four of the five, the Teacher's, migrate into the modules' payloads exactly once, read back identically after a flush and reload, and migrating twice changes nothing. **This used the migration's own label list and the modules' state code on the saves' XML. It did not run RimWorld's loader.** The save's Teacher had no lesson log or assessment in progress, so those paths were not exercised by real data.

**Not observed anywhere, and not claimed:** that the packages load; that Education works alone; that a class starts, finishes, pays XP once and releases pupils; that an assessment survives a save; that preparation ends a live class; that a prepared save loads without the module; that reinstalling does not resurrect work; that the legacy reader runs in the real loader; that unresolvable defs of an absent module only warn. [MODULE-VERIFICATION.md](MODULE-VERIFICATION.md) lists the steps for each.

## 12. Planned roles (documented, not implemented)

The extraction boundary is still moving, so no new role is being added. When they are, they use the same registration, scheduling, knowledge, authority, persistence and cleanup contracts as above, with no new framework hooks specific to them.

- **Education**: Teacher (built), **Training Instructor** (planned; practical skills as distinct from schooling; needs a post definition, a work item, a meeting or session, its own community record and holder state, a removal step, and an election-policy entry in the post's definition rather than the framework's tables).
- **Community Care**: Healer (built), **Counselor** (planned; mood and relationships support; same shape; it must go through the existing cooperative exchange and the daily contact limiter, not around them).

## 13. Remaining work toward Family Life extraction

1. Move the election work-needed and house-wish tables, the camp-lesson credit and the role presentation rows into post definitions, so a module can add a post without touching the framework.
2. Give the hall keeper and chaplain a home (Family Life) and move `HallDriver.LooksLikeSchool` out of the framework.
3. Extract Family Life's remaining systems (meals, traditions, parenting, estates, crime, institutions ...) behind the same contracts; the registry partial classes are the hard part (each system's state needs the payload treatment).
4. Introduce `SpacemanWil.WilsCommunityFramework` once Family Life leaves the base package, with a migration note.
5. Registration contracts for Story Thread providers, settings sections and window contributions, designed against the first real consumer.
6. Play-test everything in section 11's "not observed" list.
