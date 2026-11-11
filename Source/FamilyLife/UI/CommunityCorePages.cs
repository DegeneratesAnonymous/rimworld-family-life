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
    /// <summary>
    /// The framework's own pages of the Community window: Overview, Families &amp; Households, Professions, Community Assembly and Settings. The Families page hosts the existing Families
    /// window's content unchanged (the window itself still opens from the colonist's Family button), and the Professions and Assembly pages show what the career, assignment and office
    /// records already hold, so nothing is duplicated or removed.
    /// </summary>
    internal static class CommunityCorePages
    {
        private static Window_Families hosted;

        public static void Register(string moduleId)
        {
            Add(moduleId, "overview", "WFL_Hub_Overview", 0, Overview, "UI/Commands/Draft");
            Add(moduleId, "families", "WFL_Hub_Families", 10, Families, "UI/Commands/AssignOwner", ownsScrolling: true);
            Add(moduleId, "professions", "WFL_Hub_Professions", 20, Professions, null);
            Add(moduleId, "assembly", "WFL_Hub_Assembly", 30, Assembly, null);
            Add(moduleId, "settings", "WFL_Hub_Settings", 90, Settings, null);
        }

        private static void Add(string moduleId, string name, string label, int order, Func<Rect, FamilyComponent, float> render, string icon, bool ownsScrolling = false) =>
            FamilyFramework.Pages.Register(new CommunityPage<Func<Rect, FamilyComponent, float>> { Id = moduleId + "." + name, ModuleId = moduleId, LabelKey = label, Order = order, Icon = icon, Render = render, OwnsScrolling = ownsScrolling });

        // ------------------------------------------------------------------ shared drawing helpers (modules may use them too)

        internal static float Heading(Rect r, float y, string text) { Text.Font = GameFont.Small; GUI.color = FamilyKit.Gold; Widgets.Label(new Rect(0f, y, r.width, 24f), text); GUI.color = Color.white; return y + 26f; }
        internal static float Line(Rect r, float y, string text, bool dim = false)
        {
            float h = Text.CalcHeight(text, r.width - 8f);
            if (dim) GUI.color = Color.gray;
            Widgets.Label(new Rect(4f, y, r.width - 8f, h), text); GUI.color = Color.white;
            return y + h + 2f;
        }
        internal static string NameOf(FamilyComponent c, int id) => FamilyUi.NameOf(c.Registry, id);

        // ------------------------------------------------------------------ Overview

        private static float Overview(Rect r, FamilyComponent c)
        {
            var reg = c.Registry; float y = 0f;
            y = Heading(r, y, "WFL_Hub_Residents".Translate());
            int residents = reg.Persons.Count(p => p.InColony);
            y = Line(r, y, "WFL_Hub_ResidentsLine".Translate(residents, reg.Lineages.Count(l => reg.IsListedFamily(l.Id))));
            y += 6f;
            y = Heading(r, y, "WFL_Hub_Posts".Translate());
            foreach (var d in FamilyFramework.Offices.All.OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                if (!FamilyFramework.Offices.Available(d.Kind)) continue;
                var office = reg.OfficeFor(d.Kind);
                y = Line(r, y, d.LabelKey.Translate() + ": " + (office != null && office.Held ? NameOf(c, office.HolderId) : "WFL_Hub_Vacant".Translate().ToString()));
            }
            if (reg.NextAssemblyTick > 0) y = Line(r, y, "WFL_Assembly_Next".Translate(FamilyUi.Date(reg.NextAssemblyTick)), true);
            y += 6f;
            y = Heading(r, y, "WFL_Hub_Modules".Translate());
            foreach (var m in FamilyFramework.Catalog.Modules.Where(x => !x.IsFramework).OrderBy(x => x.Id, StringComparer.Ordinal))
                y = Line(r, y, m.Name + ": " + FamilyFramework.Describe(c, m.Id));
            return y;
        }

        // ------------------------------------------------------------------ Families & Households (hosts the existing window's content)

        private static float Families(Rect r, FamilyComponent c)
        {
            if (hosted == null) { hosted = new Window_Families(); hosted.PrepareHosted(); }
            var inner = new Rect(r.x, r.y, r.width, r.height);
            GUI.BeginGroup(inner);
            try { hosted.DoWindowContents(new Rect(0f, 0f, inner.width, inner.height)); }
            finally { GUI.EndGroup(); }
            if (Widgets.ButtonText(new Rect(r.xMax - 190f, r.y - 34f, 186f, 26f), "WFL_Hub_OpenFamilies".Translate())) Find.WindowStack.Add(new Window_Families());
            return 0f;
        }

        // ------------------------------------------------------------------ Professions

        private static float Professions(Rect r, FamilyComponent c)
        {
            var reg = c.Registry; var book = reg.Assignments(); var ledger = reg.Careers(); float y = 0f; int now = Find.TickManager.TicksGame;
            var roles = CareerService.Professions.All.Where(x => FamilyFramework.Catalog.IsEnabled(x.ModuleId)).OrderBy(x => x.Career, StringComparer.Ordinal).ThenBy(x => x.Level).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            if (roles.Count == 0) return Line(r, y, "WFL_Hub_NoProfessions".Translate(), true);
            y = Line(r, y, "WFL_Hub_ProfessionsHelp".Translate(), true) + 4f;
            var residents = reg.Persons.Where(p => p.InColony && !p.Juvenile).OrderBy(p => p.Id).ToList();
            string career = null;
            foreach (var role in roles)
            {
                if (role.Career != career) { career = role.Career; y += 4f; y = Heading(r, y, CareerService.Career(career) != null ? CareerService.CareerName(CareerService.Career(career), reg) : career); }
                var holders = book.Holders(role.Id).Where(id => reg.GetPerson(id)?.InColony == true).ToList();
                var row = new Rect(8f, y, r.width - 16f, 24f);
                Widgets.Label(new Rect(row.x, row.y, row.width - 130f, 24f), CareerService.TitleOf(role, reg) + ": " + (holders.Count == 0 ? "WFL_Hub_Vacant".Translate().ToString() : string.Join(", ", holders.Select(id => NameOf(c, id)))));
                if (Widgets.ButtonText(new Rect(row.xMax - 124f, row.y, 124f, 22f), "WFL_Career_Assign".Translate()))
                {
                    var options = new List<FloatMenuOption>();
                    foreach (var p in residents)
                    {
                        int id = p.Id; var rr = role;
                        bool already = book.IsAssigned(id, rr.Id); var check = AssignmentBook.Check(rr, ledger, id);
                        string label = NameOf(c, id) + (already ? " (" + "WFL_Career_Assigned".Translate() + ")" : check != AssignResult.Assigned ? " (" + "WFL_Career_Needs_Short".Translate() + ")" : "");
                        options.Add(new FloatMenuOption(label, already ? (Action)(() => { book.Unassign(id, rr.Id); }) : check == AssignResult.Assigned ? () => book.Assign(id, rr, ledger, now) : (Action)null));
                    }
                    if (options.Count == 0) options.Add(new FloatMenuOption("WFL_ZoneNoFamilies".Translate(), null));
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                y += 26f;
            }
            return y;
        }

        // ------------------------------------------------------------------ Community Assembly

        private static float Assembly(Rect r, FamilyComponent c)
        {
            var reg = c.Registry; float y = 0f;
            y = Line(r, y, "WFL_Hub_AssemblyHelp".Translate(), true) + 4f;
            y = Heading(r, y, "WFL_Hub_Posts".Translate());
            foreach (var d in FamilyFramework.Offices.All.OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                if (!FamilyFramework.Offices.Available(d.Kind)) continue;
                var office = reg.OfficeFor(d.Kind);
                y = Line(r, y, d.LabelKey.Translate() + ": " + (office != null && office.Held ? NameOf(c, office.HolderId) : "WFL_Hub_Vacant".Translate().ToString()));
            }
            y += 6f;
            if (reg.NextAssemblyTick > 0) y = Line(r, y, "WFL_Assembly_Next".Translate(FamilyUi.Date(reg.NextAssemblyTick)));
            if (reg.NextAssemblyTick > 0 && reg.OpenAssembly == null)
            {
                if (Widgets.ButtonText(new Rect(4f, y, 200f, 26f), "WFL_Assembly_Postpone".Translate())) reg.PostponeAssembly(Find.TickManager.TicksGame, 30);
                y += 32f;
            }
            var open = reg.OpenAssembly;
            if (open != null)
                foreach (var race in open.Races)
                    y = Line(r, y, "WFL_Assembly_RaceLine".Translate(("WFL_OfficeKind_" + race.Kind).Translate(), ("WFL_AssemblyState_" + open.State).Translate(), FamilyUi.NameList(reg, race.Candidates)));
            return y;
        }

        // ------------------------------------------------------------------ Settings

        private static float Settings(Rect r, FamilyComponent c)
        {
            float y = 0f;
            if (Widgets.ButtonText(new Rect(4f, y, 240f, 28f), "WFL_Hub_OpenSettings".Translate()))
            {
                var mod = LoadedModManager.GetMod<FamilyLifeMod>();
                if (mod != null) Find.WindowStack.Add(new Dialog_ModSettings(mod));
            }
            y += 38f;
            y = Heading(r, y, "WFL_Hub_Modules".Translate());
            foreach (var m in FamilyFramework.Catalog.Modules.Where(x => !x.IsFramework).OrderBy(x => x.Id, StringComparer.Ordinal))
                y = Line(r, y, m.Name + ": " + FamilyFramework.Describe(c, m.Id));
            foreach (var f in FamilyFramework.Failures) y = Line(r, y, f.Key + ": " + f.Value, true);
            return y;
        }
    }
}
