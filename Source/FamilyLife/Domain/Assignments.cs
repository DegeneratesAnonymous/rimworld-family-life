using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    // Professional ASSIGNMENTS: the work the player wants a pawn to do. Three separate things, tracked separately:
    //   qualification  - what the pawn is capable of (CareerLedger; earned, kept)
    //   assignment     - what the player has them doing (AssignmentBook; chosen, changeable, any number of holders)
    //   community office - political or ceremonial standing (CommunityOffice; elected or appointed)
    // Qualifying never assigns anyone, losing an assignment never loses a qualification, and no assignment depends on an office.

    /// <summary>A professional role a module defines. Level 1 needs no qualification; a higher level needs the career's qualification (and the specialty, at level 3).</summary>
    public sealed class ProfessionRole
    {
        public string Id, ModuleId, Career, Specialty;
        public int Level = 1;
        /// <summary>Optional: the label key to show now (a role can be titled differently as the community changes, without its id changing).</summary>
        public Func<FamilyRegistry, string> TitleKey;
    }

    public sealed class ProfessionCatalog
    {
        private readonly Dictionary<string, ProfessionRole> byId = new Dictionary<string, ProfessionRole>();
        public IEnumerable<ProfessionRole> All => byId.Values.OrderBy(r => r.Id, StringComparer.Ordinal);
        public void Register(ProfessionRole role)
        {
            if (role == null || string.IsNullOrEmpty(role.Id) || string.IsNullOrEmpty(role.Career)) throw new RegistrationException("A profession role needs an id and a career.");
            if (!string.IsNullOrEmpty(role.ModuleId) && !role.Id.StartsWith(role.ModuleId + ".", StringComparison.Ordinal)) throw new RegistrationException("Role '" + role.Id + "' must be inside its module's namespace.");
            if (role.Level < 1 || role.Level > 3) throw new RegistrationException("Role '" + role.Id + "' has level " + role.Level + "; levels are 1 to 3.");
            if (role.Level == 3 && string.IsNullOrEmpty(role.Specialty)) throw new RegistrationException("Level 3 role '" + role.Id + "' needs a specialty.");
            if (byId.ContainsKey(role.Id)) throw new RegistrationException("Role '" + role.Id + "' is registered twice.");
            byId[role.Id] = role;
        }
        public ProfessionRole ById(string id) => id != null && byId.TryGetValue(id, out var r) ? r : null;
    }

    public enum AssignResult { Assigned, AlreadyAssigned, UnknownRole, NotQualified }

    /// <summary>Who is assigned to which professional role. Any number of people may hold a role and a person may hold several. Saved as a community-wide payload.</summary>
    public sealed class AssignmentBook : IModuleState
    {
        public const string Id = "wils.assignments";
        private readonly List<int> people = new List<int>(), ticks = new List<int>();
        private readonly List<string> roles = new List<string>();
        private readonly HashSet<string> migrated = new HashSet<string>();

        public bool IsAssigned(int person, string role) { for (int i = 0; i < people.Count; i++) if (people[i] == person && roles[i] == role) return true; return false; }
        public IEnumerable<string> RolesOf(int person) { for (int i = 0; i < people.Count; i++) if (people[i] == person) yield return roles[i]; }
        public IEnumerable<int> Holders(string role) { for (int i = 0; i < people.Count; i++) if (roles[i] == role) yield return people[i]; }
        public int Count => people.Count;
        public IEnumerable<int> AllPeople() => people;

        /// <summary>What assigning this person to this role would need. Level 1 is always allowed; otherwise the qualification must already be earned.</summary>
        public static AssignResult Check(ProfessionRole role, CareerLedger ledger, int person)
        {
            if (role == null) return AssignResult.UnknownRole;
            if (role.Level <= 1) return AssignResult.Assigned;
            if (!ledger.IsQualified(person, role.Career)) return AssignResult.NotQualified;
            if (!string.IsNullOrEmpty(role.Specialty) && !ledger.IsSpecialist(person, role.Career, role.Specialty)) return AssignResult.NotQualified;
            return AssignResult.Assigned;
        }

        /// <summary>The player's choice. Refused only for lack of a qualification the role needs; there is no cap on how many people hold a role.</summary>
        public AssignResult Assign(int person, ProfessionRole role, CareerLedger ledger, int tick)
        {
            var check = Check(role, ledger, person);
            if (check != AssignResult.Assigned) return check;
            if (IsAssigned(person, role.Id)) return AssignResult.AlreadyAssigned;
            people.Add(person); roles.Add(role.Id); ticks.Add(tick);
            return AssignResult.Assigned;
        }

        public bool Unassign(int person, string role)
        {
            for (int i = 0; i < people.Count; i++) if (people[i] == person && roles[i] == role) { people.RemoveAt(i); roles.RemoveAt(i); ticks.RemoveAt(i); return true; }
            return false;
        }

        /// <summary>Drops every assignment of a person (they died or left). Their qualifications are not touched here.</summary>
        public int Vacate(int person) { int n = 0; for (int i = people.Count - 1; i >= 0; i--) if (people[i] == person) { people.RemoveAt(i); roles.RemoveAt(i); ticks.RemoveAt(i); n++; } return n; }

        public bool HasMigrated(string key) => migrated.Contains(key);
        public void MarkMigrated(string key) { migrated.Add(key); }

        public string Encode() => new StateWriter().Ints("p", people).Strings("r", roles).Ints("t", ticks).Strings("m", migrated.OrderBy(x => x, StringComparer.Ordinal).ToList()).ToString();

        public static AssignmentBook Decode(StateReader r)
        {
            var b = new AssignmentBook(); var p = r.Ints("p"); var ro = r.Strings("r"); var t = r.Ints("t");
            for (int i = 0; i < p.Count && i < ro.Count; i++)
            {
                if (p[i] <= 0 || string.IsNullOrEmpty(ro[i]) || b.IsAssigned(p[i], ro[i])) continue;
                b.people.Add(p[i]); b.roles.Add(ro[i]); b.ticks.Add(i < t.Count ? t[i] : 0);
            }
            foreach (var m in r.Strings("m")) b.migrated.Add(m);
            return b;
        }
    }

    public sealed class AssignmentBookFactory : IStateFactory<AssignmentBook>
    {
        public static readonly AssignmentBookFactory Instance = new AssignmentBookFactory();
        public int Version => 1;
        public AssignmentBook Create() => new AssignmentBook();
        public AssignmentBook Decode(StateReader reader, int version) => AssignmentBook.Decode(reader);
        public AssignmentBook FromLegacy(StateReader legacy) => new AssignmentBook();
    }

    public static class AssignmentAccess
    {
        public static AssignmentBook Assignments(this FamilyRegistry r) => r.Shared.Of(AssignmentBook.Id, AssignmentBookFactory.Instance);

        /// <summary>Drops assignments of people the registry no longer knows. Returns how many people were dropped.</summary>
        public static int ReconcileAssignments(this FamilyRegistry r)
        {
            var known = new HashSet<int>(r.Persons.Select(p => p.Id)); var b = r.Assignments(); int n = 0;
            foreach (int id in r.AssignedPeople().Where(i => !known.Contains(i)).ToList()) { b.Vacate(id); n++; }
            return n;
        }

        public static IEnumerable<int> AssignedPeople(this FamilyRegistry r) => r.Assignments().AllPeople().Distinct();
    }

    /// <summary>
    /// Brings the holders of the two old elected posts into the new model, once each, without touching the offices themselves (so everything that still reads an office keeps working). The holder
    /// is assigned the matching role and given that career's qualification as a recognised legacy holder: no experience is invented, because none was recorded. A holder who has since
    /// changed is migrated on their own; a repeat of the same holder does nothing.
    /// </summary>
    public static class LegacyProfessionMigration
    {
        public static List<string> Run(FamilyRegistry r, int tick, IEnumerable<LegacyPost> posts, ProfessionCatalog catalog)
        {
            var done = new List<string>(); var book = r.Assignments(); var ledger = r.Careers();
            foreach (var post in posts)
            {
                var office = r.OfficeFor(post.Kind);
                if (office == null || !office.Held) continue;
                string key = "office:" + post.Kind + ":" + office.HolderId;
                if (book.HasMigrated(key)) continue;
                var role = catalog.ById(post.RoleId); if (role == null) continue;
                ledger.Grandfather(office.HolderId, role.Career);
                book.Assign(office.HolderId, role, ledger, tick);
                book.MarkMigrated(key); done.Add(key);
            }
            return done;
        }
    }

    /// <summary>Which role the holder of an old post becomes.</summary>
    public sealed class LegacyPost { public OfficeKind Kind; public string RoleId; }
}

