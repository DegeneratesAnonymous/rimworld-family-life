# Family Life roadmap

## Direction and constraints

Build a colony social simulation centered on families and individual agency. Relationships normally change over days and seasons. Real deaths, betrayals and rescues may create immediate shocks; repair requires sustained behavior. Avoid permanent villain/black-sheep labels, automatic family-wide knowledge, unconditional size bonuses, and ideology/family/royalty as a zero-sum allegiance formula. Keep tribal through spacer colonies plausible and budget simulation work for 3× speed.

Current baseline: family records, households, bonds, culture, gatherings and activities; 138 event definitions; witnessed incidents, supplies and revenge. The current PR passes 408 domain tests and both builds. Live acceptance remains outstanding in FAMILY-STORIES.md and HOUSES-AND-ROLEPLAY.md.

## Milestone 1 — individual agency within families (implemented slices; live validation pending)

First slice: ordinary authored social moments use one quarter of previous primary bond changes in game contexts. Incident shocks remain separate. Add a pure directed belonging assessment from existing relationships; family size cannot manufacture loyalty. Assessments are derived, not saved labels, and make no claims about actual political behavior.

Design scope (implemented foundations and remaining depth):
- Persist personal ambitions (recognition, leadership, independence, security, protecting someone), evidence-backed progress and abandonment. Do not automatically assign evil motives from traits or stepfamily status.
- Track actual favoritism, unequal obligations, inclusion and rejection; surface why a pawn feels supported or excluded. A compassionate dissenter can be a family's black sheep.
- Support divided loyalties: a member can love one relative, distrust another and oppose the house leadership.
- Show motives and belonging in the Families UI before allowing them to drive consequential actions.
- Audit remaining direct bond changes, positive and negative rates, mood effects and daily accumulation; the initial pacing change is not a universal rate limiter.

Acceptance: gradual ordinary changes in both directions; major incidents retain justified impact; per-pawn assessments remain directed; absent/dead members do not count as current support; no new all-pairs tick scans; interrupted save/reload and 3× live profiling pass.

## Milestone 2 — household economy and recovery

Pantry/reserve controls, accessible barter, physical repayment errands, emergency sharing, negotiated compensation, mediation and sustained trust repair. Positive chains (help → gratitude → mutual aid → refusal to betray a benefactor) deserve equal depth to feuds. Observe actual inequality before generating envy. Validate storage, pathing, interruption, multiple maps and TPS before release.

### Dynamic house ambitions foundation

Houses now periodically assess compact, saved situation snapshots rather than following a fixed expansion script. Food security, suitable family beds and spare capacity, safety, continuity, essential skill coverage, cohesion, influence, independence and legacy compete to become a stable primary and secondary ambition. Ordinary goals remain in place for at least three days; only severe hunger or danger can interrupt them. Prosperous houses with room and eligible adults may want to expand their legacy, while crowded or hungry houses first pursue quarters or provisions. The same assessment identifies the weakest practical skill so mentoring can deliberately build capability inside the family.

Power disparity contributes to intimidation, but does not by itself manufacture a feud. Concrete incidents and grievances make that pressure sharper. Protection ambitions require both an actual threatening house and a separate house with positive shared history. The planner consumes existing family-zone and bed ownership results, including Wil's Better Beds locks and assignments, rather than running a second bedroom allocator.

This is a motive layer, not a completed marriage-alliance system. Betrothal negotiation still needs eligible-pair consent, compatibility, ideology, family ties, housing and provision capacity, bargaining, disallowance, and a real marriage before any household or house merger. Generic admission, self-requested recognition and trust-only autonomous unions have been removed from current agency; old saved petitions remain readable for compatibility.

The first marriage-alliance slices now keep house interest, each pawn's willingness and the game's romance eligibility separate. Relevant ambitions perform a bounded daily search, heads or Stewards physically negotiate, and each proposed pawn is then approached independently. A selected pawn or Perspective Shift avatar must answer through that pawn's context actions; autonomous pawns answer from their own recorded willingness. Refusal ends the proposal, mutual consent still creates no romance relation, and only people who learned of the discussion can see its private response details in acting-as mode. Family opposition can coexist with personal interest as recorded defiance potential, but does not force secret romance. Vanilla remains responsible for any eventual romance and marriage, and an actual cross-house marriage remains mandatory before a house union can be considered.

## Milestone 3 — social houses, membership and branches

Separate biological kinship, household residence and social house membership. Support adoption/acceptance, departures, branch formation and a player uniting the colony into one house without erasing ancestry or individual disagreement. Close biological and adoptive parent-child relationships remain excluded from romance; distant adult eligibility uses an explicit genealogical policy, never a shared surname or house label. Missing ancestry is not evidence of unrelatedness.

Large houses gain capacity through actual caregiving, teaching, resources and cooperation, with diminishing benefits and obligations. They can be generous patrons or exclusionary elites; size alone does not choose their conduct.

## Milestone 4 — crests and visible identity

Player-editable colors, emblem and background. Branches may inherit variations. Save identity independently of generated textures. Use small cached assets; no per-tick image work.

