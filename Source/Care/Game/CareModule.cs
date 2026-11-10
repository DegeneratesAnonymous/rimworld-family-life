using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>What Community Care registers with the framework: the Healer post, its work, the check-up round and the outbreak round, and the capabilities other modules may ask about.</summary>
    public sealed class CareModule : IFamilyModule
    {
        public const string Id = "wils.care";
        string IFamilyModule.Id => Id;

        public void Register()
        {
            var catalog = FamilyFramework.Catalog;
            catalog.Register(new ModuleInfo { Id = Id, Name = "Wil's Community Care", ApiVersion = FamilyFramework.ApiVersion, Requires = new[] { ModuleCatalog.FrameworkId } });
            catalog.Provide(Id, "wils.care.checkups");
            catalog.Provide(Id, "wils.care.outbreak");
            FamilyFramework.Offices.Register(new OfficeDefinition { Id = "wils.care.healer", ModuleId = Id, Kind = OfficeKind.Healer, LabelKey = "WFL_OfficeKind_Healer", PassionSkills = new[] { "Medicine" } });
            CareCareers.Register(Id);
            FamilyFramework.RegisterFitness(OfficeKind.Healer, CareDriver.Fitness);
            FamilyFramework.RegisterOfficeWork(Id, OfficeKind.Healer, CareDriver.HealerWork);
            FamilyFramework.PlannedEvents.Register(new PlannedEventDefinition
            {
                Id = "wils.care.checkups", ModuleId = Id, SavedKind = PendingEvent.Checkups, Post = OfficeKind.Healer,
                NoticeKey = "Checkups", NoPostKey = "WFL_Event_NoPost_Doctor",
                Begin = () => CareEvents.BeginCare(PendingEvent.Checkups), Pass = () => CareEvents.PassCare(PendingEvent.Checkups)
            });
            FamilyFramework.PlannedEvents.Register(new PlannedEventDefinition
            {
                Id = "wils.care.outbreak", ModuleId = Id, SavedKind = PendingEvent.Illness, Post = OfficeKind.Healer,
                NoticeKey = "Illness", NoPostKey = "WFL_Event_NoPost_Doctor",
                Begin = () => CareEvents.BeginCare(PendingEvent.Illness), Pass = () => CareEvents.PassCare(PendingEvent.Illness)
            });
            FamilyFramework.BuildingGizmos.Add("wils.care.buttons", Id, CareEvents.CareGizmos);
            FamilyFramework.Diagnostics.Add("wils.care.diagnostics", Id, new FamilyFramework.DiagnosticSection { Kind = OfficeKind.Healer, Lines = CareDriver.DiagnoseHealer });
            FamilyFramework.DueNow.Add("wils.care.duenow", Id, o => { if (o.Kind == OfficeKind.Healer) o.Care().NextRoundTick = 0; });
            FamilyFramework.Removal.Add("wils.care.removal", Id, CareRemoval.Prepare);
            FamilyFramework.Meetings.Register(new MeetingDefinition
            {
                Id = "wils.care.checkup", ModuleId = Id, JobDefName = "WFL_Checkup", Ticks = CareDriver.CheckupTicks, ListenerSlackTicks = 600,
                Prepare = (job, actor) => { if (CareDriver.MedicalRoomCell(actor, out var room)) job.targetB = room; },          // the examination is held in the medical room when there is one
                Complete = (speaker, other, id, now) =>
                {
                    var r = FamilyComponent.Current?.Registry; if (r == null) return;
                    if (r.OfficeFor(OfficeKind.Healer)?.HolderId == speaker.thingIDNumber || r.IsPractitioner(speaker.thingIDNumber)) CareDriver.CompleteCheckup(r, speaker, other, now);
                }
            });
            FamilyFramework.Handoffs.Add("wils.care.handoff", Id, CareDriver.OnHealerChanged);
            FamilyFramework.Reconcilers.Add("wils.care.reconcile", Id, CareDriver.Reconcile);
            FamilyFramework.Resets.Add("wils.care.reset", Id, () => { ClinicVisits.Reset(); CareDemand.Reset(); CareDriver.Claims.Clear(); });
        }
    }
}
