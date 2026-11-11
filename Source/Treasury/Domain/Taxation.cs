using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FamilyLife.Framework;

namespace FamilyLife.Domain
{
    public static class TreasuryRoles
    {
        public const string Career = "wils.treasury", Treasurer = "wils.treasury.treasurer", TaxCollector = "wils.treasury.taxcollector";
        /// <summary>Tax Collector qualification. Configurable here and balanced by changing these three numbers.</summary>
        public static int CollectorSocial = 7, CollectorMelee = 6, CollectorExperience = 120;
        public const int DutyPoints = 6;
    }

    /// <summary>Whether formal taxation exists yet. It needs at least one qualified Tax Collector who is currently assigned; before that the community may keep a share of income, but that is not a tax.</summary>
    public static class TaxAuthority
    {
        public static bool Unlocked(int assignedQualifiedCollectors) => assignedQualifiedCollectors > 0;
        /// <summary>With no collector assigned, existing policy and debts stay exactly as they are but nothing new is assessed and no round is run.</summary>
        public static bool MayAssess(int assignedQualifiedCollectors) => Unlocked(assignedQualifiedCollectors);
        public static bool MayCollect(int assignedQualifiedCollectors) => Unlocked(assignedQualifiedCollectors);
    }

    public enum Approach : byte { Persuade, Firm, Intimidate }
    public enum CollectionOutcome : byte { Paid, Partial, Plan, Refused, Hardship }

    /// <summary>Everything a collection visit weighs. All values are plain numbers the game layer fills in from skills, relationships, traits and mood.</summary>
    public sealed class CollectionContext
    {
        public int Owed, PayerBalance, PayerLiabilities;
        public int CollectorSocial, CollectorMelee;
        public int Opinion;                        // payer's opinion of the collector, -100..100
        public int FamilyOpinion;                  // between their families, -100..100
        public float PayerMood = .5f;              // 0..1
        public int PriorRefusals;                  // times this payer has refused before
        public int Legitimacy = 60;                // the community's trust in the collection, 0..100
        public bool PayerProud, PayerGenerous, CollectorKind, CollectorAbrasive;
        public Approach Approach = Approach.Persuade;
    }

    public sealed class CollectionResult
    {
        public CollectionOutcome Outcome; public int Pay, Installment, EveryDays;
        public int OpinionDelta, LegitimacyDelta, FamilyOpinionDelta;
        public float Chance;
    }

    public static class Collection
    {
        /// <summary>
        /// Decides one visit. Affordability comes first: someone who cannot pay is in hardship, not refusing. Otherwise a willingness score (relationship, social skill, mood, legitimacy, traits)
        /// is compared with a roll; Melee adds to compliance only for an intimidating approach and costs trust and goodwill, and nothing here is violent. The same inputs and roll always give the same result.
        /// </summary>
        public static CollectionResult Resolve(CollectionContext c, float roll)
        {
            var r = new CollectionResult();
            if (c.Owed <= 0) { r.Outcome = CollectionOutcome.Paid; return r; }
            int spare = Math.Max(0, c.PayerBalance);
            if (spare <= 0) { r.Outcome = CollectionOutcome.Hardship; r.LegitimacyDelta = 0; return r; }
            float will = .45f + c.CollectorSocial * .02f + c.Opinion / 250f + c.FamilyOpinion / 500f + (c.PayerMood - .5f) * .3f + (c.Legitimacy - 50) / 250f
                       - c.PriorRefusals * .06f - (c.PayerProud ? .08f : 0f) + (c.PayerGenerous ? .08f : 0f) + (c.CollectorKind ? .05f : 0f) - (c.CollectorAbrasive ? .06f : 0f);
            if (c.Approach == Approach.Firm) will += .05f;
            if (c.Approach == Approach.Intimidate) { will += c.CollectorMelee * .02f; r.OpinionDelta -= 6; r.LegitimacyDelta -= 3; r.FamilyOpinionDelta -= 3; }
            // Debts already weighing on the payer make it harder, never impossible.
            if (c.PayerLiabilities > spare) will -= .05f;
            will = Math.Max(.03f, Math.Min(.97f, will)); r.Chance = will;
            bool affordableInFull = spare >= c.Owed;
            if (roll < will)
            {
                if (affordableInFull) { r.Outcome = CollectionOutcome.Paid; r.Pay = c.Owed; r.OpinionDelta += c.Approach == Approach.Persuade ? 1 : 0; r.LegitimacyDelta += c.Approach == Approach.Persuade ? 1 : 0; }
                else { r.Outcome = CollectionOutcome.Partial; r.Pay = spare / 2 > 0 ? spare / 2 : spare; }
            }
            else if (roll < will + (1f - will) * .45f && c.CollectorSocial >= 4)
            {
                // Persuasion that does not get the money today can still get an agreement: a plan with a small instalment from what they actually hold.
                r.Outcome = CollectionOutcome.Plan; r.Installment = Math.Max(1, Math.Min(c.Owed, Math.Max(1, spare / 3))); r.EveryDays = 7; r.OpinionDelta += 1;
            }
            else { r.Outcome = CollectionOutcome.Refused; r.OpinionDelta -= 2; r.FamilyOpinionDelta -= 1; r.LegitimacyDelta -= 1; }
            return r;
        }

