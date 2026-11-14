# Installing Wil's Family Life and its modules

Wil's Community Framework and its optional modules are five packages. **This module candidate has not been gameplay-validated in this environment**; the steps are what the layout and the scripts are built for, and [MODULE-VERIFICATION.md](MODULE-VERIFICATION.md) is how to check them.

| Package | Folder in `Mods` | packageId | What it gives you |
|---|---|---|---|
| Wil's Community Framework (the framework) | `WilsFamilyLife` | `SpacemanWil.WilsFamilyLife` | Family records, houses, relationships, traditions, the community posts (hall keeper, chaplain), elections, and the hooks the modules plug into. Works alone. |
| Wil's Education | `WilsEducation` | `SpacemanWil.WilsEducation` | The Teacher: classes, lessons, subjects, books, yearly assessment, the lectern button |
| Wil's Community Care | `WilsCommunityCare` | `SpacemanWil.WilsCommunityCare` | The Healer: check-up rounds, the clinic, bedside visits, the outbreak round |
| Wil's Treasury | `WilsTreasury` | `SpacemanWil.WilsTreasury` | Accounts, physical silver backing, wages, debts and taxation |
| Wil's Cooking & Hospitality | `WilsHospitality` | `SpacemanWil.WilsHospitality` | Cooking careers, establishments, service, shops and inns; paid operations use Treasury's optional commerce capability |

Education and Community Care each need only Wil's Family Life (and Harmony). They never need each other: any of the four combinations is valid.

**Load order:** Harmony, Wil's Family Life, then Treasury, Education and/or Community Care, then Cooking & Hospitality (their `About.xml` says `loadAfter`; RimWorld's mod manager will sort them). Each module announces itself when the game starts; if the framework is missing or loads after, the module will not work and the log says why.

## Updating an existing game that used the all-in-one mod

The old single mod included the Teacher and the Healer. The new base package does **not**. Update in this order:

1. Make a copy of your save.
2. Install Education and Community Care alongside Wil's Family Life, and update the base package. Load order as above. Until the modules are installed, no class, assessment, check-up or clinic runs; nothing is deleted.
3. Start the game and load the save. A Teacher or Healer the colony had already elected stays on record. With the module installed, they carry on; without it, the post is kept as history, is not neglected, is not put to the yearly vote, and nobody loses standing for it.
4. Your old records come across by themselves the first time each module needs them (the old fields are read once and moved into the module's own record; migrating twice changes nothing). This was checked offline against the office records of real saves from before the change; it has not been run through RimWorld's loader.

A save made while a module was installed and later loaded without it keeps the module's records untouched and unread.

## Taking a module out of a game you want to keep playing

1. With the module still installed and the game running, open the developer menu (needs Development mode) and run **Wil's Family Life, testing: Prepare Education for removal** (or **Prepare Community Care for removal**). It ends any class, sitting, clinic or appointment, releases everyone, clears the queue and any waiting planned event and its notice, removes the module's own memories and check-up condition, and gives up the post. It logs a report.
2. If the report says **NOT prepared**, read what it lists, and run the step again. Nothing is silently discarded: a failed preparation is saved with the game and can be retried after a restart.
3. When it says **Prepared**, save and quit. The module stays stopped across restarts: it will not resume classes or appointments, even if you leave it installed. Then remove the package.
4. To bring it back, install the package again and run **Reactivate Education / Community Care**. New work starts from a clean state; cancelled sessions do not return. Without reactivating, a prepared module stays inactive.

Not undone by preparation (and said so in its report): skill experience already given, health already changed, the neutral records (what each child studied; who was seen, missed or declined). A class cut short is settled once for the time attended.

**Removing a module without preparing it is not supported.** It should load (the game will warn about the missing job, hediff and thought defs of the module), but pawns may be mid-class and still carry the module's memories and check-up condition; that is what preparation is for.

## Building and staging from the repository

```
.\scripts\Test.ps1                 # builds all five assemblies for 1.6 and 1.7, runs the tests, assembles and checks the packages, checks the Harmony patches
.\scripts\Build-Packages.ps1       # assembles all five package folders under dist and checks their layout
.\scripts\Stage-ToRimWorld.ps1     # builds and copies all five into the game's Mods folder (RimWorld must be closed)
.\scripts\Stage-ToRimWorld.ps1 -FrameworkOnly          # the framework alone
.\scripts\Stage-ToRimWorld.ps1 -Only WilsEducation     # one package (the others already in Mods are left untouched)
```

Do not publish any of these to the Workshop from the build output without playing the checklist in MODULE-VERIFICATION.md first.


## Cooking & Hospitality development candidate

Optional package `WilsHospitality` adds Community -> Establishments. Use matching Framework/Treasury builds from the candidate. See [COOKING-HOSPITALITY.md](COOKING-HOSPITALITY.md) for setup, implemented behavior, event sources and live acceptance requirements. CI publishes a checked five-package gameplay-test archive; it is not a Workshop release.

The test zip includes matching builds, `README-TESTING.md` for installation and the first restaurant, `IMPLEMENTATION.md` for detailed behavior, and `BUILD-INFO.json` with the source commit and bundled-file checksums. See [the gameplay test checklist](HOSPITALITY-TESTING.md).
