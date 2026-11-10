using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The professions section of the pawn's Family tab: for each profession the installed modules define, whether the pawn is assigned, whether they are qualified, what is still
    /// missing, and the button to assign or unassign them. Assigning is the player's choice and never follows from qualifying. Owner: the framework.
    /// </summary>
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
            foreach (var role in roles)
            {
                bool assigned = book.IsAssigned(id, role.Id);
                var check = AssignmentBook.Check(role, ledger, id);
                var def = CareerService.Career(role.Career);
                string title = CareerService.TitleOf(role, reg);
                string status = assigned ? "WFL_Career_Assigned".Translate().ToString()
                    : check == AssignResult.Assigned ? (role.Level > 1 ? "WFL_Career_Qualified_Short".Translate().ToString() : "WFL_Career_Open".Translate().ToString())
                    : "WFL_Career_Needs".Translate(string.Join(", ", ledger.Missing(id, role.Career, role.Level >= 3 ? role.Specialty : null, role.Level >= 3 && def != null && def.Specialties.TryGetValue(role.Specialty, out var sr) ? sr : def?.Level2 ?? new CareerRule(), CareerService.SkillsOf(pawn)))).ToString();
                Widgets.Label(new Rect(10f, y, view.width - 110f, 22f), title + " - " + status);
                TooltipHandler.TipRegion(new Rect(10f, y, view.width - 110f, 22f), "WFL_Career_Experience".Translate(ledger.GeneralXp(id, role.Career)));
                bool can = assigned || check == AssignResult.Assigned;
                if (can && Widgets.ButtonText(new Rect(view.width - 100f, y, 96f, 22f), (assigned ? "WFL_Career_Unassign" : "WFL_Career_Assign").Translate()))
                {
                    if (assigned) book.Unassign(id, role.Id); else book.Assign(id, role, ledger, Find.TickManager.TicksGame);
                }
                y += 26f;
            }
            var history = ledger.HistoryOf(id);
            if (history.Count > 0)
            {
                string line = string.Join(", ", history.OrderByDescending(e => e.Tick).Take(3).Select(e => CareerService.Describe(e.Qualification)));
                GUI.color = Color.gray; Widgets.Label(new Rect(10f, y, view.width - 10f, 22f), "WFL_Career_History".Translate(line)); GUI.color = Color.white; y += 22f;
            }
            y += 4f;
        }
    }
}
