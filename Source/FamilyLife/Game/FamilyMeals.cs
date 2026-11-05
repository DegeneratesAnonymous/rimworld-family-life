using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// Mealtime. When someone in a family goes to eat, the relatives who are free and fairly hungry are asked along, and they all sit at the same
    /// table, in the family's zone if it has one. Whoever is busy with something that matters is left to eat when they are ready, and anyone
    /// from the family who sits down to eat while the table is still going is seated with the rest. One mealtime a family every few hours, never
    /// for babies, and never while there is nowhere for everyone to sit. Nothing here changes what anyone eats.
    /// </summary>
    internal static class FamilyMeals
    {
        private sealed class Group
        {
            public int Lineage, Tick;
            public Map Map;
            public Building Table;
            public HashSet<int> Members = new HashSet<int>();
        }

        private const int GroupLife = 2500, Cooldown = 10000, InviteRadius = 45, MaxGuests = 7;
        private const float InviteHunger = 0.65f;        // a relative who is fuller than this is not asked
        private static readonly List<Group> groups = new List<Group>();
        private static readonly Dictionary<int, int> next = new Dictionary<int, int>();       // family -> earliest tick for the next mealtime
        private static bool inviting;
        private static readonly HashSet<string> Idle = new HashSet<string> { "Wait_Wander", "GotoWander", "Wait", "Wait_MaintainPosture", "Wait_NoSleep", "Wait_Downed" };

        public static bool Enabled => FamilyLifeMod.Settings == null || FamilyLifeMod.Settings.familyMeals;

        public static void Reset() { groups.Clear(); next.Clear(); inviting = false; }

        private static bool Eats(Pawn p) => p != null && p.Spawned && !p.Dead && p.IsColonist && p.RaceProps.Humanlike && p.needs?.food != null
            && p.DevelopmentalStage != DevelopmentalStage.Baby && p.DevelopmentalStage != DevelopmentalStage.Newborn;

        private static bool FreeEnough(Pawn p)
        {
            if (!p.Awake() || p.Downed || p.Drafted || p.InMentalState || GuestPasses.IsActive(p)) return false;
            var job = p.CurJob;
            return job == null || Idle.Contains(job.def.defName) || job.def.joyKind != null;
        }

        private static void Prune(int now) => groups.RemoveAll(g => now - g.Tick > GroupLife || g.Table == null || !g.Table.Spawned);

        /// <summary>A family member is on their way to eat: if no mealtime is going, start one and ask the others.</summary>
        public static void OnMealStarting(Pawn pawn, Job job)
        {
            if (inviting || !Enabled || job == null || job.def != JobDefOf.Ingest || !Eats(pawn)) return;
            var component = FamilyComponent.Current;
            if (component == null) return;
            int now = Find.TickManager.TicksGame;
            Prune(now);
            var registry = component.Registry;
            var record = registry.GetPerson(pawn.thingIDNumber);
            if (record == null) return;
            var lineage = registry.GetLineage(registry.LineageIdFor(record));
            if (lineage == null) return;
            var food = job.GetTarget(TargetIndex.A).Thing;
            if (food?.def?.ingestible != null && food.def.ingestible.preferability < FoodPreferability.MealAwful) return;       // a snack on the go, not a meal

            var running = groups.FirstOrDefault(g => g.Lineage == lineage.Id && g.Map == pawn.Map);
            if (running != null)
            {
                if (!running.Members.Contains(pawn.thingIDNumber) && SeatsAt(running.Table, pawn).Count > 0) running.Members.Add(pawn.thingIDNumber);
                return;
            }
            if (next.TryGetValue(lineage.Id, out int until) && now < until) return;
            next[lineage.Id] = now + Cooldown;

            // Who is free and hungry enough, nearest first.
            var guests = new List<Pawn>();
            foreach (int id in component.ZoneMemberIds(lineage))
            {
                var other = component.TryGetPawn(id);
                if (other == null || other == pawn || other.Map != pawn.Map || !Eats(other) || !FreeEnough(other)) continue;
                if (other.needs.food.CurLevelPercentage > InviteHunger || (other.Position - pawn.Position).LengthHorizontal > InviteRadius) continue;
                if (other.CurJobDef == JobDefOf.Ingest) continue;
                guests.Add(other);
            }
            if (guests.Count == 0) return;
            guests = guests.OrderBy(g => (g.Position - pawn.Position).LengthHorizontalSquared).Take(MaxGuests).ToList();

            var zone = FamilyZones.ZonesOf(lineage).FirstOrDefault(z => z.Map == pawn.Map);
            var table = ChooseTable(pawn, zone, guests.Count + 1, out int seats);
            if (table == null || seats < 2) return;
            guests = guests.Take(seats - 1).ToList();

            var group = new Group { Lineage = lineage.Id, Map = pawn.Map, Table = table, Tick = now };
            group.Members.Add(pawn.thingIDNumber);
            groups.Add(group);
            var giver = new JobGiver_GetFood();
            inviting = true;
            try
            {
                foreach (var guest in guests)
                {
                    var invite = giver.TryIssueJobPackage(guest, default).Job;
                    if (invite == null || invite.def != JobDefOf.Ingest) continue;
                    group.Members.Add(guest.thingIDNumber);
                    guest.jobs.StartJob(invite, JobCondition.InterruptForced, null, false);
                }
            }
            finally { inviting = false; }
            if (FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.logEvents)
                Log.Message("[Wil's Family Life] Mealtime for " + registry.LineageDisplayName(lineage) + ": " + group.Members.Count + " at " + table.LabelCap);
        }

        private static IEnumerable<IntVec3> SittingCells(Thing chair) => chair.OccupiedRect().Cells;

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
            && (chair.Faction == pawn.Faction) && cell.GetDangerFor(pawn, chair.Map) == Danger.None && pawn.CanReserveSittableOrSpot(cell) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Some);

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

        /// <summary>The table for this mealtime: room for as many as are coming, in the family's zone if possible, and near the one who is eating.</summary>
        private static Building ChooseTable(Pawn pawn, Zone_Family zone, int wanted, out int seats)
        {
            seats = 0;
            var map = pawn.Map;
            var tables = new Dictionary<Building, int>();
            foreach (var chair in map.listerBuildings.allBuildingsColonist)
            {
                if (chair.def.building == null || !chair.def.building.isSittable || (chair.Position - pawn.Position).LengthHorizontal > 60f) continue;
                foreach (var cell in SittingCells(chair))
                {
                    var table = TableBeside(cell, map);
                    if (table == null || !pawn.CanReserveSittableOrSpot(cell)) continue;
                    tables[table] = (tables.TryGetValue(table, out int n) ? n : 0) + 1;
                }
            }
            Building best = null; float bestScore = float.MinValue;
            foreach (var pair in tables)
            {
                if (pair.Key.IsForbidden(pawn)) continue;
                int count = SeatsAt(pair.Key, pawn).Count;
                if (count < 2) continue;
                float score = System.Math.Min(count, wanted) * 1000f
                    + (zone != null && pair.Key.OccupiedRect().Cells.Any(zone.ContainsCell) ? 200f : 0f)
                    - (pair.Key.Position - pawn.Position).LengthHorizontal;
                if (score > bestScore) { bestScore = score; best = pair.Key; seats = count; }
            }
            return best;
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

    /// <summary>A family member who has picked a meal starts, or joins, the family's mealtime.</summary>
    [HarmonyPatch(typeof(JobGiver_GetFood), "TryGiveJob")]
    internal static class Patch_FamilyMealStart
    {
        private static void Postfix(Pawn pawn, Job __result)
        {
            if (__result == null) return;
            try { FamilyMeals.OnMealStarting(pawn, __result); }
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
