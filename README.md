# Wil's Family Life

A RimWorld mod (1.6 and 1.7) that keeps durable family records and, over later versions, turns them into a generational family simulation: relationships, shared meals and activities, memories, traditions, and the strains that come with them.

**Status:** Phases 1-7 and the current houses/governance expansion are built. Player testing reports the mod and its performance are working well; the newest ceremony planning, visiting-kin, camping, community-post and political drivers still need focused live acceptance. Every change is also checked by domain tests, mutation checks and 1.6/1.7 builds. See [docs/DESIGN.md](docs/DESIGN.md) and [docs/OPEN-ITEMS.md](docs/OPEN-ITEMS.md) for the remaining release gates.

## Family stories and household supplies

Adds 48 value-tradition episodes, witnessed incidents and shared news, family supply collection and negotiation, identifiable theft/restitution, and adult revenge conspiracies. Lethal attacks are optional and off by default. Homes now express preferences while player allowed areas remain independent. See [the feature guide and live validation checklist](docs/FAMILY-STORIES.md); this expansion has not yet been played in-game.

## Development roadmap

See [docs/ROADMAP.md](docs/ROADMAP.md) for individual agency, gradual relationships, house membership and branches, crests, prestige, succession and political institutions. The current development PR includes chosen houses, earned associates, colonist-based actions, crests, gradual relationship updates, household recovery, prestige, succession disputes, elections, councils, coups and evidence-backed crime families. See [the implementation guide](docs/HOUSES-AND-ROLEPLAY.md) for supported play styles and limits. These newer jobs and UI still require focused live acceptance.

## Organic ceremonies

Families autonomously plan funerals after a death, a family wedding reception after RimWorld records a marriage, and an annual grand-reunion gathering when meaningful relatives are away. The player influences these stories through relationships, family culture, attendance, leadership and the spaces they build rather than selecting outcomes directly. Weddings, funerals and reunions have 26 dedicated social scenes, and successful organizers earn modest personal prestige while the house's standing changes gradually. A reunion card can invite up to six eligible distant relatives; they travel safely from the world, may be held up on the road (a real quest in the Quest tab, a world site and a map, which the player may accept or ignore), arrive on foot or by shuttle, visit for several days and are handed back to the world when they leave.

## Heirlooms and inheritance

Weapons, garments, books and art that a person has carried a long time, or that mark a wedding or coming of age, become heirlooms. When someone dies, their estate goes to a named heir, their partner, their children and other close kin, or their house, and is carried over by the heir; a contested heirloom becomes a case with claimants, reasons, a mediator and a compromise. Ordinary supplies stay communal, the office of head and possessions are inherited separately, and the person you are playing is asked rather than answered for. See [docs/GOVERNANCE.md](docs/GOVERNANCE.md).

## Design principles

- **Standalone.** Harmony is the only hard dependency; Biotech and other mods only enhance.
- **No game objects in saves.** Everything is stored by pawn id with name snapshots, so dead, discarded and unloaded pawns cannot break a save.
- **Budgeted simulation work.** Hourly observation and bond updates, cached work discovery, bounded nearby social searches, and staggered maintenance. Live TPS still needs profiling.

## Building

1.7 runs on .NET 10 (Waylith); 1.6 runs on .NET Framework 4.7.2. One version builds at a time and its DLL goes in that version's `Assemblies` folder. You need the .NET 10 SDK. No RimWorld install is needed to compile: game references come from the `Krafs.Rimworld.Ref` package.

```
dotnet build Source/FamilyLife/FamilyLife.csproj -c Release                      # 1.7
dotnet build Source/FamilyLife/FamilyLife.csproj -c Release -p:GameVersion=1.6   # 1.6
dotnet test  Source/FamilyLife.Tests/FamilyLife.Tests.csproj                      # domain tests
.\scripts\Test.ps1                                                               # tests + both builds + offline Harmony check
python scripts/mutation_check.py                                                 # break rules on purpose; every one must be caught
.\scripts\Stage-ToRimWorld.ps1                                                   # build and copy into the game's Mods folder
```

Close RimWorld before staging; it keeps the mod DLL locked.

## Layout

| Path | Contents |
|---|---|
| `Source/FamilyLife/Domain/` | The family model. Pure C#, no game or Unity types. The test project compiles this folder directly, so a violation fails the build. |
| `Source/FamilyLife/Persistence/` | Save/load for the domain classes (partial classes). |
| `Source/FamilyLife/Game/` | Mod entry, game component, pawn observer, Harmony hooks, debug actions. |
| `Source/FamilyLife/UI/` | Families window, pawn Family tab. |
| `Source/FamilyLife.Tests/` | Unit and property tests for the domain, plus a guard that every model field is saved. |
| `Defs/`, `Languages/`, `About/` | Game content. All player-facing text is in keyed language files. |

## Open work and calibration

[Open-item inventory](docs/OPEN-ITEMS.md) distinguishes implemented fixes, offline evidence, live acceptance and future milestones. [Seeded calibration results](docs/calibration/summary.md) compare five-year synthetic histories; they do not measure live TPS.

## Performance

The simulation uses cached work discovery, bounded social searches and staggered maintenance. See [docs/PERFORMANCE.md](docs/PERFORMANCE.md) for implementation details, local benchmark limits, and developer actions for measuring a real save at 3× speed. Live TPS gains have not yet been measured.

## License

MIT. See [LICENSE](LICENSE).
