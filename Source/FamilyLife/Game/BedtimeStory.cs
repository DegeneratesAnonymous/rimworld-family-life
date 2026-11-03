using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// In the hour before a child's bedtime, the parent with the best Social skill who is free reads to them. A child falls asleep
    /// the moment they lie down, so the story has to happen just before bed.
    /// </summary>
    public class JobGiver_BedtimeStory : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!Activities.StoriesEnabled || pawn?.Map == null || pawn.DevelopmentalStage.Juvenile()) return null;
            var component = FamilyComponent.Current;
            if (component == null) return null;
            var registry = component.Registry;
            var me = registry.GetPerson(pawn.thingIDNumber);
            if (me == null || !me.InColony) return null;

            var children = registry.ChildrenOf(me.Id);
            if (children.Count == 0) return null;
            int now = Activities.Now;
            if (!registry.HasFamilyTimeLeft(me.Id, now, Activities.DailyTargetTicks)) return null;
            if (!AnyChildGoingToBed(component, registry, pawn, children, now)) return null;
            if (!IsFreeForStory(pawn)) return null;

            foreach (var childId in children)
            {
                var childRecord = registry.GetPerson(childId);
                var child = component.TryGetPawn(childId);
                if (childRecord == null || child == null || !childRecord.InColony || child.Map != pawn.Map) continue;
                if (!child.DevelopmentalStage.Child() || !IsBedtimeNext(child) || !registry.StoryDue(childId, now)) continue;
                if (!Activities.IsFree(child) || !pawn.CanReserve(child) || !pawn.CanReach(child, PathEndMode.Touch, Danger.None)) continue;

                // The parent with the best Social skill reads, if they are free; otherwise the next best.
                var readers = childRecord.ParentIds
                    .Select(id => component.TryGetPawn(id))
                    .Where(p => p != null && p.Map == pawn.Map && IsFreeForStory(p) && registry.GetPerson(p.thingIDNumber)?.InColony == true)
                    .Select(p => new Reader { Id = p.thingIDNumber, Social = Activities.SocialOf(p) })
                    .ToList();
                var chosen = FamilyRegistry.PickReader(readers);
                if (chosen == null || chosen.Id != pawn.thingIDNumber) continue;

                // Whatever happens next, do not offer this child's story again tonight: a story that fails must never loop.
                registry.RecordStory(childId, now);
                var book = Activities.FindBookFor(pawn, child);
                return book != null ? JobMaker.MakeJob(WFL_JobDefOf.WFL_BedtimeStory, child, book) : JobMaker.MakeJob(WFL_JobDefOf.WFL_BedtimeStory, child);
            }
            return null;
        }

        /// <summary>Most of the day no child is about to go to bed; that is settled with a few cheap checks before anything about the parent is looked at.</summary>
        private static bool AnyChildGoingToBed(FamilyComponent component, FamilyRegistry registry, Pawn parent, IReadOnlyList<int> children, int now)
        {
            foreach (var childId in children)
            {
                var child = component.TryGetPawn(childId);
                if (child == null || child.Map != parent.Map || !child.DevelopmentalStage.Child()) continue;
                if (IsBedtimeNext(child) && registry.StoryDue(childId, now)) return true;
            }
            return false;
        }

        private static bool IsFreeForStory(Pawn p)
        {
            if (p == null || !p.Spawned || p.Dead || p.Downed || p.Drafted || p.InMentalState || !p.Awake() || p.IsPrisoner) return false;
            return p.timetable?.CurrentAssignment != TimeAssignmentDefOf.Work && Activities.CanSpareAttention(p);
        }

        /// <summary>True in the hour before the child's schedule says sleep.</summary>
        private static bool IsBedtimeNext(Pawn child)
        {
            var timetable = child.timetable;
            if (timetable == null) return false;
            int hour = GenLocalDate.HourOfDay(child);
            return timetable.CurrentAssignment != TimeAssignmentDefOf.Sleep && timetable.GetAssignment((hour + 1) % 24) == TimeAssignmentDefOf.Sleep;
        }
    }

    [DefOf]
    public static class WFL_JobDefOf
    {
        public static JobDef WFL_BedtimeStory;
        public static JobDef WFL_ListenToStory;

        static WFL_JobDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(WFL_JobDefOf));
    }

    /// <summary>TargetA is the child; TargetB is an optional book that the reader fetches first and puts down afterwards.</summary>
    public class JobDriver_BedtimeStory : JobDriver
    {
        private int activityStart = -1;
        private Pawn Child => job.GetTarget(TargetIndex.A).Thing as Pawn;
        private bool HasBook => job.GetTarget(TargetIndex.B).HasThing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed)) return false;
            // If someone else took the book, the story goes ahead without it.
            if (HasBook && !pawn.Reserve(job.targetB, job, 1, -1, null, false)) job.targetB = LocalTargetInfo.Invalid;
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref activityStart, "activityStart", -1);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => Child == null || Child.Dead || Child.Downed || Child.Drafted || Child.InMentalState);
            AddFinishAction(condition => PutBookDown());

            if (HasBook)
            {
                // Only while walking to the book: once it is carried it is no longer spawned, which must not fail the job.
                yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.ClosestTouch).FailOnDespawnedNullOrForbidden(TargetIndex.B);
                yield return Toils_Haul.StartCarryThing(TargetIndex.B);
            }

            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

            var read = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = ActivityTuning.StoryTicks,
                socialMode = RandomSocialMode.Off
            };
            read.initAction = () =>
            {
                pawn.pather.StopDead();
                activityStart = Activities.Now;
                var listen = JobMaker.MakeJob(WFL_JobDefOf.WFL_ListenToStory, pawn);
                Child.jobs.TryTakeOrderedJob(listen, JobTag.Misc);
            };
            read.tickAction = () => pawn.rotationTracker.FaceTarget(Child);
            read.AddFinishAction(() =>
            {
                if (activityStart < 0 || Child == null) return;
                int read_ticks = Activities.Now - activityStart;
                if (Child.CurJobDef == WFL_JobDefOf.WFL_ListenToStory)      // they were really there for it
                    Activities.CompleteStory(pawn, Child, HasBook ? job.GetTarget(TargetIndex.B).Thing : null, read_ticks);
            });
            yield return read;
        }

        private void PutBookDown()
        {
            if (pawn.carryTracker?.CarriedThing != null && pawn.carryTracker.CarriedThing == job.GetTarget(TargetIndex.B).Thing)
                pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
        }

        public override string GetReport()
        {
            var child = Child;
            return child == null ? base.GetReport() : "WFL_Report_BedtimeStory".Translate(child.LabelShort).ToString();
        }
    }

    /// <summary>The child's half of a bedtime story: sit quietly facing the reader until the story ends.</summary>
    public class JobDriver_ListenToStory : JobDriver
    {
        private Pawn Reader => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            var listen = new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Delay,
                defaultDuration = ActivityTuning.StoryTicks * 2,       // a safety limit; the reader ending the story ends this first
                socialMode = RandomSocialMode.Off
            };
            listen.initAction = () => pawn.pather.StopDead();
            listen.tickAction = () =>
            {
                var reader = Reader;
                if (reader == null || reader.Dead || reader.CurJobDef != WFL_JobDefOf.WFL_BedtimeStory) { EndJobWith(JobCondition.Succeeded); return; }
                pawn.rotationTracker.FaceTarget(reader);
            };
            yield return listen;
        }

        public override string GetReport() => "WFL_Report_ListenToStory".Translate().ToString();
    }
}
