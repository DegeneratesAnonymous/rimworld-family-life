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
        public Zone_Family Zone;
        public Area OriginalArea;                 // the guest's own restriction, put back when the visit ends
        public IntVec3 Inside, Outside;
        public int Start, GrantedTick, LeftSince = -1;
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
        public const int WaitLimit = 2500, PassLimit = 7500, NeverEnteredLimit = 1800, LeaveDelay = 240, CooldownAfterRefusal = 2500, CooldownAfterVisit = 600, CooldownNeverEntered = 5000;
        public static readonly Dictionary<int, GuestPass> ByGuest = new Dictionary<int, GuestPass>();
        private static readonly Dictionary<int, int> cooldown = new Dictionary<int, int>();
        private static readonly HashSet<string> IdleJobs = new HashSet<string> { "Wait_Wander", "GotoWander", "Wait", "Wait_MaintainPosture", "Wait_NoSleep" };

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
            foreach (var pass in ByGuest.Values.ToList()) Update(pass, now);
            foreach (var map in Find.Maps)
            {
                if (!map.zoneManager.AllZones.Any(z => z is Zone_Family f && f.areaId != 0)) continue;
                SeekWork(component, map, now);
            }
        }

        /// <summary>Finds construction and repair work inside a family zone that an idle outsider could do, and starts one visit for it.</summary>
        private static void SeekWork(FamilyComponent component, Map map, int now)
        {
            var sites = new List<KeyValuePair<IntVec3, bool>>();
            foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint)) if (t.Faction == Faction.OfPlayer) sites.Add(new KeyValuePair<IntVec3, bool>(t.Position, false));
            foreach (var t in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame)) if (t.Faction == Faction.OfPlayer) sites.Add(new KeyValuePair<IntVec3, bool>(t.Position, false));
            foreach (var t in map.listerBuildingsRepairable.RepairableBuildings(Faction.OfPlayer)) sites.Add(new KeyValuePair<IntVec3, bool>(t.Position, true));
            // Roofs are not things: a cell in the build-roof area with no roof, or in the no-roof area with one, is work waiting.
            foreach (var zoneToScan in map.zoneManager.AllZones.OfType<Zone_Family>())
            {
                if (zoneToScan.areaId == 0) continue;
                foreach (var cell in zoneToScan.Cells)
                {
                    bool roofed = map.roofGrid.Roofed(cell);
                    if ((!roofed && map.areaManager.BuildRoof[cell] && RoofCollapseUtility.WithinRangeOfRoofHolder(cell, map)) || (roofed && map.areaManager.NoRoof[cell]))
                    {
                        sites.Add(new KeyValuePair<IntVec3, bool>(cell, false));
                        break;                                                                // one waiting cell is enough to call someone
                    }
                }
            }
            if (sites.Count == 0) return;

            var registry = component.Registry;
            var busyZones = new HashSet<Zone_Family>(ByGuest.Values.Select(p => p.Zone));
            foreach (var site in sites)
            {
                if (!(map.zoneManager.ZoneAt(site.Key) is Zone_Family zone) || zone.areaId == 0 || busyZones.Contains(zone)) continue;
                var lineage = zone.Family(registry);
                if (lineage == null) continue;
                Pawn guest = null; float bestDist = float.MaxValue;
                foreach (var pawn in map.mapPawns.FreeColonistsSpawned)
                {
                    if (ByGuest.ContainsKey(pawn.thingIDNumber) || !Available(pawn) || !IdleEnough(pawn) || !CanDoSiteWork(pawn, site.Value)) continue;
                    var restriction = pawn.playerSettings?.AreaRestrictionInPawnCurrentMap;
                    if (restriction == null || restriction.TrueCount == 0 || restriction[site.Key]) continue;      // free to go there already
                    if (cooldown.TryGetValue(pawn.thingIDNumber, out int until) && now < until) continue;
                    if (IsFamilyMember(registry, lineage, pawn)) continue;
                    float d = pawn.Position.DistanceToSquared(site.Key);
                    if (d < bestDist) { bestDist = d; guest = pawn; }
                }
                if (guest == null) continue;
                if (Begin(registry, component, guest, zone, lineage, now)) busyZones.Add(zone);
                if (ByGuest.Count >= 3) return;                                                // a few visits at a time is plenty
            }
        }

        private static bool Begin(FamilyRegistry registry, FamilyComponent component, Pawn guest, Zone_Family zone, Lineage lineage, int now)
        {
            cooldown[guest.thingIDNumber] = now + CooldownAfterRefusal;                  // replaced by a shorter one if the visit goes ahead and ends well
            if (!FindDoor(zone, guest, out IntVec3 inside, out IntVec3 outside)) { Trace(guest.LabelShort + ": no edge cell found for " + zone.label); return false; }
            Pawn best = null; float bestScore = float.MaxValue;
            foreach (var person in registry.MembersOf(lineage.Id).Concat(registry.AffiliatesOf(lineage.Id)).Distinct())
            {
                var candidate = component.TryGetPawn(person.Id);
                if (candidate == null || candidate == guest || !Available(candidate) || candidate.Map != guest.Map || !CanOpenFor(candidate, guest)) continue;
                if (!candidate.CanReach(inside, PathEndMode.OnCell, Danger.Deadly)) continue;
                float score = candidate.Position.DistanceTo(inside) + (candidate.DevelopmentalStage == DevelopmentalStage.Adult ? 0f : 40f);
                if (score < bestScore) { bestScore = score; best = candidate; }
            }
            if (best == null) { Trace(guest.LabelShort + ": nobody in the family is free to let them into " + zone.label); return false; }

            var pass = new GuestPass { Guest = guest, Opener = best, Zone = zone, Inside = inside, Outside = outside, Start = now, OriginalArea = guest.playerSettings.AreaRestrictionInPawnCurrentMap };
            ByGuest[guest.thingIDNumber] = pass;
            guest.playerSettings.AreaRestrictionInPawnCurrentMap = null;                 // so the guest can walk to the door
            if (!guest.CanReach(outside, PathEndMode.OnCell, Danger.Deadly)) { Trace(guest.LabelShort + " cannot reach the edge of " + zone.label); End(pass, CooldownAfterRefusal); return false; }
            Trace(guest.LabelShort + " waits at " + zone.label + " for " + best.LabelShort);
            guest.jobs.StartJob(JobMaker.MakeJob(FamilyJobDefOf.WFL_WaitAtDoor, outside), JobCondition.InterruptForced, null, false);
            best.jobs.StartJob(JobMaker.MakeJob(FamilyJobDefOf.WFL_LetGuestIn, guest, inside), JobCondition.InterruptForced, null, true);
            return true;
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
                bool lateStart = now - pass.Start > 180;
                if (now - pass.Start > WaitLimit || (lateStart && guest.CurJobDef != FamilyJobDefOf.WFL_WaitAtDoor)) { End(pass, CooldownAfterRefusal); return; }
                var op = pass.Opener;
                if (op == null || !Available(op) || (lateStart && op.CurJobDef != FamilyJobDefOf.WFL_LetGuestIn)) { End(pass, CooldownAfterRefusal); return; }
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

        public static void Reset() { ByGuest.Clear(); cooldown.Clear(); }
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
            FamilyZoneView.Sync();
            var component = FamilyComponent.Current;
            if (component != null) GuestPasses.Process(component);
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
