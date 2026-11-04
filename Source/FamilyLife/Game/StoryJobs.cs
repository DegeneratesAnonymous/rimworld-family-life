using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    public class JobDriver_FamilySupply : JobDriver
    {
        private int requestId, waited, talking;
        private FamilyComponent Component => FamilyComponent.Current;
        private SupplyRequest Request => Component?.Registry.Requests.FirstOrDefault(r => r.Id == requestId);
        private Pawn Donor => job.targetB.Thing as Pawn;
        public override void ExposeData()
        {
            base.ExposeData(); Scribe_Values.Look(ref requestId, "supplyRequest"); Scribe_Values.Look(ref waited, "waited"); Scribe_Values.Look(ref talking, "talking");
        }
        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (requestId == 0) requestId = Component?.Registry.Requests.LastOrDefault(r => r.Open && r.RequesterId == pawn.thingIDNumber && r.SourceId == job.targetA.Thing?.thingIDNumber)?.Id ?? 0;
            return Request != null && pawn.Reserve(job.targetA, job, 1, job.count, null, errorOnFailed) &&
                pawn.Reserve(new IntVec3(Request.HomeX, 0, Request.HomeZ), job, 1, -1, null, errorOnFailed);
        }
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => Component == null || Request == null || pawn.Drafted || pawn.InMentalState || pawn.Downed || !FamilyLifeMod.Settings.familySupplies || Request.State == SupplyState.Cancelled);
            AddFinishAction(condition =>
            {
                var r = Request;
                if (r != null && r.Open) r.State = SupplyState.Cancelled;
                if (pawn.carryTracker.CarriedThing != null) pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
            });
            if (job.def == WFL_StoryDefOf.WFL_ExchangeSupply)
            {
                yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
                var talk = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
                talk.initAction = () =>
                {
                    if (Donor?.CurJobDef == WFL_StoryDefOf.WFL_StoryListen && Donor.CurJob.targetA.Pawn == pawn) return;
                    if (Donor == null || !Activities.IsFree(Donor) || Donor.CurJob?.playerForced == true) { EndJobWith(JobCondition.Incompletable); return; }
                    Donor.jobs.StartJob(JobMaker.MakeJob(WFL_StoryDefOf.WFL_StoryListen, pawn), JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                };
                talk.tickAction = () =>
                {
                    if (++waited > 1200 || Donor == null || Donor.Dead || Donor.Drafted || Donor.InMentalState || Donor.Map != pawn.Map || Donor.CurJobDef != WFL_StoryDefOf.WFL_StoryListen) { EndJobWith(JobCondition.Incompletable); return; }
                    if (pawn.Position.DistanceTo(Donor.Position) > 2) return;
                    if (++talking < 180) return;
                    if (Request.State == SupplyState.Pending && !Component.Registry.NegotiateSupply(Request, Component.ProfileOf(Donor.thingIDNumber)?[(int)Dim.Warmth] ?? 0,
                        Component.Economy.DonorNeed(Donor, Request), FamilyEconomy.InventorySilver(pawn) >= Request.Price, Activities.Now))
                    { Messages.Message("WFL_SupplyRefused".Translate(Donor.LabelShort, pawn.LabelShort), pawn, MessageTypeDefOf.NeutralEvent, false); EndJobWith(JobCondition.Incompletable); return; }
                    ReadyForNextToil();
                };
                yield return talk;
            }
            else
                yield return new Toil { initAction = () =>
                {
                    if (Request.Approach == SupplyApproach.Steal && !FamilyLifeMod.Settings.familyTheft) { EndJobWith(JobCondition.Incompletable); return; }
                    Request.State = SupplyState.Granted;
                }};
            var approach = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            approach.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return approach;
            yield return Toils_Haul.StartCarryThing(TargetIndex.A, false, false, false, false);
            yield return new Toil { initAction = () =>
            {
                var carried = pawn.carryTracker.CarriedThing;
                var r = Request;
                if (carried == null || carried.stackCount < r.Count || carried.def.defName != r.Definition || (r.Approach == SupplyApproach.Barter && Component.Registry.ClaimFor(carried.thingIDNumber)?.HolderFamily != Component.Registry.FamilyOf(r.DonorId)) || (r.Approach == SupplyApproach.Barter && (Donor == null || !Component.Economy.Pay(pawn, Donor, r.Price))))
                { EndJobWith(JobCondition.Incompletable); return; }
                var witnesses = StoryActions.Witnesses(pawn.Map, pawn, pawn.Position);
                if (!Component.Registry.ExchangeSupply(r.Id, carried.thingIDNumber, carried.stackCount, witnesses, Activities.Now)) { EndJobWith(JobCondition.Incompletable); return; }
                job.targetC = new IntVec3(r.HomeX, 0, r.HomeZ);
                Component.Economy.Invalidate(pawn.Map);
                if (r.Approach != SupplyApproach.PublicClaim && r.Approach != SupplyApproach.Relocate) Messages.Message("WFL_SupplyTransferred".Translate(pawn.LabelShort, carried.Label), pawn, MessageTypeDefOf.NeutralEvent, false);
            }};
            yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell);
            yield return new Toil { initAction = () =>
            {
                var home = FamilyZones.AreaFor(Component.Registry.GetLineage(Component.Registry.FamilyOf(pawn.thingIDNumber)));
                if (home?.Map != pawn.Map || !home[job.targetC.Cell] || job.targetC.Cell.IsForbidden(pawn) || pawn.carryTracker.CarriedThing == null || !StoreUtility.IsValidStorageFor(job.targetC.Cell, pawn.Map, pawn.carryTracker.CarriedThing) || !pawn.carryTracker.TryDropCarriedThing(job.targetC.Cell, ThingPlaceMode.Direct, out _))
                { EndJobWith(JobCondition.Incompletable); return; }
                Request.State = SupplyState.Stored; Component.Economy.Invalidate(pawn.Map);
            }};
        }
    }

    public class JobDriver_StoryTalk : JobDriver
    {
        private int waited;
        private Pawn Other => job.targetA.Thing as Pawn;
        public override void ExposeData() { base.ExposeData(); Scribe_Values.Look(ref waited, "storyWait"); }
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Other == null || Other.Dead || Other.Drafted || Other.InMentalState || pawn.Drafted || pawn.InMentalState || !FamilyLifeMod.Settings.familyStories);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var talk = new Toil { defaultCompleteMode = ToilCompleteMode.Never };
            talk.initAction = () =>
            {
                if (!Activities.IsFree(Other) || Other.CurJob?.playerForced == true) { EndJobWith(JobCondition.Incompletable); return; }
                Other.jobs.StartJob(JobMaker.MakeJob(WFL_StoryDefOf.WFL_StoryListen, pawn), JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
            };
            talk.tickAction = () =>
            {
                if (++waited > 600 || Other.CurJobDef != WFL_StoryDefOf.WFL_StoryListen || Other.Position.DistanceTo(pawn.Position) > 3) { EndJobWith(JobCondition.Incompletable); return; }
                if (waited >= 180) { StoryActions.FinishTalk(pawn, Other, job.count); ReadyForNextToil(); }
            };
            yield return talk;
        }
    }
    public class JobDriver_StoryListen : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.Drafted || pawn.InMentalState || job.targetA.Pawn?.CurJob == null ||
                (job.targetA.Pawn.CurJobDef != WFL_StoryDefOf.WFL_StoryTalk && job.targetA.Pawn.CurJobDef != WFL_StoryDefOf.WFL_ExchangeSupply));
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.Wait(1200);
        }
    }
}
