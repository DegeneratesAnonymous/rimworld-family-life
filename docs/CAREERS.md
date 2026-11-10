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

## Educators (implemented, offline only)
- Roles: Tutor (L1), Teacher (L2: Social 4, 120 experience), and twelve subject specialists (L3: Teacher + skill 12 in the subject + 150 subject experience + 400 overall). Constants are in `EducationCareers`.
- **Parallel classes.** Each class is a session on `ClassBoard` (instructor, assistants, pupils, subject, place); nobody is in two at once and a room hosts one class. Each instructor has their own schedule (`TeacherSchedule`, seeded from the old post's date). Teachers and specialists all hold classes; a teacher with no classroom gives one-to-one lessons like a tutor. A specialist-only instructor teaches only their subject. Assessment stays with the seated lead teacher. `ClassBoard.Substitute` supports an assistant taking over; specialists do not yet assist inside a teacher's class.
- Existing lesson length, breaks, age rules, release rules, XP formulas, books and assessment are unchanged.

## Community Care (implemented, offline only)
- Roles: Healer (L1, shown as Nurse after the first Doctor is assigned; stable id `wils.care.healer`) and Doctor (L2: Medicine 6, 100 experience).
- Legacy Healer holders keep their Healer assignment and the elected office, are given Doctor qualification (legacy, no experience) and are NOT appointed Doctor. The first Doctor the player assigns sets the Nurse terminology flag (`wils.care.terms`), which persists if the Doctor leaves.
- The round, budget and refusals stay community-wide. Every assigned care worker does routine visits, claiming patients and beds through `CareClaims` (atomic, expiring). Refusals are per care worker (HealerSpecific) or service-wide; they lapse only when that worker stops serving. Entry-level (unqualified) workers' check-ups are capped at Fair.

## Assembly
Teacher and Healer are never elected, reviewed or neglected out of their seat once `ProfessionsAssigned` is set (the game sets it on load). The assembly is shown as the Community Assembly; political posts, the hall keeper and the chaplain are unchanged. Renames are display-only; package ids are unchanged.

## Not done
Counselor, Surgeon, Midwife, Veterinarian, Geneticist; specialists assisting in a teacher's class; assembly recognition of achievements and a postpone option; career history view; UI for requirements of level 3 beyond the Professions section; per-teacher assessments.

## Decisions for you
- Assessment and the community record still sit on the seated lead teacher.
- Nurses/Healers do routine visits at all times (Fair cap when unqualified), rather than only assisting once a Doctor exists.
- Education specialists are Teacher + specialty; they appear as separate assignable roles.

## In-game checks (none done)
1. Load an old save with a Teacher and a Healer: both appear assigned in the pawn tab; classes and check-ups continue; the Healer still reads "Healer".
2. Assign a second qualified Teacher and a lectern/desk second room: two classes run at once with different pupils; nobody is in two; a pupil released for a need is not double-booked.
3. Assign a Tutor: one-to-one lessons happen between classes; a Tutor never gets a subject the child knows better.
4. Assign the legacy Healer as Doctor: a message says entry-level workers are now Nurses; unassign the Doctor: still Nurses; save/reload: still Nurses.
5. Two care workers, one outbreak: no patient is examined twice; a refusal to one does not bind the other.
6. Advance to the assembly: no Teacher/Healer race appears; hall keeper race does. Letters say Community Assembly.
7. Save, reload, repeat each.
