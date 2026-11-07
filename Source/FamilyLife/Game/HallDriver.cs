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
    /// The hall keeper's work. Every few days the keeper looks after the hall (the room with the most places to sit at a table): sweeps it if it is
    /// dirty, then those who come sit down and wait, and the keeper brings each of them a meal from the colony's stores and sets it before them. If the
    /// keeper is not available, nothing starts and nobody waits; anyone already seated gives up after a while and eats as they always would.
    /// </summary>
    internal static class HallDriver
    {
        private const int Day = ActivityTuning.TicksPerDay;
        private sealed class Cleaning { public int Before, Started; }
        private static readonly Dictionary<int, Cleaning> cleaning = new Dictionary<int, Cleaning>();

        internal sealed class Seat { public Building Chair; public IntVec3 Cell, TableCell; }

        private static bool IsTable(Building b) => b != null && b.def.surfaceType == SurfaceType.Eat;

        private static readonly string[] SchoolWords = { "school", "desk", "class", "lecture", "lectern", "blackboard", "study", "student", "pupil", "library" };

        /// <summary>Furniture and rooms that belong to a school, a library or a study, not a dining hall. Judged by name, because mods name them so.</summary>
        internal static bool LooksLikeSchool(ThingDef def)
        {
            string n = ((def?.defName ?? "") + " " + (def?.label ?? "")).ToLowerInvariant();
            foreach (var w in SchoolWords) if (n.Contains(w)) return true;
            return false;
        }

        private static bool RoomIsSchool(Room room) => room.ContainedAndAdjacentThings.Any(t => t is Building && LooksLikeSchool(t.def));

        /// <summary>The places to sit in the best hall on this map: seats in one room, each beside a table, that this pawn could take.</summary>
        internal static List<Seat> HallSeats(Map map, Pawn who, out Room room)
        {
            room = null;
            var named = FamilyComponent.Current?.Registry;
            Room wanted = null;
            if (named != null && named.HallNamed)
            {
                // The player named the hall: it is that room, and nothing else is ever chosen in its place.
                var cell = new IntVec3(named.HallX, 0, named.HallZ);
                if (named.HallMapId != map.uniqueID || !cell.InBounds(map)) return new List<Seat>();
                wanted = cell.GetRoom(map);
                if (wanted == null || wanted.PsychologicallyOutdoors) return new List<Seat>();
            }
            var byRoom = new Dictionary<Room, List<Seat>>();
            foreach (var chair in map.listerBuildings.allBuildingsColonist)
            {
                if (chair.def.building == null || !chair.def.building.isSittable || chair.Destroyed || chair.IsForbidden(who) || chair.IsBurning()) continue;
                var r = chair.GetRoom();
                if (r == null || r.PsychologicallyOutdoors || r.IsHuge) continue;
                if (wanted != null) { if (r != wanted) continue; }
                else if (r.Role?.defName != "DiningRoom" || LooksLikeSchool(chair.def) || RoomIsSchool(r)) continue;      // on its own the game only picks a real dining room, never a classroom
                IntVec3 tableCell = IntVec3.Invalid;
                for (int i = 0; i < 4 && !tableCell.IsValid; i++)
                {
                    var c = chair.Position + GenAdj.CardinalDirections[i];
                    if (c.InBounds(map) && IsTable(c.GetEdifice(map))) tableCell = c;
                }
                if (!tableCell.IsValid) continue;
                if (!byRoom.TryGetValue(r, out var list)) byRoom[r] = list = new List<Seat>();
                list.Add(new Seat { Chair = chair, Cell = chair.Position, TableCell = tableCell });
            }
            var best = byRoom.OrderByDescending(kv => kv.Value.Count).FirstOrDefault();
            if (best.Key == null || best.Value.Count < 2) return new List<Seat>();
            room = best.Key;
            return best.Value;
        }

        private static IEnumerable<Filth> FilthIn(Room room) => room == null ? Enumerable.Empty<Filth>() : room.ContainedAndAdjacentThings.OfType<Filth>().Where(f => f.Spawned);

        private static bool Eats(Pawn p) => p != null && p.Spawned && !p.Dead && p.IsColonist && p.RaceProps.Humanlike && p.needs?.food != null
            && p.DevelopmentalStage != DevelopmentalStage.Baby && p.DevelopmentalStage != DevelopmentalStage.Newborn;

        internal static void Work(FamilyComponent c, FamilyRegistry r, CommunityOffice office, Pawn keeper, FamilySettings s, int now, Func<string, int, int, bool> due)
        {
            if (keeper == null || keeper.Map == null) return;
            int keeperId = keeper.thingIDNumber;
            var map = keeper.Map;
            var seats = HallSeats(map, keeper, out var room);
            var diners = map.mapPawns.FreeColonistsSpawned.Where(p => p != keeper && Eats(p)).Take(80).ToList();
            bool hungry = diners.Any(p => p.needs.food.CurLevelPercentage < .8f);
            r.NoteOfficeNeed(OfficeKind.HallKeeper, seats.Count >= 2 && hungry, now);
            if (s != null && !s.holdHallMeals) return;

            // Sweeping first: credit only if the room really has less dirt afterwards.
            if (cleaning.TryGetValue(keeperId, out var clean))
            {
                bool busy = keeper.CurJobDef == JobDefOf.Clean && now - clean.Started < 6000;
                if (busy) return;
                cleaning.Remove(keeperId);
                int after = FilthIn(room).Count();
                if (clean.Before > after)
                    r.RecordOfficeWork(OfficeKind.HallKeeper, keeperId, GovernanceMeetings.NextToken(), now, .45f + .5f * Math.Min(1f, (clean.Before - after) / (float)clean.Before));
            }

            if (now >= office.NextSessionTick && office.SessionQueue.Count == 0)
            {
                if (seats.Count < 2 || !Activities.IsFree(keeper) || !Governance.Free(c, keeperId)) return;
                var dirt = FilthIn(room).Take(5).ToList();
                if (dirt.Count > 0 && !keeper.WorkTypeIsDisabled(WorkTypeDefOf.Cleaning) && due("sweep", now, 3000))
                {
                    var job = JobMaker.MakeJob(JobDefOf.Clean);
                    foreach (var f in dirt) job.AddQueuedTarget(TargetIndex.A, f);
                    cleaning[keeperId] = new Cleaning { Before = FilthIn(room).Count(), Started = now };
                    keeper.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                    return;
                }
                var guests = diners.Where(p => p.needs.food.CurLevelPercentage < .8f && Governance.Free(c, p.thingIDNumber) && Activities.IsFree(p)).OrderBy(p => p.needs.food.CurLevelPercentage).Take(seats.Count).ToList();
                int every = Math.Max(1, Math.Min(10, s?.hallMealEveryDays ?? 3)) * Day;
                if (guests.Count > 0 && r.BeginHallMeal(now, every, guests.Select(p => p.thingIDNumber)))
                {
                    SeatGuests(r, office, guests, seats);
                    Messages.Message("WFL_Hall_Begins".Translate(keeper.LabelShort), keeper, MessageTypeDefOf.NeutralEvent, false);
                }
                return;
            }

            int next = r.NextDiner(now);
            if (next == 0) return;
            if (keeper.CurJobDef == WFL_StoryDefOf.WFL_HallServe) return;                                   // already doing the round
            var diner = c.TryGetPawn(next);
            if (diner == null || !diner.Spawned || diner.Map != map || diner.CurJobDef != WFL_StoryDefOf.WFL_HallSit) { r.DinerServed(next); return; }      // gave up waiting, or went elsewhere
            if (!Activities.IsFree(keeper) || !Governance.Free(c, keeperId)) return;
            var round = JobMaker.MakeJob(WFL_StoryDefOf.WFL_HallServe);
            keeper.jobs.StartJob(round, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
        }

        private static void SeatGuests(FamilyRegistry r, CommunityOffice office, List<Pawn> guests, List<Seat> seats)
        {
            var free = seats.ToList();
            foreach (var guest in guests)
            {
                var seat = free.Where(sx => guest.CanReserveSittableOrSpot(sx.Cell) && guest.CanReach(sx.Cell, PathEndMode.OnCell, Danger.Some)).OrderBy(sx => (sx.Cell - guest.Position).LengthHorizontalSquared).FirstOrDefault();
                if (seat == null) { r.DinerServed(guest.thingIDNumber); continue; }
                free.Remove(seat);
                var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_HallSit, seat.Cell, seat.TableCell);
                guest.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            }
        }

        /// <summary>The best plain meal in the colony's stores the keeper can fetch and this person will eat. Nutrient paste dispensers are not used.</summary>
        internal static Thing BestMeal(Pawn keeper, Pawn diner)
        {
            Thing best = null; int bestRank = -1; float bestDist = float.MaxValue;
            foreach (var t in keeper.Map.listerThings.ThingsInGroup(ThingRequestGroup.FoodSourceNotPlantOrTree))
            {
                var ing = t.def.ingestible;
                if (ing == null || t is Building || t.IsForbidden(keeper) || ing.preferability < FoodPreferability.MealSimple || !t.Spawned) continue;
                if (!FoodUtility.WillEat(diner, t, keeper) || !keeper.CanReserveAndReach(t, PathEndMode.OnCell, Danger.Some)) continue;
                int rank = (int)ing.preferability;
                float dist = (t.Position - keeper.Position).LengthHorizontalSquared;
                if (rank > bestRank || rank == bestRank && dist < bestDist) { best = t; bestRank = rank; bestDist = dist; }
            }
            return best;
        }

        internal static float MealQuality(Thing meal, Pawn diner)
        {
            var pref = meal?.def.ingestible?.preferability ?? FoodPreferability.Undefined;
            float q = pref >= FoodPreferability.MealLavish ? 1f : pref >= FoodPreferability.MealFine ? .75f : .55f;
            return Math.Min(1f, q + (diner.needs.food.CurLevelPercentage < .4f ? .1f : 0f));
        }
    }

    /// <summary>A person who has come to the hall: goes to their seat and waits for the keeper, for a while, then gives up and carries on.</summary>
    public class JobDriver_HallSit : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.ReserveSittableOrSpot(job.targetA.Cell, job, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Drafted || pawn.InMentalState);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            var wait = Toils_General.Wait(7500);
            wait.socialMode = RandomSocialMode.Off;
            yield return wait;
        }
    }

    /// <summary>
    /// The keeper's round: for each person waiting in the hall, fetch a meal from the stores, carry it over and set it on the table before them, then
    /// the next, until the queue is empty. Someone who has gone, or a meal that cannot be had, ends the round; the keeper starts another later.
    /// </summary>
    public class JobDriver_HallServe : JobDriver
    {
        private Pawn Diner => job.GetTarget(TargetIndex.A).Thing as Pawn;
        private Thing Meal => job.GetTarget(TargetIndex.B).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var pick = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    var c = FamilyComponent.Current;
                    if (c == null) { EndJobWith(JobCondition.Incompletable); return; }
                    // Skip anyone who has left their seat; end the round when nobody is left or no meal can be had.
                    for (int guard = 0; guard < 12; guard++)
                    {
                        int id = c.Registry.NextDiner(Activities.Now);
                        if (id == 0) { EndJobWith(JobCondition.Succeeded); return; }
                        var diner = c.TryGetPawn(id);
                        if (diner == null || !diner.Spawned || diner.Map != pawn.Map || diner.CurJobDef != WFL_StoryDefOf.WFL_HallSit) { c.Registry.DinerServed(id); continue; }
                        var meal = HallDriver.BestMeal(pawn, diner);
                        if (meal == null || !pawn.Reserve(meal, job, 1, 1)) { EndJobWith(JobCondition.Incompletable); return; }
                        job.SetTarget(TargetIndex.A, diner); job.SetTarget(TargetIndex.B, meal); job.count = 1;
                        return;
                    }
                    EndJobWith(JobCondition.Incompletable);
                }
            };
            yield return pick;
            yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch).FailOnDespawnedNullOrForbidden(TargetIndex.B);
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () => { if (pawn.carryTracker.TryStartCarry(Meal, 1) <= 0) EndJobWith(JobCondition.Incompletable); }
            };
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch).FailOnDespawnedOrNull(TargetIndex.A);
            var hand = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    var diner = Diner; var carried = pawn.carryTracker.CarriedThing; var c = FamilyComponent.Current;
                    var sit = diner?.CurJob;
                    if (diner == null || carried == null || c == null || sit == null || diner.CurJobDef != WFL_StoryDefOf.WFL_HallSit)
                    {
                        if (diner != null && c != null) c.Registry.DinerServed(diner.thingIDNumber);         // they are gone; do not wait on them again
                        pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
                        return;
                    }
                    IntVec3 table = sit.targetB.IsValid ? sit.targetB.Cell : diner.Position;
                    if (!pawn.carryTracker.TryDropCarriedThing(table, ThingPlaceMode.Near, out Thing placed) || placed == null) { EndJobWith(JobCondition.Incompletable); return; }
                    pawn.Map.reservationManager.ReleaseAllClaimedBy(pawn);                                    // the meal is no longer ours, so the diner can take it
                    var eat = JobMaker.MakeJob(JobDefOf.Ingest, placed);
                    eat.count = FoodUtility.WillIngestStackCountOf(diner, placed.def, FoodUtility.GetNutrition(diner, placed, placed.def));
                    diner.jobs.StartJob(eat, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                    c.Registry.DinerServed(diner.thingIDNumber);
                    OfficeDriver.Served(c.Registry, OfficeKind.HallKeeper, pawn, diner, Activities.Now);
                    c.Registry.RecordOfficeWork(OfficeKind.HallKeeper, pawn.thingIDNumber, GovernanceMeetings.NextToken(), Activities.Now, HallDriver.MealQuality(placed, diner));
                }
            };
            yield return hand;
            yield return Toils_Jump.Jump(pick);
        }
    }
}