namespace FamilyLife.Domain
{
    /// <summary>
    /// The compatibility seat: the old Teacher and Healer office records still carry the working state of classes and rounds, and everything that reads them keeps working. Who sits
    /// there is no longer decided by a vote. It follows the professionals the player has assigned.
    /// </summary>
    public static class ProfessionalSeating
    {
        /// <summary>
        /// Who should sit in the working seat. The sitting person stays while they are still assigned and present (no churn); otherwise the first present assigned professional takes it, so
        /// a colleague covers for one who has died or left. 0 means nobody is assigned. <paramref name="roster"/> is in assignment order.
        /// </summary>
        public static int Pick(IList<int> roster, int current, Func<int, bool> present)
        {
            if (current != 0 && roster.Contains(current) && present(current)) return current;
            foreach (int id in roster) if (present(id)) return id;
            return 0;
        }
    }

    public sealed partial class FamilyRegistry
    {
        /// <summary>
        /// Puts a person in (or empties) the working seat of a professional post, with the same effects as the post changing hands so classes, rounds and per-holder records settle
        /// correctly, but without any vote and without touching their other posts. Returns whether anything changed.
        /// </summary>
        public bool SeatService(OfficeKind kind, int person, int now)
        {
            var office = EnsureOffice(kind);
            if (person != 0 && GetPerson(person)?.InColony != true) person = 0;
            if (office.HolderId == person) return false;
            if (office.Held) EndOffice(office, person == 0 ? OfficeEnd.Left : OfficeEnd.Replaced, now);
            if (person == 0) return true;
            office.HolderId = person; office.HolderFamily = FamilyOf(person); office.ElectedTick = now; office.TermEndTick = 0; office.LastWorkTick = now;
            office.Work = 0; office.QualitySum = 0f; office.QualityCount = 0; office.UnmetSinceTick = 0; office.EndReason = OfficeEnd.None; office.EndedTick = 0; office.Terms++;
            HolderChanged?.Invoke(this, office, 0, person);
            return true;
        }
    }
}
