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
        public IntVec3 Inside, Outside;
        public int Start, GrantedTick, LeftSince = -1;
        public bool Granted, Entered;
    }

    /// <summary>
    /// Family zones keep each family to its own area, which also keeps an outside builder out of someone else's zone. This is the door:
    /// when a colonist is stopped from building inside another family's zone, an adult of that family (or, for a child guest, another
    /// child) walks to the edge and lets them in; the guest waits outside until then, does the work, and the pass ends when they leave.
    /// Passes are short-lived and are not saved: after a load the guest simply asks again.
    /// </summary>
    internal static class GuestPasses
    {
        public const int WaitLimit = 2500, PassLimit = 7500, NeverEnteredLimit = 1800, LeaveDelay = 240, CooldownAfterRefusal = 2500, CooldownAfterVisit = 600;
        public static readonly Dictionary<int, GuestPass> ByGuest = new Dictionary<int, GuestPass>();
        private static readonly Dictionary<int, Zone_Family> requests = new Dictionary<int, Zone_Family>();
        private static readonly Dictionary<int, int> cooldown = new Dictionary<int, int>();

        public static bool Unrestricted(Pawn pawn) => ByGuest.Count > 0 && pawn != null && ByGuest.ContainsKey(pawn.thingIDNumber);
        public static bool IsActive(Pawn pawn) => Unrestricted(pawn);
        public static bool IsGranted(Pawn pawn) => pawn != null && ByGuest.TryGetValue(pawn.thingIDNumber, out var p) && p.Granted;

        /// <summary>Called when the game says a cell is outside a pawn's area. Only construction in another family's zone becomes a request.</summary>
        public static void NoteBlocked(Pawn pawn, IntVec3 c)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike) return;
            var zones = pawn.Map.zoneManager;
            if (zones.AllZones.Count == 0) return;
            if (!(zones.ZoneAt(c) is Zone_Family zone)) return;
            int id = pawn.thingIDNumber;
            if (ByGuest.ContainsKey(id) || requests.ContainsKey(id)) return;
            if (cooldown.TryGetValue(id, out int until) && Find.TickManager.TicksGame < until) return;
            bool construction = false;
            foreach (var t in c.GetThingList(pawn.Map)) if (t is Frame || t is Blueprint) { construction = true; break; }
            if (construction) requests[id] = zone;
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

        public static void Process(FamilyComponent component)
        {
            int now = Find.TickManager.TicksGame;
            foreach (var pair in requests.ToList()) Begin(component, pair.Key, pair.Value, now);
            requests.Clear();
            foreach (var pass in ByGuest.Values.ToList()) Update(pass, now);
        }

        private static void Begin(FamilyComponent component, int guestId, Zone_Family zone, int now)
        {
            var guest = component.TryGetPawn(guestId);
            if (!Available(guest) || zone == null || zone.Map != guest.Map) return;
            var registry = component.Registry;
            var lineage = zone.Family(registry);
            if (lineage == null || IsFamilyMember(registry, lineage, guest)) return;
            cooldown[guestId] = now + CooldownAfterRefusal;                       // replaced by a short one if the visit goes ahead and ends well

            if (!FindDoor(zone, guest, out IntVec3 inside, out IntVec3 outside)) return;
            var pass = new GuestPass { Guest = guest, Zone = zone, Inside = inside, Outside = outside, Start = now };
            ByGuest[guestId] = pass;                                               // from here the guest may walk to the door
            Pawn best = null; float bestScore = float.MaxValue;
            foreach (var person in registry.MembersOf(lineage.Id).Concat(registry.AffiliatesOf(lineage.Id)).Distinct())
            {
                var candidate = component.TryGetPawn(person.Id);
                if (candidate == null || candidate == guest || !Available(candidate) || candidate.Map != guest.Map || !CanOpenFor(candidate, guest)) continue;
                if (!candidate.CanReach(inside, PathEndMode.OnCell, Danger.Deadly)) continue;
                float score = candidate.Position.DistanceTo(inside) + (candidate.DevelopmentalStage == DevelopmentalStage.Adult ? 0f : 40f);
                if (score < bestScore) { bestScore = score; best = candidate; }
            }
            if (best == null || !guest.CanReach(outside, PathEndMode.OnCell, Danger.Deadly)) { ByGuest.Remove(guestId); return; }
            pass.Opener = best;
            guest.jobs.StartJob(JobMaker.MakeJob(FamilyJobDefOf.WFL_WaitAtDoor, outside), JobCondition.InterruptForced, null, false);
            var open = JobMaker.MakeJob(FamilyJobDefOf.WFL_LetGuestIn, guest, inside);
            best.jobs.StartJob(open, JobCondition.InterruptForced, null, true);
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
            if (left || never || now - pass.GrantedTick > PassLimit) End(pass, left ? CooldownAfterVisit : CooldownAfterRefusal);
        }

        private static void End(GuestPass pass, int cooldownTicks)
        {
            if (pass.Guest == null) return;
            ByGuest.Remove(pass.Guest.thingIDNumber);
            cooldown[pass.Guest.thingIDNumber] = Find.TickManager.TicksGame + cooldownTicks;
            if (pass.Guest.CurJobDef == FamilyJobDefOf.WFL_WaitAtDoor) pass.Guest.jobs.EndCurrentJob(JobCondition.Incompletable);
        }

        public static void Reset() { ByGuest.Clear(); requests.Clear(); cooldown.Clear(); }
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
        public GameComponent_FamilyDoors(Game game) { }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 30 != 0) return;
            FamilyZoneView.Sync();
            var component = FamilyComponent.Current;
            if (component != null) GuestPasses.Process(component);
        }

        public override void StartedNewGame() => GuestPasses.Reset();
        public override void LoadedGame() => GuestPasses.Reset();
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

    [HarmonyPatch(typeof(ForbidUtility), nameof(ForbidUtility.InAllowedArea))]
    internal static class Patch_InAllowedArea
    {
        static void Postfix(IntVec3 c, Pawn forPawn, ref bool __result)
        {
            if (!__result) GuestPasses.NoteBlocked(forPawn, c);
        }
    }

    /// <summary>While a guest has a pass they are not held to their own family's area; the pass ends when they leave the zone.</summary>
    [HarmonyPatch(typeof(Pawn_PlayerSettings), "get_EffectiveAreaRestrictionInPawnCurrentMap")]
    internal static class Patch_GuestArea
    {
        static void Postfix(ref Area __result, Pawn ___pawn)
        {
            if (__result != null && GuestPasses.Unrestricted(___pawn)) __result = null;
        }
    }
}
