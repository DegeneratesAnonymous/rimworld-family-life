# Changelog

## Unreleased - check-ups and classes happen in a place
- **Check-ups are held in the clinic.** With a medical bed in an enclosed room, the doctor goes there and waits. Patients come in one at a time, lie on the bed, talk with the doctor (about twenty-five seconds), are tended and get the check-up benefit, then get up and leave; the next comes in. A patient who cannot reach the bed is skipped for a while. With no medical room the doctor still walks to each patient as before.
- **Classes are held in the classroom.** The teacher stands behind a lectern if there is one, otherwise in front of a blackboard, otherwise in front of a desk, and teaches for four game hours. The colony's children come, sit in the room (chairs first), and each who sat through at least half of it gets the lesson, the bond and office credit. If the teacher picks up a skill book when the class starts, everyone who attended (and the teacher) gets what that book gives. With no classroom furniture, lessons stay one-to-one.
- The developer action "Bring a class, a check-up round and a hall meal due now" now also lets people be called even when busy, selected or on a work schedule, for two game hours, so a test is not held up.
- **Check-ups and testing are events you can begin or pass on.** When a round is due the doctor offers it as a neutral letter (Begin, Pass for now, or decide later: left alone it begins by itself after three days). A quarter of the colony ill at once, and at least three, offers a round for the outbreak, which sees the sick first (and again, if they were already seen this round), and asks again in five days if passed. In Aprimay, once a year, the teacher offers the annual testing the same way. There are also buttons: Begin check-ups on a medical bed, Begin annual testing on a lectern. The state is saved on the post.
- **Check-ups have a quality.** The doctor's check is Medicine x0.6 + Social x0.4 + how the talk went + a d20, giving Poor, Fair, Good or Excellent. The recent-check-up condition now has four stages (healing 1.05/1.15/1.25/1.4x, immunity gain +8/+20/+30/+45%) and its length is 0.7/1/1.25/1.6x the usual. A "Check-up: good" note shows over the patient; poor ones leave a "brushed off" memory, good and excellent ones a reassuring one.
- **Annual testing.** For two days each child sits a check every game hour they are up and free, at a desk in the classroom if there is one (d20 + Intellectual + half their best skill against a difficulty the teacher lowers by up to 4). At the end, 12 or more sat checks are needed to be judged; the share passed gives a quality, and the child is rewarded with experience in 1, 2, 3 or 4 skills (passions first, then the weakest) of 400, 700, 1100 or 1600 each, plus a mood memory. The results come as a letter. Classes carry on as usual throughout, independently of the testing and the check-ups.
- **A "Hold class" button on lecterns.** Select a lectern in a room that could be a classroom (not a church, bedroom, hospital or kitchen) and press Hold class: the colony's teacher goes to that lectern and the children come, at once, whatever they were doing (not if drafted, hurt or in a break). Credit and the social checks are the same as a class held on the usual round.
- **Recent check-ups keep people off the list.** Anyone whose last check-up is still doing them good (it lasts up to 20 days) is not asked again this round; if that leaves nobody, the round closes.
- **Developer menu:** "Start a class now" and "Start a check-up round now (clears recent check-ups)" each start just that, run the post's work at once and report what blocks it, including anyone who should stop for a need or an event and why. A class or check-up now stops only for an event they have been called into (a party or ritual), not for any group they belong to.
- **The check-up round is a list the doctor works through.** At the interval everyone goes on the doctor's list, and it stays open until they have all been seen (up to 14 days), however often the doctor is called away. The doctor works down it whenever free; a meal, sleep, the toilet or a hygiene need, or being called into a gathering, pauses it and it is taken up again afterwards. A patient is given a game hour to reach the clinic; if they have not come, the doctor goes to find them and walks them back. If they cannot be brought (cannot reach the bed, busy, hurt) after two tries, the doctor sees them where they are the next time they are free. During the examination the doctor moves about the room rather than standing still.
- **Classes are no longer held in a church.** Rooms for worship, sleep, healing or cooking are never used (they have lecterns too), and of the rest the one with the most school furniture and seats is chosen. A class stops for the same needs and gatherings.
- **Talk during a check-up or class is a social contest.** The doctor rolls Social + Medicine and the teacher Social + Intellectual (each plus a d20) against the other person's Social (plus a d20); a close result is flat, a clear win good or great, a clear loss poor. A check-up has three exchanges (medical advice, reassurance, healthier habits, a catch-up chat); a class has about one an hour with a random pupil present (a question, an explanation, a quiz, a story, a word to settle the room). Each shows a spoken line from both, tinted by how it went, shifts how the other person feels about the leader (attachment, trust, tension), and trains the leader's Social. How the talk went changes the outcome: a good check-up lasts up to 60% longer and leaves a reassuring mood memory, a poor one shortens it and leaves a brushed-off one; a pupil who got on well with the teacher takes in up to 50% more of the lesson and book (poor rapport, down to 60%) and remembers it as inspiring or dull. Each exchange also lifts or lowers the rest of the room a little.
- Not seen in RimWorld: both flows are compiled and the existing tests pass, but the walking, lying down and sitting need checking in the game.

