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
                y = Line(r, y, d.LabelKey.Translate() + ": " + (office != null && office.Held ? NameOf(c, office.HolderId) : "WFL_Hub_Vacant".Translate().ToString()));
            }
            return y;
        }

        private static float Modules(Rect r, FamilyComponent c)
        {
            float y = 0f;
            foreach (var m in FamilyFramework.Catalog.Modules.Where(x => !x.IsFramework).OrderBy(x => x.Id, StringComparer.Ordinal))
                y = Line(r, y, m.Name + ": " + FamilyFramework.Describe(c, m.Id));
            return y;
        }

        private static float Overview(Rect r, FamilyComponent c)
        {
            float w = CommunityPageLayout.ColumnWidth(r);
            float left = CommunityPageLayout.Section(r, 0f, 0f, w, "WFL_Hub_Residents".Translate(), a =>
            {
                float y = Line(a, 0f, "WFL_Hub_ResidentsLine".Translate(c.Registry.Persons.Count(p => p.InColony), c.Registry.Lineages.Count(l => c.Registry.IsListedFamily(l.Id))));
                return CommunityPageLayout.Button(a, y, "WFL_Hub_Families".Translate(), () => MainTabWindow_Community.Open(ModuleCatalog.FrameworkId + ".families"));
            });
            left = CommunityPageLayout.Section(r, 0f, left, w, "WFL_Hub_Modules".Translate(), a => Modules(a, c));
            bool columns = CommunityPageLayout.TwoColumns(r);
            float right = CommunityPageLayout.Section(r, columns ? w + CommunityPageLayout.Gap : 0f, columns ? 0f : left, w, "WFL_Hub_Posts".Translate(), a =>
            {
                float y = Posts(a, c);
                if (c.Registry.NextAssemblyTick > 0) y = Line(a, y, "WFL_Assembly_Next".Translate(FamilyUi.Date(c.Registry.NextAssemblyTick)), true);
                return CommunityPageLayout.Button(a, y, "WFL_Hub_Assembly".Translate(), () => MainTabWindow_Community.Open(ModuleCatalog.FrameworkId + ".assembly"));
            });
            return Mathf.Max(left, right);
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
            var residents = reg.Persons.Where(p => p.InColony && !p.Juvenile).OrderBy(p => NameOf(c, p.Id)).ToList();
            var shown = roles.Where(role => role.Career == selectedCareer && (professionSearch.Length == 0 || CareerService.TitleOf(role, reg).IndexOf(professionSearch, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
            if (shown.Count == 0) return Line(r, y, "WFL_Hub_NoMatches".Translate(), true);
            foreach (var role in shown)
            {
                var holders = book.Holders(role.Id).Where(id => reg.GetPerson(id)?.InColony == true).ToList();
                int qualified = residents.Count(p => !book.IsAssigned(p.Id, role.Id) && AssignmentBook.Check(role, ledger, p.Id) == AssignResult.Assigned);
                float buttonW = Mathf.Min(150f, r.width * .3f), textW = r.width - buttonW - 24f;
                string title = CareerService.TitleOf(role, reg);
                string details = (holders.Count == 0 ? "WFL_Hub_Vacant".Translate().ToString() : string.Join(", ", holders.Select(id => NameOf(c, id)))) + "\n" + "WFL_Hub_Qualified".Translate(role.Level, qualified);
                float titleH = Text.CalcHeight(title, textW), detailsH = Text.CalcHeight(details, textW);
                float rowH = titleH + detailsH + 20f;
                Widgets.DrawBoxSolid(new Rect(0f, y, r.width, rowH), new Color(.16f, .16f, .17f));
                Widgets.Label(new Rect(8f, y + 6f, textW, titleH), title);
                GUI.color = Color.gray; Widgets.Label(new Rect(8f, y + 8f + titleH, textW, detailsH), details); GUI.color = Color.white;
                if (Widgets.ButtonText(new Rect(r.width - buttonW - 8f, y + 8f, buttonW, Mathf.Max(30f, Text.CalcHeight("WFL_Career_Assign".Translate(), buttonW - 16f) + 8f)), "WFL_Career_Assign".Translate()))
                {
                    var options = new List<FloatMenuOption>();
                    foreach (var p in residents)
                    {
                        int id = p.Id; var rr = role;
                        bool already = book.IsAssigned(id, rr.Id); var check = AssignmentBook.Check(rr, ledger, id);
                        string label = NameOf(c, id) + (already ? " (" + "WFL_Career_Assigned".Translate() + ")" : check != AssignResult.Assigned ? " (" + "WFL_Career_Needs_Short".Translate() + ")" : "");
                        options.Add(new FloatMenuOption(label, already ? (Action)(() => book.Unassign(id, rr.Id)) : check == AssignResult.Assigned ? () => book.Assign(id, rr, ledger, Find.TickManager.TicksGame) : (Action)null));
                    }
                    if (options.Count == 0) options.Add(new FloatMenuOption("WFL_ZoneNoFamilies".Translate(), null));
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                y += rowH + 8f;
            }
            return y;
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
