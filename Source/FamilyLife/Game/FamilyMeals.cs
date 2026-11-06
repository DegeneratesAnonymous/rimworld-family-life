using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// Mealtime. At breakfast, midday and evening a family eats together: someone calls "Time to eat!", the relatives who are free and fairly
    /// hungry set off a few moments apart and all sit at one table, in the family's zone if it has one. If a family member happens to get hungry
    /// at one of those times, that starts the call instead. Whoever is busy with something that matters is left to eat when they are ready, and
    /// anyone from the family who sits down to eat while the table is still going is seated with the rest. Outside those hours people eat when
    /// they are hungry, as ever. One mealtime a family every few hours, never for babies, and never while there is nowhere for everyone to
    /// sit. Nothing here changes what anyone eats.
    /// </summary>
    internal static class FamilyMeals
    {
        private sealed class Group
        {
            public int Lineage, Tick, StartTick;
            public bool Started;
            public Dictionary<int, int> StartAt = new Dictionary<int, int>();      // when each person sets off, so they do not all stand up at once
            public HashSet<int> Sent = new HashSet<int>();
            public HashSet<int> Seated = new HashSet<int>();      // sitting at the table, waiting for the rest or eating
            public Map Map;
            public Building Table;
            public HashSet<int> Members = new HashSet<int>();
        }

        private const int GroupLife = 2500, Cooldown = 10000, InviteRadius = 45, MaxGuests = 7, ScanEvery = 250, MaxWait = 1500;
        private const float InviteHunger = 0.65f;        // a relative who is fuller than this is not asked
        private const float CallerHunger = 0.5f;         // someone has to be this hungry for a mealtime to be called at all
        private const float HoldFloor = 0.15f;           // below this nobody waits for the call
        private static readonly List<Group> groups = new List<Group>();
        private static readonly Dictionary<int, int> next = new Dictionary<int, int>();       // family -> earliest tick for the next mealtime
        private static bool inviting;
        private static int lastScan;
        private static readonly HashSet<string> Idle = new HashSet<string> { "Wait_Wander", "GotoWander", "Wait", "Wait_MaintainPosture", "Wait_NoSleep", "Wait_Downed" };

        public static bool Enabled => FamilyLifeMod.Settings == null || FamilyLifeMod.Settings.familyMeals;

        public static void Reset() { groups.Clear(); next.Clear(); inviting = false; lastScan = 0; }

        /// <summary>Breakfast, midday and evening, by the colony's own clock.</summary>
        private static bool MealHour(Map map)
        {
            int hour = GenLocalDate.HourOfDay(map);
            return (hour >= 6 && hour < 9) || (hour >= 11 && hour < 14) || (hour >= 17 && hour < 21);
        }

        private static bool Eats(Pawn p) => p != null && p.Spawned && !p.Dead && p.IsColonist && p.RaceProps.Humanlike && p.needs?.food != null
            && p.DevelopmentalStage != DevelopmentalStage.Baby && p.DevelopmentalStage != DevelopmentalStage.Newborn;

        private static bool Able(Pawn p) => p.Awake() && !p.Downed && !p.Drafted && !p.InMentalState && !GuestPasses.IsActive(p);

        private static bool FreeEnough(Pawn p)
        {
            if (!Able(p)) return false;
            var job = p.CurJob;
            return job == null || Idle.Contains(job.def.defName) || job.def.joyKind != null;
        }

        private static void Prune(int now) => groups.RemoveAll(g => now - g.Tick > GroupLife || g.Table == null || !g.Table.Spawned);

        private static Lineage LineageOf(FamilyRegistry registry, Pawn pawn)
        {
            var record = registry.GetPerson(pawn.thingIDNumber);
            return record == null ? null : registry.GetLineage(registry.LineageIdFor(record));
        }

        // ---- starting a mealtime

        /// <summary>A family member has picked a meal. Returns true if they should wait: a mealtime is being called and they are part of it.</summary>
        public static bool OnMealStarting(Pawn pawn, Job job)
        {
            if (inviting || !Enabled || job == null || job.def != JobDefOf.Ingest || !Eats(pawn)) return false;
            var component = FamilyComponent.Current;
            if (component == null) return false;
            int now = Find.TickManager.TicksGame;
            Prune(now);
            var lineage = LineageOf(component.Registry, pawn);
            if (lineage == null) return false;
            var food = job.GetTarget(TargetIndex.A).Thing;
            if (food?.def?.ingestible != null && food.def.ingestible.preferability < FoodPreferability.MealAwful) return false;       // a snack on the go, not a meal
            bool urgent = pawn.needs.food.CurLevelPercentage < HoldFloor;

            var running = groups.FirstOrDefault(g => g.Lineage == lineage.Id && g.Map == pawn.Map);
            if (running != null)
            {
                if (!running.Members.Contains(pawn.thingIDNumber))
                {
                    if (SeatsAt(running.Table, pawn).Count == 0) return false;
                    running.Members.Add(pawn.thingIDNumber);
                }
                return !running.Started && !urgent;          // still being called: wait for it
            }
            if (!MealHour(pawn.Map) || (next.TryGetValue(lineage.Id, out int until) && now < until)) return false;
            return Form(component, pawn, lineage, now) != null && !urgent;
        }

        /// <summary>Calls a mealtime: asks the free, hungry relatives, picks the table and gets everyone moving a few moments from now.</summary>
        private static Group Form(FamilyComponent component, Pawn caller, Lineage lineage, int now)
        {
            var guests = new List<Pawn>();
            foreach (int id in component.ZoneMemberIds(lineage))
            {
                var other = component.TryGetPawn(id);
                if (other == null || other == caller || other.Map != caller.Map || !Eats(other) || !FreeEnough(other) || other.CurJobDef == JobDefOf.Ingest) continue;
                if (other.needs.food.CurLevelPercentage > InviteHunger || (other.Position - caller.Position).LengthHorizontal > InviteRadius) continue;
                guests.Add(other);
            }
            if (guests.Count == 0) return null;
            guests = guests.OrderBy(g => (g.Position - caller.Position).LengthHorizontalSquared).Take(MaxGuests).ToList();

            var zone = FamilyZones.ZonesOf(lineage).FirstOrDefault(z => z.Map == caller.Map);
            var table = ChooseTable(caller, zone, guests.Count + 1, out int seats);
            if (table == null || seats < 2) return null;
            guests = guests.Take(seats - 1).ToList();

            var group = new Group { Lineage = lineage.Id, Map = caller.Map, Table = table, Tick = now, StartTick = now + 150 + Rand.RangeInclusive(0, 200) };
            group.Members.Add(caller.thingIDNumber);
            group.StartAt[caller.thingIDNumber] = group.StartTick;
            foreach (var g in guests) { group.Members.Add(g.thingIDNumber); group.StartAt[g.thingIDNumber] = group.StartTick + Rand.RangeInclusive(20, 240); }
            groups.Add(group);
            next[lineage.Id] = now + Cooldown;
            MoteMaker.ThrowText(caller.DrawPos, caller.Map, "WFL_MealCall".Translate(), Color.white);
            if (FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.logEvents)
                Log.Message("[Wil's Family Life] Mealtime called for " + component.Registry.LineageDisplayName(lineage) + ": " + group.Members.Count + " at " + table.LabelCap);
            return group;
        }

        /// <summary>Sends each person off to eat when their turn comes, a few moments apart rather than all at once.</summary>
        private static void Start(FamilyComponent component, Group group, int now)
        {
            var giver = new JobGiver_GetFood();
            inviting = true;
            try
            {
                foreach (int id in group.Members.OrderBy(i => i).ToList())
                {
                    if (group.Sent.Contains(id) || now < (group.StartAt.TryGetValue(id, out int at) ? at : group.StartTick)) continue;
                    group.Sent.Add(id);
                    var pawn = component.TryGetPawn(id);
                    if (pawn == null || pawn.Map != group.Map || !Eats(pawn) || !Able(pawn) || pawn.CurJobDef == JobDefOf.Ingest) continue;
                    if (pawn.needs.food.CurLevelPercentage > InviteHunger + 0.1f) continue;            // has eaten since
                    var job = giver.TryIssueJobPackage(pawn, default).Job;
                    if (job == null || job.def != JobDefOf.Ingest) continue;
                    pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, false);
                }
            }
            finally { inviting = false; }
            if (group.Sent.Count >= group.Members.Count) { group.Started = true; group.Tick = now; }
        }

        /// <summary>Every so often: bring a called mealtime to its start, and at mealtimes call one for a family whose members are hungry.</summary>
        public static void Tick(FamilyComponent component, int now)
        {
            if (!Enabled) return;
            foreach (var group in groups.ToList())
                if (!group.Started && now >= group.StartTick) Start(component, group, now);
            if (now - lastScan < ScanEvery) return;
            lastScan = now;
            Prune(now);

            var registry = component.Registry;
            foreach (var lineage in registry.Lineages)
            {
                if (lineage.AbsorbedIntoId != 0 || (next.TryGetValue(lineage.Id, out int until) && now < until) || groups.Any(g => g.Lineage == lineage.Id)) continue;
                Pawn caller = null; Map map = null;
                foreach (int id in component.ZoneMemberIds(lineage))
                {
                    var p = component.TryGetPawn(id);
                    if (!Eats(p) || !FreeEnough(p) || p.needs.food.CurLevelPercentage > CallerHunger || !MealHour(p.Map)) continue;
                    if (caller == null || (p.DevelopmentalStage == DevelopmentalStage.Adult && caller.DevelopmentalStage != DevelopmentalStage.Adult)) { caller = p; map = p.Map; }
                }
                if (caller != null) Form(component, caller, lineage, now);
            }
        }

        // ---- the table

        private static Building TableBeside(IntVec3 cell, Map map)
        {
            for (int i = 0; i < 4; i++)
            {
                var edifice = (cell + GenAdj.CardinalDirections[i]).GetEdifice(map);
                if (edifice != null && edifice.def.surfaceType == SurfaceType.Eat) return edifice;
            }
            return null;
        }

        private static bool UsableSeat(Pawn pawn, Building chair, IntVec3 cell) =>
            chair.def.building != null && chair.def.building.isSittable && !chair.IsForbidden(pawn) && !chair.IsBurning() && chair.IsSociallyProper(pawn)
            && chair.Faction == pawn.Faction && cell.GetDangerFor(pawn, chair.Map) == Danger.None && pawn.CanReserveSittableOrSpot(cell) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some);

        /// <summary>The free seats beside this table that this pawn could take.</summary>
        private static List<IntVec3> SeatsAt(Building table, Pawn pawn)
        {
            var seats = new List<IntVec3>();
            if (table == null || !table.Spawned || table.Map != pawn.Map) return seats;
            var map = table.Map;
            foreach (var cell in table.OccupiedRect().ExpandedBy(1).Cells)
            {
                if (!cell.InBounds(map) || seats.Contains(cell)) continue;
                var chair = cell.GetEdifice(map);
                if (chair == null || chair == table || !UsableSeat(pawn, chair, cell)) continue;
                if (TableBeside(cell, map) != table) continue;
                seats.Add(cell);
            }
            return seats;
        }

        /// <summary>The table for this mealtime: room for as many as are coming, in the family's zone if possible, and near the one who called it.</summary>
        private static Building ChooseTable(Pawn pawn, Zone_Family zone, int wanted, out int seats)
        {
            seats = 0;
            var map = pawn.Map;
            var tables = new HashSet<Building>();
            foreach (var chair in map.listerBuildings.allBuildingsColonist)
            {
                if (chair.def.building == null || !chair.def.building.isSittable || (chair.Position - pawn.Position).LengthHorizontal > 60f) continue;
                foreach (var cell in chair.OccupiedRect().Cells)
                {
                    var table = TableBeside(cell, map);
                    if (table != null) tables.Add(table);
                }
            }
            Building best = null; float bestScore = float.MinValue;
            foreach (var table in tables)
            {
                if (table.IsForbidden(pawn)) continue;
                int count = SeatsAt(table, pawn).Count;
                if (count < 2) continue;
                float score = Mathf.Min(count, wanted) * 1000f
                    + (zone != null && table.OccupiedRect().Cells.Any(zone.ContainsCell) ? 200f : 0f)
                    - (table.Position - pawn.Position).LengthHorizontal;
                if (score > bestScore) { bestScore = score; best = table; seats = count; }
            }
            return best;
        }

        /// <summary>
        /// Asked every tick by someone sitting at the table with food. True while a relative who was called is still on the way (or has not set off
        /// yet), so the meal begins together. Never for long: late or stuck relatives are given up on after <see cref="MaxWait"/> ticks.
        /// </summary>
        public static bool ShouldWaitForTable(Pawn pawn)
        {
            if (groups.Count == 0 || !Enabled || pawn?.Map == null) return false;
            int now = Find.TickManager.TicksGame, me = pawn.thingIDNumber;
            var group = groups.FirstOrDefault(g => g.StartAt.ContainsKey(me) && g.Map == pawn.Map);
            if (group == null) return false;
            group.Seated.Add(me);
            if (now > group.StartTick + MaxWait) return false;
            var component = FamilyComponent.Current;
            if (component == null) return false;
            foreach (int id in group.StartAt.Keys)
            {
                if (id == me || group.Seated.Contains(id)) continue;
                var other = component.TryGetPawn(id);
                if (other == null || other.Map != group.Map || !Eats(other) || !Able(other)) continue;
                if (!group.Sent.Contains(id) || other.CurJobDef == JobDefOf.Ingest) return true;       // still coming
            }
            return false;
        }

        /// <summary>Called when someone looks for somewhere to eat: if they are part of a mealtime, a seat at its table, else leave it to the game.</summary>
        public static bool TrySeat(Pawn pawn, ref IntVec3 cell)
        {
            if (groups.Count == 0 || !Enabled) return false;
            int now = Find.TickManager.TicksGame;
            var group = groups.FirstOrDefault(g => g.Members.Contains(pawn.thingIDNumber) && g.Map == pawn.Map && now - g.Tick <= GroupLife);
            if (group == null) return false;
            var seats = SeatsAt(group.Table, pawn);
            if (seats.Count == 0) return false;
            cell = seats.OrderBy(c => (c - pawn.Position).LengthHorizontalSquared).First();
            return true;
        }
    }

    /// <summary>Whoever has sat down does not start eating until the rest of the family called to the table are seated too.</summary>
    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.ChewIngestible))]
    internal static class Patch_FamilyMealWait
    {
        private static void Postfix(Pawn chewer, Toil __result)
        {
            try
            {
                var inner = __result.tickAction;
                __result.tickAction = () =>
                {
                    inner?.Invoke();
                    if (FamilyMeals.ShouldWaitForTable(chewer)) chewer.jobs.curDriver.ticksLeftThisToil++;       // the chewing clock stands still
                };
            }
            catch (System.Exception ex) { HookSafety.Report("family mealtime wait", ex); }
        }
    }

    /// <summary>A family member who has picked a meal starts, or joins, the family's mealtime; while it is being called they wait for it.</summary>
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    internal static class Patch_FamilyMealStart
    {
        private static void Postfix(Pawn pawn, ref Job __result)
        {
            if (__result == null) return;
            try { if (FamilyMeals.OnMealStarting(pawn, __result)) __result = null; }
            catch (System.Exception ex) { HookSafety.Report("family mealtime", ex); }
        }
    }

    /// <summary>Whoever is at the family's mealtime sits at its table.</summary>
    [HarmonyPatch(typeof(Toils_Ingest), nameof(Toils_Ingest.TryFindChairOrSpot))]
    internal static class Patch_FamilyMealSeat
    {
        private static void Postfix(Pawn pawn, ref IntVec3 cell, ref bool __result)
        {
            try { if (FamilyMeals.TrySeat(pawn, ref cell)) __result = true; }
            catch (System.Exception ex) { HookSafety.Report("family mealtime seat", ex); }
        }
    }
}
