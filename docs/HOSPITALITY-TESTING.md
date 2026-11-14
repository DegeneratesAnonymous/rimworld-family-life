# Cooking & Hospitality gameplay test

This bundle contains matching builds of all five packages for RimWorld 1.6 and 1.7. RimWorld chooses its version's assembly automatically. This is a gameplay-test candidate, not a Workshop release. No in-game save or live gameplay result is included.

## Install

1. Close RimWorld and copy the save you intend to test. Start with a new disposable colony if possible.
2. Extract the zip to a temporary folder. Copy the five `Wils...` folders directly into your RimWorld `Mods` directory: each package should have its own `About/About.xml`, `1.6/Assemblies` and `1.7/Assemblies`. Do not copy the containing extraction folder as a single mod.
3. Back up and replace existing local copies of these packages as whole folders; avoid leaving old DLLs behind. Disable any duplicate Workshop/local copy with the same package ID.
4. For the first pass enable Harmony, the usual Core/DLC entries, **Wil's Community Framework**, **Wil's Treasury**, then **Wil's Cooking & Hospitality**. Education and Community Care are included but optional for this pass. Use RimWorld's sort order and resolve any dependency warning before loading.
5. First test without third-party hospitality mods. Add Hospitality, Cash Register and Gastronomy in separate subsequent passes, then together if your game version has compatible builds. They are not bundled or required. Native adapters must report attached rather than an unsupported signature.
6. Keep `BUILD-INFO.json` with the extracted bundle. It records the source commit, source-tree cleanliness and SHA-256 of every other bundled file. Supply the commit when reporting a problem. `IMPLEMENTATION.md` contains the detailed event-source matrix and limitations.

If building on Windows instead of using this zip, run `scripts/Stage-ToRimWorld.ps1 -GameDir "<your RimWorld installation>"` from the repository with RimWorld closed. It builds/stages all five packages; it does not enable them in the game's mod list. This downloaded zip already contains the compiled packages and needs no SDK.

## First restaurant: smoke test

1. Use several adult colonists, available meals/raw ingredients, physical silver and a quiet map. Enable developer mode for logs. Let Treasury reconcile the physical silver and inspect its balances. Silver introduced after its initial reconciliation can take a day to be adopted; do not assume spawning coins instantly funds accounts.
2. Open **Community → Establishments**, create a restaurant and choose **Community** as owner for this first test. Assign two staff. Choose opening hours covering the current hour; equal opening/closing hours means closed. Enable **Open for business** and resident customers.
3. Designate a service area with a table/chairs, a counter (an existing eating surface is sufficient), a sales-stock area with meals, a private storage area with raw ingredients, and a cooking bench. Set native stockpile filters/priorities to bring the intended supplies there. Add a cooking bill at the designated bench. Ingredients for that kitchen must be in the business's designated stock/storage.
4. Give both staff personal **Work** hours during opening hours and enable **Cooking** and **Sell**. Set one to prefer Cooking and the other to prefer Sell. Lower priority numbers mean higher priority. Specialists are not required for basic service; qualifications improve evaluations.
5. Use the establishment's **Fund business: 25 silver from owner** action to fund it from community money. In **Treasury → Accounts**, transfer some existing community money to a customer's personal account. Confirm both the business and customer have spendable funds.
6. Leave customers off Work, awake, hungry or seeking recreation, and free of forced jobs. Let them choose a visit; attendance is voluntary, so the presence of communal meals does not guarantee an immediate restaurant order.
7. Watch for an order, physical delivery, vanilla eating, a counter queue and checkout. Confirm the customer's money decreases once, the business collects once and staff contributions/time pay accrue. Silver itself must never appear as shop merchandise.
8. Save during a pending order and reload. Repeat after delivery and at checkout. Check that no meal, bill or contribution is duplicated. Necessary needs, drafting and danger must interrupt work safely; service pauses if nobody can cover.

## Focused acceptance passes

Record pass/fail and the first log error for each row. Offline tests are already automated; these checks require RimWorld.

| Pass | Action | Expected result |
|---|---|---|
| Wages | Run through three consecutive 1st/8th paydays; also underfund the business | Actual work and contributions determine pay; shortfalls remain visible debts, with no currency creation or seized household funds |
| Cooking priorities | Reverse Cooking/Sell priorities, exhaust private bills, then restock | Duties follow priority; establishment cooking precedes communal cooking; needs/danger remain interruptible |
| Bookings | Reserve a family/community meal, cancel an unfilled order, then let the booking expire | Cancellation restores the open booking budget; expiry returns unused funds; no second payment |
| Refunds | Refund a recent settled order, retry and reload | Original payer or recorded successor gets one refund; earned staff pay remains protected; deductions carry forward if tax was already assessed |
| Visitors | Let a purposeful group or caravan shop; test shared and pack-animal silver | Coin pledges prevent double spending; journey/lodging reserves remain; no second billing by a native mod |
| Inn | Designate unowned guest beds, allow sleep and a stay decision, then interrupt with danger | Lodging requires real sleep; extensions need group consent, affordability and time limits; emergency departure still works; native beds are not charged twice |
| Owner personality | Compare Kind/Greedy/Abrasive owners and different moods; toggle equal treatment/bonuses | On-site conversations and climate differ; player policies and already-earned wages do not change |
| Stories | Observe mentorship, repeated service, tips, insults, real guest damage and returning guests | Appropriate mood/opinion effects and history; cooldowns persist; rare accusations/confidence stories require their recorded evidence and local conversation |
| Family/community | Book a meal, visit with relatives, test family exclusions and existing commitments | Effects correspond to actual attendance, witnessed treatment and recorded broken commitments; no invented off-map confrontation |
| Optional mods | Test Hospitality/Cash Register/Gastronomy separately and together | One Sell column; one system per till/order; existing native diners/queues retain their jobs; close a native restaurant and finish its diners before handing its register over |
| UI/performance | Small/desktop resolutions and a larger colony at 3x | Controls remain reachable; queues/pathing remain usable; record sustained TPS and repeated errors |
| Module lifecycle | Use the module's prepare-for-removal workflow on a disposable save | Pending money/records are preserved or settled; cleanup must refuse success when an obligation cannot be resolved |

For absence/removal tests, use a copy of the colony and the documented module lifecycle actions. Do not simply delete Treasury halfway through a bill in a save you intend to keep.

## Report a failure

Include the source commit from `BUILD-INFO.json`, game version, enabled mod list/load order, the exact action and expected/observed result, and `Player.log` (especially the first exception). A screenshot helps for UI/queue problems. Include a copied save if the failure only happens after reload. Do not repeatedly resume past a reproducible exception before collecting its first stack trace.
