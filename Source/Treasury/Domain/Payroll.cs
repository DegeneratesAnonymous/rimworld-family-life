using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    /// <summary>
    /// RimWorld's calendar as the payroll needs it. A quadrum is 15 days and a year 4 quadrums (60 days); "absolute day" counts days from the start of year 0. Paydays are the 1st and 8th of
    /// every quadrum. The 8th pays days 1-7 and the 1st pays days 8-15 of the quadrum before: work done on a day is paid on the first payday after that day.
    /// </summary>
    public static class PayCalendar
    {
        public const int DaysPerQuadrum = 15, QuadrumsPerYear = 4, DaysPerYear = 60;
        public static int DayOfQuadrum(int absDay) => absDay % DaysPerQuadrum + 1;          // 1-15
        public static int Quadrum(int absDay) => absDay / DaysPerQuadrum % QuadrumsPerYear;       // 0-3
        public static int Year(int absDay) => absDay / DaysPerYear;
        public static bool IsPayday(int absDay) { int d = DayOfQuadrum(absDay); return d == 1 || d == 8; }
        /// <summary>The first payday strictly after a day of work (the day's own payday, if it is one, pays the days before it).</summary>
        public static int PaydayFor(int workDay) { int d = workDay + 1; while (!IsPayday(d)) d++; return d; }
        public static int NextPayday(int fromDay) { int d = fromDay; while (!IsPayday(d)) d++; return d; }
        public static IEnumerable<int> PaydaysBetween(int afterDay, int throughDay) { for (int d = afterDay + 1; d <= throughDay; d++) if (IsPayday(d)) yield return d; }
    }

    /// <summary>What each role pays and how much one pawn can be paid in a day. Roles are named by the career registry, never listed here, so a new profession needs no change.</summary>
    public sealed class WagePolicy
    {
        public const int DefaultRate = 4, DefaultDailyUnits = 4, DefaultPawnDailyCap = 24, OrdinaryRate = 1, OrdinaryDailyUnits = 3, DefaultOrdinaryDailyCap = 8;
        public readonly Dictionary<string, int> Rates = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> DailyUnits = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Silver paid per unit of finished work in this role (0 = unpaid).</summary>
        public int RateOf(string role) => Rates.TryGetValue(role, out var r) ? Math.Max(0, r) : Labour.IsOrdinary(role) ? OrdinaryRate : DefaultRate;
        /// <summary>Most units of one role's work that count in a single day, so quick repeatable jobs cannot be farmed.</summary>
        public int UnitsPerDay(string role) => DailyUnits.TryGetValue(role, out var u) ? Math.Max(0, u) : Labour.IsOrdinary(role) ? OrdinaryDailyUnits : DefaultDailyUnits;
        public int PawnDailyCap = DefaultPawnDailyCap, OrdinaryDailyCap = DefaultOrdinaryDailyCap;          // one pawn over all roles, and the part of it that ordinary labour may fill
        /// <summary>Vanilla labour (farming, cooking...) is only paid if the player turns a rate on for it; the key is "vanilla.&lt;worktype&gt;".</summary>
        public static string VanillaRole(string category) => Labour.Role(category.ToLowerInvariant());
    }

    public sealed class Earning { public int Pawn, Day, Silver; public string Role, Key; }

    public sealed class PayLine { public int Pawn, Due, Paid; }
    public sealed class PayrollReport { public int Payday, TotalDue, TotalPaid; public List<PayLine> Lines = new List<PayLine>(); public int Shortfall => TotalDue - TotalPaid; }

    /// <summary>Earned wages, kept apart from settled wages. Persisted with the wage policy and the last settled payday.</summary>
    public sealed class PayrollBook : IModuleState
    {
        public const string StateId = "wils.treasury.payroll";
        public const int MaxReports = 8, KeyMemoryDays = 40;
        public readonly WagePolicy Policy = new WagePolicy();
        private readonly List<Earning> earned = new List<Earning>();
        private readonly Dictionary<string, int> keys = new Dictionary<string, int>();
        public int LastSettledPayday = -1;
        public readonly List<PayrollReport> Reports = new List<PayrollReport>();
        public IReadOnlyList<Earning> Earned => earned;

        /// <summary>
        /// Records finished work. Returns the silver it earned (0 if the key was already counted, the role is unpaid, or a daily limit is reached). The same work key never pays twice; a role's
        /// daily units and the pawn's daily total are capped, so one pawn holding several qualifications is not paid for the same hour more than once.
        /// </summary>
        public int Accrue(int pawn, string role, int units, int day, string key)
        {
            if (pawn <= 0 || string.IsNullOrEmpty(role) || units <= 0 || (key != null && keys.ContainsKey(key))) return 0;
            int rate = Policy.RateOf(role); if (rate <= 0) return 0;
            int usedUnits = earned.Where(e => e.Pawn == pawn && e.Day == day && e.Role == role).Sum(e => e.Silver) / rate;
            int allow = Math.Min(units, Policy.UnitsPerDay(role) - usedUnits); if (allow <= 0) return 0;
            int silver = allow * rate, paidToday = earned.Where(e => e.Pawn == pawn && e.Day == day).Sum(e => e.Silver);
            if (Labour.IsOrdinary(role)) silver = Math.Min(silver, Policy.OrdinaryDailyCap - earned.Where(e => e.Pawn == pawn && e.Day == day && Labour.IsOrdinary(e.Role)).Sum(e => e.Silver));
            silver = Math.Min(silver, Policy.PawnDailyCap - paidToday); if (silver <= 0) return 0;
            earned.Add(new Earning { Pawn = pawn, Role = role, Silver = silver, Day = day, Key = key });
            if (key != null) keys[key] = day;
            return silver;
        }

        public int PendingFor(int pawn) => earned.Where(e => e.Pawn == pawn).Sum(e => e.Silver);
        public int PendingTotal => earned.Sum(e => e.Silver);

        /// <summary>
        /// Settles every payday up to today that has not been settled, oldest first, each at most once. Earnings from before a payday are finalised; the total (plus any wages still owed from
        /// earlier shortfalls) is checked against <paramref name="funds"/> and the treasury's balance; if the money is short everyone is paid the same fraction, and the rest stays owed as a wage
        /// debt. Money only moves from the treasury to the workers: payroll never creates silver.
        /// </summary>
        public List<PayrollReport> SettleDue(Ledger ledger, int today, int funds)
        {
            var done = new List<PayrollReport>();
            if (LastSettledPayday < 0) LastSettledPayday = today - 1;          // a new payroll starts from now, with no back pay for days it did not watch
            int guard = 0;
            foreach (int payday in PayCalendar.PaydaysBetween(LastSettledPayday, today).ToList())
            {
                if (++guard > 16) break;
                done.Add(Settle(ledger, payday, funds));
                LastSettledPayday = payday;
            }
            foreach (var k in keys.Where(kv => today - kv.Value > KeyMemoryDays).Select(kv => kv.Key).ToList()) keys.Remove(k);
            return done;
        }

        private PayrollReport Settle(Ledger ledger, int payday, int funds)
        {
            var report = new PayrollReport { Payday = payday };
            string key = "payroll:" + payday;
            var due = earned.Where(e => e.Day < payday).ToList();
            var perPawn = due.GroupBy(e => e.Pawn).ToDictionary(g => g.Key, g => g.Sum(e => e.Silver));
            foreach (var d in ledger.Debts.Where(x => x.Active && x.Kind == DebtKind.Wage && x.Debtor == Ledger.CommunityId)) { int p = AccountPawn(d.Creditor); if (p > 0) perPawn[p] = (perPawn.TryGetValue(p, out var v) ? v : 0) + d.Outstanding; }
            if (perPawn.Count == 0 || ledger.Applied(key)) { RemoveSettled(due); Remember(report); return report; }
            long total = perPawn.Values.Sum(v => (long)v);
            int available = Math.Max(0, Math.Min(funds, ledger.Balance(Ledger.CommunityId)));
            var pay = new Dictionary<int, int>();
            if (total <= available) foreach (var kv in perPawn) pay[kv.Key] = kv.Value;
            else
            {
                var order = perPawn.OrderBy(kv => kv.Key).ToList();
                foreach (var kv in order) pay[kv.Key] = (int)((long)available * kv.Value / total);
                int left = available - pay.Values.Sum();
                foreach (var kv in order.OrderByDescending(kv => (long)available * kv.Value - (long)pay[kv.Key] * total).ThenBy(kv => kv.Key).Take(left)) pay[kv.Key]++;
            }
            var moves = new List<Move>();
            foreach (var kv in pay.OrderBy(k => k.Key)) { if (kv.Value > 0) { ledger.Ensure(AccountKind.Person, kv.Key); moves.Add(new Move(Ledger.CommunityId, Ledger.IdFor(AccountKind.Person, kv.Key), kv.Value, "wages")); } }
            if (moves.Count > 0 && ledger.Apply(key, payday, TxKind.Payroll, moves) != LedgerResult.Ok) { return report; }          // nothing moved, nothing marked settled: it is tried again, never doubled
            // Wages still owed carry over as one wage debt per person: what they were due, less what they were paid.
            foreach (var kv in perPawn)
            {
                int paid = pay.TryGetValue(kv.Key, out var pp) ? pp : 0, unpaid = kv.Value - paid;
                string acct = Ledger.IdFor(AccountKind.Person, kv.Key);
                var debt = ledger.Debts.FirstOrDefault(x => x.Active && x.Kind == DebtKind.Wage && x.Debtor == Ledger.CommunityId && x.Creditor == acct);
                int newlyEarned = due.Where(e => e.Pawn == kv.Key).Sum(e => e.Silver);
                if (debt != null) { ledger.Increase(debt, newlyEarned); ledger.RecordPayment(debt, paid, payday); }
                else if (unpaid > 0) { ledger.Ensure(AccountKind.Person, kv.Key); var nd = ledger.OpenDebt(Ledger.CommunityId, acct, DebtKind.Wage, kv.Value, payday, "payroll " + payday); if (nd != null) ledger.RecordPayment(nd, paid, payday); }
                report.Lines.Add(new PayLine { Pawn = kv.Key, Due = kv.Value, Paid = paid });
            }
            report.TotalDue = (int)total; report.TotalPaid = pay.Values.Sum();
            RemoveSettled(due); Remember(report);
            return report;
        }

        private static int AccountPawn(string id) => id != null && id.StartsWith("p:", StringComparison.Ordinal) && int.TryParse(id.Substring(2), out var n) ? n : 0;
        private void RemoveSettled(List<Earning> settled) { foreach (var e in settled) earned.Remove(e); }
        private void Remember(PayrollReport r) { Reports.Add(r); while (Reports.Count > MaxReports) Reports.RemoveAt(0); }

        public string Encode()
        {
            var w = new StateWriter().Int("last", LastSettledPayday + 1).Int("cap", Policy.PawnDailyCap).Int("ocap", Policy.OrdinaryDailyCap + 1);
            w.Strings("rate", Policy.Rates.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "|" + k.Value).ToList());
            w.Strings("units", Policy.DailyUnits.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "|" + k.Value).ToList());
            w.Strings("earn", earned.Select(e => string.Join("|", e.Pawn, e.Day, e.Silver, e.Role, (e.Key ?? "").Replace('|', '/'))).ToList());
            w.Strings("key", keys.Select(k => k.Key.Replace('|', '/') + "|" + k.Value).ToList());
            w.Strings("rep", Reports.Select(r => string.Join("|", r.Payday, r.TotalDue, r.TotalPaid, string.Join(";", r.Lines.Select(l => l.Pawn + ":" + l.Due + ":" + l.Paid)))).ToList());
            return w.ToString();
        }

        public static PayrollBook Decode(StateReader r)
        {
            var b = new PayrollBook(); b.LastSettledPayday = r.Int("last") - 1; b.Policy.PawnDailyCap = r.Has("cap") ? r.Int("cap") : WagePolicy.DefaultPawnDailyCap; b.Policy.OrdinaryDailyCap = r.Has("ocap") ? r.Int("ocap") - 1 : WagePolicy.DefaultOrdinaryDailyCap;
            foreach (var s in r.Strings("rate")) { var p = s.Split('|'); if (p.Length == 2) b.Policy.Rates[p[0]] = I(p[1]); }
            foreach (var s in r.Strings("units")) { var p = s.Split('|'); if (p.Length == 2) b.Policy.DailyUnits[p[0]] = I(p[1]); }
            foreach (var s in r.Strings("earn")) { var p = s.Split('|'); if (p.Length >= 5) b.earned.Add(new Earning { Pawn = I(p[0]), Day = I(p[1]), Silver = I(p[2]), Role = p[3], Key = p[4].Length == 0 ? null : p[4] }); }
            foreach (var s in r.Strings("key")) { var p = s.Split('|'); if (p.Length == 2) b.keys[p[0]] = I(p[1]); }
            foreach (var s in r.Strings("rep"))
            {
                var p = s.Split('|'); if (p.Length < 4) continue;
                var rep = new PayrollReport { Payday = I(p[0]), TotalDue = I(p[1]), TotalPaid = I(p[2]) };
                foreach (var l in p[3].Split(';')) { var q = l.Split(':'); if (q.Length == 3) rep.Lines.Add(new PayLine { Pawn = I(q[0]), Due = I(q[1]), Paid = I(q[2]) }); }
                b.Reports.Add(rep);
            }
            return b;
        }
        private static int I(string s) => int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
    }

    public sealed class PayrollFactory : IStateFactory<PayrollBook>
    {
        public static readonly PayrollFactory Instance = new PayrollFactory();
        public int Version => 1;
        public PayrollBook Create() => new PayrollBook();
        public PayrollBook Decode(StateReader reader, int version) => PayrollBook.Decode(reader);
        public PayrollBook FromLegacy(StateReader legacy) => new PayrollBook();
    }

    public static class PayrollAccess { public static PayrollBook Payroll(this FamilyRegistry r) => r.Shared.Of(PayrollBook.StateId, PayrollFactory.Instance); }
}
