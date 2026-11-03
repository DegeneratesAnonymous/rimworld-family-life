using System.Collections.Generic;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// The kinds of attention that go to someone who needs it rather than someone to enjoy: sitting with a sick relative, minding
    /// a couple's children while they are out, and bonding with a baby. Two think-tree entries are not needed; both are offered by
    /// one job giver, and each is limited by a cooldown and by the family-time budget.
    /// </summary>
    public class JobGiver_FamilyCare : ThinkNode_JobGiver
    {
        private const int ScanChannel = 1;
        private const int BedsideChannel = 2;
        private const int BabyChannel = 3;
        private const int ScanCooldown = 400;      // a pawn who found nothing to do is not asked again for a few seconds

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!Activities.FamilyTimeEnabled || pawn?.Map == null || pawn.DevelopmentalStage.Juvenile()) return null;
            var component = FamilyComponent.Current;
            if (component == null) return null;
            var registry = component.Registry;
            var me = registry.GetPerson(pawn.thingIDNumber);
            if (me == null || !me.InColony) return null;
            int now = Activities.Now;
            if (!Activities.AttemptAllowed(pawn.thingIDNumber, now, ScanCooldown, ScanChannel)) return null;
            Activities.NoteAttempt(pawn.thingIDNumber, now, ScanChannel);
            // Only someone who was idle or looking after their own needs is offered this, so care never pulls a pawn off their work.
            if (!Activities.IsFree(pawn, checkCurrentJob: true) || !Activities.CanSpareAttention(pawn)) return null;
            var settings = FamilyLifeMod.Settings;

            if ((settings == null || settings.bedside) && Activities.AttemptAllowed(pawn.thingIDNumber, now, 7500, BedsideChannel))
            {
                foreach (var bond in registry.BondsFrom(me.Id))
                {
                    if (bond.Attachment < 40f) continue;
                    var relative = component.TryGetPawn(bond.To);
                    if (relative == null || !relative.Spawned || relative.Map != pawn.Map || relative.Dead) continue;
                    bool needsCompany = relative.Downed || (relative.InBed() && HealthAIUtility.ShouldSeekMedicalRest(relative));
                    if (!needsCompany || relative.Position.DistanceTo(pawn.Position) > 60f) continue;
                    if (!pawn.CanReach(relative, PathEndMode.Touch, Danger.None)) continue;
                    Activities.NoteAttempt(pawn.thingIDNumber, now, BedsideChannel);
                    return JobMaker.MakeJob(WFL_FlavorDefOf.WFL_Bedside, relative);
                }
            }

            if ((settings == null || settings.babyBonding) && registry.HasFamilyTimeLeft(me.Id, now, Activities.DailyTargetTicks)
                && Activities.AttemptAllowed(pawn.thingIDNumber, now, 7500, BabyChannel))
            {
                foreach (var childId in registry.ChildrenOf(me.Id))
                {
                    var baby = component.TryGetPawn(childId);
                    if (baby == null || !baby.Spawned || baby.Map != pawn.Map || !baby.DevelopmentalStage.Baby()) continue;
                    if (!pawn.CanReach(baby, PathEndMode.Touch, Danger.None)) continue;
                    int hour = GenLocalDate.HourOfDay(pawn);
                    bool night = hour < 6 || hour >= 21;
                    JobDef def = !baby.Awake() && night ? WFL_FlavorDefOf.WFL_Lullaby
                               : baby.Awake() && !night ? WFL_FlavorDefOf.WFL_PlayBaby
                               : WFL_FlavorDefOf.WFL_Cradle;
                    Activities.NoteAttempt(pawn.thingIDNumber, now, BabyChannel);
                    return JobMaker.MakeJob(def, baby);
                }
            }
            return null;
        }
    }

    /// <summary>Walk to someone and stay beside them for a while. Which of the attention jobs it is decides what it does at the end.</summary>
    public class JobDriver_Attend : JobDriver
    {
        private int activityStart = -1;
        private int lastGainTick;

        private Pawn Target => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref activityStart, "activityStart", -1);
            Scribe_Values.Look(ref lastGainTick, "lastGainTick");
        }

        private int Duration
        {
            get
            {
                if (job.def == WFL_FlavorDefOf.WFL_Babysit) return 2500;
                return 1500;
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Target == null || Target.Dead);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var stay = new Toil { defaultCompleteMode = ToilCompleteMode.Delay, defaultDuration = Duration, socialMode = RandomSocialMode.Off };
            stay.initAction = () =>
            {
                pawn.pather.StopDead();
                activityStart = Activities.Now;
                lastGainTick = activityStart;
            };
            stay.tickAction = () =>
            {
                pawn.rotationTracker.FaceTarget(Target);
                int now = Activities.Now;
                if (job.def == WFL_FlavorDefOf.WFL_Babysit && now - lastGainTick >= 30 && Target.needs?.joy != null)
                {
                    Target.needs.joy.GainJoy(Activities.JoyPerTick * (now - lastGainTick), JoyKindDefOf.Social);
                    lastGainTick = now;
                }
            };
            stay.AddFinishAction(() =>
            {
                if (activityStart >= 0 && Target != null) Attending.Complete(pawn, Target, job.def, Activities.Now - activityStart);
            });
            yield return stay;
        }
    }

    internal static class Attending
    {
        public static void Complete(Pawn pawn, Pawn target, JobDef def, int ticks)
        {
            var component = FamilyComponent.Current;
            if (component == null || ticks < 600) return;
            var registry = component.Registry;
            int now = Activities.Now;
            int lineage = registry.LineageIdFor(registry.GetPerson(pawn.thingIDNumber));
            registry.RecordTogether(pawn.thingIDNumber, target.thingIDNumber, ticks, now, BondCause.Care);

            if (def == WFL_FlavorDefOf.WFL_Babysit)
            {
                Activities.GainMemory(pawn, "WFL_Babysat", target);
                var record = registry.GetPerson(target.thingIDNumber);
                foreach (var parentId in record?.ParentIds ?? new List<int>())
                {
                    var parent = component.TryGetPawn(parentId);
                    if (parent != null && !parent.Dead && parent != pawn) Activities.GainMemory(parent, "WFL_BabysatForUs", pawn);
                }
                return;
            }

            registry.AddFamilyTime(pawn.thingIDNumber, ticks, now);
            if (def == WFL_FlavorDefOf.WFL_Bedside)
            {
                Activities.GainMemory(pawn, "WFL_Bedside", target);
                Activities.GainMemory(target, "WFL_Visited", pawn);
            }
            else
            {
                Activities.GainMemory(pawn, "WFL_BondedWithBaby", target);
                Activities.GainMemory(target, "WFL_Cuddled", pawn);
                registry.Reinforce(lineage, Dim.Hearth, 0.3f);
            }
        }
    }
}
