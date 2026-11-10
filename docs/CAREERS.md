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
- Experience is credited once per activity key; repeating one kind of activity on one day earns `RepeatFactor` (0.6) of the previous; `SafetyCapPerDay` (400, 0 = off) is only a backstop. Dedup keys are dated and forgotten after 60 days (max 2,000 kept).
- Qualifications are never removed (skill decay, changed definitions). A lowered bar is met at the next check.
- Careers of a module that is not installed stay in the ledger untouched.
- Existing pawns get no invented history. Holders of the old Teacher/Healer posts are *grandfathered*: assigned, and given the Level 2 qualification marked legacy, with zero experience.

## Legacy migration
`LegacyProfessionMigration` runs from the maintenance roster. For each held Teacher/Healer office it assigns the holder once (key `office:<kind>:<holder>`), without touching the office. A later player unassignment is not undone. A new holder is migrated on their own.

## Working seat
The old Teacher/Healer office records still carry class/round working state. `FamilyRegistry.SeatService` puts an assigned professional in that seat (same hand-over hooks as a change of post, no vote). `ProfessionalSeating.Pick` keeps the sitting person while assigned and present, otherwise the first present assigned colleague covers. Implemented for **Education only** so far.

## Educators (implemented)
- Roles: Tutor (L1), Teacher (L2, needs Social 4 and 120 experience; configurable constants in `EducationCareers`). Class = 10 points per attending pupil, tutoring = 8, scaled by attendance share and delivery. The subject skill is recorded as the specialty experience.
- Tutors give one-to-one lessons using the class gap rule (they never tutor what they know less well), at most one per 5,000 ticks per tutor, never during a class. Existing XP formulas are unchanged.
- Classes still run one at a time through the seated teacher; another assigned teacher covers when the seated one is gone. Several teachers running classes in parallel needs per-teacher scheduling state (not done).
- The pawn's Family tab has a Professions section: status, what is missing, Assign/Unassign.

## Not done yet
Phase 1 rename; Care (Healer/Nurse/Doctor, shared workload, claims); the 12 subject specialists and Care specialists; Assembly refactor and rename; assembly recognition of achievements; notifications beyond a qualification message; career history view.

## In-game checks (none done)
1. Load an old save with a Teacher: the Family tab of the teacher shows Teacher assigned; classes continue.
2. Assign a second colonist as Tutor: they offer one-to-one lessons to children when idle; Unassign stops it.
3. A Tutor is not offered a subject the child knows better. Experience grows in the tooltip; at 120 with Social 4 a message announces Teacher qualification and does not assign it.
4. Unassign the Teacher, assign another qualified colonist: classes continue with them; the old teacher keeps the qualification.
5. Save, reload, repeat: assignments and experience unchanged.
