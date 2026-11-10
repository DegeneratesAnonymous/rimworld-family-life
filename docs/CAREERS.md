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
- **Parallel classes.** Each class is a `ClassBoard` session (instructor, assistants, pupils, subject, place); nobody is in two at once and a room hosts one class. Each instructor has their own schedule (`TeacherSchedule`). Teachers and specialists all hold classes; with no classroom a teacher gives one-to-one lessons like a tutor; a specialist-only instructor teaches only their subject.
- **Specialist assistance.** A free specialist for the class's subject may help (job `WFL_ClassAssist`, at most two per class, only when idle and not at a need). If they are in the room for at least half the instruction the class gets one delivery bonus of 10%, whatever the number of helpers; each helper earns 6 career points in that subject. If the instructor cannot continue before 60% of the instruction is done, a helper takes over with the pupils still present.
- **Assessments are per teacher.** `AssessmentLedger` (payload `wils.education.assessments`) holds each teacher's own assessment, when they last held one, and which (pupil, subject, year) were assessed. The owner of a (pupil, subject) is the teacher who gave most of it (ties to the lowest id; if that teacher is gone or the lesson predates teacher names, the lowest-numbered current instructor). Several teachers' lessons in one subject are merged and assessed once; the result and bonus are given once. An assessment whose teacher leaves passes to another teacher. The shared record of what children studied is `EducationRecord` (`wils.education.record`). Grading, sittings, make-ups, mood effects and XP formulas are unchanged. The single planned assessment notice (and its "skip this year" option) is gone: each teacher begins their own in Aprimay, or from the lectern button.
- **Migration.** The old Teacher post's lesson log, book use, recent subjects, assessment in progress and last-assessed year are copied once into the new records (marker per holder) and the old data is left in place.

## Community Care (offline only)
- Roles: Healer (L1, shown as Nurse after the first Doctor is assigned) and Doctor (L2: Medicine 6, 100 experience). Healers, Nurses, Doctors and specialists all keep doing routine check-ups. Qualification never overrides vanilla medical skill or tending; a Nurse with Medicine 15 simply does a check-up better than a Doctor with Medicine 6. The Fair ceiling applies only to the check-up condition of an unqualified entry-level worker, not to tending or any vanilla work.
- Legacy Healers keep the Healer assignment and the elected post, gain Doctor qualification (legacy, no experience) and are not made Doctors. The first Doctor the player assigns sets the Nurse terminology (`wils.care.terms`) for good.
- **Capacity scales.** One fully able worker has the old budget (4 routine visits a day, 6 in an outbreak). The community limit is that budget times the combined capacity of assigned care workers who can work now (manipulation/consciousness, 0 if barred from doctoring); each worker has their own daily limit and gap. The round window shortens for big staffs. The round itself is finite (each person once), so extra staff finish rounds sooner rather than creating extra examinations.
- Claims: a patient is seen by one worker at a time and a bed hosts one; claims expire. Refusals are per worker (lapse when that worker stops serving) or service-wide.
- **Specialties** (Doctor first, then skills and 60 specialty + 150 general experience; a Doctor with the skills may *practise* a specialty at a Fair cap to earn its experience; specialists keep routine care):
  - Counselor (Social 8): a supportive talk with someone whose mood is below 40% (grief first); leaves a good or modest 3-day memory; removes nothing.
  - Surgeon (Medicine 10): follow-up visit to someone with an implant or a missing part; bounded check-up benefit. Performs no operation: surgery stays the game's own.
  - Midwife (Medicine 8): visits someone pregnant or recovering from childbirth. Biotech only; without Biotech nobody is eligible.
  - Veterinarian (Medicine 6, Animals 6): examines tamed animals; bounded check-up benefit.
  - Geneticist: **not implemented.** The game has no gene procedure a community doctor can carry out, so there is nothing for the role to do but carry a title.

## Assembly
Teacher and Healer are never elected, reviewed or neglected out of their seat once `ProfessionsAssigned` is set. It is the Community Assembly; political posts, the hall keeper and the chaplain are unchanged. Qualifications earned since the last assembly are recognised in its opening letter (once each; purely a notice). The assembly can be postponed a month from the Families window between assemblies; it affects no career, class, assessment or round. Renames are display-only; package ids are unchanged.

## Career feedback
The pawn's Family tab lists each profession (assigned / qualified / what is missing / open to anyone), shows experience in a tooltip, and lists the last three qualifications. A message appears when someone qualifies.

## Not done / limits
Geneticist; progress bars; a stand-alone history window; Tutors do not assist classes; the old Teacher/Healer office record still exists for compatibility and for per-holder care state.

## Decisions for you
- Each teacher's assessment now starts by itself in Aprimay (the old notice with Begin/Skip is gone). Do you want a notice back?
- Geneticist: leave out, or define a game-supported activity?
- Doctors practising a specialty at a Fair cap is my addition so specialty experience has a path. Keep or change?

## In-game checks (none done)
1. Old save: Teacher and Healer appear assigned in the pawn tab; classes and check-ups continue; Healer still titled Healer.
2. Two Teachers, two classrooms: two classes at once with different pupils, nobody in two ("class:" trace lines).
3. A qualified Melee specialist, free, while a Teacher's Melee class runs: they walk in and help; the delivery bonus applies once; drafting the Teacher mid-class lets the helper take over.
4. Annual assessment: each Teacher begins their own in Aprimay; a pupil taught by two Teachers is assessed once per subject; kill a Teacher mid-assessment: another takes it; save/reload mid-assessment.
5. Tutor: one-to-one lessons between classes.
6. Care: legacy Healer keeps working; assign a Doctor: the "Nurses" message; unassign: still Nurses; reload: still Nurses.
7. Hospital colony (5+ assigned workers): visibly more check-ups per day than one worker, no patient seen twice, emergencies still first; an unqualified Nurse's check-ups cap at Fair.
8. Counselor / Surgeon / Midwife / Veterinarian: each performs its visit on an eligible target (low mood; implant or missing part; pregnant with Biotech; tamed animal) and nothing else.
9. Assembly: no Teacher/Healer race; recognition lines appear; the postpone button moves the date by 30 days.
10. Save, reload and repeat each.
