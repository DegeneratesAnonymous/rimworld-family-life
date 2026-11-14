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

        /// <summary>A meaningful careers event, written when "log events" is on in the mod settings (never per tick). Qualifications, migrations and failures are written regardless, with <see cref="Note"/>.</summary>
        public static void Event(string text) { var s = FamilyLifeMod.Settings; if (s != null && s.logEvents) Log.Message("[Wil's Family Life] careers: " + text); }
        /// <summary>A rare careers event that is always written: registration, migration, a qualification, a reconcile that changed something.</summary>
        public static void Note(string text) => Log.Message("[Wil's Family Life] careers: " + text);

        /// <summary>One line per module once its careers and roles are registered, so a missing or half-registered module shows in the log.</summary>
        public static void Registered(string moduleId) => Note(moduleId + " registered: " + careers.Values.Count(c => c.ModuleId == moduleId) + " career(s), " + Professions.All.Count(r => r.ModuleId == moduleId) + " role(s)");

        /// <summary>
        /// Once per loaded game, after registration closes: drops career and assignment data for people the family records never knew, and says what it did. Never throws: a failure is logged and
        /// the data is left exactly as it was.
        /// </summary>
        public static void OnLoaded(FamilyComponent c)
        {
            try
            {
                var r = c.Registry; int careers = r.ReconcileCareers(), assignments = r.ReconcileAssignments();
                Note("loaded: " + r.Assignments().Count + " assignment(s); reconciled " + careers + " career record(s) and " + assignments + " assignment(s) of people no longer known");
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] careers: load reconcile failed, data left as it was: " + ex.Message); }
        }

        /// <summary>A readable summary of every pawn's careers for the developer menu.</summary>
        public static List<string> Status(FamilyComponent c)
        {
            var r = c.Registry; var lines = new List<string>(); var l = r.Careers(); var b = r.Assignments();
            lines.Add("Roles: " + string.Join(", ", Professions.All.Select(x => x.Id)));
            lines.Add("Hooks seen this session: operations " + MedicalWorkHits.Operations + ", tends " + MedicalWorkHits.Tends + ", operations credited " + MedicalWorkHits.OperationsCredited + ", animal tends credited " + MedicalWorkHits.AnimalTendsCredited);
            foreach (var p in r.Persons.Where(x => x.InColony).OrderBy(x => x.Id))
            {
                var roles = b.RolesOf(p.Id).ToList(); var quals = Careers.SelectMany(d => new[] { d.Id }.Concat(d.Level2Branches.Keys.Concat(d.Specialties.Keys).Distinct().Select(k => d.Id + ":" + k))).Where(q => q.Contains(":") ? l.IsSpecialist(p.Id, q.Split(':')[0], q.Split(':')[1]) : l.IsQualified(p.Id, q)).ToList();
                int xp = Careers.Sum(d => l.GeneralXp(p.Id, d.Id));
                if (roles.Count == 0 && quals.Count == 0 && xp == 0) continue;
                lines.Add(FamilyUi.NameOf(r, p.Id) + ": assigned [" + string.Join(", ", roles) + "] qualified [" + string.Join(", ", quals) + "] experience " + xp);
            }
            lines.Add("Saved payloads: " + string.Join(", ", r.Shared.Ids ?? new List<string>()));
            return lines;
        }

        public static IEnumerable<CareerDefinition> Careers => careers.Values.OrderBy(c => c.Id, StringComparer.Ordinal);
        public static CareerDefinition Career(string id) => id != null && careers.TryGetValue(id, out var c) ? c : null;

        public static void RegisterCareer(CareerDefinition def) { if (def != null && !string.IsNullOrEmpty(def.Id)) careers[def.Id] = def; }
        public static void RegisterRole(ProfessionRole role) { Professions.Register(role); }

        /// <summary>
        /// Makes the holders of an old post professionals: <paramref name="roleId"/> assigned players sit in the post's working seat (no vote), and whoever held the post in an older save is
        /// migrated once. Runs from the maintenance roster, so it stops when the module does.
        /// </summary>
        public static void RegisterSeat(string moduleId, OfficeKind kind, string roleId, string fallbackRoleId = null, IEnumerable<string> middleRoleIds = null)
        {
            legacyPosts.Add(new LegacyPost { Kind = kind, RoleId = fallbackRoleId ?? roleId });          // an old holder becomes the entry-level role when there is one (never a Doctor by migration)
            FamilyFramework.Roster.Register(new WorkItem
            {
                Id = moduleId + ".seat." + kind.ToString().ToLowerInvariant(), ModuleId = moduleId, Interval = 0,
                Run = now => { var c = FamilyComponent.Current; if (c != null) Seat(c, kind, roleId, fallbackRoleId, middleRoleIds, now); }
            });
        }

        private static void Seat(FamilyComponent c, OfficeKind kind, string roleId, string fallbackRoleId, IEnumerable<string> middleRoleIds, int now)
        {
            var r = c.Registry;
            foreach (var done in LegacyProfessionMigration.Run(r, now, legacyPosts, Professions)) Note("migrated legacy post (" + done + ") to a professional assignment with a legacy qualification and no invented experience");
            var role = Professions.ById(roleId); if (role == null) return;
            var book = r.Assignments();
            var roster = book.Holders(roleId).Where(id => r.GetPerson(id)?.InColony == true).ToList();
            if (roster.Count == 0 && middleRoleIds != null) roster = middleRoleIds.SelectMany(m => book.Holders(m)).Distinct().Where(id => r.GetPerson(id)?.InColony == true).ToList();          // e.g. only specialists assigned: they carry the rounds
            if (roster.Count == 0 && fallbackRoleId != null) roster = book.Holders(fallbackRoleId).Where(id => r.GetPerson(id)?.InColony == true).ToList();          // e.g. no Doctor yet: the Healers carry on
            int current = r.OfficeFor(kind)?.HolderId ?? 0;
            int pick = ProfessionalSeating.Pick(roster, current, id => c.TryGetPawn(id) is Pawn p && !p.Dead);
            if (pick != current) { r.SeatService(kind, pick, now); Event(kind + " working seat: " + current + " -> " + pick); }
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
                if (c.Registry.GetPerson(pawn.thingIDNumber)?.InColony != true) return 0;          // only the colony's own people build a career (not a raider who tends someone, or a passing guest)
                var ledger = c.Registry.Careers();
                int day = now / ActivityTuning.TicksPerDay;
                EconomyHits.WorkReports++;
                foreach (var listener in FamilyFramework.WorkReported.Enabled())          // wages: a listener decides whether the work is paid; it is told once per finished activity
                    try { listener(pawn, careerId, specialty, kind, activityKey, now); } catch (Exception ex) { Log.Warning("[Wil's Family Life] A work listener was skipped: " + ex.Message); }
                int given = ledger.Award(pawn.thingIDNumber, careerId, specialty, kind, activityKey, points, day);
                if (given <= 0) return 0;
                Event(pawn.LabelShort + " +" + given + " " + careerId + (specialty != null ? ":" + specialty : "") + " (" + kind + ")");
                foreach (var gained in ledger.Evaluate(pawn.thingIDNumber, def, SkillsOf(pawn)))
                {
                    ledger.NoteQualified(pawn.thingIDNumber, gained, now);
                    Note(pawn.LabelShort + " qualified: " + gained);
                    Messages.Message("WFL_Career_Qualified".Translate(pawn.LabelShort, Describe(gained)), pawn, MessageTypeDefOf.PositiveEvent, false);
                }
                return given;
            }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] A career report was skipped: " + ex.Message); return 0; }
        }

        public static string TitleOf(ProfessionRole role, FamilyRegistry r) => (role.TitleKey?.Invoke(r) ?? role.Id + "_label").Translate().ToString();

        /// <summary>A specialty benefit is active only while the qualified pawn is assigned to that exact specialty.</summary>
        public static bool IsAssignedSpecialist(Pawn pawn, string career, string specialty)
        {
            var r=FamilyComponent.Current?.Registry;if(pawn==null||r==null||string.IsNullOrEmpty(specialty)||!r.Careers().IsSpecialist(pawn.thingIDNumber,career,specialty))return false;
            return Professions.All.Any(role=>role.Career==career&&role.Specialty==specialty&&r.Assignments().IsAssigned(pawn.thingIDNumber,role.Id));
        }

        /// <summary>The career's own name for headings, from its Level 2 role.</summary>
        public static string CareerName(CareerDefinition def, FamilyRegistry r) { if (!string.IsNullOrEmpty(def.LabelKey)) return def.LabelKey.Translate().ToString(); var role = Professions.All.FirstOrDefault(x => x.Career == def.Id && x.Level == 2); return role != null ? TitleOf(role, r) : def.Id; }

        /// <summary>A qualification id as a short phrase: the role it unlocks, or the career and specialty.</summary>
        public static string Describe(string qualification)
        {
            var role = Professions.All.FirstOrDefault(r => (r.Level == 2 && string.IsNullOrEmpty(r.Specialty) && r.Career == qualification) || (r.Level >= 2 && !string.IsNullOrEmpty(r.Specialty) && r.Career + ":" + r.Specialty == qualification));
            if (role != null) return TitleOf(role, FamilyComponent.Current?.Registry);
            return qualification;
        }
    }
}
