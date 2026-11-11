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
    /// <summary>Person-first, data-driven career map. Inspecting a role never awards qualifications or changes assignments.</summary>
    public static class CommunityCareerPage
    {
        private static int person;
        private static string career, selectedRole;

        public static void OpenPerson(int id) { person = id; MainTabWindow_Community.Open(FamilyLifeModule.Id + ".professions"); }
        public static void OpenRole(string roleId, int id = 0)
        {
            var role = CareerService.Professions.ById(roleId);
            if (role != null) { career = role.Career; selectedRole = role.Id; }
            if (id != 0) person = id;
            MainTabWindow_Community.Open(FamilyLifeModule.Id + ".professions");
        }

        public static float Draw(Rect r, FamilyComponent c)
        {
            var reg = c.Registry;
            var roles = CareerService.Professions.All.Where(role => FamilyFramework.Catalog.IsEnabled(role.ModuleId)).OrderBy(role => role.Level).ThenBy(role => role.Id, StringComparer.Ordinal).ToList();
            if (roles.Count == 0) return CommunityPageLayout.Line(r, 0f, "WFL_Hub_NoProfessions".Translate());
            var residents = reg.Persons.Where(p => p.InColony && !p.Juvenile).OrderBy(p => FamilyUi.NameOf(reg, p.Id)).ToList();
            if (residents.Count == 0) return CommunityPageLayout.Line(r, 0f, "WFL_UX_NoColonists".Translate());
            if (!residents.Any(p => p.Id == person)) person = residents.Any(p => p.Id == (Find.Selector.SingleSelectedThing as Pawn)?.thingIDNumber) ? ((Pawn)Find.Selector.SingleSelectedThing).thingIDNumber : residents[0].Id;
            if (!roles.Any(role => role.Career == career)) career = roles[0].Career;
            float y = CommunityPageLayout.Action(r, 0f, "WFL_UX_Colonist".Translate(FamilyUi.NameOf(reg, person)), () =>
                Find.WindowStack.Add(new FloatMenu(residents.Select(p =>
                {
                    var target = roles.FirstOrDefault(role => role.Id == selectedRole);
                    string status = target == null ? "" : " — " + (reg.Assignments().IsAssigned(p.Id, target.Id) ? "WFL_UX_Assigned" : AssignmentBook.Check(target, reg.Careers(), p.Id) == AssignResult.Assigned ? "WFL_UX_CanAssign" : "WFL_UX_Locked").Translate();
                    return new FloatMenuOption(FamilyUi.NameOf(reg, p.Id) + status, () => person = p.Id);
                }).ToList())));
            float x = 0f, rowHeight = 0f;
            foreach (var id in roles.Select(role => role.Career).Distinct())
            {
                var def = CareerService.Career(id);
                string label = (id + "_career_label").CanTranslate() ? (id + "_career_label").Translate().ToString() : def == null ? id : CareerService.CareerName(def, reg);
                float width = Mathf.Min(r.width, Text.CalcSize(label).x + 28f), height = Mathf.Max(30f, Text.CalcHeight(label, width - 20f) + 10f);
                if (x > 0f && x + width > r.width) { x = 0f; y += rowHeight + 8f; rowHeight = 0f; }
                var tab = new Rect(x, y, width, height);
                if (Widgets.ButtonText(tab, label)) { career = id; selectedRole = null; }
                if (id == career) Widgets.DrawBoxSolid(new Rect(tab.x, tab.yMax - 3f, tab.width, 3f), FamilyKit.Gold);
                rowHeight = Mathf.Max(rowHeight, height); x += width + 6f;
            }
            y += rowHeight + 14f;
            var assigned = roles.Where(role => reg.Assignments().IsAssigned(person, role.Id)).Select(role => CareerService.TitleOf(role, reg)).ToList();
            y = CommunityPageLayout.Line(r, y, "WFL_UX_CurrentRoles".Translate(assigned.Count == 0 ? "WFL_UX_None".Translate().ToString() : string.Join(", ", assigned)));
            y = CommunityPageLayout.Line(r, y, "WFL_UX_MapHelp".Translate(), true);
            var current = roles.Where(role => role.Career == career).ToList();
            var chosen = current.FirstOrDefault(role => role.Id == selectedRole) ?? current.FirstOrDefault(role => role.Level == 2) ?? current[0];
            selectedRole = chosen.Id;
            bool split = r.width >= 760f;
            float mapWidth = split ? r.width * .57f : r.width;
            float mapEnd = Map(new Rect(0f, y, mapWidth, r.height), c, current);
            float detailY = split ? y : mapEnd + 16f;
            float detailX = split ? mapWidth + 16f : 0f;
            // Re-resolve after a node click in this frame.
            chosen = current.First(role => role.Id == selectedRole);
            float detailEnd = CommunityPageLayout.Section(r, detailX, detailY, r.width - detailX, CareerService.TitleOf(chosen, reg), a => Details(a, c, chosen));
            return Mathf.Max(mapEnd, detailEnd);
        }

        private static float Map(Rect r, FamilyComponent c, List<ProfessionRole> roles)
        {
            float y = r.y;
            foreach (var level in new[] { 1, 2, 3 })
            {
                var nodes = roles.Where(role => role.Level == level).ToList();
                if (nodes.Count == 0) continue;
                if (y > r.y) { Widgets.DrawBoxSolid(new Rect(r.width / 2f, y, 1f, 22f), Color.gray); y += 24f; }
                if (level == 3)
                {
                    y = LabelAt(r, y, "WFL_UX_Specialties".Translate());
                    Widgets.DrawBoxSolid(new Rect(12f, y, r.width - 24f, 1f), Color.gray); y += 10f;
                }
                int columns = level < 3 ? 1 : Math.Max(1, (int)(r.width / 140f));
                float width = (r.width - (columns - 1) * 10f) / columns;
                for (int start = 0; start < nodes.Count; start += columns)
                {
                    var row = nodes.Skip(start).Take(columns).ToList();
                    float height = row.Max(role => Mathf.Max(80f, Text.CalcHeight(CareerService.TitleOf(role, c.Registry), Mathf.Min(level < 3 ? 220f : width, width) - 20f) + new[] { "WFL_UX_Assigned", "WFL_UX_CanAssign", "WFL_UX_Locked" }.Max(key => Text.CalcHeight(key.Translate(), Mathf.Min(level < 3 ? 220f : width, width) - 20f)) + 24f));
                    for (int col = 0; col < row.Count; col++)
                    {
                        var role = row[col];
                        float nodeWidth = level < 3 ? Mathf.Min(220f, width) : width;
                        var box = new Rect(level < 3 ? (r.width - nodeWidth) / 2f : col * (width + 10f), y, nodeWidth, height);
                        bool assigned = c.Registry.Assignments().IsAssigned(person, role.Id);
                        bool qualified = AssignmentBook.Check(role, c.Registry.Careers(), person) == AssignResult.Assigned;
                        Widgets.DrawBoxSolid(box, assigned ? new Color(.21f, .28f, .23f) : qualified ? new Color(.27f, .25f, .17f) : new Color(.14f, .16f, .18f));
                        if (role.Id == selectedRole) { GUI.color = FamilyKit.Gold; Widgets.DrawBox(box, 2); GUI.color = Color.white; }
                        float titleH = Text.CalcHeight(CareerService.TitleOf(role, c.Registry), box.width - 16f);
                        Widgets.Label(new Rect(box.x + 8f, box.y + 7f, box.width - 16f, titleH), CareerService.TitleOf(role, c.Registry));
                        GUI.color = assigned || qualified ? Color.white : new Color(.7f, .73f, .75f);
                        Widgets.Label(new Rect(box.x + 8f, box.y + titleH + 12f, box.width - 16f, box.height - titleH - 16f), (assigned ? "WFL_UX_Assigned" : qualified ? "WFL_UX_CanAssign" : "WFL_UX_Locked").Translate()); GUI.color = Color.white;
                        if (Widgets.ButtonInvisible(box)) selectedRole = role.Id;
                    }
                    y += height + 10f;
                }
            }
            return y;
        }

        private static float LabelAt(Rect r, float y, string text)
        {
            float h = Text.CalcHeight(text, r.width); Widgets.Label(new Rect(0f, y, r.width, h), text); return y + h + 8f;
        }

        private static float Details(Rect r, FamilyComponent c, ProfessionRole role)
        {
            var reg = c.Registry; var ledger = reg.Careers(); var book = reg.Assignments();
            string help = role.Id + "_help";
            float y = 0f;
            if (help.CanTranslate()) y = CommunityPageLayout.Line(r, y, help.Translate());
            var def = CareerService.Career(role.Career);
            bool assigned = book.IsAssigned(person, role.Id), qualified = AssignmentBook.Check(role, ledger, person) == AssignResult.Assigned;
            y = CommunityPageLayout.Line(r, y, (assigned ? "WFL_UX_Assigned" : qualified ? "WFL_UX_CanAssign" : "WFL_UX_Locked").Translate());
            if (role.Level == 1) y = CommunityPageLayout.Line(r, y, "WFL_UX_EntryRole".Translate());
            else if (def != null)
            {
                var progress = ledger.Progress(person, def, CareerService.SkillsOf(c.TryGetPawn(person))).FirstOrDefault(row => role.Level == 2 ? row.Key == null : row.Key == role.Specialty);
                if (role.Level == 3) y = CommunityPageLayout.Line(r, y, "WFL_UX_Prerequisite".Translate(CareerService.Describe(role.Career), ledger.IsQualified(person, role.Career) ? "WFL_UX_Earned".Translate().ToString() : "WFL_UX_Locked".Translate().ToString()));
                if (progress != null)
                {
                    if (progress.Earned) y = CommunityPageLayout.Line(r, y, (ledger.IsLegacy(person, role.Career) && role.Level == 2 ? "WFL_UX_LegacyQualified" : "WFL_UX_EarnedQualification").Translate());
                    else
                    {
                        foreach (var skill in progress.Skills)
                        {
                            var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(skill.Key);
                            y = Requirement(r, y, skillDef?.LabelCap ?? skill.Key, skill.Value[0], skill.Value[1]);
                        }
                        y = Requirement(r, y, "WFL_UX_CareerExperience".Translate(), progress.GeneralXp, progress.GeneralNeeded);
                        if (role.Level == 3) y = Requirement(r, y, "WFL_UX_SpecialtyExperience".Translate(), progress.SpecialtyXp, progress.SpecialtyNeeded);
                        if (progress.SkillsMet && progress.GeneralShare >= 1f && (role.Level < 3 || progress.SpecialtyShare >= 1f))
                            y = CommunityPageLayout.Line(r, y, "WFL_UX_AwaitingEvaluation".Translate());
                        string earn = role.Career + "_earn_help";
                        if (earn.CanTranslate()) y = CommunityPageLayout.Line(r, y, earn.Translate());
                        var pawn = c.TryGetPawn(person);
                        if (pawn == null || pawn.skills == null) y = CommunityPageLayout.Line(r, y, "WFL_UX_SkillsUnavailable".Translate());
                    }
                }
            }
            var lower = CareerService.Professions.All.Where(other => other.Career == role.Career && other.Level < role.Level && book.IsAssigned(person, other.Id)).ToList();
            bool upgrade = !assigned && lower.Count > 0;
            if (upgrade) y = CommunityPageLayout.Line(r, y, "WFL_Next_UpgradeHelp".Translate());
            y = CommunityPageLayout.Action(r, y + 8f, upgrade ? "WFL_Next_Upgrade".Translate(CareerService.TitleOf(role, reg)) : assigned ? "WFL_UX_RemoveRole".Translate(CareerService.TitleOf(role, reg)) : "WFL_UX_AssignRole".Translate(FamilyUi.NameOf(reg, person), CareerService.TitleOf(role, reg)), () =>
            {
                // Revalidate the resident and module at the action boundary; qualification is never awarded by this page.
                if (FamilyComponent.Current != c || reg.GetPerson(person)?.InColony != true || reg.GetPerson(person).Juvenile || !FamilyFramework.Catalog.IsEnabled(role.ModuleId)) return;
                if (book.IsAssigned(person, role.Id)) book.Unassign(person, role.Id);
                else
                {
                    var result = book.Assign(person, role, ledger, Find.TickManager.TicksGame);
                    if (upgrade && result == AssignResult.Assigned)
                        foreach (var old in lower) book.Unassign(person, old.Id);
                }
            }, assigned || qualified);
            if (!assigned && !qualified) y = CommunityPageLayout.Line(r, y, "WFL_UX_NotQualified".Translate());
            y = CommunityPageLayout.Line(r, y, "WFL_UX_AssignmentHelp".Translate(), true);
            return y;
        }

        private static float Requirement(Rect r, float y, string label, int have, int need)
        {
            y = CommunityPageLayout.Line(r, y, "WFL_UX_Requirement".Translate(label, have, need));
            var bar = new Rect(0f, y, r.width, 6f);
            Widgets.DrawBoxSolid(bar, new Color(.2f, .22f, .23f));
            Widgets.DrawBoxSolid(new Rect(bar.x, bar.y, bar.width * (need <= 0 ? 1f : Mathf.Clamp01(have / (float)need)), bar.height), have >= need ? new Color(.45f, .7f, .5f) : FamilyKit.Gold);
            return y + 14f;
        }
    }
}
