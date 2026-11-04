# Relationship events

Ninety relationship moments in nine groups, built on one shared engine. This document is the implementation map, how a moment is
decided, what is physical and what is not, and what has and has not been checked.

Not yet verified in the game. The rules are covered by tests that run outside it (see Testing); the parts that touch pawns, jobs and
the map have been compiled for 1.6 and 1.7 but not played.

## The ninety

**Within families: adult with adult**

A1-01 A Needed Conversation, A1-02 A Thoughtful Gesture, A1-03 A Family Disagreement, A1-04 An Evening Together, A1-05 An Unspoken Grudge, A1-06 Passing the Torch, A1-07 Remembering Together, A1-08 Defending a Relative, A1-09 Conflicting Priorities, A1-10 Quiet Reconciliation

**Within families: adult with child**

A2-01 Bedtime Story, A2-02 A Proud Moment, A2-03 Playing Favorites, A2-04 A Lesson Learned, A2-05 Seeking Comfort, A2-06 Growing Independence, A2-07 A Broken Promise, A2-08 A Shared Discovery, A2-09 Parental Disapproval, A2-10 Making Time for Them

**Within families: child with child**

A3-01 Sibling Adventure, A3-02 The Favorite Toy, A3-03 Standing Up for a Sibling, A3-04 A Secret Between Them, A3-05 Sibling Competition, A3-06 Making Up After a Fight, A3-07 The Protective Older Sibling, A3-08 Feeling Left Out, A3-09 Imitating a Sibling, A3-10 The Shared Achievement

**Outside the family: adult with adult**

B1-01 An Unexpected Friendship, B1-02 The Misunderstood Comment, B1-03 A Favor Remembered, B1-04 An Awkward Apology, B1-05 A Growing Attraction, B1-06 A Personal Betrayal, B1-07 Friendly Competition, B1-08 An Unexpected Confidant, B1-09 Defending a Friend, B1-10 Drifting Apart

**Outside the family: adult with child**

B2-01 The Unexpected Mentor, B2-02 A Helpful Neighbor, B2-03 The Familiar Face, B2-04 The Stern Warning, B2-05 A Friend of the Family, B2-06 The Unwelcome Influence, B2-07 Shared Curiosity, B2-08 A Reassuring Presence, B2-09 Recognition of Talent, B2-10 An Uncomfortable Encounter

**Outside the family: child with child**

B3-01 New Playmate, B3-02 Best Friends, B3-03 The Playground Dispute, B3-04 The Newcomer, B3-05 Standing Up to a Bully, B3-06 Drifting Apart, B3-07 Sharing a Discovery, B3-08 The Excluded Playmate, B3-09 Friendly Rivalry, B3-10 A Childhood Promise

**Between families: adult with adult**

C1-01 A Peace Offering, C1-02 The Reconciliation Dinner, C1-03 A Public Slight, C1-04 A Family Favor, C1-05 A Formal Disagreement, C1-06 The Unexpected Alliance, C1-07 The Shared Celebration, C1-08 The Unwelcome Invitation, C1-09 Remembering an Old Feud, C1-10 A Third-Party Intervention

**Between families: adult with child**

C2-01 The Neighbor's Apprentice, C2-02 The Protective Confrontation, C2-03 A Helping Hand, C2-04 An Unwelcome Friendship, C2-05 The Shared Caregiver, C2-06 The Young Ambassador, C2-07 A Family's Gratitude, C2-08 The Misjudged Child, C2-09 Encouragement Across Families, C2-10 The Generational Bridge

**Between families: child with child**

C3-01 Friendship Across Families, C3-02 The Family Rivalry, C3-03 A Playground Fight, C3-04 A Shared Victory, C3-05 The Excluded Child, C3-06 Breaking the Cycle, C3-07 A Cross-Family Playdate, C3-08 The Competitive Families, C3-09 Defending a Rival's Child, C3-10 The Next Generation

## How a moment is decided

One engine, not ninety. `Source/FamilyLife/Domain/Events/`:

| Step | What decides it | Where |
|---|---|---|
| Opportunity | Named conditions, each of which can say why it failed. Includes the pair (relatives, outsiders, two families), their ages, what is happening to them, the shared activity, cooldown | `EventCatalog.cs` (`C` conditions), `EventEngine.Explain` |
| Motivation | A weighted sum of character, bond, mood and circumstance. Then a roll: being able to is not wanting to | `Drive`, `PickEvent` |
| Execution | The skill that fits the activity (Social for an apology, Intellectual for teaching, Cooking for a dinner), or character when no skill applies. Separate from sincerity | `EventEngine.Execution` |
| Reception | How much the other person trusts, likes and is at ease with the one reaching out, plus the event's own extras | `EventDef.Reception` |
| Consequences | Both bonds move (not always alike), the cause is noted, a rift may be eased or strained, a promise may be made, family standing may shift, experience may be given, a very good or very bad moment may become a defining memory. All in `ApplyEvent` | `EventEngine.ApplyEvent` |
| What it opens | Promises that can be kept or broken; a grievance between families that can lead to a gathering to mend it; rifts that can be mended later | `Commitment`, `FamilyTie`, `RequestReconciliations` |

