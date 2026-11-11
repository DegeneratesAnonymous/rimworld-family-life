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
        private static FamilyRegistry alertRegistry;
        private static int alertRevision, alertViewer, alertTick;
        private static readonly Dictionary<int, List<FamilyAlert>> familyConcerns = new Dictionary<int, List<FamilyAlert>>();

        private static List<FamilyAlert> Concerns(FamilyComponent c, Lineage family)
        {
            int now = Find.TickManager.TicksGame;
            if (alertRegistry != c.Registry || alertRevision != c.Revision || alertViewer != c.PerspectivePawnId || now < alertTick || now - alertTick >= 300)
            {
                familyConcerns.Clear(); alertRegistry = c.Registry; alertRevision = c.Revision; alertViewer = c.PerspectivePawnId; alertTick = now;
            }
            if (!familyConcerns.TryGetValue(family.Id, out var list)) familyConcerns[family.Id] = list = FamilyAlerts.Build(c.Registry, family, c.PerspectivePawnId, now);
            return list;
        }

        private static FamilyRegistry readyRegistry;
        private static int readyRevision, readyTick, readyAssignments;
        private static string readyRoles;
        private static readonly List<KeyValuePair<int, List<ProfessionRole>>> readyPeople = new List<KeyValuePair<int, List<ProfessionRole>>>();

        private static List<KeyValuePair<int, List<ProfessionRole>>> Ready(FamilyComponent c)
        {
            var reg = c.Registry; int now = Find.TickManager.TicksGame;
            var roles = CareerService.Professions.All.Where(role => role.Level > 1 && FamilyFramework.Catalog.IsEnabled(role.ModuleId)).ToList();
            string signature = string.Join("|", roles.Select(role => role.Id));
            int assignments = reg.Assignments().Count;
            if (readyRegistry == reg && readyRevision == c.Revision && readyAssignments == assignments && readyRoles == signature && now >= readyTick && now - readyTick < 300) return readyPeople;
            readyPeople.Clear(); readyRegistry = reg; readyRevision = c.Revision; readyTick = now; readyAssignments = assignments; readyRoles = signature;
            foreach (var p in reg.Persons.Where(p => p.InColony && !p.Juvenile).OrderBy(p => p.Name))
            {
                var choices = roles.Where(role => !reg.Assignments().IsAssigned(p.Id, role.Id) && AssignmentBook.Check(role, reg.Careers(), p.Id) == AssignResult.Assigned).ToList();
                if (choices.Count > 0) readyPeople.Add(new KeyValuePair<int, List<ProfessionRole>>(p.Id, choices));
            }
            return readyPeople;
        }

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
                if (!FamilyFramework.Offices.Available(d.Kind) || c.Registry.IsProfessional(d.Kind)) continue;
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
                return CommunityPageLayout.Action(a, y, "WFL_Hub_Families".Translate(), () => MainTabWindow_Community.Open(FamilyLifeModule.Id + ".families"));
            });
            left = CommunityPageLayout.Section(r, 0f, left, w, "WFL_UX_ReadyTitle".Translate(), a =>
            {
                float y = 0f;
                var candidates = Ready(c);
                foreach (var candidate in candidates)
                {
                    y = Line(a, y, NameOf(c, candidate.Key) + ": " + string.Join(", ", candidate.Value.Select(role => CareerService.TitleOf(role, c.Registry))));
                    int id = candidate.Key; string roleId = candidate.Value[0].Id;
                    y = CommunityPageLayout.Action(a, y, "WFL_UX_ReviewCareer".Translate(), () => CommunityCareerPage.OpenRole(roleId, id));
                }
                return candidates.Count == 0 ? Line(a, y, "WFL_UX_NoReady".Translate(), true) : y;
            });
            bool columns = CommunityPageLayout.TwoColumns(r);
            float right = CommunityPageLayout.Section(r, columns ? w + CommunityPageLayout.Gap : 0f, columns ? 0f : left, w, "WFL_Hub_Posts".Translate(), a =>
            {
                float y = Posts(a, c);
                if (c.Registry.NextAssemblyTick > 0) y = Line(a, y, "WFL_Assembly_Next".Translate(FamilyUi.Date(c.Registry.NextAssemblyTick)), true);
                return CommunityPageLayout.Action(a, y, "WFL_Hub_Assembly".Translate(), () => MainTabWindow_Community.Open(FamilyLifeModule.Id + ".assembly"));
            });
            return Mathf.Max(left, right);
        }

        private static float Families(Rect r, FamilyComponent c)
        {
            float y = Line(r, 0f, "WFL_Hub_FamiliesHelp".Translate(), true);
            PerspectiveShiftBridge.TrySync(c);
            var families = c.Registry.Lineages.Where(l => c.Registry.IsListedFamily(l.Id)).OrderBy(l => l.Name).ToList();
            if (families.Count == 0) y = Line(r, y, "WFL_NoFamilies".Translate(), true);
            float w = CommunityPageLayout.ColumnWidth(r), left = y, right = y;
            int index = 0;
            foreach (var family in families)
            {
                int id = family.Id;
                bool second = CommunityPageLayout.TwoColumns(r) && index++ % 2 == 1;
                float cardY = second ? right : left;
                float end = CommunityPageLayout.Section(r, second ? w + CommunityPageLayout.Gap : 0f, second ? right : left, w, family.Name, a =>
                {
                    FamilyCrest.Draw(new Rect(0f, 0f, 36f, 42f), family);
                    var names = c.Registry.MembersOf(id).Where(person => person.InColony).Select(person => NameOf(c, person.Id)).ToList();
                    string residents = "WFL_Hub_FamilyResidents".Translate(c.Registry.LivingCount(id, Presence.Colony));
                    float summaryHeight = Mathf.Max(44f, Text.CalcHeight(residents, a.width - 82f));
                    Widgets.Label(new Rect(46f, 0f, a.width - 82f, summaryHeight), residents);
                    float h = Line(a, summaryHeight + 6f, string.Join(", ", names));
                    foreach (var alert in Concerns(c, family).Take(2))
                        h = Line(a, h, FamilyText.AlertText(c.Registry, alert));
                    var popout = new Rect(a.width - 28f, 0f, 28f, 28f);
                    TooltipHandler.TipRegion(popout, "WFL_UX_ViewFamily".Translate(family.Name));
                    if (Widgets.ButtonText(popout, "↗"))
                    {
                        Window_Families.PendingLineageId = id;
                        if (Find.WindowStack.WindowOfType<Window_Families>() == null) Find.WindowStack.Add(new Window_Families());
                    }
                    return h;
                });
                if (Widgets.ButtonInvisible(new Rect(second ? w + CommunityPageLayout.Gap : 0f, cardY, w, Mathf.Max(28f, Text.CalcHeight(family.Name, w - 16f) + 10f))))
                {
                    Window_Families.PendingLineageId = id;
                    if (Find.WindowStack.WindowOfType<Window_Families>() == null) Find.WindowStack.Add(new Window_Families());
                }
                if (second) right = end; else left = end;
            }
            y = Mathf.Max(left, right);
            return y;
        }

        private static float Professions(Rect r, FamilyComponent c) => CommunityCareerPage.Draw(r, c);

        private static float Assembly(Rect r, FamilyComponent c)
        {
            var reg = c.Registry; var assembly = reg.OpenAssembly;
            float y = Line(r, 0f, "WFL_Next_AssemblyHelp".Translate());
            if (assembly == null)
            {
                y = Heading(r, y, "WFL_Next_AssemblyUpcoming".Translate());
                if (reg.NextAssemblyTick > 0) y = Line(r, y, "WFL_Assembly_Next".Translate(FamilyUi.Date(reg.NextAssemblyTick)));
                y = Line(r, y, "WFL_Next_AssemblyIdle".Translate());
                if (reg.NextAssemblyTick > 0) y = CommunityPageLayout.Action(r, y, "WFL_Assembly_Postpone".Translate(), () => reg.PostponeAssembly(Find.TickManager.TicksGame, 30));
            }
            else
            {
                y = Heading(r, y, ("WFL_AssemblyState_" + assembly.State).Translate());
                y = Line(r, y, "WFL_Next_Turnout".Translate(assembly.Voted.Count));
                foreach (var race in assembly.Races)
                {
                    y = Heading(r, y, ("WFL_OfficeKind_" + race.Kind).Translate());
                    y = Line(r, y, "WFL_Next_Candidates".Translate(FamilyUi.NameList(reg, race.Candidates)));
                    y = Line(r, y, "WFL_Next_Ballots".Translate(race.Votes.Count));
                    if (race.WinnerId != 0) y = Line(r, y, "WFL_Next_Winner".Translate(NameOf(c, race.WinnerId)));
                }
            }
            return y;
        }

        private static float Settings(Rect r, FamilyComponent c)
        {
            float y = CommunityPageLayout.Action(r, 0f, "WFL_Hub_OpenSettings".Translate(), () =>
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
