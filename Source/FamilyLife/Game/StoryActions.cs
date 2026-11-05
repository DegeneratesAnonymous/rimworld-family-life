using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    internal static class StoryActions
    {
        internal static List<int> Witnesses(Map map, Pawn actor, IntVec3 point)
        {
            if (map == null) return new List<int>();
            return map.mapPawns.FreeColonistsSpawned.Where(p => !p.Dead && p.Position.DistanceTo(point) <= 20 && GenSight.LineOfSight(p.Position, point, map) &&
                (actor == null || p == actor || GenSight.LineOfSight(p.Position, actor.Position, map))).Select(p => p.thingIDNumber).Take(64).ToList();
        }
        internal static IncidentContext Context(Pawn actor, Pawn victim, Thing intendedTarget = null)
        {
            if (actor == null) return IncidentContext.None;
            var context = actor.InMentalState ? IncidentContext.MentalBreak : IncidentContext.None;
            if (actor.Drafted) context |= IncidentContext.Combat;
            if (intendedTarget is Pawn intended && intended != victim && intended.HostileTo(actor) && !victim.HostileTo(actor)) context |= IncidentContext.Accident | IncidentContext.Combat;
            if (FamilyComponent.Current?.Registry.Plots.Any(p => p.State == PlotState.Attempted && p.TargetId == victim.thingIDNumber && (p.OrganizerId == actor.thingIDNumber || p.Recruits.Contains(actor.thingIDNumber))) == true) context |= IncidentContext.Planned;
            return context;
        }
        private static bool Available(Pawn p) => p != null && p.Spawned && !p.Dead && !p.DevelopmentalStage.Juvenile() && p.ageTracker.AgeBiologicalYears >= 18 && Activities.IsFree(p) && p.CurJob?.playerForced != true && Activities.CanSpareAttention(p);
        internal static void CancelUnsafeAttacks(FamilyComponent c)
        {
            foreach (var plot in c.Registry.Plots.Where(p => p.AttackJobs.Count > 0))
            {
                bool unsafeNow = !FamilyLifeMod.Settings.familyStories || !FamilyLifeMod.Settings.familyViolence || !FamilyLifeMod.Settings.lethalPlots || !plot.Active ||
                    c.Registry.Incidents.FirstOrDefault(i => i.Id == plot.IncidentId)?.Resolved != false;
                foreach (var id in plot.Recruits.Concat(new[] { plot.OrganizerId }))
                {
                    var pawn = c.TryGetPawn(id);
                    if (pawn?.CurJob == null || pawn.CurJob.playerForced || !plot.AttackJobs.Contains(pawn.CurJob.loadID)) continue;
                    if (unsafeNow || pawn.Drafted || pawn.Map?.mapPawns.FreeColonistsSpawned.Any(p => p.Drafted) == true || pawn.Map?.mapPawns.AllPawnsSpawned.Any(p => !p.Downed && !p.IsPrisoner && p.HostileTo(Faction.OfPlayer)) == true)
                    { pawn.jobs.EndCurrentJob(JobCondition.InterruptForced); plot.State = PlotState.Abandoned; }
                }
            }
        }
        internal static bool TryPlan(FamilyComponent c, Map map, int now)
        {
            var r = c.Registry;
            foreach (var plot in r.Plots.Where(p => p.Active).ToList())
            {
                var actor = c.TryGetPawn(plot.OrganizerId); var target = c.TryGetPawn(plot.TargetId);
                if (actor?.Map != map || target?.Map != map || !target.Spawned) continue;
                if (plot.State == PlotState.Attempted)
                {
                    if (now - plot.LastActionTick > 3500)
                        plot.State = r.Incidents.Any(i => i.Tick >= plot.LastActionTick && i.VictimId == plot.TargetId && (i.Context & IncidentContext.Planned) != 0 && (i.ActorId == plot.OrganizerId || plot.Recruits.Contains(i.ActorId)) && (plot.Action == PlotAction.Fight || i.Kind == IncidentKind.Death)) ? PlotState.Completed : PlotState.Abandoned;
                    continue;
                }
                if (!FamilyLifeMod.Settings.familyViolence || plot.Action == PlotAction.Lethal && !FamilyLifeMod.Settings.lethalPlots) { plot.State = PlotState.Abandoned; continue; }
                if (!Available(actor) || actor.relations.OpinionOf(target) >= 30 || now - plot.LastActionTick < 2500) continue;
                if (map.mapPawns.AllPawnsSpawned.Any(p => !p.Downed && !p.IsPrisoner && p.HostileTo(Faction.OfPlayer)) || map.mapPawns.FreeColonistsSpawned.Any(p => p.Drafted)) continue;
                if (plot.State == PlotState.Recruiting)
                {
                    var recruit = map.mapPawns.FreeColonistsSpawned.Where(p => p != actor && Available(p) && r.FamilyOf(p.thingIDNumber) == r.FamilyOf(actor.thingIDNumber) && !plot.Asked.Contains(p.thingIDNumber))
                        .OrderByDescending(p => r.GetBond(p.thingIDNumber, actor.thingIDNumber)?.Trust ?? 0).FirstOrDefault();
                    if (recruit == null) { plot.State = PlotState.Abandoned; continue; }
                    if (Talk(actor, recruit, plot.Id)) { plot.LastActionTick = now; return true; }
                }
                else if (plot.State == PlotState.Ready && !target.Downed && actor.CanReach(target, PathEndMode.Touch, Danger.None))
                {
                    if (plot.Action == PlotAction.Fight)
                    {
                        if (Talk(actor, target, plot.Id)) { plot.LastActionTick = now; return true; }
                    }
                    else
                    {
                        plot.State = PlotState.Attempted; plot.LastActionTick = now;
                        Attack(actor, target, plot);
                        foreach (var id in plot.Recruits.Take(2))
                        {
                            var helper = c.TryGetPawn(id);
                            if (Available(helper) && helper.Map == map && helper.relations.OpinionOf(target) < 30 && helper.CanReach(target, PathEndMode.Touch, Danger.None)) Attack(helper, target, plot);
                        }
                        Messages.Message("WFL_PlotAttack".Translate(actor.LabelShort, target.LabelShort), actor, MessageTypeDefOf.ThreatSmall, true);
                        return true;
                    }
                }
            }
            if (map.mapPawns.AllPawnsSpawned.Any(p => !p.Downed && !p.IsPrisoner && p.HostileTo(Faction.OfPlayer))) return false;
            var free = map.mapPawns.FreeColonistsSpawned.Where(Available).Take(64).ToList();
            foreach (var incident in r.Incidents.Where(i=>!i.Resolved && i.MapId==map.uniqueID && i.ActorId!=0 && now-i.LastRepairTick>=ActivityTuning.TicksPerDay).OrderByDescending(i=>i.Tick).Take(8))
            {
                var other=c.TryGetPawn(incident.ActorId);
                if(!Available(other) || other.Map!=map) continue;
                var speakers=free.Where(p=>p!=other && r.FamilyOf(p.thingIDNumber)==incident.VictimFamily && incident.KnownBy.Contains(p.thingIDNumber) &&
                    (c.ProfileOf(p.thingIDNumber)?[(int)Dim.Warmth]??0)>.4f && Activities.AttemptAllowed(p.thingIDNumber,now,ActivityTuning.TicksPerDay,10));
                if(incident.RepairSteps>=2) speakers=speakers.Where(p=>r.CanManageHouse(p.thingIDNumber,incident.VictimFamily));
                var speaker=speakers.OrderByDescending(p=>r.CanManageHouse(p.thingIDNumber,incident.VictimFamily)).FirstOrDefault();
                if(speaker==null || !speaker.CanReach(other,PathEndMode.Touch,Danger.None)) continue;
                var talk=JobMaker.MakeJob(WFL_StoryDefOf.WFL_ReconcileStory,other);talk.count=incident.Id;
                Activities.NoteAttempt(speaker.thingIDNumber,now,10);
                speaker.jobs.StartJob(talk,JobCondition.InterruptForced,null,false,tag:JobTag.Misc);return true;
            }
            var independent = free.FirstOrDefault(p => r.CanTakePersonalTime(p.thingIDNumber,now));
            if (independent != null)
            {
                r.AgencyOf(independent.thingIDNumber).LastActionTick = now;
                independent.jobs.StartJob(JobMaker.MakeJob(WFL_StoryDefOf.WFL_PersonalTime), JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
                return true;
            }
            if (FamilyLifeMod.Settings.familyViolence)
                foreach (var i in r.Incidents.Where(i => !i.Resolved && i.MapId == map.uniqueID && i.ActorId != 0).OrderByDescending(i => i.Severity).Take(8))
                {
                    var actor = free.FirstOrDefault(p => i.KnownBy.Contains(p.thingIDNumber) && r.FamilyOf(p.thingIDNumber) == i.VictimFamily && (c.ProfileOf(p.thingIDNumber)?[(int)Dim.Warmth] ?? 0) < -0.3f &&
                        (c.ProfileOf(p.thingIDNumber)?[(int)Dim.Steadiness] ?? 0) < 0 && (c.TryGetPawn(i.ActorId) is Pawn culprit && p.relations.OpinionOf(culprit) < 30));
                    if (actor == null || c.TryGetPawn(i.ActorId) == null || Rand.Value > 0.05f) continue;
                    var action = FamilyLifeMod.Settings.lethalPlots && i.Kind == IncidentKind.Death && (r.GetTie(i.ActorFamily, i.VictimFamily)?.Grievance ?? 0) >= 70 ? PlotAction.Lethal : PlotAction.Fight;
                    var plot = r.ProposeRevenge(i.Id, actor.thingIDNumber, now, action);
                    if (plot != null) { Messages.Message("WFL_PlotBegan".Translate(actor.LabelShort, FamilyUi.NameOf(r, plot.TargetId)), actor, MessageTypeDefOf.NegativeEvent, false); return true; }
                }
            // News travels through actual conversations, not automatically through a family-wide opinion change.
            foreach (var i in r.Incidents.Where(i => !i.Resolved && i.MapId == map.uniqueID).OrderByDescending(i => i.Tick).Take(8))
            {
                var speaker = free.FirstOrDefault(p => i.KnownBy.Contains(p.thingIDNumber) && Activities.AttemptAllowed(p.thingIDNumber, now, 6000, 9));
                if (speaker == null) continue;
                var listener = free.FirstOrDefault(p => p != speaker && !i.KnownBy.Contains(p.thingIDNumber) && p.Position.DistanceTo(speaker.Position) <= 14 &&
                    (i.Kind == IncidentKind.PlotExposed && p.thingIDNumber == i.VictimId || r.FamilyOf(p.thingIDNumber) == r.FamilyOf(speaker.thingIDNumber) || (r.GetBond(p.thingIDNumber, speaker.thingIDNumber)?.Trust ?? 0) >= 50));
                if (listener != null && Talk(speaker, listener, -i.Id)) { Activities.NoteAttempt(speaker.thingIDNumber, now, 9); return true; }
            }
            return false;
        }
        private static void Attack(Pawn actor, Pawn target, RevengePlot plot)
        {
            var job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            plot.AttackJobs.Add(job.loadID);
            job.killIncappedTarget = true; job.expiryInterval = 3000; job.maxNumMeleeAttacks = 12;
            actor.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc);
        }
        private static bool Talk(Pawn actor, Pawn other, int story)
        {
            if (!actor.CanReach(other, PathEndMode.Touch, Danger.None)) return false;
            var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_StoryTalk, other); job.count = story;
            actor.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc); return true;
        }
        internal static void FinishTalk(Pawn actor, Pawn other, int story, bool reconciliation = false)
        {
            var c = FamilyComponent.Current;
            if (c == null || actor.Map != other.Map || actor.Position.DistanceTo(other.Position) > 3) return;
            if(reconciliation)
            {
                bool consent=(c.ProfileOf(other.thingIDNumber)?[(int)Dim.Warmth]??0)>=0 && (c.Registry.GetBond(other.thingIDNumber,actor.thingIDNumber)?.Tension??0)<70;
                c.Registry.DiscussReparation(story,actor.thingIDNumber,other.thingIDNumber,consent,Activities.Now);
                return;
            }
            if (story < 0 && c.Registry.Incidents.Any(i => i.Id == -story && i.KnownBy.Contains(actor.thingIDNumber))) { c.Registry.LearnIncident(-story, other.thingIDNumber, Activities.Now); return; }
            var plot = c.Registry.Plots.FirstOrDefault(p => p.Id == story && p.OrganizerId == actor.thingIDNumber);
            if (plot == null || c.Registry.Incidents.FirstOrDefault(i => i.Id == plot.IncidentId)?.Resolved != false) return;
            if (plot.State == PlotState.Recruiting)
            {
                var profile = c.ProfileOf(other.thingIDNumber);
                var response = c.Registry.AnswerRecruitment(plot, other.thingIDNumber, profile?[(int)Dim.Warmth] ?? 0, profile?[(int)Dim.Steadiness] ?? 0, Activities.Now);
                Messages.Message((response == RecruitResponse.Warn ? "WFL_PlotWarned" : response == RecruitResponse.Join ? "WFL_PlotJoined" : "WFL_PlotRefused").Translate(actor.LabelShort, other.LabelShort), other, MessageTypeDefOf.NeutralEvent, false);
            }
            else if (plot?.State == PlotState.Ready && plot.TargetId == other.thingIDNumber && FamilyLifeMod.Settings.familyViolence && actor.interactions.SocialFightPossible(other))
            {
                plot.State = PlotState.Attempted; plot.LastActionTick = Activities.Now;
                actor.interactions.StartSocialFight(other, "MessageSocialFight");
            }
        }
    }
}
