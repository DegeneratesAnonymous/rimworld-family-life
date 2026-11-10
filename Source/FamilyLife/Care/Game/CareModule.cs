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
    internal static class CareModule
    {
        internal const string Id = "wils.care";

        internal static void Register()
        {
            var catalog = FamilyModules.Catalog;
            catalog.Register(new ModuleInfo { Id = Id, Name = "Wil's Community Care", Requires = new[] { ModuleCatalog.FrameworkId } });
            catalog.Provide(Id, "wils.care.checkups");
            catalog.Provide(Id, "wils.care.outbreak");
            FamilyModules.Offices.Register(new OfficeDefinition { Id = "wils.care.healer", ModuleId = Id, Kind = OfficeKind.Healer, LabelKey = "WFL_OfficeKind_Healer" });
            FamilyModules.RegisterOfficeWork(Id, OfficeKind.Healer, OfficeDriver.HealerWork);
            FamilyModules.PlannedEvents.Register(new PlannedEventDefinition
            {
                Id = "wils.care.checkups", ModuleId = Id, SavedKind = PendingEvent.Checkups, Post = OfficeKind.Healer,
                NoticeKey = "Checkups", NoPostKey = "WFL_Event_NoPost_Doctor",
                Begin = () => OfficeEvents.BeginCare(PendingEvent.Checkups), Pass = () => OfficeEvents.PassCare(PendingEvent.Checkups)
            });
            FamilyModules.PlannedEvents.Register(new PlannedEventDefinition
            {
                Id = "wils.care.outbreak", ModuleId = Id, SavedKind = PendingEvent.Illness, Post = OfficeKind.Healer,
                NoticeKey = "Illness", NoPostKey = "WFL_Event_NoPost_Doctor",
                Begin = () => OfficeEvents.BeginCare(PendingEvent.Illness), Pass = () => OfficeEvents.PassCare(PendingEvent.Illness)
            });
            FamilyModules.BuildingGizmos.Add("wils.care.buttons", Id, OfficeEvents.CareGizmos);
            FamilyModules.Diagnostics.Add("wils.care.diagnostics", Id, new FamilyModules.DiagnosticSection { Kind = OfficeKind.Healer, Lines = OfficeDriver.DiagnoseHealer });
            FamilyModules.Resets.Add("wils.care.reset", Id, () => { ClinicVisits.Reset(); CareDemand.Reset(); });
        }
    }
}
