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
            foreach (var spec in CareSpecialties.All)
            {
                CareerService.Career(CareRoles.Career).Specialties[spec.Key] = CareSpecialties.Rule(spec);
                CareerService.RegisterRole(new ProfessionRole { Id = spec.RoleId, ModuleId = moduleId, Career = CareRoles.Career, Level = 3, Specialty = spec.Key });
            }
            CareSpecialistWork.Register(moduleId);
            FamilyFramework.OperationCompleted.Add(moduleId + ".surgery", moduleId, CareSpecialistWork.OnSurgery);
            FamilyFramework.Tended.Add(moduleId + ".tend", moduleId, CareSpecialistWork.OnTend);
            // Doctors sit in the old working seat; until the player has assigned one, the Healers carry on exactly as before.
            CareerService.RegisterSeat(moduleId, OfficeKind.Healer, CareRoles.Doctor, CareRoles.Healer);
            FamilyFramework.Roster.Register(new WorkItem { Id = moduleId + ".terms", ModuleId = moduleId, Interval = 600, Run = now => { var c = FamilyComponent.Current; if (c != null) { ObserveTerms(c, now); UpdateCapacity(c); } } });
            FamilyFramework.Roster.Register(new WorkItem { Id = moduleId + ".practitioners", ModuleId = moduleId, Interval = 300, Run = now => { var c = FamilyComponent.Current; if (c != null) PractitionerWork(c, now); } });
        }

        /// <summary>
        /// How much routine care a worker can do today: 0 if barred from doctoring or not on a map, otherwise the lower of their manipulation and consciousness (1 is fully able). It is working
        /// capacity, not medical skill: a skilled Nurse is as busy as a Doctor, and skill still decides how well each visit goes.
        /// </summary>
        internal static float CapacityOf(Pawn p)
        {
            if (p == null || p.Dead || !p.Spawned || p.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) || p.health?.capacities == null) return 0f;
            float manip = p.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation), cons = p.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
            return Math.Max(0f, Math.Min(1f, Math.Min(manip, cons)));
        }

        /// <summary>The community's combined capacity: everyone assigned to care who can work now. Recomputed often and shared through <see cref="CheckupWorkload.TotalCapacity"/>.</summary>
        private static void UpdateCapacity(FamilyComponent c)
        {
            var r = c.Registry; float total = 0f;
            foreach (int id in Practitioners(r))
                if (r.GetPerson(id)?.InColony == true) total += CapacityOf(c.TryGetPawn(id));
            var seat = r.OfficeFor(OfficeKind.Healer); if (total <= 0f && seat != null && seat.Held) total = Math.Max(1f, CapacityOf(c.TryGetPawn(seat.HolderId)));          // an old save before the first migration pass
            CheckupWorkload.TotalCapacity = Math.Max(1f, total);
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
            foreach (int id in Practitioners(r))
                if (id != office.HolderId && r.GetPerson(id)?.InColony == true && c.TryGetPawn(id) is Pawn worker) CareDriver.AssistantWork(c, r, office, worker, now);
        }

        /// <summary>Everyone assigned to care work: Healers/Nurses, Doctors and specialists. A specialist keeps doing routine care.</summary>
        internal static System.Collections.Generic.List<int> Practitioners(FamilyRegistry r) =>
            r.Assignments().Holders(CareRoles.Doctor).Concat(r.Assignments().Holders(CareRoles.Healer)).Concat(CareSpecialties.RoleIds.SelectMany(id => r.Assignments().Holders(id))).Distinct().ToList();

        /// <summary>Experience for a finished check-up; a better one is worth a little more.</summary>
        internal static void Report(FamilyComponent c, Pawn worker, Pawn patient, ResultQuality level, int now) =>
            CareerService.Report(c, worker, CareRoles.Career, null, "care.checkup", "checkup|" + patient.thingIDNumber + "|" + now, CareRoles.CheckupPoints + (int)level, now);
    }
}
