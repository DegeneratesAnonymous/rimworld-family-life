# Governance slices for issues #11–#15

Pure rules live in `Source/FamilyLife/Domain` and are covered by offline tests. The game layer (`Game/Governance*.cs`) adds the facts the domain cannot know (who is free, which maps exist, who is the Perspective Shift avatar) and the physical actions. Nothing below has been run in a game: compilation, the offline tests and the Harmony check are the only evidence. Setting: **Leadership, institutions and house politics** (off keeps only the records).

## What happens in the game

- **#11 planning card.** Each wedding, funeral, reunion and coming-of-age request gets a card (Families → Manage → Occasions). Time (bounded window), organizer, map and guests have family-chosen defaults that confirm themselves at the deadline. The gathering uses the organizer, map and guest list; a chosen meeting place becomes the spot. The avatar is asked to lead, never assumed or directed.
- **#12 Steward duties.** One house per pass. The Steward (else the head) hears a petition, looks after a guest, mediates a rift, oversees a supply request, negotiates a marriage alliance or organizes an occasion. Each is a walk to the other person (or the existing negotiation/supply/gathering systems) and counts only when its evidence exists: a finished ceremony, a recorded hearing, a one-time token from the finished meeting, or goods actually home. Interruptions are recorded as such. The optional meeting place is set by clicking a cell, and is used for gatherings and as the polling place.
- **#13 succession.** Runs for every house from the maintenance cycle (orderly, negotiation, compromise, dispute, branch). Candidate legitimacy and its reasons show in Families → Manage, filtered to what the viewer knows.
- **#14 crime families.** A house adopts theft only when need, a real target, permitting values, two willing members and a precedent all hold. The organizer recruits members one at a time by talking to them; each answers from their own bonds (join, refuse, expose). A ready job starts real thefts of real stock through the existing supply system; the job resolves from what that system recorded (goods home, witnesses, the theft incident). Heat rises when found out; a house can reform (restitution recorded by the supply system counts) or simply stop.
- **#15 institutions.** The Manage tab sets the colony's institution (consensus, council, election, one ruler) and calls elections. House heads endorse by their own judgment. Voters walk to the polling place and cast their own ballot; absent people do not vote. A term end opens the next election. Aggrieved, unattached adults may start a scheme against the leader; the organizer recruits by talking, preparations are bounded and can be discovered, set back or reconciled, and the confrontation is a walk to the leader that decides the outcome by capacity, not headcount.

## Also in the game

- **Protection deals.** A house whose past acts were mostly menace, with a poorly protected neighbour holding silver, may offer protection. The two heads talk; the neighbour accepts out of need (unsafe and trusts them) or fear (a coerced deal, which also puts an intimidation on record) or declines. Tribute is collected every few days through the supply system and recorded only when the silver really arrived. Reforming ends the deal.
- **Intimidation jobs.** The same job flow as theft, but the participants demand goods (the donor can refuse).
- **Councils.** A council names the colony's leader when the post is empty or the term ends; each seated house's head casts one vote by their own judgment.
- **Contraband, laundering and favors.** Willing-seller jobs and calling in favors. A house that holds stolen goods can sell them to a third house's member for silver, and the ownership record then follows the sale (so the original owner can no longer reclaim them through restitution). A house holding drugs can sell them to someone already dependent on them. Both use the normal barter flow and record no theft: nobody is wronged, but the house draws attention. A favor exists only because of a real recorded gift to someone in another house; calling it in is an ordinary request for goods, honoured or refused by the other side, and the outcome changes trust.
- **Council powers.** Under a council, a union of two houses needs the seated heads' approval (the first attempt raises the question, a later attempt goes through once it is approved; a union that would hold most of the colony's adults is refused), and a house of known bad conduct can be censured, which costs it standing. A head only votes on what they know; a head you control has a window to answer and is otherwise not counted.
- **Player-controlled pawns.** Questions put to a pawn you control (a scheme, a job, a protection offer, a request to hear, an open ballot) wait in the Families window under "Waiting for your answer". Nothing answers for them; an unanswered question counts for nothing.

## Not built

- Bringing distant relatives to the colony (travel, rescue, camping): they are listed with the reason, and only relatives already present can come.
- Inheritance of anything beyond the office of head is deliberately not attempted.
## Rules the tests pin down

- Ignoring a card never stalls an occasion; an invalid room, a lost organizer, a dead guest or a passed time repairs itself or ends the request cleanly.
- A Steward success needs evidence of a finished action, and the same evidence cannot count twice. The head covering for a missing Steward earns regard but adds nothing to a Steward's term. Ceremonies, aid, reconciliation by the rift system and negotiation are credited by the systems that perform them, so a duty does not credit them again.
- Legitimacy is separate from prestige, standing and office. A large family does not automatically produce a ruler. Members back different candidates for their own reasons. Private designations stay private.
- Crime needs all five conditions. Goods and tribute exist only with a one-time token or the supply system's own record. Witnesses know facts; reports and suspicion are never proof.
- Political support keeps ideology, family, legitimacy, friendship, fear, needs, policy and endorsement as separate measures. A smaller house can win through trust and conviction. A coup needs capacity (equipped, free people), not headcount.
