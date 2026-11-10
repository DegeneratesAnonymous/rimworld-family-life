using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Family Life as registered today. It still includes the existing house features that have not yet been given a module of their own (see docs/MODULAR-ARCHITECTURE.md), among
    /// them the hall keeper and the chaplain, so those posts are owned here rather than in the framework.
    /// </summary>
    internal static class FamilyLifeModule
    {
        internal const string Id = "wils.familylife";

        internal static void Register()
        {
            FamilyModules.Catalog.Register(new ModuleInfo { Id = Id, Name = "Wil's Family Life", Requires = new[] { ModuleCatalog.FrameworkId } });
            FamilyModules.Catalog.Provide(Id, "wils.familylife.meals");
            FamilyModules.Offices.Register(new OfficeDefinition { Id = "wils.familylife.hallkeeper", ModuleId = Id, Kind = OfficeKind.HallKeeper, LabelKey = "WFL_OfficeKind_HallKeeper" });
            FamilyModules.Offices.Register(new OfficeDefinition { Id = "wils.familylife.chaplain", ModuleId = Id, Kind = OfficeKind.Chaplain, LabelKey = "WFL_OfficeKind_Chaplain" });
            FamilyModules.RegisterOfficeWork(Id, OfficeKind.HallKeeper, OfficeDriver.HallWork);
        }
    }
}
