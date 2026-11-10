# Verifying the module extraction in the game

Everything below is **unverified until someone plays it**. The automated checks (unit tests, assembly inspection, package layout, offline reading of real save files) prove structure; none of them starts RimWorld. Tick a box only for something you saw. Record what you saw, not what should have happened.

Stage the packages with `.\scripts\Stage-ToRimWorld.ps1` (close RimWorld first). Turn on Development mode for the developer actions ("Wil's Family Life, testing"); **Show module status** lists every module, its state and any registration problem.

## A. Framework only
Stage with `-FrameworkOnly` (and make sure `WilsEducation` and `WilsCommunityCare` are not in `Mods`).
- [ ] The game loads with no red error. The log shows `Modules: wils.framework, wils.familylife`, and no problem.
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
