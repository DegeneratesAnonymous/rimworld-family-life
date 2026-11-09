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
        public StoryModel Narr = new StoryModel();
        public ChapterCard Chapter;
        public List<ThreadCard> Threads = new List<ThreadCard>();
        public List<BeatCard> Highlights = new List<BeatCard>();
        public HouseCounts Counts;
        public Dictionary<int, string> HouseholdLabels = new Dictionary<int, string>();
        public int Head, Steward, HouseholdCount;
        public PersonRecord Perspective;

        public bool Fresh(FamilyComponent component, int lineageId, int now) =>
            ViewFreshness.IsFresh(Registry, component.Registry, LineageId, lineageId, Revision, component.Revision, Viewer, component.PerspectivePawnId, BuiltTick, now, RefreshTicks);

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
            v.Narr = StoryModel.Build(r, v.House, v.Viewer, now, (id, cap) => SkillLevel(component, id, cap));
            v.Counts = v.Narr.Counts;
            v.Chapter = Window_Families.MakeChapter(r, v.Narr.Chapter);
            foreach (var t in v.Narr.Threads) v.Threads.Add(Window_Families.MakeThread(r, t));
            foreach (var b in v.Narr.Highlights) { var card = Window_Families.MakeBeat(r, b, v.Viewer); if (card != null) v.Highlights.Add(card); }
            foreach (var h in r.HouseholdsOfLineage(lineageId)) v.HouseholdLabels[h.Id] = FamilyText.Say(HouseholdNames.NameOf(r, h));
            return v;
        }

        /// <summary>A person's level in the skill behind a House capability, or -1 when they cannot do that work. Read once per view build, never while drawing.</summary>
        private static int SkillLevel(FamilyComponent component, int personId, HouseCapability cap)
        {
            try
            {
                var pawn = component.TryGetPawn(personId);
                if (pawn?.skills == null) return -1;
                SkillDef[] defs;
                switch (cap)
                {
                    case HouseCapability.Cooking: defs = new[] { SkillDefOf.Cooking }; break;
                    case HouseCapability.Medical: defs = new[] { SkillDefOf.Medicine }; break;
                    case HouseCapability.Growing: defs = new[] { SkillDefOf.Plants }; break;
                    case HouseCapability.Construction: defs = new[] { SkillDefOf.Construction }; break;
                    case HouseCapability.Crafting: defs = new[] { SkillDefOf.Crafting }; break;
                    case HouseCapability.Intellectual: defs = new[] { SkillDefOf.Intellectual }; break;
                    case HouseCapability.Social: case HouseCapability.Childcare: defs = new[] { SkillDefOf.Social }; break;
                    case HouseCapability.Animals: defs = new[] { SkillDefOf.Animals }; break;
                    case HouseCapability.Combat: defs = new[] { SkillDefOf.Shooting, SkillDefOf.Melee }; break;
                    default: return -1;
                }
                int best = -1;
                foreach (var def in defs) { var s = pawn.skills.GetSkill(def); if (s != null && !s.TotallyDisabled) best = System.Math.Max(best, s.Level); }
                return best;
            }
            catch (System.Exception) { return -1; }
        }

        public string NameOfPerspective(FamilyRegistry r) => Perspective == null ? "" : HouseholdNames.Short(Perspective);
    }

    /// <summary>One person as the People tab and the detail panel describe them. Built from the registry and, when the pawn is loaded, the pawn.</summary>
    internal enum PersonCategory : byte { Member, Resident, Elsewhere, Associate }

    internal sealed class PersonRow
    {
        public PersonRecord Person;
        public Pawn Pawn;
        public string Relation = "", Age = "", Household = "", Status = "", Concern = "", HouseNote = "", Thread = "";
        /// <summary>Lives with the family but belongs to another House.</summary>
        public bool Outsider;
        public PersonCategory Category;
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
            ViewFreshness.IsFresh(Registry, c.Registry, LineageId, lineageId, Revision, c.Revision, Viewer, c.PerspectivePawnId, BuiltTick, now, LiveTicks);

        public static PeopleModel Build(FamilyComponent component, FamilyView view, int now)
        {
            var r = component.Registry;
            var model = new PeopleModel { Registry = r, LineageId = view.LineageId, Revision = component.Revision, Viewer = component.PerspectivePawnId, BuiltTick = now };
            var anchor = view.Perspective ?? r.GetPerson(view.Head);
            var options = RoleDisplayOptions.Everything;
            // Everyone in the family's households: its own members, and the people who live with them but belong to another House.
            var memberIds = new HashSet<int>(view.Members.Select(p => p.Id));
            var people = new List<PersonRecord>(view.Members);
            foreach (var a in r.AffiliatesOf(view.LineageId)) if (a.InColony && memberIds.Add(a.Id)) people.Add(a);
            var contactIds = new HashSet<int>();
            foreach (var c in r.HouseContacts) if (c.FamilyId == view.LineageId) { var cp = r.GetPerson(c.PersonId); if (cp != null && cp.InColony && !r.BelongsToFamily(cp, view.LineageId)) { contactIds.Add(cp.Id); if (memberIds.Add(cp.Id)) people.Add(cp); } }
            var memberOnly = new HashSet<int>(people.Where(p => r.BelongsToFamily(p, view.LineageId)).Select(p => p.Id));
            foreach (var household in r.HouseholdsOfLineage(view.LineageId))
                foreach (var id in household.ResidentIds)
                {
                    var resident = r.GetPerson(id);
                    if (resident != null && resident.InColony && !memberIds.Contains(id) && !view.Relevant.Contains(id)) { people.Add(resident); memberIds.Add(id); }
                }
            foreach (var person in people.OrderBy(p => !view.Relevant.Contains(p.Id)).ThenByDescending(p => p.InColony).ThenBy(p => p.Juvenile).ThenBy(p => p.Name))
            {
                var row = new PersonRow { Person = person, Pawn = component.TryGetPawn(person.Id), HouseholdId = person.HouseholdId, Outsider = !view.Relevant.Contains(person.Id) };
                row.Category = memberOnly.Contains(person.Id) ? (person.InColony ? PersonCategory.Member : PersonCategory.Elsewhere) : contactIds.Contains(person.Id) ? PersonCategory.Associate : PersonCategory.Resident;
                if (row.Outsider)
                {
                    var home = r.GetLineage(person.SocialFamilyId > 0 ? person.SocialFamilyId : person.PrimaryLineageId);
                    string houseName = home != null ? r.LineageDisplayName(home) : "";
                    bool spouse = view.Members.Any(m => r.KindOf(m, person) == BondKind.Partner);
                    row.HouseNote = houseName.Length == 0 ? "" : (spouse ? "WFL_FW_ThroughMarriage" : "WFL_FW_ResidentOtherHouse").Translate(houseName).ToString();
                }
                foreach (var th in view.Threads) if (th.T.People.Contains(person.Id)) { row.Thread = th.Line; break; }
                if (anchor != null && anchor.Id != person.Id)
                {
                    var kind = r.KindOf(anchor, person);
                    row.Relation = kind.HasValue ? ("WFL_FW_Rel_" + kind.Value).Translate(anchor.Name).ToString() : "";
                }
                else if (anchor != null) row.Relation = anchor == view.Perspective && Narrative.IsPlayed(anchor.Id) ? "WFL_FW_You".Translate().ToString() : (anchor.Id == view.Head ? "WFL_FW_HouseHead".Translate().ToString() : "");
                row.Age = AgeText(person, row.Pawn);
                if (person.InColony && person.HouseholdId != 0) row.Household = view.HouseholdLabels.TryGetValue(person.HouseholdId, out var hhName) ? hhName : "WFL_FW_HH_Plain".Translate().ToString();
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
