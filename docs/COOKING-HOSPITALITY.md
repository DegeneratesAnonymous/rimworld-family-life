# Wil's Cooking & Hospitality

Status: implementation candidate for controlled gameplay testing. **No RimWorld gameplay has been run in this environment.** This is a draft, not a Workshop release. Test on a copy of a save with matching Framework and Treasury builds.

## Package and architecture

`SpacemanWil.WilsHospitality` / `WilsHospitality.dll` / module `wils.hospitality`. Requires only Community Framework. Treasury supplies the optional commerce capability; neither module references the other's assembly. Hospitality and Gastronomy are optional. Without a running account provider, paid service and new paid shifts pause and records remain saved. Cooking experience can still be earned through ordinary cooking. Receipt/refund/budget-return features use an additive `ICommerceAdjustments` capability, so older providers can still load against the unchanged `ICommerceBank` contract; unsupported adjustments refuse safely instead of losing money. Use matching current packages to enable every feature.

Framework additions are generic commerce, production/stack, ingredient/food access, completed interaction normal visitor-departure and observed property-damage contracts. Framework owns Harmony hooks; modules register lifecycle-gated listeners. Existing Treasury wage listeners yield to registered private-work handlers, preventing ordinary/professional/business double pay.

Payloads: `wils.hospitality.establishments`, `.production`, `.bookings`, `.wallets`, `.stories`. They contain ids and serialized values, not game object references. Saved job drivers keep their order and pay clocks. Module removal stops jobs, settles delivered bills where possible, releases unfilled orders and booking reservations, and releases generated visiting groups. It refuses to report successful cleanup when a delivered bill cannot settle. Accounts, careers, business records and wage liabilities remain in the save.

## Careers

| Role | Requirements | Work |
|---|---|---|
| Cook | Entry role; vanilla work eligibility still applies | Ordinary cooking and establishment production |
| Chef | Cooking 8, 120 general experience | Kitchen service and practical mentorship |
| Artisan Chef | Chef, Cooking 10, Artistic 6, 250 general + 60 specialty experience | Creative preparations; presentation = Cooking + min(4, Artistic / 4) |
| Bartender | Chef, Cooking 8, Social 6, 250 general + 60 specialty experience | Drink service, attention, checkout |
| Innkeeper | Chef, Cooking 8, Social 8, 250 general + 60 specialty experience | Service, room preparation and hosting |

Practice can bank specialty experience before qualification. Qualifications never replace vanilla recipe, skill, food poisoning or surgery rules. Artistic affects the module's presentation/experience evaluation; it does not globally modify a pawn's Cooking skill. Ordinary meals and existing recipes remain the physical products. Unqualified service workers can practice, with the module's service evaluation capped at 65; relevant qualifications remove that cap.

## Establishments tab and setup

Community -> Establishments -> Create. Restaurants, taverns, inns and general shops share a record. Choose an individual, household or community owner; assign staff (no manager); designate service cells, sales cells/containers, private storage cells/containers, a counter, kitchen benches, and inn beds. Container references follow the selected object's id. Area and container coverage is deduplicated.

Sales and storage painting creates ordinary stockpiles on empty floor. **Use their native storage filters and priorities to control incoming goods.** Designation explicitly allocates the area's contents from communal supplies. This is the supported initial-stock grant; there is no automatic ingredient purchasing agent. Private kitchen ingredient selection is limited to that business's designated stock/storage, and communal bills reject business ingredients. Normal food selection rejects private stock and another customer's reserved food. This does not override explicit player orders or every third-party item-taking job.

Object designation rejects another establishment's selected object; stock/storage painting rejects another establishment's painted stock/storage. Do not overlap another business's selected container with a painted stock area; broader modded-container ownership needs live testing.

