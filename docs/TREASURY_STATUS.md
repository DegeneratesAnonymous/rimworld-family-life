# Treasury completeness pass: what is real, what is only offline-tested, what needs RimWorld

Supersedes sections 5-8 of `docs/TREASURY.md` where they differ (that document keeps the architecture and the first-pass description). Branch `feature/community-treasury`. **Nothing has been run in RimWorld.** "Verified offline" means unit tests, source guards, builds, or reading the reference assemblies; none of it shows in-game behaviour.

## 1. Audit of the first pass: faults found and fixed
| Fault | Fix | Commit |
|---|---|---|
| Unclaimed silver was adopted by the community on a single reading (a quest drop landing, a trade settling, a caravan returning could be mistaken for it) | `SilverReconciler`: persisted one-day grace, only the part that stayed is adopted; an existing colony's silver is recorded once, with a notice, and never distributed | e6a5d00 |
| Nothing recognised missing, stolen or destroyed silver; claims could exceed physical silver forever | A shortfall persisting 3 days is written off **only from the community account** (a visible `Correction` transaction and a message); personal and household claims are never erased; the remainder stays shown as a discrepancy | e6a5d00 |
| Physical silver counted only stored silver, so a caravan trip or silver in a pocket looked like theft and would have been written off | `PhysicalSilver` counts ground, storage, pockets, hands and player caravans | 9e7b515 |
| Estate settlement erased unpaid personal debts | Unpaid debts pass to the first heir (the community forgives its own; a private debt with no heir falls to the community) | e6a5d00 |
| Family Life drops away colonists from their household, so a caravan party's household account would have been swept to the community | Leaving a household takes an equal share of silver **and debt** to the new household or the leaver's account; the last leaver takes all; every step is keyed so repeated reconciliation cannot repeat it | e6a5d00 |
| The framework did not report Treasury state of an absent module | `AbsentModules` now scans shared payloads | e6a5d00 |
| Defaults let wages greatly exceed plausible income | Lowered (professional 4/unit, 4 units/day; 24/day per person); ordinary labour has its own 8/day cap and accrues only while the treasury can cover what is already owed | 0a70914 |

Checked and found correct (still only by reading code and offline tests): payroll cannot call `Receive`; a collection visit cannot create or destroy silver (source guards); `Trade.Settle`, `DistributeReward` and payroll are atomic and keyed (replay changes nothing); planning a trade writes nothing (a cancelled trade leaves every account as it was).

### Harmony binding
Checked by reading both reference assemblies (`Krafs.Rimworld.Ref` 1.6.4871 and 1.7.4990-unstable) with a metadata loader. **Identical on both**: `TradeDeal.TryExecute(out bool actuallyTraded)`, `QuestPart_DropPods.Notify_QuestSignalReceived(Signal signal)`, `Pawn_JobTracker.EndCurrentJob(JobCondition condition, bool startNewJob, bool canReturnToPool)` (its `pawn` field is protected, so the patch injects it as `___pawn`), `ResearchManager.ResearchPerformed(float amount, Pawn researcher)`. This shows the patches *can* bind; whether they attach and fire is runtime-only. Each patch runs in a try/catch and logs instead of throwing.

## 2. Implemented in this pass
* **Physical Treasury.** Treasurer office session (walk to the Treasury chair and stay about 8 s) for business that is waiting after a payday, a shared reward or an account-funded trade, once per item. Silver deposits: the Treasurer is sent, using the game's own `HaulToCell` job, to carry silver that is loose or in a lower-priority store into the treasury's own storage (at most one per 2500 ticks). Neither touches a balance (a source guard forbids ledger calls in that file). The inspector shows missing requirements, Treasurer, silver in storage, other valuables, free slots, silver waiting to be deposited, office business waiting, and claims in excess of silver. Valuables are a configurable registry (gold, jade, onyx by default; add any item from the Overview). Liquid silver and stored valuables are counted separately and valuables never become silver. Deleting or moving the zone only clears the zone reference.
* **Wages for ordinary labour.** A prefix on `EndCurrentJob` that only acts on `Succeeded`, plus `ResearchPerformed`. Categories: growing (including cutting plants), mining, construction, cooking, cleaning, hauling *into storage*, crafting (with long recipes counting for more, up to four units), animal handling, hunting, research (one unit per 400 points performed, remainder carried). One job id pays once; hauling pays once per thing per day; each category has a daily unit cap and ordinary labour has its own daily silver cap; modules' own jobs and medicine are excluded so a profession is never paid twice. Wages accrue to the 1st/8th payday, settle from available treasury funds, and unpaid wages stay owed to the worker.
* **Family Life integration.** Household joins/leaves/marriages/dissolution, death and inheritance are driven by Family Life's own events and kin order (spouses, children, parents, siblings, otherwise the community), through the existing ledger operations. Children's shares live in the household and travel with them. No competing estate or household history is kept; `EstateDriver` still owns items.
* **Tax collection.** Outcomes weigh the payer's opinion, the household's mean opinion of the collector, mood, Social, Melee, traits, affordability, earlier refusals and community trust. Hardship is not refusal. Intimidation helps compliance and costs trust. Mechanical misconduct and positive events: embezzlement, overcharging, bribes, favouritism (waived share), reducing the collector's own taxes, delays for relatives, hardship deferral, under-reporting/hiding funds (caught by the collector's Social), discovery (becomes a restitution, refund or back-tax obligation for exactly the amount), returned money. Notices are limited to one per two days.
* **Experience.** From real operations only (payday 8, reward distribution 6, account-funded trade 4, deposit 3, collection 6, plus a small colony-size bonus), each with a unique key; half when the business expired unattended; none for sitting. Modelled estimates (assumed operation counts): small colony about 8 months, ordinary about 5, large about 2.5, a colony with almost no trade about 15. Qualification (Social 7, Melee 6, 120 XP) is unchanged.
* **Community window.** Education and Community Care register their own pages (status badge, instructors or care workers, classes or capacity, and the same begin/postpone/skip actions the buildings and letters call). Disabling one removes only its page.

