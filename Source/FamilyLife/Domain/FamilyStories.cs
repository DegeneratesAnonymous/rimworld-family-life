using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum IncidentKind : byte { Injury, Death, Theft, Gift, Refusal, Intimidation, Restitution, PlotExposed, Insult }
    [Flags] public enum IncidentContext : byte { None = 0, Combat = 1, MentalBreak = 2, Accident = 4, Planned = 8 }
    public enum PlotState : byte { Recruiting, Ready, Attempted, Completed, Abandoned, Exposed }
    public enum PlotAction : byte { Fight, Lethal }
    public enum RecruitResponse : byte { Join, Refuse, Warn }

    public sealed partial class FamilyIncident
    {
        public int Id, ActorId, VictimId, ActorFamily, VictimFamily, MapId, Tick, Amount, Restored;
        public string Resource = "";
        public IncidentKind Kind;
        public IncidentContext Context;
        public float Severity;
        public bool Resolved;
        public List<int> KnownBy = new List<int>();
        public List<int> FamiliesAffected = new List<int>();
    }

    public sealed partial class RevengePlot
    {
        public int Id, IncidentId, OrganizerId, TargetId, CreatedTick, LastActionTick;
        public PlotAction Action;
        public PlotState State;
        public List<int> Recruits = new List<int>();
        public List<int> Asked = new List<int>();
        public List<int> AttackJobs = new List<int>();
        public bool Active => State <= PlotState.Attempted;
    }

    /// <summary>Four independent changes, directed from one person to another.</summary>
    public sealed class BondChange
    {
        public readonly float Attachment, Trust, Tension, Familiarity;
        public BondChange(float attachment, float trust, float tension, float familiarity)
        { Attachment = attachment; Trust = trust; Tension = tension; Familiarity = familiarity; }
        public float Feeling => Math.Max(-0.8f, Math.Min(0.8f, (Attachment + Trust - Tension) / 10f));
    }
    public sealed class MomentOutcome
    {
        public readonly BondChange Initiator, Recipient;
        public MomentOutcome(BondChange initiator, BondChange recipient) { Initiator = initiator; Recipient = recipient; }
    }

    public sealed partial class FamilyRegistry
    {
        public List<FamilyIncident> Incidents = new List<FamilyIncident>();
        public List<RevengePlot> Plots = new List<RevengePlot>();
        public int NextIncidentId = 1, NextPlotId = 1;

        public int FamilyOf(int personId)
        {
            var p = GetPerson(personId);
            return p == null ? 0 : LineageIdFor(p);
        }

        public void ChangeBond(int from, int to, BondChange change, int now, float scale = 1f)
        {
            if (from == to || GetPerson(from) == null || GetPerson(to) == null || change == null) return;
            var b = EnsureBond(from, to, now);
            b.Attachment = BondTuning.Clamp(b.Attachment + change.Attachment * scale);
            b.Trust = BondTuning.Clamp(b.Trust + change.Trust * scale);
            b.Tension = BondTuning.Clamp(b.Tension + change.Tension * scale);
            b.Familiarity = BondTuning.Clamp(b.Familiarity + change.Familiarity * scale);
            b.Note(change.Tension > 0 || change.Trust < 0 ? BondCause.Friction : BondCause.Together, (change.Attachment + change.Trust - change.Tension) * scale / 3f, now);
        }

        public FamilyIncident RecordIncident(IncidentKind kind, int actor, int victim, int map, int now, float severity,
            IEnumerable<int> witnesses, IncidentContext context = IncidentContext.None, int amount = 0)
        {
            EnsureIndexes();
            if (GetPerson(victim) == null || actor == victim) return null;
            // One injury incident per pair per quarter-day, not one grievance per bullet.
            var incident = kind == IncidentKind.Injury ? Incidents.LastOrDefault(i => i.Kind == kind && i.ActorId == actor && i.VictimId == victim && now >= i.Tick && now - i.Tick < ActivityTuning.TicksPerDay / 4) : null;
            if (incident == null)
            {
                if (Incidents.Count >= 512)
                {
                    var drop = Incidents.Where(i => !Plots.Any(p => p.Active && p.IncidentId == i.Id) && !Requests.Any(r => r.Open && r.RootIncidentId == i.Id))
                        .OrderBy(i => !i.Resolved).ThenBy(i => i.Severity).ThenBy(i => i.Tick).FirstOrDefault();
                    if (drop == null) return null;
                    Incidents.Remove(drop);
                }
                incident = new FamilyIncident { Id = NextIncidentId++, Kind = kind, ActorId = GetPerson(actor) == null ? 0 : actor,
                    VictimId = victim, ActorFamily = FamilyOf(actor), VictimFamily = FamilyOf(victim), MapId = map, Tick = now,
                    Severity = Math.Max(0f, Math.Min(100f, severity)), Context = context, Amount = Math.Max(0, amount) };
                Incidents.Add(incident);
            }
            foreach (int id in (witnesses ?? Enumerable.Empty<int>()).Append(actor).Where(id => kind != IncidentKind.Death || id != victim).Distinct().Take(64)) LearnIncident(incident.Id, id, now);
            return incident;
        }

        public bool LearnIncident(int incidentId, int observerId, int now)
        {
            var i = Incidents.FirstOrDefault(x => x.Id == incidentId);
            var observer = GetPerson(observerId);
            if (i == null || observer == null || !observer.IsAlive || i.KnownBy.Contains(observerId)) return false;
            if (i.KnownBy.Count >= 64) return false;
            i.KnownBy.Add(observerId);
            if (i.ActorId == 0 || observerId == i.ActorId || i.Resolved) return true;
            bool involved = observerId == i.VictimId || FamilyOf(observerId) == i.VictimFamily || (GetBond(observerId, i.VictimId)?.Attachment ?? 0) >= 40;
            if (!involved) return true;
            bool good = i.Kind == IncidentKind.Gift || i.Kind == IncidentKind.Restitution;
            float blame = (i.Context & IncidentContext.Accident) != 0 ? 0.4f : ((i.Context & IncidentContext.MentalBreak) != 0 ? 0.65f : 1f);
            float size = i.Severity / 10f * (good ? 1f : blame);
            ChangeBond(observerId, i.ActorId, good ? new BondChange(size * 0.4f, size, -size, 1) : new BondChange(-size * 0.4f, -size, size * 1.5f, 1), now);
            int family = FamilyOf(observerId);
            if (family != 0 && family == i.VictimFamily && i.ActorFamily != 0 && family != i.ActorFamily && !i.FamiliesAffected.Contains(family))
            {
                i.FamiliesAffected.Add(family);
                var tie = EnsureTie(family, i.ActorFamily);
                if (tie != null)
                {
                    tie.Shared++; tie.LastTick = now; tie.Familiarity = BondTuning.Clamp(tie.Familiarity + 2);
                    if (good) { tie.Goodwill = BondTuning.Clamp(tie.Goodwill + size); tie.Grievance = Math.Max(0, tie.Grievance - size); }
                    else { tie.Grievance = BondTuning.Clamp(tie.Grievance + size * 6); tie.WrongedBy = i.ActorId; tie.WrongedPerson = i.VictimId; tie.GrievanceTick = now; }
                }
            }
            return true;
        }

        public void ForgiveIncident(int incidentId, int familyId)
        {
            var i = Incidents.FirstOrDefault(x => x.Id == incidentId && x.VictimFamily == familyId);
            if (i == null || i.Resolved) return;
            i.Resolved = true;
            foreach (var plot in Plots.Where(p => p.Active && p.IncidentId == i.Id)) plot.State = PlotState.Abandoned;
            var tie = GetTie(i.ActorFamily, i.VictimFamily);
            if (tie != null) tie.Grievance = Math.Max(0, tie.Grievance - i.Severity * 0.3f);
        }

        public RevengePlot ProposeRevenge(int incidentId, int organizer, int now, PlotAction action)
        {
            var i = Incidents.FirstOrDefault(x => x.Id == incidentId);
            var p = GetPerson(organizer);
            if (i == null || i.Resolved || i.ActorId == 0 || i.ActorId == organizer || p == null || !p.InColony || p.Juvenile || GetPerson(i.ActorId)?.InColony != true ||
                i.ActorFamily == 0 || i.VictimFamily == 0 || i.ActorFamily == i.VictimFamily || FamilyOf(organizer) != i.VictimFamily || !i.KnownBy.Contains(organizer) ||
                (action == PlotAction.Lethal ? i.Kind != IncidentKind.Death && i.Severity < 70 : i.Severity < 20) || (GetTie(i.ActorFamily, i.VictimFamily)?.Grievance ?? 0) < 45 ||
                (GetBond(organizer, i.ActorId)?.Attachment ?? 0) >= 50 || Plots.Any(x => x.Active && x.OrganizerId == organizer) ||
                Plots.Any(x => x.IncidentId == i.Id && now - x.CreatedTick < 3 * ActivityTuning.TicksPerDay)) return null;
            var plot = new RevengePlot { Id = NextPlotId++, IncidentId = i.Id, OrganizerId = organizer, TargetId = i.ActorId, CreatedTick = now, LastActionTick = now, Action = action };
            Plots.Add(plot);
            while (Plots.Count > 64)
            {
                var old = Plots.FirstOrDefault(x => !x.Active);
                if (old == null) { Plots.Remove(plot); return null; }
                Plots.Remove(old);
            }
            return plot;
        }

        public RecruitResponse AnswerRecruitment(RevengePlot plot, int candidate, float warmth, float steadiness, int now)
        {
            var person = GetPerson(candidate);
            if (plot == null || plot.State != PlotState.Recruiting || Incidents.FirstOrDefault(i => i.Id == plot.IncidentId)?.Resolved != false || person?.InColony != true || person.Juvenile || candidate == plot.TargetId || candidate == plot.OrganizerId ||
                FamilyOf(candidate) != FamilyOf(plot.OrganizerId) || plot.Asked.Contains(candidate)) return RecruitResponse.Refuse;
            plot.Asked.Add(candidate); plot.LastActionTick = now;
            LearnIncident(plot.IncidentId, candidate, now);
            float targetLove = GetBond(candidate, plot.TargetId)?.Attachment ?? 0;
            if (targetLove >= 45 || warmth > 0.5f)
            {
                plot.State = PlotState.Exposed;
                RecordIncident(IncidentKind.PlotExposed, plot.OrganizerId, plot.TargetId, Incidents.First(x => x.Id == plot.IncidentId).MapId, now, 25, new[] { candidate });
                return RecruitResponse.Warn;
            }
            if ((GetBond(candidate, plot.OrganizerId)?.Trust ?? 0) >= 35 && warmth < 0.1f && steadiness < 0.4f)
            { plot.Recruits.Add(candidate); plot.State = PlotState.Ready; return RecruitResponse.Join; }
            plot.State = PlotState.Abandoned;
            return RecruitResponse.Refuse;
        }

        public void MaintainStories(int now)
        {
            foreach (var plot in Plots.Where(p => p.Active))
                if (plot.State == PlotState.Attempted && Incidents.Any(i => i.Kind == IncidentKind.Death && i.VictimId == plot.TargetId && i.Tick >= plot.LastActionTick && (i.Context & IncidentContext.Planned) != 0 && (i.ActorId == plot.OrganizerId || plot.Recruits.Contains(i.ActorId)))) plot.State = PlotState.Completed;
                else if (GetPerson(plot.OrganizerId)?.InColony != true || GetPerson(plot.TargetId)?.IsAlive != true ||
                    Incidents.FirstOrDefault(i => i.Id == plot.IncidentId)?.Resolved != false || now - plot.CreatedTick > 3 * ActivityTuning.TicksPerDay)
                    plot.State = PlotState.Abandoned;
        }

        private void RepairStories()
        {
            Agency = (Agency ?? new List<AgencyRecord>()).Where(a => a != null && personById.ContainsKey(a.PersonId)).GroupBy(a => a.PersonId).Select(g => g.First()).ToList();
            Incidents = Incidents ?? new List<FamilyIncident>(); Plots = Plots ?? new List<RevengePlot>();
            Claims = Claims ?? new List<StockClaim>(); Requests = Requests ?? new List<SupplyRequest>();
            Incidents.RemoveAll(i => i == null || !personById.ContainsKey(i.VictimId));
            Plots.RemoveAll(p => p == null || !personById.ContainsKey(p.OrganizerId) || !Incidents.Any(i => i.Id == p.IncidentId));
            Claims.RemoveAll(c => c == null || !lineageById.ContainsKey(c.OwnerFamily));
            claimByThing.Clear();
            Requests.RemoveAll(r => r == null || !personById.ContainsKey(r.RequesterId));
            foreach (var i in Incidents) { i.KnownBy = (i.KnownBy ?? new List<int>()).Distinct().Take(64).ToList(); i.FamiliesAffected = i.FamiliesAffected ?? new List<int>(); NextIncidentId = Math.Max(NextIncidentId, i.Id + 1); }
            foreach (var p in Plots) { p.AttackJobs = p.AttackJobs ?? new List<int>(); p.Asked = p.Asked ?? new List<int>(); p.Recruits = p.Recruits ?? new List<int>(); NextPlotId = Math.Max(NextPlotId, p.Id + 1); }
            foreach (var r in Requests) NextSupplyRequestId = Math.Max(NextSupplyRequestId, r.Id + 1);
        }
    }
}
