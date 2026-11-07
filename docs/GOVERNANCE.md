# Governance slices for issues #11–#15

Pure rules live in `Source/FamilyLife/Domain` and are covered by offline tests. The game layer (`Game/Governance*.cs`) adds the facts the domain cannot know (who is free, which maps exist, who is the Perspective Shift avatar) and the physical actions. Nothing below has been run in a game: compilation, the offline tests and the Harmony check are the only evidence. Setting: **Leadership, institutions and house politics** (off keeps only the records).

## What happens in the game

- **#11 planning card.** Each wedding, funeral, reunion and coming-of-age request gets a card (Families → Manage → Occasions). Time (bounded window), organizer, map and guests have family-chosen defaults that confirm themselves at the deadline. The gathering uses the organizer, map and guest list; a chosen meeting place becomes the spot. The avatar is asked to lead, never assumed or directed.
- **#12 Steward duties.** One house per pass. The Steward (else the head) hears a petition, looks after a guest, mediates a rift, oversees a supply request, negotiates a marriage alliance or organizes an occasion. Each is a walk to the other person (or the existing negotiation/supply/gathering systems) and counts only when its evidence exists: a finished ceremony, a recorded hearing, a one-time token from the finished meeting, or goods actually home. Interruptions are recorded as such. The optional meeting place is set by clicking a cell, and is used for gatherings and as the polling place.
- **#13 succession.** Runs for every house from the maintenance cycle (orderly, negotiation, compromise, dispute, branch). Candidate legitimacy and its reasons show in Families → Manage, filtered to what the viewer knows.
- **#14 crime families.** A house adopts theft only when need, a real target, permitting values, two willing members and a precedent all hold. The organizer recruits members one at a time by talking to them; each answers from their own bonds (join, refuse, expose). A ready job starts real thefts of real stock through the existing supply system; the job resolves from what that system recorded (goods home, witnesses, the theft incident). Heat rises when found out; a house can reform (restitution recorded by the supply system counts) or simply stop.
- **#15 institutions.** The Manage tab sets the colony's institution (consensus, council, election, one ruler) and calls elections. House heads endorse by their own judgment. Voters walk to the polling place and cast their own ballot; absent people do not vote. A term end opens the next election. Aggrieved, unattached adults may start a scheme against the leader; the organizer recruits by talking, preparations are bounded and can be discovered, set back or reconciled, and the confrontation is a walk to the leader that decides the outcome by capacity, not headcount.

## Not built

- Bringing distant relatives to the colony (travel, rescue, camping): they are listed with the reason, and only relatives already present can come.
- Protection deals and the other crime directions have rules and tests but nothing starts them; only theft is adopted.
- A Perspective Shift avatar is never directed: they do not vote, answer scheme or job recruitment, or hear petitions on their own. Those choices are left to the player acting through the avatar and there is not yet a menu for them.
- Councils and the institution kind change who is listed as leader; they do not yet gate other decisions.
- Inheritance of anything beyond the office of head is deliberately not attempted.

## Rules the tests pin down

- Ignoring a card never stalls an occasion; an invalid room, a lost organizer, a dead guest or a passed time repairs itself or ends the request cleanly.
- A Steward success needs evidence of a finished action, and the same evidence cannot count twice. The head covering for a missing Steward earns regard but adds nothing to a Steward's term. Ceremonies, aid, reconciliation by the rift system and negotiation are credited by the systems that perform them, so a duty does not credit them again.
- Legitimacy is separate from prestige, standing and office. A large family does not automatically produce a ruler. Members back different candidates for their own reasons. Private designations stay private.
- Crime needs all five conditions. Goods and tribute exist only with a one-time token or the supply system's own record. Witnesses know facts; reports and suspicion are never proof.
- Political support keeps ideology, family, legitimacy, friendship, fear, needs, policy and endorsement as separate measures. A smaller house can win through trust and conviction. A coup needs capacity (equipped, free people), not headcount.
