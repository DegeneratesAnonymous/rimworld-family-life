using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FamilyLife.Domain;
using LudeonTK;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Developer-mode shortcuts for heirlooms, estates and the reunion rescue. Each one only brings forward something the game would do by itself, through the same
    /// code; none of them writes a result into the records. Run from the debug menu only.
    /// </summary>
    internal static class EstateTestTools
    {
        private const string Cat = "Wil's Family Life, testing";
        private static FamilyComponent Comp => FamilyComponent.Current;
        private static int Now => Find.TickManager.TicksGame;
        private static Pawn Selected => Find.Selector.SingleSelectedThing as Pawn;

        private static void Say(string text) => Messages.Message(text, MessageTypeDefOf.TaskCompletion, false);

        // ------------------------------------------------------------------ heirlooms

        [DebugAction(Cat, "Create an heirloom for the selected pawn (or make their best gear one)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void MakeHeirloom()
        {
            var p = Selected; var c = Comp;
            if (p == null || c == null) { Say("Select a colonist first."); return; }
            var r = c.Registry; int id = p.thingIDNumber;
            Thing best = p.equipment?.AllEquipmentListForReading.Concat<Thing>(p.apparel?.WornApparel ?? new List<Apparel>()).Where(t => EstateDriver.TryFacts(c, t, id, out _)).OrderByDescending(t => t.MarketValue).FirstOrDefault();
            if (best == null)
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail("MeleeWeapon_LongSword");
                if (def == null) { Say("No sword definition found."); return; }
                best = ThingMaker.MakeThing(def, ThingDefOf.Steel);
                best.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Masterwork, ArtGenerationContext.Colony);
                p.inventory.innerContainer.TryAdd(best);
            }
            if (!EstateDriver.TryFacts(c, best, id, out var facts)) { Say("That item cannot be a personal possession."); return; }
            r.RecordPersonalItem(best.thingIDNumber, best.def.defName, facts.Label, facts.Kind, id, facts.Value, facts.Quality, Now);
            r.MarkItemMemory(best.thingIDNumber);
            r.DesignateHeirloom(best.thingIDNumber, id, Now);
            Say(p.LabelShort + "'s " + best.LabelNoCount + " is now a recorded heirloom.");
        }

        [DebugAction(Cat, "Name the selected pawn's closest relative as heir to everything they own", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void NameHeir()
        {
            var p = Selected; var c = Comp;
            if (p == null || c == null) { Say("Select a colonist first."); return; }
            int heir = c.Registry.BestBeneficiary(p.thingIDNumber);
            if (heir == 0 || !c.Registry.DesignateBeneficiary(p.thingIDNumber, heir, 0, new[] { heir }, Now)) { Say("They have no living adult relative in the colony to name."); return; }
            Say(p.LabelShort + " has named " + FamilyUi.NameOf(c.Registry, heir) + " (and told them).");
        }

        // ------------------------------------------------------------------ estates

        private static void OpenFor(Pawn owner)
        {
            var c = Comp;
            owner.Kill(null);
            c.Sync(Now, true);
            EstateDriver.Run(c, Now);
        }

        [DebugAction(Cat, "Kill the selected pawn and open their estate", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void KillAndOpen()
        {
            var p = Selected; var c = Comp;
            if (p == null || c == null) { Say("Select a colonist first."); return; }
            int id = p.thingIDNumber;
            OpenFor(p);
            var estate = c.Registry.EstateOf(id);
            Say(estate == null ? "They left nothing recorded and findable; no estate was opened." : "Estate opened with " + estate.Items.Count + " item(s). Stage: " + estate.Stage + ".");
        }

        [DebugAction(Cat, "Force a contested inheritance: the selected pawn dies, with rivals among their kin", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void ForceContest()
        {
            var p = Selected; var c = Comp;
            if (p == null || c == null) { Say("Select a colonist first."); return; }
            var r = c.Registry; int id = p.thingIDNumber;
            var kin = r.GetPerson(id).PartnerIds().Concat(r.ChildrenOf(id)).Distinct().Where(k => r.GetPerson(k)?.InColony == true && !r.GetPerson(k).Juvenile).Take(3).ToList();
            if (kin.Count < 2) { Say("They need at least two grown relatives (partner or children) in the colony."); return; }
            foreach (int k in kin)
            {
                r.ChangeBond(k, id, new BondChange(45, 20, 0, 20), Now);
                foreach (int other in kin.Where(o => o != k)) r.ChangeBond(k, other, new BondChange(0, 0, 60, 0), Now);
            }
            foreach (var item in r.PersonalItemsOf(id).ToList()) r.MarkItemMemory(item.ThingId);
            if (!r.PersonalItemsOf(id).Any()) { Say("They own no recorded item. Create an heirloom for them first."); return; }
            OpenFor(p);
            var estate = r.EstateOf(id);
            Say(estate == null ? "No estate was opened." : "Contested estate opened. Use 'Skip the estate's current wait' to move it along.");
        }

        [DebugAction(Cat, "Skip the open estates' current wait (mourning, claims, discussion, mediation)", allowedGameStates = AllowedGameStates.Playing)]
        private static void SkipWait()
        {
            var c = Comp; if (c == null) return;
            foreach (var e in c.Registry.OpenEstates()) e.StageTick -= 3 * ActivityTuning.TicksPerDay;
            EstateDriver.Run(c, Now);
            Say(string.Join("; ", c.Registry.OpenEstates().Select(e => FamilyUi.NameOf(c.Registry, e.DeceasedId) + ": " + e.Stage)));
        }

        [DebugAction(Cat, "Try the pending heirloom deliveries now", allowedGameStates = AllowedGameStates.Playing)]
        private static void DeliverNow()
        {
            var c = Comp; if (c == null) return;
            foreach (var e in c.Registry.Estates) foreach (var i in e.Items.Where(i => !i.Terminal)) i.NextTryTick = 0;
            EstateDriver.Run(c, Now);
            Say("Deliveries tried. Watch the heir walk to the item; nothing is moved for them.");
        }

        [DebugAction(Cat, "Fail a delivery: destroy the next undelivered heirloom", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DestroyNext()
        {
            var c = Comp; if (c == null) return;
            var item = c.Registry.Estates.SelectMany(e => e.Items).FirstOrDefault(i => !i.Terminal);
            if (item == null) { Say("No undelivered item."); return; }
            var owner = c.Registry.Estates.First(e => e.Items.Contains(item)).DeceasedId;
            var found = EstateDriver.FindThing(c, c.Registry, item.ThingId, owner, item.DefName);
            if (found.Thing != null && !found.Thing.Destroyed) found.Thing.Destroy();
            foreach (var i in c.Registry.Estates.SelectMany(e => e.Items)) i.NextTryTick = 0;
            EstateDriver.Run(c, Now);
            Say(item.Label + " destroyed. State: " + item.Outcome + (item.Outcome == ItemOutcome.Lost ? " (" + item.Lost + ")" : "") + ".");
        }

        // ------------------------------------------------------------------ the rescue

        private static KinVisit OpenRescue(FamilyComponent c) => c.Registry.KinVisits.FirstOrDefault(v => v.RescueOpen);

        [DebugAction(Cat, "Create a reunion rescue quest (strand the selected house's travelling relatives now)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void StrandNow()
        {
            var c = Comp; var p = Selected; if (c == null) return;
            int family = p == null ? 0 : c.Registry.FamilyOf(p.thingIDNumber);
            var visit = c.Registry.KinVisits.FirstOrDefault(v => v.State == KinVisitState.Travelling && (family == 0 || v.FamilyId == family));
            if (visit == null) { Say("No relatives are travelling. Use 'Create a distant relative and call a grand reunion' first."); return; }
            visit.Complication = true; visit.ArriveTick = Now;
            KinVisitDriver.Run(c, Now);
            Say(KinRescueDriver.Describe(visit));
        }

        [DebugAction(Cat, "Choose the trouble for the open rescue", allowedGameStates = AllowedGameStates.Playing)]
        private static void ChooseTrouble()
        {
            var c = Comp; if (c == null) return;
            var visit = OpenRescue(c);
            if (visit == null) { Say("No open rescue."); return; }
            Find.WindowStack.Add(new FloatMenu(Enum.GetValues(typeof(RescueComplication)).Cast<RescueComplication>().Where(t => t != RescueComplication.None).Select(t =>
                new FloatMenuOption(t.ToString(), () =>
                {
                    visit.Trouble = t;
                    foreach (var site in Find.WorldObjects.AllWorldObjects.OfType<WorldObject_KinRescue>().Where(s => s.visitId == visit.Id && !s.HasMap)) site.trouble = t;
                    Say("The trouble is now " + t + (Find.WorldObjects.AllWorldObjects.OfType<WorldObject_KinRescue>().Any(s => s.visitId == visit.Id && s.HasMap) ? " (the map already exists, so it applies on the next visit)." : "."));
                })).ToList()));
        }

        [DebugAction(Cat, "Accept the open rescue quest now", allowedGameStates = AllowedGameStates.Playing)]
        private static void AcceptQuest()
        {
            var c = Comp; if (c == null) return;
            var visit = OpenRescue(c);
            var quest = visit == null ? null : KinRescueDriver.QuestOf(visit);
            if (quest == null || quest.State != QuestState.NotYetAccepted) { Say("There is no offered rescue quest."); return; }
            quest.Accept(null);
            Say(KinRescueDriver.Describe(visit));
        }

        [DebugAction(Cat, "Complete the open rescue", allowedGameStates = AllowedGameStates.Playing)]
        private static void CompleteRescue() => Force("complete");
        [DebugAction(Cat, "Let the open rescue expire", allowedGameStates = AllowedGameStates.Playing)]
        private static void ExpireRescue() => Force("expire");
        [DebugAction(Cat, "Abandon the open rescue", allowedGameStates = AllowedGameStates.Playing)]
        private static void AbandonRescue() => Force("abandon");

        private static void Force(string how)
        {
            var c = Comp; if (c == null) return;
            var visit = OpenRescue(c);
            if (visit == null) { Say("No open rescue."); return; }
            Say(KinRescueDriver.Force(c, visit, how, Now));
        }

        // ------------------------------------------------------------------ looking

        [DebugAction(Cat, "Validate heirloom and rescue state (read only)", allowedGameStates = AllowedGameStates.Playing)]
        private static void Validate()
        {
            var c = Comp; if (c == null) return;
            var r = c.Registry;
            var report = new StringBuilder("[Wil's Family Life] Read-only heirloom and rescue validation at tick " + Now + "\n");
            int problems = 0;
            void Problem(string m) { problems++; report.AppendLine("  " + m); }
            foreach (var g in r.PersonalItems.GroupBy(i => i.ThingId).Where(g => g.Count() > 1)) Problem("Item " + g.Key + " is recorded " + g.Count() + " times.");
            foreach (var i in r.PersonalItems) if (i.OwnerId != 0 && r.GetPerson(i.OwnerId)?.IsAlive != true && !r.Estates.Any(e => e.DeceasedId == i.OwnerId)) Problem("Item " + i.Label + " belongs to someone dead with no estate yet (the next pass opens it).");
            foreach (var g in r.Estates.SelectMany(e => e.Items).Where(i => !i.Terminal).GroupBy(i => i.ThingId).Where(g => g.Count() > 1)) Problem("Item " + g.Key + " is in more than one open estate.");
            foreach (var e in r.Estates)
            {
                foreach (var i in e.Items)
                {
                    if (r.PersonalItemFor(i.ThingId) != null && !i.Terminal) Problem("Estate item " + i.Label + " is still recorded as personal to someone.");
                    if (i.HeirId != 0 && !i.Terminal && r.GetPerson(i.HeirId)?.IsAlive != true) Problem("Estate item " + i.Label + " is assigned to someone who is dead or unknown.");
                    if (i.Outcome == ItemOutcome.Delivered && i.HeirId != 0 && r.PersonalItemFor(i.ThingId)?.OwnerId != i.HeirId) Problem("Delivered item " + i.Label + " is not recorded with its heir (it may have been passed on since).");
                }
                if (e.Open && Now - e.OpenedTick > EstateTuning.CaseLifeTicks * 2) Problem("Estate of " + FamilyUi.NameOf(r, e.DeceasedId) + " has been open far too long.");
            }
            foreach (var v in r.KinVisits.Where(v => v.RescueOpen))
            {
                var quest = KinRescueDriver.QuestOf(v);
                if (quest == null) Problem("Rescue " + v.Id + " is open with no quest (the next hourly pass settles it).");
                if (v.Rescue != RescueStage.Offered && v.SiteId != 0 && !Find.WorldObjects.AllWorldObjects.Any(o => o.ID == v.SiteId)) Problem("Rescue " + v.Id + " has no site (the next pass records it as lost).");
                foreach (int kin in v.KinIds)
                {
                    var pawn = c.TryGetPawn(kin);
                    if (pawn == null) { Problem("Rescue " + v.Id + " lists " + kin + " but no such pawn is known."); continue; }
                    if (pawn.Spawned && Find.WorldPawns.Contains(pawn)) Problem(pawn.LabelShort + " is both on a map and in the world's keeping.");
                }
            }
            foreach (var g in r.KinVisits.Where(v => v.Open).SelectMany(v => v.KinIds.Select(k => (v.Id, k))).GroupBy(x => x.k).Where(g => g.Count() > 1)) Problem("Relative " + g.Key + " is in more than one open visit.");
            report.AppendLine(problems + " potential issue(s). Items=" + r.PersonalItems.Count + ", heirlooms=" + r.PersonalItems.Count(i => i.Heirloom) + ", open estates=" + r.OpenEstates().Count() + ", open rescues=" + r.KinVisits.Count(v => v.RescueOpen));
            report.AppendLine("This is a snapshot of the records only. It does not test pathing, the quest tab, map generation, or save/reload.");
            Log.Message(report.ToString());
        }
    }
}
