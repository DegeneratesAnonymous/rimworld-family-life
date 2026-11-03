# Wil's Family Life

A RimWorld mod (1.6 and 1.7) that keeps durable family records and, over later versions, turns them into a generational family simulation: relationships, shared meals and activities, memories, traditions, and the strains that come with them.

**Status:** Phases 1-7 built. Phases 1 and 2 have been checked in the game; Phases 3-7 have not. See [docs/DESIGN.md](docs/DESIGN.md) for the design, [docs/PHASE1.md](docs/PHASE1.md), [docs/PHASE2.md](docs/PHASE2.md), [docs/PHASE3.md](docs/PHASE3.md) and [docs/PHASE4-7.md](docs/PHASE4-7.md) for what works, what is tested and what needs checking in the game.

## Design principles

- **Standalone.** Harmony is the only hard dependency; Biotech and other mods only enhance.
- **No game objects in saves.** Everything is stored by pawn id with name snapshots, so dead, discarded and unloaded pawns cannot break a save.
- **Near-zero tick cost.** No per-pawn tick work; one game component, an hourly reconcile, and a short debounce after a change.

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

## License

MIT. See [LICENSE](LICENSE).
