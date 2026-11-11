using System;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>Care's view of existing staff, workload and check-up actions.</summary>
    internal static class CarePage
    {
        public const string PageId = "wils.care.care";
        public static void Register(string moduleId) =>
            FamilyFramework.Pages.Register(new Framework.CommunityPage<Func<Rect, FamilyComponent, float>> { Id = PageId, ModuleId = moduleId, LabelKey = "WFL_Care_Page", Order = 120, Render = Draw, Status = Attention, Icon = "WilsFamilyLife/Roles/healer" });
        private static int Attention() { var c = FamilyComponent.Current; return c == null ? 0 : CareEvents.IsSickCount(c); }

        public static float Draw(Rect r, FamilyComponent c)
        {
            var reg = c.Registry; float w = CommunityPageLayout.ColumnWidth(r);
            float left = CommunityPageLayout.Section(r, 0f, 0f, w, "WFL_Care_Practitioners".Translate(), a =>
            {
                float y = 0f; var staff = CareCareers.Practitioners(reg).Where(id => reg.GetPerson(id)?.InColony == true).ToList();
                if (staff.Count == 0) y = CommunityPageLayout.Line(a, y, "WFL_Care_None".Translate(), true);
                foreach (int id in staff) y = CommunityPageLayout.Line(a, y, (c.TryGetPawn(id)?.LabelShort ?? "#" + id) + " - " + string.Join(", ", reg.Assignments().RolesOf(id).Select(x => { var role = CareerService.Professions.All.FirstOrDefault(p => p.Id == x); return role != null ? CareerService.TitleOf(role, reg) : x; })));
                y = CommunityPageLayout.Line(a, y, "WFL_Care_Capacity".Translate(CheckupWorkload.CommunityLimit(false), CheckupWorkload.CommunityLimit(true)));
                return CommunityPageLayout.Action(a, y, "WFL_UX_ManageCare".Translate(), () => CommunityCareerPage.OpenRole(CareRoles.Healer));
            });
            bool columns = CommunityPageLayout.TwoColumns(r);
            float right = CommunityPageLayout.Section(r, columns ? w + CommunityPageLayout.Gap : 0f, columns ? 0f : left, w, "WFL_Care_Rounds".Translate(), a =>
            {
                var seat = reg.OfficeFor(OfficeKind.Healer);
                if (seat == null || !seat.Held) return CommunityPageLayout.Line(a, 0f, "WFL_Care_NoSeat".Translate());
                var care = seat.Care(); int now = FrameworkServices.Now;
                bool active = reg.CheckupOpen(now), enabled = FamilyLifeMod.Settings == null || FamilyLifeMod.Settings.healthChecks;
                float y = CommunityPageLayout.Line(a, 0f, (!enabled ? "WFL_UX_CareDisabled" : active ? "WFL_UX_RoundActive" : seat.PendingKind != 0 ? "WFL_UX_RoundRequested" : "WFL_UX_RoundIdle").Translate());
                if (active || care.RoundStartTick != 0)
                {
                    var patients = reg.Persons.Where(p => p.InColony && p.Id != seat.HolderId).Select(p => p.Id).ToList();
                    y = CommunityPageLayout.Line(a, y, "WFL_UX_RoundProgress".Translate(patients.Count(id => care.Examined.Contains(id)), patients.Count));
                    y = CommunityPageLayout.Line(a, y, "WFL_UX_Refusals".Translate(care.Refusals.Count(f => f.ActiveAt(now))));
                }
                y = CommunityPageLayout.Line(a, y, (care.IllnessRound && active ? "WFL_Care_OutbreakRound" : "WFL_Care_RoutineRound").Translate());
                bool mayDirect = OfficeAuthority.MayDirect(reg, FrameworkServices.AvatarId, OfficeKind.Healer);
                if (!mayDirect) y = CommunityPageLayout.Line(a, y, "WFL_UX_CareAuthority".Translate());
                y = CommunityPageLayout.Action(a, y, "WFL_Event_BeginCheckups".Translate(), () => Begin(reg, PendingEvent.Checkups), enabled && mayDirect && !active);
                return CommunityPageLayout.Action(a, y, "WFL_Event_BeginIllness".Translate(), () => Begin(reg, PendingEvent.Illness), enabled && mayDirect && !(active && care.IllnessRound));
            });
            return Mathf.Max(left, right);
        }
        private static void Begin(FamilyRegistry reg, int kind)
        {
            if (FamilyComponent.Current?.Registry != reg || !OfficeAuthority.MayDirect(reg, FrameworkServices.AvatarId, OfficeKind.Healer)) return;
            if (!CareEvents.BeginCare(kind)) Messages.Message("WFL_UX_CareStartFailed".Translate(), MessageTypeDefOf.RejectInput, false);
        }

    }
}
