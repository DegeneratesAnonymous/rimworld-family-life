# The doctor and the teacher: audit, final rules and numbers

This describes the Healer (doctor) and Teacher posts as they now are, what they were before this change (read from the source, not from the earlier notes, which were wrong in several places), and what has and has not been checked. Numbers are in game ticks unless stated (60,000 per day, 2,500 per game hour).

## 1. Audit of the earlier behaviour

The earlier notes were inaccurate in these ways. Everything else in the notes held.

| What | Notes said | Source said |
|---|---|---|
| Recent-check-up benefit | modest | Natural healing x1.05-1.40 and immunity gain +0.08 to +0.45 by stage. At Excellent, +45% immunity speed and 40% faster healing for up to 32 days (20 days x1.6). |
| Kinship | none | Check-up duration doubled for the holder's relatives (up to 64 days). Lessons gave relatives x1.5 experience. The doctor's own house was always first in the list. |
| Repeat check-up | unspecified | The old benefit was removed and replaced, so a later Poor visit could replace an Excellent one. |
| Check-up | "talk" | Also called `TendUtility.DoTend`, tending the patient as a side effect of a conversation. |
| Relationship change | one source | Three, all unlimited per day: each exchange (`ChangeBond`), the visit (`RecordTogether`, 1,200 ticks of "care"), and `ThankPost`. |
| Class size | 24 | The registry queue took 6 (`MaxSessionSize`); only the player's Hold class button gave a larger class. |
| Class stop rule | food, rest, toilet, hygiene | Same, under 15%. Nothing for injury, danger, illness or being sent for. Forced jobs, so the game's own AI never pulled a pupil out. |
| Class experience | 700 | 700 per pupil under 18 (450 adults), x0.6 to x1.5 for rapport, x1.5 for the teacher's relatives, in whatever skill the teacher was furthest ahead; plus the full book experience, for everyone who sat 50% of the class, teacher included. A pupil at 49% got nothing. |
| Assessment | 48 hourly checks | A check every 2,500 ticks for 2 days, a 600-tick sitting each, so up to 28,800 ticks per child; d20 + Intellectual + half the child's best skill; under 12 attempts was automatically Poor; rewards 1 to 4 skills (passion first, then weakest, any skill) of 400/700/1,100/1,600. Up to 6,400 experience in skills that had not been taught. |
| Letters | optional | Blocked until answered or 3 days passed. |
| Rounds | 14 days | Fixed 14 days, 1,500 ticks per talk, no daily budget, no hours, no deferral. |

## 2. What the doctor does now

**Order of work** (`CareDemand`, `CarePriority`): emergency treatment, ongoing required care, an outbreak assessment, routine check-ups. The first two are the doctor's ordinary medical work and are never interrupted; the clinic job fails and closes the moment anyone needs tending (`HealthAIUtility.ShouldBeTendedNowByPlayer[Urgent]`, read at most twice a game minute per map) and nothing routine starts while that is so. It applies only to a doctor who has Doctor work active.

**Who is asked and how** (`CareRules.Assess`, `CareParticipation`):
- *Unable* (down, bleeding, needs tending, told to rest, ill, or in a medical bed): never summoned to the shared clinic; visited where they are (a one-to-one visit), if willing. Contagious people are never brought into a shared room.
- *Busy* (need under 15%, a task they were ordered to do, danger): asked again after 1,500, 4,000, 10,000, 20,000, 30,000 and 45,000 ticks. After six tries they are carried to the next round, not marked.
- *Refuses*: reluctance = tension x0.6 + grievance x0.6 - trust x0.35 - attachment x0.15 - kin 8 - personality + a mood term. Above 40 the chance of refusing is (reluctance - 40) x 2.5 + 15 percent (at most 85%); at or below 40 they come. A refusal is remembered for 6 days and not asked again this round; nobody is fetched, escorted or badgered.
- Kinship only lowers reluctance. No other effect of kinship remains.

**Workload** (`CheckupWorkload`): a visit is 1,500 ticks of talk and costs 2,100 with walking. The routine budget is 8,400 ticks a day (4 visits, about 21% of a 40,000-tick waking day), 12,600 in an outbreak (6 visits). Visits start only 7:00-19:00, at least 2,500 ticks apart, and not on a day whose budget is used. The round stays open `ceil(patients / visits per day) + 3` days, between 14 and 40:

| Colony | Visits a day | Days to see everyone | Round window |
|---|---|---|---|
| 8 | 4 | 2.0 | 14 days |
| 20 | 4 | 5.0 | 14 days |
| 40 | 4 | 10.0 | 14 days |
| 80 | 4 | 20.0 | 23 days |

Anyone not reached when a round runs out goes first next round (`Missed`) and is never marked as having failed. People who declined are not carried over.

**Quality** (`CheckupQuality`):

> score = Medicine x 0.75 + Social x 0.25 + modifiers (-2 to +2) + talk (-1 to +1) + noise (-2 to +2)

