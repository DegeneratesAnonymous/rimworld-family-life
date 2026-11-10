using System;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>Community Care's career: Healer/Nurse (anyone), Doctor (qualified), specialists later. Owner: Community Care.</summary>
    internal static class CareCareers
    {
        public static void Register(string moduleId)
        {
            CareerService.RegisterCareer(new CareerDefinition { Id = CareRoles.Career, ModuleId = moduleId, Level2 = new CareerRule { GeneralXp = CareRoles.DoctorExperience }.Skill("Medicine", CareRoles.DoctorMedicine) });
            CareerService.RegisterRole(new ProfessionRole { Id = CareRoles.Healer, ModuleId = moduleId, Career = CareRoles.Career, Level = 1, TitleKey = CareTermsAccess.EntryLevelTitleKey });
            CareerService.RegisterRole(new ProfessionRole { Id = CareRoles.Doctor, ModuleId = moduleId, Career = CareRoles.Career, Level = 2 });
            // Doctors sit in the old working seat; until the player has assigned one, the Healers carry on exactly as before.
            CareerService.RegisterSeat(moduleId, OfficeKind.Healer, CareRoles.Doctor, CareRoles.Healer);
            FamilyFramework.Roster.Register(new WorkItem { Id = moduleId + ".terms", ModuleId = moduleId, Interval = 600, Run = now => { var c = FamilyComponent.Current; if (c != null) ObserveTerms(c, now); } });
            FamilyFramework.Roster.Register(new WorkItem { Id = moduleId + ".practitioners", ModuleId = moduleId, Interval = 300, Run = now => { var c = FamilyComponent.Current; if (c != null) PractitionerWork(c, now); } });
        }

        /// <summary>The first Doctor the player assigns turns the entry-level title from Healer to Nurse, for good. Nothing else changes.</summary>
        private static void ObserveTerms(FamilyComponent c, int now)
        {
            var r = c.Registry; int doctors = r.Assignments().Holders(CareRoles.Doctor).Count(id => r.GetPerson(id)?.InColony == true);
            if (r.CareTerms().Observe(doctors, now)) Messages.Message("WFL_Care_NurseTitles".Translate(), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>Every assigned care worker other than the one in the old working seat does their own routine visits, sharing the round and budget.</summary>
        private static void PractitionerWork(FamilyComponent c, int now)
        {
            var r = c.Registry; var office = r.OfficeFor(OfficeKind.Healer); if (office == null || !office.Held) return;
            foreach (int id in r.Assignments().Holders(CareRoles.Doctor).Concat(r.Assignments().Holders(CareRoles.Healer)).Distinct().ToList())
                if (id != office.HolderId && r.GetPerson(id)?.InColony == true && c.TryGetPawn(id) is Pawn worker) CareDriver.AssistantWork(c, r, office, worker, now);
        }

        /// <summary>Experience for a finished check-up; a better one is worth a little more.</summary>
        internal static void Report(FamilyComponent c, Pawn worker, Pawn patient, ResultQuality level, int now) =>
            CareerService.Report(c, worker, CareRoles.Career, null, "care.checkup", "checkup|" + patient.thingIDNumber + "|" + now, CareRoles.CheckupPoints + (int)level, now);
    }
}
