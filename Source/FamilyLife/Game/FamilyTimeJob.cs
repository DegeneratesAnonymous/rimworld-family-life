using System.Collections.Generic;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>Offered during joy time like any other recreation, so family time replaces recreation instead of adding to the schedule.</summary>
    public class JoyGiver_FamilyTime : JoyGiver
    {
        public override bool CanBeGivenTo(Pawn pawn)
        {
            return Activities.FamilyTimeEnabled && base.CanBeGivenTo(pawn);
        }

        public override Job TryGiveJob(Pawn pawn)
        {
            if (!Activities.FamilyTimeEnabled) return null;
            var choice = Flavors.Choose(pawn);
            if (choice == null) return null;
            Flavors.StartSitter(choice);
            return JobMaker.MakeJob(choice.Def, choice.Other);
        }
    }

    /// <summary>
    /// Spend time beside a relative: walk over, stay near them (following if they move), chat now and then, and share the joy.
    /// The other person is not interrupted; they simply have company.
    /// </summary>
    public class JobDriver_FamilyTime : JobDriver
    {
        private const int Duration = 2200;
        private const int FollowEvery = 120;
        private const int ChatEvery = 700;
        private const int GiveUpFarTicks = 300;

        private int activityStart = -1;
        private int lastGainTick;
        private int lastChatTick;
        private int lastFollowTick;
        private int farTicks;

        private Pawn Partner => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref activityStart, "activityStart", -1);
            Scribe_Values.Look(ref lastGainTick, "lastGainTick");
            Scribe_Values.Look(ref lastChatTick, "lastChatTick");
            Scribe_Values.Look(ref lastFollowTick, "lastFollowTick");
            Scribe_Values.Look(ref farTicks, "farTicks");
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Partner == null || Partner.Dead || Partner.Downed || Partner.Drafted || Partner.InMentalState);

            yield return Toils_Interpersonal.GotoInteractablePosition(TargetIndex.A);

            var stay = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = Duration,
                socialMode = RandomSocialMode.Off
            };
            stay.initAction = () =>
            {
                pawn.pather.StopDead();
                activityStart = Activities.Now;
                lastGainTick = activityStart;
                lastChatTick = activityStart;
                lastFollowTick = activityStart;
            };
            stay.tickAction = () =>
            {
                var partner = Partner;
                int now = Activities.Now;
                pawn.rotationTracker.FaceTarget(partner);

                if (now - lastGainTick >= 30)
                {
                    float amount = Activities.JoyPerTick * (now - lastGainTick);
                    lastGainTick = now;
                    if (job.def != WFL_FlavorDefOf.WFL_Reconcile)
                    {
                        var kind = job.def.joyKind ?? DefDatabase<JoyKindDef>.GetNamed("WFL_FamilyTime");
                        pawn.needs?.joy?.GainJoy(amount, kind);
                        if (partner.needs?.joy != null && partner.Position.InHorDistOf(pawn.Position, 5f))
                            partner.needs.joy.GainJoy(amount, kind);
                        if (pawn.needs?.joy != null && pawn.needs.joy.CurLevel >= 0.99f) EndJobWith(JobCondition.Succeeded);
                    }
                }

                bool near = partner.Position.InHorDistOf(pawn.Position, 4f);
                farTicks = near ? 0 : farTicks + 1;
                if (farTicks > GiveUpFarTicks) { EndJobWith(JobCondition.Succeeded); return; }
                if (!near && now - lastFollowTick >= FollowEvery)
                {
                    lastFollowTick = now;
                    if (!pawn.pather.Moving) pawn.pather.StartPath(partner, PathEndMode.Touch);
                }
                if (near && now - lastChatTick >= ChatEvery)
                {
                    lastChatTick = now;
                    pawn.interactions.TryInteractWith(partner, InteractionDefOf.Chitchat);
                }
            };
            stay.AddFinishAction(() =>
            {
                if (activityStart < 0 || Partner == null) return;
                Flavors.Complete(pawn, Partner, job.def, Activities.Now - activityStart);
            });
            yield return stay;
        }

        public override string GetReport()
        {
            var partner = Partner;
            return partner == null ? base.GetReport() : "WFL_Report_FamilyTime".Translate(partner.LabelShort).ToString();
        }
    }
}
