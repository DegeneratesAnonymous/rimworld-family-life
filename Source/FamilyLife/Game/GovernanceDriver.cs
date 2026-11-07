using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    /// <summary>
    /// Starts the real, physical side of duties, voting, schemes and jobs. Every pass looks at one house or one scheme at a time and does a
    /// handful of dictionary lookups; nothing here scans the map except the existing stock snapshot the supply planner already keeps.
    /// </summary>
    internal static class GovernanceDriver
    {
        private static int dutyRotate, crimeRotate, coupRotate;
        private static readonly Dictionary<int, int> nextCrimeCheck = new Dictionary<int, int>();
        private const int Day = ActivityTuning.TicksPerDay;

        internal static void Run(FamilyComponent c, int now)
        {
            var s = FamilyLifeMod.Settings;
            if (s != null && !s.governance) return;
            Step(() => Duties(c, now), "duties");
            Step(() => Elections(c, now), "elections");
            Step(() => OfficeDriver.Run(c, now), "community posts");
            Step(() => Crime(c, now), "house crime");
            Step(() => Coups(c, now), "schemes");
        }

        private static void Step(Action action, string name)
        {
            try { action(); }
            catch (Exception ex) { Log.Warning("[Wil's Family Life] Governance step '" + name + "' skipped: " + ex.Message); }
        }

        private static Pawn Spawned(FamilyComponent c, int id) { var p = c.TryGetPawn(id); return p != null && p.Spawned ? p : null; }

        // ------------------------------------------------------------------ steward duties

        private static void Duties(FamilyComponent c, int now)
        {
            var r = c.Registry;
            // Duties whose evidence arrives from other systems (a negotiation, a petition hearing, goods brought home) close when it does.
            foreach (var duty in r.StewardDuties.Where(d => d.Open && (d.Kind == StewardDutyKind.OrganizeCeremony || d.Kind == StewardDutyKind.Negotiate && d.TargetId>0 || d.Kind == StewardDutyKind.SupplyShortage || d.Kind == StewardDutyKind.ReceivePetition)).ToList())
                r.CompleteStewardDuty(duty.Id, 0, now);

            var houses = r.Lineages.Where(h => h.AbsorbedIntoId == 0).OrderBy(h => h.Id).ToList();
            if (houses.Count == 0) return;
            var house = houses[dutyRotate++ % houses.Count];
            if (r.StewardDuties.Any(d => d.Open && d.FamilyId == house.Id)) return;
            int guests = r.HouseContacts.Count(x => x.FamilyId == house.Id && x.Respect > 0f && !r.BelongsToFamily(r.GetPerson(x.PersonId), house.Id) && Spawned(c, x.PersonId) != null);
            if (!r.TryChooseDuty(house.Id, guests, out var kind, out int target)) return;
            var duty2 = r.BeginStewardDuty(house.Id, kind, target, id => Governance.Free(c, id), now);
            if (duty2 == null) return;
            var holder = Spawned(c, duty2.HolderId);
            bool started = false;
            if (holder != null)
            {
                switch (kind)
                {
                    case StewardDutyKind.OrganizeCeremony:
                        var ceremony=r.GatheringRequests.FirstOrDefault(q=>q.Id==target);
                        var plan=ceremony==null?null:r.PlanFor(ceremony.Id);
                        // The planning card owns this choice. A duty may track its chosen Steward/head, but must
                        // never replace a valid organizer merely because this maintenance pass chose a holder.
                        started=plan?.OrganizerId==holder.thingIDNumber||ceremony?.OrganizerId==holder.thingIDNumber;
                        break;
                    case StewardDutyKind.ReceivePetition:
                        var petition = r.HousePetitions.FirstOrDefault(p => p.Id == target);
                        started = petition != null && GovernanceMeetings.Start(holder, Spawned(c, petition.PersonId), WFL_StoryDefOf.WFL_StewardPetition, target); break;
                    case StewardDutyKind.GuestCare:
                        foreach (var contact in r.HouseContacts.Where(x => x.FamilyId == house.Id && x.Respect > 0f).OrderByDescending(x => x.LastTick).Take(8))
                        {
                            var guest = Spawned(c, contact.PersonId);
                            if (guest != null && !r.BelongsToFamily(r.GetPerson(guest.thingIDNumber), house.Id) && GovernanceMeetings.Start(holder, guest, WFL_StoryDefOf.WFL_StewardGuest, 0)) { started = true; break; }
                        }
                        break;
                    case StewardDutyKind.Mediate:
                        var rift = r.Rifts.FirstOrDefault(x => x.Id == target);
                        started = rift != null && GovernanceMeetings.Start(holder, Spawned(c, rift.A == holder.thingIDNumber ? rift.B : rift.A), WFL_StoryDefOf.WFL_StewardMediate, target); break;
                    case StewardDutyKind.Negotiate:
                        var alliance = r.MarriageAlliances.FirstOrDefault(a => a.Id == target);
                        if(target>0)started = alliance != null && MarriageAlliancePlanner.StartNegotiation(c, alliance, holder);
                        else
                        {
                            alliance=r.MarriageAlliances.FirstOrDefault(a=>a.Id==-target);
                            int otherFamily=alliance==null?0:alliance.FamilyA==house.Id?alliance.FamilyB:alliance.FamilyA;
                            var other=Spawned(c,MarriageAlliancePlanner.Representative(r,otherFamily));
                            started=alliance!=null&&other!=null&&GovernanceMeetings.Start(holder,other,WFL_StoryDefOf.WFL_StewardNegotiate,alliance.Id);
                        }
                        break;
                    case StewardDutyKind.SupplyShortage:
                        started = true; break;       // the supply system does the carrying; the duty closes when the goods are home
                }
            }
            if (!started) r.CancelStewardDuty(duty2.Id);
        }

        // ------------------------------------------------------------------ elections

        private static void Elections(FamilyComponent c, int now)
        {
            var r = c.Registry;
            var political = GovernanceMeetings.Political(c);
            r.AdvanceElections(now, political, physicalVoting: true);
            r.ConveneCouncil(now, political, id => Governance.IsAvatar(c, id));
            r.AdvanceCouncil(now, id => Governance.IsAvatar(c, id));
            r.CompleteApprovedUnions(now);
            foreach (var election in r.Elections.Where(e => e.Open).Take(1))
            {
                if (election.State == ElectionState.Campaign)
                {
                    // Each house's head backs the candidate they themselves find best, unless that head is the avatar and speaks for themselves.
                    foreach (var house in r.Lineages.Where(h => h.AbsorbedIntoId == 0 && !election.Endorsements.Any(e => e.FamilyId == h.Id)).Take(2))
                    {
                        int head = r.HeadOf(house.Id);
                        var headPawn = head == 0 ? null : c.TryGetPawn(head);
                        if (head == 0 || headPawn != null && PerspectiveShiftBridge.IsAvatar(headPawn)) continue;
                        var best = election.Candidates.Select(cand => r.Support(head, cand, political, election)).OrderByDescending(x => x.Total).ThenBy(x => x.CandidateId).FirstOrDefault();
                        if (best != null && best.Total >= .1f) r.EndorseCandidate(election.Id, house.Id, head, best.CandidateId);
                    }
                }
                else if (election.State == ElectionState.Voting && PollingPlace(c, out var map, out var cell))
                {
                    int sent = 0;
                    foreach (int voterId in r.EligibleVoters(election))
                    {
                        if (sent >= 3) break;
                        var pawn = Spawned(c, voterId);
                        if (pawn == null || pawn.Map != map || !r.NeedsToVote(voterId) || PerspectiveShiftBridge.IsAvatar(pawn)) continue;
                        if (!Activities.IsFree(pawn) || pawn.CurJob?.playerForced == true || pawn.CurJobDef == WFL_StoryDefOf.WFL_CastVote || !pawn.CanReach(cell, PathEndMode.OnCell, Danger.None)) continue;
                        var job = JobMaker.MakeJob(WFL_StoryDefOf.WFL_CastVote, cell); job.count = election.Id;
                        pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, false, tag: JobTag.Misc); sent++;
                    }
                }
            }
        }

        /// <summary>The leader's house office if it has a usable one, else any house's office, else the middle of a house's own area.</summary>
        internal static bool PollingPlace(FamilyComponent c, out Map map, out IntVec3 cell)
        {
            var r = c.Registry; var inst = r.Institution;
            map = null; cell = IntVec3.Invalid;
            if (inst.PollMapId >= 0)
            {
                map = Find.Maps.FirstOrDefault(m => m.uniqueID == inst.PollMapId);
                cell = new IntVec3(inst.PollX, 0, inst.PollZ);
                if (map != null && cell.InBounds(map) && cell.Standable(map)) return true;
            }
            var houses = r.Lineages.Where(h => h.AbsorbedIntoId == 0).OrderByDescending(h => h.Id == r.FamilyOf(inst.LeaderId)).ThenByDescending(h => h.Standing).ThenBy(h => h.Id).ToList();
            foreach (var house in houses)
            {
                var office = r.UsableOffice(house.Id, (m, x, z) => Find.Maps.Any(mm => mm.uniqueID == m && new IntVec3(x, 0, z).InBounds(mm) && new IntVec3(x, 0, z).Standable(mm)));
                if (office != null) { map = Find.Maps.First(m => m.uniqueID == office.MapId); cell = new IntVec3(office.X, 0, office.Z); Remember(inst, map, cell); return true; }
            }
            foreach (var house in houses)
            {
                var area = FamilyZones.AreaFor(house);
                if (area?.Map == null) continue;
                var pick = area.ActiveCells.FirstOrDefault(x => x.Standable(area.Map));
                if (pick.IsValid) { map = area.Map; cell = pick; Remember(inst, map, cell); return true; }
            }
            return false;
        }

        private static void Remember(ColonyInstitution inst, Map map, IntVec3 cell) { inst.PollMapId = map.uniqueID; inst.PollX = cell.x; inst.PollZ = cell.z; }

        // ------------------------------------------------------------------ house crime

        private static int claimedTick = -1, claimedMap = -1;
        private static List<Thing> claimedCache = new List<Thing>();

        /// <summary>Only stock somebody has a claim on, shared by every planner for a stretch so each house visit does not rescan the map's stock.</summary>
        private static List<Thing> ClaimedStock(FamilyComponent c, Map map, int now)
        {
            if (claimedMap != map.uniqueID || now / 2500 != claimedTick)
            {
                claimedMap = map.uniqueID; claimedTick = now / 2500;
                claimedCache = c.Economy.StockOf(map, now).Where(t => t.Spawned && c.Registry.ClaimFor(t.thingIDNumber) != null).ToList();
            }
            return claimedCache;
        }

        private static List<Thing> Targets(FamilyComponent c, Lineage house, Map map, int now)
        {
            var r = c.Registry;
            return ClaimedStock(c, map, now).Where(t => t.Spawned && r.ClaimFor(t.thingIDNumber) is StockClaim claim && claim.HolderFamily != house.Id &&
                r.IsActiveHouse(claim.HolderFamily) && r.HeadOf(claim.HolderFamily) != 0).Take(40).ToList();
        }

        private static Thing FindTarget(FamilyComponent c, Heist heist, int now)
        {
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == heist.TargetMapId);
            if (map == null) return null;
            var thing = c.Economy.StockOf(map, now).FirstOrDefault(t => t.thingIDNumber == heist.TargetToken && t.Spawned);
            int holder = heist.Consensual ? heist.FamilyId : heist.TargetFamilyId;       // goods being sold are the seller's, goods being taken are the other house's
            return thing != null && c.Registry.ClaimFor(thing.thingIDNumber)?.HolderFamily == holder ? thing : null;
        }

        private static CrimeContext CrimeContextFor(FamilyComponent c, int opportunities, int now) => new CrimeContext
        {
            Opportunities = opportunities,
            TargetValid = h => FindTarget(c, h, now) != null,
            Free = id => Governance.Free(c, id)
        };

        private static void Crime(FamilyComponent c, int now)
        {
            var r = c.Registry;
            var houses = r.Lineages.Where(h => h.AbsorbedIntoId == 0 && h.ZoneMapId >= 0).OrderBy(h => h.Id).ToList();
            if (houses.Count == 0) return;
            var house = houses[crimeRotate++ % houses.Count];
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == house.ZoneMapId);
            if (map == null) return;
            r.MaintainFavors(now);
            if (house.Crime.Heat >= 40f) r.RequestCensure(house.Id, now);
            ManageHeists(c, house, now);
            if (nextCrimeCheck.TryGetValue(house.Id, out int due) && now < due) return;
            nextCrimeCheck[house.Id] = now + Day / 2;
            var targets = Targets(c, house, map, now);
            var crime = house.Crime;
            if (crime.Status == CrimeStatus.None || crime.Status == CrimeStatus.Reformed && now - crime.EndedTick > 30 * Day)
            {
                var weak = ProtectionTargets(c, house, map, now);
                var recent = r.Incidents.Where(i => i.ActorFamily == house.Id && now - i.Tick < 60 * Day).ToList();
                bool menacing = recent.Count(i => i.Kind == IncidentKind.Intimidation) > recent.Count(i => i.Kind == IncidentKind.Theft);
                var direction = menacing && weak.Count > 0 ? CrimeDirection.Protection : menacing ? CrimeDirection.Intimidation : CrimeDirection.Theft;
                int options = direction == CrimeDirection.Protection ? weak.Count : targets.Count;
                if (r.AssessCrimeDirection(house.Id, CrimeContextFor(c, options, now), now).Eligible)
                    r.AdoptCrimeDirection(house.Id, direction, CrimeContextFor(c, options, now), now);
            }
            else if (crime.Status == CrimeStatus.Active)
            {
                var last = r.Heists.Where(h => h.FamilyId == house.Id).OrderByDescending(h => h.Id).FirstOrDefault();
                bool refusal = last != null && last.Participants.Any(p => p.Stance == CrimeStance.Refused || p.Stance == CrimeStance.Exposed);
                var ambition = house.Ambition?.Primary ?? HouseAmbitionKind.None;
                if (crime.Heat >= 50f && (refusal || ambition == HouseAmbitionKind.RestoreCohesion || ambition == HouseAmbitionKind.SeekProtection)) { r.BeginReform(house.Id, now); return; }
                if ((house.Situation?.Sustenance ?? 50f) >= 60f && now - crime.LastPlanTick > 30 * Day) { r.AbandonCrimeDirection(house.Id, now); return; }
                if (crime.Direction == CrimeDirection.Protection) { ManageProtection(c, house, map, now); return; }
                if (r.Heists.Any(h => h.FamilyId == house.Id && h.Active)) return;
                if (TrySale(c, house, map, now)) return;
                if (ManageFavors(c, house, map, now)) return;
                if (crime.Direction == CrimeDirection.Contraband || crime.Direction == CrimeDirection.Laundering || crime.Direction == CrimeDirection.Favors)
                    r.SetCrimeDirection(house.Id, r.Incidents.Count(i => i.ActorFamily == house.Id && i.Kind == IncidentKind.Intimidation && now - i.Tick < 60 * Day) > 0 ? CrimeDirection.Intimidation : CrimeDirection.Theft);
                if (targets.Count == 0) return;
                var actors = r.Incidents.Where(i => i.ActorFamily == house.Id && (i.Kind == IncidentKind.Theft || i.Kind == IncidentKind.Intimidation)).Select(i => i.ActorId).Distinct().ToList();
                var organizer = actors.Select(id => Spawned(c, id)).FirstOrDefault(p => p != null && p.Map == map && !p.DevelopmentalStage.Juvenile() && Governance.Free(c, p.thingIDNumber));
                var target = targets.OrderBy(t => organizer == null ? 0 : t.Position.DistanceToSquared(organizer.Position)).FirstOrDefault();
                if (organizer == null || target == null) return;
                r.PlanHeist(house.Id, organizer.thingIDNumber, r.ClaimFor(target.thingIDNumber).HolderFamily, map.uniqueID, target.thingIDNumber, CrimeContextFor(c, targets.Count, now), now);
            }
            else if (crime.Status == CrimeStatus.Reforming)
            {
                foreach (var restitution in r.Incidents.Where(i => i.Kind == IncidentKind.Restitution && i.ActorFamily == house.Id && i.Id > crime.LastAmendsIncident).OrderBy(i => i.Id).Take(4).ToList())
                    r.NoteAmends(house.Id, restitution.Id, now);
            }
        }

        private static Thing PricedFor(FamilyComponent c, Pawn buyer, Thing t) =>
            t.Spawned && InventoryAffordable(buyer, t) ? t : null;

        private static bool InventoryAffordable(Pawn buyer, Thing t) => FamilyEconomy.InventorySilver(buyer) >= Math.Max(1, (int)Math.Ceiling(t.GetStatValue(StatDefOf.MarketValue) * Math.Min(t.stackCount, 3)));

        /// <summary>
        /// Opens a willing-seller job when there is something to sell and someone who wants it and can pay: stolen goods to a third house (laundering),
        /// or drugs to a person already dependent on them (contraband).
        /// </summary>
        private static bool TrySale(FamilyComponent c, Lineage house, Map map, int now)
        {
            var r = c.Registry;
            var held = ClaimedStock(c, map, now).Where(t => t.Spawned && r.ClaimFor(t.thingIDNumber)?.HolderFamily == house.Id).ToList();
            var seller = r.MembersOf(house.Id).Where(p => p.InColony && !p.Juvenile).Select(p => Spawned(c, p.Id)).FirstOrDefault(p => p != null && p.Map == map && Governance.Free(c, p.thingIDNumber) && Activities.IsFree(p));
            if (seller == null) return false;
            var buyers = r.Persons.Where(p => p.InColony && !p.Juvenile && r.FamilyOf(p.Id) != house.Id && r.FamilyOf(p.Id) != 0).Select(p => Spawned(c, p.Id))
                .Where(p => p != null && p.Map == map && Governance.Free(c, p.thingIDNumber) && Activities.IsFree(p)).Take(20).ToList();
            if (buyers.Count == 0) return false;
            foreach (var item in held.Where(t => r.ClaimFor(t.thingIDNumber).OwnerFamily != house.Id).Take(5))              // laundering
            {
                var buyer = buyers.FirstOrDefault(b => r.FamilyOf(b.thingIDNumber) != r.ClaimFor(item.thingIDNumber).OwnerFamily && PricedFor(c, b, item) != null);
                if (buyer != null && StartSaleJob(c, house, seller, buyer, item, CrimeDirection.Laundering, now)) return true;
            }
            foreach (var item in held.Where(t => FamilyEconomy.KindOf(t) == SupplyKind.Drug).Take(5))                       // contraband
            {
                var buyer = buyers.FirstOrDefault(b => (AddictionUtility.IsAddicted(b, item) || AddictionUtility.HasChemicalDependency(b, item)) && PricedFor(c, b, item) != null);
                if (buyer != null && StartSaleJob(c, house, seller, buyer, item, CrimeDirection.Contraband, now)) return true;
            }
            return false;
        }

        private static bool StartSaleJob(FamilyComponent c, Lineage house, Pawn seller, Pawn buyer, Thing item, CrimeDirection direction, int now)
        {
            var r = c.Registry;
            if (!r.SetCrimeDirection(house.Id, direction)) return false;
            var heist = r.PlanHeist(house.Id, seller.thingIDNumber, r.FamilyOf(buyer.thingIDNumber), seller.Map.uniqueID, item.thingIDNumber, CrimeContextFor(c, 1, now), now);
            return heist != null;
        }

        /// <summary>Calls in one favor owed to the house: a member asks the debtor''s house for goods. Whether it is honoured is the debtor''s own call.</summary>
        private static bool ManageFavors(FamilyComponent c, Lineage house, Map map, int now)
        {
            var r = c.Registry;
            r.RememberFavors(house.Id, now);
            var favor = r.Favors.FirstOrDefault(f => f.State == FavorState.Owed && f.CreditorFamily == house.Id && now - f.CreatedTick > Day / 2);
            if (favor == null) return false;
            var creditor = r.MembersOf(house.Id).Where(p => p.InColony && !p.Juvenile).Select(p => Spawned(c, p.Id)).FirstOrDefault(p => p != null && p.Map == map && Governance.Free(c, p.thingIDNumber) && Activities.IsFree(p));
            if (creditor == null || !c.Economy.RequestAid(creditor, favor.DebtorFamily)) return false;
            var request = r.Requests.LastOrDefault(q => q.RequesterId == creditor.thingIDNumber && q.Approach == SupplyApproach.Ask && q.CreatedTick >= now - 5);
            if (request != null) r.CallInFavor(favor.Id, request.Id, now);
            return request != null;
        }

        /// <summary>Nearby houses that could plausibly want protection: unsettled, with a head present, and holding silver to pay with.</summary>
        private static List<Lineage> ProtectionTargets(FamilyComponent c, Lineage house, Map map, int now)
        {
            var r = c.Registry;
            var silver = ClaimedStock(c, map, now).Where(t => t.def == ThingDefOf.Silver && t.Spawned).Select(t => r.ClaimFor(t.thingIDNumber)?.HolderFamily ?? 0).Where(f => f != 0).Distinct().ToHashSet();
            return r.Lineages.Where(h => h.AbsorbedIntoId == 0 && h.Id != house.Id && silver.Contains(h.Id) && (h.Situation?.Safety ?? 50f) < 60f && Spawned(c, r.HeadOf(h.Id)) != null).OrderBy(h => h.Situation?.Safety ?? 50f).Take(3).ToList();
        }

        private static void ManageProtection(FamilyComponent c, Lineage house, Map map, int now)
        {
            var r = c.Registry;
            var deal = r.ProtectionArrangements.FirstOrDefault(a => a.ProtectorFamily == house.Id && a.EndedTick == 0);
            var rep = Spawned(c, r.HeadOf(house.Id));
            if (deal == null)
            {
                if (rep == null || now - house.Crime.LastPlanTick < 2 * Day || rep.CurJobDef == WFL_StoryDefOf.WFL_ProtectionOffer) return;
                var target = ProtectionTargets(c, house, map, now).FirstOrDefault();
                var head = target == null ? null : Spawned(c, r.HeadOf(target.Id));
                if(head!=null&&PerspectiveShiftBridge.IsAvatar(head))
                {
                    var bond=r.GetBond(head.thingIDNumber,rep.thingIDNumber);float trust=bond?.Trust??0f,tension=bond?.Tension??0f;
                    bool needs=(target.Situation?.Safety??50f)<50f&&trust>=20f,afraid=tension>=40f&&trust<20f;
                    if(r.ProposeProtection(house.Id,target.Id,15,now,afraid&&!needs)!=null)
                    {house.Crime.LastPlanTick=now;Messages.Message("WFL_Protection_Waiting".Translate(head.LabelShort),head,MessageTypeDefOf.NeutralEvent,false);}
                    return;
                }
                if (head != null && GovernanceMeetings.Start(rep, head, WFL_StoryDefOf.WFL_ProtectionOffer, target.Id)) house.Crime.LastPlanTick = now;
                return;
            }
            if (!deal.Agreed)
            {
                if (now - Math.Max(1, house.Crime.LastPlanTick) > 2 * Day) r.EndProtection(deal.Id, now);       // never answered, so never agreed
                return;
            }
            // Payments are recorded only from requests the supply system really completed.
            foreach (var q in r.Requests.Where(x => (x.Approach == SupplyApproach.Offer || x.Approach == SupplyApproach.Threat) && x.Kind == SupplyKind.Currency && x.Id > deal.LastRequestId && !x.Open &&
                r.FamilyOf(x.RequesterId) == deal.ProtectorFamily && r.FamilyOf(x.DonorId) == deal.ProtectedFamily).OrderBy(x => x.Id).Take(4).ToList())
            {
                deal.LastRequestId = q.Id;
                if (q.State == SupplyState.Exchanged || q.State == SupplyState.Stored) r.RecordTribute(deal.Id, q.Count, q.Id + 2000000, now);
            }
            if (now - deal.LastCollectionTick < 3 * Day || rep == null) return;
            var donor = Spawned(c, r.HeadOf(deal.ProtectedFamily));
            var silverStack = donor == null ? null : c.Economy.StockOf(rep.Map, now).FirstOrDefault(t => t.def == ThingDefOf.Silver && t.Spawned && r.ClaimFor(t.thingIDNumber)?.HolderFamily == deal.ProtectedFamily && t.stackCount >= deal.TributeAmount);
            if (silverStack != null && Activities.IsFree(rep) && c.Economy.StartTheft(rep, donor, silverStack, deal.TributeAmount, now, deal.Coerced ? SupplyApproach.Threat : SupplyApproach.Offer)) deal.LastCollectionTick = now;
        }

        private static void ManageHeists(FamilyComponent c, Lineage house, int now)
        {
            var r = c.Registry;
            foreach (var heist in r.Heists.Where(h => h.FamilyId == house.Id).ToList())
            {
                var context = CrimeContextFor(c, 1, now);
                var organizer = Spawned(c, heist.OrganizerId);
                switch (heist.State)
                {
                    case HeistState.Planning:
                        if (organizer == null) { r.AbortHeist(heist.Id, now); break; }
                        if (heist.Consensual) { if (!r.ReadyHeist(heist.Id, context, now)) r.AbortHeist(heist.Id, now); break; }
                        if (organizer.CurJobDef == WFL_StoryDefOf.WFL_HeistRecruit) break;
                        var next = r.MembersOf(house.Id).Where(p => p.InColony && !p.Juvenile && heist.Participants.All(x => x.PersonId != p.Id)).OrderBy(p => p.Id)
                            .Select(p => Spawned(c, p.Id)).FirstOrDefault(p => p != null && p.Map == organizer.Map);
                        if (next != null && heist.WillingCount < 3)
                        {
                            // A pawn the player controls is asked, not talked into it: the question waits in their Families window.
                            if (PerspectiveShiftBridge.IsAvatar(next)) r.AskHeistMember(heist.Id, next.thingIDNumber);
                            else GovernanceMeetings.Start(organizer, next, WFL_StoryDefOf.WFL_HeistRecruit, heist.Id);
                            break;
                        }
                        bool waiting = heist.Participants.Any(x => x.Stance == CrimeStance.Unasked) && now - heist.StateTick < 2 * Day;
                        if (waiting) break;
                        if (heist.WillingCount >= 2) r.ReadyHeist(heist.Id, context, now); else r.AbortHeist(heist.Id, now);
                        break;
                    case HeistState.Ready:
                        if (r.BeginHeist(heist.Id, context, now) && !Launch(c, heist, now)) r.InterruptHeist(heist.Id, now);
                        break;
                    case HeistState.Executing:
                        Monitor(c, heist, now); break;
                    case HeistState.Completed: case HeistState.Exposed: case HeistState.Interrupted: case HeistState.Aborted:
                        if (now - heist.StateTick > Day / 2) r.CleanupHeist(heist.Id, now); break;
                }
            }
        }

        /// <summary>Each willing participant starts a real theft of a real item. Whatever then happens is what the supply system records.</summary>
        private static bool Launch(FamilyComponent c, Heist heist, int now)
        {
            var r = c.Registry;
            var map = Find.Maps.FirstOrDefault(m => m.uniqueID == heist.TargetMapId);
            if (heist.Consensual)
            {
                var sellerPawn = Spawned(c, heist.OrganizerId); var good = FindTarget(c, heist, now);
                if (sellerPawn == null || good == null || map == null) return false;
                var buyer = r.Persons.Where(p => p.InColony && !p.Juvenile && r.FamilyOf(p.Id) == heist.TargetFamilyId).Select(p => Spawned(c, p.Id))
                    .FirstOrDefault(p => p != null && p.Map == map && Governance.Free(c, p.thingIDNumber) && Activities.IsFree(p) && InventoryAffordable(p, good));
                if (buyer == null || !c.Economy.StartSale(buyer, sellerPawn, good, Math.Min(good.stackCount, 3), now)) return false;
                Track(r, heist, buyer.thingIDNumber, now);
                return true;
            }
            var donor = Spawned(c, r.HeadOf(heist.TargetFamilyId));
            if (donor == null || map == null) return false;
            var items = ClaimedStock(c, map, now).Where(t => t.Spawned && r.ClaimFor(t.thingIDNumber)?.HolderFamily == heist.TargetFamilyId).ToList();
            var first = FindTarget(c, heist, now);
            int started = 0;
            foreach (var part in heist.Participants.Where(p => p.Stance == CrimeStance.Willing).Take(3))
            {
                var actor = Spawned(c, part.PersonId);
                if (actor == null || actor.Map != map || !Governance.Free(c, actor.thingIDNumber) || !Activities.IsFree(actor)) continue;
                var thing = started == 0 && first != null ? first : items.Where(t => t != first).OrderBy(t => t.Position.DistanceToSquared(actor.Position)).FirstOrDefault(t => actor.CanReserve(t));
                if (thing != null && c.Economy.StartTheft(actor, donor, thing, Math.Min(thing.stackCount, 5), now, heist.Direction == CrimeDirection.Intimidation ? SupplyApproach.Threat : SupplyApproach.Steal)) { started++; Track(r, heist, actor.thingIDNumber, now); }
            }
            return started > 0;
        }

        /// <summary>Remembers the request a participant just started, by the one thing that identifies it: who asked, and that it is brand new.</summary>
        private static void Track(FamilyRegistry r, Heist heist, int requester, int now)
        {
            var request = r.Requests.LastOrDefault(q => q.RequesterId == requester && q.CreatedTick >= now - 5 && !heist.RequestIds.Contains(q.Id));
            if (request != null) heist.RequestIds.Add(request.Id);
        }

        private static void Monitor(FamilyComponent c, Heist heist, int now)
        {
            var r = c.Registry;
            if (heist.Consensual)
            {
                var sales = r.Requests.Where(q => heist.RequestIds.Contains(q.Id)).ToList();
                if (sales.Count == 0 || sales.Any(q => q.Open)) { if (now - heist.StateTick > 2 * Day) r.InterruptHeist(heist.Id, now); return; }
                var done = sales.FirstOrDefault(q => q.State == SupplyState.Exchanged || q.State == SupplyState.Stored);
                if (!r.ResolveHeist(heist.Id, done?.Count ?? 0, done == null ? 0 : done.Id + 3000000, false, null, now)) { r.InterruptHeist(heist.Id, now); return; }
                if (done != null && heist.Direction == CrimeDirection.Laundering) r.LaunderGoods(heist.Id, done.SourceId, now);
                return;
            }
            var people = heist.Participants.Where(p => p.Stance == CrimeStance.Willing).Select(p => p.PersonId).ToList();
            var requests = r.Requests.Where(q => heist.RequestIds.Contains(q.Id)).ToList();
            if (requests.Count == 0 || requests.Any(q => q.Open))
            {
                if (now - heist.StateTick > 2 * Day) r.InterruptHeist(heist.Id, now);
                return;
            }
            int goods = requests.Where(q => q.State == SupplyState.Exchanged || q.State == SupplyState.Stored).Sum(q => q.Count);
            var theft = r.Incidents.Where(i => (i.Kind == IncidentKind.Theft || i.Kind == IncidentKind.Intimidation) && people.Contains(i.ActorId) && i.Tick >= heist.StateTick).OrderByDescending(i => i.Id).FirstOrDefault();
            bool caught = theft != null && theft.KnownBy.Any(id => r.FamilyOf(id) == heist.TargetFamilyId);
            int token = goods > 0 ? requests.Max(q => q.Id) + 1000000 : 0;
            if (!r.ResolveHeist(heist.Id, goods, token, caught, theft?.KnownBy, now, theft?.Id ?? 0)) r.InterruptHeist(heist.Id, now);
        }

        // ------------------------------------------------------------------ schemes against the leader

        private static void Coups(FamilyComponent c, int now)
        {
            var r = c.Registry;
            if (r.Institution.Kind == InstitutionKind.Consensus || r.Institution.LeaderId == 0) return;
            var political = GovernanceMeetings.Political(c);
            var open = r.Coups.Where(x => x.Open).ToList();
            if (open.Count == 0)
            {
                // At most one adult is considered per pass: deeply aggrieved, with little attachment, and no reason given to wait.
                var adults = r.Persons.Where(p => p.InColony && !p.Juvenile && p.Id != r.Institution.LeaderId).OrderBy(p => p.Id).ToList();
                if (adults.Count == 0) return;
                var person = adults[coupRotate++ % adults.Count];
                var pawn = Spawned(c, person.Id);
                var bond = r.GetBond(person.Id, r.Institution.LeaderId);
                if (pawn == null || PerspectiveShiftBridge.IsAvatar(pawn) || bond == null || bond.Tension < 55f || bond.Attachment >= 20f) return;
                r.PlanCoup(person.Id, CoupGoal.ReplaceLeader, 0, political, now);
                return;
            }
            foreach (var coup in open.Take(1))
            {
                var organizer = Spawned(c, coup.OrganizerId);
                if ((r.GetBond(coup.OrganizerId, coup.TargetLeaderId)?.Tension ?? 0f) < 20f) { r.ReconcileCoup(coup.Id, now); continue; }
                r.AdvanceCoup(coup.Id, political, now, physical: true);
                if (!coup.Open || organizer == null) continue;
                if (coup.Stage == CoupStage.Recruiting && organizer.CurJobDef != WFL_StoryDefOf.WFL_CoupRecruit)
                {
                    var recruit = r.Persons.Where(p => p.InColony && !p.Juvenile && p.Id != coup.TargetLeaderId && coup.Participants.All(x => x.PersonId != p.Id)).OrderBy(p => p.Id)
                        .Select(p => Spawned(c, p.Id)).FirstOrDefault(p => p != null && p.Map == organizer.Map &&
                            ((r.GetBond(p.thingIDNumber, coup.OrganizerId)?.Trust ?? 0f) >= 25f || (r.GetBond(p.thingIDNumber, coup.TargetLeaderId)?.Tension ?? 0f) >= 30f));
                    if (recruit != null && PerspectiveShiftBridge.IsAvatar(recruit)) r.AskCoupMember(coup.Id, recruit.thingIDNumber);
                    else if (recruit != null) GovernanceMeetings.Start(organizer, recruit, WFL_StoryDefOf.WFL_CoupRecruit, coup.Id);
                }
                else if (coup.Stage == CoupStage.Executing && organizer.CurJobDef != WFL_StoryDefOf.WFL_CoupConfront)
                {
                    var leader = Spawned(c, coup.TargetLeaderId);
                    if(leader!=null&&PerspectiveShiftBridge.IsAvatar(leader))
                    {if(now-coup.StageTick>3*Day)r.AvoidCoupConfrontation(coup.Id,now);continue;}
                    if (leader != null && leader.Map == organizer.Map) GovernanceMeetings.Start(organizer, leader, WFL_StoryDefOf.WFL_CoupConfront, coup.Id);
                    else if (now - coup.StageTick > 3 * Day) r.ResolveCoup(coup.Id, political, now);
                }
            }
        }
    }
}
