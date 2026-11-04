# Family stories and household supplies

This expansion connects observed events, individual knowledge, household needs, and consequential actions. It adds 48 tradition episodes to the existing 90 social events. Game jobs, pathing, and storage still need a live colony acceptance pass; compiling and domain tests cannot establish in-game behavior or TPS.

## Narrative goals

| Story | Root and progression | Ways out |
|---|---|---|
| Blood feud | Actual injury/death → witnesses and conversations → grievance → an adult proposes revenge → another adult joins, refuses, or discloses it → confrontation/social fight or an optional melee attack | Friendship with target, refusal, disclosure, forgiveness, successful reconciliation, expiry, absence |
| Desperate household | Shortfall at home → collect reachable public surplus → approach another family at its boundary → donor considers its own needs and trust → gift, loan, barter, refusal or intimidation | Supplies physically arrive; public reserve stays available; player orders take precedence |
| Theft and restitution | Urgent need or a recognized kleptomaniac trait, prior refusal, and visible stock → physical theft → witnesses learn who acted; stolen goods retain their owner → request their return | Matching goods returned in full, or informed forgiveness |
| Uneasy reconciliation | Actual known wrong or open rift → a tradition creates a conversation → trust, closeness, tension, and familiarity change independently | Great forgiveness/reconciliation episodes can resolve the known incident |

A family tie summarizes grievances already learned by at least one involved living observer. It is not proof every family member knows the cause. Unknown attackers stay unknown. News requires an actual nearby conversation. A murdered victim cannot posthumously broadcast the killer's identity. Accident classification uses the damage's intended hostile target, not the attacker's current job. Mental-break context reduces blame but does not erase it.

## Tradition episodes

Each row has six scenes, each with poor, flat, good and great outcomes. Contextual scenes require actual records, skill differences, unfamiliarity, resource pressure, grief, or differing values; they do not invent achievements or wrongdoings. The tradition's value affects motivation.

| Value / tradition | Six episode themes |
|---|---|
| Hearth / feast | Welcome a guest; pantry shortage; promise time together; empty place; hospitality disagreement; meal toward peace |
| Scholarly / reading | Unexpected teacher; unfamiliar mind; recent discovery; correction; recorded family history; difficult honesty |
| Martial / hunt | Experience; remembered injury; courage versus caution; respect across families; promise support; quarrel set aside |
| Craft / craft fair | Preserve technique; recognize progress; scarce supplies; standards; new acquaintance; promise teaching time |
| Revelry / party | New participant; celebrate progress; taste clash; outside friendship; cost of celebrating; uneasy truce |
| Faith / vigil | Company in grief; admission; forgiveness; ways of remembering; outside respect; family memory |
| Status / founders day | Credit; newcomer's place; contested history; obligation; prosperity and need; public peace |
| Devotion / reunion | Rediscovery; outside connection; broken promise; family obligations; shared memory; reconciliation |

Closeness uses the existing `Attachment` field. Familiarity can rise without affection; an admission can ease tension while costing the listener trust. Only the three explicitly promised-time scenes create commitments. Existing daily social-event limits and pair cooldowns still apply.

## Supplies and homes

Family areas are now preferred homes. The mod no longer repeatedly changes pawn allowed areas. Existing restrictions recorded as applied by the old mod are released only when they match the resolved home on the same map. Legacy areas with ambiguous map identity need explicit reassignment. Player restrictions still control paths and stock access.

Use shelves inside the family area, with suitable storage filters. Vanilla zones cannot overlap other zones: for floor stockpiles, extend the home's linked allowed area over those stockpile cells. Unclaimed goods are public; physically collecting them establishes ownership. Existing piles in a home are not automatically confiscated. The planner respects public half-day food and two-item medicine reserves.

| Need | Household target / eligibility |
|---|---|
| Food | Approximately one day (1.6 nutrition per member), edible for the representative pawn; half-day public reserve |
| Fuel | Compatible fuel for refuelable buildings in the home, about two days of consumption; mortar barrels excluded |
| Apparel | Spare clothing matching body, developmental stage, clothing layer, and assigned outfit |
| Medicine | Small reserve when a member needs tending or has Medicine ≥6; donor protection for current tending needs |
| Drugs | Only an actual addiction/dependency and policy allowing addiction use; small reserve |
| Books | One book for a sufficiently scholarly family |

Ownership and possession are separate. Splitting a stack preserves both; differently owned stacks do not merge. Stolen stock remains owned by the victim and held by the thief's family. Normal autonomous hauling does not move private stock into public storage. A household job can recover its own dropped supplies. Drafting and explicit player orders override autonomous ownership restrictions.

