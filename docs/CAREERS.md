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

## Hands-on testing, in priority order (nothing here has been run)
1. **Load an old save** (all three mods): no errors in the log; the `Modules:` line lists all; the old Teacher and Healer show as assigned in their pawn tabs; classes and check-ups continue; the Healer is still titled Healer.
2. **Classes.** Two assigned Teachers and two classrooms: two classes at once with different pupils, nobody in two (`class:` trace lines). One room only: the second teacher waits ("room already in use"). A teacher with no classroom gives one-to-one lessons.
3. **Assessment letter.** In Aprimay one letter names every teacher with assessments due. Test Begin; Put off a week (no restart for 7 days, then a new letter); Skip (nothing for the year); ignore it (begins after a day). A pupil taught by two Teachers is assessed once per subject. Kill a Teacher mid-assessment: another takes it. Save/reload mid-assessment.
4. **Assistance.** A Teacher's class with a free Tutor: the Tutor walks in, is counted, and earns career points afterwards. With a qualified specialist for that subject: the specialist helps and the delivery bonus appears once; draft the Teacher mid-class: the specialist takes over. A Tutor never takes over.
5. **Care basics.** Assign a Doctor: the "Nurses" message once; unassign: still Nurses; reload: still Nurses. Assign several workers in a hospital colony: more check-ups a day, no patient seen twice, emergencies still first. An unqualified Nurse's check-ups cap at Fair; their tending is unaffected.
6. **Experience from vanilla work.** Perform an operation on a colonist and tend an animal with a candidate: the Professions section shows specialty experience rising; the operation's success is unchanged.
7. **Specialties.** Counselor: a low-mood colonist is talked to (memory appears; a quarrel may be reconciled sometimes, not always). Surgeon follow-up on an implant carrier. Veterinarian examines a tamed animal. Midwife (Biotech): stays with a woman in labour and the mother gets the memory; without Biotech nothing happens.
8. **Qualification flow.** Reach 100 experience with Medicine 6: the qualification message appears and nothing is assigned automatically; the progress bars and skill lines are correct.
9. **Assembly.** No Teacher/Healer race; recognition lines appear; the postpone button moves the date by 30 days.
10. **Save, reload and repeat** each of the above; look for the log warnings "listener was skipped", "could not attach", "could not be settled".
