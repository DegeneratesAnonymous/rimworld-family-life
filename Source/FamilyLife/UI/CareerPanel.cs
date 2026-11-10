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
            foreach (var career in CareerService.Careers.Where(cd => FamilyFramework.Catalog.IsEnabled(cd.ModuleId))) DrawProgress(view, ref y, pawn, reg, ledger, book, career);
            var history = ledger.HistoryOf(id);
            if (history.Count > 0)
            {
                string line = string.Join(", ", history.OrderByDescending(e => e.Tick).Take(5).Select(e => CareerService.Describe(e.Qualification)));
                GUI.color = Color.gray; Widgets.Label(new Rect(10f, y, view.width - 10f, 22f), "WFL_Career_History".Translate(line)); GUI.color = Color.white; y += 22f;
            }
            y += 4f;
        }

        /// <summary>
        /// Progress through one career: experience against what each step needs, each required skill against the pawn's own (green when met), and a bar for each. Shown only once the pawn has any
        /// experience, qualification or assignment in the career, and specialties only once they could matter.
        /// </summary>
        private static void DrawProgress(Rect view, ref float y, Pawn pawn, FamilyRegistry reg, CareerLedger ledger, AssignmentBook book, CareerDefinition career)
        {
            int id = pawn.thingIDNumber;
            var rows = ledger.Progress(id, career, CareerService.SkillsOf(pawn));
            bool involved = ledger.GeneralXp(id, career.Id) > 0 || ledger.IsQualified(id, career.Id) || CareerService.Professions.All.Any(r => r.Career == career.Id && book.IsAssigned(id, r.Id));
            if (!involved) return;
            string careerName = CareerService.CareerName(career, reg);
            foreach (var row in rows)
            {
                if (row.Key != null && !(row.Earned || row.SpecialtyXp > 0 || row.Available && row.GeneralXp > 0)) continue;
                var role = CareerService.Professions.All.FirstOrDefault(r => r.Career == career.Id && (row.Key == null ? r.Level == 2 : r.Level == 3 && r.Specialty == row.Key));
                string name = role != null ? CareerService.TitleOf(role, reg) : careerName;
                GUI.color = row.Earned ? new Color(.72f, 1f, .72f) : Color.white;
                Widgets.Label(new Rect(10f, y, view.width - 10f, 22f), name + (row.Earned ? " \u2713" : row.Available ? "" : " (needs " + CareerService.Describe(career.Id) + ")")); GUI.color = Color.white; y += 20f;
                if (row.Earned) continue;
                y = Bar(view, y, "WFL_Career_General".Translate(row.GeneralXp, row.GeneralNeeded), row.GeneralShare);
                if (row.Key != null) y = Bar(view, y, "WFL_Career_Specialty".Translate(row.SpecialtyXp, row.SpecialtyNeeded), row.SpecialtyShare);
                foreach (var k in row.Skills)
                {
                    bool met = k.Value[0] >= k.Value[1];
                    GUI.color = met ? new Color(.72f, 1f, .72f) : new Color(1f, .78f, .62f);
                    Widgets.Label(new Rect(28f, y, view.width - 28f, 20f), "WFL_Career_Skill".Translate(k.Key, k.Value[0], k.Value[1])); GUI.color = Color.white; y += 18f;
                }
                y += 4f;
            }
        }

        private static float Bar(Rect view, float y, string label, float share)
        {
            Widgets.Label(new Rect(28f, y, view.width - 28f, 18f), label); y += 18f;
            Widgets.FillableBar(new Rect(28f, y, view.width - 48f, 8f), share); return y + 12f;
        }
    }
}