## 3. Verified only offline
Ledger conservation and idempotency; reward, trade and payroll arithmetic; payday calendar and rollover; payroll after a reload; shortfall and wage debt; household split and estate totals; reconciler decisions; labour classification and exploit limits; mixed professional and ordinary wages; XP estimates; tax gating, collection resolution, discovery obligations; page registry with modules disabled; persistence of every payload across an absent module (`SaveSim`); six-year balance and size. 1,331 tests; four assemblies build for 1.6 and 1.7.

## 4. Requires RimWorld runtime testing (unverified)
1. Both Harmony groups attach and fire exactly once (trade, quest drop, job success, research); vanilla trading, hauling and research are unchanged and unslowed.
2. Quest silver delivered other than by `QuestPart_DropPods` is **not hooked** (direct-to-pawn, caravan gifts, `QuestPart_GiveToCaravan`); such silver is adopted by the community after a day instead of being shared by policy. Which vanilla quest rewards use drop pods needs checking in play.
3. Trade price rounding against vanilla's net silver; caravan and settlement trades (`ColonySilverBefore` is the currency tradeable's colony-held count); silver in transport pods in flight shows as a temporary shortfall.
4. Zone recognition (room, table, chair, shelves/stockpiles accepting silver, modded storage), the architect entry, the inspector.
5. The office session (reservation of the chair, the wait, progress bar, whether the pawn visibly sits) and the deposit haul (destination cell choice, reservation, interruption, no tug-of-war with vanilla hauling).
6. Job-completion wage signals for each category (which vanilla jobs end `Succeeded` once per unit of work), research accrual, the solvency gate.
7. Collection visits (walking, talking, memories, no stuck jobs), event frequency and spam.
8. Family Life events (households joining/leaving/dissolving) produce the expected shares in real play, including marriage and moving as a group.
9. Existing-colony start: the one-time recording of existing silver; reload during a quest drop or a trade; module removed and re-added.

## 5. Remaining limitations
* Wages are claims, not physical handing over: the Treasurer does not carry wages to workers, and personal silver is never "in a pocket" in the game's sense. Only deposits are physical.
* Silver in pockets and caravans is counted as the colony's, but cannot be assigned to an account owner; pooled trades in a caravan use the claims against what the caravan holds.
* Trade purchases are charged treasury first and then proportionally to personal and household accounts without consent (the designed default); personal accounts are therefore exposed to community purchases.
* Item ownership is recognised only for things a pawn carries, wears or wields; sold goods otherwise count as communal (basis 0, so the whole price is profit shared by policy).
* No financial-hardship moods, loans, wedding or funeral costs, commissions or emergency aid (the debt kinds exist; nothing creates them).
* No Workshop/storefront, sales tax or business accounts in use (the contract exists).
* A Tax Collector can only visit a household whose members include an adult colonist on the map; visits are one at a time.
* One primary Treasury per map; accounts are community-wide.

## 6. Known economic balance risks
* Everything depends on how much external silver enters the colony. Wages are paid only from the treasury, which receives 25% of rewards, adopted unclaimed silver, and (once taxation exists) tax. If the community share is small, professionals' wages become debts that later income pays first; ordinary labour simply stops accruing when the treasury cannot cover what is owed. Whether this feels fair or punishing is a play-test question.
* Defaults (professional 4/unit, 4 units/day, 24/day; ordinary 1/unit, 3 units/category/day, 8/day) were chosen to keep payroll below typical income, not measured against it.
* Treasury XP estimates assume operation frequencies (5-12 rewards, 10-40 funded trades a year) that real colonies may not reach.
* Tax up to 40% of income with 7-day due dates may be punishing for small households.

