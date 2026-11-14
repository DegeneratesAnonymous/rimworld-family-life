# Crafting and Workshops — first draft

This module adds a crafting career tree and lets the existing establishment system operate player-designated commercial workshops. It is optional and references only Wil's Community Framework. Wil's Cooking & Hospitality supplies establishments; Wil's Treasury supplies real accounts, sales, wages, reserves, and payday settlement.

## Career tree

- Entry roles: **Woodworker** and **Stoneworker**. Either may be assigned immediately.
- Skilled branches: **Smith** and **Tailor**. These are parallel qualifications earned with Crafting 8 and career/branch experience.
- Smith specialties: **Weaponsmith** and **Mechanist**.
- Tailor specialties: **Couturier** and **Outfitter**.
- Shared specialty: **Armorsmith**, available after either Smith or Tailor.

Advanced specialties require Crafting 10. Couturier also requires Artistic 6. Qualifications are permanent; assignments remain a separate player choice.

## Workshop and shop loop

1. Create a **Shop** on the Establishments page.
2. Set its owner, staff, hours, service area, stock/storage areas or containers, counter, and crafting benches.
3. During overlapping personal Work hours and shop hours, assigned staff use the shop's designated crafting benches when their Craft priority wins. Sell work still handles customers and checkout.
4. Crafted output is tagged with its maker, business, product track, and career route. That provenance survives stack splits and merges.
5. Depositing output into the shop's stock or storage area credits production contribution and career experience exactly once. Sales and paydays remain handled by the establishment/Treasury systems.

Shops may now identify themselves as a **General shop, Smithy, Tailor, Armory, Mechanist workshop, or Boutique**. A focused workshop offers only matching goods with recorded workshop provenance; General retains the original sell-any-stock behavior.

## Assigned specialty bonuses

- Weaponsmith: weapons gain one quality band up to Excellent and a 15% shop premium.
- Armorsmith: armor gains one quality band up to Excellent and a 15% shop premium.
- Mechanist: mechanical goods gain 20% presentation and shop value.
- Couturier: prestige apparel gains one quality band up to Excellent and a 25% shop premium.
- Outfitter: general apparel gains one quality band up to Excellent and a 15% shop premium.

Bonuses require both qualification and assignment to the exact specialty. Qualification alone never activates them. Craft work also produces bounded mood and customer relationship memories; every crafting role has five positive, five negative, and five relationship event definitions, with initial production-quality and customer-sale triggers wired in.

Products are classified from vanilla item traits and names: weapons, armor, mechanical goods, prestige apparel, ordinary apparel, metalwork, stonework, and woodwork. Modded products with unusual definitions may initially fall outside a track; compatibility mappings are a planned tuning surface.

## First-draft boundaries

- Reuses the existing generic **Shop** establishment rather than adding separate workshop types.
- Does not add new crafting recipes or replace vanilla bills.
- The first event triggers cover completed products, flawed low-quality work, and customer reactions. More situational triggers such as rejected commissions, material shortages, rivalry, and stolen credit remain a narrative expansion surface.
- Offline compilation and domain tests cover both RimWorld 1.6 and 1.7, but gameplay validation is still required for job priority, modded recipes, stock hauling, customer demand, and balance.
