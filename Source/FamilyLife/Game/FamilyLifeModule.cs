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
    internal sealed class FamilyLifeModule : IFamilyModule
    {
        internal const string Id = "wils.familylife";
        string IFamilyModule.Id => Id;

        public void Register()
        {
            FamilyFramework.Catalog.Register(new ModuleInfo { Id = Id, Name = "Wil's Community Framework", ApiVersion = FamilyFramework.ApiVersion, Requires = new[] { ModuleCatalog.FrameworkId } });
            FamilyFramework.Catalog.Provide(Id, "wils.familylife.meals");
            FamilyFramework.Offices.Register(new OfficeDefinition { Id = "wils.familylife.chaplain", ModuleId = Id, Kind = OfficeKind.Chaplain, LabelKey = "WFL_OfficeKind_Chaplain" });
            FamilyFramework.RegisterFitness(OfficeKind.Chaplain, FrameworkServices.SocialFitness);
            CommunityCorePages.Register(Id);
        }
    }
}
