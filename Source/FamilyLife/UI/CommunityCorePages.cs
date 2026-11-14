using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>Community summaries and links into the existing family, office and assignment systems.</summary>
    internal static class CommunityCorePages
    {
        private static string selectedCareer;
        private static string professionSearch = "";

        private static string coreModule = "wils.familylife";
        /// <summary>The id of one of this module's own pages (its pages carry the module's id, not the framework's).</summary>
        internal static string PageId(string name) => coreModule + "." + name;

        public static void Register(string moduleId)
        {
            coreModule = moduleId;
            Add(moduleId, "overview", "WFL_Hub_Overview", 0, Overview, null);
            Add(moduleId, "families", "WFL_Hub_Families", 10, Families, null);
            Add(moduleId, "professions", "WFL_Hub_Professions", 20, Professions, null);
            Add(moduleId, "assembly", "WFL_Hub_Assembly", 30, Assembly, null);
            Add(moduleId, "settings", "WFL_Hub_Settings", 90, Settings, null);
        }

        private static void Add(string moduleId, string name, string label, int order, Func<Rect, FamilyComponent, float> render, string icon) =>
            FamilyFramework.Pages.Register(new CommunityPage<Func<Rect, FamilyComponent, float>> { Id = moduleId + "." + name, ModuleId = moduleId, LabelKey = label, Order = order, Icon = icon, Render = render });

        internal static float Heading(Rect r, float y, string text)
        {
            float h = Mathf.Max(28f, Text.CalcHeight(text, r.width - 16f) + 10f);
            Widgets.DrawBoxSolid(new Rect(0f, y, r.width, h), new Color(.18f, .18f, .19f));
            GUI.color = FamilyKit.Gold; Widgets.Label(new Rect(8f, y + 5f, r.width - 16f, h - 5f), text); GUI.color = Color.white;
            return y + h + 8f;
        }
        internal static float Line(Rect r, float y, string text, bool dim = false) => CommunityPageLayout.Line(r, y, text, dim);
        internal static string NameOf(FamilyComponent c, int id) => FamilyUi.NameOf(c.Registry, id);

        private static float Posts(Rect r, FamilyComponent c)
        {
            float y = 0f;
            foreach (var d in FamilyFramework.Offices.All.OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                if (!FamilyFramework.Offices.Available(d.Kind)) continue;
                var office = c.Registry.OfficeFor(d.Kind);
                if (d.Kind == OfficeKind.Chaplain && !(office != null && office.Held)) continue;
                y = Line(r, y, d.LabelKey.Translate() + ": " + (office != null && office.Held ? NameOf(c, office.HolderId) : "WFL_Hub_Vacant".Translate().ToString()));
            }
            return y;
        }

        private static float Modules(Rect r, FamilyComponent c)
        {
            float y = 0f;
            foreach (var m in FamilyFramework.Catalog.Modules.Where(x => !x.IsFramework).OrderBy(x => x.Id, StringComparer.Ordinal))
                {
                y = Line(r, y, m.Name + ": " + FamilyFramework.Describe(c, m.Id));
                if (FamilyFramework.Failures.TryGetValue(m.Id, out var why) && !string.IsNullOrEmpty(why)) y = Line(r, y, why, true);
            }
            return y;
        }

        private static float Overview(Rect r, FamilyComponent c)
        {
            var reg = c.Registry; float w = r.width, y = 0f;
            int people = reg.Persons.Count(p => p.InColony), children = reg.Persons.Count(p => p.InColony && p.Juvenile);
            var families = reg.Lineages.Where(l => reg.IsListedFamily(l.Id)).OrderBy(l => l.Name).ToList();
            var roles = CareerService.Professions.All.Where(x => FamilyFramework.Catalog.IsEnabled(x.ModuleId)).ToList(); var book = reg.Assignments();
            int staffed = roles.Count(x => book.Holders(x.Id).Any(id => reg.GetPerson(id)?.InColony == true));
            string government = ("WFL_Institution_" + reg.Institution.Kind).Translate().ToString().CapitalizeFirst();

            // --- banner: the colony's name and the one line that sums it up
            var banner = new Rect(0f, 0f, w, 78f);
            Widgets.DrawBoxSolid(banner, new Color(.13f, .13f, .15f)); Widgets.DrawBoxSolid(new Rect(0f, 0f, 5f, banner.height), FamilyKit.Gold);
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(20f, 10f, w - 40f, 34f), Faction.OfPlayer?.Name ?? "");
            Text.Font = GameFont.Small; GUI.color = Color.gray;
            Widgets.Label(new Rect(20f, 46f, w - 40f, 24f), "WFL_Ov_Summary".Translate(people, families.Count, government).ToString()); GUI.color = Color.white;
            y = banner.height + 12f;

            // --- figures, each one a way in to the page behind it
            bool wide = w >= 600f; int perRow = wide ? 4 : 2; float gap = 10f, tw = (w - gap * (perRow - 1)) / perRow, th = 74f;
            string nextAssembly = reg.NextAssemblyTick > 0 ? FamilyUi.Date(reg.NextAssemblyTick) : "-";
            var tiles = new[]
            {
                new Tile("WFL_Ov_Residents", people.ToString(), children > 0 ? "WFL_Ov_Children".Translate(children).ToString() : "", PageId("families")),
                new Tile("WFL_Ov_Families", families.Count.ToString(), "", PageId("families")),
                new Tile("WFL_Ov_Professions", staffed + " / " + roles.Count, "WFL_Ov_Staffed".Translate().ToString(), PageId("professions")),
                new Tile("WFL_Ov_Assembly", nextAssembly, "", PageId("assembly")),
            };
            for (int i = 0; i < tiles.Length; i++) DrawTile(new Rect(i % perRow * (tw + gap), y + i / perRow * (th + gap), tw, th), tiles[i]);
            y += ((tiles.Length + perRow - 1) / perRow) * (th + gap) + 8f;

            // --- the two public seats
            y = Rule(w, y, "WFL_Ov_Leadership".Translate());
            int leader = reg.Institution.Kind == InstitutionKind.Consensus ? 0 : reg.Institution.LeaderId; if (reg.GetPerson(leader)?.InColony != true) leader = 0;
            var ideo = OfficeDriver.PopularIdeo(); int speaker = reg.OfficeFor(OfficeKind.Chaplain) is CommunityOffice co && co.Held ? co.HolderId : 0;
            int seats = speaker != 0 ? 2 : 1; float sw = seats == 2 && wide ? (w - gap) / 2f : w, sh = 92f; float sy = y;
            Seat(new Rect(0f, sy, sw, sh), c, FamilyRole.ColonyLeader, leader, leader != 0 ? government : government + " - " + "WFL_Ov_NoLeader".Translate(), new Color(.85f, .7f, .3f));
            if (speaker != 0) { bool side = wide; Seat(new Rect(side ? sw + gap : 0f, side ? sy : sy + sh + gap, sw, sh), c, FamilyRole.Chaplain, speaker, ideo != null ? ideo.name : "", new Color(.55f, .65f, .9f)); }
            y = sy + (seats == 2 && !wide ? sh * 2f + gap : sh) + 14f;

            // --- the families, as chips
            if (families.Count > 0)
            {
                y = Rule(w, y, "WFL_Hub_Families".Translate());
                float cw = Mathf.Min(220f, (w - gap) / 2f), x = 0f;
                foreach (var f in families)
                {
                    if (x + cw > w + .5f) { x = 0f; y += 52f; }
                    var chip = new Rect(x, y, cw, 44f); int id = f.Id;
                    Widgets.DrawBoxSolid(chip, new Color(.15f, .15f, .17f)); if (Mouse.IsOver(chip)) Widgets.DrawHighlight(chip);
                    Widgets.Label(new Rect(chip.x + 10f, chip.y + 4f, chip.width - 20f, 22f), f.Name.Truncate(chip.width - 20f));
                    GUI.color = Color.gray; Widgets.Label(new Rect(chip.x + 10f, chip.y + 22f, chip.width - 20f, 20f), "WFL_Hub_FamilyResidents".Translate(reg.LivingCount(id, Presence.Colony)).ToString()); GUI.color = Color.white;
                    if (Widgets.ButtonInvisible(chip)) { Window_Families.PendingLineageId = id; if (Find.WindowStack.WindowOfType<Window_Families>() == null) Find.WindowStack.Add(new Window_Families()); }
                    x += cw + gap;
                }
                y += 52f + 8f;
            }

            // --- modules: a dot and a name, nothing to read
            y = Rule(w, y, "WFL_Hub_Modules".Translate());
            float mx = 0f;
            foreach (var m in FamilyFramework.Catalog.Modules.Where(q => !q.IsFramework).OrderBy(q => q.Id, StringComparer.Ordinal))
            {
                string state = FamilyFramework.Describe(c, m.Id); string name = m.Name.Replace("Wil's Role Modules : ", "");
                float pw = Text.CalcSize(name).x + 36f;
                if (mx + pw > w) { mx = 0f; y += 32f; }
                var pill = new Rect(mx, y, pw, 26f); Widgets.DrawBoxSolid(pill, new Color(.15f, .15f, .17f));
                bool bad = state == "Refused" || state == "Absent", on = state == "Active";
                Widgets.DrawBoxSolid(new Rect(pill.x + 9f, pill.center.y - 4f, 8f, 8f), bad ? new Color(.85f, .35f, .3f) : on ? new Color(.45f, .75f, .45f) : new Color(.55f, .55f, .55f));
                Widgets.Label(new Rect(pill.x + 24f, pill.y + 2f, pill.width - 28f, 22f), name);
                string why = FamilyFramework.Failures.TryGetValue(m.Id, out var f2) ? "\n" + f2 : "";
                TooltipHandler.TipRegion(pill, state + why);
                mx += pw + 8f;
            }
            return y + 34f;
        }

        private struct Tile { public string Label, Big, Small, Page; public Tile(string l, string b, string s, string p) { Label = l; Big = b; Small = s; Page = p; } }

        private static void DrawTile(Rect t, Tile tile)
        {
            Widgets.DrawBoxSolid(t, new Color(.15f, .15f, .17f)); if (Mouse.IsOver(t)) Widgets.DrawHighlight(t);
            Widgets.DrawBoxSolid(new Rect(t.x, t.yMax - 2f, t.width, 2f), new Color(FamilyKit.Gold.r, FamilyKit.Gold.g, FamilyKit.Gold.b, .55f));
            Text.Font = GameFont.Tiny; GUI.color = Color.gray; Widgets.Label(new Rect(t.x + 12f, t.y + 6f, t.width - 24f, 16f), tile.Label.Translate().ToString().ToUpperInvariant());
            GUI.color = Color.white; Text.Font = GameFont.Medium; Widgets.Label(new Rect(t.x + 12f, t.y + 22f, t.width - 24f, 30f), tile.Big);
            Text.Font = GameFont.Tiny; GUI.color = Color.gray; if (tile.Small.Length > 0) Widgets.Label(new Rect(t.x + 12f, t.y + 52f, t.width - 24f, 16f), tile.Small);
            GUI.color = Color.white; Text.Font = GameFont.Small;
            if (Widgets.ButtonInvisible(t)) MainTabWindow_Community.Open(tile.Page);
        }

        private static float Rule(float w, float y, string text)
        {
            Text.Font = GameFont.Tiny; GUI.color = FamilyKit.Gold; Widgets.Label(new Rect(0f, y, w, 18f), text.ToUpperInvariant()); GUI.color = Color.white; Text.Font = GameFont.Small;
            Widgets.DrawBoxSolid(new Rect(0f, y + 20f, w, 1f), new Color(1f, 1f, 1f, .12f));
            return y + 28f;
        }

        /// <summary>One public seat: the person's portrait and name, or an empty seat when nobody holds it.</summary>
        private static void Seat(Rect card, FamilyComponent c, FamilyRole role, int person, string subtitle, Color accent)
        {
            Widgets.DrawBoxSolid(card, new Color(.14f, .14f, .16f)); Widgets.DrawBoxSolid(new Rect(card.x, card.y, 4f, card.height), person != 0 ? accent : new Color(.35f, .35f, .37f));
            var pawn = person != 0 ? c.TryGetPawn(person) : null;
            var pr = new Rect(card.x + 16f, card.y + 10f, 72f, card.height - 20f);
            if (pawn != null) GUI.DrawTexture(pr, PortraitsCache.Get(pawn, pr.size, Rot4.South, new Vector3(0f, 0f, 0.1f), 1.15f));
            else { Widgets.DrawBoxSolid(pr, new Color(.1f, .1f, .11f)); Text.Anchor = TextAnchor.MiddleCenter; Text.Font = GameFont.Medium; GUI.color = new Color(1f, 1f, 1f, .25f); Widgets.Label(pr, "?"); GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; }
            float x = pr.xMax + 14f, tw = card.xMax - x - 10f;
            Text.Font = GameFont.Tiny; GUI.color = accent; Widgets.Label(new Rect(x, card.y + 12f, tw, 16f), RoleIcons.Label(role).ToUpperInvariant());
            Text.Font = GameFont.Medium; GUI.color = person != 0 ? Color.white : Color.gray; Widgets.Label(new Rect(x, card.y + 28f, tw, 30f), person != 0 ? NameOf(c, person) : "WFL_Hub_Vacant".Translate().CapitalizeFirst().ToString());
            Text.Font = GameFont.Small; GUI.color = Color.gray; Widgets.Label(new Rect(x, card.y + 60f, tw, 24f), subtitle.Truncate(tw)); GUI.color = Color.white;
            TooltipHandler.TipRegion(card, RoleIcons.Description(role));
            if (person != 0 && Widgets.ButtonInvisible(card))
            {
                var lineage = c.Registry.FamilyOf(person); if (lineage != 0) Window_Families.PendingLineageId = lineage;
                if (Find.WindowStack.WindowOfType<Window_Families>() == null) Find.WindowStack.Add(new Window_Families());
            }
        }

        private static float Families(Rect r, FamilyComponent c)
        {
            float y = Line(r, 0f, "WFL_Hub_FamiliesHelp".Translate(), true);
            var families = c.Registry.Lineages.Where(l => c.Registry.IsListedFamily(l.Id)).OrderBy(l => l.Name).ToList();
            if (families.Count == 0) y = Line(r, y, "WFL_NoFamilies".Translate(), true);
            float w = CommunityPageLayout.ColumnWidth(r), left = y, right = y;
            int index = 0;
            foreach (var family in families)
            {
                int id = family.Id;
                bool second = CommunityPageLayout.TwoColumns(r) && index++ % 2 == 1;
                float end = CommunityPageLayout.Section(r, second ? w + CommunityPageLayout.Gap : 0f, second ? right : left, w, family.Name, a =>
                {
                    float h = Line(a, 0f, "WFL_Hub_FamilyResidents".Translate(c.Registry.LivingCount(id, Presence.Colony)));
                    return CommunityPageLayout.Button(a, h, "WFL_Hub_OpenFamilies".Translate(), () =>
                    {
                        Window_Families.PendingLineageId = id;
                        if (Find.WindowStack.WindowOfType<Window_Families>() == null) Find.WindowStack.Add(new Window_Families());
                    });
                });
                if (second) right = end; else left = end;
            }
            y = Mathf.Max(left, right);
            return CommunityPageLayout.Button(new Rect(0f, 0f, Mathf.Min(w, r.width), r.height), y, "WFL_Hub_AllFamilies".Translate(), () =>
            {
                if (Find.WindowStack.WindowOfType<Window_Families>() == null) Find.WindowStack.Add(new Window_Families());
            });
        }

        private static float Professions(Rect r, FamilyComponent c)
        {
            var reg = c.Registry; var book = reg.Assignments(); var ledger = reg.Careers();
            var roles = CareerService.Professions.All.Where(x => FamilyFramework.Catalog.IsEnabled(x.ModuleId)).OrderBy(x => x.Career, StringComparer.Ordinal).ThenBy(x => x.Level).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            if (roles.Count == 0) return Line(r, 0f, "WFL_Hub_NoProfessions".Translate(), true);
            float y = Line(r, 0f, "WFL_Hub_ProfessionsHelp".Translate(), true);
            var careers = roles.Select(x => x.Career).Distinct().ToList();
            if (!careers.Contains(selectedCareer)) selectedCareer = careers[0];
            Func<string, string> careerName = id => CareerService.Career(id) != null ? CareerService.CareerName(CareerService.Career(id), reg) : id;
            y = CommunityPageLayout.Button(r, y, careerName(selectedCareer), () => Find.WindowStack.Add(new FloatMenu(careers.Select(id => new FloatMenuOption(careerName(id), () => { selectedCareer = id; professionSearch = ""; })).ToList())));
            Widgets.Label(new Rect(0f, y, 100f, 28f), "WFL_Hub_Search".Translate());
            professionSearch = Widgets.TextField(new Rect(104f, y, r.width - 104f, 28f), professionSearch);
            y += 38f;
            var shown = roles.Where(role => role.Career == selectedCareer && (professionSearch.Length == 0 || CareerService.TitleOf(role, reg).IndexOf(professionSearch, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (shown.Count == 0) return Line(r, y, "WFL_Hub_NoMatches".Translate(), true);
            return ProfessionTree.Draw(r, y, c, selectedCareer, roles.Where(role => role.Career == selectedCareer).ToList(), professionSearch);
        }

        private static float Assembly(Rect r, FamilyComponent c)
        {
            float y = Line(r, 0f, "WFL_Hub_AssemblyHelp".Translate(), true), w = CommunityPageLayout.ColumnWidth(r);
            float left = CommunityPageLayout.Section(r, 0f, y, w, "WFL_Hub_Posts".Translate(), a => Posts(a, c));
            bool columns = CommunityPageLayout.TwoColumns(r);
            float right = CommunityPageLayout.Section(r, columns ? w + CommunityPageLayout.Gap : 0f, columns ? y : left, w, "WFL_Hub_Assembly".Translate(), a =>
            {
                float h = 0f; var reg = c.Registry;
                if (reg.NextAssemblyTick > 0) h = Line(a, h, "WFL_Assembly_Next".Translate(FamilyUi.Date(reg.NextAssemblyTick)));
                if (reg.NextAssemblyTick > 0 && reg.OpenAssembly == null)
                    h = CommunityPageLayout.Button(a, h, "WFL_Assembly_Postpone".Translate(), () => reg.PostponeAssembly(Find.TickManager.TicksGame, 30));
                if (reg.OpenAssembly != null)
                    foreach (var race in reg.OpenAssembly.Races)
                        h = Line(a, h, "WFL_Assembly_RaceLine".Translate(("WFL_OfficeKind_" + race.Kind).Translate(), ("WFL_AssemblyState_" + reg.OpenAssembly.State).Translate(), FamilyUi.NameList(reg, race.Candidates)));
                return h;
            });
            return Mathf.Max(left, right);
        }

        private static float Settings(Rect r, FamilyComponent c)
        {
            float y = CommunityPageLayout.Button(r, 0f, "WFL_Hub_OpenSettings".Translate(), () =>
            {
                var mod = LoadedModManager.GetMod<FamilyLifeMod>();
                if (mod != null) Find.WindowStack.Add(new Dialog_ModSettings(mod));
            });
            y = CommunityPageLayout.Section(r, 0f, y, r.width, "WFL_Hub_Modules".Translate(), a => Modules(a, c));
            foreach (var f in FamilyFramework.Failures) y = Line(r, y, f.Key + ": " + f.Value, true);
            return y;
        }
    }
}