## Milestone 5 — prestige, legitimacy and succession

Separate earned personal prestige, collective family standing, recognized rank/office and perceived legitimacy. High rank can benefit a house without making every relative respected. Build expectations, favoritism, inheritance disputes and succession responsibilities. A scheming spouse/guardian, overlooked heir, reluctant successor or aspiring tyrant should emerge from motives and actions, not assigned story roles.

### Organic ceremony foundation

The current ceremony slice deliberately precedes political power. Families now create their own funeral plans after a death, wedding receptions after RimWorld records a real marriage, and annual grand-reunion plans when living relatives are away and family culture or actual missing-person bonds support it. Plans are staggered, bounded and explain which conditions shaped them. The player cannot choose an outcome from a control panel: family space, attendance, relationships, culture and support from the head or steward affect what happens.

Ceremonies use the existing party machinery and add 26 occasion-specific interactions (8 wedding, 8 funeral and 10 reunion). Organizers earn small, capped personal prestige through successful public service; the house receives a small standing change. This is groundwork for succession rather than a claim that succession is complete. Exact distant-relative travel, multi-day reunion quests, legitimacy, inheritance, elections and coups remain separate slices so a failed visitor or political system cannot corrupt the family record.

## Milestone 6 — political institutions and schemes

Support individual elections, house councils and hereditary arrangements. Ideological conviction, family attachment, authority legitimacy, friendships, ambition, fear and immediate needs overlap and depend on the decision. Low certainty and weak family attachment do not automatically imply royal loyalty. Separate obedience, affection and belief.

Schemes need a concrete goal, bounded actions, willing recruits, discovery, setbacks and exit conditions. Numerical strength enables a coup but does not guarantee support or victory. Small houses can seek sponsorship, build counter-coalitions or earn leadership. Preserve player orders and emergency priorities.

## Milestone 7 — evidence and narrative depth

Distinguish witnessed facts, received reports and suspicion. Develop discovery without inventing guilt. Give traditions distinct physical activities and obligations, not only new text. Expand secret support, betrayal, moral limits, reform and reconciliation using existing evidence/action systems.

## Release gates

Domain and persistence checks, both supported builds, Harmony checks, then live tribal/spacer and large-colony acceptance including save/reload and measured 3× TPS. Merge status is not release validation. Detailed balancing numbers remain provisional until playtested.

### Milestone 1 follow-up — lived experience and personal aims

Implemented on the individual-agency branch: persisted per-adult records of positive/difficult completed family moments, source event/date, and goals for connection, recognition or independence. The first qualifying moment per in-game day counts; three days establish a goal, eight further favorable days can fulfill a connection goal when a trusted connection exists, or a recognition goal through achievement-context episodes, setbacks reduce progress, a seven-day cooldown prevents immediate restarting, and fourteen days without evidence or leaving the colony sets an active aim aside. Recognition requires achievement-context episodes. Independence progress measures resolve; three completed personal-time sessions on separate days can fulfill a need for breathing room, without forcing departure. Mixed directed outcomes can count differently for each participant. Neutral/ambient moments do not count, and lack of interaction is never automatically exclusion. Family changes reset the old context.

Families → Moments now explains current trusted/strained connections, experience and aims (up to 24 current adults per family). The model does not infer intentional favoritism, force departure, or manipulate elections/political schemes. Those deeper behavior integrations remain future work. The recorded experience is a foundation for perceived inclusion, not proof of anyone's intentions. No new all-pairs tick loop: evidence updates on completed events; goal maintenance joins existing staggered maintenance.

Live acceptance still required: save/reload progress and cooldowns, adult transitions, changing families, mixed-result episodes, UI scrolling, and 3× performance with many families.

## Current implementation checkpoint

See [HOUSES-AND-ROLEPLAY.md](HOUSES-AND-ROLEPLAY.md) for the implemented mechanics and their limits. Milestone 1 now includes bounded agency biases, actual personal time, a measured parent-child attention ledger, daily quality caps and staged repair. Milestone 2 has pantry controls, emergency sharing, physical repayment and barter-fund collection. Milestone 3 has chosen membership, departure, branches, explicit unions and kinship checks. Milestone 4 has editable persisted crests with shared cached rendering. These slices remain subject to live acceptance rather than being declared release-complete.

The associate/colonist layer adds earned respect without automatic membership, head/steward roles, permission-aware player actions, physically delivered petitions and private story-record views. It supports chosen crews and rival houses while preserving ancestry. Ceremony organizers can now earn capped personal prestige and houses can gain or lose standing, but initial persisted head selection remains a compatibility fallback; legitimacy, inheritance and succession disputes remain open. Milestones 6–7's political institutions, deeper evidence and physical tradition activities remain future work.

Stewardship is now a recorded office rather than a generic status request. Heads appoint members who meet service, trust and skill expectations; active and past terms record ceremonies, aid, reconciliation and later negotiation/guest/supply work, including failures. The office currently delegates authority and records existing services. A physical office/meeting place, proactive steward jobs and succession weight remain later work.
