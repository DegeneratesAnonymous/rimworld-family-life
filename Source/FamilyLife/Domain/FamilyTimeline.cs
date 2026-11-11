using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // The Events tab's content: what is coming up, what has just happened, and what has become part of the family's permanent history.
    // Read-only views over records the registry already keeps, filtered by what the person looking is able to know.

    public enum UpcomingKind : byte { Ceremony, Tradition, Promise, KinVisit, CampTrip, Marriage }

    public sealed class UpcomingItem
    {
        public UpcomingKind Kind;
        public int Tick;
        public CeremonyKind Ceremony;
        public string TraditionId = "";
        public int RefId, PersonA, PersonB, Count = 1;
        public bool Overdue;
        public bool Planned;                  // there is a plan with a chosen time, not only a wish
    }

    public enum TimelineKind : byte { Ceremony, Moment, Incident, Plot, Chronicle }

    public sealed class TimelineEntry
    {
        public TimelineKind Kind;
        public int Tick;
        public EventCategory Category;
        public bool Defining;
        public EventRecord Moment;
        public FamilyEvent Chronicle;
        public CeremonyRecord Ceremony;
        public FamilyIncident Incident;
        public RevengePlot Plot;
    }

    public static class FamilyTimeline
    {
        public const int RecentWindow = 15 * 60000;       // about fifteen days
        public const int MaxEntries = 120;

        public static List<UpcomingItem> Upcoming(FamilyRegistry r, Lineage house, int viewer, int now)
        {
            var list = new List<UpcomingItem>();
            if (r == null || house == null || !FamilyWindowFacts.IsInsider(r, house.Id, viewer)) return list;
            int id = house.Id;
            var people = new HashSet<int>(r.MembersOf(id).Where(p => p.InColony).Select(p => p.Id));

            foreach (var request in r.GatheringRequests)
            {
                if (request.LineageId != id && request.SecondLineageId != id) continue;
                if (request.State != CeremonyState.Planned && request.State != CeremonyState.Active) continue;
                var plan = r.PlanFor(request.Id);
                if (plan != null && plan.State == PlanState.Cancelled) continue;
                if (plan != null && !r.PlanVisibleTo(plan, viewer)) continue;
                list.Add(new UpcomingItem
                {
                    Kind = request.Ceremony == CeremonyKind.Tradition ? UpcomingKind.Tradition : UpcomingKind.Ceremony,
                    Tick = r.ScheduledStart(request), Ceremony = request.Ceremony, TraditionId = request.Ceremony == CeremonyKind.Tradition ? request.Kind : "",
                    RefId = request.Id, PersonA = request.SubjectId, PersonB = request.SubjectBId, Planned = plan != null && plan.State != PlanState.Open
                });
            }
            foreach (var c in r.Commitments)
            {
                if (!c.IsOpen || !(people.Contains(c.FromId) || people.Contains(c.ToId))) continue;
                if (viewer != 0 && c.FromId != viewer && c.ToId != viewer) continue;
                list.Add(new UpcomingItem { Kind = UpcomingKind.Promise, Tick = c.DueTick, RefId = c.Id, PersonA = c.FromId, PersonB = c.ToId, Overdue = c.DueTick < now });
            }
            foreach (var visit in r.KinVisits)
                if (visit.FamilyId == id && visit.Open)
                    list.Add(new UpcomingItem { Kind = UpcomingKind.KinVisit, Tick = visit.State == KinVisitState.Travelling ? visit.ArriveTick : visit.StayEndTick, RefId = visit.Id, Count = visit.KinIds.Count });
            foreach (var trip in r.CampTrips)
                if (trip.FamilyId == id && trip.Open)
                    list.Add(new UpcomingItem { Kind = UpcomingKind.CampTrip, Tick = trip.State == CampState.Proposed ? trip.ExpiresTick : trip.AuthorizedTick, RefId = trip.Id, PersonA = trip.OrganizerId });
            foreach (var a in r.MarriageAlliances)
            {
                if ((a.FamilyA != id && a.FamilyB != id) || !r.KnowsMarriageAlliance(viewer, a)) continue;
                if (a.State == MarriageAllianceState.Expired || a.State == MarriageAllianceState.Withdrawn || a.State == MarriageAllianceState.Fulfilled) continue;
                list.Add(new UpcomingItem { Kind = UpcomingKind.Marriage, Tick = a.ExpiresTick, RefId = a.Id, PersonA = a.PawnA, PersonB = a.PawnB });
            }
            return list.OrderBy(u => u.Overdue ? 0 : 1).ThenBy(u => u.Tick).ToList();
        }

        /// <summary>Whether a chronicle entry is about this house or one of its people, as the old History tab decided it.</summary>
        public static bool ChronicleAbout(FamilyEvent e, int lineageId, ISet<int> relevant)
        {
            switch (e.Kind)
            {
                case FamilyEventKind.PersonRecorded: case FamilyEventKind.BondDeepened: case FamilyEventKind.BondCooled: case FamilyEventKind.MarriageFormed: return false;
                case FamilyEventKind.RecordsBegan: return true;
                case FamilyEventKind.LineageFounded: case FamilyEventKind.LineageAffiliated: case FamilyEventKind.SocialBranch: case FamilyEventKind.SocialMembership: return e.B == lineageId;
                case FamilyEventKind.TraditionAdopted: case FamilyEventKind.TraditionAbandoned: case FamilyEventKind.TraditionHeld: return e.A == lineageId;
                default:
                    bool pair = e.Kind == FamilyEventKind.PartnershipFormed || e.Kind == FamilyEventKind.PartnershipEnded || e.Kind == FamilyEventKind.Birth
                        || e.Kind == FamilyEventKind.Anniversary || e.Kind == FamilyEventKind.RiftOpened || e.Kind == FamilyEventKind.RiftResolved;
                    return relevant.Contains(e.A) || (pair && relevant.Contains(e.B));
            }
        }

        /// <summary>The last stretch of the family's story: ceremonies, meaningful moments, incidents and plots the viewer knows, and the chronicle's own recent entries.</summary>
        public static List<TimelineEntry> Recent(FamilyRegistry r, Lineage house, int viewer, int now, int mask)
        {
            var list = new List<TimelineEntry>();
            if (r == null || house == null) return list;
            int id = house.Id;
            var relevant = Relevant(r, house);
            int from = Math.Max(0, now - RecentWindow);

            foreach (var c in r.Ceremonies)
            {
                if ((c.LineageId != id && c.SecondLineageId != id) || c.CompletedTick < from) continue;
                var cat = EventCategory.Ceremonies | (c.Kind == CeremonyKind.Tradition ? EventCategory.Traditions : EventCategory.None) | (c.Kind == CeremonyKind.Assembly ? EventCategory.Leadership : EventCategory.None);
                if (viewer != 0 && !FamilyWindowFacts.IsInsider(r, id, viewer)) continue;
                if (EventFilters.Matches(mask, cat)) list.Add(new TimelineEntry { Kind = TimelineKind.Ceremony, Tick = c.CompletedTick, Category = cat, Ceremony = c });
            }
            for (int i = r.EventLog.Count - 1; i >= 0 && list.Count < MaxEntries * 3; i--)
            {
                var e = r.EventLog[i];
                if (e.Tick < from) break;
                if (e.Weight == EventWeight.Ambient) continue;
                if (!relevant.Contains(e.A) && !relevant.Contains(e.B)) continue;
                if (viewer != 0 && e.A != viewer && e.B != viewer && e.Third != viewer) continue;
                var cat = EventFilters.OfMoment(false, false, e.Tier);
                if (EventFilters.Matches(mask, cat)) list.Add(new TimelineEntry { Kind = TimelineKind.Moment, Tick = e.Tick, Category = cat, Moment = e, Defining = e.Weight == EventWeight.Defining });
            }
            foreach (var inc in r.Incidents)
            {
                if ((inc.ActorFamily != id && inc.VictimFamily != id) || !r.KnowsIncident(viewer, inc) || inc.Tick < from) continue;
                if (EventFilters.Matches(mask, EventCategory.Conflict)) list.Add(new TimelineEntry { Kind = TimelineKind.Incident, Tick = inc.Tick, Category = EventCategory.Conflict, Incident = inc });
            }
            foreach (var plot in r.Plots)
            {
                if (!(relevant.Contains(plot.OrganizerId) || relevant.Contains(plot.TargetId)) || !r.KnowsPlot(viewer, plot) || plot.CreatedTick < from) continue;
                if (EventFilters.Matches(mask, EventCategory.Conflict)) list.Add(new TimelineEntry { Kind = TimelineKind.Plot, Tick = plot.CreatedTick, Category = EventCategory.Conflict, Plot = plot });
            }
            for (int i = r.Chronicle.Count - 1; i >= 0; i--)
            {
                var e = r.Chronicle[i];
                if (e.Tick < from) break;
                if (!ChronicleAbout(e, id, relevant)) continue;
                var cat = EventFilters.Of(e.Kind);
                if (EventFilters.Matches(mask, cat)) list.Add(new TimelineEntry { Kind = TimelineKind.Chronicle, Tick = e.Tick, Category = cat, Chronicle = e });
            }
            return list.OrderByDescending(t => t.Tick).Take(MaxEntries).ToList();
        }

        /// <summary>Only permanent or defining history: the chronicle itself, and moments that became a defining memory.</summary>
        public static List<TimelineEntry> Chronicle(FamilyRegistry r, Lineage house, int viewer, int mask)
        {
            var list = new List<TimelineEntry>();
            if (r == null || house == null) return list;
            var relevant = Relevant(r, house);
            for (int i = r.Chronicle.Count - 1; i >= 0 && list.Count < 300; i--)
            {
                var e = r.Chronicle[i];
                if (!ChronicleAbout(e, house.Id, relevant)) continue;
                var cat = EventFilters.Of(e.Kind);
                if (EventFilters.Matches(mask, cat)) list.Add(new TimelineEntry { Kind = TimelineKind.Chronicle, Tick = e.Tick, Category = cat, Chronicle = e });
            }
            for (int i = r.EventLog.Count - 1; i >= 0 && list.Count < 400; i--)
            {
                var e = r.EventLog[i];
                if (e.Weight != EventWeight.Defining) continue;
                if (!relevant.Contains(e.A) && !relevant.Contains(e.B)) continue;
                if (viewer != 0 && e.A != viewer && e.B != viewer && e.Third != viewer) continue;
                var cat = EventFilters.OfMoment(false, false, e.Tier);
                if (EventFilters.Matches(mask, cat)) list.Add(new TimelineEntry { Kind = TimelineKind.Moment, Tick = e.Tick, Category = cat, Moment = e, Defining = true });
            }
            return list.OrderByDescending(t => t.Tick).Take(300).ToList();
        }

        public static HashSet<int> Relevant(FamilyRegistry r, Lineage house)
        {
            var set = new HashSet<int>(r.MembersOf(house.Id).Select(p => p.Id));
            foreach (var a in house.Affiliations) set.Add(a.PersonId);
            return set;
        }
    }
}
