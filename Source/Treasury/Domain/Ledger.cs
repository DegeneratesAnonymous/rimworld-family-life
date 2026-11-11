using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    public enum AccountKind : byte { Community, Person, Household, Business }
    public enum TxKind : byte { Income, Spend, Transfer, Reward, Payroll, Tax, Trade, Debt, Estate, Correction, Misconduct }
    public enum DebtKind : byte { Tax, Wage, Loan, Restitution, Damage, Commission, Wedding, Funeral, Inheritance, Separation, Emergency }
    public enum DebtStatus : byte { Open, Paid, Forgiven, Disputed, WrittenOff }

    /// <summary>A claim on the community's silver. Accounts are ownership claims against real silver, never additional currency.</summary>
    public sealed class Account
    {
        public string Id;
        public AccountKind Kind;
        public int Owner;          // pawn id, lineage id or business id; 0 for the community
        public int Balance;
        public bool Closed;
        public string Successor;          // where a closed account's claims went
    }

    public sealed class Transaction
    {
        public int Id, Day, Amount;
        public TxKind Kind;
        public string From, To, Key, Memo, Tag;          // From/To are null for silver entering or leaving the community from outside
    }

    /// <summary>One leg of a batch. A null side is outside the ledger (income arriving or spending leaving).</summary>
    public struct Move
    {
        public string From, To, Memo; public int Amount;
        public Move(string from, string to, int amount, string memo = null) { From = from; To = to; Amount = amount; Memo = memo; }
    }

    public sealed class Debt
    {
        public int Id, Original, Outstanding, DueDay, Installment, EveryDays, FamilyRef, NextDueDay, Waived;          // Waived: part given up by decision, never paid
        public bool Ordinary;          // a wage debt for ordinary labour (paid after professional wages when money is short)
        public string Debtor, Creditor, Origin;
        public DebtKind Kind;
        public DebtStatus Status;
        public List<string> Payments = new List<string>();          // "day:amount", most recent last, bounded
        public bool Active => Status == DebtStatus.Open || Status == DebtStatus.Disputed;
    }

    public enum LedgerResult : byte { Ok, Duplicate, Insufficient, UnknownAccount, Invalid, Closed }

    /// <summary>
    /// The financial ledger, free of any game type. Every change is a batch of legs that is checked first and applied whole or not at all; a batch carries an idempotency key, so replaying it
    /// (a reload, a retried hand-over) changes nothing. Value is conserved: the sum of every balance always equals silver brought in minus silver spent (<see cref="Conserved"/>); a transfer
    /// can never make an account negative, so no operation can create spendable silver. Liabilities (<see cref="Debt"/>) are kept apart from balances.
    /// </summary>
    public sealed class Ledger : IModuleState
    {
        public const string StateId = "wils.treasury.ledger";
        public const string CommunityId = "c:1";
        public const int MaxHistory = 600;
        private const int KeyMemoryDays = 200;

        private readonly Dictionary<string, Account> accounts = new Dictionary<string, Account>();
        private readonly List<Transaction> history = new List<Transaction>();
        private readonly Dictionary<string, int> keys = new Dictionary<string, int>();          // key -> day applied
        private readonly List<Debt> debts = new List<Debt>();
        public int NextTxId = 1, NextDebtId = 1;
        public long ExternalIn, ExternalOut;

        public IEnumerable<Account> Accounts => accounts.Values;
        public IReadOnlyList<Transaction> History => history;
        public IReadOnlyList<Debt> Debts => debts;

        public Ledger() { Ensure(AccountKind.Community, 0); }

        public static string IdFor(AccountKind kind, int owner) => kind == AccountKind.Community ? CommunityId : (kind == AccountKind.Person ? "p:" : kind == AccountKind.Household ? "h:" : "b:") + owner;
        public Account Get(string id) => id != null && accounts.TryGetValue(id, out var a) ? a : null;
        public int Balance(string id) => Get(id)?.Balance ?? 0;
        public Account Ensure(AccountKind kind, int owner)
        {
            string id = IdFor(kind, owner);
            if (!accounts.TryGetValue(id, out var a)) accounts[id] = a = new Account { Id = id, Kind = kind, Owner = owner };
            return a;
        }

        /// <summary>The invariant that no operation may break: balances sum to what came in minus what went out.</summary>
        public bool Conserved => accounts.Values.Sum(a => (long)a.Balance) == ExternalIn - ExternalOut;
        public long TotalClaims => accounts.Values.Sum(a => (long)a.Balance);

        // ------------------------------------------------------------------ backing and liquidity
        // Five different things are kept apart: the silver that physically exists (told to the ledger by the game), who owns it (balances, never reduced by a loss), what may be spent now
        // (Available), what is owed (debts, never netted against balances) and the part of the claims that has no silver behind it (Unbacked). A theft never erases ownership, but it does take
        // spending power away from every account in proportion until the silver is found again or the owners accept the loss (WriteDown).

        /// <summary>Claims that currently have no physical silver behind them. Set by the reconciliation from the silver the colony really holds; zero means every claim is backed.</summary>
        public int Unbacked;

        /// <summary>Tells the ledger how much silver physically exists. Claims above it become <see cref="Unbacked"/>; nothing is credited or removed.</summary>
        public void SetPhysical(long physical) { Unbacked = (int)Math.Max(0, Math.Min(int.MaxValue, TotalClaims - Math.Max(0, physical))); }

        /// <summary>
        /// What an account can spend right now: its balance less its proportional share (rounded up, so the total can never exceed the silver that exists) of the unbacked claims. The balance itself
        /// is untouched, so the account regains its spending power if the silver comes back.
        /// </summary>
        public int Available(string id)
        {
            var a = Get(id); if (a == null || a.Closed) return 0;
            long claims = TotalClaims; if (Unbacked <= 0 || claims <= 0) return a.Balance;
            long impaired = ((long)a.Balance * Math.Min(Unbacked, claims) + claims - 1) / claims;
            return (int)Math.Max(0, a.Balance - impaired);
        }
        public long TotalAvailable => accounts.Values.Where(a => !a.Closed).Sum(a => (long)Available(a.Id));

        /// <summary>
        /// The owners accept that unbacked silver is gone: every account loses its proportional share (largest-remainder, exactly the unbacked total) as a recorded <see cref="TxKind.Correction"/>, after
        /// which the claims equal the silver that exists. Only ever called by an explicit decision, never automatically. Returns the amount written down.
        /// </summary>
        public int WriteDown(string key, int day)
        {
            if (Unbacked <= 0 || (key != null && keys.ContainsKey(key))) return 0;
            long claims = TotalClaims; int loss = (int)Math.Min(Unbacked, claims);
            var holders = accounts.Values.Where(a => !a.Closed && a.Balance > 0).OrderBy(a => a.Id, StringComparer.Ordinal).ToList();
            var share = holders.ToDictionary(a => a.Id, a => (int)((long)a.Balance * loss / claims));
            int left = loss - share.Values.Sum();
            foreach (var a in holders.OrderByDescending(a => (long)a.Balance * loss - (long)share[a.Id] * claims).ThenBy(a => a.Id, StringComparer.Ordinal).Take(left)) share[a.Id]++;
            var moves = holders.Where(a => share[a.Id] > 0).Select(a => new Move(a.Id, null, Math.Min(a.Balance, share[a.Id]), "accepted loss of unbacked silver")).ToList();
            if (moves.Count == 0 || Apply(key, day, TxKind.Correction, moves) != LedgerResult.Ok) return 0;
            int taken = moves.Sum(m => m.Amount); Unbacked = Math.Max(0, Unbacked - taken); return taken;
        }

        // Income received since an account was last assessed for tax (rewards, wages, trade proceeds). Kept here, not read back from the bounded history, so a busy colony cannot lose income to trimming.
        private readonly Dictionary<string, int> incomeSince = new Dictionary<string, int>();
        public int IncomeSince(string account) => incomeSince.TryGetValue(account, out var v) ? v : 0;
        public int TakeIncome(string account) { int v = IncomeSince(account); incomeSince.Remove(account); return v; }

        /// <summary>Applies the legs together or not at all. A leg with a null side is silver entering or leaving the community; both sides null, a non-positive amount, a closed or unknown account, or an account that would go negative refuses the whole batch.</summary>
        public LedgerResult Apply(string key, int day, TxKind kind, IList<Move> moves, string tag = null)
        {
            if (moves == null || moves.Count == 0) return LedgerResult.Invalid;
            if (key != null && keys.ContainsKey(key)) return LedgerResult.Duplicate;
            var net = new Dictionary<string, long>();
            foreach (var m in moves)
            {
                if (m.Amount <= 0 || (m.From == null && m.To == null) || m.From == m.To) return LedgerResult.Invalid;
                foreach (var id in new[] { m.From, m.To }) if (id != null) { var a = Get(id); if (a == null) return LedgerResult.UnknownAccount; if (a.Closed) return LedgerResult.Closed; }
                if (m.From != null) net[m.From] = (net.TryGetValue(m.From, out var f) ? f : 0) - m.Amount;
                if (m.To != null) net[m.To] = (net.TryGetValue(m.To, out var t) ? t : 0) + m.Amount;
            }
            foreach (var kv in net) if (Get(kv.Key).Balance + kv.Value < 0) return LedgerResult.Insufficient;
            foreach (var m in moves)
            {
                if (m.From != null) accounts[m.From].Balance -= m.Amount; else ExternalIn += m.Amount;
                if (m.To != null) accounts[m.To].Balance += m.Amount; else ExternalOut += m.Amount;
                if (m.To != null && (kind == TxKind.Reward || kind == TxKind.Payroll || kind == TxKind.Trade)) incomeSince[m.To] = IncomeSince(m.To) + m.Amount;
                history.Add(new Transaction { Id = NextTxId++, Day = day, Kind = kind, From = m.From, To = m.To, Amount = m.Amount, Key = key, Memo = m.Memo, Tag = tag });
            }
            if (key != null) keys[key] = day;
            while (history.Count > MaxHistory) history.RemoveAt(0);
            return LedgerResult.Ok;
        }

        public LedgerResult Transfer(string from, string to, int amount, TxKind kind, string key, int day, string memo = null) => Apply(key, day, kind, new[] { new Move(from, to, amount, memo) });
        /// <summary>Silver that really arrived (a reward, sale proceeds, a gift) credited to one account.</summary>
        public LedgerResult Receive(string to, int amount, TxKind kind, string key, int day, string memo = null) => Apply(key, day, kind, new[] { new Move(null, to, amount, memo) });
        /// <summary>Silver that really left (spent in a trade, destroyed).</summary>
        public LedgerResult Spend(string from, int amount, TxKind kind, string key, int day, string memo = null) => Apply(key, day, kind, new[] { new Move(from, null, amount, memo) });
        public bool Applied(string key) => key != null && keys.ContainsKey(key);

        /// <summary>Forgets idempotency keys older than the memory window so the save stays small. A period older than this can no longer be replayed by a stale caller, which is the intent.</summary>
        public void Prune(int today)
        {
            foreach (var k in keys.Where(kv => today - kv.Value > KeyMemoryDays).Select(kv => kv.Key).ToList()) keys.Remove(k);
        }

        // ------------------------------------------------------------------ obligations

        public Debt OpenDebt(string debtor, string creditor, DebtKind kind, int amount, int dueDay, string origin, int familyRef = 0, int installment = 0, int everyDays = 0)
        {
            if (amount <= 0 || Get(debtor) == null || Get(creditor) == null || debtor == creditor) return null;
            var d = new Debt { Id = NextDebtId++, Debtor = debtor, Creditor = creditor, Kind = kind, Original = amount, Outstanding = amount, DueDay = dueDay, Origin = origin ?? "", FamilyRef = familyRef, Installment = installment, EveryDays = everyDays, NextDueDay = dueDay, Status = DebtStatus.Open };
            debts.Add(d);
            return d;
        }

        public Debt DebtById(int id) => debts.FirstOrDefault(d => d.Id == id);
        public IEnumerable<Debt> DebtsOf(string account) => debts.Where(d => d.Active && (d.Debtor == account || d.Creditor == account));
        /// <summary>What an account owes (liabilities) and is owed. Never netted against the balance: an account with no silver is not in debt, and a debt is not a negative balance.</summary>
        public int Liabilities(string account) => debts.Where(d => d.Active && d.Debtor == account).Sum(d => d.Outstanding);
        public int Receivables(string account) => debts.Where(d => d.Active && d.Creditor == account).Sum(d => d.Outstanding);

        /// <summary>Pays up to <paramref name="amount"/> toward a debt from the debtor's actual balance. Returns what was paid (0 if nothing could be).</summary>
        public int Pay(int debtId, int amount, string key, int day)
        {
            var d = DebtById(debtId); if (d == null || !d.Active || amount <= 0) return 0;
            int pay = Math.Min(Math.Min(amount, d.Outstanding), Available(d.Debtor));          // only silver that is backed can pay a debt
            if (pay <= 0) return 0;
            if (Transfer(d.Debtor, d.Creditor, pay, d.Kind == DebtKind.Tax ? TxKind.Tax : d.Kind == DebtKind.Wage ? TxKind.Payroll : TxKind.Debt, key, day, "debt " + d.Id) != LedgerResult.Ok) return 0;
            d.Outstanding -= pay; d.Payments.Add(day + ":" + pay); if (d.Payments.Count > 12) d.Payments.RemoveAt(0);
            if (d.Outstanding == 0) d.Status = DebtStatus.Paid; else if (d.EveryDays > 0) d.NextDueDay = day + d.EveryDays;
            return pay;
        }

        /// <summary>Adds to what is owed (new unpaid wages, a further assessment). Never touches a balance.</summary>
        public void Increase(Debt d, int amount) { if (d == null || amount <= 0) return; d.Original += amount; d.Outstanding += amount; if (d.Status == DebtStatus.Paid) d.Status = DebtStatus.Open; }
        public void RecordPayment(Debt d, int paid, int day) { if (d == null || paid <= 0) return; d.Outstanding = Math.Max(0, d.Outstanding - paid); d.Payments.Add(day + ":" + paid); if (d.Payments.Count > 12) d.Payments.RemoveAt(0); if (d.Outstanding == 0 && d.Active) d.Status = DebtStatus.Paid; }

        /// <summary>Gives up part of what is owed by decision (a favour, a bribe, forgiveness). It is recorded as <see cref="Debt.Waived"/>, never as a payment, so a waived obligation can be told apart from a paid one.</summary>
        public int Waive(Debt d, int amount, int day)
        {
            if (d == null || !d.Active || amount <= 0) return 0;
            int w = Math.Min(amount, d.Outstanding); d.Outstanding -= w; d.Waived += w;
            if (d.Outstanding == 0) d.Status = d.Waived >= d.Original ? DebtStatus.Forgiven : DebtStatus.Paid;
            return w;
        }

        public bool Forgive(int debtId) { var d = DebtById(debtId); if (d == null || !d.Active) return false; Waive(d, d.Outstanding, 0); d.Status = DebtStatus.Forgiven; return true; }
        public bool Dispute(int debtId, bool disputed) { var d = DebtById(debtId); if (d == null || (d.Status != DebtStatus.Open && d.Status != DebtStatus.Disputed)) return false; d.Status = disputed ? DebtStatus.Disputed : DebtStatus.Open; return true; }

        // ------------------------------------------------------------------ household changes and death

        /// <summary>
        /// A household splits: <paramref name="amount"/> of its silver and the listed debts move to the new account, each exactly once. A debt that is moved is re-pointed, never copied.
        /// </summary>
        public LedgerResult Split(string from, string to, int amount, IEnumerable<int> debtIds, string key, int day)
        {
            if (Get(from) == null || Get(to) == null) return LedgerResult.UnknownAccount;
            if (key != null && keys.ContainsKey(key)) return LedgerResult.Duplicate;
            if (amount > Balance(from)) return LedgerResult.Insufficient;
            if (amount > 0) { var r = Transfer(from, to, amount, TxKind.Transfer, key, day, "split"); if (r != LedgerResult.Ok) return r; } else if (key != null) keys[key] = day;
            foreach (int id in debtIds ?? new int[0]) { var d = DebtById(id); if (d == null || !d.Active) continue; if (d.Debtor == from) d.Debtor = to; if (d.Creditor == from) d.Creditor = to; if (d.Debtor == d.Creditor) { d.Status = DebtStatus.Forgiven; d.Outstanding = 0; } }
            return LedgerResult.Ok;
        }

        /// <summary>
        /// Someone leaves a shared account taking <paramref name="num"/>/<paramref name="den"/> of it: that fraction of its silver moves, and the same fraction of every debt it owes and is owed
        /// moves with it (split into a new obligation, never copied), so the total money and the total liabilities are exactly what they were. Applied once per key. Taking the last share
        /// (<c>num == den</c>) moves everything.
        /// </summary>
        public LedgerResult TransferShare(string from, string to, int num, int den, string key, int day)
        {
            if (Get(from) == null || Get(to) == null || from == to || den <= 0 || num <= 0 || num > den) return LedgerResult.Invalid;
            if (key != null && keys.ContainsKey(key)) return LedgerResult.Duplicate;
            int silver = (int)((long)Balance(from) * num / den);
            if (silver > 0) { var r = Transfer(from, to, silver, TxKind.Transfer, key, day, "share of household"); if (r != LedgerResult.Ok) return r; } else if (key != null) keys[key] = day;
            foreach (var d in debts.Where(x => x.Active && (x.Debtor == from || x.Creditor == from)).ToList())
            {
                int part = num == den ? d.Outstanding : (int)((long)d.Outstanding * num / den);
                if (part <= 0) continue;
                var moved = new Debt { Id = NextDebtId++, Debtor = d.Debtor == from ? to : d.Debtor, Creditor = d.Creditor == from ? to : d.Creditor, Kind = d.Kind, Ordinary = d.Ordinary, Original = part, Outstanding = part, DueDay = d.DueDay, NextDueDay = d.NextDueDay, Installment = d.Installment, EveryDays = d.EveryDays, FamilyRef = d.FamilyRef, Origin = (d.Origin ?? "") + " (share)", Status = d.Status };
                d.Outstanding -= part; if (d.Outstanding == 0) d.Status = DebtStatus.Paid;
                if (moved.Debtor == moved.Creditor) continue;          // both sides ended up the same account: the obligation cancels itself
                debts.Add(moved);
            }
            return LedgerResult.Ok;
        }

        /// <summary>Silver that is gone (stolen, destroyed, burned). Recorded as an outflow from the named account so the ledger stays conserved and the loss is visible in the history.</summary>
        public LedgerResult RecordLoss(string account, int amount, string key, int day, string memo) => Spend(account, amount, TxKind.Correction, key, day, memo);

        /// <summary>
        /// A person dies. The estate is settled from the estate's own assets first: creditors are paid in order of standing (public obligations - tax, restitution, damage - before private debts), and within a
        /// class pro rata when the assets fall short, so no creditor is favoured by being first on a list. What cannot be paid is recorded as an unrecovered estate shortfall (status written off, the unpaid
        /// amount kept visible); it does <b>not</b> pass to the heirs unless <paramref name="heirsLiable"/> is set by an explicit gameplay policy. What remains is shared equally among the heirs, the odd silver
        /// to the first, or goes to the community if there are none. Debts owed to the deceased pass to the first heir (or the community). The account closes. Applied once per key.
        /// </summary>
        public LedgerResult SettleEstate(string deceased, IList<string> heirs, string key, int day, bool heirsLiable = false)
        {
            var acct = Get(deceased); if (acct == null) return LedgerResult.UnknownAccount;
            if (key != null && keys.ContainsKey(key)) return LedgerResult.Duplicate;
            var live = (heirs ?? new string[0]).Where(h => h != deceased && Get(h) != null && !Get(h).Closed).Distinct().ToList();
            var moves = new List<Move>(); int left = acct.Balance;
            var owed = debts.Where(x => x.Active && x.Debtor == deceased).OrderBy(x => x.DueDay).ThenBy(x => x.Id).ToList();
            foreach (var cls in new[] { owed.Where(x => x.Kind == DebtKind.Tax || x.Kind == DebtKind.Restitution || x.Kind == DebtKind.Damage).ToList(), owed.Where(x => !(x.Kind == DebtKind.Tax || x.Kind == DebtKind.Restitution || x.Kind == DebtKind.Damage)).ToList() })
            {
                long total = cls.Sum(x => (long)x.Outstanding); if (total <= 0 || left <= 0) continue;
                int pool = (int)Math.Min(left, total);
                var pay = cls.ToDictionary(x => x.Id, x => (int)((long)pool * x.Outstanding / total));
                int rest = pool - pay.Values.Sum();
                foreach (var x in cls.OrderByDescending(x => (long)pool * x.Outstanding - (long)pay[x.Id] * total).ThenBy(x => x.Id).Take(rest)) pay[x.Id]++;
                foreach (var x in cls)
                {
                    int p = Math.Min(pay[x.Id], x.Outstanding); if (p <= 0) continue;
                    moves.Add(new Move(deceased, x.Creditor, p, "estate debt " + x.Id)); x.Outstanding -= p; x.Payments.Add(day + ":" + p); left -= p;
                    if (x.Outstanding == 0) x.Status = DebtStatus.Paid;
                }
            }
            foreach (var d in owed.Where(x => x.Outstanding > 0))
            {
                if (heirsLiable && live.Count > 0) { d.Debtor = live[0]; d.Origin = (d.Origin ?? "") + " (inherited by policy)"; if (d.Creditor == d.Debtor) Waive(d, d.Outstanding, day); }
                else { d.Status = DebtStatus.WrittenOff; d.Origin = (d.Origin ?? "") + " (estate shortfall)"; }          // the unpaid amount stays on the record; it is no longer a live liability of anyone
            }
            if (left > 0)
            {
                if (live.Count == 0) moves.Add(new Move(deceased, CommunityId, left, "estate to community"));
                else { int share = left / live.Count, odd = left - share * live.Count; for (int i = 0; i < live.Count; i++) { int part = share + (i == 0 ? odd : 0); if (part > 0) moves.Add(new Move(deceased, live[i], part, "inheritance")); } }
            }
            foreach (var d in debts.Where(x => x.Active && x.Creditor == deceased).ToList()) d.Creditor = live.Count > 0 ? live[0] : CommunityId;
            if (moves.Count > 0) { var r = Apply(key, day, TxKind.Estate, moves); if (r != LedgerResult.Ok) return r; } else if (key != null) keys[key] = day;
            acct.Closed = true; acct.Successor = live.Count > 0 ? live[0] : CommunityId;
            return LedgerResult.Ok;
        }

        // ------------------------------------------------------------------ persistence

        private static string S(string v) => (v ?? "").Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ').Replace(',', ';');
        public string Encode()
        {
            var w = new StateWriter().Int("ntx", NextTxId).Int("ndebt", NextDebtId).Strings("xin", new[] { ExternalIn.ToString(System.Globalization.CultureInfo.InvariantCulture) }).Strings("xout", new[] { ExternalOut.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            w.Strings("acct", accounts.Values.OrderBy(a => a.Id, StringComparer.Ordinal).Select(a => string.Join("|", S(a.Id), (int)a.Kind, a.Owner, a.Balance, a.Closed ? 1 : 0, S(a.Successor))).ToList());
            w.Strings("tx", history.Select(t => string.Join("|", t.Id, t.Day, (int)t.Kind, S(t.From), S(t.To), t.Amount, S(t.Key), S(t.Memo), S(t.Tag))).ToList());
            w.Strings("key", keys.OrderBy(k => k.Value).ThenBy(k => k.Key, StringComparer.Ordinal).Select(k => S(k.Key) + "|" + k.Value).ToList());
            w.Strings("debt", debts.Select(d => string.Join("|", d.Id, S(d.Debtor), S(d.Creditor), (int)d.Kind, d.Original, d.Outstanding, d.DueDay, d.Installment, d.EveryDays, d.FamilyRef, d.NextDueDay, (int)d.Status, S(d.Origin), string.Join(";", d.Payments), d.Waived, d.Ordinary ? 1 : 0)).ToList());
            w.Int("unb", Unbacked); w.Strings("inc", incomeSince.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => S(k.Key) + "|" + k.Value).ToList());
            return w.ToString();
        }

        public static Ledger Decode(StateReader r)
        {
            var l = new Ledger(); l.accounts.Clear();
            l.NextTxId = Math.Max(1, r.Int("ntx", 1)); l.NextDebtId = Math.Max(1, r.Int("ndebt", 1));
            l.Unbacked = r.Int("unb"); foreach (var s in r.Strings("inc")) { var p = s.Split('|'); if (p.Length == 2 && p[0].Length > 0) l.incomeSince[p[0]] = I(p[1]); }
            long.TryParse(r.Strings("xin").FirstOrDefault() ?? "0", System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out l.ExternalIn);
            long.TryParse(r.Strings("xout").FirstOrDefault() ?? "0", System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out l.ExternalOut);
            foreach (var s in r.Strings("acct")) { var p = s.Split('|'); if (p.Length < 6 || string.IsNullOrEmpty(p[0])) continue; l.accounts[p[0]] = new Account { Id = p[0], Kind = (AccountKind)I(p[1]), Owner = I(p[2]), Balance = I(p[3]), Closed = p[4] == "1", Successor = p[5].Length == 0 ? null : p[5] }; }
            if (!l.accounts.ContainsKey(CommunityId)) l.accounts[CommunityId] = new Account { Id = CommunityId, Kind = AccountKind.Community };
            foreach (var s in r.Strings("tx")) { var p = s.Split('|'); if (p.Length < 9) continue; l.history.Add(new Transaction { Id = I(p[0]), Day = I(p[1]), Kind = (TxKind)I(p[2]), From = N(p[3]), To = N(p[4]), Amount = I(p[5]), Key = N(p[6]), Memo = N(p[7]), Tag = N(p[8]) }); }
            foreach (var s in r.Strings("key")) { var p = s.Split('|'); if (p.Length == 2 && p[0].Length > 0) l.keys[p[0]] = I(p[1]); }
            foreach (var s in r.Strings("debt"))
            {
                var p = s.Split('|'); if (p.Length < 14) continue;
                var d = new Debt { Id = I(p[0]), Debtor = p[1], Creditor = p[2], Kind = (DebtKind)I(p[3]), Original = I(p[4]), Outstanding = I(p[5]), DueDay = I(p[6]), Installment = I(p[7]), EveryDays = I(p[8]), FamilyRef = I(p[9]), NextDueDay = I(p[10]), Status = (DebtStatus)I(p[11]), Origin = p[12] };
                if (p[13].Length > 0) d.Payments.AddRange(p[13].Split(';'));
                if (p.Length >= 16) { d.Waived = I(p[14]); d.Ordinary = p[15] == "1"; }
                l.debts.Add(d);
            }
            return l;
        }
        private static int I(string s) => int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : 0;
        private static string N(string s) => s.Length == 0 ? null : s;
    }

    public sealed class LedgerFactory : IStateFactory<Ledger>
    {
        public static readonly LedgerFactory Instance = new LedgerFactory();
        public int Version => 1;
        public Ledger Create() => new Ledger();
        public Ledger Decode(StateReader reader, int version) => Ledger.Decode(reader);
        public Ledger FromLegacy(StateReader legacy) => new Ledger();
    }

    public static class LedgerAccess
    {
        public static Ledger Ledger(this FamilyRegistry r) => r.Shared.Of(Domain.Ledger.StateId, LedgerFactory.Instance);
    }
}
