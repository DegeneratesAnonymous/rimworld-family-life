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
    /// What happens when a Tax Collector really visits a household (the framework's talk job ended). The outcome comes from the people involved: relationship, the household's opinion of the
    /// collector, mood, Social, Melee, traits, what the payer actually holds, earlier refusals and the community's trust. A dishonest or lenient moment can change what is paid, but only through the
    /// ledger: money that moves is recorded as it really moved, and money that was waived or diverted is remembered so it can be found and recovered.
    /// </summary>
    internal static class TreasuryTaxVisit
    {
        public static void OnCollectionVisit(Pawn collector, Pawn payer, int debtId, int now)
        {
            var c = FamilyComponent.Current; if (c == null) return;
            var r = c.Registry; var l = r.Ledger(); var tax = r.Tax(); int today = TreasuryService.Today();
            var debt = l.DebtById(debtId); if (debt == null || !debt.Active || debt.Kind != DebtKind.Tax) return;
            if (!TaxAuthority.MayCollect(TreasuryService.ActiveCollectors(r)) || !r.Assignments().IsAssigned(collector.thingIDNumber, TreasuryRoles.TaxCollector)) return;
            string collectorAccount = TreasuryService.PersonAccount(r, collector.thingIDNumber);
            int owed = debt.Installment > 0 ? Math.Min(debt.Installment, debt.Outstanding) : debt.Outstanding;
            tax.Refusals.TryGetValue(debt.Debtor, out int prior);
            bool abrasive = collector.story?.traits?.HasTrait(TraitDefOf.Abrasive) == true, greedyC = collector.story?.traits?.HasTrait(TraitDefOf.Greedy) == true;
            int melee = collector.skills?.GetSkill(SkillDefOf.Melee).Level ?? 0, social = collector.skills?.GetSkill(SkillDefOf.Social).Level ?? 0;
            var ctx = new CollectionContext
            {
                Owed = owed, PayerBalance = l.Balance(debt.Debtor), PayerLiabilities = Math.Max(0, l.Liabilities(debt.Debtor) - debt.Outstanding),
                CollectorSocial = social, CollectorMelee = melee,
                Opinion = payer.relations?.OpinionOf(collector) ?? 0, FamilyOpinion = HouseholdOpinion(r, c, payer, collector), PayerMood = payer.needs?.mood?.CurLevelPercentage ?? .5f, PriorRefusals = prior, Legitimacy = tax.Legitimacy,
                CollectorKind = collector.story?.traits?.HasTrait(TraitDefOf.Kind) == true, CollectorAbrasive = abrasive, PayerGenerous = payer.story?.traits?.HasTrait(TraitDefOf.Kind) == true,
                Approach = prior >= 3 && abrasive && melee >= 8 ? Approach.Intimidate : prior >= 2 ? Approach.Firm : Approach.Persuade
            };
            var result = Collection.Resolve(ctx, Rand.Value);
            bool sameHouse = r.GetPerson(collector.thingIDNumber)?.HouseholdId > 0 && r.GetPerson(collector.thingIDNumber)?.HouseholdId == r.GetPerson(payer.thingIDNumber)?.HouseholdId;
            var trig = new TaxEvents.Trigger { Refused = result.Outcome == CollectionOutcome.Refused, Legitimacy = tax.Legitimacy, Day = today, CollectorOwnKin = sameHouse };
            if (greedyC) trig.Traits.Add("Greedy"); if (abrasive) trig.Traits.Add("Abrasive");
            var ev = TaxEvents.Pick(trig, tax.LastEventDay, () => Rand.Value);
            string key = "col:" + debt.Id + ":" + now; string payerAccount = debt.Debtor;
            int favourWaived = 0; bool embezzled = false;
            if (ev != null) tax.LastEventDay[ev.Id] = today;

            // Events that change what is paid, before it is applied.
            if (ev != null && (result.Outcome == CollectionOutcome.Paid || result.Outcome == CollectionOutcome.Partial) && result.Pay > 0)
            {
                switch (ev.Id)
                {
                    case "underreports_income":
                    case "hides_funds":
                        {
                            int hidden = Math.Max(1, result.Pay * (ev.Id == "hides_funds" ? 50 : 30) / 100);
                            if (Rand.Value < Mathf.Clamp01(.10f + social * .04f)) { tax.Legitimacy = Mathf.Min(100, tax.Legitimacy + 1); Messages.Message("WFL_Tr_Caught".Translate(payer.LabelShort), payer, MessageTypeDefOf.NeutralEvent, false); }
                            else { result.Pay -= hidden; result.Outcome = CollectionOutcome.Partial; tax.RecordDiversion(payerAccount, Ledger.CommunityId, hidden, today, "conceal"); }
                            break;
                        }
                    case "favours_friends":
                        if (ctx.Opinion >= 30 && result.Outcome == CollectionOutcome.Paid) { favourWaived = Math.Max(1, result.Pay / 4); result.Pay -= favourWaived; }
                        break;
                    case "embezzles": embezzled = result.Pay >= 4; break;
                }
            }
            if (ev != null && ev.Id == "delays_against_relatives" && sameHouse && result.Outcome != CollectionOutcome.Paid) debt.NextDueDay = today + 14;          // a relative is given more time than anyone else would be
            if (ev != null && ev.Id == "defers_hardship" && result.Outcome == CollectionOutcome.Hardship) debt.NextDueDay = today + 14;          // a fair collector lets someone with nothing pay later

            int paid;
            if (embezzled)
            {
                int collected = Math.Min(result.Pay, l.Balance(payerAccount)), diverted = Math.Max(1, collected / 4);
                if (collected > 0 && Misconduct.Divert(l, debt, collected, Math.Min(diverted, collected), collectorAccount, key, today) == LedgerResult.Ok) { tax.RecordDiversion(collectorAccount, Ledger.CommunityId, Math.Min(diverted, collected), today, "embezzle"); paid = collected; } else paid = 0;
            }
            else paid = Collection.Apply(l, debt, result, key, today);
            if (favourWaived > 0 && paid > 0) { l.RecordPayment(debt, favourWaived, today); tax.RecordDiversion(collectorAccount, Ledger.CommunityId, favourWaived, today, "favour"); }
            if (ev != null) ApplyEvent(c, tax, l, ev, collector, debt, collectorAccount, payerAccount, owed, today);

            if (result.Outcome == CollectionOutcome.Refused) tax.Refusals[debt.Debtor] = prior + 1; else if (result.Outcome == CollectionOutcome.Paid) tax.Refusals.Remove(debt.Debtor);
            tax.Legitimacy = Mathf.Clamp(tax.Legitimacy + result.LegitimacyDelta + (ev?.LegitimacyDelta ?? 0), 0, 100);
            GainMemory(payer, result.OpinionDelta < -4 ? "WFL_TaxPressured" : result.OpinionDelta > 0 ? "WFL_TaxPaidFairly" : null, collector);
            if (result.Outcome == CollectionOutcome.Refused) GainMemory(collector, "WFL_TaxRefusedAgainst", payer);
            CareerService.Report(c, collector, TreasuryRoles.Career, null, "treasury.collection", "collect|" + debt.Id + "|" + now, TreasuryXp.Points(FinancialOp.Collection, 0), now);
            Messages.Message("WFL_Tr_CollectionNote".Translate(payer.LabelShort, ("WFL_Tr_Outcome_" + result.Outcome).Translate()), payer, result.Outcome == CollectionOutcome.Refused ? MessageTypeDefOf.NegativeEvent : MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>How the payer's household feels about the collector: the mean opinion its other members hold. A family that dislikes the collector makes a visit harder for everyone in it.</summary>
        private static int HouseholdOpinion(FamilyRegistry r, FamilyComponent c, Pawn payer, Pawn collector)
        {
            var h = r.GetHousehold(r.GetPerson(payer.thingIDNumber)?.HouseholdId ?? 0); if (h == null) return 0;
            var others = h.ResidentIds.Where(i => i != payer.thingIDNumber && i != collector.thingIDNumber).Select(c.TryGetPawn).Where(p => p?.relations != null).ToList();
            return others.Count == 0 ? 0 : (int)others.Average(p => p.relations.OpinionOf(collector));
        }

        private static void GainMemory(Pawn pawn, string def, Pawn other)
        {
            var d = def == null ? null : DefDatabase<ThoughtDef>.GetNamedSilentFail(def); if (d == null || pawn?.needs?.mood?.thoughts?.memories == null) return;
            pawn.needs.mood.thoughts.memories.TryGainMemory(d, other);
        }

        /// <summary>The money part of an event, only through the ledger or the recorded diversions, plus trust and an occasional notice.</summary>
        private static void ApplyEvent(FamilyComponent c, TaxBook tax, Ledger l, TaxEventDef ev, Pawn collector, Debt debt, string collectorAccount, string payerAccount, int owed, int today)
        {
            string k = "ev:" + ev.Id + ":" + debt.Id + ":" + today;
            switch (ev.Id)
            {
                case "demands_more":
                    {
                        int extra = Math.Max(1, owed / 10);
                        if (l.Transfer(payerAccount, collectorAccount, extra, TxKind.Misconduct, k, today, "demanded beyond the tax") == LedgerResult.Ok) tax.RecordDiversion(collectorAccount, payerAccount, extra, today, "overcharge");
                        break;
                    }
                case "accepts_bribe":
                case "offers_bribe":
                    {
                        bool takes = collector.story?.traits?.HasTrait(TraitDefOf.Greedy) == true || ev.Id == "accepts_bribe";
                        if (!takes) { tax.Legitimacy = Mathf.Min(100, tax.Legitimacy + 3); break; }          // a refused bribe is to the collector's credit
                        int bribe = Math.Max(1, owed / 10);
                        if (l.Transfer(payerAccount, collectorAccount, bribe, TxKind.Misconduct, k, today, "bribe") == LedgerResult.Ok)
                        { int waived = Math.Min(debt.Outstanding, bribe * 2); l.RecordPayment(debt, waived, today); tax.RecordDiversion(collectorAccount, Ledger.CommunityId, waived, today, "bribe"); }
                        break;
                    }
                case "reduces_own_taxes":
                    {
                        string house = Ledger.IdFor(AccountKind.Household, c.Registry.GetPerson(collector.thingIDNumber)?.HouseholdId ?? 0);
                        var own = l.Debts.FirstOrDefault(x => x.Active && x.Kind == DebtKind.Tax && (x.Debtor == collectorAccount || x.Debtor == house));
                        if (own != null) { int cut = own.Outstanding / 2; l.RecordPayment(own, cut, today); tax.RecordDiversion(own.Debtor, Ledger.CommunityId, cut, today, "own taxes"); }
                        break;
                    }
                case "reports_missing_funds":
                case "corruption_accusation":
                    tax.DiscoverFirst(l, today); break;          // somebody finds what was taken, and it is now owed back
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