        /// <summary>Applies a result to the books: money moves from the payer to the community (and the debt falls by exactly that), a plan is attached to the debt, anything else changes nothing financial.</summary>
        public static int Apply(Ledger ledger, Debt debt, CollectionResult result, string key, int day)
        {
            if (debt == null || !debt.Active) return 0;
            int paid = 0;
            if (result.Outcome == CollectionOutcome.Paid || result.Outcome == CollectionOutcome.Partial) paid = ledger.Pay(debt.Id, result.Pay, key, day);
            else if (result.Outcome == CollectionOutcome.Plan) { debt.Installment = result.Installment; debt.EveryDays = result.EveryDays; debt.NextDueDay = day + result.EveryDays; }
            return paid;
        }
    }

    /// <summary>
    /// A collector who handles money dishonestly. The ledger always records what really moved: from a 100-silver collection of which 30 is diverted, 70 reaches the treasury and 30 the collector, in
    /// one batch whose tag keeps the amount that was reported. Later discovery opens a restitution debt for exactly the diverted amount.
    /// </summary>
    public static class Misconduct
    {
        public static LedgerResult Divert(Ledger ledger, Debt debt, int collected, int diverted, string collectorAccount, string key, int day, bool reportedAsFull = true)
        {
            if (debt == null || collected <= 0 || diverted < 0 || diverted > collected || ledger.Get(collectorAccount) == null) return LedgerResult.Invalid;
            int toTreasury = collected - diverted;
            var moves = new List<Move>();
            if (toTreasury > 0) moves.Add(new Move(debt.Debtor, debt.Creditor, toTreasury, "collection"));
            if (diverted > 0) moves.Add(new Move(debt.Debtor, collectorAccount, diverted, "diverted"));
            var r = ledger.Apply(key, day, diverted > 0 ? TxKind.Misconduct : TxKind.Tax, moves, diverted > 0 ? "reported:" + (reportedAsFull ? collected : toTreasury) + ";actual:" + toTreasury : null);
            if (r == LedgerResult.Ok) ledger.RecordPayment(debt, collected, day);          // the payer really paid it all; the treasury is the one short
            return r;
        }

        /// <summary>Making it right: the collector owes the treasury (or the payer) what was taken. Opens a restitution debt; paying it is an ordinary transfer.</summary>
        public static Debt Restitution(Ledger ledger, string collectorAccount, string toAccount, int amount, string origin, int day) => ledger.OpenDebt(collectorAccount, toAccount, DebtKind.Restitution, amount, day + 7, origin);
    }

    /// <summary>Events the tax round can raise. Triggers are grounded in the visit itself; probabilities are bounded and each event has its own cooldown, so nothing repeats or floods the player.</summary>
    public sealed class TaxEventDef
    {
        public string Id, Category;          // noncompliance, misconduct, positive, consequence
        public float Base; public int CooldownDays;
        public bool NeedsRefusal, NeedsLowLegitimacy, NeedsHighLegitimacy, NeedsCollectorOwnKin;
        public string TraitUp;          // a trait key that makes it likelier (never certain)
        public int LegitimacyDelta, OpinionDelta;
    }

