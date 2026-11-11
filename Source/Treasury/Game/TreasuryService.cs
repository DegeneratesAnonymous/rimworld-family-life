using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The game side of Wil's Treasury: it reads the world (silver in storage, who is assigned, the calendar) and calls the pure ledger, payroll and tax code. Everything financial is decided
    /// by <see cref="Ledger"/> and friends; nothing here moves money except through them. Owner: Wil's Treasury.
    /// </summary>
    internal static class TreasuryService
    {
        public const string Id = "wils.treasury";
        public const string PageId = "wils.treasury.treasury";

        /// <summary>The pay-calendar day: days since the start of year 0, from the world's own calendar at the home map's longitude.</summary>
        public static int Today()
        {
            var map = Find.AnyPlayerHomeMap ?? Find.CurrentMap;
            float lon = map != null ? Find.WorldGrid.LongLatOf(map.Tile).x : 0f;
            long abs = Find.TickManager.TicksAbs;
            return GenDate.Year(abs, lon) * PayCalendar.DaysPerYear + GenDate.DayOfYear(abs, lon);
        }

        public static List<Resident> Residents(FamilyRegistry r) => r.Persons.Where(p => p.InColony).Select(p => new Resident(p.Id, p.HouseholdId)).ToList();
        public static string PersonAccount(FamilyRegistry r, int pawnId) { r.Ledger().Ensure(AccountKind.Person, pawnId); return Ledger.IdFor(AccountKind.Person, pawnId); }

        /// <summary>Assigned colonists in a role (assignment already implies the qualification the role needs).</summary>
        public static List<int> Holders(FamilyRegistry r, string role) => r.Assignments().Holders(role).Where(id => r.GetPerson(id)?.InColony == true).ToList();
        public static int ActiveCollectors(FamilyRegistry r) => Holders(r, TreasuryRoles.TaxCollector).Count;

        public static int PhysicalSilver(Map map) => map == null ? 0 : map.resourceCounter.GetCount(ThingDefOf.Silver);

        // ------------------------------------------------------------------ income

        /// <summary>Silver arrived from a quest reward. Shared once (the key is the quest part's own), by the configured policy.</summary>
        public static void OnSilver(int amount, string key)
        {
            var c = FamilyComponent.Current; if (c == null || amount <= 0) return;
            var r = c.Registry; var st = r.Treasury();
            var result = Income.DistributeReward(r.Ledger(), amount, Residents(r), new IncomePolicy { CommunityPercent = st.CommunityPercent }, key, Today());
            if (result == LedgerResult.Ok) Messages.Message("WFL_Tr_RewardShared".Translate(amount, r.Ledger().Balance(Ledger.CommunityId)), MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>A completed trade: purchases leave the accounts treasury-first, sales arrive by owner or by the community policy. Settled once under the trade's own id.</summary>
        public static void OnTrade(TradeReport report)
        {
            var c = FamilyComponent.Current; if (c == null || report == null) return;
            var r = c.Registry; var l = r.Ledger(); var st = r.Treasury();
            int cost = report.Lines.Where(x => !x.Sold).Sum(x => x.Count * x.UnitPrice);
            var plan = cost > 0 ? Trade.Plan(l, cost, report.ColonySilverBefore) : null;
            var sales = report.Lines.Where(x => x.Sold).Select(x => new Income.Sale { Proceeds = x.Count * x.UnitPrice, OwnerAccount = x.OwnerPawn != null && r.GetPerson(x.OwnerPawn.thingIDNumber)?.InColony == true ? PersonAccount(r, x.OwnerPawn.thingIDNumber) : null }).ToList();
            var res = Trade.Settle(l, report.Id, Today(), plan, sales, Residents(r), new IncomePolicy { CommunityPercent = st.CommunityPercent, OwnerFeePercent = st.OwnerFeePercent });
            if (res != LedgerResult.Ok && res != LedgerResult.Duplicate) Log.Warning("[Wil's Treasury] A trade could not be recorded (" + res + ").");
        }

        // ------------------------------------------------------------------ wages

        /// <summary>Finished professional work. Paid only to someone assigned to a role in that career, at that role's rate, once per finished activity, within the daily limits.</summary>
        public static void OnWork(Pawn pawn, string career, string specialty, string kind, string key, int now)
        {
            var c = FamilyComponent.Current; if (c == null || pawn == null) return;
            var r = c.Registry; int id = pawn.thingIDNumber; var book = r.Assignments();
            var role = CareerService.Professions.All.Where(x => x.Career == career && book.IsAssigned(id, x.Id)).OrderByDescending(x => x.Specialty == specialty ? 1 : 0).ThenByDescending(x => x.Level).FirstOrDefault();
            if (role == null) return;
            r.Payroll().Accrue(id, role.Id, 1, Today(), key ?? (career + "|" + id + "|" + now));
        }

        // ------------------------------------------------------------------ the periodic pass (payroll, silver reconciliation, estates)

        public static void Periodic(int now)
        {
            var c = FamilyComponent.Current; if (c == null) return;
            var map = Find.AnyPlayerHomeMap; if (map == null) return;
            var r = c.Registry; var l = r.Ledger(); var st = r.Treasury(); int today = Today();
            Reconcile(l, st, PhysicalSilver(map), now, today);
            foreach (var rep in r.Payroll().SettleDue(l, today, PhysicalSilver(map)))
            {
                if (rep.TotalDue <= 0) continue;
                Messages.Message("WFL_Tr_Payday".Translate(rep.TotalDue, rep.TotalPaid, rep.Shortfall > 0 ? (string)"WFL_Tr_ShortBy".Translate(rep.Shortfall) : ""), MessageTypeDefOf.NeutralEvent, false);
                var treasurer = Holders(r, TreasuryRoles.Treasurer).Select(c.TryGetPawn).FirstOrDefault(p => p != null);
                if (treasurer != null) CareerService.Report(c, treasurer, TreasuryRoles.Career, null, "treasury.payday", "payday|" + rep.Payday, TreasuryRoles.DutyPoints, now);
            }
            Estates(r, l, today);
            l.Prune(today);
        }

        /// <summary>
        /// Silver in storage that no account claims is adopted by the community treasury (an existing colony's starting silver, loot, a gift). Claims above the silver in storage are only reported:
        /// nothing is debited to hide a shortfall, because the silver may simply be in a caravan or a pocket.
        /// </summary>
        private static void Reconcile(Ledger l, TreasuryState st, int physical, int now, int today)
        {
            long surplus = physical - l.TotalClaims;
            if (surplus > 0 && st.AdoptSurplus) l.Receive(Ledger.CommunityId, (int)Math.Min(int.MaxValue, surplus), TxKind.Correction, "recon:" + now, today, "unclaimed silver in storage");
        }

        /// <summary>A person who has died settles their estate (debts first, then heirs: their old household, otherwise the community); a household nobody lives in any more passes to the community.</summary>
        private static void Estates(FamilyRegistry r, Ledger l, int today)
        {
            foreach (var a in l.Accounts.Where(x => !x.Closed && x.Kind != AccountKind.Community).ToList())
            {
                if (a.Kind == AccountKind.Person)
                {
                    var p = r.GetPerson(a.Owner);
                    if (p == null || p.Presence != Presence.Dead) continue;
                    var heirs = new List<string>();
                    var h = r.GetHousehold(p.LastHouseholdId); if (h != null && h.ResidentIds.Any(i => i != a.Owner && r.GetPerson(i)?.InColony == true)) { l.Ensure(AccountKind.Household, h.Id); heirs.Add(Ledger.IdFor(AccountKind.Household, h.Id)); }
                    l.SettleEstate(a.Id, heirs, "estate:" + a.Id, today);
                }
                else if (a.Kind == AccountKind.Household)
                {
                    var h = r.GetHousehold(a.Owner);
                    if (h == null || !h.ResidentIds.Any(i => r.GetPerson(i)?.InColony == true)) l.SettleEstate(a.Id, null, "estate:" + a.Id, today);
                }
            }
        }

        // ------------------------------------------------------------------ taxation

        private static ThoughtDef Thought(string name) => DefDatabase<ThoughtDef>.GetNamedSilentFail(name);

        /// <summary>The tax pass: assess income on paydays, then (if a collector is free) send them to the oldest overdue household, or remit automatically if the player prefers.</summary>
        public static void TaxPass(int now)
        {
            var c = FamilyComponent.Current; if (c == null) return;
            var r = c.Registry; var l = r.Ledger(); var tax = r.Tax(); int today = Today(); int collectors = ActiveCollectors(r);
            if (!TaxAuthority.MayAssess(collectors) || tax.RatePercent <= 0) return;          // locked, or no collector: existing debts and policy stay as they are
            bool assessedToday = tax.LastEventDay.TryGetValue("_assess", out var lastAssess) && lastAssess == today;
            if (PayCalendar.IsPayday(today) && !assessedToday)
            {
                tax.LastEventDay["_assess"] = today;
                foreach (var a in l.Accounts.Where(x => !x.Closed && (x.Kind == AccountKind.Household || (x.Kind == AccountKind.Person && r.GetPerson(x.Owner)?.HouseholdId == 0 && r.GetPerson(x.Owner)?.InColony == true))).ToList()) tax.Assess(l, a.Id, today, collectors);
            }
            var due = l.Debts.Where(x => x.Active && x.Status == DebtStatus.Open && x.Kind == DebtKind.Tax && x.NextDueDay <= today && !(tax.LastEventDay.TryGetValue("try:" + x.Id, out var tried) && tried == today)).OrderBy(x => x.DueDay).FirstOrDefault();
            if (due == null) return;
            if (tax.AutoRemit) { tax.LastEventDay["try:" + due.Id] = today; l.Pay(due.Id, Math.Min(due.Outstanding, Math.Max(due.Installment, 0) > 0 ? due.Installment : due.Outstanding), "remit:" + due.Id + ":" + today, today); return; }
            var job = DefDatabase<JobDef>.GetNamedSilentFail("WFL_TaxVisit"); if (job == null) return;
            foreach (int cid in Holders(r, TreasuryRoles.TaxCollector))
            {
                var collector = c.TryGetPawn(cid); if (collector == null || !collector.Spawned || PawnReadinessApi.StopReason(collector, now) != null) continue;
                var target = PayerPawn(c, due, cid); if (target == null || !target.Spawned) continue;
                tax.LastEventDay["try:" + due.Id] = today;
                if (FrameworkServices.StartMeeting(collector, target, job, due.Id)) return;
            }
        }

        private static Pawn PayerPawn(FamilyComponent c, Debt debt, int notThis)
        {
            var r = c.Registry; var a = r.Ledger().Get(debt.Debtor); if (a == null) return null;
            if (a.Kind == AccountKind.Person) return c.TryGetPawn(a.Owner);
            var h = r.GetHousehold(a.Owner);
            return h?.ResidentIds.Where(i => i != notThis && r.GetPerson(i)?.InColony == true && !(r.GetPerson(i)?.Juvenile ?? true)).Select(c.TryGetPawn).FirstOrDefault(p => p != null && p.Spawned);
        }

        /// <summary>The visit has really happened (the framework's talk job ended). Decides the outcome from the people involved, applies exactly what moved, and may raise an event.</summary>
        public static void OnCollectionVisit(Pawn collector, Pawn payer, int debtId, int now)
        {
            var c = FamilyComponent.Current; if (c == null) return;
            var r = c.Registry; var l = r.Ledger(); var tax = r.Tax(); int today = Today();
            var debt = l.DebtById(debtId); if (debt == null || !debt.Active || debt.Kind != DebtKind.Tax) return;
            if (!TaxAuthority.MayCollect(ActiveCollectors(r)) || !r.Assignments().IsAssigned(collector.thingIDNumber, TreasuryRoles.TaxCollector)) return;
            string collectorAccount = PersonAccount(r, collector.thingIDNumber);
            int owed = debt.Installment > 0 ? Math.Min(debt.Installment, debt.Outstanding) : debt.Outstanding;
            tax.Refusals.TryGetValue(debt.Debtor, out int prior);
            bool abrasive = collector.story?.traits?.HasTrait(TraitDefOf.Abrasive) == true, greedyC = collector.story?.traits?.HasTrait(TraitDefOf.Greedy) == true;
            int melee = collector.skills?.GetSkill(SkillDefOf.Melee).Level ?? 0;
            var ctx = new CollectionContext
            {
                Owed = owed, PayerBalance = l.Balance(debt.Debtor), PayerLiabilities = Math.Max(0, l.Liabilities(debt.Debtor) - debt.Outstanding),
                CollectorSocial = collector.skills?.GetSkill(SkillDefOf.Social).Level ?? 0, CollectorMelee = melee,
                Opinion = payer.relations?.OpinionOf(collector) ?? 0, PayerMood = payer.needs?.mood?.CurLevelPercentage ?? .5f, PriorRefusals = prior, Legitimacy = tax.Legitimacy,
                CollectorKind = collector.story?.traits?.HasTrait(TraitDefOf.Kind) == true, CollectorAbrasive = abrasive, PayerGenerous = payer.story?.traits?.HasTrait(TraitDefOf.Kind) == true,
                Approach = prior >= 3 && abrasive && melee >= 8 ? Approach.Intimidate : prior >= 2 ? Approach.Firm : Approach.Persuade
            };
            var result = Collection.Resolve(ctx, Rand.Value);
            var trig = new TaxEvents.Trigger { Refused = result.Outcome == CollectionOutcome.Refused, Legitimacy = tax.Legitimacy, Day = today, CollectorOwnKin = r.GetPerson(collector.thingIDNumber)?.HouseholdId == r.GetPerson(payer.thingIDNumber)?.HouseholdId && r.GetPerson(payer.thingIDNumber)?.HouseholdId > 0 };
            if (greedyC) trig.Traits.Add("Greedy"); if (abrasive) trig.Traits.Add("Abrasive");
            var ev = TaxEvents.Pick(trig, tax.LastEventDay, () => Rand.Value);

            int paid;
            string key = "col:" + debt.Id + ":" + now;
            if (ev != null && ev.Id == "embezzles" && (result.Outcome == CollectionOutcome.Paid || result.Outcome == CollectionOutcome.Partial) && result.Pay >= 4)
            {
                int diverted = Math.Max(1, result.Pay / 4), collected = Math.Min(result.Pay, l.Balance(debt.Debtor));
                if (Misconduct.Divert(l, debt, collected, Math.Min(diverted, collected), collectorAccount, key, today) == LedgerResult.Ok) { tax.Diversions.Add(collector.thingIDNumber + "|" + Math.Min(diverted, collected) + "|" + today); paid = collected; } else paid = 0;
            }
            else paid = Collection.Apply(l, debt, result, key, today);
            if (ev != null) ApplyEvent(c, tax, l, ev, collector, payer, debt, collectorAccount, owed, today);

            if (result.Outcome == CollectionOutcome.Refused) tax.Refusals[debt.Debtor] = prior + 1; else if (result.Outcome == CollectionOutcome.Paid) tax.Refusals.Remove(debt.Debtor);
            tax.Legitimacy = Mathf.Clamp(tax.Legitimacy + result.LegitimacyDelta + (ev?.LegitimacyDelta ?? 0), 0, 100);
            GainMemory(payer, result.OpinionDelta < -4 ? "WFL_TaxPressured" : result.OpinionDelta > 0 ? "WFL_TaxPaidFairly" : null, collector);
            if (result.Outcome == CollectionOutcome.Refused) GainMemory(collector, "WFL_TaxRefusedAgainst", payer);
            CareerService.Report(c, collector, TreasuryRoles.Career, null, "treasury.collection", "collect|" + debt.Id + "|" + now, TreasuryRoles.DutyPoints, now);
            Messages.Message("WFL_Tr_CollectionNote".Translate(payer.LabelShort, ("WFL_Tr_Outcome_" + result.Outcome).Translate()), payer, result.Outcome == CollectionOutcome.Refused ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.NeutralEvent, false);
        }

        private static void GainMemory(Pawn pawn, string def, Pawn other)
        {
            var d = def == null ? null : Thought(def); if (d == null || pawn?.needs?.mood?.thoughts?.memories == null) return;
            pawn.needs.mood.thoughts.memories.TryGainMemory(d, other);
        }

        /// <summary>The mechanical part of an event: money (only through the ledger), trust, and at most an occasional notice.</summary>
        private static void ApplyEvent(FamilyComponent c, TaxBook tax, Ledger l, TaxEventDef ev, Pawn collector, Pawn payer, Debt debt, string collectorAccount, int owed, int today)
        {
            tax.LastEventDay[ev.Id] = today;
            string k = "ev:" + ev.Id + ":" + debt.Id + ":" + today;
            switch (ev.Id)
            {
                case "demands_more":
                    {
                        int extra = Math.Max(1, owed / 10);
                        if (l.Transfer(debt.Debtor, collectorAccount, extra, TxKind.Misconduct, k, today, "demanded beyond the tax") == LedgerResult.Ok) tax.Diversions.Add(collector.thingIDNumber + "|" + extra + "|" + today);
                        break;
                    }
                case "accepts_bribe":
                    {
                        int bribe = Math.Max(1, owed / 10);
                        if (l.Transfer(debt.Debtor, collectorAccount, bribe, TxKind.Misconduct, k, today, "bribe") == LedgerResult.Ok) { int waived = Math.Min(debt.Outstanding, bribe * 2); l.RecordPayment(debt, waived, today); tax.Diversions.Add(collector.thingIDNumber + "|" + (bribe + waived) + "|" + today); }
                        break;
                    }
                case "reports_missing_funds":
                case "corruption_accusation":
                    if (tax.Diversions.Count > 0)
                    {
                        var p = tax.Diversions[0].Split('|'); tax.Diversions.RemoveAt(0);
                        if (p.Length == 3 && int.TryParse(p[0], out var who) && int.TryParse(p[1], out var amount) && amount > 0) Misconduct.Restitution(l, PersonAccount(c.Registry, who), Ledger.CommunityId, amount, "diversion " + p[2], today);
                    }
                    break;
                case "returns_money":
                    {
                        var rest = l.Debts.FirstOrDefault(x => x.Active && x.Kind == DebtKind.Restitution && x.Debtor == collectorAccount);
                        if (rest != null) l.Pay(rest.Id, rest.Outstanding, k, today);
                        break;
                    }
            }
            if (ev.Category != "noncompliance" || ev.LegitimacyDelta != 0)
            {
                tax.LastEventDay.TryGetValue("_notify", out var lastNote);
                if (today - lastNote >= 2) { tax.LastEventDay["_notify"] = today; string key = "WFL_Tr_Event_" + ev.Id; if (key.CanTranslate()) Messages.Message(key.Translate(), collector, ev.Category == "positive" ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, false); }
            }
        }
    }
}
