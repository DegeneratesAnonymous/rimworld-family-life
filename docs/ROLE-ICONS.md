# Family role icons

The supplied `WFL_Role_Icon_Pack` pictures are used exactly as drawn (64x64, transparent). Nothing is redrawn, generated or recoloured; every role's tint is white, so the artwork is unchanged. House-colour tinting is **not** implemented.

## Where things live

| What | Where |
|---|---|
| Presentation registry (label key, description key, texture, tint, display priority) | `Source/FamilyLife/Domain/RolePresentation.cs` (`RoleCatalog`) |
| Who holds which role, and who may see it | `RoleDisplay.PublicRoles` (same file) |
| Texture loading, fallback, colonist-bar cache, drawing | `Source/FamilyLife/Game/PostBadges.cs` (`RoleIcons`, `RoleBar`, `PostBadges`) |
| Words and settings text | `Languages/English/Keyed/Roles.xml` |
| Active pictures (loaded by the game) | `Textures/WilsFamilyLife/Roles/` |
| Kept, never loaded | `Assets/RoleIcons/Reserved/`, `Assets/RoleIcons/ProtectorAlternatives/` |

## Active icons

| Role | Icon | Source of the role |
|---|---|---|
| Colony leader | colony_leader | the institution's leader (not under consensus) |
| Council representative | council_representative | head of a house seated on the council |
| Healer / Teacher / Hall keeper / Chaplain | healer / teacher / hall_keeper / chaplain | colony posts |
| Head of house | house_head | head of a house with more than one person in the colony |
| Public heir | public_heir | a designated heir, when the observer knows (see below) |
| Steward | steward | a house steward |
| Ceremony organizer | ceremony_organizer | organizer of an open (to those told) or confirmed occasion plan |
| Trusted associate / Associate | trusted_associate / associate | a non-member with standing with a house |
| House Protector | protector_eye | **reserved: no mechanic assigns it yet** |

`associate.png` is also the generic fallback picture; if even that is missing the game's own placeholder is used. A missing picture logs one warning.

## Reserved (kept, not loaded, no role)

`mediator`, `quartermaster`, `envoy`, `judge`, `chronicler`, `caravan_guide`, `heirloom_keeper`, `caregiver` (Assets/RoleIcons/Reserved), and the alternative Protector pictures `protector_spears`, `protector_tower`, `protector_house`. A test fails if one of these appears in the registry.

## House Protector

There was no "Watch Captain" role or identifier anywhere in the mod, so there was nothing to rename and no saved value to migrate (a test checks the old name appears nowhere). House Protector exists as presentation only: label, description and `protector_eye`. Its duties (warnings, escort weighting, negotiation influence, story consequences) are a separate design and are not built. Nothing produces the role, so no emblem is shown for it.

## Colonist bar

One emblem per pawn, in a corner of the portrait box: size small/medium/large (30/38/46% of the box width), corner chosen in settings, dark backing so it reads on any portrait. Hover shows the role, house, a short explanation, other roles the pawn holds and a click hint; click selects the pawn and opens their Family tab (or the Families window when they are not on the map). Priority: colony posts (leader, council representative, healer, teacher, hall keeper, chaplain), head of house, public heir, steward, House Protector, ceremony organizer, trusted associate, associate; ties go to the lower house id.

Defaults: icons on, small, top left, "leadership and appointed roles only" on, associate icons off. Trusted associates show only when the leadership-only option is off; ordinary associates only when their own option is on.

## What is hidden

The observer is the person being played under Perspective Shift, or nobody. With nobody, only public facts show: an heir counts as public only when every adult in the house has been told; an organizer only when the occasion is confirmed. A played character also sees what they were told (their own heir record, a plan they know of or belong to). Conspirators, undisclosed crime-family roles and secret alliances have no emblem and never enter the role list.

## Performance

Drawing reads one cached entry. An entry is worked out again when the family records' revision changes, a display setting changes, the person being played changes, or after 180 ticks; the tooltip text is built on first hover and kept. Textures are loaded once per role. No pawn or house scan, succession calculation, LINQ or list allocation happens while drawing (a test checks the drawing method's source). Perspective Shift's avatar is read from a field.

## Not live verified

Nothing here has run in RimWorld. Still to check in play: the default corner against the game's own colonist-bar overlays (with and without Ideology), crowded bars and UI scales, click behaviour against the bar's own click handling, and Perspective Shift switching characters.
