using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // The Families window's logic that needs no game: which tab holds what, what needs attention, how relationships and events are
    // filtered and sorted, what a supply reading means, and which offices a given observer may see. The window only draws the answers.
    // Nothing here is saved. Everything is worked out from the registry's existing records; no new simulation is created for the window.

    public enum FamilyTab : byte { Summary, People, Relationships, Story, Home, HouseAffairs }
    public enum StorySub : byte { Current, Timeline, Chronicle }
    public enum HomeSub : byte { Households, Supplies, Area }
    public enum PeopleView : byte { Portraits, Tree }

    /// <summary>Where each piece of the old eight-tab window now lives. Nothing was removed, so every old section resolves to a place.</summary>
    public static class FamilyWindowLayout
    {
        public struct Place { public FamilyTab Tab; public int Sub; public Place(FamilyTab tab, int sub = 0) { Tab = tab; Sub = sub; } }

        private static readonly Dictionary<string, Place> legacy = new Dictionary<string, Place>
        {
            // Overview
            { "overview.founded", new Place(FamilyTab.Summary) }, { "overview.ambition", new Place(FamilyTab.HouseAffairs) },
            { "overview.tree", new Place(FamilyTab.People) }, { "overview.culture", new Place(FamilyTab.Story, (int)StorySub.Chronicle) },
            // Relationships
            { "relationships", new Place(FamilyTab.Relationships) },
            // Moments
            { "moments.ceremonies", new Place(FamilyTab.Story, (int)StorySub.Current) }, { "moments.prestige", new Place(FamilyTab.HouseAffairs) },
            { "moments.agency", new Place(FamilyTab.People) }, { "moments.attention", new Place(FamilyTab.Relationships) },
            { "moments.estates", new Place(FamilyTab.HouseAffairs) }, { "moments.standing", new Place(FamilyTab.HouseAffairs) },
            { "moments.incidents", new Place(FamilyTab.Story, (int)StorySub.Timeline) }, { "moments.plots", new Place(FamilyTab.Story, (int)StorySub.Timeline) },
            { "moments.supplies", new Place(FamilyTab.Story, (int)StorySub.Timeline) }, { "moments.promises", new Place(FamilyTab.Story, (int)StorySub.Current) },
            { "moments.recent", new Place(FamilyTab.Story, (int)StorySub.Timeline) },
            // Households
            { "households", new Place(FamilyTab.Home, (int)HomeSub.Households) },
            // House (Manage)
            { "manage.petitions", new Place(FamilyTab.HouseAffairs) }, { "manage.alliances", new Place(FamilyTab.HouseAffairs) },
            { "manage.stewardship", new Place(FamilyTab.HouseAffairs) }, { "manage.governance", new Place(FamilyTab.HouseAffairs) },
            { "manage.associates", new Place(FamilyTab.HouseAffairs) }, { "manage.crest", new Place(FamilyTab.HouseAffairs) },
            // Zone
            { "zone.pantry", new Place(FamilyTab.Home, (int)HomeSub.Supplies) }, { "zone.area", new Place(FamilyTab.Home, (int)HomeSub.Area) },
            // Traditions, History
            { "traditions", new Place(FamilyTab.Story, (int)StorySub.Current) }, { "history", new Place(FamilyTab.Story, (int)StorySub.Chronicle) }
        };

        public static IReadOnlyCollection<string> LegacySections => legacy.Keys;
        public static bool TryPlace(string legacySection, out Place place) => legacy.TryGetValue(legacySection, out place);

        /// <summary>The old tab name (as the previous version saved it nowhere, but as old code and docs call it) to its new place.</summary>
        public static Place FromLegacyTab(string oldTab)
        {
            switch (oldTab)
            {
                case "Relationships": return new Place(FamilyTab.Relationships);
                case "Moments": return new Place(FamilyTab.Story, (int)StorySub.Timeline);
                case "Traditions": return new Place(FamilyTab.Story, (int)StorySub.Current);
                case "History": return new Place(FamilyTab.Story, (int)StorySub.Chronicle);
                case "Households": return new Place(FamilyTab.Home, (int)HomeSub.Households);
                case "Zone": return new Place(FamilyTab.Home, (int)HomeSub.Area);
                case "Manage": return new Place(FamilyTab.HouseAffairs);
                default: return new Place(FamilyTab.Summary);
            }
        }

        public static int SubCount(FamilyTab tab) => tab == FamilyTab.Story ? 3 : tab == FamilyTab.Home ? 3 : 0;
    }

    /// <summary>What the window remembers between visits: the family, the tab, the sub-tab, the filters. Held for the session, never saved.</summary>
    public sealed class FamilyWindowState
    {
        public int LineageId;
        public FamilyTab Tab = FamilyTab.Summary;
        public int EventsSub, HomeSub;
        public PeopleView PeopleView = PeopleView.Portraits;
        public int SelectedPerson;           // the person whose detail panel is open (People), or whose relationships are shown (Relationships)
        public int SelectedRelation;         // the other person in an open relationship detail
        public int EventFilter;              // EventCategory mask; 0 = everything
        public int SelectedHousehold;        // the household whose detail is open on Home
        public int StoryPerson;              // the Timeline shows only moments this person took part in; 0 = everyone
        public bool ShowRoutine;             // the Timeline lists everyday matters too
        public RelFilter RelFilter = RelFilter.Family;
        public RelSort RelSort = RelSort.Closest;
        public FamilySort SidebarSort = FamilySort.Name;
        public bool SidebarCollapsed;
        public string Search = "", RelSearch = "";

        public int Sub(FamilyTab tab) => tab == FamilyTab.Story ? EventsSub : tab == FamilyTab.Home ? HomeSub : 0;

        public void Navigate(FamilyTab tab, int sub = 0)
        {
            Tab = tab;
            if (tab == FamilyTab.Story) EventsSub = Clamp(sub, 3);
            else if (tab == FamilyTab.Home) HomeSub = Clamp(sub, 3);
        }

        public void Select(int lineageId)
        {
            if (lineageId == LineageId) return;
            LineageId = lineageId; SelectedPerson = 0; SelectedRelation = 0;
        }

        /// <summary>Follows an alert to the content it is about.</summary>
        public void Go(FamilyAlert alert)
        {
            if (alert == null) return;
            Navigate(alert.Tab, alert.Sub);
            if (alert.PersonId != 0) { SelectedPerson = alert.PersonId; SelectedRelation = 0; }
        }

        private static int Clamp(int v, int count) => v < 0 ? 0 : v >= count ? count - 1 : v;
    }

    // ------------------------------------------------------------------ what needs attention

    public enum AlertKind : byte
    {
        NoHead, NoSteward, FoodLow, HousingProblem, SafetyLow, CapabilityGap, PromiseDue, OpenRift, Grievance,
        CeremonyPrep, Petition, Negotiation
    }

    public sealed class FamilyAlert
    {
        public AlertKind Kind;
        /// <summary>0 for a note, 1 for something to look at, 2 for something urgent.</summary>
        public int Severity;
        public FamilyTab Tab;
        public int Sub;
        public int PersonId;
        /// <summary>How many things the alert stands for (promises due, open grievances), for its wording.</summary>
        public int Count = 1;
        public bool Urgent => Severity >= 2;
    }

    public static class FamilyAlerts
    {
        public const float FoodLowAt = 35f, FoodCriticalAt = 20f, SpaceLowAt = 35f, SafetyLowAt = 30f, CapabilityLowAt = 35f;
        public const int PromiseWindow = 60000;

        /// <summary>
        /// The few concerns this family has right now that existing records support, most urgent first. <paramref name="viewer"/> is the person
        /// being played (0 for the colony overview): what only the head would know, or what the viewer was never told, is not listed.
        /// </summary>
        public static List<FamilyAlert> Build(FamilyRegistry r, Lineage house, int viewer, int now)
        {
            var list = new List<FamilyAlert>();
            if (r == null || house == null) return list;
            int id = house.Id;
            if (!FamilyWindowFacts.IsInsider(r, id, viewer)) return list;       // what a house is worried about is its own business
            var colony = r.MembersOf(id).Where(p => p.InColony).ToList();
            var colonyIds = new HashSet<int>(colony.Select(p => p.Id));
            if (colony.Count == 0) return list;

            if (r.HeadOf(id) == 0 || house.VacantHeadId != 0)
                list.Add(new FamilyAlert { Kind = AlertKind.NoHead, Severity = 2, Tab = FamilyTab.HouseAffairs });
            else if (!house.Stewards.Any(s => colonyIds.Contains(s)) && colony.Count(p => !p.Juvenile) >= 3)
                list.Add(new FamilyAlert { Kind = AlertKind.NoSteward, Severity = 0, Tab = FamilyTab.HouseAffairs });

            var s = house.Situation;
            if (s != null && s.EvaluatedTick > 0)
            {
                if (s.Sustenance < FoodLowAt) list.Add(new FamilyAlert { Kind = AlertKind.FoodLow, Severity = s.Sustenance < FoodCriticalAt ? 2 : 1, Tab = FamilyTab.Home, Sub = (int)HomeSub.Supplies });
                if (s.LivingSpace < SpaceLowAt) list.Add(new FamilyAlert { Kind = AlertKind.HousingProblem, Severity = 1, Tab = FamilyTab.Home, Sub = (int)HomeSub.Households });
                if (s.Safety < SafetyLowAt) list.Add(new FamilyAlert { Kind = AlertKind.SafetyLow, Severity = 1, Tab = FamilyTab.HouseAffairs });
                if (s.Capability < CapabilityLowAt && s.WeakestCapability != HouseCapability.None) list.Add(new FamilyAlert { Kind = AlertKind.CapabilityGap, Severity = 0, Tab = FamilyTab.HouseAffairs });
            }

            int due = r.Commitments.Count(c => c.IsOpen && c.DueTick - now <= PromiseWindow
                && (colonyIds.Contains(c.FromId) || colonyIds.Contains(c.ToId)) && (viewer == 0 || c.FromId == viewer || c.ToId == viewer));
            if (due > 0) list.Add(new FamilyAlert { Kind = AlertKind.PromiseDue, Severity = 1, Tab = FamilyTab.Story, Sub = (int)StorySub.Current, Count = due });

            foreach (var member in colony)
            {
                if (viewer != 0 && member.Id != viewer) continue;           // a rift is a private matter between its two people
                var rift = r.OpenRiftsOf(member.Id).FirstOrDefault();
                if (rift != null) { list.Add(new FamilyAlert { Kind = AlertKind.OpenRift, Severity = rift.Severity >= 60f ? 2 : 1, Tab = FamilyTab.Relationships, PersonId = member.Id }); break; }
            }

            int open = r.Incidents.Count(i => !i.Resolved && i.VictimFamily == id && r.KnowsIncident(viewer, i));
            int strained = r.TiesOf(id).Count(t => t.State == FamilyTieState.Strained || t.State == FamilyTieState.Feuding);
            if (open + strained > 0) list.Add(new FamilyAlert { Kind = AlertKind.Grievance, Severity = strained > 0 && open > 0 ? 2 : 1, Tab = FamilyTab.Story, Sub = (int)StorySub.Timeline, Count = open + strained });

            int prep = r.CeremonyPlans.Count(p => p.FamilyId == id && p.State == PlanState.Open && r.PlanVisibleTo(p, viewer));
            if (prep > 0) list.Add(new FamilyAlert { Kind = AlertKind.CeremonyPrep, Severity = 1, Tab = FamilyTab.Story, Sub = (int)StorySub.Current, Count = prep });

            bool manages = viewer == 0 || r.CanManageHouse(viewer, id);
            int petitions = manages ? r.HousePetitions.Count(p => p.FamilyId == id && p.State == PetitionState.Proposed) : 0;
            if (petitions > 0) list.Add(new FamilyAlert { Kind = AlertKind.Petition, Severity = 1, Tab = FamilyTab.HouseAffairs, Count = petitions });

            int talks = r.MarriageAlliances.Count(a => (a.FamilyA == id || a.FamilyB == id) && r.KnowsMarriageAlliance(viewer, a)
                && (a.State == MarriageAllianceState.Negotiating || a.State == MarriageAllianceState.Supported || a.State == MarriageAllianceState.Opposed || a.State == MarriageAllianceState.Consented));
            if (talks > 0) list.Add(new FamilyAlert { Kind = AlertKind.Negotiation, Severity = 0, Tab = FamilyTab.HouseAffairs, Count = talks });

            return list.OrderByDescending(a => a.Severity).ThenBy(a => (int)a.Kind).ToList();
        }
    }

    // ------------------------------------------------------------------ the sidebar

    public enum FamilySort : byte { Name, Size, Standing, Attention }

    public sealed class FamilySummary
    {
        public int LineageId;
        public string Name = "";
        public int Members, Residents, Households, Alerts;
        public bool Urgent;
        /// <summary>The cohesion tier name ("Steady"), or null when the house has not been assessed yet.</summary>
        public string Tier;
        public float Cohesion = -1f;
        public AlertKind? FirstAlert;
        public List<AlertKind> AlertKinds = new List<AlertKind>();
    }

    public static class FamilySummaries
    {
        public static FamilySummary Of(FamilyRegistry r, Lineage house, int viewer, int now)
        {
            var s = new FamilySummary
            {
                LineageId = house.Id, Name = r.LineageDisplayName(house),
                Members = HouseCountsOf.Of(r, house).HouseMembers, Residents = HouseCountsOf.Of(r, house).Residents, Households = r.HouseholdsOfLineage(house.Id).Count
            };
            if (FamilyWindowFacts.IsInsider(r, house.Id, viewer) && house.Situation != null && house.Situation.EvaluatedTick > 0) { s.Cohesion = house.Situation.Cohesion; s.Tier = BondTuning.CohesionTier(house.Situation.Cohesion); }
            var alerts = FamilyAlerts.Build(r, house, viewer, now);
            s.Alerts = alerts.Count; s.Urgent = alerts.Any(a => a.Urgent);
            if (alerts.Count > 0) s.FirstAlert = alerts[0].Kind;
            s.AlertKinds = alerts.Select(a => a.Kind).ToList();
            return s;
        }

        public static List<FamilySummary> Sort(IEnumerable<FamilySummary> rows, FamilySort sort, string search)
        {
            var q = rows;
            if (!string.IsNullOrWhiteSpace(search)) q = q.Where(x => x.Name.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
            switch (sort)
            {
                case FamilySort.Size: return q.OrderByDescending(x => x.Members).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                case FamilySort.Standing: return q.OrderByDescending(x => x.Cohesion).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                case FamilySort.Attention: return q.OrderByDescending(x => x.Urgent).ThenByDescending(x => x.Alerts).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
                default: return q.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
            }
        }
    }

    // ------------------------------------------------------------------ relationships

    public enum RelFilter : byte { Family, Associates, Everyone }
    public enum RelSort : byte { Closest, LeastTrusted, MostTense, Recent }

    public struct RelRow
    {
        public int OtherId;
        public string Name;
        public BondKind Kind;
        public float Attachment, Trust, Tension, Familiarity;
        public int LastTick;
        /// <summary>-1 cooling, 0 steady, +1 warming, from the reasons the bond recently moved.</summary>
        public int Trend;
        public bool Family, Associate, Dead;
    }

    public static class RelationshipView
    {
        public const float TrendThreshold = 2f;

        public static List<RelRow> Build(FamilyRegistry r, int subject, int familyId, RelFilter filter, RelSort sort, string search)
        {
            var rows = new List<RelRow>();
            var me = r?.GetPerson(subject);
            if (me == null) return rows;
            string needle = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
            foreach (var bond in r.BondsFrom(subject))
            {
                var other = r.GetPerson(bond.To);
                if (other == null) continue;
                var kind = r.KindOf(me, other) ?? BondKind.Acquaintance;
                var role = r.RoleInHouse(other.Id, familyId);
                bool kin = kind == BondKind.Parent || kind == BondKind.Child || kind == BondKind.Sibling || kind == BondKind.Partner;
                bool family = role >= HouseRole.Member || kin;
                bool associate = !family && (role == HouseRole.Associate || role == HouseRole.TrustedAssociate || role == HouseRole.FamilyFriend);
                if (filter == RelFilter.Family && !family) continue;
                if (filter == RelFilter.Associates && !associate) continue;
                if (needle != null && (other.FullName ?? other.Name ?? "").IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                rows.Add(new RelRow
                {
                    OtherId = other.Id, Name = other.FullName ?? other.Name ?? "", Kind = kind, Attachment = bond.Attachment, Trust = bond.Trust,
                    Tension = bond.Tension, Familiarity = bond.Familiarity, LastTick = bond.LastTick, Trend = TrendOf(bond),
                    Family = family, Associate = associate, Dead = other.Presence == Presence.Dead
                });
            }
            return Sort(rows, sort);
        }

        public static int TrendOf(Bond bond)
        {
            if (bond?.Reasons == null || bond.Reasons.Count == 0) return 0;
            float net = 0f;
            foreach (var reason in bond.Reasons) net += reason.Delta;
            return net > TrendThreshold ? 1 : net < -TrendThreshold ? -1 : 0;
        }

        public static List<RelRow> Sort(List<RelRow> rows, RelSort sort)
        {
            IOrderedEnumerable<RelRow> q;
            switch (sort)
            {
                case RelSort.LeastTrusted: q = rows.OrderBy(x => x.Trust); break;
                case RelSort.MostTense: q = rows.OrderByDescending(x => x.Tension); break;
                case RelSort.Recent: q = rows.OrderByDescending(x => x.LastTick); break;
                default: q = rows.OrderByDescending(x => x.Attachment); break;
            }
            return q.ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.OtherId).ToList();
        }
    }

    // ------------------------------------------------------------------ events

    [Flags]
    public enum EventCategory
    {
        None = 0, Ceremonies = 1, Relationships = 2, Conflict = 4, Leadership = 8, BirthsDeaths = 16, Traditions = 32, Household = 64
    }

    public static class EventFilters
    {
        public static readonly EventCategory[] All =
        {
            EventCategory.Ceremonies, EventCategory.Relationships, EventCategory.Conflict, EventCategory.Leadership,
            EventCategory.BirthsDeaths, EventCategory.Traditions, EventCategory.Household
        };

        public static EventCategory Of(FamilyEventKind kind)
        {
            switch (kind)
            {
                case FamilyEventKind.Birth: case FamilyEventKind.Died: return EventCategory.BirthsDeaths;
                case FamilyEventKind.CameOfAge: case FamilyEventKind.Reunion: return EventCategory.Ceremonies;
                case FamilyEventKind.MarriageFormed: return EventCategory.Ceremonies | EventCategory.Relationships;
                case FamilyEventKind.Anniversary: case FamilyEventKind.PartnershipFormed: case FamilyEventKind.PartnershipEnded:
                case FamilyEventKind.BondDeepened: case FamilyEventKind.BondCooled: return EventCategory.Relationships;
                case FamilyEventKind.RiftOpened: case FamilyEventKind.RiftResolved: return EventCategory.Conflict;
                case FamilyEventKind.TraditionAdopted: case FamilyEventKind.TraditionAbandoned: case FamilyEventKind.TraditionHeld: return EventCategory.Traditions;
                case FamilyEventKind.LineageFounded: case FamilyEventKind.SocialBranch: return EventCategory.Leadership | EventCategory.Household;
                default: return EventCategory.Household;      // joined, left, households, affiliations, memberships, step-families, the records starting
            }
        }

        /// <summary>A moment between two people: a ceremony or tradition moment belongs to its occasion, a poor one is also a conflict.</summary>
        public static EventCategory OfMoment(bool ceremony, bool tradition, EventTier tier)
        {
            var c = EventCategory.Relationships;
            if (ceremony) c |= EventCategory.Ceremonies;
            if (tradition) c |= EventCategory.Traditions;
            if (tier == EventTier.Poor) c |= EventCategory.Conflict;
            return c;
        }

        public static EventCategory OfIncident() => EventCategory.Conflict;

        /// <summary>A mask of 0 means no filter.</summary>
        public static bool Matches(int mask, EventCategory category) => mask == 0 || ((int)category & mask) != 0;

        public static int Toggle(int mask, EventCategory category) => mask ^ (int)category;
    }

    // ------------------------------------------------------------------ supplies

    public enum SupplyLevel : byte { Unknown, Short, Low, Enough }

    public static class SupplyReading
    {
        public const float ShortBelow = 0.5f;

        /// <summary>Current against what the family wants to keep. Nothing known is Unknown, never a made-up zero.</summary>
        public static SupplyLevel Level(bool known, float current, float target)
        {
            if (!known) return SupplyLevel.Unknown;
            if (target <= 0f) return SupplyLevel.Enough;
            if (current >= target) return SupplyLevel.Enough;
            return current < target * ShortBelow ? SupplyLevel.Short : SupplyLevel.Low;
        }
    }

    // ------------------------------------------------------------------ offices

    public sealed class RoleCardData
    {
        public FamilyRole Role;
        public int HolderId;
        public int FamilyId;
        /// <summary>The viewer may not know who holds this. The card then says so instead of naming anyone.</summary>
        public bool Hidden;
        public bool Vacant;
        /// <summary>A key suffix for why this person holds it ("Succession", "Appointed", "Elected", "Designated").</summary>
        public string Why = "";
        public int TermEndTick;
    }

    public static class RoleCards
    {
        /// <summary>The offices that exist for this house, each with its holder as far as the viewer may know. Roles nothing in the game assigns are never listed.</summary>
        public static List<RoleCardData> For(FamilyRegistry r, Lineage house, int viewer)
        {
            var cards = new List<RoleCardData>();
            if (r == null || house == null) return cards;
            int id = house.Id;

            int head = r.HeadOf(id);
            cards.Add(new RoleCardData { Role = FamilyRole.HouseHead, FamilyId = id, HolderId = head, Vacant = head == 0, Why = "Succession" });

            var steward = house.Stewards.Select(s => r.GetPerson(s)).FirstOrDefault(p => p != null && p.InColony);
            cards.Add(new RoleCardData { Role = FamilyRole.Steward, FamilyId = id, HolderId = steward?.Id ?? 0, Vacant = steward == null, Why = "Appointed" });

            bool heirKnown = viewer == 0 || RoleDisplay.HeirKnownTo(r, house, viewer) || house.DesignatedHeirId == viewer;       // the overview is the administrative view: it sees everything
            if (house.DesignatedHeirId != 0)
                cards.Add(new RoleCardData { Role = FamilyRole.PublicHeir, FamilyId = id, HolderId = heirKnown ? house.DesignatedHeirId : 0, Hidden = !heirKnown, Why = "Designated" });
            else
                cards.Add(new RoleCardData { Role = FamilyRole.PublicHeir, FamilyId = id, Vacant = true, Why = "Designated" });

            foreach (OfficeKind kind in Enum.GetValues(typeof(OfficeKind)))
            {
                var post = r.OfficeFor(kind);
                bool held = post != null && post.Held && r.GetPerson(post.HolderId) != null;
                if (!held && !FamilyLife.Framework.OfficeAvailability.IsOffered(kind)) continue;          // no vacant card for a post whose module is not installed
                cards.Add(new RoleCardData
                {
                    Role = RoleDisplay.OfficeRole(kind), HolderId = held ? post.HolderId : 0, Vacant = !held, FamilyId = 0,
                    Why = FamilyRegistry.IsAppointed(kind) ? "Appointed" : "Elected", TermEndTick = held ? post.TermEndTick : 0
                });
            }
            return cards;
        }
    }

    public static class FamilyWindowFacts
    {
        /// <summary>Whether the person being played belongs to this house (or nobody is, in the colony overview). An outsider sees what is public, not how the house is doing inside.</summary>
        public static bool IsInsider(FamilyRegistry r, int family, int viewer) => viewer == 0 || r.RoleInHouse(viewer, family) >= HouseRole.Member;

        /// <summary>The colony's recognised leader, or 0 when there is none (a consensus colony, no one chosen, or a leader the records no longer hold). Never a placeholder person.</summary>
        public static int RecognizedLeader(FamilyRegistry r)
        {
            if (r == null || r.Institution == null) return 0;
            int leader = r.Institution.LeaderId;
            if (leader == 0 || r.Institution.Kind == InstitutionKind.Consensus) return 0;
            var person = r.GetPerson(leader);
            return person != null && person.IsAlive ? leader : 0;
        }

        /// <summary>The first, second and third most meaningful recent events for a house's story: defining before meaningful, newest first.</summary>
        public static List<EventRecord> TopStory(FamilyRegistry r, ISet<int> relevant, int viewer, int take)
        {
            var q = new List<EventRecord>();
            if (r == null) return q;
            for (int i = r.EventLog.Count - 1; i >= 0 && q.Count < 200; i--)
            {
                var e = r.EventLog[i];
                if (e.Weight == EventWeight.Ambient) continue;
                if (!relevant.Contains(e.A) && !relevant.Contains(e.B)) continue;
                if (viewer != 0 && e.A != viewer && e.B != viewer && e.Third != viewer) continue;
                q.Add(e);
            }
            return q.OrderByDescending(e => e.Weight).ThenByDescending(e => e.Tick).Take(take).ToList();
        }
    }
}
