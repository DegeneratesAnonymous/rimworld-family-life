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
    /// <summary>One visit: an outsider waits at the edge of a family zone until a member of that family comes and lets them in.</summary>
    internal sealed class GuestPass
    {
        public Pawn Guest, Opener;
        public int Lineage;
        public Zone_Family Zone;
        public Area OriginalArea;                 // the guest's own restriction, put back when the visit ends
        public IntVec3 Inside, Outside;
        public int Start, GrantedTick, LeftSince = -1;
        public int ArrivedTick = -1, LastKnock = -1000000, LastSummon = -1000000;
        public HashSet<int> Tried = new HashSet<int>();      // people already called who did not come
        public bool Granted, Entered;
    }

    /// <summary>
    /// Family zones keep each family to its own area, which also keeps an outside builder out of someone else's zone, and the game never even
    /// offers them the work. This is the door. Every half second the construction and repair jobs inside family zones are checked; when an
    /// idle colonist from another family could do one, an adult of the zone's family (or, for a child guest, another child) walks to the
    /// edge and lets them in. The guest waits outside until then, does the work, and the visit ends when they leave the zone.
    /// While visiting, the guest's own area restriction is lifted and then put back. Visits are not saved; the restriction is.
    /// </summary>
    internal static class GuestPasses
    {
        public const int WaitLimit = 2500, PassLimit = 7500, NeverEnteredLimit = 1800, LeaveDelay = 240, CooldownAfterRefusal = 2500, CooldownAfterVisit = 600, CooldownNeverEntered = 5000, WalkLimit = 6000, KnockEvery = 180, SummonEvery = 120;
        public static readonly Dictionary<int, GuestPass> ByGuest = new Dictionary<int, GuestPass>();
        private static readonly Dictionary<int, int> cooldown = new Dictionary<int, int>();
        private static readonly HashSet<string> IdleJobs = new HashSet<string> { "Wait_Wander", "GotoWander", "Wait", "Wait_MaintainPosture", "Wait_NoSleep" };

        private sealed class WorkSite
        {
            public IntVec3 Cell;
            public Thing Thing;
            public bool Repair;
            public bool IsCurrent(Map map)
            {
                if (Thing != null) return Thing.Spawned && Thing.Map == map && Thing.Position == Cell && Thing.Faction == Faction.OfPlayer &&
                    (Repair ? Thing.HitPoints < Thing.MaxHitPoints : Thing is Blueprint || Thing is Frame);
                return RoofWork(map, Cell);
            }
        }
        private static bool RoofWork(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return false;
            bool roofed = map.roofGrid.Roofed(cell);
            return (!roofed && map.areaManager.BuildRoof[cell] && RoofCollapseUtility.WithinRangeOfRoofHolder(cell, map)) ||
                   (roofed && map.areaManager.NoRoof[cell]);
        }
        private sealed class MapWork
        {
            public readonly TickGate Refresh = new TickGate(300, 60);
            public readonly List<WorkSite> Sites = new List<WorkSite>();
            public readonly SearchCursor Cursor = new SearchCursor();
        }
        // Map objects scope caches to the actual map, never to map-local area IDs.
        private static readonly Dictionary<Map, MapWork> work = new Dictionary<Map, MapWork>();
        private static readonly SearchCursor mapCursor = new SearchCursor();
        private static readonly TickGate pruneWork = new TickGate(2500);
        public static void Invalidate(Map map) { if (map != null && work.TryGetValue(map, out var cached)) cached.Refresh.Invalidate(); }

        internal static void WorkChanged(Map map, IntVec3 cell)
        {
            if (map != null && work.ContainsKey(map) && cell.InBounds(map) && map.zoneManager.ZoneAt(cell) is Zone_Family)
                Invalidate(map);
        }

        public static bool IsActive(Pawn pawn) => pawn != null && ByGuest.ContainsKey(pawn.thingIDNumber);
        public static bool IsGranted(Pawn pawn) => pawn != null && ByGuest.TryGetValue(pawn.thingIDNumber, out var p) && p.Granted;

        private static void Trace(string text)
        {
            if (FamilyLifeMod.Settings != null && FamilyLifeMod.Settings.logEvents) Log.Message("[Wil's Family Life] doors: " + text);
        }

        public static bool IsFamilyMember(FamilyRegistry registry, Lineage lineage, Pawn pawn)
        {
            var person = registry.GetPerson(pawn.thingIDNumber);
            return person != null && (person.PrimaryLineageId == lineage.Id || lineage.HasAffiliate(person.Id));
        }

        /// <summary>An adult can let anyone in. A child can only let another child in.</summary>
        public static bool CanOpenFor(Pawn opener, Pawn guest)
        {
            var stage = opener.DevelopmentalStage;
            if (stage == DevelopmentalStage.Adult) return true;
            return stage == DevelopmentalStage.Child && guest.DevelopmentalStage.Juvenile();
        }

        private static bool Available(Pawn p) => p != null && p.Spawned && !p.Dead && !p.Downed && !p.Drafted && p.Awake() && !p.InMentalState
            && p.health.capacities.CapableOf(PawnCapacityDefOf.Moving);

        private static bool IdleEnough(Pawn p) => p.CurJob == null || IdleJobs.Contains(p.CurJobDef.defName);

        private static bool CanDoSiteWork(Pawn p, bool repair)
        {
            if (p.workSettings == null) return false;
            if (WorkAllowed(p, WorkTypeDefOf.Construction)) return true;
            if (repair)
            {
                var repairType = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Repair");
                if (repairType != null && WorkAllowed(p, repairType)) return true;
            }
            return false;
        }

        private static bool WorkAllowed(Pawn p, WorkTypeDef def) => !p.WorkTypeIsDisabled(def) && p.workSettings.WorkIsActive(def);

        public static void Process(FamilyComponent component)
        {
            int now = Find.TickManager.TicksGame;
            if (ByGuest.Count != 0)
                foreach (var pass in ByGuest.Values.ToList()) Update(pass, now);
            if (pruneWork.Take(now))
                foreach (var gone in work.Keys.Where(m => !Find.Maps.Contains(m)).ToList()) work.Remove(gone);
            if (ByGuest.Count >= 3 || Find.Maps.Count == 0) return;
            // One map per pass; active visits above are always maintained regardless of discovery budget.
            var map = Find.Maps[mapCursor.Take(Find.Maps.Count)];
            if (!map.zoneManager.AllZones.Any(z => z is Zone_Family f && f.areaId != 0)) { work.Remove(map); return; }
            using (component.Performance.Measure("Zone work discovery")) SeekWork(component, map, now);
        }

        private static void RefreshWork(Map map, MapWork cached)
        {
            cached.Sites.Clear();
            void Add(Thing thing, bool repair)
            {
                if (thing.Faction == Faction.OfPlayer && map.zoneManager.ZoneAt(thing.Position) is Zone_Family z && z.areaId != 0)
                    cached.Sites.Add(new WorkSite { Cell = thing.Position, Thing = thing, Repair = repair });
            }
            foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)) Add(t, false);
            foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)) Add(t, false);
            foreach (var t in map.listerBuildingsRepairable.RepairableBuildings(Faction.OfPlayer)) Add(t, true);
            foreach (var zone in map.zoneManager.AllZones.OfType<Zone_Family>())
            {
                if (zone.areaId == 0) continue;
                foreach (var cell in zone.Cells)
                {
                    if (!RoofWork(map, cell)) continue;
                    cached.Sites.Add(new WorkSite { Cell = cell });
                    break; // one roof task per zone is enough to arrange access
                }
            }
        }

        /// <summary>Cached work refreshes every 300 ticks. Try at most four rotating sites per pass, revalidating before use.</summary>
        private static void SeekWork(FamilyComponent component, Map map, int now)
        {
            if (!work.TryGetValue(map, out var cached)) work[map] = cached = new MapWork();
            if (cached.Refresh.Take(now))
                using (component.Performance.Measure("Zone work refresh")) RefreshWork(map, cached);
            if (cached.Sites.Count == 0) return;
            var workers = map.mapPawns.FreeColonistsSpawned.Where(p => !ByGuest.ContainsKey(p.thingIDNumber) && Available(p) && IdleEnough(p)).ToList();
            if (workers.Count == 0) return;
            var registry = component.Registry;
            var busyZones = new HashSet<Zone_Family>(ByGuest.Values.Select(p => p.Zone));
            int start = cached.Cursor.Take(cached.Sites.Count);
            for (int offset = 0; offset < System.Math.Min(4, cached.Sites.Count); offset++)
            {
                int index = (start + offset) % cached.Sites.Count;
                cached.Cursor.ResumeAfter(index, cached.Sites.Count);
                var site = cached.Sites[index];
                if (!site.IsCurrent(map) || !(map.zoneManager.ZoneAt(site.Cell) is Zone_Family zone) || zone.areaId == 0 || busyZones.Contains(zone)) continue;
                var lineage = zone.Family(registry);
                if (lineage == null) continue;
                Pawn guest = null; float bestDist = float.MaxValue;
                foreach (var pawn in workers)
                {
                    if (ByGuest.ContainsKey(pawn.thingIDNumber) || !CanDoSiteWork(pawn, site.Repair)) continue;
                    var restriction = pawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
                    if (restriction == null || restriction.TrueCount == 0 || restriction[site.Cell]) continue;
                    if (cooldown.TryGetValue(pawn.thingIDNumber, out int until) && now < until) continue;
                    if (IsFamilyMember(registry, lineage, pawn)) continue;
                    float d = pawn.Position.DistanceToSquared(site.Cell);
                    if (d < bestDist) { bestDist = d; guest = pawn; }
                }
                if (guest != null && Begin(registry, component, guest, zone, lineage, now)) busyZones.Add(zone);
                if (ByGuest.Count >= 3) return;
            }
        }

        private static bool Begin(FamilyRegistry registry, FamilyComponent component, Pawn guest, Zone_Family zone, Lineage lineage, int now)
        {
            if (ByGuest.Count >= 3) return false;
            cooldown[guest.thingIDNumber] = now + CooldownAfterRefusal;                  // replaced by a shorter one if the visit goes ahead and ends well
            if (!FindDoor(zone, guest, out IntVec3 inside, out IntVec3 outside)) { Trace(guest.LabelShort + ": no edge cell found for " + zone.label); return false; }
            var pass = new GuestPass { Guest = guest, Zone = zone, Inside = inside, Outside = outside, Start = now, OriginalArea = guest.playerSettings.AreaRestrictionInPawnCurrentMap, Lineage = lineage.Id };
            ByGuest[guest.thingIDNumber] = pass;
            guest.playerSettings.AreaRestrictionInPawnCurrentMap = null;                 // so the guest can walk to the door
            if (!guest.CanReach(outside, PathEndMode.OnCell, Danger.Deadly)) { Trace(guest.LabelShort + " cannot reach the edge of " + zone.label); End(pass, CooldownAfterRefusal); return false; }
            Trace(guest.LabelShort + " goes to knock at " + zone.label);
            guest.jobs.StartJob(JobMaker.MakeJob(FamilyJobDefOf.WFL_WaitAtDoor, outside), JobCondition.InterruptForced, null, false);
            return true;
        }

        /// <summary>
        /// Calls someone in the family to the door: the nearest adult who is awake and able (a child, for a child). Whatever they are
        /// doing, they stop and come. If the one called does not come, the next knock calls someone else.
        /// </summary>
        private static void Summon(GuestPass pass, FamilyComponent component, int now)
        {
            pass.LastSummon = now;
            var registry = component.Registry;
            var lineage = registry.GetLineage(pass.Lineage);
            if (lineage == null) return;
            var guest = pass.Guest;
            Pawn best = null;
            for (int round = 0; round < 2 && best == null; round++)
            {
                var candidates = registry.MembersOf(lineage.Id).Concat(registry.AffiliatesOf(lineage.Id)).Distinct()
                    .Select(person => component.TryGetPawn(person.Id))
                    .Where(candidate => candidate != null && candidate != guest && Available(candidate) && candidate.Map == guest.Map && CanOpenFor(candidate, guest))
                    .Where(candidate => round != 0 || !pass.Tried.Contains(candidate.thingIDNumber))
                    .OrderBy(candidate => candidate.Position.DistanceTo(pass.Inside) + (candidate.DevelopmentalStage == DevelopmentalStage.Adult ? 0f : 40f));
                // Cheap distance ranking first: stop at the nearest reachable opener rather than path-checking every relative.
                foreach (var candidate in candidates)
                    if (candidate.CanReach(pass.Inside, PathEndMode.OnCell, Danger.Deadly)) { best = candidate; break; }
                if (best == null) pass.Tried.Clear();                                    // everyone has been tried: start over
            }
            if (best == null) { Trace(guest.LabelShort + " knocks but nobody in the family is able to come to " + pass.Zone.label); return; }
            pass.Opener = best;
            pass.Tried.Add(best.thingIDNumber);
            Trace(best.LabelShort + " is called to the door of " + pass.Zone.label + " for " + guest.LabelShort);
            best.jobs.StartJob(JobMaker.MakeJob(FamilyJobDefOf.WFL_LetGuestIn, guest, pass.Inside), JobCondition.InterruptForced, null, true);
        }

        /// <summary>The edge of the zone nearest the guest: a cell inside the zone and the free cell beside it.</summary>
        private static bool FindDoor(Zone_Family zone, Pawn guest, out IntVec3 inside, out IntVec3 outside)
        {
            inside = outside = IntVec3.Invalid;
            var map = zone.Map;
            float best = float.MaxValue;
            foreach (var cell in zone.Cells)
            {
                if (!cell.Standable(map)) continue;
                float d = cell.DistanceToSquared(guest.Position);
                if (d >= best) continue;
                for (int i = 0; i < 4; i++)
                {
                    var n = cell + GenAdj.CardinalDirections[i];
                    if (n.InBounds(map) && !zone.ContainsCell(n) && n.Standable(map)) { best = d; inside = cell; outside = n; break; }
                }
            }
            return inside.IsValid;
        }

        public static void Grant(Pawn guest)
        {
            if (guest == null || !ByGuest.TryGetValue(guest.thingIDNumber, out var pass) || pass.Granted) return;
            pass.Granted = true;
            pass.GrantedTick = Find.TickManager.TicksGame;
            Trace(guest.LabelShort + " was let into " + pass.Zone.label);
            if (pass.Opener != null && pass.Opener.Spawned) MoteMaker.ThrowText(pass.Opener.DrawPos, pass.Opener.Map, "WFL_ComeIn".Translate(), Color.white);
        }

        private static void Update(GuestPass pass, int now)
        {
            var guest = pass.Guest;
            if (guest == null || guest.Dead || !guest.Spawned || guest.Map != pass.Zone.Map || pass.Zone.Map == null || !pass.Zone.Map.zoneManager.AllZones.Contains(pass.Zone))
            { End(pass, CooldownAfterVisit); return; }
            if (!pass.Granted)
            {
                bool atDoor = guest.CurJobDef == FamilyJobDefOf.WFL_WaitAtDoor && guest.Position.DistanceTo(pass.Outside) <= 2.5f;
                if (atDoor && pass.ArrivedTick < 0) pass.ArrivedTick = now;
                if (now - pass.Start > 180 && guest.CurJobDef != FamilyJobDefOf.WFL_WaitAtDoor) { End(pass, CooldownAfterRefusal); return; }     // called away
                // They wait a reasonable while once they have arrived, and then give up and carry on with something else.
                if ((pass.ArrivedTick >= 0 && now - pass.ArrivedTick > WaitLimit) || now - pass.Start > WalkLimit) { Trace(guest.LabelShort + " gave up waiting at " + pass.Zone.label); End(pass, CooldownAfterRefusal); return; }
                if (!atDoor) return;

                if (now - pass.LastKnock >= KnockEvery)
                {
                    pass.LastKnock = now;
                    MoteMaker.ThrowText(guest.DrawPos, guest.Map, "WFL_Knock".Translate(), Color.white);
                }
                var op = pass.Opener;
                bool coming = op != null && Available(op) && op.CurJobDef == FamilyJobDefOf.WFL_LetGuestIn;
                if (!coming && now - pass.LastSummon >= SummonEvery) Summon(pass, FamilyComponent.Current, now);
                return;
            }
            bool inside = pass.Zone.ContainsCell(guest.Position);
            if (inside) { pass.Entered = true; pass.LeftSince = -1; }
            else if (pass.Entered && pass.LeftSince < 0) pass.LeftSince = now;
            bool left = pass.Entered && pass.LeftSince >= 0 && now - pass.LeftSince > LeaveDelay;
            bool never = !pass.Entered && now - pass.GrantedTick > NeverEnteredLimit;
            if (left || never || now - pass.GrantedTick > PassLimit) End(pass, left ? CooldownAfterVisit : (never ? CooldownNeverEntered : CooldownAfterRefusal));
        }

        private static void End(GuestPass pass, int cooldownTicks)
        {
            var guest = pass.Guest;
            if (guest == null) return;
            ByGuest.Remove(guest.thingIDNumber);
            cooldown[guest.thingIDNumber] = Find.TickManager.TicksGame + cooldownTicks;
            if (guest.playerSettings != null && guest.playerSettings.AreaRestrictionInPawnCurrentMap == null) guest.playerSettings.AreaRestrictionInPawnCurrentMap = pass.OriginalArea;
            if (guest.CurJobDef == FamilyJobDefOf.WFL_WaitAtDoor) guest.jobs.EndCurrentJob(JobCondition.Incompletable);
            Trace(guest.LabelShort + "'s visit to " + pass.Zone?.label + " ended");
        }

        public static void Reset() { ByGuest.Clear(); cooldown.Clear(); work.Clear(); mapCursor.Reset(); pruneWork.Invalidate(); SocialMoments.Reset(); FamilyZoneView.Sync(); }
    }

    [DefOf]
    public static class FamilyJobDefOf
    {
        public static JobDef WFL_WaitAtDoor, WFL_LetGuestIn;
        static FamilyJobDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(FamilyJobDefOf)); }
    }

    /// <summary>The guest stands at the edge of the zone until someone lets them in.</summary>
    public class JobDriver_WaitAtDoor : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => !GuestPasses.IsActive(pawn));
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            var wait = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            wait.initAction = () => pawn.pather.StopDead();
            wait.tickAction = () => { if (GuestPasses.IsGranted(pawn)) ReadyForNextToil(); };
            yield return wait;
        }
    }

    /// <summary>A member of the family walks to the edge of the zone and lets the waiting guest in.</summary>
    public class JobDriver_LetGuestIn : JobDriver
    {
        private Pawn Guest => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => !GuestPasses.IsActive(Guest));
            yield return Toils_Goto.GotoCell(TargetIndex.B, PathEndMode.OnCell);
            yield return Toils_General.Wait(90, TargetIndex.A);
            yield return new Toil { defaultCompleteMode = ToilCompleteMode.Instant, initAction = () => GuestPasses.Grant(Guest) };
        }
    }

    /// <summary>Runs the guest passes a couple of times a second and keeps family zones shown or hidden to match the toggle.</summary>
    public class GameComponent_FamilyDoors : GameComponent
    {
        private List<Pawn> savedPawns = new List<Pawn>();
        private List<int> savedAreas = new List<int>();

        public GameComponent_FamilyDoors(Game game) { }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var visiting = GuestPasses.ByGuest.Values.Where(p => p.OriginalArea != null && p.Guest != null).ToList();
                savedPawns = visiting.Select(p => p.Guest).ToList();
                savedAreas = visiting.Select(p => p.OriginalArea.ID).ToList();
            }
            Scribe_Collections.Look(ref savedPawns, "wflGuestPawns", LookMode.Reference);
            Scribe_Collections.Look(ref savedAreas, "wflGuestAreas", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                savedPawns = savedPawns ?? new List<Pawn>();
                savedAreas = savedAreas ?? new List<int>();
            }
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 30 != 0) return;
            var component = FamilyComponent.Current;
            if (component == null) return;
            using var measurement = component.Performance.Measure("Zone tick");
            GuestPasses.Process(component);
            if (Find.TickManager.TicksGame % 120 == 0)
                using (component.Performance.Measure("Zone work schedules")) FamilyZones.ManageWorkTime(component);
        }

        public override void StartedNewGame() => GuestPasses.Reset();
        public override void LoadedGame()
        {
            GuestPasses.Reset();
            // A save made mid-visit holds the guest's own restriction; put it back.
            for (int i = 0; i < savedPawns.Count && i < savedAreas.Count; i++)
            {
                var pawn = savedPawns[i];
                var area = FamilyZones.FindArea(savedAreas[i]);
                if (pawn?.playerSettings != null && area != null && pawn.playerSettings.AreaRestrictionInPawnCurrentMap == null) pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
            }
            savedPawns.Clear(); savedAreas.Clear();
        }
    }

    /// <summary>The map toggle for family zones, kept beside the game's own zone toggle.</summary>
    internal static class FamilyZoneView
    {
        public static bool Show = true;
        private static Texture2D icon;
        public static Texture2D Icon => icon ?? (icon = ContentFinder<Texture2D>.Get("UI/WFL_FamilyZones", false) ?? TexButton.ShowZones);

        public static void Sync()
        {
            foreach (var map in Find.Maps)
                foreach (var zone in map.zoneManager.AllZones)
                    if (zone is Zone_Family && zone.Hidden == Show) zone.Hidden = !Show;
        }
    }

    [HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
    internal static class Patch_FamilyZoneToggle
    {
        static void Postfix(WidgetRow row, bool worldView)
        {
            if (worldView || Current.ProgramState != ProgramState.Playing) return;
            bool before = FamilyZoneView.Show;
            row.ToggleableIcon(ref FamilyZoneView.Show, FamilyZoneView.Icon, "WFL_ZoneToggle".Translate(), SoundDefOf.Mouseover_ButtonToggle);
            if (before != FamilyZoneView.Show) FamilyZoneView.Sync();
        }
    }
}
