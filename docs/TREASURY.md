# Community Management Hub and Wil's Role Modules : Treasury
> **Superseded in part by `docs/TREASURY_STATUS.md`** (the completeness pass: audit fixes, physical treasury, ordinary-labour wages, Family Life integration, module pages). Read that first for what is real, what is only offline-tested and what needs RimWorld.

Branch `feature/community-treasury` (cut from `feature/community-careers`; neither the careers branch, PR #7, nor any staged package was touched).
Everything below is **offline evidence only**. Nothing has been run in RimWorld. Section 6 lists what is unverified and how to verify it.

## 1. Architecture assessment (what was found, what was reused)

| Need | Existing piece reused | New |
|---|---|---|
| Module registration, enable/disable, absent modules | `FamilyFramework.Install`, `ModuleCatalog`, `ModuleHost` (frozen after start-up) | `wils.treasury` module (`TreasuryModule`) |
| Module state in saves | `SharedState` payloads (opaque, versioned, kept untouched when a module is absent) | payloads `wils.treasury.ledger / .payroll / .tax / .state` |
| Professions and experience | `CareerService`, `CareerLedger`, `AssignmentBook`, `ProfessionRole` | career `wils.treasury`: Treasurer (L1), Tax Collector (L2) |
| Meetings, walking, interruption | `MeetingCatalog` + `JobDriver_StoryTalk` via `FrameworkServices.StartMeeting` | job `WFL_TaxVisit`, meeting `wils.treasury.collection` |
| Scheduled work | `WorkRoster` (staggered, budgeted, skips disabled modules) | work items `wils.treasury.periodic` (2500 ticks) and `.tax` (1500) |
| Vanilla hooks | The framework already owns Harmony patches; an architecture test forbids a module defining its own | `Game/EconomyHooks.cs` (framework): `TradeDeal.TryExecute`, `QuestPart_DropPods.Notify_QuestSignalReceived`, exposed as `TradeCompleted` / `SilverReceived` contribution lists; `WorkReported` listener list on `CareerService.Report` |
| Zones | `Zone_Family` + `Designator_ZoneAdd_Family` + `Patches/FamilyZone.xml` | `Zone_Treasury`, `Designator_ZoneAdd_Treasury`, `Patches/TreasuryZone.xml` (in the Treasury package) |
| UI | `Window_Families`, `CareerPanel` | `MainTabWindow_Community` + generic `CommunityPageCatalog` |
| Estates | `EstateDriver` exists | **not integrated** (see section 7); the ledger has its own conservative estate settlement |

Compatibility concerns recorded:
* `Window_Families.PreOpen` was split so the Community window can host its content (`PrepareHosted`); behaviour of the standalone window is unchanged.
* The framework must not name optional modules. `CommunityPageTests` asserts the page code never contains `wils.care`, `wils.education` or `wils.treasury`.
* Quest silver is read from `QuestPart_DropPods` things at the moment the signal fires. Other delivery routes (direct-to-pawn, caravan gifts, `QuestPart_GiveToCaravan`) are not hooked.
* Vanilla ownership of trade goods does not exist. A sold item is attributed to a person only when it is in their inventory, worn, or wielded; everything else is communal.

## 2. Phase A - Community window
* `CommunityPageCatalog<TRender>` (framework, pure, tested): stable id, module ownership, label key, icon, order, availability, status badge, `OwnsScrolling`, render. A page of a disabled/absent/broken module is never listed; a stale selection falls back to the first listed page; a page whose availability or status check throws is dropped, not propagated. A page whose render throws is switched off for the session with one log entry.
* `MainTabWindow_Community` (main button `WFL_Community`): left navigation, selected page, per-page scrolling. Pages: **Overview**, **Families & Households** (hosts the existing Families window content; the window still opens from the Family button), **Professions** (assign/unassign any registered role), **Community Assembly**, **Settings**; plus **Treasury** from its module. Education and Community Care pages and a Workshop page are *not* added (no content was requested for them yet); the registry is ready for them.

## 3. Phase B - ledger (`Source/Treasury/Domain/Ledger.cs`)
* Four account kinds (community, person, household, business). Accounts are claims, never currency.
* Every change is a batch of legs checked first and applied whole or not at all, with an idempotency key (replay = no change). Value is conserved: `sum(balances) == silver in - silver out` is a tested invariant (randomised 2000-operation test). No operation can make a balance negative.
* Liabilities (`Debt`) are separate from balances; a poor household does not acquire debt by being poor. Debts record debtor, creditor, origin, original/outstanding, due day, instalment plan, payment history, status (open/paid/forgiven/disputed/written off) and a family reference.
* `Split` (household split: silver and chosen debts move once, never copied) and `SettleEstate` (debts first, remainder to heirs, odd silver to the first; no heir = community; account closed with a successor).
* Persistence: compact text payload, round-trip tested including idempotency keys; keys older than 200 days are pruned; history is bounded to 600 rows.

## 4. Phases C-G - what exists
* **Treasury Zone.** Pure rules (`TreasuryRoom`): enclosed room, table, seat, silver-accepting storage; zone validity is recomputed (cached 600 ticks, never saved), so resizing/removing/destroying the room cannot lose accounts. One primary zone per map (the lowest id). Inspector shows status, missing requirements, Treasurer, silver in storage, estimated value of other recognised valuables and free slots; a button opens the Treasury page. Valuables are a configurable registry (silver liquid; gold, jade, onyx by default; artwork always a valuable).
* **Income.** Quest silver: community keeps `CommunityPercent` (default 25) plus any odd silver; the rest is equal per colonist, credited to the household if there is one, with per-person attribution on each transaction (children included). Applied once per quest part (idempotency key). Sale proceeds: owned items to the owner (optional fee), communal items return their cost basis first and only the profit is shared (gross is not profit). Internal transfers are never income. Unclaimed silver in storage is adopted by the community (setting `AdoptSurplus`); a claims-above-storage difference is only reported.
* **Pooled trades.** Treasury first, then personal/household accounts proportionally (largest-remainder rounding, never more than an account holds). Planning writes nothing, so a cancelled trade changes nothing; a completed trade settles once under its own id as one atomic batch of purchases and sales. A purchase above the silver physically present at the trade is refused, so account balances never create silver in a distant caravan. Cost beyond every claim is "unattributed": physical silver that no account claims, paid without moving any account.
* **Payroll.** Paydays are day 1 and day 8 of every quadrum (8 a year); work on a day is paid on the first payday after it (so day 8 pays 1-7 and day 1 pays 8-15 of the previous quadrum, across year boundaries). Rates are per role id from the career registry (nothing hard-coded); a role's units per day and one pawn's total per day are capped; one finished-activity key pays once. Settlement is per payday, at most once (persisted `LastSettledPayday` plus the ledger key), catches up missed paydays in order, pays only from the treasury balance and from no more than the physical silver reported, pays proportionally when short and keeps the rest as a wage debt that is paid first on later paydays. Payroll never calls `Receive`; a source test enforces that.
* **Treasurer / Tax Collector.** Treasurer: level 1, no gate. Tax Collector: level 2, Social 7, Melee 6, 120 Treasury XP (three constants in `TreasuryRoles`), earned automatically, assigned by the player. Treasury XP comes from payday settlement (Treasurer) and completed collection visits.
* **Taxation.** `TaxAuthority.Unlocked` needs at least one assigned qualified collector. Locked: assessment, collection and rate controls are unavailable. Losing the last collector keeps policy and debts and suspends new assessment/collection. Assessment is on income received since the last assessment (rewards, wages, trade proceeds; not transfers) and becomes a tax debt to the community. A visit (`Collection.Resolve`) weighs affordability first (nothing to pay with = hardship, not refusal), then relationship, Social, mood, community trust, prior refusals, traits and approach; intimidation uses Melee, helps compliance and costs trust and goodwill; nothing is violent. Outcomes: paid, partial, repayment plan, refused, hardship. Only money actually paid moves.
* **Misconduct and events.** 24 events in four categories with grounding conditions, per-event cooldown, probability capped at 25%, traits that raise (never force) likelihood. **Mechanical** today: embezzlement (70 reaches the treasury and 30 the collector from a 100 collection, with the reported and actual amounts kept on the transaction), demanding more than authorised, accepting a bribe (waives debt, treasury loses the claim), discovery of a diversion (opens a restitution debt for exactly the amount), returning money. The remaining events change community trust, goodwill memories and notifications only.
* **Debts page / Transactions page / Accounts page** including a manual transfer, forgive and dispute.

## 5. Phase H - verification performed (offline)
* 1,294 automated tests pass (1,233 existing + 61 new across Phase A and Treasury; none weakened).
* All four assemblies build with 0 errors for RimWorld 1.6 (net472, Harmony 2.4.3) and 1.7 (net10.0, Harmony 3.0.0-rc.3); `scripts/Build-Packages.ps1` assembles and checks the four packages.
* Reference-assembly check (1.7.4990-unstable): `TradeDeal.TryExecute(out bool actuallyTraded)`, `QuestPart_DropPods.Notify_QuestSignalReceived(Signal signal)`, `Tradeable.GetPriceFor/CountHeldBy/ActionToDo`, `TradeAction`, `Transactor` exist with the parameter names the patches use. The 1.6 build compiles against the same members; parameter names for Harmony binding on 1.6 could not be listed by the same tool and are unverified.

Test map to the 22 acceptance items: 1 conservation (randomised), 2 reward once, 3 payroll save/load, 4-5 calendar and rollover, 6 shortfall, 7 later payment of wage debt, 8 funding order, 9 cancellation, 10 mixed trade, 11 caravan limit, 12-13 zone validation/resizing/loss of zone, 14 not applicable yet (no physical hauling exists to double-credit; see section 7), 15 split and estate, 16-17 tax gating and qualification constants, 18 partial/plan/refusal, 19 corruption conserves money, 20 disabled/absent modules (page tests, absent-save test), 21 builds, 22 six-year run (balanced, bounded, under 5 s).

## 6. Unverified in a running game (needs RimWorld)
1. The Community button appears and the window opens; each page draws; the hosted Families page behaves like the window; no log errors.
2. The two Harmony patches attach on 1.6 and 1.7 (`TradeDeal.TryExecute`, `QuestPart_DropPods.Notify_QuestSignalReceived`) and fire once per trade and once per silver drop; vanilla trading is unchanged; quest silver is shared exactly once.
3. Zone room detection (`Room.ProperRoom`, `TouchesMapEdge`), table (`SurfaceType.Eat`), seat (`isSittable`) and storage (`IStoreSettingsParent` / stockpile slot groups) recognition, including modded shelves; the painted zone over furniture; the architect entry and its placement.
4. `GenDate`-based pay calendar matches the in-game date shown (payday really falls on the 1st and 8th).
5. Payroll against `resourceCounter` silver, the reconciliation adopting unclaimed silver, and that adoption does not distort the first days of an existing colony.
6. Wage accrual: assigned professionals earn through `CareerService.Report`; the rates and caps feel right.
7. Tax visits: the collector is sent, walks, talks, outcome and memories apply, no stuck jobs; `WFL_TaxVisit` uses the shared talk job.
8. Trade price rounding versus vanilla's net silver (a few silver of drift is expected and shows as surplus or a discrepancy line).
9. The save size and load time with a long history (the ledger caps history at 600 rows).
10. 1.6 and 1.7 behaviour of `Thought_MemorySocial` defs and `baseOpinionOffset`.

### Exact verification steps
1. Enable Framework, Treasury (and Education/Care for wages) on 1.7; start a new tribal colony; confirm log line `Modules:` lists `wils.treasury` and `careers: wils.treasury.* registered: 1 career(s), 2 role(s)`; open the Community button.
2. Architect > Zone > Treasury Zone; paint a closed room with a table, a chair and a shelf set to accept silver; select the zone: status Treasury open; remove the chair: missing requirement listed; add it back.
3. Professions page: assign a Treasurer. Dev Mode > give a silver quest (or complete a quest with a silver reward): confirm the message "A reward ... was shared" once, and the Accounts page matches (community 25%, equal shares). Save and reload during the drop and confirm nothing is shared twice.
4. Assign a Doctor/Teacher and let them work; Payroll page shows rates; advance to day 8 of the quadrum; confirm one payday message and balances; reload on payday and confirm no second payment.
5. Make the treasury poor (transfer to a household) and advance to payday: partial payment, wage debt on the Debts page, paid on the next payday when funds exist.
6. Trade with an orbital trader buying more than the treasury holds: treasury empties first, then households proportionally; cancel a trade: balances unchanged.
7. Reach Tax Collector (Social 7, Melee 6, 120 Treasury XP) or assign through dev mode; Taxation page unlocks; set a rate; wait for payday; watch a visit; confirm outcomes, memories and the Transactions page.
8. Remove the Treasury mod, load (accounts kept, one notice); re-add it; accounts back.

## 7. Planned, not implemented
* Treasurer office use (sitting at the zone's desk for payday/household meetings), physical hauling of wages, tax silver and deposits (everything is ledger-only; payroll limits itself to physical silver in storage but does not move it).
* Wages for ordinary vanilla labour: the rate fields exist (default 0) but no work-completion signal is wired for vanilla job types; only registered professions accrue.
* Integration with `EstateDriver`/the inheritance system, and a hook for household splits (the ledger operations exist and are tested).
* Mood and relationship effects of financial hardship; emergency assistance, loans, wedding/funeral costs, commissions (debt kinds exist, no source creates them).
* Deposits of valuables in custody, heirloom custody, "outstanding deposits" on the zone inspector.
* Storefront/Workshop module, sales tax, business accounts in use (the `Business` account kind and debt kinds are the contract), Craftsman profession.
* Community and Education/Care Community-window pages; per-module status badges beyond Treasury.
* Several second-tier events are mood/trust only (see section 4).
* A relocation helper for the zone and multiple settlements/treasuries (one primary per map today; accounts are community-wide).

## 8. Balance questions
* Default rate 6 silver per finished task, 6 tasks per role per day, 60 per person per day (a doctor doing all four daily visits earns 24): high or low against a 25% reward share?
* Tax Collector requirements (Social 7, Melee 6, 120 XP) and the 6 XP per payday/visit: a Treasurer earns 6 XP per payday, 48 a year, so about 2.5 game years to reach 120 from paydays alone (visits add more once a collector exists, but a collector needs the qualification first). Probably slow; tune `TreasuryRoles.DutyPoints`.
* Tax on income at up to 40% with 7-day due dates.