A donor only grants after an actual boundary conversation. Barter needs a complete silver stack already in the requester's inventory; the silver physically transfers to the donor's inventory before the supply handover. Loans become debts only after handover, can be repaid by a completed reverse transfer of equivalent food nutrition or identical other goods, and default once after three days. There is no automatic remote payment or dedicated repayment errand. Restitution resolves a theft only after matching goods have been returned in full.

Theft requires a prior refusal, visible reachable goods, and urgency (or recognized kleptomaniac trait); a more desperate family can steal even without an existing grievance. It does not inspect hidden stock to select a theft. A failed or interrupted job creates no completed handover. Interrupted carried goods are dropped and remain recoverable.

## Control and visibility

Settings independently control the story system, supplies, theft, revenge/social fights, and lethal plots. Lethal plots default **off**. Organizers/recruits must be adults; friends can veto or disclose a plot. Drafted pawns, player-forced jobs, emergencies and movement restrictions take precedence. Lethal attack jobs are identified and cancelled when disabled, forgiven or interrupted by drafting; expiry limits attempts.

Families → Moments shows incidents, known observers, context, resolution, plot causes and states, and recent supply requests. The player can forgive a victim-family incident there. This is a player overview, so it also reveals secret plots; it does not give that knowledge to pawns.

## Performance budget

The planner gets one opportunity per 600 ticks, rotates maps and homes, gives a family at most one supply attempt per 2,500 ticks, and alternates social planning opportunities with supply planning. Stock snapshots refresh at most once per 2,500 ticks (invalidated transfers have a 300-tick minimum). Searches cap need categories, prospective donors, incident roots and conversation participants. Claim checks use an index; no per-tick map scan or all-pairs relationship update was added. Incident history caps at 512, plots at 64, requests at 128, preserving active causes/debts where needed. Full ledgers can decline a new record rather than discard an active cause.

These are work bounds, not a measured TPS promise. Use the existing opt-in performance capture on the same 30/60/100-pawn save at 3× with this feature on and off. The new metric is `Family story planner`. Check pathing and job counts as well as planner time.

## Live acceptance pass (required before release)

1. Tribal and spacer saves, two homes, appropriate shelves and public food/fuel: observe collection, consumption/refuelling, reserve protection, and no oscillating haul jobs.
2. Restrict the requester with a player allowed area: no forbidden-area bypass. Draft or manually order either negotiator during approach, speech, pickup and delivery: no false transfer or duplicate stack.
3. Trigger need with public storage unavailable: observe boundary conversation, justified refusal, successful physical gift/loan; test silver in inventory for barter and repayment/default once.
4. Force a qualified theft with and without witnesses. Victim-family opinions change only after a living witness learns and can converse; ownership survives splits, merging and dropped carry stacks.
5. Cause injury, crossfire, mental-break killing and an unknown/unwitnessed death. Inspect correct context and knowledge, no per-bullet grievance multiplication, no suspect invented for an unknown cause.
6. Build sufficient grievance, adult loyalty and low warmth/steadiness: observe recruitment, refusal/friend warning, social fight. Separately enable lethal plots and verify actual attacks and interruption by settings/forgiveness/drafting. Never infer a successful assassination from an unrelated death.
7. Hold each tradition under several actual contexts, confirm matching scene text and directed bond changes. Check forgiveness and broken-promise acknowledgement.
8. Save/reload during speech, carrying, an active plot and a debt; repeat across two maps whose area IDs coincide. Confirm no duplicate handovers and independent home assignments.
9. Profile 3× on the same save with these systems enabled and disabled. Measure median/p95 TPS and planner/job/pathing overhead before adjusting frequency.

Known scope limits: no fabricated investigations to identify an unwitnessed thief, no property-law UI for individual stacks, no wealth-comparison envy model, no organized theft conspiracy, and no custom assassination weapon/poison AI. Lethal revenge currently uses bounded vanilla melee jobs. Future story variety should extend the evidence and action systems rather than invent causes in descriptive text.

## Verification for this change

- 333 domain/invariant tests pass, including the existing suite and new narrative/ownership cases.
- Eight added mutation checks are caught: knowledge broadcast, repeated family blame, child organizers, friendship veto, insufficient quantity, stolen ownership, public confiscation, and ownership loss on split. Restored sources pass.
- Release builds pass for 1.6 and 1.7 with zero warnings/errors; offline metadata checks match all 18 Harmony patch definitions in both versions.
- XML parses, keyed labels are unique, and all 240 new tradition localization keys exist.
- No live game, save/reload playtest or TPS benchmark was run here. The tests do not execute Verse job/pathing code.