Set customer access, alcohol service, intoxication cutoff, quiet house rules, opening/closing hours, price multiplier, lodging price, maximum stay, household allowance, reserve and wages. Equal opening/closing times means closed; overnight hours work. The counter uses an existing eating surface or an optional Cash Register object. Individual bans and lineage exclusions are explicit player policies; a rejected pawn only gains a relationship memory after a real on-site conversation. Staff work during the intersection of opening hours and their personal Work timetable. Cooking/Sell priorities select duties; business cooking precedes communal cooking. Necessary needs and danger interrupt service. One available employee covers the counter; when nobody can cover, new orders pause.

A canonical existing work type named/labeled Sell is reused if found; our work giver moves into it, our own column becomes invisible and its cached work-giver list is cleared. The inspected Cash Register definitions provide Sell; Hospitality's Entertain and Gastronomy's Wait remain separate. Cached reflection adapters prevent native shopping against our private stock, competing billing for active customers, spending pledged visitor coins, and native register shifts overlapping our assigned shifts. Native Gastronomy diners and queued customers retain their existing jobs. An active native restaurant must be closed and its diners/queued orders finished before its register can be assigned here; one system owns each till. Adapter attachment/signature failures are visible on the Establishments page. Unknown third-party job systems still require dedicated integration.

## Orders and visitors

Customers compare needs, reputation, price, distance, recreation and the presence of communal food. They choose affordable stock. Silver is currency and is excluded from merchandise, preventing a sale from exporting backing while being recorded as new income. Diners wait in the service area, staff physically deliver the product, vanilla ingestion handles eating, and diners approach the counter in FIFO order for staff checkout. Shoppers make a short browsing walk and receive their purchase. Selected service cells prioritize seats, but precise chair/table use and physical queue layout need gameplay validation.

Resident money is reserved when ordering and transferred at checkout. Reservations live in Treasury and protect funds from ordinary spending even when backing is healthy. Delivered orders cannot be cancelled for free goods. Interrupted checkout can settle remotely; genuine emergency interruption does not itself imply bad service. Unfilled orders release funds. Household fallback uses a shared daily allowance; an order uses one account rather than combining personal and household funds.

Purposeful visitors arrive with finite physical silver, can patronize multiple businesses, and leave after their business/stay. Inn occupancy uses selected, unowned, non-prisoner beds. Actual sleep accrues before lodging is charged. Visitor spending preserves a journey reserve and expected lodging costs. Visitor payments physically transfer silver before Treasury records outside income; refunds physically return it before recording outside expenditure. Internal purchases never create outside income.

Stay decisions consider reputation, personal experience, trait-based atmosphere preferences, social connections, funds, food/journey reserves, injuries and maximum stay. Generated groups decide together. Normal vanilla visit/trade departure timers can be delayed only when every human member agrees, can afford its per-person share of the extension and a bed exists. Original lords are retained; travel/combat/emergency transitions are untouched. The optional Hospitality normal-departure trigger has the same consent gate; native sent-away/leaving states override extension. Existing native guest-bed assignments remain under Hospitality's lodging system, with no second room charge here. Shared wallets include same-faction group members and pack animals on the map, reserve specific contributors' coins per order, and replan if funds change. Coins pledged to another order cannot fund a second one. This does not rewrite native bed-rental policies or support arbitrary custom departure graphs.

## Pay and money

Base pay defaults to **0.25 silver per game hour**, plus a **20% collected-sales contribution pool**. Fractions carry and time clocks survive save/load. Production, service, checkout and support weights default to 4/3/2/1. The player can adjust base pay, pool share and each weight per establishment. A separate, explicit personality preset uses Greedy/Kind owner traits for future rates and prices; it never rewrites earned wages or secretly overrides player settings.

Production provenance follows stack splits and merges. The maker receives production credit per deposited product quantity. Saved fragment ids and credited flags prevent stacking, splitting, rehauling or a later sale from crediting the same units again. Recipe experience remains once per recipe activity. Hauling does not become production credit. Repeated social attention has daily keys and event cooldowns. Staff revenue-share fractions also carry across small sales.

