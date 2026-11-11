# Community UI review and verification

Reviewed 2026-11-10 (America/Chicago). Source: seven supplied screenshots, open PR metadata/reviews, and local code.

## Branch reconciliation

- [PR #7](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7): organic ceremonies and foundations, head a66953b.
- [PR #16](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/16): Community and Treasury, head 8d346ba.
- `git rev-list --left-right --count origin/feature/organic-ceremonies...origin/feature/community-treasury` returns `0 41`. #16 contains #7 completely. Cherry-picking or merging #7 into it would duplicate work.
- UI branch `fix/community-ui` starts at #16, preserving its history and keeping both existing PR branches untouched. Its PR targets `feature/community-treasury` for a small UI-only review. Neither existing PR is merged or closed.

## Screenshot findings and implementation

- Navigation used a fixed 28px label area and reserved badge space even without a badge. Long text wrapped below the visible row. Measured row heights, a wider scrollable sidebar, aligned icon slots and conditional badge space now keep the complete label available. Removed the unrelated Draft icon and unavailable AssignOwner icon from core navigation.
- Overview, Assembly, Education and Care used a single narrow stream of text across a large empty panel. Shared section bars, spacing, measured text/buttons and responsive two-column sections organize existing information; narrow layouts stack sections.
- Professions rendered every career and level as one long list. A career selector and role search limit the list; each role shows its level, current holders and count of eligible unassigned colonists. Existing assignment/unassignment and qualification checks remain in use.
- Families embedded a full independent window, including a second sidebar and its tabs. Community now shows family summaries with links selecting that family in the unchanged full Families window. Family management remains available there, including relationships, homes, estates and governance.
- One shared content height caused empty scroll space to leak between pages. Heights are now remembered per page. Scroll views and GUI state unwind on renderer failure.
- Treasury tabs wrap to additional rows. Council ballot choices now include all candidates and wrap with other family question buttons instead of overflowing or truncating to four candidates.
- Both package/test scripts omitted Treasury from their build loops. All four assemblies are now built for both versions. CI also builds all four before tests that inspect assembly contracts.

## Compatibility boundary

No domain model, persistence/Scribe code, save field, package ID, economy rule, career prerequisite, service scheduling or module enablement rule changed. The full Families window remains the authority for detailed family management. Opening a page does not create financial transactions. Tests/builds cannot prove actual Scribe load compatibility or live UI behavior.

## Outstanding gameplay review concerns

PR #16 has no inline review threads. PR #7 has 94 threads, 68 unresolved (including repeated and outdated reports). An unresolved flag is not proof that a defect still exists. This UI change addresses the council ballot truncation; it does not mark review threads resolved or claim to clear the gameplay backlog.

Three reports were confirmed still present during this pass: WFL_RescueDefOf lacks [DefOf] in KinRescueDriver.cs; HallDriver records unmet need before checking holdHallMeals; GovernanceDriver unconditionally continues after a refused ReconcileCoup. These remain outside this UI change and should block treating the combined branch as release-ready. Estate/caravan ownership, avatar consent, visitor travel and Care/Education runtime reports also need a separate current-code audit and focused live acceptance.

### Unresolved review inventory at inspection

- [Gate autonomous community elections on the governance setting](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504288998) — Source/FamilyLife/Game/Governance.cs
- [Exclude the player avatar from autonomous comfort jobs](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504289000) — Source/FamilyLife/Game/ComfortDriver.cs
- [Cancel incompatible elections when changing institutions](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504289004) — Source/FamilyLife/Domain/Institutions.cs
- [Route reunion visitors to the confirmed venue map](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504289006) — Source/FamilyLife/Game/KinVisitDriver.cs
- [Track continuous attendance during camping stays](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504289007) — Source/FamilyLife/Domain/Camping.cs
- [Allow enrolled adults to complete their lessons](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504289010) — Source/FamilyLife/Game/GovernanceMeetings.cs (outdated diff)
- [Score hall-keeper candidates using the relevant skills](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504316421) — Source/FamilyLife/Game/OfficeDriver.cs (outdated diff)
- [Revalidate assembly candidates before installing the winner](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504316424) — Source/FamilyLife/Domain/CommunityOffices.cs
- [Interrupt heists whose physical jobs all failed](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504316428) — Source/FamilyLife/Game/GovernanceDriver.cs
- [Clear check-up deferrals when loading another game](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504316432) — Source/FamilyLife/Game/OfficeDriver.cs (outdated diff)
- [Preserve buyer eligibility when launching contraband sales](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504316435) — Source/FamilyLife/Game/GovernanceDriver.cs
- [Seat only diners enrolled in the hall meal](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504763219) — Source/FamilyLife/Game/HallDriver.cs
- [Check caravan-held items before declaring possessions missing](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504763226) — Source/FamilyLife/Game/EstateDriver.cs
- [Separate community-service jobs from the family-stories gate](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504763234) — Defs/FamilyStoryJobs.xml (outdated diff)
- [Continue processing a coup when reconciliation is refused](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504763240) — Source/FamilyLife/Game/GovernanceDriver.cs
- [Register the rescue definitions with the DefOf attribute](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504920796) — Source/FamilyLife/Game/KinRescueDriver.cs
- [Preserve previous users when delivering an inherited item](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504920801) — Source/FamilyLife/Domain/Estates.cs
- [Base teaching demand on students who can actually be taught](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504920808) — Source/FamilyLife/Game/OfficeDriver.cs (outdated diff)
- [Revalidate living claimants before settling an estate](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4504920813) — Source/FamilyLife/Domain/Estates.cs
- [Include actively hauled possessions in estate lookups](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4506612060) — Source/FamilyLife/Game/EstateDriver.cs
- [Allow polling fallback when an assembly cannot start](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4506612063) — Source/FamilyLife/Game/AssemblyDriver.cs
- [Exclude disabled hall meals from neglect tracking](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4506612065) — Source/FamilyLife/Game/HallDriver.cs
- [Filter pre-reform restitution before limiting the scan](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4506612068) — Source/FamilyLife/Game/GovernanceDriver.cs
- [Honor distant invitations accepted on wedding and funeral cards](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4506780257) — Source/FamilyLife/Domain/CeremonyPlanning.cs
- [Offer every candidate on the council leadership ballot](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4506780259) — Source/FamilyLife/UI/Window_Families.Governance.cs
- [Preserve injured rescue relatives when revisiting the site](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4506833642) — Source/FamilyLife/Game/KinRescueDriver.cs
- [Recheck the house-member minimum throughout a camping stay](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4506833643) — Source/FamilyLife/Domain/Camping.cs
- [Use the transfer approach to distinguish paid sales from gifts](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4506833644) — Source/FamilyLife/Domain/FamilySupplies.cs
- [Resolve house inheritance recipients after a house merger](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4506833645) — Source/FamilyLife/Game/EstateDriver.cs
- [Keep reunion visitors through the scheduled ceremony](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4507209844) — Source/FamilyLife/Domain/KinVisits.cs
- [Skip unavailable recruits instead of blocking the heist](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4515377100) — Source/FamilyLife/Game/GovernanceDriver.cs
- [Offer avatar rulers a way to fill community-post vacancies](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4515377114) — Source/FamilyLife/Domain/CommunityOffices.cs
- [Preserve possessions that exceed the estate-item limit](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4515377127) — Source/FamilyLife/Domain/Estates.cs
- [Abort obsolete elections when changing institutions](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4515722483) — Source/FamilyLife/Domain/Institutions.cs
- [Compute succession support from each member's knowledge](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4515722491) — Source/FamilyLife/Domain/Succession.cs
- [Preserve the avatar's eligibility as a ceremony guest](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4515722500) — Source/FamilyLife/Game/Gatherings.cs
- [Allow enrolled adults to complete teacher lessons](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4517138178) — Source/FamilyLife/Game/GovernanceMeetings.cs (outdated diff)
- [Respect stock ownership when handing meals to other houses](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4517138194) — Source/FamilyLife/Game/HallDriver.cs
- [Exclude the Perspective Shift avatar from automatic comfort jobs](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4517138204) — Source/FamilyLife/Game/ComfortDriver.cs
- [Authorize inherited stock for heirs from another house](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4517214407) — Source/FamilyLife/Game/EstateDriver.cs
- [Track continuous attendance throughout camping stays](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4517214417) — Source/FamilyLife/Domain/Camping.cs
- [Evaluate hall-keeper candidates using their own post's skills](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4517214434) — Source/FamilyLife/Game/OfficeDriver.cs (outdated diff)
- [Revalidate assembly candidates before installing the winner](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4517424584) — Source/FamilyLife/Domain/CommunityOffices.cs
- [Keep tracking visitors who cannot leave before the timeout](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4517696209) — Source/FamilyLife/Game/KinVisitDriver.cs
- [End the patient job when their examination finishes](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4517906045) — Source/FamilyLife/Game/ClinicAndClass.cs (outdated diff)
- [Interrupt heists whose transfers were all cancelled](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4517906050) — Source/FamilyLife/Game/GovernanceDriver.cs
- [Register the rescue definitions with the DefOf attribute](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4518213425) — Source/FamilyLife/Game/KinRescueDriver.cs
- [Preserve the dependency requirement when selecting a contraband buyer](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4518213434) — Source/FamilyLife/Game/GovernanceDriver.cs
- [Preserve prior users when completing an inheritance](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4518352485) — Source/FamilyLife/Domain/Estates.cs
- [Exclude the avatar from autonomous tribute collection](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4518352497) — Source/FamilyLife/Game/GovernanceDriver.cs
- [Seat only diners enrolled in the hall-meal session](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4518352511) — Source/FamilyLife/Game/HallDriver.cs
- [Preserve captured relatives during rescue cleanup](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4518352523) — Source/FamilyLife/Game/KinRescueDriver.cs
- [Search caravan cargo before declaring estate items missing](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520304790) — Source/FamilyLife/Game/EstateDriver.cs
- [Apply subject age restrictions to each pupil's XP grant](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520304802) — Source/Education/Game/Classes.cs
- [Revalidate the mental state before completing comfort](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520304810) — Source/FamilyLife/Game/ComfortDriver.cs
- [Use a bedside-specific job for patients unable to travel](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520304823) — Source/FamilyLife/Game/OfficeDriver.cs (outdated diff)
- [Restrict assessment sittings to the teacher's current map](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520304834) — Source/FamilyLife/Game/OfficeEvents.cs (outdated diff)
- [Exclude the avatar from autonomous assessment jobs](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520446644) — Source/FamilyLife/Game/OfficeEvents.cs (outdated diff)
- [Wait for the make-up sitting to finish before closing testing](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520446647) — Source/FamilyLife/Domain/OfficeEvents.cs (outdated diff)
- [Reassign undelivered inheritances when their heir dies](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520446651) — Source/FamilyLife/Game/EstateDriver.cs
- [Offer every council candidate on the player's ballot](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520689370) — Source/FamilyLife/UI/Window_Families.Governance.cs
- [Preserve downed relatives when revisiting a rescue site](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520689374) — Source/FamilyLife/Game/KinRescueDriver.cs
- [Ask avatar heads before appointing an estate-compensation steward](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520786614) — Source/FamilyLife/Domain/Estates.cs
- [Preserve unrelated queued jobs during module cleanup](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4520786616) — Source/FamilyLife/Care/Game/CareRemoval.cs (outdated diff)
- [Skip disabled module posts when counting an existing assembly](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4521002312) — Source/FamilyLife/Domain/CommunityOffices.cs
- [Clear hall-keeper demand when hall meals are disabled](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4522000492) — Source/FamilyLife/Game/HallDriver.cs
- [Reject distant invitations without a supported travel path](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4522000504) — Source/FamilyLife/Domain/CeremonyPlanning.cs
- [Continue coup upkeep when reconciliation is rejected](https://github.com/DegeneratesAnonymous/rimworld-family-life/pull/7#discussion_r4522000512) — Source/FamilyLife/Game/GovernanceDriver.cs

## In-game acceptance still required

- Check all pages at UI scales 1.0, 1.25 and 1.5 and narrow/wide resolutions, including long translations and badge counts.
- Confirm section columns stack cleanly, no horizontal clipping, and scroll positions recover after filtering and switching pages.
- Assign/unassign an entry-level and qualified advanced profession; check eligibility counts after assignment and module disablement.
- Open each family from Community, verify its selection and all detailed tabs, then close/reopen and save/load.
- Test Care check-up and Education assessment controls against the previous branch; validate Treasury subpages and five/six-candidate council ballots.
- Use a copied existing save with the same modules and confirm wages, balances, offices, classes, appointments and household records remain intact.

No in-game session, game-folder staging, publication or merge was performed.

No in-game session, game-folder staging, publication or merge was performed.


## Verification results

- Full updated scripts/Test.ps1 completed successfully on the final UI sources.
- 1,360 tests passed, zero failed/skipped.
- All eight Release builds passed: Framework, Education, Care and Treasury for RimWorld 1.6 and 1.7.
- Education and Care pure-domain isolation builds passed.
- All four package layouts passed for both game versions.
- Harmony checker: 31 patch classes passed against installed 1.7; 31 passed against 1.6 reference assemblies.
- Build warnings remain: existing unused CenterOf local, obsolete Treasury translation overloads, isolation output-path warnings, and package vulnerability checks unavailable under restricted network access. These were not zero-warning builds.
- XML parsing and git diff --check passed. No domain or persistence source changes.
- Remote CI is separate from these local results; the workflow now includes all modules and runs assembly-contract tests after building.

