using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class TreasuryLedgerTests
    {
        internal static Ledger Funded(params (AccountKind kind, int owner, int silver)[] accounts)
        {
            var l = new Ledger();
            foreach (var (kind, owner, silver) in accounts) { var a = l.Ensure(kind, owner); if (silver > 0) l.Receive(a.Id, silver, TxKind.Income, "seed:" + a.Id, 0); }
            return l;
        }
        private static string P(int id) => Ledger.IdFor(AccountKind.Person, id);
        private static string H(int id) => Ledger.IdFor(AccountKind.Household, id);

        [Fact] public void Every_transfer_conserves_value_and_never_goes_negative()
        {
            var l = Funded((AccountKind.Person, 1, 100), (AccountKind.Household, 7, 40));
            var rng = new Random(5);
            for (int i = 0; i < 2000; i++)
            {
                var ids = l.Accounts.Select(a => a.Id).ToArray(); string from = ids[rng.Next(ids.Length)], to = ids[rng.Next(ids.Length)]; int amt = rng.Next(1, 60);
                if (i % 7 == 0) l.Receive(to, rng.Next(1, 30), TxKind.Income, "in" + i, i); else if (i % 11 == 0) l.Spend(from, rng.Next(1, 30), TxKind.Spend, "out" + i, i); else l.Transfer(from, to, amt, TxKind.Transfer, "t" + i, i);
                Assert.True(l.Conserved); Assert.All(l.Accounts, a => Assert.True(a.Balance >= 0));
            }
        }

        [Fact] public void A_transfer_larger_than_the_balance_is_refused_whole_and_changes_nothing()
        {
            var l = Funded((AccountKind.Person, 1, 10), (AccountKind.Person, 2, 0));
            Assert.Equal(LedgerResult.Insufficient, l.Transfer(P(1), P(2), 11, TxKind.Transfer, "k", 1));
            Assert.Equal(10, l.Balance(P(1))); Assert.Equal(0, l.Balance(P(2))); Assert.False(l.Applied("k"));
        }

        [Fact] public void A_batch_with_one_bad_leg_applies_none_of_its_legs()
        {
            var l = Funded((AccountKind.Person, 1, 10), (AccountKind.Person, 2, 0));
            var r = l.Apply("b", 1, TxKind.Transfer, new[] { new Move(P(1), P(2), 5), new Move(P(1), P(2), 6) });
            Assert.Equal(LedgerResult.Insufficient, r); Assert.Equal(10, l.Balance(P(1))); Assert.Equal(0, l.Balance(P(2)));
        }

        [Fact] public void The_same_key_is_applied_exactly_once()
        {
            var l = Funded((AccountKind.Person, 1, 10), (AccountKind.Person, 2, 0));
            Assert.Equal(LedgerResult.Ok, l.Transfer(P(1), P(2), 4, TxKind.Transfer, "same", 1));
            Assert.Equal(LedgerResult.Duplicate, l.Transfer(P(1), P(2), 4, TxKind.Transfer, "same", 1));
            Assert.Equal(4, l.Balance(P(2)));
        }

        [Fact] public void The_ledger_survives_a_save_and_load_exactly_including_its_idempotency_keys()
        {
            var l = Funded((AccountKind.Person, 1, 100), (AccountKind.Household, 3, 50));
            l.Transfer(P(1), H(3), 25, TxKind.Transfer, "x", 4, "gift, with | odd chars"); var d = l.OpenDebt(P(1), Ledger.CommunityId, DebtKind.Tax, 20, 9, "tax 3"); l.Pay(d.Id, 5, "pay1", 5);
            var back = Ledger.Decode(new StateReader(l.Encode()));
            Assert.Equal(l.Encode(), back.Encode()); Assert.True(back.Conserved);
            Assert.Equal(LedgerResult.Duplicate, back.Transfer(P(1), H(3), 25, TxKind.Transfer, "x", 4));
            Assert.Equal(15, back.DebtById(d.Id).Outstanding);
        }

        [Fact] public void Liabilities_are_kept_apart_from_balances_and_a_poor_household_does_not_acquire_debt()
        {
            var l = Funded((AccountKind.Household, 3, 0));
            Assert.Equal(0, l.Liabilities(H(3))); Assert.Empty(l.Debts);                  // zero silver is not debt
            var d = l.OpenDebt(H(3), Ledger.CommunityId, DebtKind.Tax, 30, 5, "tax");
            Assert.Equal(30, l.Liabilities(H(3))); Assert.Equal(0, l.Balance(H(3)));
            Assert.Equal(0, l.Pay(d.Id, 30, "p", 6));                                      // nothing to pay with: nothing moves, the debt stays
            Assert.Equal(30, l.DebtById(d.Id).Outstanding);
        }

        [Fact] public void A_household_split_moves_silver_and_listed_debts_once_without_duplication()
        {
            var l = Funded((AccountKind.Household, 1, 100)); l.Ensure(AccountKind.Household, 2);
            var d = l.OpenDebt(H(1), Ledger.CommunityId, DebtKind.Tax, 40, 9, "t");
            Assert.Equal(LedgerResult.Ok, l.Split(H(1), H(2), 30, new[] { d.Id }, "split1", 3));
            Assert.Equal(70, l.Balance(H(1))); Assert.Equal(30, l.Balance(H(2))); Assert.Equal(H(2), l.DebtById(d.Id).Debtor); Assert.Single(l.Debts);
            Assert.Equal(LedgerResult.Duplicate, l.Split(H(1), H(2), 30, new[] { d.Id }, "split1", 3)); Assert.Equal(30, l.Balance(H(2))); Assert.True(l.Conserved);
        }

        [Fact] public void A_death_settles_debts_first_then_shares_the_rest_among_heirs_and_conserves_value()
        {
            var l = Funded((AccountKind.Person, 1, 100), (AccountKind.Person, 2, 0), (AccountKind.Person, 3, 0));
            var d1 = l.OpenDebt(P(1), Ledger.CommunityId, DebtKind.Tax, 30, 5, "t"); var d2 = l.OpenDebt(P(1), P(3), DebtKind.Loan, 500, 8, "loan");
            Assert.Equal(LedgerResult.Ok, l.SettleEstate(P(1), new[] { P(2), P(3) }, "die1", 10));
            Assert.Equal(DebtStatus.Paid, l.DebtById(d1.Id).Status); Assert.True(l.DebtById(d2.Id).Active); Assert.Equal(P(2), l.DebtById(d2.Id).Debtor); Assert.Equal(430, l.DebtById(d2.Id).Outstanding);          // the unpaid part of the loan passes to the first heir; it is never erased
            Assert.True(l.Get(P(1)).Closed); Assert.Equal(0, l.Balance(P(1))); Assert.True(l.Conserved);
            Assert.Equal(LedgerResult.Duplicate, l.SettleEstate(P(1), new[] { P(2) }, "die1", 10));
        }

        [Fact] public void A_closed_account_cannot_be_used()
        {
            var l = Funded((AccountKind.Person, 1, 10), (AccountKind.Person, 2, 0)); l.SettleEstate(P(1), new[] { P(2) }, "e", 1);
            Assert.Equal(LedgerResult.Closed, l.Transfer(P(1), P(2), 1, TxKind.Transfer, "z", 2));
        }
    }

    public class TreasuryIncomeTests
    {
        private static List<Resident> Residents() => new List<Resident> { new Resident(1, 10), new Resident(2, 10), new Resident(3, 0), new Resident(4, 20) };

        [Fact] public void A_reward_is_shared_exactly_once_with_the_community_share_and_the_rest_equal_per_resident()
        {
            var l = new Ledger(); var pol = new IncomePolicy { CommunityPercent = 25 };
            Assert.Equal(LedgerResult.Ok, Income.DistributeReward(l, 1000, Residents(), pol, "quest:7", 3));
            Assert.Equal(LedgerResult.Duplicate, Income.DistributeReward(l, 1000, Residents(), pol, "quest:7", 3));
            Assert.Equal(252, l.Balance(Ledger.CommunityId)); Assert.Equal(1000, l.TotalClaims); Assert.True(l.Conserved);          // 25% = 250 plus the 2 silver left over from sharing 750 four ways
            Assert.Equal(374, l.Balance("h:10")); Assert.Equal(187, l.Balance("p:3")); Assert.Equal(187, l.Balance("h:20"));          // the household holds the combined shares of its two members
        }

        [Fact] public void Odd_silver_stays_with_the_community_and_nothing_is_lost_or_invented()
        {
            var l = new Ledger(); Income.DistributeReward(l, 1001, Residents(), new IncomePolicy { CommunityPercent = 25 }, "q", 1);
            Assert.Equal(1001, l.TotalClaims); Assert.True(l.Conserved);
        }

        [Fact] public void Children_keep_their_attribution_on_the_household_credit()
        {
            var l = new Ledger(); Income.DistributeReward(l, 400, Residents(), new IncomePolicy { CommunityPercent = 0 }, "q", 1);
            Assert.Contains(l.History, t => t.To == "h:10" && t.Memo == "share p:1"); Assert.Contains(l.History, t => t.To == "h:10" && t.Memo == "share p:2");
        }

        [Fact] public void A_reward_with_no_residents_goes_to_the_community()
        {
            var l = new Ledger(); Income.DistributeReward(l, 100, new List<Resident>(), new IncomePolicy(), "q", 1); Assert.Equal(100, l.Balance(Ledger.CommunityId));
        }

        [Fact] public void An_owned_item_pays_its_owner_and_a_communal_item_returns_its_cost_before_profit_is_shared()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Person, 1, 0)); var pol = new IncomePolicy { CommunityPercent = 50 };
            var moves = Income.SaleMoves(l, new[] { new Income.Sale { Proceeds = 100, OwnerAccount = "p:1" }, new Income.Sale { Proceeds = 300, CostBasis = 200 } }, new List<Resident> { new Resident(1, 0) }, pol, "sale");
            Assert.Equal(LedgerResult.Ok, l.Apply("s", 1, TxKind.Trade, moves));
            Assert.Equal(100 + 50, l.Balance("p:1"));                                       // the owner's sale + half of the 100 profit
            Assert.Equal(200 + 50, l.Balance(Ledger.CommunityId)); Assert.True(l.Conserved);
        }
    }

    public class TreasuryTradeTests
    {
        [Fact] public void The_treasury_pays_first_then_other_accounts_in_proportion_to_what_they_hold()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 100), (AccountKind.Person, 1, 60), (AccountKind.Household, 2, 120));
            var plan = Trade.Plan(l, 280, 10000);
            Assert.True(plan.Ok); Assert.Equal(100, plan.FromCommunity);
            Assert.Equal(60, plan.Legs.Single(x => x.Key == "p:1").Value); Assert.Equal(120, plan.Legs.Single(x => x.Key == "h:2").Value);
            var partial = Trade.Plan(l, 190, 10000);          // 100 from the treasury, 90 split 1:2
            Assert.Equal(30, partial.Legs.Single(x => x.Key == "p:1").Value); Assert.Equal(60, partial.Legs.Single(x => x.Key == "h:2").Value);
        }

        [Fact] public void Rounding_never_takes_more_than_an_account_holds_and_always_sums_to_the_cost()
        {
            var rng = new Random(2);
            for (int n = 0; n < 300; n++)
            {
                var l = new Ledger(); int total = 0;
                for (int i = 1; i <= rng.Next(1, 8); i++) { int s = rng.Next(0, 90); total += s; l.Ensure(AccountKind.Person, i); if (s > 0) l.Receive("p:" + i, s, TxKind.Income, "s" + i, 0); }
                int cost = rng.Next(0, total + 1); var plan = Trade.Plan(l, cost, total);
                Assert.True(plan.Ok); Assert.Equal(cost, plan.Legs.Sum(x => x.Value) + plan.Unattributed);
                foreach (var leg in plan.Legs) Assert.True(leg.Value <= l.Balance(leg.Key));
            }
        }

        [Fact] public void A_cancelled_trade_changes_nothing_and_a_completed_one_settles_once()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 50), (AccountKind.Person, 1, 50)); string before = l.Encode();
            var plan = Trade.Plan(l, 70, 1000);          // planning alone writes nothing
            Assert.Equal(before, l.Encode());
            Assert.Equal(LedgerResult.Ok, Trade.Settle(l, "t1", 4, plan, null, null, null));
            Assert.Equal(LedgerResult.Duplicate, Trade.Settle(l, "t1", 4, plan, null, null, null));
            Assert.Equal(0, l.Balance(Ledger.CommunityId)); Assert.Equal(30, l.Balance("p:1")); Assert.True(l.Conserved); Assert.Equal(30, l.TotalClaims);
        }

        [Fact] public void A_purchase_larger_than_the_silver_actually_present_is_refused_whatever_the_accounts_say()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 500));          // plenty of claims, but only 50 silver is in the caravan
            Assert.False(Trade.Plan(l, 200, 50).Ok);
        }

        [Fact] public void A_cost_beyond_every_claim_is_paid_from_unattributed_silver_without_inventing_account_money()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 20), (AccountKind.Person, 1, 10));
            var plan = Trade.Plan(l, 100, 100); Assert.True(plan.Ok); Assert.Equal(70, plan.Unattributed);
            Trade.Settle(l, "t", 1, plan, null, null, null); Assert.Equal(0, l.TotalClaims); Assert.True(l.Conserved);
        }

        [Fact] public void Mixed_purchases_and_sales_settle_together_in_one_batch()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 100), (AccountKind.Person, 1, 0));
            var plan = Trade.Plan(l, 60, 1000);
            var res = Trade.Settle(l, "mix", 2, plan, new[] { new Income.Sale { Proceeds = 80, OwnerAccount = "p:1" } }, new List<Resident>(), new IncomePolicy());
            Assert.Equal(LedgerResult.Ok, res); Assert.Equal(40, l.Balance(Ledger.CommunityId)); Assert.Equal(80, l.Balance("p:1")); Assert.True(l.Conserved);
        }

        [Fact] public void A_caravan_in_another_settlement_can_only_spend_silver_that_is_really_there()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 300), (AccountKind.Person, 5, 200));
            Assert.True(Trade.Plan(l, 40, 40).Ok); Assert.False(Trade.Plan(l, 41, 40).Ok);          // the caravan carries 40 silver, so 40 is the limit
        }
    }

    public class TreasuryPayrollTests
    {
        [Fact] public void Paydays_are_the_first_and_eighth_of_each_quadrum_with_rollover_between_quadrums_and_years()
        {
            Assert.Equal(new[] { 0, 7, 15, 22, 30, 37, 45, 52 }, Enumerable.Range(0, 60).Where(PayCalendar.IsPayday).Take(8).ToArray());
            Assert.Equal(8, Enumerable.Range(0, 60).Count(PayCalendar.IsPayday));                                            // eight paydays a year
            Assert.Equal(7, PayCalendar.PaydayFor(0)); Assert.Equal(15, PayCalendar.PaydayFor(7));          // work on the 8th is paid on the next payday, the 1st
            Assert.Equal(15, PayCalendar.PaydayFor(14)); Assert.Equal(60, PayCalendar.PaydayFor(52));          // year rollover: the 8th-15th of the last quadrum are paid on the 1st of the next year
            Assert.Equal(1, PayCalendar.Year(60)); Assert.Equal(0, PayCalendar.Quadrum(60)); Assert.Equal(1, PayCalendar.DayOfQuadrum(60));
        }

        private static (Ledger, PayrollBook) Set(int treasury = 1000) { var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, treasury)); return (l, new PayrollBook()); }

        [Fact] public void Work_is_paid_on_payday_from_actual_treasury_funds_and_never_twice()
        {
            var (l, b) = Set(); b.SettleDue(l, 0, 10000);          // a payroll that starts now
            b.Accrue(1, "wils.care.doctor", 3, 2, "c1"); b.Accrue(2, "wils.education.teacher", 2, 5, "c2");
            var reports = b.SettleDue(l, 7, 10000);
            Assert.Single(reports); Assert.Equal(30, reports[0].TotalPaid); Assert.Equal(18, l.Balance("p:1")); Assert.Equal(12, l.Balance("p:2"));
            Assert.Empty(b.SettleDue(l, 7, 10000)); Assert.Empty(b.SettleDue(l, 10, 10000));          // the same period never settles again
            Assert.Equal(970, l.Balance(Ledger.CommunityId)); Assert.True(l.Conserved);
        }

        [Fact] public void Settlement_does_not_repeat_after_a_save_and_load()
        {
            var (l, b) = Set(); b.SettleDue(l, 0, 9999); b.Accrue(1, "r", 2, 3, "k");
            var l2 = Ledger.Decode(new StateReader(l.Encode())); var b2 = PayrollBook.Decode(new StateReader(b.Encode()));
            b.SettleDue(l, 7, 9999);
            var again = b2.SettleDue(l2, 7, 9999); Assert.Equal(12, l2.Balance("p:1"));          // paid once in the reloaded copy
            var reloaded = Ledger.Decode(new StateReader(l2.Encode())); var b3 = PayrollBook.Decode(new StateReader(b2.Encode()));
            Assert.Empty(b3.SettleDue(reloaded, 7, 9999)); Assert.Equal(12, reloaded.Balance("p:1"));
        }

        [Fact] public void An_earning_work_key_pays_once_and_daily_limits_stop_farming()
        {
            var b = new PayrollBook(); b.Policy.Rates["r"] = 5; b.Policy.DailyUnits["r"] = 4; b.Policy.PawnDailyCap = 100;
            Assert.Equal(15, b.Accrue(1, "r", 3, 1, "a")); Assert.Equal(0, b.Accrue(1, "r", 3, 1, "a"));          // same work: nothing
            Assert.Equal(5, b.Accrue(1, "r", 3, 1, "b"));                                                          // only one unit of the daily four remained
            Assert.Equal(0, b.Accrue(1, "r", 1, 1, "c"));
            Assert.Equal(20, b.PendingFor(1));
        }

        [Fact] public void A_pawn_with_several_qualifications_is_not_paid_beyond_one_daily_total()
        {
            var b = new PayrollBook(); b.Policy.PawnDailyCap = 30;
            b.Accrue(1, "a", 6, 1, "x"); b.Accrue(1, "b", 6, 1, "y");
            Assert.Equal(30, b.PendingFor(1));
        }

        [Fact] public void An_unpaid_role_earns_nothing()
        {
            var b = new PayrollBook(); b.Policy.Rates["vanilla.cooking"] = 0; Assert.Equal(0, b.Accrue(1, "vanilla.cooking", 3, 1, "k"));
        }

        [Fact] public void When_funds_are_short_everyone_gets_the_same_fraction_and_the_rest_stays_owed_as_a_wage_debt()
        {
            var (l, b) = Set(40); b.SettleDue(l, 0, 99999);
            b.Accrue(1, "r", 5, 1, "a"); b.Accrue(2, "r", 5, 2, "b");          // 30 each, 60 in all, 40 available
            var rep = b.SettleDue(l, 7, 99999)[0];
            Assert.Equal(40, rep.TotalPaid); Assert.Equal(20, l.Balance("p:1")); Assert.Equal(20, l.Balance("p:2")); Assert.Equal(0, l.Balance(Ledger.CommunityId));
            Assert.Equal(2, l.Debts.Count(d => d.Kind == DebtKind.Wage)); Assert.All(l.Debts, d => Assert.Equal(10, d.Outstanding)); Assert.True(l.Conserved);
        }

        [Fact] public void Unpaid_wages_remain_payable_after_missed_paydays_when_money_returns()
        {
            var (l, b) = Set(0); b.SettleDue(l, 0, 99999); b.Accrue(1, "r", 5, 1, "a");
            Assert.Equal(0, b.SettleDue(l, 7, 99999)[0].TotalPaid); Assert.Equal(30, l.Liabilities(Ledger.CommunityId));
            b.SettleDue(l, 15, 99999);          // another payday, still nothing in the treasury
            l.Receive(Ledger.CommunityId, 100, TxKind.Income, "quest", 16);
            var rep = b.SettleDue(l, 22, 99999).Last(r => r.TotalDue > 0);
            Assert.Equal(30, rep.TotalPaid); Assert.Equal(30, l.Balance("p:1")); Assert.Equal(0, l.Liabilities(Ledger.CommunityId)); Assert.True(l.Conserved);
        }

        [Fact] public void Payroll_never_pays_more_than_the_physical_silver_it_is_told_is_available()
        {
            var (l, b) = Set(1000); b.SettleDue(l, 0, 99999); b.Accrue(1, "r", 5, 1, "a");
            Assert.Equal(10, b.SettleDue(l, 7, 10)[0].TotalPaid); Assert.Equal(20, l.Liabilities(Ledger.CommunityId));
        }

        [Fact] public void Missed_paydays_after_a_long_absence_each_settle_once_in_order()
        {
            var (l, b) = Set(); b.SettleDue(l, 0, 99999); b.Accrue(1, "r", 1, 1, "a"); b.Accrue(1, "r", 1, 9, "b");
            var reports = b.SettleDue(l, 22, 99999);
            Assert.Equal(new[] { 7, 15, 22 }, reports.Select(r => r.Payday).ToArray()); Assert.Equal(12, l.Balance("p:1"));
        }

        [Fact] public void The_payroll_state_round_trips()
        {
            var (l, b) = Set(); b.Policy.Rates["x.y"] = 9; b.SettleDue(l, 0, 99); b.Accrue(3, "x.y", 2, 1, "k"); b.SettleDue(l, 7, 99);
            Assert.Equal(b.Encode(), PayrollBook.Decode(new StateReader(b.Encode())).Encode());
        }
    }

    public class TreasuryZoneAndValuablesTests
    {
        [Fact] public void A_zone_needs_an_enclosed_room_a_table_a_seat_and_silver_storage()
        {
            Assert.Equal(ZoneNeeds.Enclosed | ZoneNeeds.Table | ZoneNeeds.Seat | ZoneNeeds.SilverStorage, TreasuryRoom.Missing(new ZoneFacts()));
            var ok = new ZoneFacts { Cells = 12, Enclosed = true, Tables = 1, Seats = 1, SilverStorages = 1 };
            Assert.True(TreasuryRoom.Valid(ok));
            Assert.Equal(ZoneNeeds.Seat, TreasuryRoom.Missing(new ZoneFacts { Cells = 12, Enclosed = true, Tables = 1, SilverStorages = 1 }));
        }

        [Fact] public void Resizing_to_a_valid_or_invalid_shape_is_a_pure_recomputation_that_touches_no_account()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 500)); string before = l.Encode();
            TreasuryRoom.Valid(new ZoneFacts { Cells = 4 }); TreasuryRoom.Valid(new ZoneFacts { Cells = 20, Enclosed = true, Tables = 1, Seats = 2, SilverStorages = 2 });
            Assert.Equal(before, l.Encode());
        }

        [Fact] public void The_office_needs_both_a_valid_room_and_a_treasurer_but_accounts_do_not()
        {
            var ok = new ZoneFacts { Cells = 12, Enclosed = true, Tables = 1, Seats = 1, SilverStorages = 1 };
            Assert.False(TreasuryRoom.OfficeOpen(ok, false)); Assert.False(TreasuryRoom.OfficeOpen(new ZoneFacts(), true)); Assert.True(TreasuryRoom.OfficeOpen(ok, true));
        }

        [Fact] public void Only_silver_is_spendable_and_other_valuables_are_counted_at_estimated_value()
        {
            var reg = new ValuablesRegistry(); reg.Set("ModdedCrystal", false);
            var s = reg.Summarise(new[] { new ValuablesRegistry.Stack { DefName = "Silver", Count = 500 }, new ValuablesRegistry.Stack { DefName = "Gold", Count = 10, UnitValue = 10 }, new ValuablesRegistry.Stack { DefName = "ModdedCrystal", Count = 4, UnitValue = 25 },
                new ValuablesRegistry.Stack { DefName = "Painting", Count = 1, UnitValue = 900, IsArtOrHeirloom = true }, new ValuablesRegistry.Stack { DefName = "Steel", Count = 400, UnitValue = 2 } });
            Assert.Equal(500, s.LiquidSilver); Assert.Equal(100 + 100 + 900, s.EstimatedValuables); Assert.Equal(3, s.ValuableStacks);
        }

        [Fact] public void The_primary_zone_reference_and_valuables_round_trip_and_losing_the_zone_loses_no_account()
        {
            var st = new TreasuryState { PrimaryZoneId = 7, MapId = 2 }; st.Valuables.Set("Onyx", false); st.Valuables.Set("Platinum", false);
            var back = TreasuryState.Decode(new StateReader(st.Encode())); Assert.Equal(7, back.PrimaryZoneId); Assert.True(back.Valuables.Recognises("Platinum"));
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 321)); back.PrimaryZoneId = 0; Assert.Equal(321, l.Balance(Ledger.CommunityId));
        }
    }

    public class TreasuryTaxTests
    {
        [Fact] public void Formal_taxation_is_unavailable_until_a_collector_is_assigned_then_assessments_are_debts_on_income_only()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Household, 1, 0)); Income.DistributeReward(l, 400, new List<Resident> { new Resident(1, 1) }, new IncomePolicy { CommunityPercent = 0 }, "q", 1);
            var tax = new TaxBook { RatePercent = 10 };
            Assert.Null(tax.Assess(l, "h:1", 5, 0));                                         // locked: no collector
            var d = tax.Assess(l, "h:1", 5, 1);
            Assert.Equal(40, d.Original); Assert.Equal(DebtKind.Tax, d.Kind); Assert.Equal("h:1", d.Debtor); Assert.Equal(Ledger.CommunityId, d.Creditor);
            Assert.Null(tax.Assess(l, "h:1", 6, 1));                                         // no new income since: nothing owed
            l.Transfer("h:1", "p:9", 1, TxKind.Transfer, "t", 7); Assert.Null(tax.Assess(l, "h:1", 8, 1));          // a transfer is not income
        }

        [Fact] public void Losing_the_last_collector_keeps_policy_and_debts_but_stops_new_assessment_and_collection()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Household, 1, 100)); var tax = new TaxBook { RatePercent = 10 }; var d = l.OpenDebt("h:1", Ledger.CommunityId, DebtKind.Tax, 20, 9, "t");
            Assert.False(TaxAuthority.MayAssess(0)); Assert.False(TaxAuthority.MayCollect(0)); Assert.Equal(10, tax.RatePercent); Assert.Equal(20, l.DebtById(d.Id).Outstanding);
        }

        [Fact] public void Tax_collector_qualification_is_three_balanced_numbers()
        {
            Assert.Equal(7, TreasuryRoles.CollectorSocial); Assert.Equal(6, TreasuryRoles.CollectorMelee); Assert.Equal(120, TreasuryRoles.CollectorExperience);
        }

        private static CollectionContext Ctx(int owed = 100, int balance = 500) => new CollectionContext { Owed = owed, PayerBalance = balance, CollectorSocial = 10, Opinion = 20 };

        [Fact] public void A_payer_who_cannot_pay_is_in_hardship_not_refusing()
        {
            Assert.Equal(CollectionOutcome.Hardship, Collection.Resolve(Ctx(100, 0), .5f).Outcome);
        }

        [Fact] public void Collection_outcomes_cover_full_partial_plan_and_refusal_deterministically()
        {
            Assert.Equal(CollectionOutcome.Paid, Collection.Resolve(Ctx(), .01f).Outcome);
            Assert.Equal(CollectionOutcome.Partial, Collection.Resolve(Ctx(100, 60), .01f).Outcome);
            var plan = Collection.Resolve(Ctx(), .8f); Assert.Equal(CollectionOutcome.Plan, plan.Outcome); Assert.True(plan.Installment > 0);
            Assert.Equal(CollectionOutcome.Refused, Collection.Resolve(Ctx(), .999f).Outcome);
            Assert.Equal(Collection.Resolve(Ctx(), .4f).Outcome, Collection.Resolve(Ctx(), .4f).Outcome);
        }

        [Fact] public void Relationship_and_skill_help_and_intimidation_helps_compliance_but_costs_trust()
        {
            var friendly = Ctx(); friendly.Opinion = 80; var hostile = Ctx(); hostile.Opinion = -80;
            Assert.True(Collection.Resolve(friendly, 0).Chance > Collection.Resolve(hostile, 0).Chance);
            var firm = Ctx(); firm.CollectorMelee = 12; firm.Approach = Approach.Intimidate; var calm = Ctx(); calm.CollectorMelee = 12;
            var a = Collection.Resolve(firm, 0); var b = Collection.Resolve(calm, 0);
            Assert.True(a.Chance > b.Chance); Assert.True(a.LegitimacyDelta < b.LegitimacyDelta); Assert.True(a.OpinionDelta < b.OpinionDelta);
        }

        [Fact] public void Partial_payment_a_plan_and_refusal_move_only_what_was_really_paid()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Household, 1, 60)); var d = l.OpenDebt("h:1", Ledger.CommunityId, DebtKind.Tax, 100, 9, "t");
            Assert.Equal(30, Collection.Apply(l, d, new CollectionResult { Outcome = CollectionOutcome.Partial, Pay = 30 }, "c1", 3));
            Assert.Equal(70, d.Outstanding); Assert.Equal(30, l.Balance(Ledger.CommunityId));
            Assert.Equal(0, Collection.Apply(l, d, new CollectionResult { Outcome = CollectionOutcome.Plan, Installment = 10, EveryDays = 7 }, "c2", 4)); Assert.Equal(10, d.Installment);
            Assert.Equal(0, Collection.Apply(l, d, new CollectionResult { Outcome = CollectionOutcome.Refused }, "c3", 5)); Assert.Equal(70, d.Outstanding); Assert.True(l.Conserved);
        }

        [Fact] public void A_diverted_collection_credits_the_treasury_and_the_collector_with_what_really_moved()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Household, 1, 100), (AccountKind.Person, 5, 0)); var d = l.OpenDebt("h:1", Ledger.CommunityId, DebtKind.Tax, 100, 9, "t");
            Assert.Equal(LedgerResult.Ok, Misconduct.Divert(l, d, 100, 30, "p:5", "col1", 4));
            Assert.Equal(70, l.Balance(Ledger.CommunityId)); Assert.Equal(30, l.Balance("p:5")); Assert.Equal(0, l.Balance("h:1")); Assert.Equal(0, d.Outstanding); Assert.True(l.Conserved);
            Assert.Contains(l.History, t => t.Kind == TxKind.Misconduct && t.Tag.Contains("reported:100") && t.Tag.Contains("actual:70"));          // the record keeps both the claim and the truth
            Assert.Equal(LedgerResult.Duplicate, Misconduct.Divert(l, d, 100, 30, "p:5", "col1", 4));
            var rest = Misconduct.Restitution(l, "p:5", Ledger.CommunityId, 30, "embezzle", 9); Assert.Equal(30, l.Pay(rest.Id, 30, "rest", 10)); Assert.Equal(100, l.Balance(Ledger.CommunityId)); Assert.True(l.Conserved);
        }

        [Fact] public void Every_event_chance_is_bounded_grounded_and_on_cooldown_after_it_fires()
        {
            var t = new TaxEvents.Trigger { Legitimacy = 20, Day = 100, CollectorOwnKin = true, Refused = true }; t.Traits.Add("Greedy");
            foreach (var e in TaxEvents.All) Assert.InRange(TaxEvents.Chance(e, t, null), 0f, TaxEvents.MaxChance);
            var embezzle = TaxEvents.All.Single(e => e.Id == "embezzles"); Assert.True(TaxEvents.Chance(embezzle, t, null) > 0);
            Assert.Equal(0f, TaxEvents.Chance(embezzle, t, new Dictionary<string, int> { { "embezzles", 90 } }));
            t.Legitimacy = 90; Assert.Equal(0f, TaxEvents.Chance(embezzle, t, null));                     // an honest, trusted community does not see it
            Assert.Equal(0f, TaxEvents.Chance(TaxEvents.All.Single(e => e.Id == "refuses_disliked_collector"), new TaxEvents.Trigger { Refused = false }, null));
            Assert.True(TaxEvents.Chance(embezzle, new TaxEvents.Trigger { Legitimacy = 20, Day = 100, Traits = { "Greedy" } }, null) > TaxEvents.Chance(embezzle, new TaxEvents.Trigger { Legitimacy = 20, Day = 100 }, null));
        }

        [Fact] public void The_tax_book_round_trips()
        {
            var b = new TaxBook { RatePercent = 12, Legitimacy = 44, AutoRemit = true }; b.LastEventDay["embezzles"] = 90; b.Refusals["h:1"] = 2;
            Assert.Equal(b.Encode(), TaxBook.Decode(new StateReader(b.Encode())).Encode());
        }
    }

    public class TreasuryIsolationTests
    {
        [Fact] public void The_treasury_state_is_kept_untouched_in_a_save_without_the_module()
        {
            var r = new FamilyRegistry(); var l = r.Ledger(); l.Ensure(AccountKind.Person, 3); l.Receive("p:3", 77, TxKind.Income, "k", 1);
            var back = SaveSim.Reload(r);
            Assert.True(back.Shared.Has(Ledger.StateId)); Assert.Equal(77, back.Ledger().Balance("p:3"));          // only the owning module decodes it; the framework just carries it
        }
    }
}