    public static class TaxEvents
    {
        public const float MaxChance = .25f;
        public static readonly List<TaxEventDef> All = new List<TaxEventDef>
        {
            new TaxEventDef { Id = "refuses_disliked_collector", Category = "noncompliance", Base = .10f, CooldownDays = 15, NeedsRefusal = true, OpinionDelta = -3 },
            new TaxEventDef { Id = "underreports_income", Category = "noncompliance", Base = .06f, CooldownDays = 30, TraitUp = "Greedy" },
            new TaxEventDef { Id = "claims_hardship", Category = "noncompliance", Base = .08f, CooldownDays = 20 },
            new TaxEventDef { Id = "requests_extension", Category = "noncompliance", Base = .08f, CooldownDays = 20 },
            new TaxEventDef { Id = "hides_funds", Category = "noncompliance", Base = .04f, CooldownDays = 30, TraitUp = "Greedy", NeedsLowLegitimacy = true },
            new TaxEventDef { Id = "offers_bribe", Category = "noncompliance", Base = .04f, CooldownDays = 30, TraitUp = "Greedy" },
            new TaxEventDef { Id = "accuses_unfairness", Category = "noncompliance", Base = .05f, CooldownDays = 25, NeedsLowLegitimacy = true, LegitimacyDelta = -2 },
            new TaxEventDef { Id = "embezzles", Category = "misconduct", Base = .03f, CooldownDays = 45, TraitUp = "Greedy", NeedsLowLegitimacy = true, LegitimacyDelta = -6 },
            new TaxEventDef { Id = "demands_more", Category = "misconduct", Base = .03f, CooldownDays = 45, TraitUp = "Abrasive", LegitimacyDelta = -4 },
            new TaxEventDef { Id = "favours_friends", Category = "misconduct", Base = .04f, CooldownDays = 40, LegitimacyDelta = -3 },
            new TaxEventDef { Id = "reduces_own_taxes", Category = "misconduct", Base = .02f, CooldownDays = 60, NeedsCollectorOwnKin = true, LegitimacyDelta = -5 },
            new TaxEventDef { Id = "delays_against_relatives", Category = "misconduct", Base = .03f, CooldownDays = 40, NeedsCollectorOwnKin = true, LegitimacyDelta = -3 },
            new TaxEventDef { Id = "falsifies_record", Category = "misconduct", Base = .02f, CooldownDays = 60, TraitUp = "Greedy", LegitimacyDelta = -6 },
            new TaxEventDef { Id = "accepts_bribe", Category = "misconduct", Base = .02f, CooldownDays = 60, TraitUp = "Greedy", LegitimacyDelta = -5 },
            new TaxEventDef { Id = "defers_hardship", Category = "positive", Base = .08f, CooldownDays = 20, LegitimacyDelta = 2, OpinionDelta = 3 },
            new TaxEventDef { Id = "negotiates_plan", Category = "positive", Base = .08f, CooldownDays = 15, LegitimacyDelta = 1, OpinionDelta = 2 },
            new TaxEventDef { Id = "fair_treatment", Category = "positive", Base = .07f, CooldownDays = 20, NeedsHighLegitimacy = true, LegitimacyDelta = 3 },
            new TaxEventDef { Id = "reports_missing_funds", Category = "positive", Base = .03f, CooldownDays = 45, LegitimacyDelta = 4 },
            new TaxEventDef { Id = "refuses_bribe", Category = "positive", Base = .04f, CooldownDays = 40, LegitimacyDelta = 3, OpinionDelta = 2 },
            new TaxEventDef { Id = "returns_money", Category = "positive", Base = .02f, CooldownDays = 60, LegitimacyDelta = 3 },
            new TaxEventDef { Id = "resolves_dispute", Category = "positive", Base = .04f, CooldownDays = 30, LegitimacyDelta = 2, OpinionDelta = 2 },
            new TaxEventDef { Id = "corruption_accusation", Category = "consequence", Base = .05f, CooldownDays = 40, NeedsLowLegitimacy = true, LegitimacyDelta = -2 },
            new TaxEventDef { Id = "restitution_demand", Category = "consequence", Base = .04f, CooldownDays = 40, NeedsLowLegitimacy = true },
            new TaxEventDef { Id = "community_petition", Category = "consequence", Base = .03f, CooldownDays = 60, NeedsLowLegitimacy = true, LegitimacyDelta = -1 },
            new TaxEventDef { Id = "improved_relations", Category = "consequence", Base = .04f, CooldownDays = 45, NeedsHighLegitimacy = true, OpinionDelta = 2 }
        };

        public sealed class Trigger { public bool Refused; public int Legitimacy = 60, Day; public bool CollectorOwnKin; public HashSet<string> Traits = new HashSet<string>(); }

