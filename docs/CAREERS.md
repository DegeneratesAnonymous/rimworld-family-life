# Professional careers (branch `feature/community-careers`)

Status: **offline tests and compilation only. Nothing here has been observed in RimWorld.** Based on a66953b; not staged, not merged.

## Three separate things
| | What it is | Where it lives | Who decides |
|---|---|---|---|
| Qualification | what a pawn is capable of | `CareerLedger` (payload `wils.careers`) | earned from skills + experience, kept for good |
| Assignment | what the player has them doing | `AssignmentBook` (payload `wils.assignments`) | the player; any number of holders |
| Community office | political / ceremonial standing | `CommunityOffice` | the community (unchanged so far) |

Both payloads sit in the registry's `SharedState` (versioned, opaque, kept for modules that are absent, saved with the family records).

## Career rules (`Careers.cs`)
- Level 1: no requirement. Level 2: skill minimums and general experience. Level 3: Level 2, specialty skill, specialty experience (may be banked before qualifying).
- Experience is credited once per activity key; the first `FullPerDay` (8) activities of a kind in a day earn full points, then each repeat earns `RepeatFalloff` (5%) less, never below `MinShare` (50%); `SafetyCapPerDay` (600, 0 = off) is only a backstop. All are configurable statics. Dedup keys are dated and forgotten after 60 days (max 2,000 kept).
- Qualifications are never removed (skill decay, changed definitions). A lowered bar is met at the next check.
- Careers of a module that is not installed stay in the ledger untouched.
- Existing pawns get no invented history. Holders of the old Teacher/Healer posts are *grandfathered*: assigned, and given the Level 2 qualification marked legacy, with zero experience.

## Legacy migration
`LegacyProfessionMigration` runs from the maintenance roster. For each held Teacher/Healer office it assigns the holder once (key `office:<kind>:<holder>`), without touching the office. A later player unassignment is not undone. A new holder is migrated on their own.

## Working seat
The old Teacher/Healer office records still carry shared working state (the community record, assessment, per-holder care budget). `FamilyRegistry.SeatService` puts an assigned professional in that seat (same hand-over hooks as a change of post, no vote). `ProfessionalSeating.Pick` keeps the sitting person while assigned and present, otherwise a colleague covers. Teacher: seat follows Teacher role holders. Care: Doctors first; with no Doctor assigned, Healer-role holders carry on.

## Educators (offline only)
- Roles: Tutor (L1), Teacher (L2: Social 4, 120 experience), twelve subject specialists (L3: Teacher + skill 12 in the subject + 150 subject experience + 400 overall). Constants in `EducationCareers`.
- **Parallel classes.** Each class is a `ClassBoard` session (instructor, assistants, pupils, subject, place); nobody is in two at once and a room hosts one class. Each instructor has their own schedule (`TeacherSchedule`). Teachers and specialists hold classes; with no classroom a teacher gives one-to-one lessons like a tutor; a specialist-only instructor teaches only their subject.
- **Assistance (at most two helpers per class, only free, idle pawns not at a real need).** A specialist for the class's subject helps first: if present for at least half the instruction the class gets one 10% delivery bonus, whatever the number of specialists; if the instructor cannot continue before 60% of the instruction is done, a specialist takes over with the pupils still present. A Tutor helps with the room: present for half the instruction, the pupils' engagement gets one +0.4 lift, whatever the number of tutors. A Tutor never leads a group class or takes over (only assigned Teachers and specialists are instructors). Helpers earn 6 career points in the subject.
- **Assessments are per teacher, announced once.** `AssessmentLedger` holds each teacher's own assessment, when they last held one, who postponed, and which (pupil, subject, year) were assessed. In Aprimay one consolidated letter lists every teacher with assessments due, with **Begin**, **Put off a week** and **Skip this period**. It does not hold anything up: unanswered for a day, the assessments begin. Postpone delays only the eligible teachers a week, then a new letter is sent; Skip lasts the year; neither restarts by itself. The owner of a (pupil, subject) is the teacher who gave most of it (ties to the lowest id; if that teacher is gone or the lesson predates teacher names, the lowest-numbered current instructor). Several teachers' lessons in one subject are merged and assessed once. An assessment whose teacher leaves passes to another. Grading, sittings, make-ups, mood effects and XP are unchanged. The lectern "Begin annual testing" button remains.
- **Migration.** The old Teacher post's lesson log, book use, recent subjects, assessment in progress and last-assessed year are copied once into `EducationRecord` / the ledger (marker per holder); the old data is left in place.