Modifiers: medical bed +0.5, hospital room +0.5, clean room +0.5 (filthy -1), a visit outside the clinic -0.5, a rested doctor +0.25 (exhausted -1). The talk is three cooperative exchanges (see 4), summed and scaled by 0.4 then clamped. Levels: Poor under 5.5, Fair under 9.5, Good under 14, otherwise Excellent. Ten levels of Medicine are worth 7.5 points; luck is at most 2 (it used to be 9.5).

Distribution over 20,000 simulated check-ups per cell (three exchanges, participant Social 0-12; produced by `OfficeCareTests`):

| Room | Medicine / Social | Poor | Fair | Good | Excellent |
|---|---|---|---|---|---|
| poor (-1) | 3 / 3 | 100.0% | 0.0% | 0.0% | 0.0% |
| | 8 / 5 | 33.0% | 67.0% | 0.0% | 0.0% |
| | 12 / 8 | 0.0% | 21.1% | 78.9% | 0.0% |
| | 16 / 10 | 0.0% | 0.0% | 40.0% | 60.0% |
| ordinary | 3 / 3 | 100.0% | 0.0% | 0.0% | 0.0% |
| | 8 / 5 | 5.9% | 79.9% | 14.2% | 0.0% |
| | 12 / 8 | 0.0% | 0.0% | 80.7% | 19.3% |
| | 16 / 10 | 0.0% | 0.0% | 19.5% | 80.5% |
| good (+1.5) | 3 / 3 | 71.2% | 28.8% | 0.0% | 0.0% |
| | 8 / 5 | 0.0% | 54.1% | 45.9% | 0.0% |
| | 12 / 8 | 0.0% | 0.0% | 59.8% | 40.2% |
| | 16 / 10 | 0.0% | 0.0% | 0.0% | 100.0% |

**The benefit** (`WFL_CheckedUp`, `CheckupQuality.Merge`): four stages, modest. Poor "brief": no clinical effect (the mood thought is the main result). Fair: natural healing x1.03, immunity gain +0.03. Good: x1.05, +0.06. Excellent: x1.08, +0.09. Base 20 days x 0.5 / 1 / 1.1 / 1.25. It never stacks: a visit equal to the existing one only restores the time; a lower one changes nothing at all (it does not reduce, refresh or improve a better one); a higher one replaces it and keeps the longer remaining time. A visit cut short counts only if at least three fifths of it was done, as a Fair at most and half the time; less than that counts for nothing and the patient is asked again soon. A check-up does not tend, cure or add immunity beyond the table; if the patient needs tending, tending comes first and the check-up waits. People with a check-up still doing them good are not asked again this round.

**Automatic**: a round is planned a day ahead with a notice and begins by itself. The notice offers *Begin now*, *Put off a week* and *Skip this time* (only to someone allowed to direct the post) and never has to be answered. An outbreak (a quarter of the colony and at least three visibly ill) starts a sick-first round at once with a message; tending still comes first. An old save with a waiting event begins it at once.

## 3. What the teacher does now

**Shape of a class**: about 3.8 game hours (9,500 ticks): four 2,000-tick blocks of instruction with 500-tick breaks. Pupils are free in the breaks and are asked back for each block (at most three asks a block). A class holds up to 24 children. Teaching needs no furniture: a lectern, board or desk in a room is preferred; failing that a room with seats; failing that the open ground beside the teacher.

**Subject** (`LessonSubjects`): one subject per class, chosen when it starts. The teacher must be level 4 or more; most of the class must be old enough (combat from 13, medicine from 10, under 7 only Social, Artistic, Animals, Plants) and have a gap of at least 2 levels to learn from. Passion x1.35, the house's own skill-gap ambition x1.25, a book on the subject x1.15 (a book never makes a subject the teacher cannot explain), a subject taught in the last two classes x0.6.

**Leaving and releasing**: a pupil's job ends for any real need (food, rest, toilet, hygiene under 15%), needing tending or rest, being down, drafted, in a mental break, danger on the map, a player order, the teacher stopping or the break. They are not recalled until the need clears (1,500 ticks) or the next block, and not after three tries in a block. The teacher's job ends for the same reasons, or if they are downed, die or leave the map, and releases everyone. Repeated failed starts back off 600, 1,800, 5,400, then 7,500 ticks. Absences for a need, illness, danger, sleep or being called away are *excused* and shorten the time the pupil could attend; they are not held against anyone.

**Experience** (`LessonMath`, `LessonGrant`: the only place it is given, once per pupil at the end):

> XP = 480 x (time attended / 8,000) x delivery x engagement x gap x age

Delivery 0.5-1.4 = 0.45 + subject skill x 0.035 + Social x 0.015 + facility/preparation (-0.15 to +0.2) - tiredness (up to 0.25). Engagement 0.7-1.25 from how the teacher's exchanges with that pupil went. Gap: 0 unless the teacher is 2 or more levels ahead, then 0.35 + gap/9 up to 1. Age: 1.0 under 13, 0.85 under 18, 0.65 adults. A pupil who attended under 20% of the possible time, or under 600 ticks, gets nothing. Applied with `Learn(xp, direct: true)`, as books do, so the game's own learning multipliers are not applied twice (see section 7).

