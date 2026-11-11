# Verifying the module extraction in the game

Everything below is **unverified until someone plays it**. The automated checks (unit tests, assembly inspection, package layout, offline reading of real save files) prove structure; none of them starts RimWorld. Tick a box only for something you saw. Record what you saw, not what should have happened.

## First runtime sequence (do this before anything below)
1. Back up the old (all-in-one) save by copying its `.rws` file somewhere safe.
2. Stage all three packages: `.\scripts\Stage-ToRimWorld.ps1` (RimWorld closed). Enable Development mode.
3. Load the old save **first**, before starting any new game. Do not play.
4. Confirm the one authoritative log line, written when that save loads (not at the main menu): `Modules: wils.framework, wils.familylife, wils.education, wils.care`. No `arrived after start-up` message may appear. If a module is missing from the line, stop and send the log.
5. Run **Show module status**. Expect every module `active`, none FAILED, no ABSENT entry.
6. Save under a new test name, then reload that test copy. The module line is not repeated (it is logged once per game session).
7. Only then begin the scenarios in sections A to H.

Extra checks for the hardening change (record what you see):
- [ ] **Freeze timing.** The `Modules:` line appears when a game starts or loads, after every mod has initialised, and exactly once however many times you load a save in the session.
- [ ] **Healer-specific refusal, then replacement.** Make a patient refuse the Healer (high tension with them, low trust); the clinic trace says they would rather not, and the Healer diagnostics count one under "this doctor". Replace the Healer: the new Healer asks that patient again.
- [ ] **Service-wide refusal, then replacement.** Make a patient refuse while in a very low mood with no problem with the doctor (counted under "check-ups"). Replace the Healer: the patient is still left alone until the six-day cooldown ends.
- [ ] **Old save refusals.** A pre-change save with a declined patient still leaves them alone after loading; saving and reloading neither adds nor loses any.
- [ ] **Absent-module notice.** With only the framework staged, load a save that has a Teacher or Healer: one letter names the missing module(s), says what is inactive, that records are intact, and how to restore or remove properly. "Don't show again for this save" stops it after a reload, but removing a different module later warns again. A brand-new game shows nothing.
- [ ] **Prepared module.** Prepare Education, save, remove it, load: **no** "not installed" letter for Education (removed on purpose); the log says it was prepared for removal. Prepared Education plus a missing Care warns about Care only.
- [ ] **Reinstall.** Reinstall a module that had been reported: it is active again (or still Prepared if you prepared it), its records are unchanged, and the dismissal no longer applies.

Stage the packages with `.\scripts\Stage-ToRimWorld.ps1` (close RimWorld first). Turn on Development mode for the developer actions ("Wil's Family Life, testing"); **Show module status** lists every module, its state and any registration problem.

## A. Framework only
Stage with `-FrameworkOnly` (and make sure `WilsEducation` and `WilsCommunityCare` are not in `Mods`).
- [ ] The game loads with no red error. The log (written when the game loads) shows `Modules: wils.framework, wils.familylife`, and no problem.
- [ ] No error about a missing job, thought or hediff def, and none about a missing Education or Care type.
- [ ] The Family window, people, relationships and navigation work.
- [ ] No lectern "hold class" button, no medical-bed "begin check-ups" button, no Teacher or Healer vacant card.
- [ ] Load a save from the all-in-one version: it loads; a Teacher or Healer already elected shows in the history; no class or check-up runs; the holder is not removed for neglect after 15+ days.

## B. Framework plus Education (no Care)
- [ ] The log shows `wils.education` after the framework; **Show module status** says Active.
- [ ] The Teacher is elected once (one card, not two).
- [ ] A class starts (dev action "Start a class now") and finishes; attendance and XP appear **once**, not twice.
- [ ] The yearly assessment can be started ("Start annual testing now"); pupils sit; results appear once.
- [ ] The lectern button appears; "Hold class" queues one when the teacher cannot start.
- [ ] No Healer card, no check-up button, no Care error.

## C. Office replacement (Education, then Care)
- [ ] Replace the Teacher mid-class (resign or dev action). The class ends; every pupil leaves their class job; no one is stuck.
- [ ] What the children studied (the lesson log) is still there for the new teacher; the old teacher's assessment in progress is gone.
- [ ] Replace the Healer mid-round. The clinic closes; the patient is released. The new Healer **continues the round**: those already seen are not examined again; people who declined are still left alone.
- [ ] The former holders do nothing further.

## D. Save and load
- [ ] Save during a class, load: no duplicate XP, no pupil stuck on the class job.
- [ ] Save during an assessment, load: it resumes or ends cleanly with no duplicate result.
- [ ] Save during a clinic visit, load: no duplicate appointment, no stuck patient.

## E. Preparation for removal
- [ ] Prepare Education during an active class. The report says **Prepared**; the teacher and pupils are free; no reservation is left; the queue and any notice are gone.
- [ ] Save, quit, restart with Education still installed: it stays inactive (no class starts). **Show module status** says Prepared.
- [ ] Remove the Education folder and load that save: the framework works; no red error; any warning is about nothing the module owns.
- [ ] Force a failure (leave a pawn on a module job, or run the step while a class is starting) and confirm the report says **NOT prepared**, and a second run can succeed.
- [ ] Repeat for Community Care (active clinic, then active round).

## F. Reinstallation
- [ ] Reinstall the package: it is still inactive. No cancelled session returns.
- [ ] Run **Reactivate**: status says Active; new work starts (a class, a round) from a clean state.
- [ ] Both modules together, either order of reinstalling, each only once.

## G. Old saves
- [ ] Load a copy of a pre-extraction save (the all-in-one version). Both posts keep their holders; the Healer's saved round state is intact; the Teacher's lesson log (if any) is intact.
- [ ] Save, quit, load again: nothing doubled (queues, rewards, appointments).
- [ ] Open the save file and confirm the module state is now in the posts' `payload*` lists and the old per-post labels are no longer written.

## H. Performance
- [ ] With the modules installed but idle, compare tick time with the all-in-one build in the same save. (No improvement is expected.) With a module prepared, confirm it adds nothing to the maintenance profile.

## What the automated checks do cover (so you need not repeat them by hand)
Registration order and refusals; lifecycle persistence, failure and retry; the record-ownership rules; hand-over hooks; absence not counting as neglect; migration of real pre-extraction save data (the office records, offline); assembly references, type ownership and package contents; both game versions compiling; the Harmony patches matching the game.
