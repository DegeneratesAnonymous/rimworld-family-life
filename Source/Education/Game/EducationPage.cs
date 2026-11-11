using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Education's page in the Community window. It shows what the module already knows (who instructs, classes under way, the assessments due) and calls the same actions the lectern's buttons and the
    /// assessment letter call. The page is registered by the module, so when Education is off or absent the framework simply lists no such page.
    /// </summary>
    internal static class EducationPage
    {
        public const string PageId = "wils.education.education";

        public static void Register(string moduleId) =>
            FamilyFramework.Pages.Register(new FamilyLife.Framework.CommunityPage<Func<Rect, FamilyComponent, float>> { Id = PageId, ModuleId = moduleId, LabelKey = "WFL_Edu_Page", Order = 110, Render = Draw, Status = Attention, Icon = "WilsFamilyLife/Roles/teacher" });

        private static int Attention() { var c = FamilyComponent.Current; return c == null ? 0 : EducationEvents.EligibleNow(c, FrameworkServices.Now).Count; }

        private static float Line(Rect r, float y, string text, bool dim = false)
        {
            float h = Text.CalcHeight(text, r.width - 8f); if (dim) GUI.color = Color.gray;
            Widgets.Label(new Rect(4f, y, r.width - 8f, h), text); GUI.color = Color.white; return y + h + 2f;
        }

        public static float Draw(Rect r, FamilyComponent c)
        {
            var reg = c.Registry; float y = 0f; int now = FrameworkServices.Now;
            GUI.color = new Color(0.90f, 0.74f, 0.28f); Widgets.Label(new Rect(0f, y, r.width, 24f), "WFL_Edu_Instructors".Translate()); GUI.color = Color.white; y += 26f;
            var instructors = EducationCareers.Instructors(reg);
            if (instructors.Count == 0) y = Line(r, y, "WFL_Edu_None".Translate(), true);
            foreach (int id in instructors) y = Line(r, y, (c.TryGetPawn(id)?.LabelShort ?? "#" + id) + " - " + string.Join(", ", reg.Assignments().RolesOf(id).Select(x => { var role = CareerService.Professions.All.FirstOrDefault(p => p.Id == x); return role != null ? CareerService.TitleOf(role, reg) : x; })));
            y += 8f;
            var running = ClassSessions.RunningSessions.Where(s => !s.Finished).ToList();
            GUI.color = new Color(0.90f, 0.74f, 0.28f); Widgets.Label(new Rect(0f, y, r.width, 24f), "WFL_Edu_Classes".Translate()); GUI.color = Color.white; y += 26f;
            if (running.Count == 0) y = Line(r, y, "WFL_Edu_NoClasses".Translate(), true);
            foreach (var s in running) y = Line(r, y, "WFL_Edu_ClassLine".Translate(c.TryGetPawn(s.TeacherId)?.LabelShort ?? "#" + s.TeacherId, s.Subject, s.Roster.Count));
            y += 8f;
            var due = EducationEvents.EligibleNow(c, now);
            GUI.color = new Color(0.90f, 0.74f, 0.28f); Widgets.Label(new Rect(0f, y, r.width, 24f), "WFL_Edu_Assessments".Translate()); GUI.color = Color.white; y += 26f;
            if (due.Count == 0) { y = Line(r, y, "WFL_Edu_NoAssessments".Translate(), true); return y; }
            y = Line(r, y, "WFL_Edu_AssessmentsDue".Translate(string.Join(", ", due.Select(p => p.LabelShort))));
            if (Widgets.ButtonText(new Rect(4f, y, 150f, 26f), "WFL_Event_Begin".Translate())) EducationEvents.BeginAll(c, now);
            if (Widgets.ButtonText(new Rect(160f, y, 170f, 26f), "WFL_Edu_Postpone".Translate())) EducationEvents.PostponeAll(c, now);
            if (Widgets.ButtonText(new Rect(336f, y, 150f, 26f), "WFL_Edu_Skip".Translate())) EducationEvents.SkipAll(c, now);
            return y + 34f;
        }
    }
}
