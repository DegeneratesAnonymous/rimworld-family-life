# Testing ground

A quick way to try every family feature without waiting weeks of game time. A saved game cannot be made without running RimWorld, so this sets one up in a few minutes and you save it yourself.

## Start

1. New colony, scenario **Three Families: testing ground** (needs Biotech). It is the main Three Families scenario with much more food, silver and materials.
2. Choose a flat, temperate start so the camping tile finder has land around it. Turn on developer mode.
3. Once the pods land: **Debug actions, Wil's Family Life, testing**:
   - **Give every adult colonist some skill and a calling** (so there are people fit for the doctor, teacher and hall keeper posts, and with a reason to stand).
   - **Build a dining hall and a medical room here**, with the mouse over open ground next to the base. It builds walls, roofs, floors, three tables with chairs, four hospital beds and meals, and names the hall.
   - **Choose the colony's posts now**.
4. Save as **WFL testing ground**.

## What to try, in order

| Feature | What to do | What you should see |
|---|---|---|
| Posts | Open the Families window, Governance | Doctor, teacher, hall keeper holders; the chaplain only with Ideology and a moral guide. Colonist bar shows a tag on each holder; the pawn's family tab lists roles |
| Class | Debug: **Bring a class, a check-up round and a hall meal due now**, then unpause | The teacher walks to each free child in turn and talks; a message "is holding a class" |
| Adult students | Mod settings: let adults join the class | Adults with something to learn are taught too |
| Check-ups | Same debug action | Doctor and patients meet beside a hospital bed; each patient gets "recent check-up" (twice as long for the doctor's house) |
| Hall meal | Same debug action | Keeper sweeps if dirty, diners sit, keeper fetches a meal and sets it before each, in turn |
| School exclusion | Name a different room as the hall (**Name the dining hall**), or build a room with school furniture and clear the name | The game only picks a real dining room, never a classroom |
| Mental break help | Select a pawn with a partner or parent and run **Push the selected pawn close to a mental break** | Their partner or parent walks over and talks; a message says how it went; a good talk lifts them or ends the break |
| Yearly assembly | **Call the yearly assembly now**, then **Skip the assembly's campaign to voting** | A letter lists who stands and why; a colony-wide gathering; speeches as messages; ballots cast on arrival; a results letter; "took part in the assembly" mood |
| Camping | **Propose a camping trip** for a pawn, then agree in the Families window and form a caravan to the shown tile | After a day or two there, a message about how it went |
| Kin by shuttle or foot | **Create a distant relative and call a grand reunion**; the message says whether they come by shuttle (spacer faction) or on foot | Arrival letter; the reunion includes them; they leave by the map edge after about three days |
| Guest door | Let a colonist from another family try to use a room in a family zone | They knock, are let in, and carry on with what they came for |
| Crests | Start a new game with several surnames | Each house has a different crest; make a branch and its crest keeps the parent's main colour and mark |

## Heirlooms, estates and the rescue quest

Debug actions are under **Wil's Family Life, testing** (all of these bring forward what the game would do by itself through the same code; none writes a result into the records).

| What to try | Action | What you should see |
|---|---|---|
| An heirloom | **Create an heirloom for the selected pawn** | A masterwork sword (or their best gear) recorded as an heirloom, listed under Governance, "Possessions, heirlooms and estates" |
| A named heir | **Name the selected pawn's closest relative as heir** (or the "Name an heir..." button for a pawn you play) | The heir is told; the line appears in the pawn's own list only |
| An estate | **Kill the selected pawn and open their estate** | A message with the number of keepsakes; if they are still on the body, a note that the body must be stripped before burial |
| A dispute | **Force a contested inheritance** (the pawn needs two grown relatives), then **Skip the open estates' current wait** several times | Claims with reasons, the family talking it over, a mediator, a spread of different heirlooms, a grievance or a Steward appointed |
| Questions to you | Select an heir or the mediator and use Perspective Shift, or select them in the Families window | A question under "Waiting for your answer"; leave it and it lapses with no delivery |
| Delivery | **Try the pending heirloom deliveries now** | The heir walks to the item and carries it; the list shows "now with ..." |
| A lost item | **Fail a delivery: destroy the next undelivered heirloom** | The item is recorded as lost and the case closes; nothing replaces it |
| The rescue quest | **Create a reunion rescue quest (strand the selected house's travelling relatives now)** (after creating a distant relative and calling a grand reunion) | One letter, and a quest in the Quest tab naming the house and the relatives |
| Trouble | **Choose the trouble for the open rescue** before accepting | Each of breakdown, injury, wild animals, raiders and weather |
| Accepting | **Accept the open rescue quest now** (or accept it in the Quest tab) | A site on the world map; form a caravan to it; the relatives are on the map |
| Endings | **Complete the open rescue**, **Let the open rescue expire**, **Abandon the open rescue** | Each ends it once; the reunion goes ahead; the quest closes; the site goes when you leave |
| A check | **Validate heirloom and rescue state (read only)** | A log of anything inconsistent; it changes nothing |

None of this has been run in the game yet; report anything odd with the validation log.

## When something looks wrong

Run **Log the posts, hall, assembly and trips** and send the log lines (look for `[Wil's Family Life] ... skipped`) with what you saw.
