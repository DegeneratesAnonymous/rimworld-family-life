# Phase 3 report — everyday family life

Version 0.3.0. Also in this version: the Families view moved from the bottom bar to a **Family button on the bio card** (next to the name, hidden if it would run into the game's own icons). The surname link stays. The Families screen is now a normal window you can drag and resize.

## What was implemented

**Family time (joy activity).** A joy giver offered during joy time, next to TV and chess. A pawn picks the relative they most want to be with, among those who are free and near, walks over and stays beside them for about 2,200 ticks (following if they move), chats now and then, and both gain joy. Because it spends joy like any recreation, it replaces recreation rather than adding to the schedule, and repeated use makes it less appealing through the game's own joy tolerance. The relative is not interrupted or given a job.

**Choosing whom to be with** (`RankCompanions`). Desire rises with attachment and with how long since they last spent time together on purpose, falls with tension, and depends on the relationship (parents seek out their children most, then partners, children, siblings, housemates). Former partners and strangers are never chosen. Pawns also never target someone on a work shift, asleep, drafted, downed, or in a mental break.

**Family-time budget.** A soft per-pawn target (default 2 hours a day, 0.5 to 4 in settings). Once reached, no new family activity starts until tomorrow. Nothing in progress is cut short. It counts family time and stories; it does not count meals.

**Bedtime stories.** In the hour before a child's bedtime (from their own schedule), the free parent with the best Social skill reads to them; if that parent is busy, the next best. The parent fetches a book if there is one nearby that would teach the child something, carries it to the child and puts it down afterwards. The child gets a matching "listening" job so the two are together. The child gains skill experience: with a book, the book's own rate scaled by the reader's Social and halved as a shortened reading; without one, the parent teaches their best skill the child can still learn, at a lower rate. Child skill levels above 8 don't gain from stories, and the game's own daily learning saturation applies. It happens at most once a night per child, both get a small mood memory, and the bond between them grows.

**Eating together.** When a relative finishes a meal in the same room as another relative who finished within about 25 in-game minutes, both feel it: a small mood memory and a small bond gain. It changes nothing about where or when anyone eats.

**Settings.** Family activities on/off, daily hours, bedtime stories on/off, eating together on/off.

## How it avoids eating colony time

- Family time is chosen only inside joy time.
- The story node sits after the game's emergency, duty and forced-work handling, and only fires for a parent who is not on a work shift, is fed, rested and not needing medical rest.
- Nobody in a work shift, asleep, drafted, downed or in a mental break is ever the target.
- A failed activity can't loop: a story is marked for the night the moment it is offered, and a pawn whose family time just failed is not offered another for 600 ticks.
- Each activity is bounded in length and in budget.
- Cost: no new per-tick work. The think node exits within a few comparisons for anyone without a child.

## Not implemented, deliberately

- **No cook role, meal delivery or seating.** Eating together rewards what happens; it does not arrange it. The design moved the cook away from delivering meals; it comes back as a preparation priority in a later phase.
- **No separate board games, stargazing, walks, picnics or campfire stories.** Phase 3 has one activity, spending time together. Activity variety needs the facilities and gatherings of later phases.
- **No general intention scheduler.** The pieces that exist (ranking, budget, availability) are the planner for these two activities. Comforting, teaching, mediating and the like will use them, and need the conflict and role systems first.
- **No special children's play** (following a parent, sibling play, pets). Children can choose relatives for family time like anyone else, but nothing yet makes them seek out play.
- **No newborn bonding or other Biotech-specific behavior.** Babies take no part.
- **Bond values still only drive grief and these activities.** Trust and tension shape who is chosen and how bonds move; they do not yet cause conflict.

## Tests

`dotnet test`: **106 passed.** New in this phase: budget (per day, reset, soft limit, pruning), time together (both directions, scales with time, bounded), ranking (stale versus recent, relationship kind, tension, exes and strangers excluded, works without bonds), stories (once a night, best reader, ties, book versus none, storyteller skill, sane bounds), eating together (room, time window, strangers, weak bonds, self).

**Mutation check**: 23 deliberate breakages (12 earlier, 11 for this phase), **all caught**. Two slipped past the first run of the new ones and exposed weak tests (an ex that was not actually an ex in the test data, and a ranking that was decided by relationship weight alone so "not seen for longest" was never tested). Both fixed and rerun.

**Static checks**: both versions build with 0 errors and 0 warnings; the offline Harmony check passes all 5 patches against the installed 1.7 game and the 1.6 reference assemblies, including the new meal hook; every field in the new defs was written against names verified in the game's classes; XML is well formed.

A compile warning during the work revealed that the game's `JobDriver` already has a `startTick` field that mine hid; renamed.

## Found while reading the game's code, and handled

- A tired pawn falls asleep the instant they lie down, so there is no "awake in bed" moment for a story. Stories therefore happen in the hour *before* bed, with a coordinated listening job for the child.
- The initiator's *previous* job tag is meaningless at the moment they choose joy; availability is checked on the other person only.
- A "fail if the book is not on the map" rule on the whole job would have failed it the moment the book was picked up; it is applied to the walk to the book only.
- 1.7 and 1.6 differ in the joy-tick helper signature, so joy is granted directly rather than through it.

## Requires in-game verification

None of this has run in the game. In order:

1. **Family time.** With a couple or a parent and child, give them joy time. Within a day you should see "spending time with X" in the job report, two pawns standing together, a chitchat now and then, and "time with family" in the mood tab. Check that it appears *among* other recreation and not instead of all of it, and that a pawn who has had their 2 hours stops.
2. **Not disrupting work.** Put someone on a work shift: no family time. Draft someone: it ends. Downed or mental-break relative: it ends.
3. **Bedtime story.** Give a child (needs Biotech) a Sleep hour. In the hour before it, a parent should walk to the child; the child sits quietly; the parent reads for about 30 in-game minutes. Check the child's skills went up a little (more if there was a book nearby), both have a mood memory, and the book is put back down. Try with two parents of different Social skill: the better one should read.
4. **No loops.** If a story can't start (no path, child asleep), the parent should go back to what they were doing and not try again that night.
5. **Eating together.** Two relatives eat in the same dining room a few minutes apart: "ate with family". A stranger at the next table: nothing.
6. **Performance.** Watch for hitching when many pawns finish a job at once. The planner runs only when a pawn chooses joy or a parent thinks.
7. **Settings.** Turning "Family activities" off should stop all of the above; the budget slider should change how soon new activities stop.
8. **Bio card.** The Family button sits after the name and clears the game's icons at different name lengths and UI scales.

## Design compromises

- A story is claimed for the night when it is offered, not when it succeeds, so a failed story is lost for that night. Safer than a loop.
- The partner in family time is not reserved, so a doctor can still tend them; the cost is that they may wander off, in which case the activity ends after about 5 seconds apart.
- Mood effects are small (+2 to +4, a day or less). They are easy to change in `Defs/ThoughtDefs.xml`.
- Social skill stands in for "childcare skill" (the game's childcare work uses Social).