namespace FamilyLife.Tests
{
    public class TreasuryFamilyIntegrationTests
    {
        private static string H(int id) => Ledger.IdFor(AccountKind.Household, id);
        private static string P(int id) => Ledger.IdFor(AccountKind.Person, id);

        [Fact] public void Leaving_a_household_moves_a_share_of_silver_and_debt_and_preserves_both_totals()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Household, 1, 300)); l.Ensure(AccountKind.Household, 2);
            l.OpenDebt(H(1), Ledger.CommunityId, DebtKind.Tax, 90, 5, "tax"); l.OpenDebt(Ledger.CommunityId, H(1), DebtKind.Wage, 30, 5, "owed to the house");
            long claims = l.TotalClaims; int liab = l.Debts.Where(d => d.Active).Sum(d => d.Outstanding);
            Assert.Equal(LedgerResult.Ok, l.TransferShare(H(1), H(2), 1, 3, "k1", 4));
            Assert.Equal(200, l.Balance(H(1))); Assert.Equal(100, l.Balance(H(2)));
            Assert.Equal(60, l.Liabilities(H(1))); Assert.Equal(30, l.Liabilities(H(2)));
            Assert.Equal(claims, l.TotalClaims); Assert.Equal(liab, l.Debts.Where(d => d.Active).Sum(d => d.Outstanding)); Assert.True(l.Conserved);
            Assert.Equal(LedgerResult.Duplicate, l.TransferShare(H(1), H(2), 1, 3, "k1", 4)); Assert.Equal(100, l.Balance(H(2)));          // Family Life reconciling again changes nothing
        }

        [Fact] public void The_last_person_to_leave_takes_everything_so_a_dissolved_household_keeps_nothing_behind()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Household, 1, 101)); l.OpenDebt(H(1), Ledger.CommunityId, DebtKind.Tax, 7, 5, "t");
            l.Ensure(AccountKind.Person, 9); l.TransferShare(H(1), P(9), 1, 1, "last", 3);
            Assert.Equal(0, l.Balance(H(1))); Assert.Equal(101, l.Balance(P(9))); Assert.Equal(0, l.Liabilities(H(1))); Assert.Equal(7, l.Liabilities(P(9)));
        }

        [Fact] public void Equal_sequential_shares_across_several_leavers_add_up_to_the_whole()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Household, 1, 100)); foreach (var p in new[] { 1, 2, 3, 4 }) l.Ensure(AccountKind.Person, p);
            l.TransferShare(H(1), P(1), 1, 4, "a", 1); l.TransferShare(H(1), P(2), 1, 3, "b", 1); l.TransferShare(H(1), P(3), 1, 2, "c", 1); l.TransferShare(H(1), P(4), 1, 1, "d", 1);
            Assert.Equal(new[] { 25, 25, 25, 25 }, new[] { 1, 2, 3, 4 }.Select(i => l.Balance(P(i))).ToArray()); Assert.Equal(0, l.Balance(H(1)));
        }

        [Fact] public void A_death_never_erases_what_the_dead_owed_and_heirs_receive_the_silver_once()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Person, 1, 50), (AccountKind.Household, 7, 0)); var debt = l.OpenDebt(P(1), Ledger.CommunityId, DebtKind.Loan, 200, 9, "loan");
            Assert.Equal(LedgerResult.Ok, l.SettleEstate(P(1), new[] { H(7) }, "e", 5)); Assert.Equal(LedgerResult.Duplicate, l.SettleEstate(P(1), new[] { H(7) }, "e", 5));
            Assert.Equal(0, l.Balance(H(7)));                                                    // the 50 went to the creditor first
            Assert.Equal(H(7), l.DebtById(debt.Id).Debtor); Assert.Equal(150, l.DebtById(debt.Id).Outstanding); Assert.True(l.Conserved);
        }

        [Fact] public void Money_with_no_heir_goes_to_the_community_and_a_private_debt_with_no_heir_falls_to_it_too()
        {
            var l = TreasuryLedgerTests.Funded((AccountKind.Person, 1, 40), (AccountKind.Person, 2, 0)); var d = l.OpenDebt(P(1), P(2), DebtKind.Loan, 100, 9, "loan");
            l.SettleEstate(P(1), new string[0], "e", 3);
            Assert.Equal(0, l.Balance(Ledger.CommunityId)); Assert.Equal(40, l.Balance(P(2))); Assert.Equal(Ledger.CommunityId, l.DebtById(d.Id).Debtor); Assert.Equal(60, l.DebtById(d.Id).Outstanding);
        }
    }

    public class SilverReconcilerTests
    {
        [Fact] public void An_existing_colonys_silver_is_recorded_once_for_the_community_and_never_distributed()
        {
            var st = new TreasuryState(); var d = SilverReconciler.Decide(st, 0, 800, 0, 100, true);
            Assert.Equal(ReconcileAction.AdoptSurplus, d.Action); Assert.Equal(800, d.Amount); Assert.True(d.Initial);
            Assert.Equal(ReconcileAction.None, SilverReconciler.Decide(st, 800, 800, 800, 101, true).Action);
        }

        [Fact] public void Later_unclaimed_silver_is_adopted_only_after_it_has_stayed_a_day_and_only_the_part_that_stayed()
        {
            var st = new TreasuryState { Initialized = true };
            Assert.Equal(ReconcileAction.None, SilverReconciler.Decide(st, 100, 160, 100, 10, true).Action);          // just appeared: maybe a quest drop landing
            var d = SilverReconciler.Decide(st, 100, 140, 100, 11, true);                                              // 40 of it stayed
            Assert.Equal(ReconcileAction.AdoptSurplus, d.Action); Assert.Equal(40, d.Amount);
        }

        [Fact] public void A_shortfall_is_written_off_only_after_days_only_from_the_community_and_never_from_personal_accounts()
        {
            var st = new TreasuryState { Initialized = true };
            Assert.Equal(ReconcileAction.None, SilverReconciler.Decide(st, 500, 300, 150, 10, true).Action);
            Assert.Equal(ReconcileAction.None, SilverReconciler.Decide(st, 500, 300, 150, 12, true).Action);
            var d = SilverReconciler.Decide(st, 500, 300, 150, 13, true);
            Assert.Equal(ReconcileAction.WriteOffLoss, d.Action); Assert.Equal(150, d.Amount); Assert.Equal(50, d.Discrepancy);          // 50 stays visible: personal claims are not erased to hide it
        }

        [Fact] public void A_recovered_shortfall_is_forgotten_and_a_written_off_loss_conserves_the_ledger()
        {
            var st = new TreasuryState { Initialized = true }; SilverReconciler.Decide(st, 500, 300, 150, 10, true);
            Assert.Equal(ReconcileAction.None, SilverReconciler.Decide(st, 500, 500, 150, 11, true).Action); Assert.Equal(0, st.ShortSinceDay);
            var l = TreasuryLedgerTests.Funded((AccountKind.Community, 0, 150), (AccountKind.Person, 1, 350)); Assert.Equal(LedgerResult.Ok, l.RecordLoss(Ledger.CommunityId, 150, "loss", 20, "stolen")); Assert.True(l.Conserved); Assert.Equal(350, l.TotalClaims);
        }
    }

    public class TreasuryAbsenceTests
    {
        [Fact] public void The_framework_attributes_the_treasury_payloads_to_the_absent_module_and_keeps_them()
        {
            var r = new FamilyRegistry(); r.Ledger().Receive(Ledger.CommunityId, 55, TxKind.Income, "k", 1); r.Payroll().Accrue(3, "x", 1, 1, "w"); r.Tax().RatePercent = 9;
            var back = SaveSim.Reload(r);
            var absent = AbsentModules.Find(back, id => id == ModuleCatalog.FrameworkId).FirstOrDefault(m => m.ModuleId == "wils.treasury");
            Assert.NotNull(absent); Assert.True(absent.Payloads.Count >= 3);
            var once = SaveSim.Reload(back); Assert.Equal(55, once.Ledger().Balance(Ledger.CommunityId)); Assert.Equal(9, once.Tax().RatePercent); Assert.Equal(6, once.Payroll().PendingFor(3));
        }
    }
}
