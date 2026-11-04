# Open items and completion evidence

Inventory taken from GitHub issues #2, #3, #4, PR #6, and ROADMAP.md. Implementation, offline evidence and live acceptance are separate states. This file does not mark the full dynasty roadmap complete.

| Item | Implemented / evidence | Still needed |
|---|---|---|
| #2 Map identity and movement policy | Preferred homes no longer enforce pawn allowed areas; stable map identity persisted. Shared domain resolver is now used by game lookup, with collision, missing-map and ambiguous-legacy tests. | Live two-map save/reload, caravan and player-restriction acceptance. Legacy assignments with ambiguous maps require explicit reassignment. One family has one preferred home; members on other maps retain player movement policies and do not get a substitute home. |
| #3 Coincidental work vs deliberate aid | Idle discovery only considers recreation, excludes family-time recursion, and now requires the same physical target. Nearby workers cannot trigger CommunityAid through discovery. Domain policy tests protect the classification. | Dedicated requested labor assistance remains future work. Existing actual supply gifts can benefit another family but are not falsely labeled organized labor. Live adjacent-work and shared-TV/game tests. |
| #4 Outcome calibration | Reproducible five-year (300-day) seeded harness, five profiles/history cases × isolated, hourly and scripted-recovery modes; per-ID/tier counts and daily trajectories. Regression for high-execution hostile slight with unreceptive target. See calibration/summary.md. | Controlled resolution probes cover all 138 definitions (90 core + 48 tradition) × five profiles × 100 seeds, but bypass eligibility and do not apply consequences. Natural scenario coverage is incomplete: the long runs select 39 of 90 core definitions, and no tradition episodes. Expand third-party, achievement, distress and bespoke contexts. Actual-game activity distributions, time lost, mood effects and event frequency still need live measurement. |
| PR #6 Individual agency | Gradual authored/third-party/promise changes; capped daily quality gains; duration-matched conversation credit; aims bias eligible choices, independence personal-time jobs, measured parent-child attention and staged reconciliation. | Intentional favoritism/obligation evidence, broader ambitions, live pacing/UI/interruption acceptance. Attention differences do not imply intent. |
| Economy/recovery | Saved pantry reserves, emergency sharing, physical repayment collection, accessible barter funds, source ownership through onward gifts, staged repair discussions, player food-aid actions. | Live storage/pathing/interruption and public-cash race checks; negotiated compensation, dedicated labor assistance and broader mediation. |
| Social houses | Chosen membership independent of ancestry; admission/departure, branches, newborn affiliation, explicit house unions and adult kinship policy. | Live unions/births/map and reload acceptance; deeper adoption/residence workflows. |
| Crests | Saved editable colors/mark/pattern; shared cached masks; branch variation. | Live rendering/layout acceptance. |
| Associates and colonist agency | Earned per-house standing; associates/trusted associates; persisted heads and delegated stewards; actual admission/recognition/union conversations; acting-as controls and knowledge-filtered story records. | Live permissions, interruption, successor/reload and secrecy checks; richer associate duties/contracts. This is an initial authority model, not full prestige/succession. |
| Prestige/succession | Earned house respect and initial head/steward authority provide a foundation. | Rank vs legitimacy, leadership expectations, succession disputes and inheritance. |
| Political institutions | Design roadmap. Explicit unions require both heads in colonist mode. | Individual elections, family councils, hereditary regimes, individual loyalties and bounded political schemes. |
| Evidence depth | Witness knowledge/news, measured attention differences, private perspective records and actual repair steps. | Report vs fact vs suspicion, discovery, observed resource inequality, deeper tradition actions and positive story chains. |

## Run the offline calibration

```
dotnet run --project tools/StoryCalibration -- docs/calibration
```

The summary, event/tier table, all-ID coverage table and controlled outcome probes are committed. The detailed `history.csv` is reproducible and ignored by git. Run twice with unchanged source to compare byte-identical outputs. Synthetic-hourly mode includes passive updates, not actual game jobs. Recovery changes profiles/opinions deliberately after day 150; it proves capacity for recovery under those assumptions, not organic reform. Initial pawns remain at their starting age/presence. All 138 definitions remain available, but opportunity conditions deliberately exclude missing evidence rather than inventing it. No TPS or pawn work time is measured by this harness.

## Live release gate

Use the checklist in FAMILY-STORIES.md, including two maps, player allowed areas, shortage/storage interruption, witnessed/unwitnessed crime, plots and save/reload. Developer action **Wil's Family Life → Validate story state (read only)** logs unresolved homes, ownership and active-cause problems without mutating the save. Run it before and after scenarios; transient plot cleanup may need a maintenance interval. A clean snapshot is not a playtest pass.

Use the existing performance capture to compare the same 30/60/100-pawn save at 3× with the new systems on/off, checking jobs/pathing as well as planner timings. There is no running RimWorld installation in this execution environment; these gates remain open rather than being inferred from compilation.

## Order of further work

1. Run the expanded live checklist in HOUSES-AND-ROLEPLAY.md, including save/reload, interruptions and measured 3× TPS; complete missing calibration contexts.
2. Refine evidence, positive cooperation, associate duties and compensation using playtest findings.
3. Build prestige/legitimacy and succession before elections, coups and deeper political schemes.

## Offline checks for this pass

408 tests pass; 1.6 and 1.7 Release builds pass with zero warnings/errors. All 18 Harmony patch definitions match both versions. Four new deliberate breaks (unfinished personal time, associate management, same-day respect farming, ancestry replacement) are caught; restored sources pass. Refreshed calibration files reproduce byte-for-byte across two runs, with 69,000 controlled resolutions across all 138 definitions. Calibration outputs remain synthetic; no live acceptance item is closed by these checks.
