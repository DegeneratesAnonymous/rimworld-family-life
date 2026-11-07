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
    /// The yearly assembly: all the families come together to choose the colony's posts. It is announced, the house heads say whom they back, and then
    /// every adult who is free walks to the polling place and votes in person. The player's own pawn is asked, not sent. The count is announced, with
    /// how each holder was judged to have done.
    /// </summary>
    internal static class AssemblyDriver
    {
        internal static string GradeWord(float grade) => (grade >= .7f ? "WFL_Grade_Good" : grade >= .4f ? "WFL_Grade_Fair" : "WFL_Grade_Poor").Translate();

        internal static void Run(FamilyComponent c, FamilyRegistry r, int now)
        {
            var ctx = OfficeDriver.Context(c);
            var called = r.CallAssembly(now, ctx);
            if (called != null && called.Races.Count > 0) Announce(c, r, called);
            var a = r.OpenAssembly;
            if (a != null && a.State == AssemblyState.Campaign) Endorse(c, r, a, ctx);
            var counted = r.AdvanceAssembly(now);
            if (counted != null && a != null) Report(c, r, a);
            a = r.OpenAssembly;
            if (a != null && a.State == AssemblyState.Voting) SendVoters(c, r, a);
        }

        /// <summary>Why someone is standing, in plain words: a calling, a personal aim, their house's wish, a duty, or already holding it.</summary>
        internal static string MotiveWords(PostMotive m)
        {
            var parts = new List<string>();
            foreach (PostMotive flag in new[] { PostMotive.Calling, PostMotive.Recognition, PostMotive.Sponsored, PostMotive.Duty, PostMotive.Incumbent })
                if (m.HasFlag(flag)) parts.Add(("WFL_Motive_" + flag).Translate().ToString());
            return string.Join(", ", parts);
        }

        private static string Names(FamilyRegistry r, IEnumerable<int> ids) => string.Join(", ", ids.Select(i => FamilyUi.NameOf(r, i)));

        private static void Announce(FamilyComponent c, FamilyRegistry r, CommunityAssembly a)
        {
            var lines = new List<string>();
            foreach (var race in a.Races)
            {
                string post = ("WFL_OfficeKind_" + race.Kind).Translate();
                var challengers = race.Candidates.Where(x => x != race.IncumbentId).ToList();
                lines.Add(race.IncumbentId != 0
                    ? "WFL_Assembly_RaceIncumbent".Translate(post, FamilyUi.NameOf(r, race.IncumbentId), GradeWord(race.IncumbentGrade)).ToString()
                    : "WFL_Assembly_RaceOpen".Translate(post).ToString());
                foreach (int cand in challengers)
                    lines.Add("    " + "WFL_Assembly_Stands".Translate(FamilyUi.NameOf(r, cand), MotiveWords(r.MotivesOf(race, cand))).ToString());
            }
            Find.LetterStack.ReceiveLetter("WFL_Assembly_Title".Translate(), "WFL_Assembly_Text".Translate(string.Join("\n", lines)), LetterDefOf.PositiveEvent);
        }

        /// <summary>Each house head, unless they are the player's pawn, publicly backs whom they find best in each race. Two houses a step keeps it light.</summary>
        private static void Endorse(FamilyComponent c, FamilyRegistry r, CommunityAssembly a, OfficeContext ctx)
        {
            foreach (var house in r.Lineages.Where(h => h.AbsorbedIntoId == 0 && r.IsActiveHouse(h.Id)).Take(24))
            {
                int head = r.HeadOf(house.Id);
                if (head == 0 || Governance.IsAvatar(c, head)) continue;
                foreach (var race in a.Races.Where(x => !a.Endorsements.Any(e => e.FamilyId == house.Id && e.Kind == x.Kind)))
                {
                    int pick = r.HouseChoice(head, race, a, ctx, out float score);
                    if (pick != 0 && score >= FamilyRegistry.SupportFloor) r.EndorseAssembly(a.Id, house.Id, head, race.Kind, pick);
                }
            }
        }

        private static void SendVoters(FamilyComponent c, FamilyRegistry r, CommunityAssembly a)
        {
            if (!GovernanceDriver.PollingPlace(c, out var map, out var cell)) return;
            int sent = 0;
            foreach (int voterId in r.AssemblyVoters(a))
            {
                if (sent >= 6) break;
                var pawn = c.TryGetPawn(voterId);
                if (pawn == null || !pawn.Spawned || pawn.Map != map || PerspectiveShiftBridge.IsAvatar(pawn)) continue;
                if (!Activities.IsFree(pawn) || pawn.CurJob?.playerForced == true || pawn.CurJobDef == WFL_StoryDefOf.WFL_CastAssemblyVote || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_CastAssemblyVote, cell); job.count = a.Id;
                pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc); sent++;
            }
        }

        private static void Report(FamilyComponent c, FamilyRegistry r, CommunityAssembly a)
        {
            var lines = new List<string>();
            foreach (var race in a.Races)
            {
                string post = ("WFL_OfficeKind_" + race.Kind).Translate();
                if (race.WinnerId == 0) lines.Add("WFL_Assembly_ResultNone".Translate(post).ToString());
                else if (race.WinnerId == race.IncumbentId) lines.Add("WFL_Assembly_ResultKept".Translate(post, FamilyUi.NameOf(r, race.WinnerId), GradeWord(race.IncumbentGrade)).ToString());
                else lines.Add("WFL_Assembly_ResultNew".Translate(post, FamilyUi.NameOf(r, race.WinnerId), race.IncumbentId == 0 ? "" : FamilyUi.NameOf(r, race.IncumbentId)).ToString());
            }
            Find.LetterStack.ReceiveLetter("WFL_Assembly_ResultTitle".Translate(), "WFL_Assembly_ResultText".Translate(string.Join("\n", lines)), LetterDefOf.PositiveEvent);
        }
    }

    /// <summary>A colonist walking to the polling place and casting their ballot for every post at the yearly assembly.</summary>
    public class JobDriver_CastAssemblyVote : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOn(() => pawn.Drafted || pawn.InMentalState);
            yield return Toils_Goto.GotoCell(TargetIndex.A, PathEndMode.OnCell);
            yield return Toils_General.Wait(180);
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant,
                initAction = () =>
                {
                    var c = FamilyComponent.Current;
                    c?.Registry.CastAssemblyBallot(job.count, pawn.thingIDNumber, OfficeDriver.Context(c), Activities.Now);
                }
            };
        }
    }
}