Base wages and contribution shares use the Treasury 1st/8th payday calendar. Business accounts owe business wages; public or household money is not automatically seized. Payments share a shortfall proportionally; unpaid amounts remain wage debts without caps or forgiveness. Earned/paid/owed accounting and a 180-day simulation are tested. Owner withdrawals protect reserve, wage debt, pending base pay and the contribution pool. The Treasury's manual account controls remain administrative tools, so deliberate manual transfers can still underfund a business.

Bookings reserve a sponsor's real budget, subdivide it into order reservations, pay only completed orders and release the unused reservation at expiry. Current actions: household celebration meal, community meal, emergency meals/shelter. Visitors/residents retain voluntary attendance; avatars are not commandeered. Cancellation returns an unfilled order's reservation and allocation to its still-open booking. If the booking already closed, cancellation releases the child hold without reopening it. Provider absence preserves the pending obligation until it can be safely released. Household allowance usage is restored on cancellation/refund. Ceremony scheduling does not automatically buy catering; these bookings are explicit player actions.

Full refunds are available for recent completed bills, preserve earned staff pay, and slightly improve service/reliability. They cannot erase all reputation damage. Saved receipts bind a refund to the original business, payer and amount; replay cannot pay twice. Refunds reverse business taxable receipts and paid business wages are deductible. If income was already assessed, the negative deduction survives empty periods and offsets future income; existing tax debts are not retroactively cancelled and no tax-refund currency is created. This is cash-basis sales/payroll accounting, not a complete ingredient-cost or inventory-valuation system. Voluntary small tips require actual personal/visitor funds and accrue to the serving employee on payday; they never draw the sponsor's meal budget, become owner profit, or vanish with a later meal refund.

## Family/community and reputation

Ownership follows Treasury's recorded estate/household successor. Staff wages remain personal claims. Household meal attendance, visiting kin, shared work, actual celebrations at the premises, booked meals, emergency shelter, public contracts, successful services, mentorship, real insults and witnessed fights produce appropriate facts/memories. Existing nonpayment thoughts are not duplicated with a second wage penalty.

Reputation records reliability, food, service, comfort, value, safety and rowdiness. Fights hurt safety and increase rowdiness regardless of xenotype. Successful peaceful service gradually restores safety; quiet rules, alcohol restrictions, coverage, stock, value and refunds give the player concrete interventions. Traits influence attraction and extensions. Public history explains witnessed trouble and financial/service actions. There is no automatic faction-goodwill conversion, hired-bouncer AI or global rumor broadcast.

### Event catalog and activation boundary

`HospitalityEvents.cs` and `HospitalityThoughts.xml` define **five positive mood events, five negative mood events and five relationship events per role**, plus family/community/customer events. All require explicit facts; social memories require another pawn. Runtime selection allows at most one mood and one relationship thought per event and has persisted per-pawn/other cooldowns.

Every catalog fact now has a runtime source. Uncommon negative stories require saved evidence and completed local conversations; they are not random remote accusations. Conversations queue as interruptible Sell jobs after urgent deliveries/checkout and respect needs, Work hours, danger and avatars. Pending tasks, confidences, visit history and handled commitments survive reload. Runtime frequency and positioning remain gameplay-test items.

