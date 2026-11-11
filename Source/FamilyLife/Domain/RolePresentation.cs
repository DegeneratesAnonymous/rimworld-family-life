using System;
using System.Collections.Generic;

namespace FamilyLife.Domain
{
    /// <summary>
    /// The family roles that have an emblem. A role is listed here only when something in the game can make a pawn hold it, with one reserved
    /// exception, <see cref="HouseProtector"/>, which has its icon, label and description but no mechanic yet: nothing produces it.
    /// Never serialized: roles are worked out from the registry's own records, so the numbers here can change freely.
    /// </summary>
    public enum FamilyRole : byte
    {
        ColonyLeader, CouncilRepresentative, Healer, Teacher, HallKeeper, Chaplain,
        HouseHead, PublicHeir, Steward, HouseProtector, CeremonyOrganizer, TrustedAssociate, Associate
    }

    /// <summary>
    /// Everything the interface needs to show one role, in one place: translation keys, texture, tint and where it ranks. No window,
    /// colonist bar or tooltip holds its own copy. Texture paths are relative to the Textures folder; the tint is multiplied into the picture
    /// (white leaves the supplied artwork as drawn).
    /// </summary>
    public sealed class FamilyRolePresentation
    {
        public FamilyRole Role;
        public string LabelKey, DescriptionKey, TexturePath;
        public float TintR = 1f, TintG = 1f, TintB = 1f;
        /// <summary>Lower is shown first when a pawn holds several roles.</summary>
        public int DisplayPriority;
        /// <summary>False for a reserved role: it has presentation but nothing in the game assigns it.</summary>
        public bool Implemented = true;
        /// <summary>A colony-wide post or a family office (as against an associate's standing). Used by the "leadership and appointed only" setting.</summary>
        public bool Leadership = true;
        public bool ColonyWide;
    }

    /// <summary>One role a pawn holds as far as an observer may know. FamilyId is 0 for a colony-wide post.</summary>
    public struct RoleEntry
    {
        public FamilyRole Role;
        public int FamilyId;
        public RoleEntry(FamilyRole role, int family) { Role = role; FamilyId = family; }
    }

    public sealed class RoleDisplayOptions
    {
        public bool ShowTrustedAssociates = false, ShowAssociates = false;
        public static RoleDisplayOptions Everything => new RoleDisplayOptions { ShowTrustedAssociates = true, ShowAssociates = true };
    }

    public static class RoleCatalog
    {
        public const string TextureFolder = "WilsFamilyLife/Roles/";
        public const string GenericTexture = TextureFolder + "associate";

        private static FamilyRolePresentation P(FamilyRole role, string file, int priority, bool implemented = true, bool leadership = true, bool colonyWide = false) => new FamilyRolePresentation
        {
            Role = role, LabelKey = "WFL_RoleName_" + role, DescriptionKey = "WFL_RoleDesc_" + role, TexturePath = TextureFolder + file,
            DisplayPriority = priority, Implemented = implemented, Leadership = leadership, ColonyWide = colonyWide
        };

        /// <summary>The registry. Order of priority: colony-wide office, house head, public heir, steward, house protector, other appointed family office, trusted associate, associate.</summary>
        public static readonly IReadOnlyList<FamilyRolePresentation> All = new[]
        {
            P(FamilyRole.ColonyLeader, "colony_leader", 10, colonyWide: true),
            P(FamilyRole.CouncilRepresentative, "council_representative", 11, colonyWide: true),
            P(FamilyRole.Healer, "healer", 12, colonyWide: true),
            P(FamilyRole.Teacher, "teacher", 13, colonyWide: true),
            P(FamilyRole.HallKeeper, "hall_keeper", 14, colonyWide: true),
            P(FamilyRole.Chaplain, "chaplain", 15, colonyWide: true),
            P(FamilyRole.HouseHead, "house_head", 20),
            P(FamilyRole.PublicHeir, "public_heir", 30),
            P(FamilyRole.Steward, "steward", 40),
            P(FamilyRole.HouseProtector, "protector_eye", 50, implemented: false),
            P(FamilyRole.CeremonyOrganizer, "ceremony_organizer", 60),
            P(FamilyRole.TrustedAssociate, "trusted_associate", 70, leadership: false),
            P(FamilyRole.Associate, "associate", 80, leadership: false)
        };

        /// <summary>Icons that are kept in Assets/RoleIcons for a role that does not exist yet. They are never loaded or shown.</summary>
        public static readonly string[] ReservedIcons = { "mediator", "quartermaster", "envoy", "judge", "chronicler", "caravan_guide", "heirloom_keeper", "caregiver" };
        /// <summary>Other House Protector pictures, kept for a future choice. Not separate roles.</summary>
        public static readonly string[] ProtectorAlternatives = { "protector_spears", "protector_tower", "protector_house" };

        private static readonly Dictionary<FamilyRole, FamilyRolePresentation> byRole = Build();
        private static Dictionary<FamilyRole, FamilyRolePresentation> Build()
        {
            var map = new Dictionary<FamilyRole, FamilyRolePresentation>();
            foreach (var p in All) map[p.Role] = p;
            return map;
        }

        public static FamilyRolePresentation Get(FamilyRole role) => byRole[role];
    }

