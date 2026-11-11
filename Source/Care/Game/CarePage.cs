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
    /// Community Care's page in the Community window: who provides care, how much routine care the community can give, where the check-up round stands, and the same buttons the medical building shows
    /// for beginning or passing a round. Registered by the module: when Community Care is off or absent the framework lists no such page.
    /// </summary>
    internal static class CarePage
    {
        public const string PageId = "wils.care.care";

        public static void Register(string moduleId) =>
            FamilyFramework.Pages.Register(new FamilyLife.Framework.CommunityPage<Func<Rect, FamilyComponent, float>> { Id = PageId, ModuleId = moduleId, LabelKey = "WFL_Care_Page", Order = 120, Render = Draw, Status = Attention, Icon = "WilsFamilyLife/Roles/healer" });

        private static int Attention() { var c = FamilyComponent.Current; return c == null ? 0 : CareEvents.IsSickCount(c); }

        private static float Line(Rect r, float y, string text, bool dim = false)
        {
            float h = Text.CalcHeight(text, r.width - 8f); if (dim) GUI.color = Color.gray;
            Widgets.Label(new Rect(4f, y, r.width - 8f, h), text); GUI.color = Color.white; return y + h + 2f;
        }
        private static float Heading(Rect r, float y, string text) { GUI.color = new Color(0.90f, 0.74f, 0.28f); Widgets.Label(new Rect(0f, y, r.width, 24f), text); GUI.color = Color.white; return y + 26f; }

        public static float Draw(Rect r, FamilyComponent c)
        {
            var reg = c.Registry; float y = 0f;
            y = Heading(r, y, "WFL_Care_Practitioners".Translate());
            var staff = CareCareers.Practitioners(reg).Where(id => reg.GetPerson(id)?.InColony == true).ToList();
            if (staff.Count == 0) y = Line(r, y, "WFL_Care_None".Translate(), true);
            foreach (int id in staff) y = Line(r, y, (c.TryGetPawn(id)?.LabelShort ?? "#" + id) + " - " + string.Join(", ", reg.Assignments().RolesOf(id).Select(x => { var role = CareerService.Professions.All.FirstOrDefault(p => p.Id == x); return role != null ? CareerService.TitleOf(role, reg) : x; })));
            y = Line(r, y + 4f, "WFL_Care_Capacity".Translate(CheckupWorkload.CommunityLimit(false), CheckupWorkload.CommunityLimit(true)));
            y += 8f;
            y = Heading(r, y, "WFL_Care_Rounds".Translate());
            var seat = reg.OfficeFor(OfficeKind.Healer);
            if (seat == null || !seat.Held) return Line(r, y, "WFL_Care_NoSeat".Translate(), true);
            bool outbreak = seat.Care().IllnessRound;
            y = Line(r, y, (outbreak ? "WFL_Care_OutbreakRound" : "WFL_Care_RoutineRound").Translate());
            if (Widgets.ButtonText(new Rect(4f, y, 190f, 26f), "WFL_Event_BeginCheckups".Translate())) CareEvents.BeginCare(PendingEvent.Checkups);
            if (Widgets.ButtonText(new Rect(200f, y, 190f, 26f), "WFL_Event_BeginIllness".Translate())) CareEvents.BeginCare(PendingEvent.Illness);
            return y + 34f;
        }
    }
}
