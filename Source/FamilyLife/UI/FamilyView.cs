using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Everything the window shows about one family, worked out once and kept. It is rebuilt only when the family records change (the component's
    /// revision), the family or the person being played changes, or after a short while. Drawing reads it; it never scans pawns or the registry itself.
    /// </summary>
    internal sealed class FamilyView
    {
        public const int RefreshTicks = 600;

        public FamilyRegistry Registry;
        public Lineage House;
        public int LineageId, Revision = -1, Viewer = -1, BuiltTick = -100000;
        public string Name = "";
        public bool Insider;
        public List<PersonRecord> Members = new List<PersonRecord>();
        public List<PersonRecord> Colony = new List<PersonRecord>();
        public HashSet<int> Relevant = new HashSet<int>();
        public List<FamilyAlert> Alerts = new List<FamilyAlert>();
        public List<RoleCardData> Roles = new List<RoleCardData>();
        public List<EventRecord> Story = new List<EventRecord>();
        public int Head, Steward, HouseholdCount;
        public PersonRecord Perspective;

        public bool Fresh(FamilyComponent component, int lineageId, int now) =>
            Registry == component.Registry && LineageId == lineageId && Revision == component.Revision && Viewer == component.PerspectivePawnId && now - BuiltTick < RefreshTicks;

        public static FamilyView Build(FamilyComponent component, int lineageId, int now)
        {
            var r = component.Registry;
            var v = new FamilyView { Registry = r, LineageId = lineageId, Revision = component.Revision, Viewer = component.PerspectivePawnId, BuiltTick = now };
            v.House = r.GetLineage(lineageId);
            if (v.House == null) return v;
            v.Name = r.LineageDisplayName(v.House);
            v.Perspective = v.Viewer == 0 ? null : r.GetPerson(v.Viewer);
            v.Insider = FamilyWindowFacts.IsInsider(r, lineageId, v.Viewer);
            v.Members = r.MembersOf(lineageId).ToList();
            v.Colony = v.Members.Where(p => p.InColony).ToList();
            v.Relevant = new HashSet<int>(v.Members.Select(p => p.Id));
            foreach (var a in v.House.Affiliations) v.Relevant.Add(a.PersonId);
            v.Head = r.HeadOf(lineageId);
            v.Steward = v.House.Stewards.Select(id => r.GetPerson(id)).FirstOrDefault(p => p != null && p.InColony)?.Id ?? 0;
            v.HouseholdCount = r.HouseholdsOfLineage(lineageId).Count;
            v.Alerts = FamilyAlerts.Build(r, v.House, v.Viewer, now);
            v.Roles = RoleCards.For(r, v.House, v.Viewer);
            v.Story = FamilyWindowFacts.TopStory(r, v.Relevant, v.Viewer, 3);
            return v;
        }

        public string NameOfPerspective(FamilyRegistry r) => Perspective == null ? "" : Perspective.Name;
    }

    /// <summary>One person as the People tab and the detail panel describe them. Built from the registry and, when the pawn is loaded, the pawn.</summary>
    internal sealed class PersonRow
    {
        public PersonRecord Person;
        public Pawn Pawn;
        public string Relation = "", Age = "", Household = "", Status = "", Concern = "";
        public int HouseholdId;
        public List<RoleEntry> Roles = new List<RoleEntry>();
    }

    /// <summary>The People tab's rows for one family, rebuilt with the view and every few seconds for the live parts (map status, concern).</summary>
    internal sealed class PeopleModel
    {
        public const int LiveTicks = 180;
        public int LineageId, Revision = -1, Viewer = -1, BuiltTick = -100000;
        public FamilyRegistry Registry;
        public List<PersonRow> Rows = new List<PersonRow>();
        public Dictionary<int, PersonRow> ById = new Dictionary<int, PersonRow>();

        public bool Fresh(FamilyComponent c, int lineageId, int now) =>
            Registry == c.Registry && LineageId == lineageId && Revision == c.Revision && Viewer == c.PerspectivePawnId && now - BuiltTick < LiveTicks;

        public static PeopleModel Build(FamilyComponent component, FamilyView view, int now)
        {
            var r = component.Registry;
            var model = new PeopleModel { Registry = r, LineageId = view.LineageId, Revision = component.Revision, Viewer = component.PerspectivePawnId, BuiltTick = now };
            var anchor = view.Perspective ?? r.GetPerson(view.Head);
            var options = RoleDisplayOptions.Everything;
            foreach (var person in view.Members.OrderByDescending(p => p.InColony).ThenBy(p => p.Juvenile).ThenBy(p => p.Name))
            {
                var row = new PersonRow { Person = person, Pawn = component.TryGetPawn(person.Id), HouseholdId = person.HouseholdId };
                if (anchor != null && anchor.Id != person.Id)
                {
                    var kind = r.KindOf(anchor, person);
                    row.Relation = kind.HasValue ? ("WFL_FW_Rel_" + kind.Value).Translate(anchor.Name).ToString() : "";
                }
                else if (anchor != null) row.Relation = anchor == view.Perspective ? "WFL_FW_You".Translate().ToString() : "WFL_FW_HouseHead".Translate().ToString();
                row.Age = AgeText(person, row.Pawn);
                if (person.InColony && person.HouseholdId != 0) row.Household = "WFL_FW_HouseholdN".Translate(person.HouseholdId).ToString();
                row.Status = StatusText(person, row.Pawn);
                row.Concern = ConcernText(r, person, row.Pawn, view.Viewer);
                if (person.InColony) row.Roles = RoleDisplay.PublicRoles(r, person.Id, view.Viewer, options);
                model.Rows.Add(row);
                model.ById[person.Id] = row;
            }
            return model;
        }

        private static string AgeText(PersonRecord person, Pawn pawn)
        {
            try
            {
                var stage = pawn?.ageTracker?.CurLifeStage;
                if (stage != null && !string.IsNullOrEmpty(stage.label)) return stage.label.CapitalizeFirst();
            }
            catch (System.Exception) { }
            return (person.Juvenile ? "WFL_Child".Translate().ToString() : "WFL_FW_Adult".Translate().ToString()).CapitalizeFirst();
        }

        private static string StatusText(PersonRecord person, Pawn pawn)
        {
            if (person.Presence == Presence.Dead) return FamilyUi.StatusText(person);
            if (person.Presence == Presence.Away) return "WFL_StatusAway".Translate().ToString().CapitalizeFirst();
            if (pawn == null || pawn.Dead) return "WFL_FW_NotLoaded".Translate().ToString();
            if (!pawn.Spawned) return CaravanUtility.IsCaravanMember(pawn) ? "WFL_FW_InCaravan".Translate().ToString() : "WFL_FW_OffMap".Translate().ToString();
            try { var report = pawn.jobs?.curDriver?.GetReport(); if (!string.IsNullOrEmpty(report)) return report.CapitalizeFirst(); }
            catch (System.Exception) { }
            return "WFL_FW_OnMap".Translate().ToString();
        }

        /// <summary>Something visibly wrong right now, or a private strain only the person themselves (or the overview) would know of.</summary>
        private static string ConcernText(FamilyRegistry r, PersonRecord person, Pawn pawn, int viewer)
        {
            if (!person.InColony) return "";
            if (pawn != null && pawn.Spawned)
            {
                if (pawn.InMentalState) return "WFL_FW_Concern_Break".Translate().ToString();
                if (pawn.Downed) return "WFL_FW_Concern_Downed".Translate().ToString();
                if (pawn.needs?.food != null && pawn.needs.food.CurLevelPercentage < 0.1f) return "WFL_FW_Concern_Hungry".Translate().ToString();
                if (pawn.health != null && pawn.health.HasHediffsNeedingTend()) return "WFL_FW_Concern_Hurt".Translate().ToString();
            }
            if (viewer == 0 || viewer == person.Id)
            {
                var rift = r.OpenRiftsOf(person.Id).FirstOrDefault();
                if (rift != null) return "WFL_FW_Concern_Rift".Translate(FamilyUi.NameOf(r, rift.A == person.Id ? rift.B : rift.A)).ToString();
            }
            return "";
        }
    }
}