Every outcome is one of four tiers (poor, flat, good, great) and each event words all four in its own way. A tier is how the moment went
*for the relationship*: for a quarrel, great means it was settled well.

Guards against inflation and runaway hostility:

- Gains shrink as a bond grows and as the same event repeats between the same two people within a week.
- A minor quarrel adds a few points of tension, never a rift by itself; a rift needs tension that is already high, and never a second for one pair.
- A neglect or rivalry rift is only ever eased by a conversation. It closes when the attention is really restored.
- No one has more than three meaningful moments a day. Ambient moments do not count against that and are never announced.
- Passing near someone gives nothing. Nothing in this system is a standing mood bonus.

## What is new in the records

- **Acquaintance bonds**: people who are not family or housemates now get a bond of their own once a moment has brought them together, so friendship follows the game's opinion the same way family bonds do. It is capped at twelve per person.
- **Family standing** (`FamilyTie`): familiarity, goodwill and grievance between two families, built from incidents and shared moments only. It is never an average of everyone's opinion. It fades slowly, a grievance eases slowly, and it remembers who was wronged by whom, so a feud can be tied to particular people.
- **Promises** (`Commitment`): only ones the mod made itself: A2-08, A2-10 and B3-10 leave one when they go well. Spending time together keeps it; a day passes and it is broken, which costs the other person a little trust. A2-07 can only happen about a promise that was actually broken.
- **Event log**: the last 300 moments (ambient ones are dropped first). Feeds cooldowns, the Moments tab and the developer view.

## Where moments come from

Nothing is a log line: either two people really do something, or the thing already happening is the opportunity.

| Setting | How it is real |
|---|---|
| Two people who are free and near | One walks up and talks while the other gives their attention (`WFL_SocialMoment`, `WFL_SocialListen`). Both give way to a player order. Sometimes the two take a short walk in the home area in fair weather first (nature walk) |
| Shared meal / picnic | After relatives, friends or strangers finish eating in the same room (outdoors, within a few steps, for a picnic) |
| Celebration / reconciliation gathering | At the end of the game's own family gathering. Guests from other families already come; a gathering to mend things is now arranged by itself when two families have a real grievance (twelve days between tries) |
| Storytelling | The existing bedtime story. A2-01 only adds how it was received, and no second set of rewards |
| Learning | The existing teaching time |
| Support visit | The existing bedside visit |
| Game night / competition | Two people already doing the same recreation side by side (chess, horseshoes, TV). Nobody is moved |
| Community aid | Two people already building, repairing, sowing or harvesting side by side. Nobody is moved |
| Playdate | Two children together, in family time or in a conversation |

## What is smaller than the design asked for

- **Storytelling circle (G04)** is the bedtime story and the elder telling the family's history. There is no multi-listener circle around a fire.
- **Nature walk (G07)** is two people walking a short loop in the home area. There is no longer outing.
- **Community aid (G11)** is recognised only for building, repair, sowing and harvesting. Other shared work is not noticed.
- **Game night / competition (G05, G08)** are recognised from what two people are really doing; the mod does not start a game.
- **A moment never moves a pawn who is working.** If everyone is busy, nothing happens, which is intended.
- **Promises** come from three events only. There is no general promise tracking.
- **Wronged** is read from the sour social memories a pawn holds (opinion offset of -5 or worse). A wrong the game does not record as such is invisible to the mod.
- **Newcomer** means less than five days in the colony, from the game's own records.
- Without Biotech no child event can happen.
- Frequencies and strengths are first guesses. Tune with the settings (frequency, which groups, which announcements).

## Settings

Settings, Mods, Wil's Family Life: relationship moments on or off; within families, between friends and others, between families and
during shared activities each on or off; how often (0.25x to 2x); and how much to be told (nothing, moments that matter, every notable
moment). Routine moments are never announced.

## Finding out why something did not happen

Debug actions menu, Wil's Family Life, "Explain relationship events for two selected pawns": select two colonists and it logs, for
each direction, every one of the ninety events as possible or with the first thing that rules it out. Turn on "Log events" in the
settings to log each moment as it starts and finishes. The Families window has a Moments tab: how this family stands with others
and why, promises being kept track of, and recent moments.

## Testing

- `EventCatalogTests`: ninety events, ten per group; every event can become possible under some consistent circumstance and can go more than one way (a 30,000-circumstance sweep); none is possible for the wrong kind of pair; child events are off without Biotech; every event has all its text.
- `EventEngineTests`: direction of consequences, diminishing returns, bounds, rift rules, promises, family standing, cooldowns, teaching, confronting, defining memories, gatherings to mend things.
- 22 new mutation checks (`python scripts/mutation_check.py --only Events/`) each break one of those rules on purpose and confirm a test notices.
- Not tested outside the game: the sampling of real pawns, the two job drivers, the hooks into meals, stories, gatherings and the bedside, and the thoughts they add.
