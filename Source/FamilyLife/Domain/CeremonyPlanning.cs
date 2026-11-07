using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum PlanState : byte { Open, Confirmed, Defaulted, Cancelled }
    public enum InviteSource : byte { Family, Guest, Distant }
    public enum InviteStatus : byte { Invited, Declined, Unavailable, Removed }
    public enum AvatarAsk : byte { None, Asked, Accepted, Declined, Unanswered }

    /// <summary>Why someone is not on the list. Shown next to the name so the player can see the reason rather than guess it.</summary>
    [Flags]
    public enum InviteBarrier : ushort
    {
        None = 0, Dead = 1, Hostile = 2, TooFarForWindow = 4, CannotTravel = 8, Unavailable = 16,
        NotKin = 32, TheSubject = 64, WrongMap = 128
    }

    /// <summary>Facts only the game can know. Every member is optional: a missing one never blocks a plan.</summary>
    public sealed class PlanContext
    {
        /// <summary>Ticks until the person could reach the venue; negative when they cannot.</summary>
        public Func<int, int> ArrivalEstimate;
        /// <summary>False when the person is downed, imprisoned, drafted or otherwise cannot take part right now.</summary>
        public Func<int, bool> Available;
        public Func<int, bool> IsAvatar;
        public Func<int, int, bool> VenueValid;       // map id, area id
        public int DefaultMapId = -1;

        internal bool CanTake(int person) => Available == null || Available(person);
        internal bool Avatar(int person) => IsAvatar != null && IsAvatar(person);
        internal int Arrival(int person) => ArrivalEstimate == null ? 0 : ArrivalEstimate(person);
        internal bool Venue(int map, int area) => VenueValid == null || VenueValid(map, area);
    }

    public sealed partial class PlanInvitee
    {
        public int PersonId, ArrivalTick;
        public InviteSource Source;
        public InviteStatus Status;
        public InviteBarrier Barriers;
        public bool ByPlayer;
    }

    /// <summary>
    /// The one neutral planning card a family raises when it commits to an occasion. Everything on it has a family-chosen default,
    /// so the occasion happens whether or not anyone looks at the card. It chooses logistics, never how the occasion goes.
    /// </summary>
    public sealed partial class CeremonyPlan
    {
        public int Id, RequestId, FamilyId, OpenedTick, DeadlineTick, EarliestTick, LatestTick, ScheduledTick;
        public int OrganizerId, VenueMapId = -1, VenueAreaId, AvatarId;
        public PlanState State;
        public AvatarAsk Ask;
        public bool PlayerAdjusted;
        public List<PlanInvitee> Invitees = new List<PlanInvitee>();
        /// <summary>Who has actually been told. Invitations are not visible to anyone else.</summary>
        public List<int> KnownBy = new List<int>();

        public bool Open => State == PlanState.Open;
        public IEnumerable<int> InvitedIds => Invitees.Where(i => i.Status == InviteStatus.Invited).Select(i => i.PersonId);
    }

    public sealed partial class FamilyRegistry
    {
        public const int PlanWindowTicks = 3 * ActivityTuning.TicksPerDay;
        public const int PlanMinimumLeadTicks = 2500;
        public const int MaxPlans = 32;
        public List<CeremonyPlan> CeremonyPlans = new List<CeremonyPlan>();
        public int NextPlanId = 1;

        public CeremonyPlan PlanFor(int requestId) => CeremonyPlans.FirstOrDefault(p => p.RequestId == requestId && p.State != PlanState.Cancelled);

        /// <summary>When the occasion will start: the plan's chosen time once there is one, the request's own time otherwise.</summary>
        public int ScheduledStart(GatheringRequest request)
        {
            var plan = request == null ? null : PlanFor(request.Id);
            return plan == null || plan.State == PlanState.Open ? (request?.NotBefore ?? 0) : plan.ScheduledTick;
        }

        /// <summary>
        /// Raises the planning card for a committed occasion. The first look at it is the family's own choices; if nobody changes
        /// anything, the deadline simply confirms them. Idempotent for a request.
        /// </summary>
        public CeremonyPlan OpenCeremonyPlan(int requestId, int now, PlanContext ctx = null)
        {
            ctx = ctx ?? new PlanContext();
            var request = GatheringRequests.FirstOrDefault(r => r.Id == requestId);
            if (request == null || request.State != CeremonyState.Planned || !IsActiveHouse(request.LineageId)) return null;
            var existing = PlanFor(requestId);
            if (existing != null) return existing;
            if (CeremonyPlans.Count >= MaxPlans)
            {
                var stale = CeremonyPlans.FirstOrDefault(p => p.State != PlanState.Open && GatheringRequests.All(r => r.Id != p.RequestId));
                if (stale == null) return null;
                CeremonyPlans.Remove(stale);
            }
            int earliest = Math.Max(now, request.NotBefore);
            int latest = Math.Max(earliest, Math.Min(earliest + PlanWindowTicks, request.Expires - PlanMinimumLeadTicks));
            var plan = new CeremonyPlan
            {
                Id = NextPlanId++, RequestId = requestId, FamilyId = request.LineageId, OpenedTick = now,
                EarliestTick = earliest, LatestTick = latest, ScheduledTick = earliest,
                DeadlineTick = Math.Max(now + PlanMinimumLeadTicks, earliest), VenueMapId = ctx.DefaultMapId
            };
            plan.OrganizerId = DefaultOrganizer(request, ctx);
            foreach (var candidate in PlanCandidates(request, plan, now, ctx))
                if (candidate.Barriers == InviteBarrier.None) plan.Invitees.Add(candidate);
            foreach (int person in new[] { plan.OrganizerId, HeadOf(request.LineageId) }.Concat(
                         plan.Invitees.Where(i => i.Source == InviteSource.Family).Select(i => i.PersonId)))
                if (person != 0 && !plan.KnownBy.Contains(person)) plan.KnownBy.Add(person);
            CeremonyPlans.Add(plan);
            return plan;
        }

        /// <summary>The family's own choice of organizer: an active Steward, then the head, then the most experienced capable adult. Never the subject, never the avatar.</summary>
        public int DefaultOrganizer(GatheringRequest request, PlanContext ctx = null)
        {
            ctx = ctx ?? new PlanContext();
            bool Fit(PersonRecord p) => p != null && p.InColony && !p.Juvenile && ctx.CanTake(p.Id) && !ctx.Avatar(p.Id) && p.Id != request.SubjectId && p.Id != request.SubjectBId;
            int family = request.LineageId;
            var steward = Stewards(family).Where(Fit).OrderByDescending(p => StewardshipOf(p.Id, family, true)?.Successes ?? 0).ThenBy(p => p.Id).FirstOrDefault();
            if (steward != null) return steward.Id;
            var head = GetPerson(HeadOf(family));
            if (Fit(head)) return head.Id;
            return MembersOf(family).Where(Fit).OrderByDescending(p => OfficeFor(OfficeKind.Chaplain)?.HolderId == p.Id).ThenByDescending(p => PrestigeOf(p.Id)?.Value ?? 0).ThenBy(p => p.BirthAbsTick).ThenBy(p => p.Id).Select(p => p.Id).FirstOrDefault();
        }

        private IEnumerable<PersonRecord> Stewards(int family)
        {
            var house = GetLineage(family);
            return house == null ? Enumerable.Empty<PersonRecord>() : house.Stewards.Select(GetPerson).Where(p => p != null && BelongsToFamily(p, family));
        }

        /// <summary>
        /// Everyone the occasion could reasonably include, with the reason for any who cannot be. A distant relative who could not arrive inside the
        /// scheduling window is listed with that reason rather than silently dropped.
        /// </summary>
        public List<PlanInvitee> PlanCandidates(GatheringRequest request, CeremonyPlan plan, int now, PlanContext ctx = null)
        {
            ctx = ctx ?? new PlanContext();
            var result = new List<PlanInvitee>();
            int family = request.LineageId, window = Math.Max(0, plan.LatestTick - now);
            void Add(PersonRecord p, InviteSource source)
            {
                if (p == null || result.Any(r => r.PersonId == p.Id)) return;
                var item = new PlanInvitee { PersonId = p.Id, Source = source, Status = InviteStatus.Invited };
                if (!p.IsAlive) item.Barriers |= InviteBarrier.Dead;
                if (p.Id == request.SubjectId && request.Ceremony == CeremonyKind.Funeral) item.Barriers |= InviteBarrier.TheSubject;
                int otherHouse = FamilyOf(p.Id);
                if (otherHouse != 0 && otherHouse != family && (GetTie(family, otherHouse)?.Grievance ?? 0f) >= 40f) item.Barriers |= InviteBarrier.Hostile;
                if (p.IsAlive && source == InviteSource.Family && !ctx.CanTake(p.Id)) item.Barriers |= InviteBarrier.Unavailable;
                if (p.IsAlive && !p.InColony)
                {
                    int arrival = ctx.ArrivalEstimate == null ? -1 : ctx.Arrival(p.Id);
                    if (arrival < 0) item.Barriers |= InviteBarrier.CannotTravel;
                    else if (arrival > window) item.Barriers |= InviteBarrier.TooFarForWindow;
                    else item.ArrivalTick = now + arrival;
                }
                result.Add(item);
            }
            foreach (var member in MembersOf(family).Where(p => p.InColony)) Add(member, InviteSource.Family);
            if (request.SecondLineageId != 0)
                foreach (var member in MembersOf(request.SecondLineageId).Where(p => p.InColony)) Add(member, InviteSource.Family);
            foreach (var contact in HouseContacts.Where(c => c.FamilyId == family && c.Respect >= 6f).OrderByDescending(c => c.Respect).ThenBy(c => c.PersonId).Take(16))
                Add(GetPerson(contact.PersonId), InviteSource.Guest);
            if (request.Ceremony == CeremonyKind.Wedding || request.Ceremony == CeremonyKind.Funeral || request.Ceremony == CeremonyKind.GrandReunion)
                foreach (var away in Persons.Where(p => p.Presence == Presence.Away && p.IsAlive && BelongsToFamily(p, family)).OrderBy(p => p.Id).Take(24)) Add(away, InviteSource.Distant);
            return result;
        }

        // ---- choices the player can make (logistics only) ----

        public bool SetPlanSchedule(int planId, int tick, int now)
        {
            var plan = CeremonyPlans.FirstOrDefault(p => p.Id == planId);
            if (plan == null || !plan.Open || tick < Math.Max(plan.EarliestTick, now) || tick > plan.LatestTick) return false;
            plan.ScheduledTick = tick; plan.PlayerAdjusted = true;
            RefreshArrivals(plan, now);
            return true;
        }

        public bool SetPlanOrganizer(int planId, int person, PlanContext ctx = null)
        {
            ctx = ctx ?? new PlanContext();
            var plan = CeremonyPlans.FirstOrDefault(p => p.Id == planId); var p2 = GetPerson(person);
            var request = plan == null ? null : GatheringRequests.FirstOrDefault(r => r.Id == plan.RequestId);
            if (plan == null || request == null || !plan.Open || p2?.InColony != true || p2.Juvenile || !ctx.CanTake(person)) return false;
            if (person == request.SubjectId || person == request.SubjectBId && request.Ceremony == CeremonyKind.Funeral) return false;
            // The avatar can lead only after answering for themselves. Asking is not agreeing, and silence is neither.
            if (ctx.Avatar(person) && !(plan.AvatarId == person && plan.Ask == AvatarAsk.Accepted)) return false;
            if (!BelongsToFamily(p2, plan.FamilyId) && RoleInHouse(person, plan.FamilyId) < HouseRole.TrustedAssociate) return false;
            plan.OrganizerId = person; plan.PlayerAdjusted = true; Tell(plan, person);
            return true;
        }

        public bool SetPlanVenue(int planId, int mapId, int areaId, PlanContext ctx = null)
        {
            ctx = ctx ?? new PlanContext();
            var plan = CeremonyPlans.FirstOrDefault(p => p.Id == planId);
            if (plan == null || !plan.Open || !ctx.Venue(mapId, areaId)) return false;
            plan.VenueMapId = mapId; plan.VenueAreaId = areaId; plan.PlayerAdjusted = true;
            return true;
        }

        public bool InvitePlanPerson(int planId, int person, int now, PlanContext ctx = null)
        {
            ctx = ctx ?? new PlanContext();
            var plan = CeremonyPlans.FirstOrDefault(p => p.Id == planId); var p2 = GetPerson(person);
            var request = plan == null ? null : GatheringRequests.FirstOrDefault(r => r.Id == plan.RequestId);
            if (plan == null || request == null || !plan.Open || p2 == null || plan.Invitees.Count >= 64) return false;
            var candidate = PlanCandidates(request, plan, now, ctx).FirstOrDefault(c => c.PersonId == person);
            if (candidate == null)
            {   // An outsider the family has no tie to can be asked, subject to the same hard checks.
                candidate = new PlanInvitee { PersonId = person, Source = InviteSource.Guest, Status = InviteStatus.Invited };
                if (!p2.IsAlive) candidate.Barriers |= InviteBarrier.Dead;
                if (p2.IsAlive && !p2.InColony) candidate.Barriers |= InviteBarrier.CannotTravel;
            }
            if (candidate.Barriers != InviteBarrier.None) return false;
            plan.Invitees.RemoveAll(i => i.PersonId == person);
            candidate.ByPlayer = true; plan.Invitees.Add(candidate); plan.PlayerAdjusted = true; Tell(plan, person);
            return true;
        }

        public bool UninvitePlanPerson(int planId, int person)
        {
            var plan = CeremonyPlans.FirstOrDefault(p => p.Id == planId);
            var item = plan?.Invitees.FirstOrDefault(i => i.PersonId == person);
            if (plan == null || !plan.Open || item == null || person == plan.OrganizerId) return false;
            item.Status = InviteStatus.Removed; item.ByPlayer = true; plan.PlayerAdjusted = true;
            return true;
        }

        /// <summary>Asks the avatar to lead. It changes nothing until the avatar answers.</summary>
        public bool AskAvatarToLead(int planId, int avatar, PlanContext ctx)
        {
            var plan = CeremonyPlans.FirstOrDefault(p => p.Id == planId);
            if (plan == null || !plan.Open || ctx == null || !ctx.Avatar(avatar) || GetPerson(avatar)?.InColony != true) return false;
            plan.AvatarId = avatar; plan.Ask = AvatarAsk.Asked; Tell(plan, avatar);
            return true;
        }

        public bool AnswerAvatar(int planId, int avatar, bool accept, PlanContext ctx)
        {
            var plan = CeremonyPlans.FirstOrDefault(p => p.Id == planId);
            if (plan == null || plan.AvatarId != avatar || plan.Ask != AvatarAsk.Asked) return false;
            if (!accept) { plan.Ask = AvatarAsk.Declined; return true; }
            plan.Ask = AvatarAsk.Accepted;
            if (plan.Open && !SetPlanOrganizer(planId, avatar, ctx)) { plan.Ask = AvatarAsk.Declined; return false; }
            return true;
        }

        public bool ConfirmPlan(int planId, int now, PlanContext ctx = null)
        {
            var plan = CeremonyPlans.FirstOrDefault(p => p.Id == planId);
            if (plan == null || !plan.Open) return false;
            Finalize(plan, PlanState.Confirmed, now, ctx ?? new PlanContext());
            return true;
        }

        private void Tell(CeremonyPlan plan, int person) { if (person != 0 && !plan.KnownBy.Contains(person)) plan.KnownBy.Add(person); }

        /// <summary>Invitations and the reasons behind them are visible to the people who were told, and to the omniscient view (observer 0).</summary>
        public bool PlanVisibleTo(CeremonyPlan plan, int observer) => plan != null && (observer == 0 || plan.KnownBy.Contains(observer) || HeadOf(plan.FamilyId) == observer);

        /// <summary>
        /// Whether the autonomous systems may direct this person to the occasion. An invited avatar, or anyone who has not
        /// been told, is only invited: the game never moves them on their own behalf.
        /// </summary>
        public bool PlanMayDirect(CeremonyPlan plan, int person, PlanContext ctx)
        {
            if (plan == null || ctx == null) return false;
            if (ctx.Avatar(person)) return plan.AvatarId == person && plan.Ask == AvatarAsk.Accepted;
            return plan.InvitedIds.Contains(person);
        }

        // ---- keeping plans honest ----

        /// <summary>
        /// Called on the normal maintenance cycle. Open cards past their deadline confirm their defaults; confirmed plans repair
        /// themselves when a room, organizer or guest has gone; plans for requests that no longer exist disappear.
        /// </summary>
        public void AdvanceCeremonyPlans(int now, PlanContext ctx = null) => AdvanceCeremonyPlans(now, ctx == null ? (Func<int, PlanContext>)null : (_ => ctx));

        /// <summary>Same, with a context per house (each house can have a different home map and different people available).</summary>
        public void AdvanceCeremonyPlans(int now, Func<int, PlanContext> contextFor)
        {
            foreach (var plan in CeremonyPlans.ToList())
            {
                var ctx = contextFor == null ? new PlanContext() : contextFor(plan.FamilyId) ?? new PlanContext();
                var request = GatheringRequests.FirstOrDefault(r => r.Id == plan.RequestId);
                if (request == null || request.State == CeremonyState.Completed || request.State == CeremonyState.Expired || request.State == CeremonyState.Cancelled || !IsActiveHouse(plan.FamilyId))
                { if (request == null || plan.State == PlanState.Cancelled || plan.OpenedTick < now - 6 * ActivityTuning.TicksPerDay) CeremonyPlans.Remove(plan); else plan.State = PlanState.Cancelled; continue; }
                if (request.State == CeremonyState.Active) continue;
                if (plan.Open && now >= plan.DeadlineTick) { Finalize(plan, plan.PlayerAdjusted ? PlanState.Confirmed : PlanState.Defaulted, now, ctx); continue; }
                if (plan.State == PlanState.Cancelled) continue;
                Repair(plan, request, now, ctx);
            }
        }

        private void Finalize(CeremonyPlan plan, PlanState state, int now, PlanContext ctx)
        {
            var request = GatheringRequests.FirstOrDefault(r => r.Id == plan.RequestId);
            plan.State = state;
            if (plan.Ask == AvatarAsk.Asked) plan.Ask = AvatarAsk.Unanswered;
            if (request != null) Repair(plan, request, now, ctx);
            if (request != null) request.NotBefore = Math.Min(Math.Max(request.NotBefore, plan.ScheduledTick), Math.Max(request.NotBefore, request.Expires - PlanMinimumLeadTicks));
        }

        private void Repair(CeremonyPlan plan, GatheringRequest request, int now, PlanContext ctx)
        {
            // A time that has already passed moves to the next workable moment inside the request's life; if there is none, the request ends cleanly.
            if (plan.ScheduledTick < now)
            {
                int next = now + PlanMinimumLeadTicks;
                if (next > request.Expires) { request.State = CeremonyState.Expired; GatheringRequests.Remove(request); plan.State = PlanState.Cancelled; return; }
                plan.ScheduledTick = next; plan.LatestTick = Math.Max(plan.LatestTick, next);
                if (!plan.Open) request.NotBefore = Math.Max(request.NotBefore, next);
            }
            var organizer = GetPerson(plan.OrganizerId);
            bool organizerOk = organizer?.InColony == true && !organizer.Juvenile && ctx.CanTake(organizer.Id) && (!ctx.Avatar(organizer.Id) || plan.Ask == AvatarAsk.Accepted);
            if (!organizerOk) { plan.OrganizerId = DefaultOrganizer(request, ctx); Tell(plan, plan.OrganizerId); }
            if (!ctx.Venue(plan.VenueMapId, plan.VenueAreaId)) { plan.VenueMapId = ctx.DefaultMapId; plan.VenueAreaId = 0; }
            foreach (var invitee in plan.Invitees.Where(i => i.Status == InviteStatus.Invited).ToList())
            {
                var p = GetPerson(invitee.PersonId);
                if (p == null || !p.IsAlive) { invitee.Status = InviteStatus.Unavailable; invitee.Barriers |= InviteBarrier.Dead; }
                else if (!p.InColony && invitee.Source != InviteSource.Distant) { invitee.Status = InviteStatus.Unavailable; invitee.Barriers |= InviteBarrier.WrongMap; }
                else if (!p.InColony)
                {
                    int arrival = ctx.ArrivalEstimate == null ? -1 : ctx.Arrival(p.Id);
                    if (arrival < 0 || now + arrival > plan.ScheduledTick + PlanWindowTicks) { invitee.Status = InviteStatus.Unavailable; invitee.Barriers |= arrival < 0 ? InviteBarrier.CannotTravel : InviteBarrier.TooFarForWindow; }
                    else invitee.ArrivalTick = now + arrival;
                }
            }
        }

        private void RefreshArrivals(CeremonyPlan plan, int now)
        {
            foreach (var invitee in plan.Invitees.Where(i => i.Source == InviteSource.Distant && i.ArrivalTick > 0))
                if (invitee.ArrivalTick > plan.ScheduledTick) invitee.Barriers |= InviteBarrier.TooFarForWindow; else invitee.Barriers &= ~InviteBarrier.TooFarForWindow;
        }
    }
}
