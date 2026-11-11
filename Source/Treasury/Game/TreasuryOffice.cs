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
    /// The Treasurer's physical work. Two things only, both tied to something real: (1) business waiting at the office after a legitimate financial operation (a payday, a reward shared, a trade
    /// funded from accounts), dealt with by sitting at the Treasury's chair once; (2) carrying silver that is not yet in the treasury's own storage into it. Neither changes a balance: the books
    /// were settled when the operation happened, so nothing depends on the walk. Experience is granted per operation key (never for repeated sitting), in full when the office was used and at half when
    /// the business expired unattended.
    /// </summary>
    internal static class TreasuryOffice
    {
        private static readonly List<OfficeItem> items = new List<OfficeItem>();
        private static readonly Dictionary<int, int> deposits = new Dictionary<int, int>();          // treasurer pawn id -> the silver stack they were sent for
        private static int nextDepositTick;
        private const int DepositGapTicks = 2500, OfficeWaitTicks = 480;

        public static int Pending() => items.Count;
        internal static void Reset() { items.Clear(); deposits.Clear(); nextDepositTick = 0; }

        /// <summary>A legitimate operation settled and a Treasurer is assigned: queue the office business. Without a Treasurer nothing is queued and no experience exists to grant.</summary>
        public static void Add(FamilyComponent c, string key, FinancialOp op)
        {
            var r = c.Registry; if (TreasuryService.Holders(r, TreasuryRoles.Treasurer).Count == 0 || items.Any(i => i.Key == key)) return;
            items.Add(new OfficeItem { Key = key, Points = TreasuryXp.Points(op, r.Persons.Count(p => p.InColony)), ExpiresDay = TreasuryService.Today() + 2 });
        }

        private static Pawn FreeTreasurer(FamilyComponent c, Map map, int now) =>
            TreasuryService.Holders(c.Registry, TreasuryRoles.Treasurer).Select(c.TryGetPawn).FirstOrDefault(p => p != null && p.Spawned && p.Map == map && PawnReadinessApi.StopReason(p, now) == null && p.CurJob?.def?.defName != "WFL_TreasuryOffice");

        /// <summary>The work item (every 600 ticks): expire what nobody attended, then send the Treasurer to the office or to a deposit, at most one thing at a time.</summary>
        public static void Run(int now)
        {
            var c = FamilyComponent.Current; var map = Find.AnyPlayerHomeMap; if (c == null || map == null) return;
            int today = TreasuryService.Today();
            foreach (var expired in items.Where(i => i.ExpiresDay < today).ToList()) { Credit(c, FirstTreasurer(c), expired, TreasuryXp.AwayFromOffice(expired.Points), now); items.Remove(expired); }
            var zone = map.zoneManager.AllZones.OfType<Zone_Treasury>().OrderBy(z => z.ID).FirstOrDefault(); if (zone == null) return;
            var status = zone.Evaluate(); if (!status.Valid) return;
            var treasurer = FreeTreasurer(c, map, now); if (treasurer == null) return;
            if (items.Count > 0 && status.Seat != null && treasurer.CanReserveAndReach(status.Seat, PathEndMode.OnCell, Danger.None))
            { Start(treasurer, "WFL_TreasuryOffice", status.Seat, null); return; }
            if (status.DepositThing != null && now >= nextDepositTick) StartDeposit(treasurer, status, now);
        }

        private static Pawn FirstTreasurer(FamilyComponent c) => TreasuryService.Holders(c.Registry, TreasuryRoles.Treasurer).Select(c.TryGetPawn).FirstOrDefault(p => p != null);

        private static void Start(Pawn pawn, string def, LocalTargetInfo a, LocalTargetInfo? b)
        {
            var d = DefDatabase<JobDef>.GetNamedSilentFail(def); if (d == null) return;
            var job = JobMaker.MakeJob(d, a); if (b.HasValue) job.targetB = b.Value;
            pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
        }

        /// <summary>The Treasurer carries the loose silver stack into the treasury's best storage cell, using the game's own haul job (so stacking, reservation and storage rules are vanilla's).</summary>
        private static void StartDeposit(Pawn treasurer, TreasuryZoneStatus status, int now)
        {
            var thing = status.DepositThing; if (thing == null || thing.Destroyed || !thing.Spawned || thing.IsForbidden(treasurer)) return;
            nextDepositTick = now + DepositGapTicks;
            var cell = status.DepositGroup?.CellsList.FirstOrDefault(cl => StoreUtility.IsGoodStoreCell(cl, treasurer.Map, thing, treasurer, treasurer.Faction));
            if (cell == null || !treasurer.CanReserveAndReach(thing, PathEndMode.ClosestTouch, Danger.Deadly)) return;
            var job = JobMaker.MakeJob(JobDefOf.HaulToCell, thing, cell.Value); job.count = thing.stackCount; job.haulMode = HaulMode.ToCellStorage;
            deposits[treasurer.thingIDNumber] = thing.thingIDNumber;
            treasurer.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
        }

        /// <summary>A job ended successfully: if it is the deposit we sent the Treasurer for, that is a completed treasury deposit (once per stack per day).</summary>
        public static void OnJobFinished(LabourReport rep)
        {
            if (rep?.Pawn == null || !deposits.TryGetValue(rep.Pawn.thingIDNumber, out int thingId) || thingId != rep.ThingId) return;
            deposits.Remove(rep.Pawn.thingIDNumber);
            var c = FamilyComponent.Current; if (c == null) return;
            int now = Find.TickManager.TicksGame;
            CareerService.Report(c, rep.Pawn, TreasuryRoles.Career, null, "treasury.deposit", "deposit|" + thingId + "|" + TreasuryService.Today(), TreasuryXp.Points(FinancialOp.Deposit, 0), now);
        }

        /// <summary>The Treasurer has sat at the office for the business that was waiting: each item is credited once, in full.</summary>
        public static void Complete(Pawn pawn)
        {
            var c = FamilyComponent.Current; if (c == null || pawn == null) return;
            int now = Find.TickManager.TicksGame;
            foreach (var item in items.ToList()) { Credit(c, pawn, item, item.Points, now); items.Remove(item); }
        }

        private static void Credit(FamilyComponent c, Pawn pawn, OfficeItem item, int points, int now)
        {
            if (pawn != null) CareerService.Report(c, pawn, TreasuryRoles.Career, null, "treasury.office", item.Key, points, now);
        }

        internal static bool Working(Pawn p) => p?.CurJob?.def?.defName == "WFL_TreasuryOffice";
    }

    /// <summary>Walk to the Treasury's chair and stay for a short while: the office visit for business that is waiting. Nothing is decided here.</summary>
    public class JobDriver_TreasuryOffice : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);
            var sit = Toils_General.Wait(480);
            sit.WithProgressBarToilDelay(TargetIndex.A);
            yield return sit;
            yield return Toils_General.Do(() => TreasuryOffice.Complete(pawn));
        }
    }
}