    /// <summary>What a pawn's emblem should be, decided from the registry. Pure, so the colonist bar can cache the answer.</summary>
    public static class RoleDisplay
    {
        public const int MaxAssociateHouses = 3;

        /// <summary>Whether a designated heir is known to this observer. With no observer (0), only an heir the whole adult house knows about counts as public.</summary>
        public static bool HeirKnownTo(FamilyRegistry r, Lineage house, int observer)
        {
            if (house == null || house.DesignatedHeirId == 0) return false;
            var known = house.DesignationKnownBy;
            if (known == null) return false;
            if (observer != 0) return known.Contains(observer);
            bool any = false;
            foreach (var m in r.MembersOf(house.Id))
            {
                if (!m.InColony || m.Juvenile) continue;
                any = true;
                if (!known.Contains(m.Id)) return false;
            }
            return any;
        }

        /// <summary>
        /// The roles this pawn holds that the observer may see. <paramref name="observer"/> is the person being played (0 when no one is):
        /// a secret heir, an invitation not yet told and a standing no one has recognised stay out.
        /// </summary>
        public static List<RoleEntry> PublicRoles(FamilyRegistry r, int person, int observer, RoleDisplayOptions options = null)
        {
            options = options ?? new RoleDisplayOptions();
            var list = new List<RoleEntry>();
            var record = r?.GetPerson(person);
            if (record == null || !record.InColony) return list;

            if (r.Institution.LeaderId == person && r.Institution.Kind != InstitutionKind.Consensus) list.Add(new RoleEntry(FamilyRole.ColonyLeader, 0));
            foreach (var office in r.OfficesHeldBy(person)) list.Add(new RoleEntry(OfficeRole(office.Kind), 0));

            int family = r.FamilyOf(person);
            var house = family == 0 ? null : r.GetLineage(family);
            if (house != null)
            {
                if (r.HeadOf(family) == person)
                {
                    if (r.Institution.Kind == InstitutionKind.Council && r.Institution.CouncilHouseIds.Contains(family)) list.Add(new RoleEntry(FamilyRole.CouncilRepresentative, family));
                    if (CountInColony(r, family) > 1) list.Add(new RoleEntry(FamilyRole.HouseHead, family));
                }
                else if (r.StewardshipOf(person, family, true) != null) list.Add(new RoleEntry(FamilyRole.Steward, family));
                if (house.DesignatedHeirId == person && HeirKnownTo(r, house, observer)) list.Add(new RoleEntry(FamilyRole.PublicHeir, family));
            }

            foreach (var plan in r.CeremonyPlans)
            {
                if (plan.OrganizerId != person) continue;
                bool visible = plan.State == PlanState.Confirmed || plan.State == PlanState.Open && observer != 0 && (observer == person || r.FamilyOf(observer) == plan.FamilyId || plan.KnownBy.Contains(observer));
                if (visible) { list.Add(new RoleEntry(FamilyRole.CeremonyOrganizer, plan.FamilyId)); break; }
            }

            if (options.ShowTrustedAssociates || options.ShowAssociates)
            {
                int houses = 0;
                foreach (var contact in r.HouseContacts)
                {
                    if (contact.PersonId != person || houses >= MaxAssociateHouses) continue;
                    var standing = r.RoleInHouse(person, contact.FamilyId);
                    if (standing == HouseRole.TrustedAssociate && (options.ShowTrustedAssociates || options.ShowAssociates)) { list.Add(new RoleEntry(FamilyRole.TrustedAssociate, contact.FamilyId)); houses++; }
                    else if (standing == HouseRole.Associate && options.ShowAssociates) { list.Add(new RoleEntry(FamilyRole.Associate, contact.FamilyId)); houses++; }
                }
            }
            list.Sort(Compare);
            return list;
        }

        private static int CountInColony(FamilyRegistry r, int family)
        {
            int n = 0;
            foreach (var m in r.MembersOf(family)) if (m.InColony) n++;
            return n;
        }

        public static FamilyRole OfficeRole(OfficeKind kind)
        {
            switch (kind)
            {
                case OfficeKind.Healer: return FamilyRole.Healer;
                case OfficeKind.Teacher: return FamilyRole.Teacher;
                case OfficeKind.HallKeeper: return FamilyRole.HallKeeper;
                default: return FamilyRole.Chaplain;
            }
        }

        /// <summary>Deterministic order: by display priority, then by house, so the same roles always give the same emblem.</summary>
        public static int Compare(RoleEntry a, RoleEntry b)
        {
            int c = RoleCatalog.Get(a.Role).DisplayPriority.CompareTo(RoleCatalog.Get(b.Role).DisplayPriority);
            return c != 0 ? c : a.FamilyId.CompareTo(b.FamilyId);
        }

        /// <summary>The one role to show on the colonist bar, or false when the settings leave none. A list sorted by <see cref="Compare"/> is expected.</summary>
        public static bool TryPrimary(List<RoleEntry> sorted, bool leadershipOnly, bool showAssociates, out RoleEntry primary)
        {
            for (int i = 0; i < sorted.Count; i++)
            {
                var entry = sorted[i];
                var presentation = RoleCatalog.Get(entry.Role);
                if (entry.Role == FamilyRole.Associate && !showAssociates) continue;
                if (leadershipOnly && !presentation.Leadership) continue;
                primary = entry; return true;
            }
            primary = default(RoleEntry); return false;
        }
    }
}
