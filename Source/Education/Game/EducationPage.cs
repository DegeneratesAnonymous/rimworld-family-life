using System;
using System.Linq;
using FamilyLife.Domain;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>Education's view of existing staff, sessions and assessments.</summary>
    internal static class EducationPage
    {
        public const string PageId = "wils.education.education";
        public static void Register(string moduleId) =>
            FamilyFramework.Pages.Register(new Framework.CommunityPage<Func<Rect, FamilyComponent, float>> { Id = PageId, ModuleId = moduleId, LabelKey = "WFL_Edu_Page", Order = 110, Render = Draw, Status = Attention, Icon = "WilsFamilyLife/Roles/teacher" });
        private static int Attention() { var c = FamilyComponent.Current; return c == null ? 0 : EducationEvents.EligibleNow(c, FrameworkServices.Now).Count; }

        public static float Draw(Rect r, FamilyComponent c)
        {
            var reg = c.Registry; float w = CommunityPageLayout.ColumnWidth(r);
            float left = CommunityPageLayout.Section(r, 0f, 0f, w, "WFL_Edu_Instructors".Translate(), a =>
            {
                float y = 0f; var instructors = EducationCareers.Instructors(reg);
                if (instructors.Count == 0) y = CommunityPageLayout.Line(a, y, "WFL_Edu_None".Translate(), true);
                foreach (int id in instructors) y = CommunityPageLayout.Line(a, y, (c.TryGetPawn(id)?.LabelShort ?? "#" + id) + " - " + string.Join(", ", reg.Assignments().RolesOf(id).Select(x => { var role = CareerService.Professions.All.FirstOrDefault(p => p.Id == x); return role != null ? CareerService.TitleOf(role, reg) : x; })));
                return y;
            });
            left = CommunityPageLayout.Section(r, 0f, left, w, "WFL_Edu_Classes".Translate(), a =>
            {
                float y = 0f; var running = ClassSessions.RunningSessions.Where(s => !s.Finished).ToList();
                if (running.Count == 0) y = CommunityPageLayout.Line(a, y, "WFL_Edu_NoClasses".Translate(), true);
                foreach (var s in running) y = CommunityPageLayout.Line(a, y, "WFL_Edu_ClassLine".Translate(c.TryGetPawn(s.TeacherId)?.LabelShort ?? "#" + s.TeacherId, s.Subject, s.Roster.Count));
                return y;
            });
            bool columns = CommunityPageLayout.TwoColumns(r);
            float right = CommunityPageLayout.Section(r, columns ? w + CommunityPageLayout.Gap : 0f, columns ? 0f : left, w, "WFL_Edu_Assessments".Translate(), a =>
            {
                float y = 0f; int now = FrameworkServices.Now; var due = EducationEvents.EligibleNow(c, now);
                if (due.Count == 0) return CommunityPageLayout.Line(a, y, "WFL_Edu_NoAssessments".Translate(), true);
                y = CommunityPageLayout.Line(a, y, "WFL_Edu_AssessmentsDue".Translate(string.Join(", ", due.Select(p => p.LabelShort))));
                y = CommunityPageLayout.Button(a, y, "WFL_Event_Begin".Translate(), () => EducationEvents.BeginAll(c, now));
                y = CommunityPageLayout.Button(a, y, "WFL_Edu_Postpone".Translate(), () => EducationEvents.PostponeAll(c, now));
                return CommunityPageLayout.Button(a, y, "WFL_Edu_Skip".Translate(), () => EducationEvents.SkipAll(c, now));
            });
            return Mathf.Max(left, right);
        }
    }
}
