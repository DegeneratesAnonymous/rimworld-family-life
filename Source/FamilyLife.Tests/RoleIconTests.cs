using System;
using System.IO;
using System.Linq;
using FamilyLife.Domain;
using Xunit;

namespace FamilyLife.Tests
{
    public class RoleIconTests
    {
        // Elder (1) head of a house with Wife (2), Kid (3) and Kid2 (4); Outsider (9) is in the colony but in no house of this family.
        private static FamilyRegistry Home()
        {
            var r = new FamilyRegistry();
            r.Apply(new[]
            {
                T.P(1, "Elder", spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }),
                T.P(3, "Kid", parents: new[] { 1, 2 }), T.P(4, "Kid2", parents: new[] { 1, 2 }),
                T.P(9, "Friend")
            }, 1000);
            return r;
        }

        private static string RepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Textures"))) dir = dir.Parent;
            return dir?.FullName;
        }

        private static readonly RoleDisplayOptions Everything = RoleDisplayOptions.Everything;

        // ---- the supplied pictures

        [Fact]
        public void Every_implemented_role_maps_to_its_supplied_icon_and_the_file_is_a_64_pixel_png()
        {
            var expected = new System.Collections.Generic.Dictionary<FamilyRole, string>
            {
                { FamilyRole.HouseHead, "house_head" }, { FamilyRole.Steward, "steward" }, { FamilyRole.HouseProtector, "protector_eye" }, { FamilyRole.PublicHeir, "public_heir" },
                { FamilyRole.Associate, "associate" }, { FamilyRole.TrustedAssociate, "trusted_associate" }, { FamilyRole.Healer, "healer" }, { FamilyRole.Teacher, "teacher" },
                { FamilyRole.HallKeeper, "hall_keeper" }, { FamilyRole.Chaplain, "chaplain" }, { FamilyRole.ColonyLeader, "colony_leader" },
                { FamilyRole.CouncilRepresentative, "council_representative" }, { FamilyRole.CeremonyOrganizer, "ceremony_organizer" }
            };
            Assert.Equal(Enum.GetValues(typeof(FamilyRole)).Length, RoleCatalog.All.Count);
            string root = RepoRoot();
            Assert.NotNull(root);
            foreach (var p in RoleCatalog.All)
            {
                Assert.Equal("WilsFamilyLife/Roles/" + expected[p.Role], p.TexturePath);
                string file = Path.Combine(root, "Textures", p.TexturePath.Replace('/', Path.DirectorySeparatorChar) + ".png");
                Assert.True(File.Exists(file), file);
                var bytes = File.ReadAllBytes(file);
                Assert.Equal(0x89, bytes[0]);
                Assert.Equal(64, (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19]);        // IHDR width
                Assert.Equal(64, (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23]);        // IHDR height
                Assert.Equal("WFL_RoleName_" + p.Role, p.LabelKey);
                Assert.Equal("WFL_RoleDesc_" + p.Role, p.DescriptionKey);
            }
        }

        [Fact]
        public void Every_role_has_its_words_in_the_language_file()
        {
            string text = File.ReadAllText(Path.Combine(RepoRoot(), "Languages", "English", "Keyed", "Roles.xml"));
            foreach (var p in RoleCatalog.All)
            {
                Assert.Contains("<" + p.LabelKey + ">", text);
                Assert.Contains("<" + p.DescriptionKey + ">", text);
            }
        }

        [Fact]
        public void Reserved_and_alternative_icons_are_kept_but_never_loaded_or_made_into_roles()
        {
            string root = RepoRoot();
            foreach (var name in RoleCatalog.ReservedIcons)
            {
                Assert.True(File.Exists(Path.Combine(root, "Assets", "RoleIcons", "Reserved", name + ".png")), name);
                Assert.False(File.Exists(Path.Combine(root, "Textures", "WilsFamilyLife", "Roles", name + ".png")), name);
                Assert.DoesNotContain(RoleCatalog.All, p => p.TexturePath.EndsWith("/" + name));
            }
            foreach (var name in RoleCatalog.ProtectorAlternatives)
            {
                Assert.True(File.Exists(Path.Combine(root, "Assets", "RoleIcons", "ProtectorAlternatives", name + ".png")), name);
                Assert.False(File.Exists(Path.Combine(root, "Textures", "WilsFamilyLife", "Roles", name + ".png")), name);
            }
            // Exactly the roles that exist have an entry, and the only one without a mechanic is the Protector.
            Assert.Equal(new[] { FamilyRole.HouseProtector }, RoleCatalog.All.Where(p => !p.Implemented).Select(p => p.Role).ToArray());
        }

        [Fact]
        public void No_state_of_the_registry_makes_anyone_a_house_protector()
        {
            var r = Home();
            r.Institution.LeaderId = 1; r.Institution.Kind = InstitutionKind.Authoritarian;
            r.BeginStewardship(2, r.FamilyOf(1), 0);
            foreach (int id in new[] { 1, 2, 3, 4, 9 })
                Assert.DoesNotContain(RoleDisplay.PublicRoles(r, id, 0, Everything), e => e.Role == FamilyRole.HouseProtector);
        }

        [Fact]
        public void The_old_Watch_Captain_name_appears_nowhere_and_no_role_is_saved_by_name()
        {
            string root = RepoRoot();
            foreach (var file in Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
                         .Where(f => (f.EndsWith(".cs") || f.EndsWith(".xml") || f.EndsWith(".md")) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.EndsWith("RoleIconTests.cs") && !f.EndsWith("ROLE-ICONS.md") && !f.EndsWith("CHANGELOG.md")))
            {
                string text = File.ReadAllText(file);
                Assert.DoesNotContain("Watch Captain", text, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("WatchCaptain", text, StringComparison.OrdinalIgnoreCase);
            }
            // A role is worked out from the records and never written to a save, so renaming a label cannot break a save.
            Assert.DoesNotContain("FamilyRole", File.ReadAllText(Path.Combine(root, "Source", "FamilyLife", "Persistence", "Persist.cs")));
        }

        // ---- who holds what, and who may see it

        [Fact]
        public void A_head_a_steward_and_a_colony_leader_are_shown_with_their_house()
        {
            var r = Home();
            int house = r.FamilyOf(1);
            r.BeginStewardship(2, house, 0);
            var head = RoleDisplay.PublicRoles(r, 1, 0, Everything);
            Assert.Contains(head, e => e.Role == FamilyRole.HouseHead && e.FamilyId == house);
            Assert.Contains(RoleDisplay.PublicRoles(r, 2, 0, Everything), e => e.Role == FamilyRole.Steward && e.FamilyId == house);
            r.Institution.Kind = InstitutionKind.Authoritarian; r.Institution.LeaderId = 3;
            Assert.Equal(FamilyRole.ColonyLeader, RoleDisplay.PublicRoles(r, 3, 0, Everything)[0].Role);
        }

        [Fact]
        public void A_house_with_one_person_in_the_colony_has_no_head_emblem()
        {
            var r = new FamilyRegistry();
            r.Apply(new[] { T.P(1, "Solo") }, 1000);
            Assert.DoesNotContain(RoleDisplay.PublicRoles(r, 1, 0, Everything), e => e.Role == FamilyRole.HouseHead);
        }

        [Fact]
        public void A_secret_heir_stays_hidden_from_everyone_but_those_who_were_told()
        {
            var r = Home();
            int house = r.FamilyOf(1);
            Assert.True(r.DesignateHeir(house, 1, 3, new int[0], 2000));             // only the head and the heir know
            Assert.DoesNotContain(RoleDisplay.PublicRoles(r, 3, 0, Everything), e => e.Role == FamilyRole.PublicHeir);       // the player's own view shows only what is public
            Assert.DoesNotContain(RoleDisplay.PublicRoles(r, 3, 4, Everything), e => e.Role == FamilyRole.PublicHeir);       // as another family member
            Assert.Contains(RoleDisplay.PublicRoles(r, 3, 1, Everything), e => e.Role == FamilyRole.PublicHeir);              // as the head who chose
            Assert.Contains(RoleDisplay.PublicRoles(r, 3, 3, Everything), e => e.Role == FamilyRole.PublicHeir);              // as the heir
        }

        [Fact]
        public void An_heir_the_whole_adult_house_was_told_of_is_public()
        {
            var r = Home();
            int house = r.FamilyOf(1);
            Assert.True(r.DesignateHeir(house, 1, 3, new[] { 2, 4 }, 2000));
            Assert.Contains(RoleDisplay.PublicRoles(r, 3, 0, Everything), e => e.Role == FamilyRole.PublicHeir);
            Assert.Contains(RoleDisplay.PublicRoles(r, 3, 4, Everything), e => e.Role == FamilyRole.PublicHeir);
        }

        [Fact]
        public void An_invitation_not_yet_confirmed_does_not_reveal_who_is_organizing()
        {
            var r = Home();
            int house = r.FamilyOf(1);
            var plan = new CeremonyPlan { Id = 1, FamilyId = house, OrganizerId = 4, State = PlanState.Open };
            r.CeremonyPlans.Add(plan);
            Assert.DoesNotContain(RoleDisplay.PublicRoles(r, 4, 0, Everything), e => e.Role == FamilyRole.CeremonyOrganizer);
            Assert.DoesNotContain(RoleDisplay.PublicRoles(r, 4, 9, Everything), e => e.Role == FamilyRole.CeremonyOrganizer);       // a stranger
            Assert.Contains(RoleDisplay.PublicRoles(r, 4, 2, Everything), e => e.Role == FamilyRole.CeremonyOrganizer);              // someone in that family
            plan.State = PlanState.Confirmed;
            Assert.Contains(RoleDisplay.PublicRoles(r, 4, 0, Everything), e => e.Role == FamilyRole.CeremonyOrganizer);
        }

        [Fact]
        public void A_colony_post_is_a_public_role()
        {
            var r = Home();
            r.Offices.Add(new CommunityOffice { Id = 1, Kind = OfficeKind.Healer, HolderId = 4 });
            Assert.Contains(RoleDisplay.PublicRoles(r, 4, 0, Everything), e => e.Role == FamilyRole.Healer && e.FamilyId == 0);
        }

        // ---- one emblem, always the same one

        [Fact]
        public void Priority_is_colony_office_then_head_then_heir_then_steward_then_other_offices_then_associates()
        {
            var order = new[] { FamilyRole.ColonyLeader, FamilyRole.CouncilRepresentative, FamilyRole.HouseHead, FamilyRole.PublicHeir, FamilyRole.Steward, FamilyRole.HouseProtector, FamilyRole.CeremonyOrganizer, FamilyRole.TrustedAssociate, FamilyRole.Associate };
            var rank = order.Select(o => RoleCatalog.Get(o).DisplayPriority).ToArray();
            Assert.Equal(rank.OrderBy(x => x).ToArray(), rank);
            Assert.Equal(rank.Distinct().Count(), rank.Length);
            foreach (var colonyPost in new[] { FamilyRole.Healer, FamilyRole.Teacher, FamilyRole.HallKeeper, FamilyRole.Chaplain })
                Assert.True(RoleCatalog.Get(colonyPost).DisplayPriority < RoleCatalog.Get(FamilyRole.HouseHead).DisplayPriority);
        }

        [Fact]
        public void The_same_roles_in_any_order_give_the_same_emblem()
        {
            var roles = new[] { new RoleEntry(FamilyRole.Steward, 2), new RoleEntry(FamilyRole.HouseHead, 3), new RoleEntry(FamilyRole.HouseHead, 1), new RoleEntry(FamilyRole.ColonyLeader, 0), new RoleEntry(FamilyRole.Associate, 2) };
            RoleEntry first = default(RoleEntry);
            for (int shuffle = 0; shuffle < 6; shuffle++)
            {
                var list = roles.OrderBy(x => (x.FamilyId * 7 + (int)x.Role * 13 + shuffle * 5) % 11).ToList();
                list.Sort(RoleDisplay.Compare);
                Assert.True(RoleDisplay.TryPrimary(list, true, false, out var primary));
                if (shuffle == 0) first = primary; else Assert.Equal(first, primary);
            }
            Assert.Equal(FamilyRole.ColonyLeader, first.Role);
        }

        [Fact]
        public void Someone_who_is_head_and_steward_elsewhere_shows_the_head_emblem_and_lists_both()
        {
            var r = Home();
            int house = r.FamilyOf(1);
            r.Offices.Add(new CommunityOffice { Id = 1, Kind = OfficeKind.Teacher, HolderId = 1 });
            var roles = RoleDisplay.PublicRoles(r, 1, 0, Everything);
            Assert.True(roles.Count >= 2);
            Assert.True(RoleDisplay.TryPrimary(roles, true, false, out var primary));
            Assert.Equal(FamilyRole.Teacher, primary.Role);          // a colony post outranks heading a house
            Assert.Contains(roles, e => e.Role == FamilyRole.HouseHead && e.FamilyId == house);
        }

        // ---- associates and the settings

        private static void Befriend(FamilyRegistry r, int person, int family, bool trusted)
        {
            r.HouseContacts.Add(new HouseContact { PersonId = person, FamilyId = family, ContactDays = 10, ServiceDays = trusted ? 12 : 4, Respect = trusted ? 30 : 8 });
        }

        [Fact]
        public void Associates_follow_the_settings()
        {
            var r = Home();
            int house = r.FamilyOf(1);
            Befriend(r, 9, house, trusted: false);
            Assert.Equal(HouseRole.Associate, r.RoleInHouse(9, house));
            Assert.Empty(RoleDisplay.PublicRoles(r, 9, 0, new RoleDisplayOptions()));
            var shown = RoleDisplay.PublicRoles(r, 9, 0, new RoleDisplayOptions { ShowAssociates = true });
            Assert.Single(shown);
            Assert.False(RoleDisplay.TryPrimary(shown, true, false, out _));            // hidden unless the setting is on
            Assert.True(RoleDisplay.TryPrimary(shown, false, true, out var p));
            Assert.Equal(FamilyRole.Associate, p.Role);
        }

        [Fact]
        public void Trusted_associates_are_hidden_when_only_leadership_is_shown_and_shown_otherwise()
        {
            var r = Home();
            int house = r.FamilyOf(1);
            Befriend(r, 9, house, trusted: true);
            var roles = RoleDisplay.PublicRoles(r, 9, 0, new RoleDisplayOptions { ShowTrustedAssociates = true });
            Assert.Equal(FamilyRole.TrustedAssociate, roles.Single().Role);
            Assert.False(RoleDisplay.TryPrimary(roles, true, false, out _));
            Assert.True(RoleDisplay.TryPrimary(roles, false, false, out var p));
            Assert.Equal(FamilyRole.TrustedAssociate, p.Role);
        }

        // ---- changes show up

        [Fact]
        public void A_change_of_head_or_membership_changes_the_answer_without_any_cache_in_the_domain()
        {
            var r = Home();
            int house = r.FamilyOf(1);
            Assert.Contains(RoleDisplay.PublicRoles(r, 1, 0, Everything), e => e.Role == FamilyRole.HouseHead);
            r.Apply(new[]
            {
                T.P(1, "Elder", presence: Presence.Dead, spouses: new[] { 2 }), T.P(2, "Wife", spouses: new[] { 1 }),
                T.P(3, "Kid", parents: new[] { 1, 2 }), T.P(4, "Kid2", parents: new[] { 1, 2 }), T.P(9, "Friend")
            }, 5000);
            Assert.Empty(RoleDisplay.PublicRoles(r, 1, 0, Everything));
            int newHead = r.HeadOf(house);
            Assert.NotEqual(1, newHead);
            Assert.Contains(RoleDisplay.PublicRoles(r, newHead, 0, Everything), e => e.Role == FamilyRole.HouseHead);
        }

        // ---- the cache and fallback live in the game layer, which these tests cannot load; the contract is checked from the source

        [Fact]
        public void The_drawing_path_uses_cached_state_and_never_loads_textures_or_scans()
        {
            string src = File.ReadAllText(Path.Combine(RepoRoot(), "Source", "FamilyLife", "Game", "PostBadges.cs"));
            int start = src.IndexOf("internal static void Draw(Rect box, Pawn pawn)", StringComparison.Ordinal);
            int end = src.IndexOf("private static void Open(", start, StringComparison.Ordinal);
            string draw = src.Substring(start, end - start);
            Assert.DoesNotContain("ContentFinder", draw);
            Assert.DoesNotContain("PublicRoles", draw);
            Assert.DoesNotContain("AllPawns", draw);
            Assert.DoesNotContain(".Select(", draw);
            Assert.DoesNotContain(".Where(", draw);
            Assert.DoesNotContain("new List", draw);
            Assert.DoesNotContain("=>", draw);
            Assert.Contains("RoleBar.TryGet", draw);
            // Textures are loaded once per role, with one warning and a fallback.
            Assert.Contains("warned.Add", src);
            Assert.Contains("BaseContent.BadTex", src);
            Assert.Contains("RoleCatalog.GenericTexture", src);
            // The cache is invalidated by the family records, the settings, the person played, and time.
            Assert.Contains("e.Revision != component.Revision", src);
            Assert.Contains("e.Stamp != stamp", src);
            Assert.Contains("e.Observer != observer", src);
        }
    }
}
