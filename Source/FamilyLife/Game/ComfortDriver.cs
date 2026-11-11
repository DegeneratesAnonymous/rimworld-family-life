using System;
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
    /// When someone is close to breaking, or has broken, the people who love them try to reach them: a partner, or a parent. A kind word from the
    /// right person can talk them back; from the wrong one it can do nothing. How well it goes rests on how they feel about each other, how good the
    /// helper is with people, and a little chance. Aggressive breaks are left to others, and nobody is made to try more than a few times.
    /// </summary>
    internal static class ComfortDriver
    {
        private const int ScanEvery = 250, TryEvery = 2500, MaxTries = 3, Forget = 30000;
        private static int lastScan;
        private static readonly Dictionary<int, int> nextTry = new Dictionary<int, int>(), tries = new Dictionary<int, int>(), lastTried = new Dictionary<int, int>();

        public static void Reset() { nextTry.Clear(); tries.Clear(); lastTried.Clear(); lastScan = 0; }

        private static bool Enabled => FamilyLifeMod.Settings == null || FamilyLifeMod.Settings.mindComfort;

        private static bool Reachable(Pawn p) => p != null && p.Spawned && !p.Dead && !p.Downed;

        /// <summary>Someone who has broken in a way a person can talk to, or is about to.</summary>
        private static bool NeedsComfort(Pawn p)
        {
            if (!Reachable(p) || !p.IsColonist || !p.RaceProps.Humanlike) return false;
            if (p.InMentalState) return !p.MentalState.def.IsAggro && !p.MentalState.def.IsExtreme;
            var breaker = p.mindState?.mentalBreaker;
            return breaker != null && (breaker.BreakMinorIsImminent || breaker.BreakMajorIsImminent || breaker.BreakExtremeIsImminent);
        }

        private static float SocialOf(Pawn p)
        {
            var s = p.skills?.GetSkill(SkillDefOf.Social);
            return s == null || s.TotallyDisabled ? 0f : Math.Min(1f, s.Level / 20f);
        }

        private static bool CanCome(Pawn helper, Pawn sufferer)
            => Reachable(helper) && helper != sufferer && helper.Map == sufferer.Map && !helper.Drafted && !helper.InMentalState && helper.Awake() && helper.CurJob?.playerForced != true &&
               helper.CurJobDef != WFL_StoryDefOf.WFL_Comfort && helper.CanReach(sufferer, PathEndMode.Touch, Danger.Some);

        public static void Tick(FamilyComponent c, int now)
        {
            if (!Enabled || now - lastScan < ScanEvery) return;
            lastScan = now;
            var r = c.Registry;
            int started = 0;
            foreach (var map in Find.Maps.Where(m => m.IsPlayerHome))
                foreach (var sufferer in map.mapPawns.FreeColonistsSpawned.Where(NeedsComfort).Take(8).ToList())
                {
                    if (started >= 3) return;
                    int id = sufferer.thingIDNumber;
                    if (lastTried.TryGetValue(id, out int last) && now - last > Forget) { tries.Remove(id); lastTried.Remove(id); }      // a different break, long after
                    if (nextTry.TryGetValue(id, out int until) && now < until) continue;
                    if (tries.TryGetValue(id, out int n) && n >= MaxTries) continue;
                    var rec = r.GetPerson(id);
                    if (rec == null) continue;
                    Pawn best = null; float bestQ = 0f;
                    foreach (int hid in rec.PartnerIds().Concat(rec.ParentIds).Distinct())
                    {
                        if (!r.CanComfort(hid, id)) continue;
                        var helper = c.TryGetPawn(hid);
                        if (!CanCome(helper, sufferer)) continue;
                        float q = r.ComfortQuality(hid, id, SocialOf(helper));
                        if (q > bestQ) { bestQ = q; best = helper; }
                    }
                    if (best == null || bestQ < FamilyRegistry.ComfortWillTry) { nextTry[id] = now + TryEvery; continue; }       // nobody who would, or who thinks it would help
                    nextTry[id] = now + TryEvery; tries[id] = n + 1; lastTried[id] = now;
                    best.jobs.StartJob(JobMaker.MakeJob(WFL_StoryDefOf.WFL_Comfort, sufferer), JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                    started++;
                }
        }

        /// <summary>The talk has happened. Settle how it went, in the sufferer's mind and between the two.</summary>
        internal static void Finish(Pawn helper, Pawn sufferer)
        {
            var c = FamilyComponent.Current;
            if (c == null || !Reachable(helper) || !Reachable(sufferer) || helper.Position.DistanceTo(sufferer.Position) > 4f) return;
            var r = c.Registry; int now = Activities.Now;
            float q = r.ComfortQuality(helper.thingIDNumber, sufferer.thingIDNumber, SocialOf(helper)) + Rand.Range(-.1f, .1f);
            var result = FamilyRegistry.ClassifyComfort(q);
            int seed = helper.thingIDNumber * 31 + sufferer.thingIDNumber + now / 600;
            string a = helper.LabelShort, b = sufferer.LabelShort;
            if (result == ComfortResult.Resolved)
            {
                if (sufferer.InMentalState) sufferer.MentalState.RecoverFromState();
                Activities.GainMemory(sufferer, "WFL_Comforted", helper);
                Messages.Message(Phrasing.Pick("WFL_Comfort_Resolved", seed, a, b), sufferer, MessageTypeDefOf.PositiveEvent, false);
            }
            else if (result == ComfortResult.Eased)
            {
                Activities.GainMemory(sufferer, "WFL_ComfortedEased", helper);          // enough to lift their mood; if they have broken, it helps them come back on their own
                Messages.Message(Phrasing.Pick("WFL_Comfort_Eased", seed, a, b), sufferer, MessageTypeDefOf.NeutralEvent, false);
            }
            else Messages.Message(Phrasing.Pick("WFL_Comfort_Failed", seed, a, b), sufferer, MessageTypeDefOf.NeutralEvent, false);
            r.RecordComfort(helper.thingIDNumber, sufferer.thingIDNumber, result, now);
        }
    }

    /// <summary>The helper goes to the troubled person and talks with them for a while.</summary>
    public class JobDriver_Comfort : JobDriver
    {
        private Pawn Sufferer => job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedOrNull(TargetIndex.A);
            this.FailOn(() => pawn.Drafted || pawn.InMentalState || Sufferer == null || Sufferer.Dead || Sufferer.Downed);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            var talk = Toils_General.WaitWith(TargetIndex.A, 300, useProgressBar: false, maintainPosture: true);
            talk.FailOn(() => Sufferer != null && Sufferer.Position.DistanceTo(pawn.Position) > 5f);
            yield return talk;
            yield return new Toil { defaultCompleteMode = ToilCompleteMode.Instant, initAction = () => ComfortDriver.Finish(pawn, Sufferer) };
        }
    }
}