## Community Care (offline only)
- Roles: Healer (L1, shown as Nurse after the first Doctor is assigned) and Doctor (L2: Medicine 6, 100 experience). Healers, Nurses, Doctors and specialists all keep doing routine check-ups; qualification never overrides vanilla skill or tending (a Nurse with Medicine 15 does a check-up better than a Doctor with Medicine 6). The Fair ceiling applies only to the check-up condition of an unqualified worker, never to tending, operations or any vanilla chance.
- Legacy Healers keep the Healer assignment and the elected post, gain Doctor qualification (legacy, no experience) and are not made Doctors. The first Doctor the player assigns sets the Nurse terminology (`wils.care.terms`) for good. The old working seat follows Doctors, then specialists, then Healers.
- **Capacity scales.** One fully able worker has the old budget (4 routine visits a day, 6 in an outbreak). The community limit is that budget times the combined capacity of assigned care workers who can work now (manipulation/consciousness; 0 if barred from doctoring); each worker has their own daily limit and gap. The round window shortens for big staffs. Rounds stay finite (each person once), so more staff finish rounds sooner rather than creating extra examinations.
- Claims: a patient is seen by one worker at a time and a bed hosts one; claims expire. Refusals are per worker (lapse when that worker stops serving) or service-wide.
- **Experience from real work.** Two read-only hooks (owned by the framework, postfix only) credit vanilla work: a completed operation (surgeon experience for people, veterinarian experience for animals) and a tend (veterinarian experience for animals; a little general care experience for people). They never change the operation, its success chance or the tend. Doing this work needs no assignment and no qualification.
- **Specialties** (Doctor first, then skills and 60 specialty + 150 general experience; a Doctor with the skills may *practise* a specialty to earn its experience, with Community Care's own service effects capped at Fair; specialists keep routine care and are not capped):
  - Counselor (Social 8): a supportive talk with someone whose mood is below 40% or who has an open quarrel (grief first). Whether it helps depends on the counselor's Social, how much the person trusts them, and grief; it leaves a good or modest 3-day memory and nudges the bond between them. If it helped and the person has an open quarrel, it can encourage a reconciliation using the quarrel's own rules (counselor skill adds up to 15 points to the chance); a failed encouragement costs nothing. Nothing removes a mental state or a thought.
  - Surgeon (Medicine 10): experience from completed operations, plus a follow-up visit to someone with an implant or missing part. Performs no operation: surgery stays the game's own.
  - Midwife (Medicine 8, Biotech): stays with someone in labour (the birth itself stays the game's own; the mother remembers being attended) and visits the pregnant and postpartum. Without Biotech nobody is eligible.
  - Veterinarian (Medicine 6, Animals 6): experience from tending and operating on animals, plus examinations of tamed animals.
  - **Geneticist: deferred.** Planned: Medicine 10 and Intellectual 10, Doctor first. Biotech opportunities to investigate (none built, none assumed): xenogerm and gene-extraction work and the genes a pawn carries (the Gene Extractor and Xenogerm Forge are vanilla buildings and bills), and the gene-related health conditions. Any activity must wrap an existing vanilla bill or condition, as Surgeon wraps operations; a role with no such activity will not ship.

## Assembly
Teacher and Healer are never elected, reviewed or neglected out of their seat once `ProfessionsAssigned` is set. It is the Community Assembly; political posts, the hall keeper and the chaplain are unchanged. Qualifications earned since the last assembly are recognised in its opening letter (once each; purely a notice). It can be postponed a month from the Families window between assemblies; this affects no career, class, assessment or round. Renames are display-only; package ids are unchanged.

## Pawn tab (Professions)
Per profession: assigned / qualified / open to anyone / what is missing, with Assign and Unassign. Per career the pawn has taken part in: general experience against the next threshold with a bar, each required skill as have / need (green when met), the specialties once they could matter with their own specialty-experience bars, and the five most recent qualifications. A message appears when someone qualifies. No separate history window.

## Save compatibility
- All new state is in the registry's `SharedState` payloads (`wils.careers`, `wils.assignments`, `wils.education.record`, `wils.education.assessments`, `wils.education.schedule`, `wils.care.terms`): versioned, kept untouched while their module is absent, and never read by the framework.
- Legacy Teacher/Healer holders are migrated once (marker per holder); the old office records and their payloads are left in place and still read.
- `ProfessionsAssigned` is applied on load, not saved. A colony loaded without this mod's career code keeps its offices.
- The removed assessment planned-event notice leaves a possible stale `PendingKind` on the old Teacher office; it is cleared by the migration.
- Concerns: lesson-log entries now carry a trailing teacher id (older readers ignore it); the assessment is no longer a framework planned event, so an older build loading a new save will not offer it.

## Known limits
Geneticist; the old Teacher/Healer office record still exists for compatibility and per-holder care state; Doctors' practice may feel slow because specialty experience comes only from specialty activities and real operations/tending; no progress UI outside the pawn tab; none of the in-game behavior has been observed.

## Runtime readiness (pre-validation review)

Feature work is frozen. Nothing in this section has been observed in RimWorld.

### Concrete risks found and fixed (commit `366d78f`)
1. **New games kept voting for Teacher and Healer.** `ProfessionsAssigned` was only set when a save was loaded or saved, so a brand-new colony elected them until its first save. It is now set from the first tick (and again at `FinalizeInit`).
2. **A stuck claim could block a care worker for hours.** `CompleteCheckup` returned early when the round had closed during a visit, before releasing the patient claim; the worker could not take another patient until the claim expired (about 2.4 game hours). The claim is now released first.
3. **Experience for non-colonists.** The new operation/tend hooks credit whoever performs the work; a raider or a passing guest who tended someone would have gained a ledger entry. Only the colony's own people build a career now.
4. **Long medical side jobs ignored colony emergencies.** The midwife's labour attendance (up to ~2 game hours) and the veterinarian's examination ended for the worker's own needs but not for "someone needs tending". Both now end when `CareDemand.DoctorShouldTend` says real medical work is waiting.
5. **A class whose teacher never reached the class job would hold its pupils and room forever.** A sweep (`ClassSessions.EndStale`) now ends such sessions two game hours after they start.
6. **Career and assignment data of people the family records never knew was never cleaned.** Both reconcilers existed but nothing called them; they now run once per loaded game and log what they did.

### Reviewed, no change needed (reasons)
- Harmony targets and parameter names: see below.
- Class cleanup: the teacher job's finish action ends the session, closes the board, releases assistants and settles attendance once; load clears all sessions (`Reset`).
- Assessment handoffs: ownership is a pure function of the shared lesson record and the current instructor list; an orphaned assessment is transferred; ledger and record are saved payloads.
- Claims: `CareClaims` is atomic per call (single-threaded game), expires, and `ReleaseAll` runs on hand-over, reset and clinic close.
- Stale pawn references: every use goes through `TryGetPawn` plus `Spawned/Dead` checks, or inside a job whose `FailOn` removes the job first.
- DLC: Biotech conditions are looked up by name through `DefDatabase` and gated by `ModsConfig.BiotechActive`; without Biotech the midwife has no targets and nothing errors.
- Assembly: only the Teacher and Healer kinds are excluded; other posts and the vote are untouched; the postpone button only moves `NextAssemblyTick`.

### Remaining risks that only the game can settle
- Whether the two Harmony postfixes attach and fire (below).
- Whether a vanilla operation's iteration-complete call happens exactly once per operation and also for botched ones. The code treats it as "an operation was performed"; key = bill id + doer + tick, so a duplicate call on the same tick cannot double-award.
- Surgery counts any vanilla surgery recipe (implants, amputations, harvesting). Repeated operations are subject to the repetition formula, nothing else.
- The new jobs (`WFL_ClassAssist`, `WFL_VetExam`, `WFL_AttendBirth`, `WFL_SpecialistVisit`) are started with forced priority on idle pawns, like the existing class and clinic jobs; how they sit with each player's work priorities needs watching.
- Two classes in one room is prevented by a per-room claim; two teachers will only run at once if the colony has two classrooms (or a classroom and open ground).
- The `Defs` for the new jobs, letter and thoughts are version-neutral XML; a typo would only show as a red error at load.

### Medical hooks
Targets, verified against the 1.6.4871 and 1.7.4990-unstable reference assemblies by reading their metadata:

| | 1.6.4871 | 1.7.4990-unstable |
|---|---|---|
| `RimWorld.Bill_Medical.Notify_IterationCompleted(Pawn billDoer, List<Thing> ingredients)` | present, public virtual, declared on `Bill_Medical` | same |
| `RimWorld.Bill_Medical.GiverPawn` | present | same |
| `RimWorld.TendUtility.DoTend(Pawn doctor, Pawn patient, Medicine medicine)` | present, public static | same |

Our postfixes bind `__instance`, `billDoer`, `doctor`, `patient`; those parameter names match both versions (Harmony binds by name). They are postfixes only, wrapped in try/catch, owned by the framework assembly (the architecture test forbids module patches) and call only listeners of running modules. They attach with the framework's other hooks.

**Not verified until seen in the game.** What to observe:
1. In the log after load: `Loaded (N hooks ...)` with no `Skipped hook Patch_OperationCompleted` or `Skipped hook Patch_TendDone`. (If a patch were skipped, no experience would be credited and nothing else breaks.)
2. Dev menu "Wil's Family Life, testing" -> "Careers: show status". Line `Hooks seen this session: operations 0, tends 0, ...`.
3. Have a colonist tend an injured colonist: `tends` rises by one per tend. Have them operate (any surgery bill): `operations` rises by one when the operation completes; `operations credited` rises by one; the pawn's Professions section shows Surgeon experience (10 points, or 12 before rounding by the repetition formula). A tamed animal tended: `animal tends credited` rises and Veterinarian experience appears.
4. Repeat the same operation on the same bill: each completed iteration is one credit, not two.
5. A raider or guest tending someone: no ledger entry.
Do not describe the hooks as working until step 3 has been seen.

## Test packages
Built from commit `366d78f`; the staged candidate (a66953b) is untouched (its installed DLL hashes differ from these).

`C:\Users\colem\Downloads\rimworld-family-life-testpkg\careers-366d78f\`
- `both\` - the three packages with both version folders (what the Workshop layout looks like)
- `1.6-only\`, `1.7-only\` - the same with the other version's assemblies removed
- `careers-366d78f-both.zip`, `-1.6-only.zip`, `-1.7-only.zip`
- `SHA256SUMS.txt` - zip hashes and each assembly's hash

The three packages share package ids with the installed ones (`SpacemanWil.WilsFamilyLife`, `.WilsEducation`, `.WilsCommunityCare`), so **they cannot be enabled together with the staged copies**. Two safe ways to test, neither performed by me:
1. **Separate game copy (recommended).** Copy the RimWorld folder (or use a second Steam library install), put only these three folders (plus Harmony and, if wanted, other mods) in that copy's `Mods`, and start it with `-savedatafolder=C:\RW-careers-test` so saves and config are separate.
2. **Swap in place.** Move the three installed folders (`WilsFamilyLife`, `WilsEducation`, `WilsCommunityCare`) to a backup folder outside `Mods`, copy the test folders in, test, then reverse. Back up your saves first; do not load the staged runtime-test save with these builds, since the staged candidate's own validation is separate.
Mod list order: Harmony, Core/DLC, Wil's Community Framework, Wil's Role Modules : Education, Wil's Role Modules : Community Care. Enable Development mode and, in Mod Settings, "Log family events to the dev log".

## Diagnostics (all low-overhead; per-event, never per-tick)
Always written: module registration, migration, qualifications, load reconcile, failures.
- `careers: wils.education registered: 1 career(s), 14 role(s)` and `careers: wils.care registered: 1 career(s), 6 role(s)` - module registration.
- `careers: loaded: N assignment(s); reconciled X career record(s) and Y assignment(s) ...` - once per loaded game; a failure reads `load reconcile failed, data left as it was`.
- `careers: migrated legacy post (office:Teacher:<id>) ...` - legacy migration ran for that holder.
- `careers: <name> qualified: wils.education.teaching` (or `...:Melee`, `wils.care.practice`) - a qualification was awarded.
- `Loaded (N hooks ...)` and `Skipped hook Patch_...` - hook attachment (framework line).
Written when "Log family events" is on:
- `careers: <name> +N <career>[:specialty] (<kind>)` - experience awarded (kinds: `teach.class`, `teach.assist`, `teach.assist.tutor`, `teach.assessment`, `care.checkup`, `care.tend`, `care.surgery`, `care.tend.animal`, `care.specialty.<X>`, `care.birth`).
- `careers: <name> assigned to / unassigned from <role>`; `careers: <Kind> working seat: a -> b`.
- `careers: class: <teacher> begins a class (<subject>, N pupil(s), place ...)` / `finished a class ... (completed | ended early, N pupil(s) learned)` / `ending a stale class ...`.
- `careers: assessment: <teacher> begins for N pupil(s)` / `finished; J judged, S subject record(s) spent`.
- `careers: care: <worker> claimed <patient> for a visit | for the clinic` / `finished seeing <patient>` / `clinic claim on <id> released`.
Dev menu "Careers: show status" prints roles, hook counters, and every colonist's assignments, qualifications and experience. "Show module status" (existing) lists module registration and any absent module.
Existing developer actions still apply: "Start a class now", "Start annual testing now", "Start a check-up round now".

## Progression estimates (from the actual awards; balance NOT changed)
A game day is 60,000 ticks; a game year is 60 days. Points: class 10 per attending pupil x attendance share x delivery (0.4-1.2, typically ~0.8, so ~8); one-to-one lesson ~4; check-up 8 + quality (0-3); operation 12; counselling talk 10; vet exam 10; animal tend 4; human tend 2; birth attendance 15; class assistance 6. The first 8 awards of one kind per day are full; then -5% each, floor 50% (rarely reached outside a hospital/school day).

| Step | Needs | Realistic activity | Estimate |
|---|---|---|---|
| Tutor -> Teacher | Social 4, 120 | a free tutor gives about 2-4 lessons a day (~4 points) plus 6 for each class they help (a class every 3 days per teacher) -> ~10-20 points/day | 6-12 days. Slower with no idle child or tutor. |
| Teacher -> Subject Specialist | skill 12, Teacher, 400 general, 150 in that subject | 6 pupils x ~8 = ~48 points per class, one class per 3 days, subject rotating among the 3-4 the teacher can teach | general 400: ~25 days; 150 in one subject: ~10 classes of rotation = ~30 days, i.e. roughly half a year; ~1.5-2 years with only 2-3 pupils. The skill 12 requirement is usually the real limit. |
| Healer/Nurse -> Doctor | Medicine 6, 100 | check-ups: ~one visit per colonist per round of 14-40 days shared by all workers (an 8-person colony ~0.5 visits/day for everyone, ~9 points each); tends: 2 points each, up to ~16/day while patients exist | quiet colony: 3-6 weeks for one worker; with injuries or several patients a day: 1-2 weeks |
| Doctor -> Counselor | Social 8, 150 general, 60 counselling | a talk (10) for anyone with mood < 40% or an open quarrel, per person every 3 days | 6 talks: 1-3 weeks if people are unhappy, otherwise it may stall |
| Doctor -> Surgeon | Medicine 10, 150 general, 60 surgery | 12 per completed operation | 5 operations; ordinary colonies do a handful a year (1-3 years), a hospital colony weeks |
| Doctor -> Midwife (Biotech) | Medicine 8, 150 general, 60 | 15 per birth attended, ~10 per prenatal/postpartum visit (every 3 days per mother) | one pregnancy gives ~50; two births qualify. Rare in a small colony. |
| Doctor -> Veterinarian | Medicine 6 + Animals 6, 150 general, 60 | 10 per exam (a given animal again after ~20 days), 4 per animal tend, 12 per animal operation | ~10 animals: about 2 weeks; 3 animals: about 6 weeks |

### Balance concerns and proposals (none applied)
1. A Teacher cannot choose to teach a subject, so subject experience before qualifying is slow and random. Proposal: let a Teacher with the skill practise focused classes, as Doctors practise specialties.
2. Check-up experience scales with colony size and is nearly absent in tiny colonies. Proposal: slightly more points per check-up for small colonies, or count more tending.
3. Surgeon experience is gated on how often a colony operates. Proposal: lower the specialty requirement or count surgical follow-up more.
4. Counselor can stall in a happy colony. That is the intended behaviour; keep unless testing shows it is never reachable.
5. Tutors earn slowly if there are no children who can learn something from them (the gap rule). Proposal: allow tutoring adults when the setting is on (already supported) and say so in the tab.
6. The repetition formula almost never bites in ordinary play; consider shortening the full-allowance only if a hospital or school day shows runaway progress.

## Hands-on validation plan (nothing here has been run)
Conventions: "enable log events" = Mod Settings -> Wil's Community Framework -> "Log family events to the dev log". Use Development mode and the dev menu category "Wil's Family Life, testing". A test passes only if every Expected line is observed. "Reload" means save, quit to the main menu, load again.

### Phase 0 - foundations (do all of these before anything else)
**T0. Start-up and attachment.** Colony: none (main menu then any save). Actions: start the game with the three mods; open the dev log. Expected: `[Wil's Family Life] Loaded (N hooks...)` with no `Skipped hook`; `Modules: wils.framework, wils.familylife, wils.education, wils.care`; `careers: wils.education registered: 1 career(s), 14 role(s)`; `careers: wils.care registered: 1 career(s), 6 role(s)`; no red errors from the mods. Pass: all lines present. Reload: no.
**T1. Brand-new tribal start (small colony).** Colony: new game, Tribal, 3 colonists plus (dev mode) spawn one child and a second adult. Actions: play one day. Expected: no Teacher/Healer chosen by a vote (no "assembly"/election letter; the Families window shows no Teacher/Healer holder unless you assigned one); open a colonist's Family tab: a "Professions" section lists Tutor, Teacher, Healer and Doctor roles with Assign buttons for Tutor and Healer only. Pass: no election; section present. Reload: yes - repeat the section check afterwards.
**T2. Assign and unassign.** Actions: assign the adult as Tutor and the second adult as Healer in their Family tabs. Expected: log (events on) `assigned to wils.education.tutor` / `wils.care.healer`; status action "Careers: show status" lists both. Unassign and reassign. Pass: log lines and status agree; Teacher and Doctor show "needs ...". Reload: yes; assignments persist.
**T3. Legacy migration.** Colony: an existing save from the staged candidate (copy it; never alter the original) with a Teacher and a Healer in place. Actions: load it with the test build. Expected: `careers: migrated legacy post (office:Teacher:<id>) ...` and `(office:Healer:<id>)`; both pawns show Teacher/Healer assigned and a legacy qualification; the Healer is still titled Healer; classes and check-ups carry on. Pass: both lines, both assignments, no errors, and a second reload prints no migration lines. Reload: yes (twice).
**T4. Missing module.** Actions: disable Education only, load a save that has a Teacher. Expected: one letter naming the missing module; no errors; re-enable, reload: Education data intact (assignments, experience). Pass: data back after re-enabling. Reload: yes.

### Phase 1 - Education
**T5. One-to-one tutoring (tribal).** Colony: tribal, one Tutor-assigned adult with a skill 3+ levels above the child, one child; no classroom. Actions: wait. Expected: the Tutor goes to the child roughly every 2 game hours when both are idle; the child's skill gains XP; events log `+N wils.education.teaching (teach.tutor)`. A tutor who knows less than the child never tutors. Pass: lessons happen, XP rises, no lesson for a subject the tutor is not better at.
**T6. One class (ordinary colony).** Colony: ordinary, a built classroom (lectern or blackboard in an enclosed room with desks), one qualified Teacher (use "Careers" status, or dev-edit Social/skills and award experience by holding classes), 4 children. Actions: "Start a class now". Expected: `class: <teacher> begins a class (<subject>, 4 pupils...)`; children go to seats; breaks between blocks; at the end `finished a class ... completed, N pupil(s) learned`; pupils gain skill XP; the Teacher gains experience lines. Pass: all of the above. Reload: yes, mid-class: the class simply does not continue and nobody is stuck (no pupil stays on the class job).
**T7. Two classes at once.** Colony: two classrooms, two assigned Teachers, 6 children. Actions: "Start a class now". Expected: two `begins a class` lines with different places and disjoint pupils; no pupil in both; a third attempt in an occupied room waits. Pass: disjoint pupils. Reload: yes.
**T8. Assistance.** Colony: T6 plus an idle Tutor and, if possible, a qualified subject specialist. Actions: start a class. Expected: the Tutor walks into the room and stands near the teacher; at the end events log `teach.assist.tutor` experience for the Tutor; with a specialist for the same subject, `teach.assist` and the class is settled with the bonus; drafting the Teacher mid-class lets a specialist (never a Tutor) take over (`takes over the class`). Pass: as listed.
**T9. Assessment letter.** Colony: T6 after several classes, in Aprimay (or use "Start annual testing now"). Expected: one letter listing every teacher with Begin / Put off a week / Skip this period. Begin: `assessment: ... begins for N pupil(s)`, sittings in the daytime, a result letter, `finished; J judged`. Run it again on fresh data for Put off (no restart for 7 days, then a new letter) and Skip (nothing for the year). Ignore the letter for a game day: the assessments begin. Pass: every path behaves; no pupil assessed twice for a subject. Reload: yes, mid-assessment.
**T10. Qualification flow.** Colony: a Tutor with Social 4. Actions: let them accrue 120 points. Expected: `careers: <name> qualified: wils.education.teaching` and an in-game message; the tab now shows Teacher "qualified, not assigned" (never auto-assigned); the progress bars and skill lines match. Pass: message once, no auto-assignment.

### Phase 2 - Care
**T11. Healer check-ups (tribal).** Colony: tribal with one Healer-assigned colonist and 3 others. Actions: "Start a check-up round now". Expected: visits one by one; each ends with `care: <worker> claimed ... for a visit` then `finished seeing ...`; the "recently checked up" condition appears; a patient who refuses is left alone. Pass: no patient seen twice in the round. Reload: yes.
**T12. Doctor and Nurse naming.** Actions: qualify or use the legacy Healer, assign them as Doctor. Expected: message "now called Nurses", Healer-role holders are titled Nurse; unassign the Doctor: still Nurses; reload: still Nurses. Pass: all three.
**T13. Several care workers (profession-heavy colony).** Colony: 12 colonists, 5 assigned as Healer/Nurse/Doctor, a medical room with 3 beds. Actions: "Start a check-up round now". Expected: visits happen in parallel; claims logged with different workers/patients; no patient claimed by two workers; more visits per day than T11; a colony emergency (injure someone) pulls workers off routine visits. Pass: no double claims; emergencies first. Also check capacity: injuring one worker's hands lowers only that worker's share.
**T14. Experience from vanilla work.** Actions: see "Medical hooks" steps 3-5 above. Pass: counters move, no duplicates, no entry for a non-colonist.
**T15. Specialties.** Colony: profession-heavy with a Doctor that has the skills. Counselor: a low-mood colonist is talked to (memory appears; a quarrel sometimes reconciles). Surgeon follow-up on an implant carrier. Veterinarian exams a tamed animal. Midwife (Biotech): stays with a woman in labour (mother gets the memory). Expected log: `care.specialty.<X>` experience. Pass: each activity happens on an eligible target only; nothing else changes. Without Biotech the midwife does nothing and no error appears.
**T16. Assembly.** Colony: any, at the yearly assembly date. Expected: "Community Assembly" wording; no Teacher/Healer race; recognition lines for qualifications earned that year; the postpone button moves the date 30 days and affects no class or round. Pass: all of the above.

### Phase 3 - soak
**T17. Long run.** Ordinary colony, 10+ game days with classes, check-ups and some injuries, then reload twice. Expected: no growth of warnings; `careers: loaded: ...` each load; status action stable; no pawn stuck on a class/clinic/assist job; the dev log free of `listener was skipped`, `could not be settled`, `load reconcile failed`. Pass: clean log and no stuck pawns.

### Reading a failure
Look first at: `Skipped hook`, `listener was skipped`, `A specialty visit's effect was skipped`, `The class could not be settled`, `load reconcile failed`, `ending a stale class`. Report the exact line, what the pawns were doing, and whether a reload changes it.