**Books**: a book is used only if it teaches the class's subject. Book experience = book value x 0.5 x time share x gap, only for pupils who attended, once per pupil per book per 12 days (saved, so a reload cannot repeat it). The teacher gets none. Other callbacks cannot grant experience (`OfficeSourceGuardTests`).

**Assessment** (`Assessments`): two to four short daytime sittings over two days (planned at 18,000, 60,000 and 96,000 ticks after the start, a 7:00-19:00 window, 600 ticks each) plus one make-up for anyone excused through illness or need. The result is read from what each child studied. Four things are kept apart:
- *Attainment*: the skill; reported, never used to grade.
- *Progress*: delivery x engagement per attended tick, regardless of where they started.
- *Confidence*: attendance / 0.6 (capped), weighted by sittings completed out of those they could have sat.
- *Completion*: Complete (2+ sittings, 45%+ attendance), Partial, or Incomplete (no result, no reward, no mood penalty).

Score = 0.6 x progress (capped) + 0.25 x engagement + 0.15 x teacher support + a few hundredths from the sittings. Poor under 0.45, Fair under 0.62, Good under 0.8, Excellent above; low confidence caps at Good and the letter says "provisional". The consolidation bonus is 5/10/15/20% of the experience recorded from lessons in each subject studied (half when Partial), nothing in a subject not studied.

Distribution over 5,000 simulated children per row (attendance 1,500-8,000 of 8,000, 0-3 sittings; a quarter are Incomplete in every row because of the random attendance and sittings, which is deliberate):

| Teaching | Poor | Fair | Good | Excellent |
|---|---|---|---|---|
| weak teacher, unengaged | 74.7% | 0.0% | 0.0% | 0.0% |
| below average | 0.0% | 66.2% | 7.3% | 0.0% |
| ordinary | 0.0% | 0.0% | 69.8% | 4.9% |
| good teacher, engaged | 0.0% | 0.0% | 16.3% | 57.7% |

A weak result adds suggestions to the letter (one-to-one time, a different approach, a make-up after illness), not a penalty. Moods: pride for Good and Excellent, a mild short thought (-1 for 2 days) for Poor, none for an Incomplete.

## 4. Cooperative exchanges and relationships

`CooperativeExchange`: delivery = skill x 0.7 + Social x 0.3 + preparation and room (bounded +-1.5) - tiredness; difficulty 6 (7 for habits, quizzing, holding attention; 5 for catching up and stories); margin = delivery + the other person's Social / 20 + noise (-2..+2) - difficulty; Poor at -3 or less, Flat under 2, Good under 6, Great above. The other person's Social is worth one point at most, never against the professional. The opposed contest `GuidedExchanges.Resolve` is unchanged and unused by routine work.

`FamilyRegistry.ProfessionalExchange` is the one place an office exchange changes a relationship. Familiarity first (0.3 / 0.6 / 0.8 / 1.0), trust from Good and Great (0.35 / 0.7), tension only from Poor (0.8), attachment only from a Great *personal* topic and only on the first contact of the day (0.3 x (1 - attachment / 40)). Same-pair contact per day scales 1, 0.5, 0.25, then nothing. Removed: `RecordTogether` and `ThankPost` for these offices. Thirty days of the best possible visits give under 10 attachment (tested).

## 5. Saves

Added to `CommunityOffice`, all defaulting safely when absent: `RoundWindow`, `Missed`, `Refused/RefusedTick`, `RoutineDay/RoutineUsed/LastVisitTick`, `ContactDay/ContactIds/ContactCounts`, `LessonLog`, `BookUse`, `RecentSubjects`, `TestScore`, `TestExcused`, `KinQuality/OtherQuality/KinSeen/OtherSeen`. `TestPass` is kept (unused) so older saves load. `PendingTick` now means the planned time. `LessonLog` is at most 120 text records; `BookUse` 64. A new holder starts a clean record of rounds and refusals; what the children studied stays. Not saved (a session in progress simply does not continue after a load): the clinic and class sessions, the appointment gaps, the danger and need caches.

## 6. Perspective Shift

Optional and not required. When a pawn is being played: the Begin/Put off/Skip options and the Hold class, Begin check-ups and Begin assessment buttons appear only if that pawn is the post's holder or manages the holder's house (`OfficeAuthority.MayDirect`, checked again when the button is pressed); a check-up result above a patient's head is shown only to the patient and the doctor; the played pawn is never made to run a clinic or class job.

## 7. What is not verified

See the change notes. In particular nothing in the game has been observed yet, the vanilla learning multipliers behind `Learn(xp, true)` were not independently inspected (the code follows how books grant experience), and story threads are not integrated.
