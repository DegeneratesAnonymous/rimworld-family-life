using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using RimWorld.Planet;
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

        /// <summary>
        /// All the silver the colony physically has, wherever it is: on the ground and in storage on its home maps, in colonists' pockets and hands, and in its caravans. Counting only stored silver would make
        /// a caravan trip look like theft. Silver held by anyone else, or in a transport pod in flight, is not counted (so a delivery in flight shows as a temporary shortfall, which is never written off
        /// until it has lasted several days).
        /// </summary>
        public static int PhysicalSilver(Map unused = null)
        {
            long total = 0;
            foreach (var m in Find.Maps)
            {
                if (!m.IsPlayerHome) continue;
                foreach (var t in m.listerThings.ThingsOfDef(ThingDefOf.Silver)) total += t.stackCount;
                foreach (var p in m.mapPawns.PawnsInFaction(Faction.OfPlayer))
                {
                    total += p.inventory?.innerContainer?.TotalStackCountOfDef(ThingDefOf.Silver) ?? 0;
                    if (p.carryTracker?.CarriedThing?.def == ThingDefOf.Silver) total += p.carryTracker.CarriedThing.stackCount;
                }
            }
            foreach (var caravan in Find.WorldObjects.Caravans)
                if (caravan.IsPlayerControlled) foreach (var t in CaravanInventoryUtility.AllInventoryItems(caravan)) if (t.def == ThingDefOf.Silver) total += t.stackCount;
            return (int)Math.Min(int.MaxValue, total);
        }

        // ------------------------------------------------------------------ income

        /// <summary>Silver arrived from a quest reward. Shared once (the key is the quest part's own), by the configured policy.</summary>
        public static void OnSilver(int amount, string key)
        {
            var c = FamilyComponent.Current; if (c == null || amount <= 0) return;
            var r = c.Registry; var st = r.Treasury();
            st.LastIncomeDay = Today();
            var result = Income.DistributeReward(r.Ledger(), amount, Residents(r), new IncomePolicy { CommunityPercent = st.CommunityPercent }, key, Today());
            if (result == LedgerResult.Ok) { Messages.Message("WFL_Tr_RewardShared".Translate(amount, r.Ledger().Balance(Ledger.CommunityId)), MessageTypeDefOf.NeutralEvent, false); TreasuryOffice.Add(c, "reward|" + key, FinancialOp.RewardDistribution); }
        }

        /// <summary>A completed trade: purchases leave the accounts treasury-first, sales arrive by owner or by the community policy. Settled once under the trade's own id.</summary>
        public static void OnTrade(TradeReport report)
        {
            var c = FamilyComponent.Current; if (c == null || report == null) return;
            var r = c.Registry; var l = r.Ledger(); var st = r.Treasury();
            int cost = report.Lines.Where(x => !x.Sold).Sum(x => x.Count * x.UnitPrice);
            var plan = cost > 0 ? Trade.Plan(l, cost, report.ColonySilverBefore) : null;
            var sales = report.Lines.Where(x => x.Sold).Select(x => new Income.Sale { Proceeds = x.Count * x.UnitPrice, OwnerAccount = x.OwnerPawn != null && r.GetPerson(x.OwnerPawn.thingIDNumber)?.InColony == true ? PersonAccount(r, x.OwnerPawn.thingIDNumber) : null }).ToList();
            st.LastIncomeDay = Today();
            var res = Trade.Settle(l, report.Id, Today(), plan, sales, Residents(r), new IncomePolicy { CommunityPercent = st.CommunityPercent, OwnerFeePercent = st.OwnerFeePercent });
            if (res == LedgerResult.Ok && plan != null && plan.Legs.Count > 0) TreasuryOffice.Add(c, "trade|" + report.Id, FinancialOp.PooledTrade);
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

        // ------------------------------------------------------------------ ordinary labour

        public static void OnLabour(LabourReport rep)
        {
            var c = FamilyComponent.Current; if (c == null || rep?.Pawn == null) return;
            TreasuryOffice.OnJobFinished(rep);
            var r = c.Registry; var person = r.GetPerson(rep.Pawn.thingIDNumber); if (person == null || !person.InColony) return;
            var facts = new JobFacts { JobDef = rep.JobDef, WorkType = rep.WorkType, RecipeSkill = rep.RecipeSkill, JobId = rep.JobId, ThingId = rep.ThingId, HaulToStorage = rep.HaulToStorage, WorkAmount = rep.WorkAmount };
            string cat = Labour.CategoryOf(facts); if (cat == null) return;
            int day = Today();
            r.Payroll().Accrue(rep.Pawn.thingIDNumber, Labour.Role(cat), Labour.Units(facts, cat), day, Labour.Key(facts, cat, day));
        }

        private static readonly Dictionary<int, float> researchCarry = new Dictionary<int, float>();
        private static readonly Dictionary<int, int> researchUnits = new Dictionary<int, int>();

        /// <summary>Research is continuous, so it pays per amount actually performed (one unit per <see cref="Labour.ResearchPointsPerUnit"/> points), never for starting or for time spent not researching.</summary>
        public static void OnResearch(Pawn pawn, float performed)
        {
            var c = FamilyComponent.Current; if (c == null || pawn == null) return;
            var r = c.Registry; if (r.GetPerson(pawn.thingIDNumber)?.InColony != true) return;
            researchCarry.TryGetValue(pawn.thingIDNumber, out float carry);
            int units = Labour.ResearchUnits(ref carry, performed); researchCarry[pawn.thingIDNumber] = carry;
            if (units <= 0) return;
            int day = Today(); researchUnits.TryGetValue(pawn.thingIDNumber, out int n);
            for (int i = 0; i < units; i++) r.Payroll().Accrue(pawn.thingIDNumber, Labour.Role("research"), 1, day, "r:" + pawn.thingIDNumber + ":" + day + ":" + (++n));
            researchUnits[pawn.thingIDNumber] = n;
        }

        internal static void ResetLabour() { researchCarry.Clear(); researchUnits.Clear(); }

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
                TreasuryOffice.Add(c, "payday|" + rep.Payday, FinancialOp.Payday);
            }
            Estates(r, l, today);
            l.Prune(today);
        }

        /// <summary>
        /// Reconciles the claims with the silver in storage through <see cref="SilverReconciler"/>: unclaimed silver is adopted by the community only once it has stayed a day (or once, when the module
        /// first runs in a game, with a notice), and a shortfall is written off from the community's own account only after several days. Personal and household money is never erased.
        /// </summary>
        private static void Reconcile(Ledger l, TreasuryState st, int physical, int now, int today)
        {
            var d = SilverReconciler.Decide(st, l.TotalClaims, physical, today, st.AdoptSurplus);
            if (d.Action == ReconcileAction.AdoptSurplus && d.Amount > 0 && l.Receive(Ledger.CommunityId, d.Amount, TxKind.Correction, "recon:" + now, today, d.Initial ? "silver already in storage when the treasury began" : "unclaimed silver in storage") == LedgerResult.Ok && d.Initial)
                Messages.Message("WFL_Tr_Adopted".Translate(d.Amount), MessageTypeDefOf.NeutralEvent, false);
            l.SetPhysical(physical);          // claims above the silver that exists are unbacked: ownership is kept, spending power is reduced pro rata
            if (l.Unbacked > 0 && today - st.NoticeDay >= 5) { st.NoticeDay = today; Messages.Message("WFL_Tr_Unbacked".Translate(l.Unbacked), MessageTypeDefOf.NegativeEvent, false); }
            else if (l.Unbacked == 0) st.NoticeDay = 0;
        }

        // ------------------------------------------------------------------ Family Life: households and death

        /// <summary>
        /// Family Life decides who lives where and who is kin; the ledger owns the money. Someone leaving a household takes an equal share of its silver and debts to wherever they went (their new
        /// household if they joined one in the same sweep, otherwise their own account), so the totals never change and a caravan party's household account is not swept away. A death settles the
        /// person's own account to kin. Every step has its own key, so Family Life reconciling again cannot repeat it.
        /// </summary>
        public static void OnFamilyEvents(FamilyComponent c, IList<FamilyEvent> events, int now)
        {
            var r = c.Registry; var l = r.Ledger(); int today = Today();
            var died = new HashSet<int>(events.Where(e => e.Kind == FamilyEventKind.Died).Select(e => e.A));
            var left = events.Where(e => e.Kind == FamilyEventKind.HouseholdLeft && !died.Contains(e.A) && r.GetPerson(e.A)?.Presence != Presence.Dead).ToList();
            foreach (var group in left.GroupBy(e => e.B))
            {
                int oldH = group.Key; var house = r.GetHousehold(oldH);
                var residents = house?.ResidentIds.Where(id => r.GetPerson(id)?.Presence != Presence.Dead).ToList() ?? new List<int>();
                var leavers = group.Select(e => new Leaver { Pawn = e.A, DestHousehold = events.FirstOrDefault(x => x.Kind == FamilyEventKind.HouseholdJoined && x.A == e.A)?.B ?? 0, Away = r.GetPerson(e.A)?.InColony != true }).ToList();
                bool divorce = leavers.Any(lv => events.Any(x => x.Kind == FamilyEventKind.PartnershipEnded && ((x.A == lv.Pawn && residents.Contains(x.B)) || (x.B == lv.Pawn && residents.Contains(x.A)))));
                HouseholdRules.OnLeave(l, oldH, residents.Count, leavers, divorce, "hl:" + oldH + ":" + group.Min(e => e.Tick), today);
            }
            foreach (var e in events.Where(x => x.Kind == FamilyEventKind.HouseholdJoined && !left.Any(lv => lv.A == x.A)))          // someone who was away joins a household again
            {
                int old = r.GetPerson(e.A)?.LastHouseholdId ?? 0; if (old == 0) continue;
                HouseholdRules.OnReturn(l, old, e.B, r.GetHousehold(old)?.ResidentIds.Count > 0, "hr:" + e.A + ":" + old + ":" + e.Tick, today);
            }
            foreach (int id in died) SettleDeath(r, l, id, today);
        }

        /// <summary>Heirs in Family Life's own order: spouses, then children, then parents, then siblings, then nobody (the community). A juvenile heir's share goes to their household.</summary>
        internal static List<string> HeirAccounts(FamilyRegistry r, int deceased)
        {
            var p = r.GetPerson(deceased); if (p == null) return new List<string>();
            Func<IEnumerable<int>, List<string>> accounts = ids => ids.Select(r.GetPerson).Where(x => x != null && x.InColony && x.Id != deceased).Select(x => x.HouseholdId > 0 ? Ledger.IdFor(AccountKind.Household, x.HouseholdId) : Ledger.IdFor(AccountKind.Person, x.Id)).Distinct().ToList();
            var tier = accounts(p.SpouseIds);
            if (tier.Count == 0) tier = accounts(r.ChildrenOf(deceased));
            if (tier.Count == 0) tier = accounts(p.ParentIds);
            if (tier.Count == 0) tier = accounts(r.SiblingsOf(deceased));
            return tier;
        }

        private static void SettleDeath(FamilyRegistry r, Ledger l, int id, int today)
        {
            string acct = Ledger.IdFor(AccountKind.Person, id); if (l.Get(acct) == null || l.Get(acct).Closed) return;
            var heirs = HeirAccounts(r, id);
            foreach (var h in heirs) { var a = l.Get(h); if (a == null) l.Ensure(h.StartsWith("h:") ? AccountKind.Household : AccountKind.Person, int.Parse(h.Substring(2))); }
            l.SettleEstate(acct, heirs, "estate:" + acct, today);
        }

        /// <summary>The safety net for a death the event stream missed (a load, a mod that was off): every person recorded dead who still has an open account settles once, by the same rule.</summary>
        private static void Estates(FamilyRegistry r, Ledger l, int today)
        {
            foreach (var a in l.Accounts.Where(x => !x.Closed && x.Kind == AccountKind.Person).ToList())
                if (r.GetPerson(a.Owner)?.Presence == Presence.Dead) SettleDeath(r, l, a.Owner, today);
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
    }
}