| Story group | Actual source / safeguards |
|---|---|
| Rush and team service | Four completed recent orders; contributor records identify the team. Coverage complaints require a cancelled accepted service assignment, an unexcused worker and a present working colleague. |
| Mentorship | A nearby, better-skilled colleague completes a conversation; accepted advice grants practice, rejection depends on actual traits/opinion. |
| Credit theft / false blame | A completed order identifies its real maker and a present witness. False blame additionally needs recorded destruction of an assigned order by the accuser and an uninvolved target. Neither can move production credit or wages. |
| Creative rivalry / taste | Both rivals have real completed preparations. Relationship opinion determines respect or friction. A refined customer discusses an artisan's served dish. |
| Experiments / ruined dishes | A saved preparation outcome lowers only custom presentation; the outcome follows stock fragments. Ruin requires observed real rot or destruction, not a missing stack after ingestion/merging. |
| Tips / bills / complaints | A completed sale, affordable physical/account payment, an actual cashier conversation or an owner-authorized paid refund. |
| Listening / introductions / confidence | Real conversations record a distressed patron confiding in a host. Disclosure requires that keeper's saved knowledge, a new listener and the confider within hearing/line of sight; it can occur once. No invented private crimes or medical secrets. |
| Drunks / fights / damage | Real intoxication and active cutoff policy; observed insults/fighters; property hit-point loss caused by an actual guest. House-rule conversations follow recorded damage. De-escalation memories require a later conversation with the previously seen fighters, not an invented instant end to combat. |
| Check-in / returning guests | A saved stay and completed welcome; prior completed visit history. A destroyed reserved bed before the first sleep can cause a chaotic-arrival memory. Native beds are not billed twice. |
| Family / workplace hardship | Existing broken family commitments plus recorded business work near their due time; emergencies excuse the extra work/family memory. Long-outstanding wages, actual witnessed owner favoritism, and explicit family exclusion policies provide the other triggers. |
| Succession | Treasury's recorded successor remains authoritative. A recorded spouse/child may request an on-site ownership conversation; a dispute never silently reallocates the asset. |
| Supplies / public work | Removing designated food stock while accepted orders remain, witnessed by staff; completed sponsored emergency meals, public bookings and real attendance provide public-service memories. |


## Validation and live acceptance

Offline validation covers domain conservation/idempotency, holds and subdivision, order state, wage debts/proportional settlement, payday calendar, contribution division, fractional pay, persistence/absent modules, provenance, visitor affordability/group rules, role/event coverage and XML. Both version builds and hook metadata checks are required. `python3 scripts/package.py` checks five installable package layouts and creates `dist/WilsCookingHospitality-test.zip`. CI uploads it as `cooking-hospitality-gameplay-test`.

Before merge/release, run on **both** game versions:

1. New/existing colony, each module combination; confirm startup hooks attach without skipped-hook warnings.
2. Establishments tab at desktop/small resolutions; every designation tool, container movement/destruction, removed maps and stockpile filters.
3. Cooking above/below Sell, multiple staff, unavailable ingredients, business-to-communal fallback, breaks, drafting and danger.
4. Physical delivery, correct vanilla ingestion, seat/table behavior, shop browsing and multi-customer checkout; interruption after pickup/delivery/ingestion; save/reload at each stage.
5. Resident/household purchases, outside silver, refund, insolvency, sponsored bookings and three successive paydays; no duplicate pay/income.
6. Inn sleeping, completed-night charges, affordability, group extensions, trade-caravan departure and emergency exits; custom Hospitality/Gastronomy installed separately and together.
7. Family meals, emergency shelter, succession, social fights, recovery, mood/opinion cooldowns and 3x TPS on a large colony.
8. Prepare/remove/re-add each module; a provider unavailable during a delivered bill must preserve the bill and never invent payment or report successful removal.

No in-game behavior, measured TPS, balance tuning or UI rendering is claimed by an offline test pass.

## Establishment personality expansion

Establishments now develop a persistent character across five axes: warmth, refinement, frugality, discipline and revelry. Trait combinations contribute together, including Kind, Greedy, Abrasive, Psychopath, Ascetic, Gourmand and Brawler, plus the degrees of vanilla Industriousness and DrugDesire. Modded trait names are optional; missing traits are harmless. No character is inferred from xenotype.

An individual owner contributes weight 5, the collective staff weight 3, and actual recent customers weight 2, normalized over sources that exist. Household owners contribute the mean of their adult members, without appointing a manager. Community-owned venues develop from staff and customers. Culture changes at most three points per axis per economic day; loading after a long absence cannot fast-forward it, and succession does not instantly replace its identity. Quiet house rules guide the revelry target. Descriptive tags emerge from the resulting values rather than being randomly assigned.

