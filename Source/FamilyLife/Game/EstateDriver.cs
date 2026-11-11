using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// The game side of personal possessions and inheritance. Discovery is bounded and event-driven: a few colonists are looked at on each hourly pass, and nothing
    /// scans the map's items. A dead person's estate is opened once from the things the game can actually find; an heir is only ever given something that is
    /// physically carried to them. Nothing here removes gear from someone who is alive, and nothing is created, teleported or replaced.
    /// </summary>
    internal static class EstateDriver
    {
        private const int Hour = 2500;
        private const int PawnsPerPass = 3, PruneChecksPerPass = 2, DeliveriesPerPass = 3;
        private static readonly SearchCursor people = new SearchCursor();
        private static readonly Dictionary<int, int> seen = new Dictionary<int, int>();      // item id -> tick it was first noticed in someone's keeping; not saved, so the two days start again after a load

        internal static void Reset() { people.Reset(); seen.Clear(); told.Clear(); }

        internal static EstateContext Context(FamilyComponent c) => new EstateContext { IsAvatar = id => Governance.IsAvatar(c, id) };

        // ------------------------------------------------------------------ what counts

        internal struct ItemFacts { public PersonalItemKind Kind; public float Value; public int Quality; public string Label; }

        /// <summary>Whether a thing can be a personal possession, and what it is. Stacks, quest items, bonded gear and anything the family's own stock claims for someone else are not.</summary>
        internal static bool TryFacts(FamilyComponent c, Thing thing, int owner, out ItemFacts facts)
        {
            facts = default;
            if (thing == null || thing.Destroyed || thing.def == null || thing.def.stackLimit != 1 || thing.def.category != ThingCategory.Item) return false;
            if (thing.questTags != null && thing.questTags.Count > 0) return false;
            if (thing.TryGetComp<CompBiocodable>()?.Biocoded == true) return false;
            var claim = c.Registry.ClaimFor(thing.thingIDNumber);
            if (claim != null && claim.OwnerFamily != c.Registry.FamilyOf(owner)) return false;
            PersonalItemKind kind;
            if (thing.def.IsWeapon) kind = PersonalItemKind.Weapon;
            else if (thing.def.IsApparel) kind = PersonalItemKind.Apparel;
            else if (thing.def.thingClass != null && thing.def.thingClass.Name == "Book") kind = PersonalItemKind.Book;
            else if (thing.TryGetComp<CompArt>() != null) kind = PersonalItemKind.Art;
            else return false;
            int quality = thing.TryGetQuality(out QualityCategory q) ? (int)q : -1;
            facts = new ItemFacts { Kind = kind, Value = thing.MarketValue, Quality = quality, Label = thing.LabelNoCount };
            return true;
        }

        private static IEnumerable<Thing> Carried(Pawn pawn)
        {
            if (pawn.equipment != null) foreach (var t in pawn.equipment.AllEquipmentListForReading) yield return t;
            if (pawn.apparel != null) foreach (var t in pawn.apparel.WornApparel) yield return t;
            if (pawn.inventory != null)
            {
                int seen = 0;
                foreach (var t in pawn.inventory.innerContainer) { if (seen++ >= 24) break; yield return t; }
            }
        }

        // ------------------------------------------------------------------ the hourly pass

        internal static void Run(FamilyComponent c, int now)
        {
            if (FamilyLifeMod.Settings != null && !FamilyLifeMod.Settings.governance) return;
            var r = c.Registry;
            using (c.Performance.Measure("Estates: discovery")) Discover(c, r, now);
            using (c.Performance.Measure("Estates: open and advance"))
            {
                foreach (int owner in r.OwnersAwaitingEstate())
                {
                    var estate = r.OpenEstate(owner, id => FindThing(c, r, id, owner).Exists, now);
                    if (estate == null) continue;
                    Messages.Message("WFL_Estate_Opened".Translate(FamilyUi.NameOf(r, owner), estate.Items.Count), MessageTypeDefOf.NeutralEvent, false);
                    // Worn things stay on the body: the game never strips or buries anyone to hand something down.
                    if (estate.Items.Any(i => FindThing(c, r, i.ThingId, owner, i.DefName).OnCorpse))
                        Messages.Message("WFL_Estate_OnBody".Translate(FamilyUi.NameOf(r, owner)), MessageTypeDefOf.NeutralEvent, false);
                }
                r.AdvanceEstates(now, Context(c));
                ReportMilestones(c, r, now);
            }
            using (c.Performance.Measure("Estates: delivery")) Deliver(c, r, now);
        }

        private static void Discover(FamilyComponent c, FamilyRegistry r, int now)
        {
            var adults = r.Persons.Where(p => p.InColony && !p.Juvenile).OrderBy(p => p.Id).Select(p => p.Id).ToList();
            if (adults.Count == 0) return;
            for (int i = 0; i < Math.Min(PawnsPerPass, adults.Count); i++)
            {
                int id = adults[people.Take(adults.Count)];
                var pawn = c.TryGetPawn(id);
                if (pawn == null || pawn.Dead || pawn.Destroyed) continue;
                foreach (var thing in Carried(pawn))
                {
                    if (!TryFacts(c, thing, id, out var facts)) continue;
                    int tid = thing.thingIDNumber;
                    var known = r.PersonalItemFor(tid);
                    if (known == null)
                    {
                        // Something becomes a possession once it has been kept for a couple of days, not on first sight.
                        if (!seen.TryGetValue(tid, out int first)) { seen[tid] = now; continue; }
                        if (now - first < EstateTuning.PossessionHours * Hour) continue;
                        if (r.RecordPersonalItem(tid, thing.def.defName, facts.Label, facts.Kind, id, facts.Value, facts.Quality, now)) seen.Remove(tid);
                    }
                    else if (known.OwnerId != id) r.TransferPersonalItem(tid, id, now);
                    else r.NoteItemHeld(tid, id, Math.Max(1, (now - known.LastNoteTick) / Hour), now);
                }
                PruneGone(c, r, id, now);
            }
            PruneGone(c, r, 0, now);                                                  // things a house keeps
            r.NameBeneficiariesNow(now, id => Governance.IsAvatar(c, id), 1);
            if (seen.Count > 256) foreach (int stale in seen.Where(kv => now - kv.Value > 10 * ActivityTuning.TicksPerDay).Select(kv => kv.Key).ToList()) seen.Remove(stale);
        }

        /// <summary>A record whose item has vanished (sold, destroyed, taken by another mod) is dropped, a couple per pass, so lists never fill with ghosts.</summary>
        private static void PruneGone(FamilyComponent c, FamilyRegistry r, int owner, int now)
        {
            int checks = 0;
            foreach (var item in r.PersonalItemsOf(owner).Where(i => now - i.LastNoteTick > 30 * ActivityTuning.TicksPerDay).ToList())
            {
                if (checks++ >= PruneChecksPerPass) break;
                if (!FindThing(c, r, item.ThingId, owner, item.DefName).Exists) r.ForgetPersonalItem(item.ThingId);
                else item.LastNoteTick = now;
            }
        }

        /// <summary>Marriages and coming of age are remembered through what the person was wearing or carrying.</summary>
        internal static void OnFamilyEvents(FamilyComponent c, IEnumerable<FamilyEvent> events, int now)
        {
            var r = c.Registry;
            foreach (var e in events)
            {
                if (e.Kind != FamilyEventKind.MarriageFormed && e.Kind != FamilyEventKind.CameOfAge) continue;
                foreach (int id in e.Kind == FamilyEventKind.MarriageFormed ? new[] { e.A, e.B } : new[] { e.A })
                {
                    var pawn = c.TryGetPawn(id);
                    if (pawn == null || pawn.Dead) continue;
                    Thing best = (e.Kind == FamilyEventKind.CameOfAge ? Carried(pawn).Where(t => t.def.IsWeapon) : Carried(pawn).Where(t => t.def.IsApparel))
                        .OrderByDescending(t => t.MarketValue).FirstOrDefault(t => TryFacts(c, t, id, out _));
                    if (best == null || !TryFacts(c, best, id, out var facts)) continue;
                    r.RecordPersonalItem(best.thingIDNumber, best.def.defName, facts.Label, facts.Kind, id, facts.Value, facts.Quality, now);
                    r.MarkItemMemory(best.thingIDNumber);
                }
            }
        }

        // ------------------------------------------------------------------ finding a thing again

        internal struct Finding { public bool Exists; public Thing Thing; public Pawn Holder; public bool OnCorpse, Buried; }

        /// <summary>
        /// Looks for one specific item by its id: on its owner's body or in a grave, worn or carried by a colonist or visitor, or lying on a map. This runs only for
        /// the few items of an open estate (or a pruning check), never for the whole map.
        /// </summary>
        internal static Finding FindThing(FamilyComponent c, FamilyRegistry r, int thingId, int ownerId, string defName = null)
        {
            var result = new Finding();
            foreach (var map in Find.Maps)
            {
                foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
                    if (t is Corpse corpse && corpse.InnerPawn != null && (ownerId == 0 || corpse.InnerPawn.thingIDNumber == ownerId))
                    {
                        var held = Carried(corpse.InnerPawn).FirstOrDefault(x => x.thingIDNumber == thingId);
                        if (held != null) return new Finding { Exists = true, Thing = held, OnCorpse = true };
                    }
                foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Grave))
                    if (t is Building_Grave grave && grave.HasCorpse && grave.Corpse.InnerPawn != null)
                    {
                        var held = Carried(grave.Corpse.InnerPawn).FirstOrDefault(x => x.thingIDNumber == thingId);
                        if (held != null) return new Finding { Exists = true, Thing = held, Buried = true };
                    }
                foreach (var pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (!pawn.RaceProps.Humanlike) continue;
                    var held = Carried(pawn).FirstOrDefault(x => x.thingIDNumber == thingId);
                    if (held != null) return new Finding { Exists = true, Thing = held, Holder = pawn };
                }
                var def = string.IsNullOrEmpty(defName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (def != null)
                    foreach (var t in map.listerThings.ThingsOfDef(def))
                        if (t.thingIDNumber == thingId) return new Finding { Exists = !t.Destroyed, Thing = t };
            }
            return result;
        }

        private static Finding FindThing(FamilyComponent c, FamilyRegistry r, int thingId, int ownerId)
        {
            var known = r.PersonalItemFor(thingId);
            return FindThing(c, r, thingId, ownerId, known?.DefName);
        }

        // ------------------------------------------------------------------ delivery

        private static void Deliver(FamilyComponent c, FamilyRegistry r, int now)
        {
            foreach (var (estate, item) in r.DeliveriesDue(now, DeliveriesPerPass))
            {
                try { DeliverOne(c, r, estate, item, now); }
                catch (Exception ex) { Log.Warning("[Wil's Family Life] Delivery of " + item.Label + " skipped: " + ex.Message); r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.None, false, now); }
            }
        }

        private static void DeliverOne(FamilyComponent c, FamilyRegistry r, EstateCase estate, EstateItem item, int now)
        {
            var found = FindThing(c, r, item.ThingId, estate.DeceasedId, item.DefName);
            if (!found.Exists) { r.ReportItemLost(estate.Id, item.ThingId, LostReason.Missing, now); return; }
            if (found.Buried) { r.ReportItemLost(estate.Id, item.ThingId, LostReason.Buried, now); return; }
            if (found.OnCorpse) { r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.Buried, false, now); return; }     // never stripped from a body here: it waits for the burial or for someone to strip it
            var thing = found.Thing;
            // A person playing the heir collects it themselves, from the estate list. The game never drives their pawn.
            Pawn carrier = item.HeirId != 0 ? c.TryGetPawn(item.HeirId) : HouseCarrier(c, r, item.RecipientHouseId);
            if (carrier == null || !carrier.Spawned || carrier.Dead || carrier.Downed) { r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.None, false, now); return; }
            if (found.Holder != null)
            {
                if (found.Holder == carrier && item.HeirId == carrier.thingIDNumber) { r.CompleteInheritance(estate.Id, item.ThingId, carrier.thingIDNumber, now); return; }
                r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.Held, false, now);                            // someone is wearing or carrying it: it is not taken from them
                return;
            }
            if (Governance.IsAvatar(c, carrier.thingIDNumber) || Find.Selector.IsSelected(carrier)) { r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.None, false, now); return; }      // a pawn the player is playing or has selected is never sent off; try again later without holding up the rest
            if (!thing.Spawned || thing.Map != carrier.Map) { r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.None, false, now); return; }
            if (thing.IsForbidden(carrier)) { r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.Forbidden, false, now); return; }
            if (!carrier.Map.reservationManager.CanReserve(carrier, thing)) { r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.None, false, now); return; }
            if (!Activities.IsFree(carrier) || carrier.CurJob?.playerForced == true || carrier.CurJobDef == WFL_StoryDefOf.WFL_FetchHeirloom) { r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.None, false, now); return; }
            if (!carrier.CanReach(thing, PathEndMode.ClosestTouch, Danger.Some)) { r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.Unreachable, true, now); return; }
            var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_FetchHeirloom, thing);
            job.count = 1;
            if (item.HeirId == 0)
            {
                var drop = HouseDropCell(r, item.RecipientHouseId, carrier.Map);
                if (!drop.IsValid) { r.ReportDeliveryBlocked(estate.Id, item.ThingId, LostReason.None, false, now); return; }
                job.targetB = drop;
            }
            if (!r.BeginDelivery(estate.Id, item.ThingId, now)) return;
            carrier.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
        }

        private static Pawn HouseCarrier(FamilyComponent c, FamilyRegistry r, int house)
        {
            if (house == 0) return null;
            foreach (int id in new[] { r.HeadOf(house) }.Concat((r.GetLineage(house)?.Stewards ?? new List<int>()).Take(4)).Distinct())
            {
                var p = c.TryGetPawn(id);
                if (p != null && p.Spawned && !p.Dead && !p.Downed && !Governance.IsAvatar(c, id)) return p;
            }
            return null;
        }

        private static IntVec3 HouseDropCell(FamilyRegistry r, int house, Map map)
        {
            var area = FamilyZones.AreaFor(r.GetLineage(house));
            if (area == null || area.Map != map) return IntVec3.Invalid;
            int checks = 0;
            foreach (var cell in area.ActiveCells)
            {
                if (checks++ > 400) break;
                if (cell.Standable(map) && cell.GetFirstItem(map) == null && cell.GetEdifice(map) == null) return cell;
            }
            return IntVec3.Invalid;
        }

        /// <summary>The person playing the heir walks over and picks the item up themselves.</summary>
        internal static bool Collect(FamilyComponent c, Pawn heir, EstateItem item)
        {
            var found = FindThing(c, c.Registry, item.ThingId, c.Registry.EstateCaseOfItem(item.ThingId)?.DeceasedId ?? 0, item.DefName);
            if (!found.Exists || found.Holder != null || found.OnCorpse || found.Thing == null || !found.Thing.Spawned || found.Thing.Map != heir.Map) return false;
            if (found.Thing.IsForbidden(heir) || !heir.CanReach(found.Thing, PathEndMode.ClosestTouch, Danger.Some)) return false;
            var estate = c.Registry.EstateCaseOfItem(item.ThingId);
            if (estate == null || !c.Registry.BeginDelivery(estate.Id, item.ThingId, Activities.Now)) return false;
            var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_FetchHeirloom, found.Thing); job.count = 1;
            heir.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            return true;
        }

        // ------------------------------------------------------------------ telling the player, once

        private static readonly HashSet<int> told = new HashSet<int>();

        private static void ReportMilestones(FamilyComponent c, FamilyRegistry r, int now)
        {
            foreach (var estate in r.Estates.Where(e => e.Stage == EstateStage.Resolved && now - e.ClosedTick < 2 * ActivityTuning.TicksPerDay && told.Add(estate0(e))).ToList())
            {
                int got = estate.Items.Count(i => i.Outcome == ItemOutcome.Delivered), lost = estate.Items.Count(i => i.Outcome == ItemOutcome.Lost);
                Messages.Message("WFL_Estate_Settled".Translate(FamilyUi.NameOf(r, estate.DeceasedId), got, lost), MessageTypeDefOf.NeutralEvent, false);
            }
        }

        private static int estate0(EstateCase e) => e.Id;
    }

    /// <summary>
    /// Fetches an item and either keeps it with the heir or sets it down in the house's place. The heir (or the house's carrier) walks to the item, picks it up
    /// and only then is the inheritance recorded. If the job fails for any reason the item stays where it was and a later pass tries again.
    /// </summary>
    public class JobDriver_FetchHeirloom : JobDriver
    {
        private Thing Item => job.GetTarget(TargetIndex.A).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, 1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(TargetIndex.A);
            this.FailOnForbidden(TargetIndex.A);
            this.FailOn(() => pawn.Drafted || pawn.InMentalState);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.ClosestTouch);
            yield return Toils_Haul.StartCarryThing(TargetIndex.A);
            if (job.targetB.IsValid)
            {
                yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
                yield return new Toil
                {
                    defaultCompleteMode = ToilCompleteMode.Instant,
                    initAction = () =>
                    {
                        var carried = pawn.carryTracker.CarriedThing;
                        if (carried == null) return;
                        int id = carried.thingIDNumber;
                        if (pawn.carryTracker.TryDropCarriedThing(job.targetB.Cell, ThingPlaceMode.Near, out Thing dropped))
                        {
                            var c = FamilyComponent.Current; var estate = c?.Registry.EstateCaseOfItem(id);
                            if (estate != null) c.Registry.CompleteInheritance(estate.Id, id, 0, Activities.Now);
                        }
                    }
                };
            }
            else
            {
                yield return new Toil
                {
                    defaultCompleteMode = ToilCompleteMode.Instant,
                    initAction = () =>
                    {
                        var carried = pawn.carryTracker.CarriedThing;
                        if (carried == null) return;
                        int id = carried.thingIDNumber;
                        if (pawn.carryTracker.innerContainer.TryTransferToContainer(carried, pawn.inventory.innerContainer, carried.stackCount) > 0)
                        {
                            var c = FamilyComponent.Current; var estate = c?.Registry.EstateCaseOfItem(id);
                            if (estate != null && c.Registry.CompleteInheritance(estate.Id, id, pawn.thingIDNumber, Activities.Now))
                            {
                                var keepsake = DefDatabase<ThoughtDef>.GetNamedSilentFail("WFL_InheritedKeepsake");
                                if (keepsake != null) pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(keepsake);
                                Messages.Message("WFL_Estate_Received".Translate(pawn.LabelShort, carried.LabelNoCount, FamilyUi.NameOf(c.Registry, estate.DeceasedId)), pawn, MessageTypeDefOf.PositiveEvent, false);
                            }
                        }
                    }
                };
            }
        }
    }
}
