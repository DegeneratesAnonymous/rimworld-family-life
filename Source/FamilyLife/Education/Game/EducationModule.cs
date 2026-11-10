using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>What Education registers with the framework: the Teacher post, the work that goes with it, the annual assessment, its buttons, and the capabilities other modules may ask about.</summary>
    internal static class EducationModule
    {
        internal const string Id = "wils.education";

        internal static void Register()
        {
            var catalog = FamilyModules.Catalog;
            catalog.Register(new ModuleInfo { Id = Id, Name = "Wil's Education", Requires = new[] { ModuleCatalog.FrameworkId } });
            catalog.Provide(Id, "wils.education.classes");
            catalog.Provide(Id, "wils.education.tutoring");
            catalog.Provide(Id, "wils.education.assessment");
            FamilyModules.Offices.Register(new OfficeDefinition { Id = "wils.education.teacher", ModuleId = Id, Kind = OfficeKind.Teacher, LabelKey = "WFL_OfficeKind_Teacher" });
            FamilyModules.RegisterOfficeWork(Id, OfficeKind.Teacher, OfficeDriver.TeacherWork);
            FamilyModules.PlannedEvents.Register(new PlannedEventDefinition
            {
                Id = "wils.education.assessment", ModuleId = Id, SavedKind = PendingEvent.Testing, Post = OfficeKind.Teacher,
                NoticeKey = "Testing", NoPostKey = "WFL_Event_NoPost_Teacher",
                Begin = OfficeEvents.BeginAssessment, Pass = OfficeEvents.PassTesting
            });
            FamilyModules.BuildingGizmos.Add("wils.education.buttons", Id, LecternButtons);
            FamilyModules.Diagnostics.Add("wils.education.diagnostics", Id, new FamilyModules.DiagnosticSection { Kind = OfficeKind.Teacher, Lines = OfficeDriver.DiagnoseTeacher });
            FamilyModules.Removal.Add("wils.education.removal", Id, EducationRemoval.Prepare);
            FamilyModules.Resets.Add("wils.education.reset", Id, ClassSessions.Reset);
        }

        private static IEnumerable<Gizmo> LecternButtons(Building b)
        {
            var hold = HoldClass.For(b);
            if (hold != null) yield return hold;
            foreach (var g in OfficeEvents.EducationGizmos(b)) yield return g;
        }
    }
}
