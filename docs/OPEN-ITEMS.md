# Open items and completion evidence

Inventory taken from GitHub issues #2, #3, #4, PR #6, and ROADMAP.md. Implementation, offline evidence and live acceptance are separate states. This file does not mark the full dynasty roadmap complete.

| Item | Implemented / evidence | Still needed |
|---|---|---|
| #2 Map identity and movement policy | Preferred homes no longer enforce pawn allowed areas; stable map identity persisted. Shared domain resolver is now used by game lookup, with collision, missing-map and ambiguous-legacy tests. | Live two-map save/reload, caravan and player-restriction acceptance. Legacy assignments with ambiguous maps require explicit reassignment. One family has one preferred home; members on other maps retain player movement policies and do not get a substitute home. |
| #3 Coincidental work vs deliberate aid | Idle discovery only considers recreation, excludes family-time recursion, and now requires the same physical target. Nearby workers cannot trigger CommunityAid through discovery. Domain policy tests protect the classification. | Dedicated requested labor assistance remains future work. Existing actual supply gifts can benefit another family but are not falsely labeled organized labor. Live adjacent-work and shared-TV/game tests. |
| #4 Outcome calibration | Reproducible five-year (300-day) seeded harness, five profiles/history cases × isolated, hourly and scripted-recovery modes; per-ID/tier counts and daily trajectories. Regression for high-execution hostile slight with unreceptive target. See calibration/summary.md. | Controlled resolution probes cover all 90 × five profiles × 100 seeds, but bypass eligibility and do not apply consequences. Natural scenario coverage is incomplete: the long runs select 40 of 90 event definitions. Expand third-party, achievement, distress and bespoke contexts. Actual-game activity distributions, time lost, mood effects and event frequency still need live measurement. |
| PR #6 Individual agency | Gradual primary social changes; persisted aims and source evidence; visible belonging/progress; directed outcomes and lifecycle tests. | Full relationship mutation/rate audit, intentional favoritism detection and ambition-driven actions. UI and interrupted save/reload acceptance. |
| Economy/recovery | Collection, negotiation, ownership, loans, theft and restitution exist. | Pantry controls, repayment errands, accessible barter, emergency policy, negotiated compensation and mediation. |
| Social houses | Biological genealogy and households exist. | Separate social membership, acceptance/departure/branches and explicit adult kinship eligibility policy. |
| Crests | Roadmap agreed. | Persisted player-editable identity and cached rendering, branch inheritance. |
| Prestige/succession | Roadmap agreed. | Earned reputation, rank vs legitimacy, leadership expectations and succession. |
| Political institutions | Roadmap agreed. | Individual elections, family councils, hereditary regimes, individual loyalties and bounded schemes. |
| Evidence depth | Witness knowledge and news exist. | Report vs fact vs suspicion, discovery, observed inequality, deeper tradition actions and positive story chains. |

## Run the offline calibration

```
dotnet run --project tools/StoryCalibration -- docs/calibration
```

The summary, event/tier table, all-ID coverage table and controlled outcome probes are committed. The detailed `history.csv` is reproducible and ignored by git. Run twice with unchanged source to compare byte-identical outputs. Synthetic-hourly mode includes passive updates, not actual game jobs. Recovery changes profiles/opinions deliberately after day 150; it proves capacity for recovery under those assumptions, not organic reform. Initial pawns remain at their starting age/presence. All 90 definitions remain available, but opportunity conditions deliberately exclude missing evidence rather than inventing it. No TPS or pawn work time is measured by this harness.

## Live release gate

Use the checklist in FAMILY-STORIES.md, including two maps, player allowed areas, shortage/storage interruption, witnessed/unwitnessed crime, plots and save/reload. Developer action **Wil's Family Life → Validate story state (read only)** logs unresolved homes, ownership and active-cause problems without mutating the save. Run it before and after scenarios; transient plot cleanup may need a maintenance interval. A clean snapshot is not a playtest pass.

Use the existing performance capture to compare the same 30/60/100-pawn save at 3× with the new systems on/off, checking jobs/pathing as well as planner timings. There is no running RimWorld installation in this execution environment; these gates remain open rather than being inferred from compilation.

## Order of further work

1. Complete calibration contexts and audit remaining unscaled relationship paths; live stability/performance gate.
2. Make ambitions influence bounded existing interactions, observe unequal treatment, and finish economy/reconciliation.
3. Social-house membership/branches and kinship policy before crests and inherited identity.
4. Prestige, legitimacy, succession, elections and schemes, with visible reasons and player controls.
5. Evidence/discovery, positive chains and tradition-specific physical activities throughout.

## Offline checks for this pass

364 tests pass; 1.6 and 1.7 Release builds pass with zero warnings/errors; all 18 Harmony patch definitions match both versions. Three deliberate breaks to map identity, ambiguous-area handling and shared-target checks are caught by tests. No live acceptance item is closed by these checks.