        /// <summary>The chance of an event in this situation, bounded to <see cref="MaxChance"/>. A trait raises it; it never makes the event certain. Zero if the grounds are absent or it is on cooldown.</summary>
        public static float Chance(TaxEventDef e, Trigger t, IDictionary<string, int> lastDay)
        {
            if (e.NeedsRefusal && !t.Refused) return 0f;
            if (e.NeedsLowLegitimacy && t.Legitimacy >= 50) return 0f;
            if (e.NeedsHighLegitimacy && t.Legitimacy < 65) return 0f;
            if (e.NeedsCollectorOwnKin && !t.CollectorOwnKin) return 0f;
            if (lastDay != null && lastDay.TryGetValue(e.Id, out var last) && t.Day - last < e.CooldownDays) return 0f;
            float p = e.Base * (e.TraitUp != null && t.Traits.Contains(e.TraitUp) ? 2f : 1f) * (e.Category == "misconduct" ? 1f + (50 - Math.Min(50, t.Legitimacy)) / 100f : 1f);
            return Math.Max(0f, Math.Min(MaxChance, p));
        }

        /// <summary>Picks at most one event for a visit: each eligible event is tried in turn with a roll the caller supplies, so the result is reproducible in tests.</summary>
        public static TaxEventDef Pick(Trigger t, IDictionary<string, int> lastDay, Func<float> roll)
        {
            foreach (var e in All) if (roll() < Chance(e, t, lastDay)) return e;
            return null;
        }
    }

    /// <summary>The tax policy and what the collection round remembers. Persisted in the module's own payload.</summary>
    public sealed class TaxBook : IModuleState
    {
        public const string StateId = "wils.treasury.tax";
        public int RatePercent, Legitimacy = 60;
        public bool AutoRemit;
        private readonly Dictionary<string, int> cursor = new Dictionary<string, int>();          // account -> last transaction id assessed
        public readonly Dictionary<string, int> LastEventDay = new Dictionary<string, int>();
        public readonly Dictionary<string, int> Refusals = new Dictionary<string, int>();

        public int CursorOf(string account) => cursor.TryGetValue(account, out var c) ? c : 0;

        /// <summary>
        /// Assesses one account for the income it received since it was last assessed (rewards, wages and trade proceeds; not internal transfers). Refused if formal taxation is not unlocked. A new
        /// assessment is a debt owed to the community; a zero-income account owes nothing, and no account is ever driven into debt by having a low balance.
        /// </summary>
        public Debt Assess(Ledger ledger, string account, int day, int activeCollectors)
        {
            if (!TaxAuthority.MayAssess(activeCollectors) || RatePercent <= 0 || ledger.Get(account) == null || account == Ledger.CommunityId) return null;
            int after = CursorOf(account);
            int income = ledger.History.Where(t => t.Id > after && t.To == account && (t.Kind == TxKind.Reward || t.Kind == TxKind.Payroll || t.Kind == TxKind.Trade)).Sum(t => t.Amount);          // income only: transfers between accounts are not income
            cursor[account] = ledger.NextTxId - 1;
            int tax = (int)((long)income * Math.Max(0, Math.Min(60, RatePercent)) / 100);
            return tax > 0 ? ledger.OpenDebt(account, Ledger.CommunityId, DebtKind.Tax, tax, day + 7, "tax " + day) : null;
        }

        public string Encode()
        {
            var w = new StateWriter().Int("rate", RatePercent).Int("leg", Legitimacy).Bool("auto", AutoRemit);
            w.Strings("cur", cursor.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "|" + k.Value).ToList());
            w.Strings("ev", LastEventDay.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "|" + k.Value).ToList());
            w.Strings("ref", Refusals.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "|" + k.Value).ToList());
            return w.ToString();
        }

        public static TaxBook Decode(StateReader r)
        {
            var b = new TaxBook { RatePercent = r.Int("rate"), Legitimacy = r.Has("leg") ? r.Int("leg") : 60, AutoRemit = r.Bool("auto") };
            Fill(r.Strings("cur"), b.cursor); Fill(r.Strings("ev"), b.LastEventDay); Fill(r.Strings("ref"), b.Refusals);
            return b;
        }
        private static void Fill(List<string> src, Dictionary<string, int> into) { foreach (var s in src) { var p = s.Split('|'); if (p.Length == 2 && int.TryParse(p[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) into[p[0]] = n; } }
    }

    public sealed class TaxFactory : IStateFactory<TaxBook>
    {
        public static readonly TaxFactory Instance = new TaxFactory();
        public int Version => 1;
        public TaxBook Create() => new TaxBook();
        public TaxBook Decode(StateReader reader, int version) => TaxBook.Decode(reader);
        public TaxBook FromLegacy(StateReader legacy) => new TaxBook();
    }

    public static class TaxAccess { public static TaxBook Tax(this FamilyRegistry r) => r.Shared.Of(TaxBook.StateId, TaxFactory.Instance); }
}
