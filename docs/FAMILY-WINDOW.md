# The Families window

The window is a persistent **header**, a **family list** on the left, and **six tabs**: Summary, People, Relationships, Events, Home, Leadership.
Older documents (PHASE1-7, EVENTS) talk about eight tabs; the table below says where each of those went. Nothing was removed.

## Where the old tabs went

| Old tab / section | Now |
|---|---|
| Overview: founded, counts | Summary > Family status; header |
| Overview: house ambition | Leadership > Ambition (with reasons, urgency, ways forward); header badge |
| Overview: family tree | People > Family tree (zoom, recenter, scroll; no closeness meter) |
| Overview: culture, values, legacies, memories | Events > Chronicle > Family culture |
| Relationships | Relationships (four bars per person, search, filters, sorting, detail panel) |
| Moments: ceremonies | Events > Upcoming (planned) and Recent (finished) |
| Moments: belonging, agency | People > person detail > How they belong |
| Moments: attention between children | Relationships > Attention card (only when uneven) |
| Moments: standing between families | Leadership > Alliances and requests |
| Moments: incidents, plots, forgiveness | Events > Recent (Matters still open, with Forgive) |
| Moments: supply requests | Events > Recent |
| Moments: promises | Events > Upcoming; Summary |
| Moments: recent moments | Events > Recent (event cards) |
| Households (with Move...) | Home > Households (row menu `...`) |
| House: petitions, marriage alliances | Leadership > Alliances and requests |
| House: stewardship, offices, succession, colony institution, crime, favours, hall, assembly | Leadership > Family offices and Institutions |
| House: associates, crest, branch | Leadership > Associates and identity |
| Zone: pantry reserves, emergency sharing, public funds | Home > Supplies (reserves and Household policies) |
| Zone: family area, meeting place | Home > Family area |
| Traditions (mute, hold now) | Events > Upcoming > Traditions |
| History (chronicle) | Events > Chronicle |
| Heirlooms and estates (in House) | Leadership > Heirlooms and estates |
| Save records to a file | `...` menu in the header (colony overview only) |

## The header

Crest, family name, House Head, Steward (when there is one), members, households; a cohesion badge, the current ambition, and a count of things needing
attention (urgent ones in red). Top right: **whose eyes** the window shows ("Viewing as Debby" or "Colony overview") and the `...` menu. The header and the tab bar stay
put while a tab scrolls. When the person being played is outside the house, cohesion, ambition and alerts say "Not known to <name>" instead.

## The family list

Crest, name, member count, and either the first thing needing attention ("Food needed") or the cohesion tier. A gold bar marks the selected family; a dot marks
something to look at (red if urgent). Search and sorting (name, size, standing, needs attention). It collapses with `<`, and starts collapsed when there is only one family.
Row facts are computed a few per frame and kept until the records change or about fifteen seconds pass; nothing is recomputed while a row is drawn.

## What each tab is for

- **Summary**: a dashboard of cards. Family status; **Needs attention** (each line goes to where it can be dealt with); Upcoming; the three most meaningful recent events; a portrait row.
- **People**: a portrait list grouped by household, or the family tree. Each person shows portrait, name, relationship to the viewer (or the head), age stage, household, public role icons, what they are doing, and a visible concern. Choosing a person opens a detail panel; the window stays open. Double-click finds them in the colony.
- **Relationships**: whose feelings (the played character, or a menu in the overview), search, Family / Associates / Everyone known, and sorting (Closest, Least trusted, Most tense, Recently changed). Four bars per person (closeness green, trust blue, tension red, familiarity gold; grey when unknown) and a trend arrow. A row opens its detail: values, recent causes, shared memories, grievances, promises, and what the acting pawn can do, each action greyed out with its reason when it cannot be used.
- **Events**: *Upcoming* (occasions, traditions, promises, visits, camping, marriage talks; traditions can be muted or held now), *Recent* (open matters, then event cards with a date, kind, participants, description, consequence and a "Defining memory" mark), *Chronicle* (permanent history and culture). Category filters apply to Recent and Chronicle. Empty systems are one line, not a heading each.
- **Home**: *Households* (who lives with whom, bed, room, area, problems; `...` menu names who moves where), *Supplies* (current / wanted for food, fuel, medicine, apparel with a bar and a target slider; household policies), *Family area* (what it covers, beds, access, meeting place, missing essentials, and the actions to draw, choose, remove or set a meeting place).
- **Leadership**: an icon card for each office that exists (House Head, Steward, Public Heir, Healer, Teacher, Hall Keeper, Chaplain) with holder, description, why they hold it, status and actions; the Ambition card; Alliances and requests; Institutions; Associates and identity; Heirlooms and estates. Cards with nothing in them are not drawn. **House Protector** is not shown: the mod has no mechanic that assigns it (see ROLE-ICONS.md).

## Perspective

The window reads `FamilyComponent.PerspectivePawnId`. Zero is the colony overview: the full administrative view. Otherwise only what that person knows is shown, with
"Not known to <name>" where something is hidden. Specifically: an heir named in private, rifts between two other people, promises and incidents the viewer was not part of or told of, marriage
negotiations they were not told about, and how a house they do not belong to is doing, are all left out. Without Perspective Shift the pawn you select becomes the perspective
(only when the selection changes), and the header lets you pick another character or the overview. With Perspective Shift the window follows the character you are playing. The integration
is optional and read through the existing cached bridge; the window never reflects.

## Under the hood

- `Domain/FamilyWindowModel.cs`: tabs, the legacy mapping, remembered state, alerts, family summaries, relationship rows, event categories, supply levels, office cards. Pure and tested.
- `Domain/FamilyTimeline.cs`: Upcoming, Recent and Chronicle lists.
- `UI/FamilyKit.cs`: the shared parts (cards, section headers, badges, metric bars, alert rows, tab strips, event cards, empty states). Gold means selection, importance or the primary action only.
- `UI/FamilyView.cs`: the per-family view model (rebuilt on a change in the records, the family, the viewer, or every ten seconds) and the People rows (every three seconds).
- `UI/Window_Families.*.cs`: one file per tab.
- Only the selected tab is drawn. Long lists skip what is outside the scrolled view. Tooltips for rows and tree nodes are built only when hovered. Portraits are drawn a few new ones per frame. Textures are loaded once by `RoleIcons`.
- What the window remembers (family, tab, sub-tab, filters, sorting, open person, list collapse) lives in `Window_Families.State`, which outlives the window and is never saved. Saved data is unchanged.

## Checks

Offline: `FamilyWindowTests` (mapping, remembered state, alert navigation, perspective, filters, sorting, supplies, offices, missing leader, old-save shapes, text keys, drawing rules).
In the game, with a family of each size, at 100 / 125 / 150 % UI scale and small and large windows: open every tab and sub-tab; collapse the family list; switch family and perspective;
open a person, a relationship and an alert; zoom and recenter the tree; filter events; change a reserve slider; try each greyed-out action and read its reason.
