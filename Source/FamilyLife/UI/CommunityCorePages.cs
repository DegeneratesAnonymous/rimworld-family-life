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

        public static void Register(string moduleId)
        {
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
            var reg = c.Registry; bool side = r.width >= 560f; float gap = CommunityPageLayout.Gap, cw = side ? (r.width - gap) / 2f : r.width, h = 132f;
            int leader = reg.Institution.Kind == InstitutionKind.Consensus ? 0 : reg.Institution.LeaderId; if (reg.GetPerson(leader)?.InColony != true) leader = 0;
            var ideo = OfficeDriver.PopularIdeo(); int speaker = reg.OfficeFor(OfficeKind.Chaplain) is CommunityOffice co && co.Held ? co.HolderId : 0;
            string govern = ("WFL_Institution_" + reg.Institution.Kind).Translate().ToString().CapitalizeFirst();
            LeaderCard(new Rect(0f, 0f, cw, h), c, FamilyRole.ColonyLeader, leader, govern, new Color(.85f, .7f, .3f));
            // the chaplain is the moral guide of the colony's biggest ideoligion; with no moral guide there is no chaplain, so no card
            if (speaker != 0) LeaderCard(new Rect(side ? cw + gap : 0f, side ? 0f : h + 10f, cw, h), c, FamilyRole.Chaplain, speaker, ideo != null ? ideo.name : "", new Color(.55f, .65f, .9f));
            float top = (side || speaker == 0 ? h : h * 2f + 10f) + 14f;
            float w = CommunityPageLayout.ColumnWidth(r);
            float left = CommunityPageLayout.Section(r, 0f, top, w, "WFL_Hub_Residents".Translate(), a =>
            {
                float y = Line(a, 0f, "WFL_Hub_ResidentsLine".Translate(reg.Persons.Count(p => p.InColony), reg.Lineages.Count(l => reg.IsListedFamily(l.Id))));
                y = CommunityPageLayout.Button(a, y, "WFL_Hub_Families".Translate(), () => MainTabWindow_Community.Open(ModuleCatalog.FrameworkId + ".families"));
                if (reg.NextAssemblyTick > 0) y = Line(a, y, "WFL_Assembly_Next".Translate(FamilyUi.Date(reg.NextAssemblyTick)), true);
                return CommunityPageLayout.Button(a, y, "WFL_Hub_Assembly".Translate(), () => MainTabWindow_Community.Open(ModuleCatalog.FrameworkId + ".assembly"));
            });
            bool columns = CommunityPageLayout.TwoColumns(r);
            float right = CommunityPageLayout.Section(r, columns ? w + CommunityPageLayout.Gap : 0f, columns ? top : left, w, "WFL_Hub_Modules".Translate(), a => Modules(a, c));
            return Mathf.Max(left, right);
        }

        /// <summary>A large card for one of the colony's two public figures: portrait, title, name and what they lead or speak for.</summary>
        private static void LeaderCard(Rect card, FamilyComponent c, FamilyRole role, int person, string subtitle, Color accent)
        {
            Widgets.DrawBoxSolid(card, new Color(.14f, .14f, .16f)); Widgets.DrawBoxSolid(new Rect(card.x, card.y, card.width, 4f), accent);
            GUI.color = new Color(1f, 1f, 1f, .07f); RoleIcons.Draw(new Rect(card.xMax - 118f, card.y + 10f, 110f, 110f), role, true); GUI.color = Color.white;
            var portrait = new Rect(card.x + 14f, card.y + 16f, 84f, card.height - 32f);
            Widgets.DrawBoxSolid(portrait, new Color(.08f, .08f, .09f));
            var pawn = person != 0 ? c.TryGetPawn(person) : null;
            if (pawn != null) GUI.DrawTexture(portrait, PortraitsCache.Get(pawn, portrait.size, Rot4.South, new Vector3(0f, 0f, 0.1f), 1.15f));
            else RoleIcons.Draw(portrait.ContractedBy(14f), role, true);
            Widgets.DrawBox(portrait);
            float x = portrait.xMax + 14f, tw = card.xMax - x - 12f;
            Text.Font = GameFont.Tiny; GUI.color = accent; Widgets.Label(new Rect(x, card.y + 16f, tw, 18f), RoleIcons.Label(role).ToUpperInvariant()); GUI.color = Color.white;
            Text.Font = GameFont.Medium; if (person == 0) GUI.color = Color.gray;
            Widgets.Label(new Rect(x, card.y + 36f, tw, 34f), person != 0 ? NameOf(c, person) : "WFL_Hub_Vacant".Translate().CapitalizeFirst().ToString());
            Text.Font = GameFont.Small; GUI.color = Color.gray; Widgets.Label(new Rect(x, card.y + 74f, tw, 44f), subtitle); GUI.color = Color.white;
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
