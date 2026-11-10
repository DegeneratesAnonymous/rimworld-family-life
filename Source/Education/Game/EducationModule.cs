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
    public sealed class EducationModule : IFamilyModule
    {
        public const string Id = "wils.education";
        string IFamilyModule.Id => Id;

        public void Register()
        {
            var catalog = FamilyFramework.Catalog;
            catalog.Register(new ModuleInfo { Id = Id, Name = "Wil's Educators", ApiVersion = FamilyFramework.ApiVersion, Requires = new[] { ModuleCatalog.FrameworkId } });
            catalog.Provide(Id, "wils.education.classes");
            catalog.Provide(Id, "wils.education.tutoring");
            catalog.Provide(Id, "wils.education.assessment");
            FamilyFramework.Offices.Register(new OfficeDefinition { Id = "wils.education.teacher", ModuleId = Id, Kind = OfficeKind.Teacher, LabelKey = "WFL_OfficeKind_Teacher", PassionSkills = new[] { "Intellectual", "Social" } });
            EducationCareers.Register(Id);
            FamilyFramework.RegisterFitness(OfficeKind.Teacher, EducationDriver.Fitness);
            FamilyFramework.RegisterOfficeWork(Id, OfficeKind.Teacher, EducationDriver.TeacherWork);
            FamilyFramework.PlannedEvents.Register(new PlannedEventDefinition
            {
                Id = "wils.education.assessment", ModuleId = Id, SavedKind = PendingEvent.Testing, Post = OfficeKind.Teacher,
                NoticeKey = "Testing", NoPostKey = "WFL_Event_NoPost_Teacher",
                Begin = EducationEvents.BeginAssessment, Pass = EducationEvents.PassTesting
            });
            FamilyFramework.BuildingGizmos.Add("wils.education.buttons", Id, LecternButtons);
            FamilyFramework.Diagnostics.Add("wils.education.diagnostics", Id, new FamilyFramework.DiagnosticSection { Kind = OfficeKind.Teacher, Lines = EducationDriver.DiagnoseTeacher });
            FamilyFramework.Removal.Add("wils.education.removal", Id, EducationRemoval.Prepare);
            FamilyFramework.Meetings.Register(new MeetingDefinition
            {
                Id = "wils.education.lesson", ModuleId = Id, JobDefName = "WFL_OfficeLesson", Ticks = EducationDriver.LessonTicks, ListenerSlackTicks = 1500,
                Prepare = (job, actor) => { if (EducationDriver.LecternCell(actor, out var lectern)) job.targetB = lectern; },          // the teacher speaks from the classroom's lectern
                Complete = (speaker, other, id, now) =>
                {
                    var r = FamilyComponent.Current?.Registry; if (r == null) return;
                    if (r.GetPerson(other.thingIDNumber)?.Juvenile != true && !(FamilyLifeMod.Settings?.adultStudents ?? false)) return;
                    if (r.OfficeFor(OfficeKind.Teacher)?.HolderId == speaker.thingIDNumber && r.OfficeFor(OfficeKind.Teacher).SessionQueue.Contains(other.thingIDNumber)) EducationDriver.CompleteLesson(r, speaker, other, now);
                    else if (r.Assignments().IsAssigned(speaker.thingIDNumber, EducationCareers.Tutor) || r.Assignments().IsAssigned(speaker.thingIDNumber, EducationCareers.Teacher)) EducationCareers.CompleteTutoring(r, speaker, other, now);
                }
            });
            FamilyFramework.Handoffs.Add("wils.education.handoff", Id, EducationDriver.OnTeacherChanged);
            FamilyFramework.Reconcilers.Add("wils.education.reconcile", Id, EducationDriver.Reconcile);
            FamilyFramework.Resets.Add("wils.education.reset", Id, ClassSessions.Reset);
        }

        private static IEnumerable<Gizmo> LecternButtons(Building b)
        {
            var hold = HoldClass.For(b);
            if (hold != null) yield return hold;
            foreach (var g in EducationEvents.EducationGizmos(b)) yield return g;
        }
    }
}
