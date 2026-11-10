using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The game side of professional careers. Modules register their careers and roles here; the framework counts experience, awards qualifications, keeps the player's assignments and,
    /// for the two old working posts, seats the assigned professionals so the existing class and clinic code carries on without an election. Owner: the framework.
    /// </summary>
    public static class CareerService
    {
        private static readonly Dictionary<string, CareerDefinition> careers = new Dictionary<string, CareerDefinition>();
        public static readonly ProfessionCatalog Professions = new ProfessionCatalog();
        private static readonly List<LegacyPost> legacyPosts = new List<LegacyPost>();

        public static IEnumerable<CareerDefinition> Careers => careers.Values.OrderBy(c => c.Id, StringComparer.Ordinal);
        public static CareerDefinition Career(string id) => id != null && careers.TryGetValue(id, out var c) ? c : null;

        public static void RegisterCareer(CareerDefinition def) { if (def != null && !string.IsNullOrEmpty(def.Id)) careers[def.Id] = def; }
        public static void RegisterRole(ProfessionRole role) { Professions.Register(role); }

        /// <summary>
        /// Makes the holders of an old post professionals: <paramref name="roleId"/> assigned players sit in the post's working seat (no vote), and whoever held the post in an older save is
        /// migrated once. Runs from the maintenance roster, so it stops when the module does.
        /// </summary>
        public static void RegisterSeat(string moduleId, OfficeKind kind, string roleId, string fallbackRoleId = null)
        {
            legacyPosts.Add(new LegacyPost { Kind = kind, RoleId = fallbackRoleId ?? roleId });          // an old holder becomes the entry-level role when there is one (never a Doctor by migration)
            FamilyFramework.Roster.Register(new WorkItem
            {
                Id = moduleId + ".seat." + kind.ToString().ToLowerInvariant(), ModuleId = moduleId, Interval = 0,
                Run = now => { var c = FamilyComponent.Current; if (c != null) Seat(c, kind, roleId, fallbackRoleId, now); }
            });
        }

        private static void Seat(FamilyComponent c, OfficeKind kind, string roleId, string fallbackRoleId, int now)
        {
            var r = c.Registry;
            LegacyProfessionMigration.Run(r, now, legacyPosts, Professions);
            var role = Professions.ById(roleId); if (role == null) return;
            var book = r.Assignments();
            var roster = book.Holders(roleId).Where(id => r.GetPerson(id)?.InColony == true).ToList();
            if (roster.Count == 0 && fallbackRoleId != null) roster = book.Holders(fallbackRoleId).Where(id => r.GetPerson(id)?.InColony == true).ToList();          // e.g. no Doctor yet: the Healers carry on
            int current = r.OfficeFor(kind)?.HolderId ?? 0;
            int pick = ProfessionalSeating.Pick(roster, current, id => c.TryGetPawn(id) is Pawn p && !p.Dead);
            if (pick != current) r.SeatService(kind, pick, now);
        }

        public static Func<string, int> SkillsOf(Pawn p) => name =>
        {
            var def = DefDatabase<SkillDef>.GetNamedSilentFail(name);
            var sk = def == null ? null : p?.skills?.GetSkill(def);
            return sk == null || sk.TotallyDisabled ? 0 : sk.Level;
        };

        /// <summary>
        /// A module reports a finished activity. Experience is credited once per <paramref name="activityKey"/>, qualifications are checked, and the player is told once about a new one.
        /// Returns the experience credited. Never throws into the caller: careers must not be able to break the work that reports them.
        /// </summary>
        public static int Report(FamilyComponent c, Pawn pawn, string careerId, string specialty, string kind, string activityKey, int points, int now)
        {
            try
            {
                var def = Career(careerId); if (c == null || pawn == null || def == null) return 0;
                var ledger = c.Registry.Careers();
                int day = now / ActivityTuning.TicksPerDay;
                int given = ledger.Award(pawn.thingIDNumber, careerId, specialty, kind, activityKey, points, day);
                if (given <= 0) return 0;
                foreach (var gained in ledger.Evaluate(pawn.thingIDNumber, def, SkillsOf(pawn)))
                {
                    ledger.NoteQualified(pawn.thingIDNumber, gained, now);
                    Messages.Message("WFL_Career_Qualified".Translate(pawn.LabelShort, Describe(gained)), pawn, MessageTypeDefOf.PositiveEvent, false);
                }
                return given;
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] A career report was skipped: " + ex.Message); return 0; }
        }

        public static string TitleOf(ProfessionRole role, FamilyRegistry r) => (role.TitleKey?.Invoke(r) ?? role.Id + "_label").Translate().ToString();

        /// <summary>A qualification id as a short phrase: the role it unlocks, or the career and specialty.</summary>
        public static string Describe(string qualification)
        {
            var role = Professions.All.FirstOrDefault(r => (r.Level == 2 && r.Career == qualification) || (r.Level == 3 && r.Career + ":" + r.Specialty == qualification));
            if (role != null) return TitleOf(role, FamilyComponent.Current?.Registry);
            return qualification;
        }
    }
}
