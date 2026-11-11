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
    public enum PayrollFunding : byte { Proportional, EssentialFirst, Manual }

    public sealed class WagePolicy
    {
        public const int DefaultRate = 3, DefaultDailyUnits = 2, DefaultPawnDailyCap = 10, OrdinaryRate = 1, OrdinaryDailyUnits = 1, DefaultOrdinaryDailyCap = 1;
        /// <summary>How a payroll shortfall is shared. Chosen by the player; the default shares the money that exists equally.</summary>
        public PayrollFunding Funding = PayrollFunding.Proportional;
        /// <summary>Roles paid first under <see cref="PayrollFunding.EssentialFirst"/>.</summary>
        public readonly HashSet<string> Essential = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>Roles in the order they are paid under <see cref="PayrollFunding.Manual"/>; roles not listed come last.</summary>
        public readonly List<string> Order = new List<string>();
        /// <summary>The payment tier of a role: 0 is paid first. Everything is one tier under proportional funding.</summary>
        public int TierOf(string role)
        {
            if (Funding == PayrollFunding.EssentialFirst) return role != null && Essential.Contains(role) ? 0 : 1;
            if (Funding == PayrollFunding.Manual) { int i = Order.IndexOf(role ?? ""); return i >= 0 ? i : Order.Count; }
            return 0;
        }
        public readonly Dictionary<string, int> Rates = new Dictionary<string, int>(StringComparer.Ordinal);
        public readonly Dictionary<string, int> DailyUnits = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Silver paid per unit of finished work in this role (0 = unpaid).</summary>
        /// <summary>Default silver per unit for an appointed profession by its level in the career ladder: entry 2, mid 3, specialist 4. Set when a role first earns, so the player can then change it.</summary>
        public static int DefaultRateForLevel(int level) => Math.Max(1, Math.Min(6, 1 + level));
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

        /// <summary>
        /// Settles one payday. Everything earned before it, plus wages still owed from earlier shortfalls, is what is due. Wages are claims that exist whether or not the treasury can pay: payment is
        /// limited to the spendable (backed) silver in the community treasury, and whatever is not paid stays owed to the worker as a wage debt, however large it grows. How a shortfall is shared is
        /// the player's <see cref="PayrollFunding"/> policy: the same fraction for everyone, essential roles first, or a manual priority order; within one tier everyone gets the same fraction.
        /// Payroll only ever moves silver out of the community treasury: household and personal accounts are never touched to cover it.
        /// </summary>
        private PayrollReport Settle(Ledger ledger, int payday, int funds)
        {
            var report = new PayrollReport { Payday = payday };
            string key = "payroll:" + payday;
            var due = earned.Where(e => e.Day < payday).ToList();
            var dues = new Dictionary<(int pawn, int tier), int>(); var ordinaryOnly = new Dictionary<(int pawn, int tier), bool>();
            foreach (var e in due) { var k = (e.Pawn, Policy.TierOf(e.Role)); dues[k] = (dues.TryGetValue(k, out var v) ? v : 0) + e.Silver; ordinaryOnly[k] = (!ordinaryOnly.TryGetValue(k, out var o) || o) && Labour.IsOrdinary(e.Role); }
            foreach (var d in ledger.Debts.Where(x => x.Active && x.Kind == DebtKind.Wage && x.Debtor == Ledger.CommunityId)) { int p = AccountPawn(d.Creditor); if (p > 0) { var k = (p, d.Tier); dues[k] = (dues.TryGetValue(k, out var v) ? v : 0) + d.Outstanding; if (!ordinaryOnly.ContainsKey(k)) ordinaryOnly[k] = d.Ordinary; } }
            if (dues.Count == 0 || ledger.Applied(key)) { RemoveSettled(due); Remember(report); return report; }

            int remaining = Math.Max(0, Math.Min(funds, ledger.Available(Ledger.CommunityId)));
            var pay = new Dictionary<(int pawn, int tier), int>();
            foreach (var tier in dues.Keys.Select(k => k.tier).Distinct().OrderBy(t => t))
            {
                var members = dues.Where(kv => kv.Key.tier == tier).ToList(); long total = members.Sum(kv => (long)kv.Value);
                int pool = (int)Math.Min(total, remaining); Share(members, total, pool, pay); remaining -= pool;
            }

            var moves = new List<Move>();
            foreach (var g in pay.GroupBy(kv => kv.Key.pawn).OrderBy(g => g.Key)) { int amt = g.Sum(kv => kv.Value); if (amt > 0) { ledger.Ensure(AccountKind.Person, g.Key); moves.Add(new Move(Ledger.CommunityId, Payee(ledger, g.Key), amt, "wages")); } }
            if (moves.Count > 0 && ledger.Apply(key, payday, TxKind.Payroll, moves) != LedgerResult.Ok) return report;          // nothing moved, nothing marked settled: it is tried again, never doubled

            foreach (var kv in dues)
            {
                int paid = pay.TryGetValue(kv.Key, out var pp) ? pp : 0, unpaid = kv.Value - paid;
                string acct = Ledger.IdFor(AccountKind.Person, kv.Key.pawn);
                var existing = ledger.Debts.Where(x => x.Active && x.Kind == DebtKind.Wage && x.Debtor == Ledger.CommunityId && x.Creditor == acct && x.Tier == kv.Key.tier).OrderBy(x => x.Id).ToList();
                int newlyEarned = due.Where(e => e.Pawn == kv.Key.pawn && Policy.TierOf(e.Role) == kv.Key.tier).Sum(e => e.Silver);
                if (existing.Count > 0) { ledger.Increase(existing[0], newlyEarned); int left = paid; foreach (var dd in existing) { int part = Math.Min(left, dd.Outstanding); if (part > 0) { ledger.RecordPayment(dd, part, payday); left -= part; } } }          // a worker with an inherited wage debt of the same kind has several: the payment is applied to each in turn
                else if (unpaid > 0) { ledger.Ensure(AccountKind.Person, kv.Key.pawn); var nd = ledger.OpenDebt(Ledger.CommunityId, acct, DebtKind.Wage, kv.Value, payday, "payroll " + payday); if (nd != null) { nd.Ordinary = ordinaryOnly[kv.Key]; nd.Tier = kv.Key.tier; ledger.RecordPayment(nd, paid, payday); } }
            }
            foreach (var g in dues.GroupBy(kv => kv.Key.pawn).OrderBy(g => g.Key))
            {
                int d = g.Sum(kv => kv.Value), p = g.Sum(kv => pay.TryGetValue(kv.Key, out var pp) ? pp : 0);
                report.Lines.Add(new PayLine { Pawn = g.Key, Due = d, Paid = p });
                if (p < d) missed[g.Key] = Missed(g.Key) + 1; else missed.Remove(g.Key);
            }
            report.TotalDue = (int)dues.Values.Sum(v => (long)v); report.TotalPaid = pay.Values.Sum();
            RemoveSettled(due); Remember(report);
            return report;
        }

        /// <summary>Consecutive paydays on which this worker was paid less than they were owed. The game layer turns this into consequences; here it is only a count.</summary>
        public int Missed(int pawn) => missed.TryGetValue(pawn, out var n) ? n : 0;
        private readonly Dictionary<int, int> missed = new Dictionary<int, int>();
        /// <summary>Workers who have gone unpaid (in part or in full) for at least this many paydays in a row.</summary>
        public IList<int> WorkersMissing(int paydays) => missed.Where(kv => kv.Value >= paydays).Select(kv => kv.Key).OrderBy(i => i).ToList();

        /// <summary>Shares a pool among a tier pro rata (largest remainder, never more than a worker is due).</summary>
        private static void Share(List<KeyValuePair<(int pawn, int tier), int>> members, long total, int pool, Dictionary<(int pawn, int tier), int> into)
        {
            if (total <= 0 || pool <= 0) return;
            if (pool >= total) { foreach (var kv in members) into[kv.Key] = kv.Value; return; }
            var order = members.OrderBy(kv => kv.Key.pawn).ToList();
            foreach (var kv in order) into[kv.Key] = (int)((long)pool * kv.Value / total);
            int left = pool - order.Sum(kv => into[kv.Key]);
            foreach (var kv in order.OrderByDescending(kv => (long)pool * kv.Value - (long)into[kv.Key] * total).ThenBy(kv => kv.Key.pawn).Take(left)) into[kv.Key] = Math.Min(kv.Value, into[kv.Key] + 1);
        }

        private static int AccountPawn(string id) => id != null && id.StartsWith("p:", StringComparison.Ordinal) && int.TryParse(id.Substring(2), out var n) ? n : 0;
        /// <summary>Wages go to the worker's account; if that account was closed (the worker died and the estate was settled) they go to the account that inherited it, so one closed account never blocks the whole payroll.</summary>
        private static string Payee(Ledger ledger, int pawn)
        {
            string id = Ledger.IdFor(AccountKind.Person, pawn); var a = ledger.Get(id);
            for (int i = 0; i < 4 && a != null && a.Closed && !string.IsNullOrEmpty(a.Successor); i++) { id = a.Successor; a = ledger.Get(id); }
            return a == null || a.Closed ? Ledger.CommunityId : id;
        }

        private void RemoveSettled(List<Earning> settled) { foreach (var e in settled) earned.Remove(e); }
        private void Remember(PayrollReport r) { Reports.Add(r); while (Reports.Count > MaxReports) Reports.RemoveAt(0); }

        public string Encode()
        {
            var w = new StateWriter().Int("last", LastSettledPayday + 1).Int("cap", Policy.PawnDailyCap).Int("ocap", Policy.OrdinaryDailyCap + 1).Int("fund", (int)Policy.Funding);
            w.Strings("ess", Policy.Essential.OrderBy(x => x, StringComparer.Ordinal).ToList()); w.Strings("ord", Policy.Order.ToList()); w.Strings("miss", missed.OrderBy(k => k.Key).Select(k => k.Key + "|" + k.Value).ToList());
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
            b.Policy.Funding = r.Has("fund") ? (PayrollFunding)Math.Max(0, Math.Min(2, r.Int("fund"))) : PayrollFunding.Proportional;
            foreach (var s in r.Strings("ess")) b.Policy.Essential.Add(s); foreach (var s in r.Strings("ord")) b.Policy.Order.Add(s);
            foreach (var s in r.Strings("miss")) { var p = s.Split((char)124); if (p.Length == 2) b.missed[I(p[0])] = I(p[1]); }
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
