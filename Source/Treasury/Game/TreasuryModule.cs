using System;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>Wil's Treasury's entry point: announces the module to the framework once, from the mod constructor. Registration is explicit; nothing is discovered later.</summary>
    public class TreasuryMod : Mod
    {
        public TreasuryMod(ModContentPack content) : base(content) { FamilyFramework.Install(new TreasuryModule()); }
    }

    /// <summary>What Wil's Treasury registers: its career (Treasurer, Tax Collector), its page, its work, its meeting and its listeners on the framework's economy hooks.</summary>
    public sealed class TreasuryModule : IFamilyModule
    {
        public const string Id = TreasuryService.Id;
        string IFamilyModule.Id => Id;

        public void Register()
        {
            var catalog = FamilyFramework.Catalog;
            catalog.Register(new ModuleInfo { Id = Id, Name = "Wil's Treasury", ApiVersion = FamilyFramework.ApiVersion, Requires = new[] { ModuleCatalog.FrameworkId } });
            catalog.Provide(Id, "wils.treasury.accounts");
            FamilyFramework.Commerce.Register(Id, () => FamilyComponent.Current == null ? null : new BusinessBank(FamilyComponent.Current.Registry.Ledger()));

            // The career: Treasurer is level 1 (anyone may be assigned); Tax Collector is level 2 and earned. Both are ordinary career roles, so assignment, progress and wages need nothing special.
            CareerService.RegisterCareer(new CareerDefinition { Id = TreasuryRoles.Career, ModuleId = Id, Level2 = new CareerRule { GeneralXp = TreasuryRoles.CollectorExperience }.Skill("Social", TreasuryRoles.CollectorSocial).Skill("Melee", TreasuryRoles.CollectorMelee) });
            CareerService.RegisterRole(new ProfessionRole { Id = TreasuryRoles.Treasurer, ModuleId = Id, Career = TreasuryRoles.Career, Level = 1 });
            CareerService.RegisterRole(new ProfessionRole { Id = TreasuryRoles.TaxCollector, ModuleId = Id, Career = TreasuryRoles.Career, Level = 2 });
            CareerService.Registered(Id);

            FamilyFramework.Pages.Register(new CommunityPage<System.Func<Rect, FamilyComponent, float>> { Id = TreasuryService.PageId, ModuleId = Id, LabelKey = "WFL_Treasury_Page", Order = 130, Icon = "UI/Commands/Draft", Render = TreasuryPage.Draw, Status = TreasuryPage.Attention });

            FamilyFramework.TradeCompleted.Add("wils.treasury.trade", Id, TreasuryService.OnTrade);
            FamilyFramework.SilverReceived.Add("wils.treasury.reward", Id, TreasuryService.OnSilver);
            FamilyFramework.FamilyEventsApplied.Add("wils.treasury.family", Id, TreasuryService.OnFamilyEvents);
            FamilyFramework.WorkReported.Add("wils.treasury.work", Id, TreasuryService.OnWork);
            FamilyFramework.LabourDone.Add("wils.treasury.labour", Id, TreasuryService.OnLabour);
            FamilyFramework.ResearchDone.Add("wils.treasury.research", Id, TreasuryService.OnResearch);
            FamilyFramework.Resets.Add("wils.treasury.reset", Id, () => { TreasuryService.ResetLabour(); TreasuryOffice.Reset(); });
            FamilyFramework.Roster.Register(new WorkItem { Id = "wils.treasury.periodic", ModuleId = Id, Interval = 2500, Run = TreasuryService.Periodic });
            FamilyFramework.Roster.Register(new WorkItem { Id = "wils.treasury.office", ModuleId = Id, Interval = 600, Run = TreasuryOffice.Run });
            FamilyFramework.Roster.Register(new WorkItem { Id = "wils.treasury.tax", ModuleId = Id, Interval = 1500, Run = TreasuryService.TaxPass });
            FamilyFramework.Meetings.Register(new MeetingDefinition { Id = "wils.treasury.collection", ModuleId = Id, JobDefName = "WFL_TaxVisit", Ticks = 240, ListenerSlackTicks = 900, Complete = (speaker, other, id, now) => TreasuryTaxVisit.OnCollectionVisit(speaker, other, id, now) });
            FamilyFramework.Removal.Add("wils.treasury.removal", Id, c =>
            {
                var report = new CleanupReport(Id);
                report.Step("Accounts, debts and payroll are kept in the save", () => "nothing to release");
                return report;
            });
            FamilyFramework.Reconcilers.Add("wils.treasury.reconcile", Id, c => { var l = c.Registry.Ledger(); if (!l.Conserved) Log.Warning("[Wil's Treasury] The saved ledger does not balance (claims " + l.TotalClaims + ", in minus out " + (l.ExternalIn - l.ExternalOut) + "). It is left as saved; nothing is adjusted silently."); });
        }
    }
}
