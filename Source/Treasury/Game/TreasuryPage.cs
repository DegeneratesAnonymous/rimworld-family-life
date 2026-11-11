using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The Treasury page of the Community window, in six subpages: Overview, Accounts, Payroll, Taxation, Debts and Transactions. It only reads the ledger and calls its operations; the page can be
    /// closed, broken or absent without any account changing.
    /// </summary>
    internal static class TreasuryPage
    {
        private static int sub;
        private static bool showAccounting;
        private static TreasuryState policyOwner;
        private static int draftPercent, observedPercent;
        private static string transferFrom, transferTo; private static string amountText = "10"; private static int amount = 10;
        private static readonly string[] Tabs = { "WFL_Tr_Overview", "WFL_Tr_Accounts", "WFL_Tr_Payroll", "WFL_Tr_Taxation", "WFL_Tr_Debts", "WFL_Tr_Transactions", "WFL_UX_AssetsPolicies" };

        /// <summary>The number shown on the page's navigation entry: wages owed plus overdue tax, so something needing the player's eye is visible without opening it.</summary>
        public static int Attention()
        {
            var c = FamilyComponent.Current; if (c == null) return 0;
            var l = c.Registry.Ledger(); int today = TreasuryService.Today();
            return l.Debts.Count(d => d.Active && d.Kind == DebtKind.Wage) + l.Debts.Count(d => d.Active && d.Kind == DebtKind.Tax && d.DueDay < today - 7);
        }

        private static string NameOfPawn(FamilyComponent c, int id) => c.TryGetPawn(id)?.LabelShort ?? c.Registry.GetPerson(id)?.Name ?? "#" + id;
        public static string NameOf(FamilyComponent c, string account)
        {
            var a = c.Registry.Ledger().Get(account); if (a == null) return account ?? "WFL_Tr_Outside".Translate().ToString();
            switch (a.Kind)
            {
                case AccountKind.Community: return "WFL_Tr_Community".Translate();
                case AccountKind.Person: return NameOfPawn(c, a.Owner);
                case AccountKind.Household: { var h = c.Registry.GetHousehold(a.Owner); var first = h?.ResidentIds.FirstOrDefault(); return "WFL_Tr_Household".Translate(first.HasValue ? NameOfPawn(c, first.Value) : "#" + a.Owner); }
                default: return "WFL_Tr_Business".Translate(a.Owner);
            }
        }

        private static float Line(Rect r, float y, string text, bool dim = false)
        {
            float h = Text.CalcHeight(text, r.width - 8f); if (dim) GUI.color = Color.gray;
            Widgets.Label(new Rect(4f, y, r.width - 8f, h), text); GUI.color = Color.white; return y + h + 2f;
        }
        private static float Heading(Rect r, float y, string text) { GUI.color = new Color(0.90f, 0.74f, 0.28f); Widgets.Label(new Rect(0f, y, r.width, 24f), text); GUI.color = Color.white; return y + 26f; }

        public static float Draw(Rect r, FamilyComponent c)
        {
            float y = 0f, x = 0f, rowHeight = 0f;
            for (int i = 0; i < Tabs.Length; i++)
            {
                string label = Tabs[i].Translate(); float w = Mathf.Min(r.width, Mathf.Max(90f, Text.CalcSize(label).x + 20f));
                float h = Mathf.Max(30f, Text.CalcHeight(label, w - 16f) + 10f);
                if (x > 0f && x + w > r.width) { x = 0f; y += rowHeight + 8f; rowHeight = 0f; }
                var tab = new Rect(x, y, w, h);
                if (Widgets.ButtonText(tab, label)) sub = i;
                if (i == sub) Widgets.DrawBoxSolid(new Rect(tab.x, tab.yMax - 3f, tab.width, 3f), new Color(.9f, .74f, .28f));
                x += w + 4f; rowHeight = Mathf.Max(rowHeight, h);
            }
            y += rowHeight + 12f;
            switch (sub)
            {
                case 0: return Overview(r, c, y);
                case 1: return Accounts(r, c, y);
                case 2: return Payroll(r, c, y);
                case 3: return Taxation(r, c, y);
                case 4: return Debts(r, c, y);
                case 5: return Transactions(r, c, y);
                default: return AssetsPolicies(r, c, y);
            }
        }

        private static string Names(FamilyComponent c, string role)
        {
            var list = TreasuryService.Holders(c.Registry, role).Select(id => NameOfPawn(c, id)).ToList();
            return list.Count == 0 ? "WFL_Tr_None".Translate().ToString() : string.Join(", ", list);
        }

        private static float Overview(Rect r, FamilyComponent c, float y)
        {
            var reg = c.Registry; var l = reg.Ledger(); var pay = reg.Payroll(); int today = TreasuryService.Today();
            float width = CommunityPageLayout.ColumnWidth(r);
            int days = PayCalendar.NextPayday(today + 1) - today;
            string[] labels = { "WFL_UX_FundsLabel", "WFL_UX_WagesLabel", "WFL_UX_PaydayLabel" };
            string[] values = { "WFL_UX_SilverValue".Translate(l.Available(Ledger.CommunityId)), "WFL_UX_SilverValue".Translate(pay.PendingTotal), "WFL_UX_DaysValue".Translate(days) };
            int metricColumns = r.width >= 620f ? 3 : 1;
            float metricWidth = (r.width - (metricColumns - 1) * 16f) / metricColumns;
            float metricY = y, rowHeight = 0f;
            for (int i = 0; i < labels.Length; i++)
            {
                if (i > 0 && i % metricColumns == 0) { metricY += rowHeight + 10f; rowHeight = 0f; }
                GUI.BeginGroup(new Rect(i % metricColumns * (metricWidth + 16f), metricY, metricWidth, 1000f));
                try { rowHeight = Mathf.Max(rowHeight, CommunityPageLayout.Metric(new Rect(0f, 0f, metricWidth, 1000f), labels[i].Translate(), values[i])); }
                finally { GUI.EndGroup(); }
            }
            y = metricY + rowHeight + 10f;
            int wageDebt = l.Debts.Where(d => d.Active && d.Kind == DebtKind.Wage).Sum(d => d.Outstanding);
            if (wageDebt > 0) y = Line(r, y, "WFL_UX_OverdueWages".Translate(wageDebt));
            if (l.Unbacked > 0)
            {
                GUI.color = new Color(1f, .75f, .6f);
                y = Line(r, y, "WFL_Tr_Discrepancy".Translate(l.Unbacked, l.TotalAvailable));
                y = Line(r, y, "WFL_Tr_Bearer".Translate(l.CommunityShare, l.SpilledToOwners));
            }
            y = CommunityPageLayout.Action(r, y, (showAccounting ? "WFL_UX_HideAccounting" : "WFL_UX_ShowAccounting").Translate(), () => showAccounting = !showAccounting);
            if (showAccounting)
            {
                y = Line(r, y, "WFL_UX_AccountingHelp".Translate());
                y = Line(r, y, "WFL_UX_CommunityOwned".Translate(l.Balance(Ledger.CommunityId)));
                y = Line(r, y, "WFL_UX_PhysicalClaims".Translate(l.TotalClaims, TreasuryService.PhysicalSilver()));
                y = Line(r, y, "WFL_Tr_Owned".Translate(l.TotalClaims, l.TotalAvailable, l.Unbacked));
                var personal = l.Accounts.Where(a => a.Kind != AccountKind.Community && !a.Closed).ToList();
                y = Line(r, y, "WFL_Tr_Wealth".Translate(personal.Sum(a => a.Balance), personal.Count));
                var open = l.Debts.Where(d => d.Active).ToList();
                y = Line(r, y, "WFL_Tr_DebtsTotal".Translate(open.Sum(d => d.Outstanding), open.Count));
            }
            float start = y + 8f;
            float left = CommunityPageLayout.Section(r, 0f, start, width, "WFL_UX_TreasuryStaff".Translate(), a =>
            {
                float h = Line(a, 0f, "WFL_Tr_Treasurer".Translate(Names(c, TreasuryRoles.Treasurer)));
                h = CommunityPageLayout.Action(a, h, "WFL_UX_ChooseTreasurer".Translate(), () => CommunityCareerPage.OpenRole(TreasuryRoles.Treasurer));
                h = Line(a, h, "WFL_Tr_Collector".Translate(Names(c, TreasuryRoles.TaxCollector)));
                return CommunityPageLayout.Action(a, h, "WFL_UX_ChooseCollector".Translate(), () => CommunityCareerPage.OpenRole(TreasuryRoles.TaxCollector));
            });
            bool columns = CommunityPageLayout.TwoColumns(r);
            float right = CommunityPageLayout.Section(r, columns ? width + CommunityPageLayout.Gap : 0f, columns ? start : left, width, "WFL_UX_TreasurySpace".Translate(), a =>
            {
                var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
                var zone = map?.zoneManager.AllZones.OfType<Zone_Treasury>().OrderBy(z => z.ID).FirstOrDefault();
                float h = Line(a, 0f, zone == null ? "WFL_UX_NoTreasuryZone".Translate().ToString() : zone.GetInspectString());
                return CommunityPageLayout.Action(a, h, "WFL_UX_ZoneHelpButton".Translate(), () => Find.WindowStack.Add(new Dialog_MessageBox("WFL_UX_ZoneHelp".Translate())));
            });
            y = Mathf.Max(left, right);
            y = Heading(r, y, "WFL_UX_RecentActivity".Translate());
            foreach (var t in l.History.Reverse().Take(4)) y = Line(r, y, TxText(c, t));
            return CommunityPageLayout.Action(r, y, "WFL_UX_ViewTransactions".Translate(), () => sub = 5);
        }

        private static float AssetsPolicies(Rect r, FamilyComponent c, float y)
        {
            var st = c.Registry.Treasury();
            if (policyOwner != st || observedPercent != st.CommunityPercent)
            {
                policyOwner = st; draftPercent = observedPercent = st.CommunityPercent;
            }
            y = Heading(r, y, "WFL_UX_IncomePolicy".Translate());
            y = Line(r, y, "WFL_UX_IncomeScope".Translate());
            y = Line(r, y, "WFL_UX_IncomeCurrent".Translate(st.CommunityPercent, draftPercent));
            draftPercent = (int)Widgets.HorizontalSlider(new Rect(4f, y, Mathf.Min(320f, r.width - 8f), 24f), draftPercent, 0f, 100f, true, null, null, null, 5f);
            y += 32f;
            y = CommunityPageLayout.Action(r, y, "WFL_UX_ApplyPolicy".Translate(), () =>
            {
                st.CommunityPercent = draftPercent; observedPercent = draftPercent;
                Messages.Message("WFL_UX_PolicyApplied".Translate(), MessageTypeDefOf.NeutralEvent, false);
            }, draftPercent != st.CommunityPercent);
            y = Heading(r, y, "WFL_UX_TrackedAssets".Translate());
            y = Line(r, y, "WFL_UX_TrackingHelp".Translate());
            // The asset listing is explicitly scoped to items on the current map; no mixed colony/map totals.
            var map = Find.CurrentMap ?? Find.AnyPlayerHomeMap;
            y = Line(r, y, "WFL_UX_AssetMap".Translate(map?.ToString() ?? "WFL_UX_NoMap".Translate().ToString()));
            foreach (var val in st.Valuables.All.ToList())
            {
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(val.DefName);
                int quantity = map == null || def == null ? 0 : map.listerThings.ThingsOfDef(def).Sum(t => t.stackCount);
                string label = def == null ? val.DefName : def.LabelCap.ToString();
                y = Line(r, y, "WFL_UX_AssetLine".Translate(label, quantity, (val.Liquid ? "WFL_UX_LiquidSilver" : "WFL_UX_StoredAsset").Translate().ToString()));
                if (val.DefName != "Silver")
                {
                    string name = val.DefName;
                    y = CommunityPageLayout.Action(r, y, "WFL_UX_StopTracking".Translate(label), () => st.Valuables.Remove(name));
                }
            }
            return CommunityPageLayout.Action(r, y, "WFL_Tr_AddValuable".Translate(), () =>
            {
                var options = DefDatabase<ThingDef>.AllDefs.Where(d => d.category == ThingCategory.Item && d.BaseMarketValue >= 5f && !st.Valuables.Recognises(d.defName)).OrderBy(d => d.label).Select(d =>
                {
                    var name = d.defName;
                    return new FloatMenuOption(d.LabelCap, () => st.Valuables.Set(name, false));
                }).ToList();
                if (options.Count == 0) options.Add(new FloatMenuOption("WFL_UX_NoAssetsToAdd".Translate(), null));
                Find.WindowStack.Add(new FloatMenu(options));
            });
        }

        private static string TxText(FamilyComponent c, Transaction t)
        {
            // Initialization is identified by its original reconciliation key and memo, not by amount or row position.
            if (t.Kind == TxKind.Correction && t.From == null && t.To == Ledger.CommunityId && t.Memo == "silver already in storage when the treasury began")
                return "WFL_UX_StartingBalance".Translate(t.Amount);
            string kind = "WFL_UX_Tx_" + t.Kind;
            string text = "WFL_Tr_TxLine".Translate(t.Day % PayCalendar.DaysPerYear + 1, kind.CanTranslate() ? kind.Translate().ToString() : t.Kind.ToString(), t.Amount, t.From == null ? "WFL_Tr_Outside".Translate().ToString() : NameOf(c, t.From), t.To == null ? "WFL_Tr_Outside".Translate().ToString() : NameOf(c, t.To));
            if (t.Kind == TxKind.Correction && !string.IsNullOrEmpty(t.Memo)) text += " — " + t.Memo;
            return text;
        }

        private static float Accounts(Rect r, FamilyComponent c, float y)
        {
            var l = c.Registry.Ledger();
            foreach (var a in l.Accounts.Where(x => !x.Closed).OrderBy(x => x.Kind).ThenByDescending(x => x.Balance)) { y = Line(r, y, "WFL_Tr_AccountLine".Translate(NameOf(c, a.Id), a.Balance)); }
            y += 8f; y = Heading(r, y, "WFL_Tr_Transfer".Translate());
            var open = l.Accounts.Where(x => !x.Closed).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
            if (Widgets.ButtonText(new Rect(4f, y, 200f, 24f), "WFL_Tr_TransferFrom".Translate() + ": " + (transferFrom == null ? "-" : NameOf(c, transferFrom)))) Find.WindowStack.Add(new FloatMenu(open.Select(a => { var id = a.Id; return new FloatMenuOption(NameOf(c, id), () => transferFrom = id); }).ToList()));
            if (Widgets.ButtonText(new Rect(212f, y, 200f, 24f), "WFL_Tr_TransferTo".Translate() + ": " + (transferTo == null ? "-" : NameOf(c, transferTo)))) Find.WindowStack.Add(new FloatMenu(open.Select(a => { var id = a.Id; return new FloatMenuOption(NameOf(c, id), () => transferTo = id); }).ToList()));
            Widgets.TextFieldNumeric(new Rect(420f, y, 70f, 24f), ref amount, ref amountText, 1f, 1000000f);
            y += 30f;
            if (transferFrom != null && transferTo != null && Widgets.ButtonText(new Rect(4f, y, 220f, 26f), "WFL_Tr_TransferDo".Translate(amount)))
            {
                var res = l.Transfer(transferFrom, transferTo, amount, TxKind.Transfer, "manual:" + Find.TickManager.TicksGame + ":" + transferFrom + ":" + transferTo, TreasuryService.Today(), "player transfer");
                if (res != LedgerResult.Ok) Messages.Message("WFL_Tr_TransferFailed".Translate(res), MessageTypeDefOf.RejectInput, false);
            }
            return y + 32f;
        }

        private static float Payroll(Rect r, FamilyComponent c, float y)
        {
            var reg = c.Registry; var pay = reg.Payroll(); var pol = pay.Policy;
            y = Line(r, y, "WFL_Tr_PayrollHelp".Translate(), true) + 4f;
            y = Heading(r, y + 4f, "WFL_Tr_FundingTitle".Translate());
            foreach (PayrollFunding f in Enum.GetValues(typeof(PayrollFunding))) { if (Widgets.RadioButtonLabeled(new Rect(4f, y, r.width - 16f, 24f), ("WFL_Tr_Funding_" + f).Translate(), pol.Funding == f)) pol.Funding = f; y += 26f; }
            y = Line(r, y, "WFL_Tr_FundingNote".Translate(), true);
            var roles = CareerService.Professions.All.Where(x => FamilyFramework.Catalog.IsEnabled(x.ModuleId)).OrderBy(x => x.Career, StringComparer.Ordinal).ThenBy(x => x.Level).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
            if (pol.Funding == PayrollFunding.EssentialFirst)
                foreach (var role in roles) { bool ess = pol.Essential.Contains(role.Id); Widgets.CheckboxLabeled(new Rect(24f, y, 360f, 24f), "WFL_Tr_Essential".Translate() + ": " + CareerService.TitleOf(role, reg), ref ess); if (ess) pol.Essential.Add(role.Id); else pol.Essential.Remove(role.Id); y += 26f; }
            if (pol.Funding == PayrollFunding.Manual)
            {
                foreach (var role in roles) if (!pol.Order.Contains(role.Id)) pol.Order.Add(role.Id);
                for (int i = 0; i < pol.Order.Count; i++)
                {
                    var rl = roles.FirstOrDefault(x => x.Id == pol.Order[i]); Widgets.Label(new Rect(24f, y, 300f, 24f), (i + 1) + ". " + (rl == null ? pol.Order[i] : CareerService.TitleOf(rl, reg)));
                    if (i > 0 && Widgets.ButtonText(new Rect(330f, y, 30f, 22f), "▲")) { var t = pol.Order[i]; pol.Order[i] = pol.Order[i - 1]; pol.Order[i - 1] = t; }
                    if (i < pol.Order.Count - 1 && Widgets.ButtonText(new Rect(364f, y, 30f, 22f), "▼")) { var t = pol.Order[i]; pol.Order[i] = pol.Order[i + 1]; pol.Order[i + 1] = t; }
                    y += 26f;
                }
            }
            if (pay.WorkersMissing(2).Count > 0)
            {
                if (Widgets.ButtonText(new Rect(4f, y, 320f, 24f), "WFL_Tr_Renegotiate".Translate())) foreach (var k in pol.Rates.Keys.ToList()) pol.Rates[k] = Math.Max(1, pol.Rates[k] * 3 / 4);
                y = Line(r, y + 26f, "WFL_Tr_RenegotiateNote".Translate(), true);
            }
            foreach (var role in CareerService.Professions.All.Where(x => FamilyFramework.Catalog.IsEnabled(x.ModuleId)).OrderBy(x => x.Career, StringComparer.Ordinal).ThenBy(x => x.Level).ThenBy(x => x.Id, StringComparer.Ordinal))
                y = RateRow(r, y, role.Id, CareerService.TitleOf(role, reg));
            y = Heading(r, y + 6f, "WFL_Tr_VanillaWork".Translate());
            foreach (var cat in Labour.Categories) y = RateRow(r, y, Labour.Role(cat), cat, WagePolicy.OrdinaryRate);
            int ocap = pol.OrdinaryDailyCap; string obuf = ocap.ToString();
            Widgets.Label(new Rect(4f, y, 300f, 24f), "WFL_Tr_OrdinaryCap".Translate()); Widgets.TextFieldNumeric(new Rect(310f, y, 70f, 24f), ref ocap, ref obuf, 0f, 100000f); pol.OrdinaryDailyCap = ocap; y += 28f;
            int cap = pol.PawnDailyCap; string buf = cap.ToString();
            Widgets.Label(new Rect(4f, y, 300f, 24f), "WFL_Tr_DailyCap".Translate()); Widgets.TextFieldNumeric(new Rect(310f, y, 70f, 24f), ref cap, ref buf, 0f, 100000f); pol.PawnDailyCap = cap; y += 32f;
            y = Heading(r, y, "WFL_Tr_PastPayments".Translate());
            foreach (var rep in pay.Reports.AsEnumerable().Reverse()) y = Line(r, y, "WFL_Tr_PaydayLine".Translate(rep.Payday % PayCalendar.DaysPerYear + 1, rep.TotalDue, rep.TotalPaid, rep.Shortfall > 0 ? (string)"WFL_Tr_ShortBy".Translate(rep.Shortfall) : ""), true);
            return y;
        }

        private static readonly Dictionary<string, string> rateBuffers = new Dictionary<string, string>();
        private static float RateRow(Rect r, float y, string role, string label, int fallback = -1)
        {
            var pol = FamilyComponent.Current.Registry.Payroll().Policy;
            int rate = pol.Rates.TryGetValue(role, out var v) ? v : (fallback >= 0 ? fallback : WagePolicy.DefaultRate);
            if (!rateBuffers.TryGetValue(role, out var buf)) buf = rate.ToString();
            Widgets.Label(new Rect(4f, y, 300f, 24f), "WFL_Tr_RatePerUnit".Translate(label));
            Widgets.TextFieldNumeric(new Rect(310f, y, 70f, 24f), ref rate, ref buf, 0f, 100000f); rateBuffers[role] = buf; pol.Rates[role] = rate;
            return y + 26f;
        }

        private static float Taxation(Rect r, FamilyComponent c, float y)
        {
            var reg = c.Registry; var l = reg.Ledger(); var tax = reg.Tax(); int today = TreasuryService.Today();
            if (!TaxAuthority.Unlocked(TreasuryService.ActiveCollectors(reg))) return Line(r, y, "WFL_Tr_TaxLocked".Translate(), true);
            y = Line(r, y, "WFL_Tr_Collector".Translate(Names(c, TreasuryRoles.TaxCollector)));
            y = Line(r, y, "WFL_Tr_TaxRate".Translate(tax.RatePercent));
            float v = Widgets.HorizontalSlider(new Rect(4f, y, 240f, 20f), tax.RatePercent, 0f, 40f, true, null, null, null, 1f); tax.RatePercent = (int)v; y += 26f;
            bool auto = tax.AutoRemit; Widgets.CheckboxLabeled(new Rect(4f, y, 360f, 24f), "Collect automatically without visits", ref auto); tax.AutoRemit = auto; y += 28f;
            y = Line(r, y, "WFL_Tr_Legitimacy".Translate(tax.Legitimacy));
            y = Heading(r, y + 6f, "WFL_Tr_Taxation".Translate());
            foreach (var d in l.Debts.Where(x => x.Active && x.Kind == DebtKind.Tax).OrderBy(x => x.DueDay)) y = Line(r, y, "WFL_Tr_TaxLine".Translate(NameOf(c, d.Debtor), d.Outstanding, d.DueDay % PayCalendar.DaysPerYear + 1) + (d.DueDay < today ? "  !" : ""));
            return y;
        }

        private static float Debts(Rect r, FamilyComponent c, float y)
        {
            var l = c.Registry.Ledger();
            foreach (var d in l.Debts.Where(x => x.Active).OrderBy(x => x.DueDay).ToList())
            {
                y = Line(r, y, "WFL_Tr_DebtLine".Translate(NameOf(c, d.Debtor), NameOf(c, d.Creditor), d.Outstanding, d.Original, d.Kind + (d.Status == DebtStatus.Disputed ? ", disputed" : "")));
                y = CommunityPageLayout.Action(r, y, "WFL_Tr_Forgive".Translate(), () => l.Forgive(d.Id));
                y = CommunityPageLayout.Action(r, y, (d.Status == DebtStatus.Disputed ? "WFL_Tr_Resolve" : "WFL_Tr_Dispute").Translate(), () => l.Dispute(d.Id, d.Status != DebtStatus.Disputed));
            }
            return y;
        }

        private static float Transactions(Rect r, FamilyComponent c, float y)
        {
            foreach (var t in c.Registry.Ledger().History.Reverse().Take(80)) y = Line(r, y, TxText(c, t) + (t.Tag != null ? "  [" + t.Tag + "]" : ""), t.Kind != TxKind.Misconduct);
            return y;
        }
    }
}