## 7. Manual verification
See `docs/TREASURY.md` section 6 for the base run, then in addition:
1. **Existing colony:** load an old save with the Treasury added; expect one message that N silver was recorded as community funds and nothing given to anyone.
2. **Caravan:** send a caravan carrying silver away for more than three days; no loss message; trade with it and confirm the Transactions page.
3. **Loss:** destroy or drop-and-forbid-lose silver (dev mode) and wait three days; expect one loss message and a community-only write-off.
4. **Ordinary labour:** let colonists harvest, mine, build, cook, clean, craft, haul and research for a day; check Payroll/Overview "waiting" totals rise only for finished work, stop at the daily caps, and that repeatedly carrying one stack earns once.
5. **Treasurer:** assign a Treasurer, finish a payday; expect the Treasurer to visit the chair once and Treasury XP to rise; leave silver on the floor and expect a deposit haul (Treasury XP +3).
6. **Households:** move a colonist to another household and marry two colonists; check the account shares on the Accounts page add up to what was there before.
7. **Death:** kill a colonist with money and a debt; the heirs' accounts and the Debts page change once; reload and confirm nothing repeats.
8. **Tax:** as in the base run; watch for stuck visits and message volume over a quadrum.
9. **Pages:** disable Education, then Community Care, and confirm the window and navigation stay intact.

## 8. Financial correctness review (second pass)

Offline only: 1,348 tests pass; both 1.6 and 1.7 packages build. Nothing here was run in RimWorld.

### Fixed
* **Missing silver is an impairment, not a write-off.** The reconciler no longer deletes the community's share after a few days. It reports `Unbacked` (claims minus physical silver); `Ledger.SetPhysical` makes every account's spendable amount (`Available`) shrink pro rata, so ownership is untouched but nobody can spend silver that does not exist. Payroll, trade funding and debt payment use `Available`. `Ledger.WriteDown` is the explicit "accept the loss" operation (pro rata, conserved, keyed). There is **no UI button for it yet**; it is domain-only.
* **Surplus adoption** needs the surplus to stay for two days, ignores differences of 3 or less, and is held back while a reward or trade settled recently (`LastIncomeDay`), so a delivery still being recognised is not adopted as unrelated income. The first-run adoption is unchanged.
* **Wages accrue during insolvency.** The old `CanFundOrdinary` gate is gone (it silently discarded earned wages). Earned wages stay claims, pay only from backed silver, and the remainder becomes a wage debt (flagged professional/ordinary). When money is short, professionals are served first, but ordinary workers keep a floor of a quarter of what is available.
* **Arrears are capped** per worker (ordinary: 120 silver, professional: 240). Work beyond the cap is still done but earns no further claim until arrears are paid down. This was found by simulation: without it a 42-pawn colony with weak income owed ~84,000 silver after 180 days.
* **A closed account no longer blocks payroll.** Found by simulation: after a colonist died, wages to their closed account failed the whole payroll batch every payday, forever. Wages now go to the account that inherited it.
* **Estates:** creditors are paid from the estate first, public debts (tax, restitution, damage) before private ones, pro rata within a class. Any shortfall is recorded as written off with origin "(estate shortfall)" and stays visible; heirs inherit no liability unless `heirsLiable` is set (no gameplay setting enables it). Remaining silver is split among heirs. Idempotent per key.
* **Households (`HouseholdRules`):** being away moves nothing; moving out of a household that carries on takes nothing; divorce gives a per-person share; dissolution divides among those who joined other households (away members' shares stay for their return); a returning member brings the remaining old account along. All transfers are keyed and conserve value.
* **Tax:** the "conceal" diversion is removed (it opened a second debt for money still owed on the first). Favour, bribe and own-tax reductions are now `Waive` (distinct from paid). Discovery ignores closed accounts. Tax assessment uses the per-account income accumulator, not a 600-row history window. Routine "paid" collection messages are suppressed.
* **Labour signals:** a hunt pays only if the target is dead; a crafting or cooking job pays only with a real bill; resource delivery jobs are excluded; the same target pays once per day.
* **Treasurer office:** only payroll items cause a desk visit; rewards and trades expire at half value if no visit happens.

### Simulation results (deterministic, 180 days, real ledger/payroll code)
All three keep `Conserved` every day and reconcile to zero. Wages paid never exceed external income. No currency is added to balance anything.

| Colony | External in | Wages paid | Wage debt at end | Treasury at end |
|---|---|---|---|---|
| Tribal, 5 pawns, 1 pro | 3,630 | 1,964 | 826 | 0 |
| Ordinary, 16 pawns, 4 pros | 9,400 | 5,853 | 2,840 | 0 |
| Large, 42 pawns, 10 pros, 1 death | 6,600 | 2,181 | 7,492 | 0 |

These scenarios give every pawn the daily ordinary maximum and each professional a full day, which is deliberately pessimistic. They show the defaults can outrun income for any colony that does not receive roughly 1.5-3 silver per pawn per day from outside. The caps stop the debt growing without limit; they do not make wages affordable.

### Not done / unresolved
* No UI for `WriteDown`; the unbacked notice and the page line are informational.
* Tax Collector progression and tax collection volume are **not** in the simulations; tax outcomes are covered by unit tests only.
* `LabourReport.TargetDead`, the delivery-job exclusion and the office visit change live in game code that the offline tests cannot execute; they compile on both versions but are unverified in the game.
* Interrupted jobs are excluded by the existing `Succeeded` condition; partial construction and work whose effect happens before the job ends are only approximated by the per-target daily key.
* Wage defaults are not tuned. Treat wage inflation as the main runtime risk and watch the "waiting" and debt totals in a real colony.
