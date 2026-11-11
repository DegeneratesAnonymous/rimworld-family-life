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
        private static string transferFrom, transferTo; private static string amountText = "10"; private static int amount = 10;
        private static readonly string[] Tabs = { "WFL_Tr_Overview", "WFL_Tr_Accounts", "WFL_Tr_Payroll", "WFL_Tr_Taxation", "WFL_Tr_Debts", "WFL_Tr_Transactions" };

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
            float y = 0f; float x = 0f;
            for (int i = 0; i < Tabs.Length; i++)
            {
                var label = Tabs[i].Translate(); float w = Mathf.Max(90f, Text.CalcSize(label).x + 20f);
                var tab = new Rect(x, y, w, 26f);
                if (Widgets.ButtonText(tab, label, true, true, i == sub ? new Color(0.9f, 0.74f, 0.28f) : Color.white)) sub = i;
                x += w + 4f;
            }
            y += 34f;
            switch (sub)
            {
                case 0: return Overview(r, c, y);
                case 1: return Accounts(r, c, y);
                case 2: return Payroll(r, c, y);
                case 3: return Taxation(r, c, y);
                case 4: return Debts(r, c, y);
                default: return Transactions(r, c, y);
            }
        }

        private static string Names(FamilyComponent c, string role)
        {
            var list = TreasuryService.Holders(c.Registry, role).Select(id => NameOfPawn(c, id)).ToList();
            return list.Count == 0 ? "WFL_Tr_None".Translate().ToString() : string.Join(", ", list);
        }

        private static float Overview(Rect r, FamilyComponent c, float y)
        {
            var reg = c.Registry; var l = reg.Ledger(); var pay = reg.Payroll(); int today = TreasuryService.Today(); var map = Find.AnyPlayerHomeMap;
            y = Line(r, y, "WFL_Tr_CommunityFunds".Translate(l.Balance(Ledger.CommunityId)));
            int physical = TreasuryService.PhysicalSilver(map);
            y = Line(r, y, "WFL_Tr_Claims".Translate(l.TotalClaims, physical));
            if (l.TotalClaims != physical) y = Line(r, y, "WFL_Tr_Discrepancy".Translate(Math.Abs(l.TotalClaims - physical)), true);
            var personal = l.Accounts.Where(a => a.Kind != AccountKind.Community && !a.Closed).ToList();
            y = Line(r, y, "WFL_Tr_Wealth".Translate(personal.Sum(a => a.Balance), personal.Count));
            var zone = map?.zoneManager.AllZones.OfType<Zone_Treasury>().OrderBy(z => z.ID).FirstOrDefault();
            y = Line(r, y, "WFL_Tr_ZoneStatus".Translate(zone == null ? "WFL_Tr_NoZone".Translate().ToString() : zone.GetInspectString().Replace('\n', ' ')));
            y = Line(r, y, "WFL_Tr_Treasurer".Translate(Names(c, TreasuryRoles.Treasurer)));
            y = Line(r, y, "WFL_Tr_Collector".Translate(Names(c, TreasuryRoles.TaxCollector)));
            int days = PayCalendar.NextPayday(today + 1) - today;
            y = Line(r, y, "WFL_Tr_NextPayday".Translate(days, pay.PendingTotal));
            var open = l.Debts.Where(d => d.Active).ToList();
            y = Line(r, y, "WFL_Tr_DebtsTotal".Translate(open.Sum(d => d.Outstanding), open.Count));
            y = Line(r, y, "WFL_Tr_IncomeShare".Translate(reg.Treasury().CommunityPercent));
            var st = reg.Treasury(); int pct = st.CommunityPercent;
            float v = Widgets.HorizontalSlider(new Rect(4f, y, 240f, 20f), pct, 0f, 100f, true, null, null, null, 5f); if ((int)v != pct) st.CommunityPercent = (int)v;
            y += 26f;
            y = Heading(r, y + 6f, "WFL_Tr_Transactions".Translate());
            foreach (var t in l.History.Reverse().Take(6)) y = Line(r, y, TxText(c, t), true);
            return y;
        }

        private static string TxText(FamilyComponent c, Transaction t) => "WFL_Tr_TxLine".Translate(t.Day % PayCalendar.DaysPerYear + 1, t.Kind, t.Amount, t.From == null ? "WFL_Tr_Outside".Translate().ToString() : NameOf(c, t.From), t.To == null ? "WFL_Tr_Outside".Translate().ToString() : NameOf(c, t.To));

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
                if (Widgets.ButtonText(new Rect(r.width - 250f, y - 24f, 80f, 22f), "WFL_Tr_Forgive".Translate())) l.Forgive(d.Id);
                if (Widgets.ButtonText(new Rect(r.width - 166f, y - 24f, 80f, 22f), (d.Status == DebtStatus.Disputed ? "WFL_Tr_Resolve" : "WFL_Tr_Dispute").Translate())) l.Dispute(d.Id, d.Status != DebtStatus.Disputed);
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