## Unreleased - stories told from the right side
- **Strick's chapter no longer says "warm" when his dealings went badly.** Every moment now has an effect on the person looking (`StoryValence`): Ivan standing up for Debby against Strick is good between them and bad for Strick; a failed friendship is bad for the one it failed with; a reluctant acceptance that changed nothing is neutral. The chapter's tone, its evidence lines ("Why Strick sees it this way"), the colours of story cards and the word for each known relationship (Strained, Uneasy, Warm, ...) all come from that, so Summary, Story and Relationships agree. A known person is no longer "Unfamiliar" just because exact values are private.
- A capability gap and the ambition to fill it are now one thread ("House Northcott needs a capable cook"), with the score in its tooltip. The vague "A turning point" thread is gone: a moment becomes a thread only when it leaves something ongoing. Suggested actions are checked against the person (apologise only where there is something to apologise for, teach or learn only if the skill allows it, people named), and the overview keeps general suggestions.
- Header and counts use proper singular and plural ("1 household", "4 House members"), spacing after labels is fixed, family ties read as sentences ("Ivan is Sally's parent"), Home counts beds and sleeping spots separately, the sidebar alert is a numbered badge whose tooltip lists the matters, the tree's Recenter control has room and its colour strip and cohesion line are explained, and House Affairs uses two columns on wide windows with diplomacy as cards.
- Not done: heirloom item cards, the Family inspect tab changes, Better Bedrooms. Nothing here has been seen in RimWorld.


## Unreleased - perspective, authority and counts
- **Knowledge and authority are now each decided in one place** (`Domain/KnowledgeAndAuthority.cs`). `Knowledge` classes information as public, observable, shared, House-private or secret and says what the person being played may know; `HouseAuthority` says what they may do. Declaring an heir, appointing a steward, editing the crest, founding a branch and designating an heirloom now go through it, and each action checks again inside itself, so a call that skips the window is refused too. Before this, the heir menu passed the House's head to the registry as the actor, so the registry's own check could not catch an outsider. A visitor now sees no such buttons; a member who is not the head sees them greyed with the reason ("Only the Head of House Northcott may publicly declare an heir.").
- **A visitor's story is no longer blank.** Viewing another House shows your own history with it ("Uneasy dealings with House Northcott", built from moments you took part in), else what is public ("Known by reputation"), else "Little is known". Its ambition, supplies, private relationships and chronicle stay hidden. Relationships shows your own relationships with the House (in words) and its public family ties; Home and House Affairs explain what is private instead of a bare "Not known".
- **Counts agree.** A House member is anyone who belongs to the House (the test the sidebar and every action already used). The sidebar, header, Summary and Home use the same count; people who live in its households but belong to another House are shown as resident non-members.
- People is a card grid grouped as House members, resident non-members, associates and relatives elsewhere. Home shows compact household cards with the selected household's detail beside it. Story cards no longer cut off their titles, colour by outcome (green good, red harmful, grey unchanged, blue ongoing, gold defining), name the pair in their buttons, and fall back to a plain legacy entry for moments whose definition is gone. Summary now shows one-line highlights; the full cards are in Story. House Affairs gains Diplomacy and Community influence cards, "Head of House Northcott", "Select Ivan" and "Declare an heir". Names use one short form everywhere.
- Not done: see the pull request for the honest list (no in-game check or screenshots, Better Bedrooms, family tree legend, Family inspect tab, heirloom item cards).