Customers compare their own temperament with the venue's character when choosing to visit and considering an extension. Existing affordability, travel obligations, safety, group consent and maximum-stay rules remain in force. Matching character is a modest influence, not a guarantee of business.

Owner mood is temporary and separate from stable culture. On-site owners can have at most one completed discretionary staff conversation per establishment per economic day. They must be physically close, able to interact, and not a Perspective Shift avatar. Conversations use the real social interaction path and do not forcibly interrupt jobs or pull an absent owner across the map. Household members can represent their own business; community venues have no invented personal proprietor.

The response weighs the owner's temperament, current mood, opinion of the worker, the worker's distress, recorded contributions, actual backlog, recent sales, available business funds and existing family membership:

- Praise recognizes recorded work.
- Support responds to a struggling worker.
- Criticism requires an actual service backlog; low mood and stress sensitivity increase its relative likelihood.
- Profit discussion responds to recent sales or financial obligations; greedy owners favor it.
- A discretionary one-silver bonus can be awarded for recorded work when free business funds cover it. It becomes an earned payday obligation immediately; reserve, existing wages and contribution shares are protected.
- Family favoritism singles out a productive relative for special praise in front of unrelated coworkers. Only nearby witnesses who can see the owner receive the resentment memory. It does not steal anyone's credit or accrued pay.
- Celebration acknowledges recorded contributions and recent sales.

Eight new memories have both mood and opinion effects. Staff climate records the consequences of these exchanges, tends gradually toward neutral, and makes a bounded adjustment (at most five points) to service evaluation. It does not change vanilla skill levels, actual product quality or job speed. Staff distress can invite later support, so owners can repair relationships through real interactions.

The Establishments page shows character, each axis, staff climate, owner mood and the reason for the latest owner decision. Players can disable owner conversations, separately prohibit discretionary bonuses, or require equal treatment to prevent family favoritism. Owner behavior never changes player-set prices, opening hours, base pay, contribution weights or staff share; earned wages cannot be reduced. The old explicit financial-policy suggestion button remains available.

Owner exchanges now feed the corresponding generic workplace/favoritism stories as well. Runtime behavior, social positioning and balance still need RimWorld testing.

## Optional-mod compatibility evidence

Adapters were checked against source and tracked assembly metadata from these pinned upstream checkouts:

| Repository | Commit | Assemblies inspected |
|---|---|---|
| OrionFive/Hospitality | `6b6769769e8ed556d18f4a67606e1a1eb5d4fb30` | 1.6 |
| OrionFive/CashRegister | `e1155b8d0ca25b9f2da55dbe885cac472c3d0ee2` | 1.6 |
| OrionFive/Gastronomy | `cf34a227b6161475c8560d1364dc134cc248f67d` | 1.5 (latest tracked assembly in that checkout) |

`dotnet run --project tools/CommerceCompatCheck -- <game-ref-folder> <Hospitality-assemblies> <CashRegister-assemblies> <Gastronomy-assemblies>` checks 18 method/member contracts without executing those mods. No optional assembly is a compile-time dependency or included in our packages. This proves the inspected signatures, not live patch composition or compatibility of every published version. Both our 1.6 and 1.7 packages build; native-mod gameplay, especially available 1.7 combinations, remains a live test.

The financial/story completion pass adds regression coverage for recycled booking budgets, absent-provider cancellation, payer-bound refunds, tax deduction carryforward, business payroll expense, shared wallet pledges/replanning, truthful story evidence/knowledge, broken commitments, paid tips, and saved story/provenance/admission state.

Completion validation: **1,455 tests pass** (34 added in this pass); all five modules build for both versions; all **40 framework Harmony targets** match both reference versions; **18 optional-mod metadata contracts** pass for the inspected assemblies; four calibration snapshots are unchanged; the five-package archive and XML/layout checks pass. No live RimWorld run or measured TPS validation was performed.
