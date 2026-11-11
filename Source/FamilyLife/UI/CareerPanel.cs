using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>The pawn's existing assignments and a direct link into the shared career map.</summary>
    internal static class CareerPanel
    {
        internal static void Draw(Rect view, ref float y, Pawn pawn, FamilyComponent c)
        {
            if (pawn == null || c == null || pawn.Faction != Faction.OfPlayer || !pawn.RaceProps.Humanlike || pawn.DevelopmentalStage.Baby()) return;
            var roles = CareerService.Professions.All.Where(r => FamilyFramework.Catalog.IsEnabled(r.ModuleId)).ToList();
            if (roles.Count == 0) return;
            var reg = c.Registry; int id = pawn.thingIDNumber;
            if (reg.GetPerson(id)?.InColony != true) return;
            var book = reg.Assignments(); var ledger = reg.Careers();
            Text.Font = GameFont.Small;
            GUI.color = Color.gray; Widgets.Label(new Rect(0f, y, view.width, 22f), "WFL_Tab_Professions".Translate()); GUI.color = Color.white; y += 22f;
            var assigned = roles.Where(role => book.IsAssigned(id, role.Id)).Select(role => CareerService.TitleOf(role, reg)).ToList();
            y = CommunityPageLayout.Line(view, y, "WFL_UX_CurrentRoles".Translate(assigned.Count == 0 ? "WFL_UX_None".Translate().ToString() : string.Join(", ", assigned)));
            y = CommunityPageLayout.Action(view, y, "WFL_UX_ManageCareer".Translate(), () => CommunityCareerPage.OpenPerson(id));
            var history = ledger.HistoryOf(id);
            if (history.Count > 0)
            {
                string line = string.Join(", ", history.OrderByDescending(e => e.Tick).Take(5).Select(e => CareerService.Describe(e.Qualification)));
                GUI.color = Color.gray; Widgets.Label(new Rect(10f, y, view.width - 10f, 22f), "WFL_Career_History".Translate(line)); GUI.color = Color.white; y += 22f;
            }
            y += 4f;
        }

    }
}
