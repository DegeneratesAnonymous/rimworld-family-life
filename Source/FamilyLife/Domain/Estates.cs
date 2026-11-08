using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // New values are always appended so earlier saves keep their meaning.
    public enum PersonalItemKind : byte { Weapon, Apparel, Book, Art, Other }
    public enum HeirloomBasis : byte { None, Use, Memory, Designated, Legacy, Recorded }
    public enum EstateStage : byte { Mourning, Claims, Discussion, Mediation, Resolved, Unresolved, Delivery }
    public enum ItemOutcome : byte { Pending, Assigned, Delivering, Delivered, Relinquished, Lost }
    public enum LostReason : byte { None, Destroyed, Missing, Unreachable, Forbidden, Held, Buried, Unanswered, NoHeir }
    public enum EstateAskKind : byte { Heir, Press, Mediate }
    public enum EstateAnswer : byte { Accept, Relinquish, Contest }
    public enum MediationChoice : byte { Approve, LetThemSettle, Give }

    [Flags]
    public enum ClaimBasis : ushort { None = 0, Kin = 1, Designation = 2, Bond = 4, Use = 8, Custom = 16, HouseHeirloom = 32 }

    /// <summary>
    /// One individually meaningful, non-fungible thing a person has made their own: a weapon, a garment, a book, a work of art.
    /// Identity is the game's thing id; there is never a live reference. Ordinary stacked supplies are deliberately never recorded: they stay communal.
    /// </summary>
    public sealed partial class PersonalItem
    {
        public int ThingId, OwnerId, HouseId, RecordedTick, LastNoteTick, HeldHours, Quality = -1;
        public string DefName = "", Label = "";
        public float Value;
        public PersonalItemKind Kind;
        public bool Heirloom;
        public HeirloomBasis Basis;
        public List<int> UsedBy = new List<int>();
    }

    /// <summary>A named heir, for everything a person owns (ThingId 0) or for one item. Who knew about it is part of the record.</summary>
    public sealed partial class Beneficiary
    {
        public int OwnerId, HeirId, ThingId, Tick;
        public List<int> KnownBy = new List<int>();
    }

    public sealed partial class EstateClaim
    {
        public int ClaimantId;
        public ClaimBasis Basis;
        public float Strength;
        public bool Raised, Relinquished, Dormant, Pressed;
    }

    public sealed partial class EstateItem
    {
        public int ThingId, HeirId, RecipientHouseId, ProposedHeirId, Attempts, NextTryTick, DeliveryStartTick, BlockedSince, HouseId, ClosedTick;
        public string DefName = "", Label = "";
        public PersonalItemKind Kind;
        public bool Heirloom, Contested;
        public float Value;
        public ItemOutcome Outcome;
        public LostReason Lost;
        public List<EstateClaim> Claims = new List<EstateClaim>();
        public List<int> UsedBy = new List<int>();
        public bool Terminal => Outcome == ItemOutcome.Delivered || Outcome == ItemOutcome.Relinquished || Outcome == ItemOutcome.Lost;
    }

    public sealed partial class EstateAsk
    {
        public int ItemThingId, PersonId, AskedTick, ExpiresTick;
        public EstateAskKind Kind;
        public bool Answered, Expired;
        public bool Open => !Answered && !Expired;
    }

    public sealed partial class EstateCase
    {
        public int Id, DeceasedId, FamilyId, OpenedTick, StageTick, ClosedTick, MediatorId;
        public EstateStage Stage;
        public bool MediatorAsked, Compromised;
        public int StewardAppointedId, GrievanceCount;
        public List<EstateItem> Items = new List<EstateItem>();
        public List<EstateAsk> Asks = new List<EstateAsk>();
        public bool Open => Stage <= EstateStage.Mediation || Stage == EstateStage.Delivery;
    }

    /// <summary>What the game tells the rules about the world. Everything is optional.</summary>
    public sealed class EstateContext
    {
        public Func<int, bool> IsAvatar;
    }

    public static class EstateTuning
    {
        public const int MournTicks = ActivityTuning.TicksPerDay * 2, ClaimTicks = ActivityTuning.TicksPerDay, DiscussionTicks = ActivityTuning.TicksPerDay * 2,
            MediationTicks = ActivityTuning.TicksPerDay, AskTicks = ActivityTuning.TicksPerDay * 3, CaseLifeTicks = ActivityTuning.TicksPerDay * 40,
            RetryTicks = ActivityTuning.TicksPerDay / 4, ForbiddenWaitTicks = ActivityTuning.TicksPerDay * 10, HeldWaitTicks = ActivityTuning.TicksPerDay * 10,
            CorpseWaitTicks = ActivityTuning.TicksPerDay * 8;
        public const int PossessionHours = 48, UseHeirloomHours = 15 * 24, MaxPersonalItems = 160, MaxItemsPerOwner = 14, MaxCaseItems = 12, MaxClaimsPerItem = 6,
            MaxEstateHistory = 24, MaxAttempts = 5, MaxAsks = 12, MaxNotedHoursPerVisit = 24;
        public const float ClaimFloor = .25f, ContestMargin = .15f, ContestFloor = .3f;
        public const float HeirloomValue = 80f, LegacyValue = 800f, LegacyQualityValue = 150f, LegacyArtValue = 400f;
        public const int GoodQuality = 3, ExcellentQuality = 4;
    }

    public sealed partial class FamilyRegistry
    {
        public List<PersonalItem> PersonalItems = new List<PersonalItem>();
        public List<Beneficiary> Beneficiaries = new List<Beneficiary>();
        public List<EstateCase> Estates = new List<EstateCase>();
        public int NextEstateId = 1;

        // ------------------------------------------------------------------ what a person owns

        public PersonalItem PersonalItemFor(int thingId) => PersonalItems.FirstOrDefault(i => i.ThingId == thingId);
        public IEnumerable<PersonalItem> PersonalItemsOf(int owner) => PersonalItems.Where(i => i.OwnerId == owner);
        public IEnumerable<PersonalItem> HouseHeirlooms(int house) => PersonalItems.Where(i => i.OwnerId == 0 && i.HouseId == house);

        /// <summary>True when an item is valuable or fine enough to be remembered as a legacy item even before it has a history.</summary>
        public static bool QualifiesAsLegacy(PersonalItemKind kind, float value, int quality)
        {
            if (value >= EstateTuning.LegacyValue) return true;
            if (kind == PersonalItemKind.Art && value >= EstateTuning.LegacyArtValue) return true;
            return quality >= EstateTuning.ExcellentQuality && value >= EstateTuning.LegacyQualityValue;
        }

        /// <summary>
        /// A person has come to hold something that is theirs alone. Only living colonists own things, and only a bounded number of things:
        /// the oldest ordinary item makes room, but heirlooms are never pushed out by ordinary items.
        /// </summary>
        public bool RecordPersonalItem(int thingId, string defName, string label, PersonalItemKind kind, int owner, float value, int quality, int now)
        {
            if (thingId <= 0 || GetPerson(owner)?.InColony != true || (kind == PersonalItemKind.Other && value < EstateTuning.HeirloomValue)) return false;
            var existing = PersonalItemFor(thingId);
            if (existing != null)
            {
                if (existing.OwnerId == owner) { existing.Label = label ?? existing.Label; existing.Value = value; existing.Quality = quality; return true; }
                return false;
            }
            if (PersonalItemsOf(owner).Count() >= EstateTuning.MaxItemsPerOwner && !EvictOrdinary(owner)) return false;
            if (PersonalItems.Count >= EstateTuning.MaxPersonalItems && !EvictOrdinary(0)) return false;
            var item = new PersonalItem { ThingId = thingId, DefName = defName ?? "", Label = label ?? "", Kind = kind, OwnerId = owner, Value = Math.Max(0f, value), Quality = quality, RecordedTick = now, LastNoteTick = now };
            if (QualifiesAsLegacy(kind, value, quality)) { item.Heirloom = true; item.Basis = HeirloomBasis.Legacy; }
            PersonalItems.Add(item);
            return true;
        }

        private bool EvictOrdinary(int owner)
        {
            var victim = PersonalItems.Where(i => !i.Heirloom && (owner == 0 || i.OwnerId == owner)).OrderBy(i => i.RecordedTick).ThenBy(i => i.ThingId).FirstOrDefault();
            if (victim == null) return false;
            PersonalItems.Remove(victim);
            return true;
        }

        public bool ForgetPersonalItem(int thingId)
        {
            bool removed = PersonalItems.RemoveAll(i => i.ThingId == thingId) > 0;
            Beneficiaries.RemoveAll(b => b.ThingId == thingId);
            return removed;
        }

        /// <summary>
        /// The pawn has been seen holding the item for some hours. Sustained use is how an ordinary garment or tool becomes an heirloom:
        /// fifteen days of carrying it, provided it is worth keeping. Hours are capped per visit so a long gap between visits cannot fake a history.
        /// </summary>
        public bool NoteItemHeld(int thingId, int owner, int hours, int now)
        {
            var item = PersonalItemFor(thingId);
            if (item == null || item.OwnerId != owner || hours <= 0) return false;
            item.HeldHours += Math.Min(hours, EstateTuning.MaxNotedHoursPerVisit);
            item.LastNoteTick = now;
            if (!item.Heirloom && item.HeldHours >= EstateTuning.UseHeirloomHours && (item.Value >= EstateTuning.HeirloomValue || item.Quality >= EstateTuning.GoodQuality))
            { item.Heirloom = true; item.Basis = HeirloomBasis.Use; }
            return true;
        }

        /// <summary>A significant family memory is tied to this item (a wedding, a coming of age): it is remembered as an heirloom.</summary>
        public bool MarkItemMemory(int thingId)
        {
            var item = PersonalItemFor(thingId);
            if (item == null) return false;
            if (!item.Heirloom) { item.Heirloom = true; item.Basis = HeirloomBasis.Memory; }
            return true;
        }

        /// <summary>The owner, or the head of the owner's house, declares an item a family heirloom.</summary>
        public bool DesignateHeirloom(int thingId, int by, int now)
        {
            var item = PersonalItemFor(thingId);
            if (item == null || GetPerson(by)?.InColony != true) return false;
            int house = item.OwnerId == 0 ? item.HouseId : FamilyOf(item.OwnerId);
            if (house == 0 || !(by == item.OwnerId || HeadOf(house) == by)) return false;
            item.HouseId = house;
            if (!item.Heirloom || item.Basis != HeirloomBasis.Designated) { item.Heirloom = true; item.Basis = HeirloomBasis.Designated; }
            return true;
        }

        /// <summary>The previous holder of an item is remembered as someone who used and cared for it.</summary>
        private static void RememberUser(PersonalItem item, int person)
        {
            if (person == 0 || item.UsedBy.Contains(person)) return;
            item.UsedBy.Add(person);
            while (item.UsedBy.Count > 4) item.UsedBy.RemoveAt(0);
        }

        // ------------------------------------------------------------------ naming an heir

        /// <summary>
        /// An owner names who should have one thing (thingId) or everything personal (0). The heir must be a living adult colonist other than the owner;
        /// who was told is recorded, because a designation nobody knew about carries no weight with anyone but the owner.
        /// </summary>
        public bool DesignateBeneficiary(int owner, int heir, int thingId, IEnumerable<int> told, int now)
        {
            var o = GetPerson(owner); var h = GetPerson(heir);
            if (o?.InColony != true || h?.InColony != true || h.Juvenile || owner == heir) return false;
            if (thingId != 0 && PersonalItemFor(thingId)?.OwnerId != owner) return false;
            Beneficiaries.RemoveAll(b => b.OwnerId == owner && b.ThingId == thingId);
            while (Beneficiaries.Count >= 96) Beneficiaries.RemoveAt(0);
            Beneficiaries.Add(new Beneficiary { OwnerId = owner, HeirId = heir, ThingId = thingId, Tick = now,
                KnownBy = new[] { owner }.Concat(told ?? Enumerable.Empty<int>()).Where(id => GetPerson(id) != null).Distinct().Take(16).ToList() });
            return true;
        }

        public bool RevokeBeneficiary(int owner, int thingId) => Beneficiaries.RemoveAll(b => b.OwnerId == owner && b.ThingId == thingId) > 0;

        /// <summary>The designation that applies to one item: a specific one beats a general one, and it counts only if the heir was told (or is the one who knew) and is still fit to inherit.</summary>
        public Beneficiary ValidBeneficiary(int owner, int thingId)
        {
            foreach (var b in Beneficiaries.Where(b => b.OwnerId == owner && (b.ThingId == thingId || b.ThingId == 0)).OrderByDescending(b => b.ThingId != 0).ThenByDescending(b => b.Tick))
            {
                var heir = GetPerson(b.HeirId);
                if (heir?.InColony != true || heir.Juvenile) continue;
                bool known = b.KnownBy.Contains(b.HeirId) || b.KnownBy.Count > 1;
                if (known) return b;
            }
            return null;
        }

        // ------------------------------------------------------------------ opening an estate

        /// <summary>People whose death has left recorded things behind and whose estate has not been opened yet.</summary>
        public List<int> OwnersAwaitingEstate() => PersonalItems.Where(i => i.OwnerId != 0 && GetPerson(i.OwnerId)?.Presence == Presence.Dead && !Estates.Any(e => e.DeceasedId == i.OwnerId))
            .Select(i => i.OwnerId).Distinct().OrderBy(id => id).Take(4).ToList();

        /// <summary>
        /// A person died. The estate holds only the things the game confirms still exist; everything else on their record is simply dropped,
        /// and nothing is ever made up to take its place. Opening twice returns the same case.
        /// </summary>
        public EstateCase OpenEstate(int deceased, Func<int, bool> resolvable, int now)
        {
            var existing = Estates.FirstOrDefault(e => e.DeceasedId == deceased);
            if (existing != null) return existing;
            var person = GetPerson(deceased);
            if (person == null || person.IsAlive) return null;
            var mine = PersonalItemsOf(deceased).OrderByDescending(i => i.Heirloom).ThenByDescending(i => i.Value).ThenBy(i => i.ThingId).ToList();
            var real = mine.Where(i => resolvable == null || resolvable(i.ThingId)).Take(EstateTuning.MaxCaseItems).ToList();
            foreach (var gone in mine) PersonalItems.Remove(gone);
            Beneficiaries.RemoveAll(b => b.OwnerId == deceased && !real.Any(i => i.ThingId == b.ThingId) && b.ThingId != 0);
            if (real.Count == 0) return null;
            while (Estates.Count(e => !e.Open) >= EstateTuning.MaxEstateHistory)
            {
                var old = Estates.Where(e => !e.Open && e.Stage != EstateStage.Unresolved).OrderBy(e => e.ClosedTick).FirstOrDefault() ?? Estates.Where(e => !e.Open).OrderBy(e => e.ClosedTick).First();
                Estates.Remove(old);
            }
            var estate = new EstateCase { Id = NextEstateId++, DeceasedId = deceased, FamilyId = FamilyOf(deceased), OpenedTick = now, StageTick = now, Stage = EstateStage.Mourning };
            foreach (var item in real)
                estate.Items.Add(new EstateItem { ThingId = item.ThingId, DefName = item.DefName, Label = item.Label, Kind = item.Kind, Heirloom = item.Heirloom, Value = item.Value, HouseId = item.HouseId, UsedBy = item.UsedBy.ToList() });
            Estates.Add(estate);
            return estate;
        }

        public EstateCase EstateFor(int id) => Estates.FirstOrDefault(e => e.Id == id);
        public EstateCase EstateOf(int deceased) => Estates.FirstOrDefault(e => e.DeceasedId == deceased);
        public IEnumerable<EstateCase> OpenEstates() => Estates.Where(e => e.Open);
        /// <summary>Items that are rightfully this person's and ready to be picked up, with no question still waiting on them.</summary>
        public List<(EstateCase Case, EstateItem Item)> ReadyToCollect(int person) =>
            Estates.Where(e => e.Stage == EstateStage.Delivery || e.Stage == EstateStage.Unresolved)
                .SelectMany(e => e.Items.Where(i => i.HeirId == person && (i.Outcome == ItemOutcome.Assigned || i.Outcome == ItemOutcome.Delivering) && !WaitingOnAnswer(e, i)).Select(i => (e, i))).ToList();

        public EstateCase EstateCaseOfItem(int thingId) => Estates.FirstOrDefault(e => e.Items.Any(i => i.ThingId == thingId && !i.Terminal));

        /// <summary>An item changed hands in life (a gift, a trade between colonists). The earlier holder is remembered as someone who used it; a new holder starts a new history.</summary>
        public bool TransferPersonalItem(int thingId, int newOwner, int now)
        {
            var item = PersonalItemFor(thingId);
            if (item == null || GetPerson(newOwner)?.InColony != true || item.OwnerId == newOwner) return false;
            if (PersonalItemsOf(newOwner).Count() >= EstateTuning.MaxItemsPerOwner && !EvictOrdinary(newOwner)) return false;
            if (item.OwnerId != 0) RememberUser(item, item.OwnerId);
            item.OwnerId = newOwner; item.HeldHours = 0; item.LastNoteTick = now;
            return true;
        }

        public EstateItem EstateItemFor(int thingId) => Estates.SelectMany(e => e.Items).FirstOrDefault(i => i.ThingId == thingId && !i.Terminal);

        /// <summary>The deceased's relatives, then the house: the people who could reasonably expect something, whether or not they are entitled to it.</summary>
        private List<(int Id, ClaimBasis Basis, float Kin)> Candidates(EstateCase estate, EstateItem item)
        {
            var found = new Dictionary<int, (ClaimBasis Basis, float Kin)>();
            void Add(int id, float kin, ClaimBasis basis)
            {
                var p = GetPerson(id);
                if (id == estate.DeceasedId || p?.InColony != true || p.Juvenile) return;        // present and capable of receiving it
                if (found.TryGetValue(id, out var cur)) found[id] = (cur.Basis | basis, Math.Max(cur.Kin, kin));
                else found[id] = (basis, kin);
            }
            var d = GetPerson(estate.DeceasedId);
            foreach (int partner in d.PartnerIds()) Add(partner, d.SpouseIds.Contains(partner) ? .55f : .45f, ClaimBasis.Kin);
            foreach (int child in ChildrenOf(estate.DeceasedId)) Add(child, .5f, ClaimBasis.Kin);
            foreach (int parent in d.ParentIds) Add(parent, .35f, ClaimBasis.Kin);
            foreach (int sibling in SiblingsOf(estate.DeceasedId)) Add(sibling, .3f, ClaimBasis.Kin);
            foreach (int child in ChildrenOf(estate.DeceasedId)) foreach (int grand in ChildrenOf(child)) Add(grand, .2f, ClaimBasis.Kin);
            var designee = ValidBeneficiaryAtDeath(estate.DeceasedId, item.ThingId);
            if (designee != null) Add(designee.HeirId, 0f, ClaimBasis.None);
            foreach (int user in item.UsedBy) Add(user, 0f, ClaimBasis.None);
            if (item.HouseId != 0)
            {
                int head = HeadOf(item.HouseId);
                if (head != 0 && head != estate.DeceasedId) Add(head, 0f, ClaimBasis.HouseHeirloom);
            }
            return found.Select(kv => (kv.Key, kv.Value.Basis, kv.Value.Kin)).OrderBy(x => x.Key).ToList();
        }

        // The designation record outlives the owner's presence; it is only checked at the time of death.
        private Beneficiary ValidBeneficiaryAtDeath(int owner, int thingId)
        {
            foreach (var b in Beneficiaries.Where(b => b.OwnerId == owner && (b.ThingId == thingId || b.ThingId == 0)).OrderByDescending(b => b.ThingId != 0).ThenByDescending(b => b.Tick))
            {
                var heir = GetPerson(b.HeirId);
                if (heir?.InColony != true || heir.Juvenile) continue;
                if (b.KnownBy.Contains(b.HeirId) || b.KnownBy.Count > 1) return b;
            }
            return null;
        }

        /// <summary>The strength and reasons of each person's claim to one item. Pure: nothing changes.</summary>
        public List<EstateClaim> WeighClaims(EstateCase estate, EstateItem item)
        {
            var candidates = Candidates(estate, item);
            var designee = ValidBeneficiaryAtDeath(estate.DeceasedId, item.ThingId);
            var family = estate.FamilyId;
            var custom = family == 0 ? SuccessionCustom.Seniority : CustomOf(family);
            var adults = candidates.Select(c => GetPerson(c.Id)).Where(p => p != null).ToList();
            int oldest = adults.OrderBy(p => p.BirthAbsTick).ThenBy(p => p.Id).Select(p => p.Id).FirstOrDefault();
            int eldestChild = candidates.Where(c => ChildrenOf(estate.DeceasedId).Contains(c.Id)).Select(c => GetPerson(c.Id)).OrderBy(p => p.BirthAbsTick).ThenBy(p => p.Id).Select(p => p.Id).FirstOrDefault();
            int warmest = candidates.OrderByDescending(c => GetBond(c.Id, estate.DeceasedId)?.Attachment ?? 0f).ThenBy(c => c.Id).Select(c => c.Id).FirstOrDefault();
            var users = item.UsedBy;
            var claims = new List<EstateClaim>();
            foreach (var c in candidates)
            {
                var basis = c.Basis; float strength = c.Kin;
                var bond = GetBond(c.Id, estate.DeceasedId);
                if (bond != null) { float b = bond.Attachment / 100f * .25f - bond.Tension / 100f * .12f; strength += b; if (bond.Attachment >= 40f) basis |= ClaimBasis.Bond; }
                if (designee != null && designee.HeirId == c.Id) { strength += .6f; basis |= ClaimBasis.Designation; }
                if (users.Contains(c.Id)) { strength += .15f; basis |= ClaimBasis.Use; }
                if (basis.HasFlag(ClaimBasis.HouseHeirloom)) strength += .2f;
                float bonus = 0f;
                switch (custom)
                {
                    case SuccessionCustom.Seniority: if (c.Id == oldest) bonus = .08f; break;
                    case SuccessionCustom.Primogeniture: if (c.Id == eldestChild) bonus = .1f; break;
                    case SuccessionCustom.Designation: if (designee != null && designee.HeirId == c.Id) bonus = .08f; break;
                    case SuccessionCustom.Consensus: if (c.Id == warmest) bonus = .08f; break;
                    case SuccessionCustom.Merit: if (TopSkillRank(c.Id, candidates.Select(x => x.Id)) == 0) bonus = .08f; break;
                }
                if (bonus > 0f) { strength += bonus; basis |= ClaimBasis.Custom; }
                strength = Math.Max(0f, Math.Min(1.5f, strength));
                if (strength < EstateTuning.ClaimFloor) continue;
                claims.Add(new EstateClaim { ClaimantId = c.Id, Basis = basis, Strength = strength, Raised = true });
            }
            return claims.OrderByDescending(c => c.Strength).ThenBy(c => c.ClaimantId).Take(EstateTuning.MaxClaimsPerItem).ToList();
        }

        private int TopSkillRank(int person, IEnumerable<int> among)
        {
            var ordered = among.Select(GetPerson).Where(p => p != null).OrderByDescending(p => p.TopSkillLevel).ThenBy(p => p.Id).Select(p => p.Id).ToList();
            return ordered.IndexOf(person);
        }

        // ------------------------------------------------------------------ the stages

        private static int Seed(EstateCase estate, int thing) => unchecked((estate.Id * 7919) ^ (thing * 104729)) & int.MaxValue;

        /// <summary>
        /// Moves every open case along. Safe to call at any time and as often as wanted: each stage depends only on the time spent in it and on what the
        /// case already records, so repeating a call changes nothing.
        /// </summary>
        public void AdvanceEstates(int now, EstateContext ctx = null)
        {
            ctx = ctx ?? new EstateContext();
            foreach (var estate in Estates.Where(e => e.Open || e.Stage == EstateStage.Unresolved).ToList())
            {
                ExpireAsks(estate, now);
                switch (estate.Stage)
                {
                    case EstateStage.Mourning:
                        if (now - estate.StageTick >= EstateTuning.MournTicks) RaiseClaims(estate, now, ctx);
                        break;
                    case EstateStage.Claims:
                        if (now - estate.StageTick >= EstateTuning.ClaimTicks) EndClaims(estate, now, ctx);
                        break;
                    case EstateStage.Discussion:
                        if (now - estate.StageTick >= EstateTuning.DiscussionTicks) EndDiscussion(estate, now, ctx);
                        break;
                    case EstateStage.Mediation:
                        AdvanceMediation(estate, now, ctx);
                        break;
                    case EstateStage.Delivery:
                    case EstateStage.Unresolved:
                        SettleDelivery(estate, now, ctx);
                        break;
                }
            }
        }

        private void SetStage(EstateCase estate, EstateStage stage, int now) { estate.Stage = stage; estate.StageTick = now; if (!estate.Open && stage != EstateStage.Unresolved) estate.ClosedTick = now; }

        private void RaiseClaims(EstateCase estate, int now, EstateContext ctx)
        {
            foreach (var item in estate.Items)
            {
                if (item.Terminal) continue;
                item.Claims = WeighClaims(estate, item);
                var lead = item.Claims.FirstOrDefault();
                var second = item.Claims.Skip(1).FirstOrDefault();
                item.Contested = item.Heirloom && lead != null && second != null && second.Strength >= EstateTuning.ContestFloor &&
                                 (lead.Strength - second.Strength < EstateTuning.ContestMargin || (lead.Strength - second.Strength < .3f && Rivals(second.ClaimantId, lead.ClaimantId)));
                if (lead == null) { ToHouse(estate, item, now); continue; }
                if (!item.Contested) Assign(estate, item, lead.ClaimantId, now, ctx);
                else item.ProposedHeirId = lead.ClaimantId;
                // The one playing the avatar is asked about a claim that is rightfully theirs; the rest are decided by the pawns' own positions.
                foreach (var c in item.Claims.Take(2).Where(c => ctx.IsAvatar != null && ctx.IsAvatar(c.ClaimantId)))
                    AddAsk(estate, item.ThingId, c.ClaimantId, item.Contested || c != lead ? EstateAskKind.Press : EstateAskKind.Heir, now);
            }
            SetStage(estate, EstateStage.Claims, now);
        }

        private bool Rivals(int a, int b) => (GetBond(a, b)?.Tension ?? 0f) >= 40f || OpenRiftBetween(a, b) != null;

        /// <summary>No one is in a position to inherit: the house keeps it, and if there is no house either, it is recorded as having no heir.</summary>
        private void ToHouse(EstateCase estate, EstateItem item, int now)
        {
            item.Contested = false; item.HeirId = 0;
            int house = HouseFor(estate);
            if (house == 0) { ReportItemLost(estate.Id, item.ThingId, LostReason.NoHeir, now); return; }
            item.RecipientHouseId = house; item.Outcome = ItemOutcome.Assigned;
        }

        private int HouseFor(EstateCase estate) => estate.FamilyId != 0 && IsActiveHouse(estate.FamilyId) ? estate.FamilyId : 0;

        private void AddAsk(EstateCase estate, int thing, int person, EstateAskKind kind, int now)
        {
            if (estate.Asks.Any(a => a.Open && a.PersonId == person && a.ItemThingId == thing && a.Kind == kind)) return;
            if (estate.Asks.Count >= EstateTuning.MaxAsks) return;
            estate.Asks.Add(new EstateAsk { ItemThingId = thing, PersonId = person, Kind = kind, AskedTick = now, ExpiresTick = now + EstateTuning.AskTicks });
        }

        /// <summary>A question nobody answered is not an answer. It lapses, the claim goes quiet, and everyone else carries on without it.</summary>
        private void ExpireAsks(EstateCase estate, int now)
        {
            foreach (var ask in estate.Asks.Where(a => a.Open && now >= a.ExpiresTick))
            {
                ask.Expired = true;
                if (ask.Kind == EstateAskKind.Mediate) { estate.MediatorAsked = false; if (estate.MediatorId == ask.PersonId) estate.MediatorId = -1; continue; }
                var item = estate.Items.FirstOrDefault(i => i.ThingId == ask.ItemThingId);
                var claim = item?.Claims.FirstOrDefault(c => c.ClaimantId == ask.PersonId);
                if (claim != null) claim.Dormant = true;
            }
        }

        private void EndClaims(EstateCase estate, int now, EstateContext ctx)
        {
            if (estate.Items.Any(i => i.Contested && !i.Terminal)) SetStage(estate, EstateStage.Discussion, now);
            else SetStage(estate, EstateStage.Delivery, now);
        }

        /// <summary>The family talks it over. Those whose claim is clearly weaker and who are not at odds with the leader let it go; the rest stand.</summary>
        private void EndDiscussion(EstateCase estate, int now, EstateContext ctx)
        {
            foreach (var item in estate.Items.Where(i => i.Contested && !i.Terminal))
            {
                var lead = item.Claims.Where(c => !c.Relinquished && !c.Dormant).OrderByDescending(c => c.Strength).ThenBy(c => c.ClaimantId).FirstOrDefault();
                if (lead == null) continue;
                foreach (var c in item.Claims.Where(c => c != lead && !c.Relinquished && !c.Dormant && !(ctx.IsAvatar != null && ctx.IsAvatar(c.ClaimantId))))
                {
                    float tension = GetBond(c.ClaimantId, lead.ClaimantId)?.Tension ?? 0f;
                    bool rival = tension >= 40f || OpenRiftBetween(c.ClaimantId, lead.ClaimantId) != null;
                    if (c.Strength < lead.Strength * .75f && !rival && !c.Pressed) c.Relinquished = true;
                    else if (!rival && !c.Pressed && Seed(estate, item.ThingId + c.ClaimantId) % 100 < 35) c.Relinquished = true;
                }
                var standing = item.Claims.Where(c => !c.Relinquished && !c.Dormant).ToList();
                if (standing.Count <= 1 && standing.Count == 1) { item.Contested = false; Assign(estate, item, standing[0].ClaimantId, now, ctx); }
            }
            if (estate.Items.Any(i => i.Contested && !i.Terminal)) { SetStage(estate, EstateStage.Mediation, now); }
            else SetStage(estate, EstateStage.Delivery, now);
        }

        private void AdvanceMediation(EstateCase estate, int now, EstateContext ctx)
        {
            var contested = estate.Items.Where(i => i.Contested && !i.Terminal).ToList();
            if (contested.Count == 0) { SetStage(estate, EstateStage.Delivery, now); return; }
            if (estate.MediatorId == 0)
            {
                estate.MediatorId = ChooseMediator(estate, contested);
                if (estate.MediatorId > 0 && ctx.IsAvatar != null && ctx.IsAvatar(estate.MediatorId))
                {
                    AddAsk(estate, 0, estate.MediatorId, EstateAskKind.Mediate, now);
                    estate.MediatorAsked = true;
                    foreach (var item in contested) item.ProposedHeirId = Proposal(estate, contested)[item.ThingId];
                    return;
                }
            }
            if (estate.MediatorAsked && estate.Asks.Any(a => a.Open && a.Kind == EstateAskKind.Mediate)) return;      // waiting for the person playing the mediator
            if (now - estate.StageTick < EstateTuning.MediationTicks) return;
            Settle(estate, now, ctx);
        }

        /// <summary>The head if they have no stake, else a Steward, else whoever the claimants trust most. Never a claimant on the items in dispute.</summary>
        private int ChooseMediator(EstateCase estate, List<EstateItem> contested)
        {
            var claimants = new HashSet<int>(contested.SelectMany(i => i.Claims).Select(c => c.ClaimantId));
            bool Able(int id) { var p = GetPerson(id); return p?.InColony == true && !p.Juvenile && !claimants.Contains(id) && id != estate.DeceasedId; }
            int family = estate.FamilyId;
            int head = family == 0 ? 0 : HeadOf(family);
            if (head != 0 && Able(head)) return head;
            var house = family == 0 ? null : GetLineage(family);
            var steward = house?.Stewards.Where(Able).OrderBy(id => id).FirstOrDefault() ?? 0;
            if (steward != 0) return steward;
            var pool = (family == 0 ? new List<PersonRecord>() : MembersOf(family)).Where(p => Able(p.Id)).ToList();
            var best = pool.OrderByDescending(p => claimants.Average(c => GetBond(c, p.Id)?.Trust ?? 25f)).ThenBy(p => p.Id).FirstOrDefault();
            return best?.Id ?? -1;
        }

        /// <summary>A fair spread of the contested things: each claimant gets one before anyone gets two, preferring the stronger claim for the more valuable item.</summary>
        public Dictionary<int, int> Proposal(EstateCase estate, List<EstateItem> contested = null)
        {
            contested = contested ?? estate.Items.Where(i => i.Contested && !i.Terminal).ToList();
            var result = new Dictionary<int, int>();
            var awarded = new Dictionary<int, int>();
            foreach (var item in contested.OrderByDescending(i => i.Value).ThenBy(i => i.ThingId))
            {
                var best = item.Claims.Where(c => !c.Relinquished && !c.Dormant)
                    .OrderByDescending(c => c.Strength - .2f * (awarded.TryGetValue(c.ClaimantId, out var n) ? n : 0)).ThenBy(c => c.ClaimantId).FirstOrDefault();
                result[item.ThingId] = best?.ClaimantId ?? 0;
                if (best != null) awarded[best.ClaimantId] = (awarded.TryGetValue(best.ClaimantId, out var m) ? m : 0) + 1;
            }
            return result;
        }

        /// <summary>The person playing the mediator answers. Approving applies the suggested spread; giving one item to a claimant settles that item; letting them settle it leaves it to the family.</summary>
        public bool AnswerMediation(int estateId, int mediator, MediationChoice choice, int thingId, int heir, int now, EstateContext ctx = null)
        {
            var estate = EstateFor(estateId);
            var ask = estate?.Asks.FirstOrDefault(a => a.Open && a.Kind == EstateAskKind.Mediate && a.PersonId == mediator);
            if (ask == null || estate.Stage != EstateStage.Mediation) return false;
            ctx = ctx ?? new EstateContext();
            var contested = estate.Items.Where(i => i.Contested && !i.Terminal).ToList();
            switch (choice)
            {
                case MediationChoice.Approve: ask.Answered = true; estate.MediatorAsked = false; Settle(estate, now, ctx, approved: true); return true;
                case MediationChoice.LetThemSettle: ask.Answered = true; estate.MediatorAsked = false; estate.MediatorId = -1; Settle(estate, now, ctx); return true;
                default:
                    var item = contested.FirstOrDefault(i => i.ThingId == thingId);
                    if (item == null || !item.Claims.Any(c => c.ClaimantId == heir && !c.Relinquished)) return false;
                    item.ProposedHeirId = heir;
                    estate.Compromised = true;
                    ApplyAward(estate, item, heir, now, ctx);
                    if (!estate.Items.Any(i => i.Contested && !i.Terminal)) { ask.Answered = true; estate.MediatorAsked = false; SetStage(estate, EstateStage.Delivery, now); }
                    return true;
            }
        }

        private void Settle(EstateCase estate, int now, EstateContext ctx, bool approved = false)
        {
            var contested = estate.Items.Where(i => i.Contested && !i.Terminal).ToList();
            var spread = Proposal(estate, contested);
            estate.Compromised = approved || estate.MediatorId > 0;
            foreach (var item in contested)
            {
                int heir = approved && item.ProposedHeirId != 0 && item.Claims.Any(c => c.ClaimantId == item.ProposedHeirId) ? item.ProposedHeirId : spread[item.ThingId];
                if (heir == 0) { ToHouse(estate, item, now); continue; }
                ApplyAward(estate, item, heir, now, ctx);
            }
            SetStage(estate, EstateStage.Delivery, now);
        }

        /// <summary>The settled award, and what it costs: the loser of a close claim is either given standing in the house or keeps a lasting grievance.</summary>
        private void ApplyAward(EstateCase estate, EstateItem item, int winner, int now, EstateContext ctx)
        {
            item.Contested = false;
            Assign(estate, item, winner, now, ctx);
            var witnesses = item.Claims.Select(c => c.ClaimantId).Append(estate.MediatorId > 0 ? estate.MediatorId : winner).Distinct().ToList();
            foreach (var loser in item.Claims.Where(c => c.ClaimantId != winner && !c.Relinquished && !c.Dormant).ToList())
            {
                int family = FamilyOf(loser.ClaimantId);
                bool overlooked = loser.Strength >= .5f && family != 0 && family == FamilyOf(winner) && estate.StewardAppointedId == 0 && !(GetLineage(family)?.Stewards.Contains(loser.ClaimantId) ?? true)
                                  && HeadOf(family) != 0 && HeadOf(family) != loser.ClaimantId && DelegateHouse(HeadOf(family), loser.ClaimantId, family, true, now);
                if (overlooked) { estate.StewardAppointedId = loser.ClaimantId; ChangeBond(loser.ClaimantId, winner, new BondChange(-.5f, 0f, 1f, 0f), now); continue; }
                float weight = Math.Min(1.5f, loser.Strength);
                ChangeBond(loser.ClaimantId, winner, new BondChange(-2f * weight, -3f * weight, 5f * weight, 0f), now);
                RecordIncident(IncidentKind.Refusal, winner, loser.ClaimantId, 0, now, 10f, witnesses);
                estate.GrievanceCount++;
            }
        }

        /// <summary>Gives one item to a person, or to the house when nobody is in a position to take it.</summary>
        private void Assign(EstateCase estate, EstateItem item, int heir, int now, EstateContext ctx)
        {
            item.HeirId = heir; item.RecipientHouseId = 0; item.ProposedHeirId = 0;
            item.Outcome = ItemOutcome.Assigned;
            // The person playing the heir decides for themselves: until they say yes, the item is not delivered.
            if (ctx.IsAvatar != null && ctx.IsAvatar(heir) && !item.Contested)
            {
                AddAsk(estate, item.ThingId, heir, EstateAskKind.Heir, now);
            }
        }

        /// <summary>The person playing an heir accepts, relinquishes or presses a claim. Only their own claim and only while the question is open (or after it lapsed, if the case is still unsettled).</summary>
        public bool AnswerHeirAsk(int estateId, int thingId, int person, EstateAnswer answer, int now, EstateContext ctx = null)
        {
            var estate = EstateFor(estateId);
            var item = estate?.Items.FirstOrDefault(i => i.ThingId == thingId);
            var claim = item?.Claims.FirstOrDefault(c => c.ClaimantId == person);
            if (item == null || claim == null || item.Terminal || !(estate.Open || estate.Stage == EstateStage.Unresolved)) return false;
            var ask = estate.Asks.Where(a => a.PersonId == person && a.ItemThingId == thingId && a.Kind != EstateAskKind.Mediate).OrderByDescending(a => a.AskedTick).FirstOrDefault();
            if (ask == null) return false;
            ctx = ctx ?? new EstateContext();
            ask.Answered = true; ask.Expired = false;
            claim.Dormant = false;
            switch (answer)
            {
                case EstateAnswer.Accept:
                    if (item.Contested || item.HeirId != person && item.Outcome != ItemOutcome.Pending) { claim.Pressed = true; return true; }
                    item.HeirId = person; item.Outcome = ItemOutcome.Assigned;
                    break;
                case EstateAnswer.Relinquish:
                    claim.Relinquished = true;
                    if (item.HeirId == person) { item.HeirId = 0; item.Outcome = ItemOutcome.Pending; ReassignAfterRelinquish(estate, item, now, ctx); }
                    break;
                default:
                    claim.Pressed = true;
                    if (!item.Contested && item.Heirloom && item.Outcome <= ItemOutcome.Assigned && (estate.Stage == EstateStage.Claims || estate.Stage == EstateStage.Discussion))
                    {
                        item.Contested = true;
                        if (item.HeirId != 0 && item.HeirId != person) { item.ProposedHeirId = item.HeirId; item.HeirId = 0; item.Outcome = ItemOutcome.Pending; }
                    }
                    break;
            }
            return true;
        }

        private void ReassignAfterRelinquish(EstateCase estate, EstateItem item, int now, EstateContext ctx)
        {
            var next = item.Claims.Where(c => !c.Relinquished && !c.Dormant).OrderByDescending(c => c.Strength).ThenBy(c => c.ClaimantId).FirstOrDefault();
            if (next != null) Assign(estate, item, next.ClaimantId, now, ctx);
            else ToHouse(estate, item, now);
        }

        // ------------------------------------------------------------------ physical delivery

        /// <summary>Items ready for the game to try to deliver, each at most once in a retry interval, and never one still waiting on a question or a dispute.</summary>
        public List<(EstateCase Case, EstateItem Item)> DeliveriesDue(int now, int max)
        {
            var due = new List<(EstateCase, EstateItem)>();
            foreach (var estate in Estates.Where(e => e.Stage == EstateStage.Delivery || e.Stage == EstateStage.Unresolved))
                foreach (var item in estate.Items.Where(i => (i.Outcome == ItemOutcome.Assigned || i.Outcome == ItemOutcome.Delivering) && now >= i.NextTryTick && !WaitingOnAnswer(estate, item: i)))
                {
                    due.Add((estate, item));
                    if (due.Count >= max) return due;
                }
            return due;
        }

        private static bool WaitingOnAnswer(EstateCase estate, EstateItem item) => item.Claims.Any(c => c.ClaimantId == item.HeirId && c.Dormant) || estate.Asks.Any(a => a.Open && a.ItemThingId == item.ThingId && a.PersonId == item.HeirId);

        /// <summary>The game has started someone carrying the item to its heir.</summary>
        public bool BeginDelivery(int estateId, int thingId, int now)
        {
            var item = EstateFor(estateId)?.Items.FirstOrDefault(i => i.ThingId == thingId);
            if (item == null || (item.Outcome != ItemOutcome.Assigned && item.Outcome != ItemOutcome.Delivering)) return false;
            if (item.Outcome == ItemOutcome.Assigned) { item.Outcome = ItemOutcome.Delivering; item.DeliveryStartTick = now; }
            item.NextTryTick = Math.Max(item.NextTryTick, now + EstateTuning.RetryTicks);      // one job at a time; the next try waits for this one to finish or fail
            return true;
        }

        /// <summary>
        /// The item is in its heir's hands (or in the house's place). The game must report who actually holds it: a record is never enough, and nobody can
        /// inherit from a distance. Reporting twice does nothing the second time.
        /// </summary>
        public bool CompleteInheritance(int estateId, int thingId, int holder, int now)
        {
            var estate = EstateFor(estateId);
            var item = estate?.Items.FirstOrDefault(i => i.ThingId == thingId);
            if (item == null || item.Terminal || (item.Outcome != ItemOutcome.Assigned && item.Outcome != ItemOutcome.Delivering)) return false;
            item.BlockedSince = 0;
            if (item.HeirId != 0)
            {
                if (holder != item.HeirId || GetPerson(holder)?.InColony != true) return false;
                var record = new PersonalItem { ThingId = item.ThingId, DefName = item.DefName, Label = item.Label, Kind = item.Kind, OwnerId = holder, Value = item.Value, Heirloom = item.Heirloom,
                    Basis = item.Heirloom ? HeirloomBasis.Recorded : HeirloomBasis.None, RecordedTick = now, LastNoteTick = now, HouseId = item.HouseId };
                RememberUser(record, estate.DeceasedId);
                if (PersonalItemFor(item.ThingId) == null) PersonalItems.Add(record);
                ChangeBond(holder, estate.DeceasedId, new BondChange(1f, 1f, -1f, 0f), now);
            }
            else if (item.RecipientHouseId != 0 && holder == 0)
            {
                if (PersonalItemFor(item.ThingId) == null && PersonalItems.Count < EstateTuning.MaxPersonalItems && item.Heirloom)
                    PersonalItems.Add(new PersonalItem { ThingId = item.ThingId, DefName = item.DefName, Label = item.Label, Kind = item.Kind, OwnerId = 0, HouseId = item.RecipientHouseId, Value = item.Value, Heirloom = true, Basis = HeirloomBasis.Recorded, RecordedTick = now, LastNoteTick = now });
            }
            else return false;
            item.Outcome = ItemOutcome.Delivered; item.ClosedTick = now;
            CloseIfDone(estate, now);
            return true;
        }

        /// <summary>The item was destroyed, buried, sold or taken away: the inheritance is unfulfilled. It is never replaced.</summary>
        public bool ReportItemLost(int estateId, int thingId, LostReason reason, int now)
        {
            var estate = EstateFor(estateId);
            var item = estate?.Items.FirstOrDefault(i => i.ThingId == thingId);
            if (item == null || item.Terminal) return false;
            item.Outcome = ItemOutcome.Lost; item.Lost = reason == LostReason.None ? LostReason.Missing : reason; item.ClosedTick = now; item.Contested = false;
            estate.Asks.RemoveAll(a => a.Open && a.ItemThingId == thingId);
            CloseIfDone(estate, now);
            return true;
        }

        /// <summary>
        /// A try failed because the item cannot be reached or used right now. The next try waits longer each time, and after enough tries the
        /// inheritance is recorded as unfulfilled instead of being searched for forever.
        /// </summary>
        public bool ReportDeliveryBlocked(int estateId, int thingId, LostReason cause, bool counts, int now)
        {
            var estate = EstateFor(estateId);
            var item = estate?.Items.FirstOrDefault(i => i.ThingId == thingId);
            if (item == null || item.Terminal) return false;
            if (counts) item.Attempts++;
            if (item.BlockedSince == 0) item.BlockedSince = now;
            item.NextTryTick = now + EstateTuning.RetryTicks * (1 << Math.Min(3, item.Attempts));
            if (item.Outcome == ItemOutcome.Delivering) item.Outcome = ItemOutcome.Assigned;
            int limit = cause == LostReason.Forbidden ? EstateTuning.ForbiddenWaitTicks : cause == LostReason.Held ? EstateTuning.HeldWaitTicks : cause == LostReason.Buried ? EstateTuning.CorpseWaitTicks : int.MaxValue;
            if (item.Attempts >= EstateTuning.MaxAttempts || now - item.BlockedSince > limit)
                return ReportItemLost(estateId, thingId, cause == LostReason.None ? LostReason.Unreachable : cause, now);
            return true;
        }

        private void SettleDelivery(EstateCase estate, int now, EstateContext ctx)
        {
            // Gone-quiet claims: when the whole case has waited far too long, what is still open is left as it is and the case records that.
            if (now - estate.OpenedTick > EstateTuning.CaseLifeTicks)
            {
                foreach (var item in estate.Items.Where(i => !i.Terminal))
                {
                    if (WaitingOnAnswer(estate, item)) continue;
                    ReportItemLost(estate.Id, item.ThingId, LostReason.Missing, now);
                }
                if (estate.Items.Any(i => !i.Terminal) && estate.Stage != EstateStage.Unresolved) SetStage(estate, EstateStage.Unresolved, now);
            }
            CloseIfDone(estate, now);
        }

        private void CloseIfDone(EstateCase estate, int now)
        {
            if (estate.Items.All(i => i.Terminal) && estate.Stage != EstateStage.Resolved) SetStage(estate, EstateStage.Resolved, now);
        }

        /// <summary>The estate cases a person would reasonably know about: the deceased's house, claimants, the mediator, and close kin. A viewer of 0 sees everything.</summary>
        public bool EstateVisibleTo(EstateCase estate, int viewer)
        {
            if (estate == null) return false;
            if (viewer == 0) return true;
            if (estate.MediatorId == viewer) return true;
            if (estate.Items.Any(i => i.Claims.Any(c => c.ClaimantId == viewer))) return true;
            var d = GetPerson(estate.DeceasedId);
            if (d != null && (d.PartnerIds().Contains(viewer) || ChildrenOf(estate.DeceasedId).Contains(viewer) || d.ParentIds.Contains(viewer) || SiblingsOf(estate.DeceasedId).Contains(viewer))) return true;
            var me = GetPerson(viewer);
            return me != null && estate.FamilyId != 0 && BelongsToFamily(me, estate.FamilyId);
        }

        /// <summary>Open questions for the person playing this pawn. Nothing here ever answers on their behalf.</summary>
        public List<(EstateCase Case, EstateAsk Ask)> EstateAsksFor(int person) =>
            Estates.Where(e => e.Open || e.Stage == EstateStage.Unresolved).SelectMany(e => e.Asks.Where(a => a.PersonId == person && (a.Open || a.Expired && e.Stage == EstateStage.Unresolved && e.Items.Any(i => i.ThingId == a.ItemThingId && !i.Terminal))).Select(a => (e, a))).ToList();

        /// <summary>Owners an old or at-risk person is likely to name an heir for: the closest living relative who is not a child. Used to let the pawns settle things themselves.</summary>
        public int BestBeneficiary(int owner)
        {
            var o = GetPerson(owner);
            if (o?.InColony != true) return 0;
            var kin = o.PartnerIds().Concat(ChildrenOf(owner)).Concat(o.ParentIds).Concat(SiblingsOf(owner)).Distinct()
                .Select(GetPerson).Where(p => p?.InColony == true && !p.Juvenile).ToList();
            return kin.OrderByDescending(p => GetBond(owner, p.Id)?.Attachment ?? 0f).ThenBy(p => p.Id).Select(p => p.Id).FirstOrDefault();
        }

        /// <summary>Pawns decide for themselves, now and then, who should have their heirloom. A named heir is usually told; sometimes the choice is kept private.</summary>
        public int NameBeneficiariesNow(int now, Func<int, bool> isAvatar, int max = 2)
        {
            int named = 0;
            foreach (var group in PersonalItems.Where(i => i.Heirloom && i.OwnerId != 0).GroupBy(i => i.OwnerId).OrderBy(g => g.Key))
            {
                if (named >= max) break;
                int owner = group.Key;
                if (isAvatar != null && isAvatar(owner) || Beneficiaries.Any(b => b.OwnerId == owner)) continue;
                var p = GetPerson(owner);
                if (p?.InColony != true || p.Juvenile) continue;
                int heir = BestBeneficiary(owner);
                if (heir == 0) continue;
                bool told = Seed(new EstateCase { Id = owner }, heir) % 100 < 70;
                if (DesignateBeneficiary(owner, heir, 0, told ? new[] { heir } : null, now)) named++;
            }
            return named;
        }
    }
}