## Unreleased - the Families window tells the family's story
- **Story** replaces Events (Current, Timeline, Chronicle) and **House Affairs** replaces Leadership. Summary now opens with a **Current Chapter** (what the family is mostly going through, in plain sentences built from real records), followed by up to three **story threads**, informative attention rows (what, how much, what could be done; click to go there), what is coming, the three most meaningful developments, and a small status card. Wide windows use three columns, then two, then one.
- Events are **ranked** (defining, major, meaningful, everyday) and **grouped** when they belong together: a parent teaching two children reads as one scene ("Strick's lessons"), while moments that went differently, or opened a rift, stay apart. Everyday matters fold into one line. The Chronicle holds only defining history. Timeline cards show who took part, the cause when known, how it landed and how much it mattered, and can be filtered by person.
- House, household and family are now counted apart ("3 House members, 4 household residents in 1 household") with a tooltip explaining the difference. Households have names of people ("Jessop Household", "Strick and Madeline's Household"). People who live with the family but belong to another House are listed and marked ("Lives here through marriage; remains a member of House Garrick").
- Relationships explains each pair in a few sentences (tone, trust, recent direction, concerns, a possible interaction) and says Closeness instead of Attachment. House Affairs separates House offices from community offices (and names the holder's House), turns the ambition into a narrative card (why, urgency, strengths, obstacles, paths, what a pawn could do) with small need bars whose tooltips explain themselves, replaces the cycling crest buttons with an Edit crest dialog (swatches, symbol grid, patterns, live preview, Confirm / Cancel), gives founding a House its own card, and says "Choose inheritor" for heirlooms.
- Tab alert squares are now labelled counts. Tabs keep a fixed width. Nothing is invented: the story layer saves nothing and changes nothing.
- Offline only: 35 new tests (895 in all), both game builds, 24 patch checks. Not seen in RimWorld, no screenshots yet. See docs/FAMILY-WINDOW.md, "The story layer", for what is and is not done.


## Unreleased - the Families window, reorganised
- The eight tabs are now six: **Summary, People, Relationships, Events, Home, Leadership**, under a persistent header (crest, name, head, steward, members, households, cohesion, ambition, things needing attention, and whose eyes you are looking through). Overview became Summary and People; Moments, Traditions and History became Events; Households and Zone became Home; House became Leadership. Nothing was removed; docs/FAMILY-WINDOW.md says where each part went.
- Summary is a dashboard: status, a short list of concerns (each one opens where it can be dealt with), what is coming, the three most meaningful recent events, and a portrait row. Concerns come only from records the mod already keeps.
- The family list has search, sorting (name, size, standing, needs attention), a selected state, an attention dot and a collapse button. The tree no longer paints closeness over genealogy; closeness is in Relationships, which now has search, family / associates / everyone filters, four sort orders, coloured bars with a trend arrow, and a detail panel for each person.
- Events has Upcoming, Recent and Chronicle with category filters and event cards. Home shows current against wanted amounts for food, fuel, medicine and apparel, beds and rooms per household, and the family area's coverage and missing essentials. Leadership shows an icon card for each office, the house's ambition and why, alliances and requests, and institutions.
- Perspective is always visible in the header. Outsiders see 'Not known to <name>'; a privately named heir, other people's rifts and promises, and how another house is doing stay hidden. A missing colony leader reads 'No recognised colony leader' rather than 'Someone'.
- Fixed: several office/assembly texts in Governance.xml had been written into one value and did not exist as texts.
- Offline only: 37 new tests (859 in all), both game builds, 24 patch checks. Nothing here has been seen in RimWorld, and no screenshots exist yet.

## Unreleased - doors and moods
- A pawn approaching or following a person is now judged by where that person is, so nobody walks into a home after someone (a downed person is still reachable for rescue). Once a visit is over the door closes again at once, and the grace for stepping out and back is about 15 game minutes.
- Social moments no longer pile up in the mood list: at most 2 'moments that mattered', 3 good moments, 2 awkward and 2 bad ones at a time.

## Unreleased - door fixes
- Nobody knocks at a home with no one in it: if no family member who can answer is inside the zone, the way in is simply closed (no knock, no waiting), and a knock that loses its last possible answerer is given up after a few tries.
- A knock is made at a real way in (a door, or where one room meets another), never at furniture such as an animal bed or a spot inside a room.

## Unreleased - role icons
- The supplied role icons replace the letter tags on the colonist bar: one emblem per pawn for their most senior public role, with a tooltip and a click that opens their family. Settings for visibility, size, corner, associates and leadership-only. The Family tab lists every role the played character may know with its icon. House Protector has its icon, label and description reserved but nothing assigns the role yet. See docs/ROLE-ICONS.md.

## Unreleased - fixes after first play
- Visitors and guests no longer wander into family areas (unless let in or at a family gathering). A house with one person in the colony no longer shows the Hd tag.
- Teachers speak from a lectern in an enclosed room; lessons last two game hours; check-ups happen beside a hospital bed and last about five seconds at triple speed.
- Door visits: only a family member who is inside the zone comes to the door; the one who lets a guest in stays with them until the visit ends; a guest with nothing to carry on with spends a while with their host, then leaves; a knock nobody answers is given up and not repeated for about half a day.
- Children's beds: parents are housed before their children, and a child goes to the bed nearest a parent's by the walk (not a straight line), including when the parent keeps their own bed. A child whose bed is outside the family's zone, or whose bed this mod gave out and is now clearly farther from a parent than a free one, is looked at again.

## Unreleased - heirlooms, estates and the reunion rescue quest
- Add personal possessions and heirlooms (weapons, garments, books, art), kept apart from family stock claims, with an estate for each death, designated heirs, claims and disputes, mediation and compromise, and real physical delivery by the heir; ordinary stacked supplies stay communal. Head succession and inheritance are separate. The person you are playing is asked (accept, let go, press a claim, or mediate) and silence never counts as consent.
- Replace the letter-only rescue destination with a real Quest-tab quest, a world site and a generated map where the stranded relatives are placed as the same pawns, with five kinds of trouble, honest endings (declined, ignored, abandoned, site lost, lost, partial survival, succeeded) that can each happen only once, and credit for the actual rescuers. Older saves with a stranded party still resolve.
- New developer actions for heirlooms, estates, deliveries and each rescue ending; a read-only validation; timings added to the performance capture.
- Offline only: domain tests, source-contract tests, a simulated save/reload, 17 new mutation checks and both game builds. Nothing here has been run in RimWorld.

## Unreleased - community posts, camping and visiting kin
- Add four community posts: a teacher who holds a class every few days (adjustable, adults optional), a doctor who gives everyone a check-up twice a year in the medical room, a hall keeper who cleans the hall and serves meals to people who sit and wait, and a chaplain named by the ideoligion. People stand for a post for a reason (a calling, a personal aim, a sponsoring house, or duty) and only one post each; houses with a need want the matching post and its holder's kin are served first and learn or heal more. Elected posts are chosen at a yearly assembly where the incumbent's graded year weighs in the vote; add player-authorized camping trips and relatives who really arrive for a grand reunion, with a rescue window and a safe hand-back.
- A player-controlled head is asked for their council leadership ballot; a union a head accepted under a council now finishes when the council approves it.
- Offline tests only; nothing here has been run in the game.

## Unreleased — family stories and supplies

- Add 48 tradition episodes with independent directed bond outcomes and real-context eligibility.
- Record witnessed harm, death, theft, refusals and restitution; share news through physical conversations.
- Add household supplies, ownership-preserving stack handling, collection, boundary negotiation, debts and desperate theft.
- Add adult revenge recruitment and social fights; optional lethal melee plots default off.
- Make family areas preferred homes with map-scoped identity and conservative legacy restriction migration.
- Add incident/plot/supply history, forgiveness controls, settings and bounded story planning.
- Domain tests and offline builds do not replace the live acceptance pass in docs/FAMILY-STORIES.md.

## Unreleased - performance at accelerated speeds
- Cache family-zone work per map, invalidate on relevant changes with debounce, retain a periodic fallback, and revalidate targets. Active visits stay responsive; discovery rotates maps/sites and enforces the three-visit limit.
- Use spatial social searches with rotating, bounded shared-activity candidates; reuse pair contexts and eligibility calculations.
- Compare observed facts before structural reconciliation, with a full six-hour fallback and forced manual resync. Continue hourly observation and relationship simulation.
- Group bond lookups once per sweep, stagger independent maintenance, cache family-zone member IDs and UI layouts/lists, and rank door openers before path checks.
- Add opt-in timing/allocation diagnostics, regression and mutation checks, and a documented 3× profiling procedure. No live TPS gain is claimed.

## Unreleased - grounded relationship moments
- Face-to-face moments require living, spawned participants on the same map; a pending third-person event cancels when its participant leaves.
- Ordinary adjacent labor no longer masquerades as a community aid day. Deliberate aid still needs a request and beneficiary mechanic.
- Two-person events are no longer multiplied by the number of nearby witnesses; duplicate contexts do not add selection weight.
- Meaningful moments respect both participants' daily limit, including during activities; ambient moments remain available. Pending conversations recheck current eligibility.
- Lessons follow their event's teacher and learner roles and select usable skills. Cross-family apprenticeships retain both their teaching and family-standing effects.
- Added regressions and event mutation checks; in-game validation is still required.

## 0.13.4 - find out why pawns stay in the zone
- New debug action 'Why is the selected pawn staying in their family zone?': prints the pawn's family, restriction, schedule, current job and work priorities, runs the work-time check once and prints the restriction afterwards.

## 0.13.3 - family zones no longer stop work
- Pawns restricted to a small family zone could only work inside it, so they stood around. The restriction is now lifted during work and 'anything' hours and put back for sleep and recreation (checked every couple of seconds). A setting turns this off. Only people holding the family's area (or none) are touched.
- Side effect: because workers are free during work hours, other families' workers also walk into a zone then, so the door/knock only matters outside work hours. Not yet verified in game.

## 0.13.2 - beds inside family zones
- A bed that is even partly inside a family zone now has all of it, and the cells beside it where a sleeper stands, added to the zone (or to the family's area if another zone owns the cell). A bed with a corner outside the area could not be claimed or walked to. Checked every sync, so zones painted earlier are repaired.
- New debug action 'Why can the selected pawn not use a bed?' lists every bed, whether it is outside the pawn's area, forbidden or unreachable, and who owns it.
- Not yet verified in game; if it still happens, run that debug action on a pawn and send me the log.

## 0.13.1 - knocking at the door
- A worker who needs into another family's zone now goes to the edge and knocks (a visible 'Knock, knock' every few seconds). Whoever is nearest in that family who is awake and able is called to the door and stops what they are doing to come; if they don't come, the next knock calls someone else. The worker waits a reasonable while after arriving, then gives up and carries on. Before, an adult was chosen once and the worker could be left waiting.
- Not yet verified in game.

## 0.13.0 - relationship events
- 90 relationship moments across nine groups (within a family, between friends and others, between families; adult with adult, adult with child, child with child), all built on one engine that separates opportunity, motivation, execution, reception and consequences. See docs/EVENTS.md.
- People outside the family now get bonds of their own once something has brought them together. Two families have a standing built from incidents and shared moments, never averaged from members. Promises the mod itself records can be kept or broken.
- Moments are real: two free people walk up and talk (or go for a short walk), or the moment happens during something already shared: a meal, a picnic, a gathering, a story, teaching, a bedside visit, a game, shared work.
- Two families with a real grievance are invited to a gathering to mend things.
- New Moments tab in the Families window, settings to turn groups off, set how often, and set how much you are told, and a debug action that explains why an event is or is not possible for two pawns.
- 22 new mutation checks. Not yet verified in game; see docs/EVENTS.md for what is smaller than the design.

## 0.12.3 - furniture counts
- Family zones can now be painted over beds, tables and other furniture (the game's own zone tool refuses them). Walls and fog are still excluded. Zones painted earlier need repainting over the furniture.

## 0.12.2 - roofs
- Building and removing roofs inside another family's zone now brings a builder to the door too (roofs are cells in the roof areas, not blueprints, so they were missed).

## 0.12.1 - doors find the work themselves
- The builder never approached another family's zone because the game never offers work outside a pawn's area, so my first hook never fired. Now the mod checks construction and repair jobs inside family zones every half second and sends an idle colonist from another family (who has Construction or Repair enabled) to the edge to be let in. Their own area is lifted for the visit and put back after, including across a save. Turn on Log events in the mod settings to see each step in the log.

## 0.12.0 - family doors, clearer zones, zone toggle
- A colonist stopped from building inside another family's zone now waits at its edge until a member of that family lets them in (an adult can let anyone in; a child can only let in another child). They build, and the pass ends when they leave. Only construction (frames and blueprints) triggers this.
- Family zones are now a faint tint (5% opacity) so everything underneath is clear.
- New toggle in the bottom-right map controls that shows or hides only family zones.
- Not yet verified in game.

## 0.11.0 - family zone is a real zone type
- New **Family zone** tool in Architect > Zone (paint, then Delete Zone to shrink or remove). Choose the family from the zone's button or the Families > Zone tab; every family member, including those born or married in later, is kept to it. The zone owns a matching allowed area that follows its cells.
- Not yet verified in game.

## 0.4.1 (unreleased) - bond rework from simulation
- New headless five-year simulation of three families (Source/FamilyLife.Tests/SimulationTests.cs; report in the test output folder). It showed the old math let every family settle at 61-76 attachment regardless of character, with no rifts.
- Attachment now settles mainly by how the two actually feel about each other; being related or sharing a house sets only a floor. Time together helps in proportion to liking, helps less the closer they already are, and grates (adds tension) when one dislikes the other. Result in the simulation: warm family about 57, mixed about 37, guarded about 33, and clashing pairs open lasting rifts.
- The Families list no longer shows families of one (unless opened directly from a surname or the Family button).
- 169 tests; mutation check 46 rules, all caught.

## 0.4.0 (unreleased) - Phases 4-7: identity, major events, conflict, polish
- Family culture (values drawn from members' traits and shared life), legacies, defining memories, and traditions the family takes up and holds as gatherings (mute or hold now in the Traditions tab).
- Gatherings through the game's own party system for traditions, coming of age and memorials; anniversaries, reunions and missing someone.
- Dates for partners with a trusted sitter for children, teaching between relatives, baby bonding (hold, sing, play), bedside visits.
- Rifts between relatives, sibling neglect and rivalry, reconciliation during family time, couple health that scales the game's breakup chance. Affairs are off by default; strict romance is available.
- New settings for all of it; chronicle, Overview, Traditions and Relationships screens extended. Mutation check covers 42 rules. Not yet tested in the game: see docs/PHASE4-7.md.

## 0.3.0 (unreleased) - Phase 3: everyday family life
- The Families screen opens from a Family button on the bio card (and the surname link or the pawn's Family tab) instead of the bottom bar. It is now a normal window you can drag and resize.
- Family time: relatives spend time together during joy time, chosen by attachment, how long since they were last together, and tension.
- A soft daily family-time budget (default 2 hours) that only keeps new activities from starting.
- Bedtime stories: in the hour before a child's bedtime the free parent with the best Social skill reads to them, fetching a book if one is nearby. The child learns a little.
- Eating together: relatives who finish a meal in the same room around the same time grow closer and get a small mood lift.
- Settings for all of it. Offline Harmony check now covers the meal hook; mutation check covers 23 rules.

## 0.2.0 (unreleased) - Phase 2: relationships
- Directed bonds (attachment, trust, tension, familiarity) between relatives, partners and housemates, driven by time together, the game's opinion, and family events.
- Household closeness (derived), left-out detection, and a Relationships tab.
- Deeper, temporary grief for close relatives when someone dies (setting, on by default).
- Surnames on character cards open the family.
- Fixes from the code check: ancestry no longer erodes when the game discards old pawns; dead partners are remembered; vanished partners are not reported as breakups; a colonist who dies with no relatives around is recorded as dead; the relative-walk cap never drops colonists; selection updates when the window is already open.
- Offline check that every Harmony patch matches the game (1.6 and 1.7); mutation check script.

## 0.1.0 (unreleased) - Phase 1: foundation
- Durable family records: people, lineages, households, a bounded chronicle.
- Lineages survive the death of their founder and are never recomputed from a changing surname.
- Households: partners who move in together form one, children stay with parents, adult children stay until they pair up, anyone can be moved by hand.
- Families window (bottom bar) and a Family tab on colonists.
- Builds for RimWorld 1.6 and 1.7. Requires Harmony only.

