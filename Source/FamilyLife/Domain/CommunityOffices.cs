using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>Healer, teacher and hall keeper are chosen by the colony at its yearly assembly. The chaplain is not chosen: the game's own ideoligion names its moral guide.</summary>
    public enum OfficeKind : byte { Healer, Teacher, HallKeeper, Chaplain }
    public enum OfficeEnd : byte { None, TermEnded, Neglected, Left, Resigned, Replaced }
    public enum AssemblyState : byte { Campaign, Voting, Counted, Cancelled }

    /// <summary>Why someone stands for a post. A person who has none of these does not stand: nobody is drafted by a score.</summary>
    [Flags]
    public enum PostMotive : byte { None = 0, Calling = 1, Recognition = 2, Sponsored = 4, Duty = 8, Incumbent = 16 }

    /// <summary>What a house wants a post for, drawn from its own ambition and situation. It is why a head backs one of their own and why a house sponsors a member.</summary>
    public enum HouseWish : byte { None, Care, Teach, Feed, Standing }

    public sealed class PostCandidate { public int Id; public PostMotive Motives; }

    /// <summary>
    /// A post in the community. Holding one is public standing, so people and houses want it, but it is earned by being chosen and kept by doing the
    /// work well. It never changes what a person can do, only how they are regarded. At the next assembly the colony grades the holder's year: how much
    /// they did and how well, and that grade weighs in the vote, so a good holder is hard to unseat and a poor one is not.
    /// </summary>
    public sealed partial class CommunityOffice
    {
        public int Id, HolderId, HolderFamily, ElectedTick, TermEndTick, LastWorkTick, Work, DayStamp, ActsToday, Counter, Backers, Opponents;
        public int UnmetSinceTick, NextVoteTick, LastHolderId, BarredId, BarredUntil, EndedTick, Terms, QualityCount;
        public int NextRoundTick, RoundStartTick;          // the doctor's check-ups
        public int NextSessionTick, SessionStartTick;      // the teacher's classes and the hall keeper's meals
        public int PendingKind, PendingTick;               // an event offered to the player and not yet answered (PendingEvent)
        public int LastIllnessTick, LastTestingYear;       // when a round was last called for an outbreak; the year the children were last tested
        public bool IllnessRound;                          // the current round was called for an outbreak: the sick are seen first
        public int TestingStartTick, TestingSlot;          // the annual testing under way, and the hour it has reached
        public List<int> TestIds = new List<int>(), TestPass = new List<int>(), TestTry = new List<int>();          // each child tested, and how many hourly checks they passed and sat
        public float QualitySum, LastGrade;
        public bool CounterReady;
        public OfficeKind Kind;
        public OfficeEnd EndReason;
        public List<int> Tokens = new List<int>();         // evidence already counted
        public List<int> Examined = new List<int>();       // people the doctor has seen in the current round
        public List<int> SessionQueue = new List<int>();   // students or diners still to be seen to in the current session
        public bool Held => HolderId != 0;
    }

    /// <summary>What the game can say about a person's fitness for a post. Skill is 0-1; a person who cannot do the work at all must report 0.</summary>
    public sealed class OfficeContext
    {
        public Func<int, OfficeKind, float> Skill;
        public Func<int, bool> Free;
        public Func<int, bool> Player;     // a player-controlled pawn is never made a candidate or voted for
        public Func<int, OfficeKind, bool> Passion;     // the person burns for the skill the post uses
    }

    public sealed partial class AssemblyEndorsement { public int FamilyId, EndorserId, CandidateId; public OfficeKind Kind; }

    /// <summary>One post at one assembly: who stood, how the incumbent had done, and how it went.</summary>
    public sealed partial class AssemblyRace
    {
        public OfficeKind Kind;
        public int IncumbentId, WinnerId, Abstained;
        public float IncumbentGrade, Margin;
        public List<int> Candidates = new List<int>();
        public List<int> Motives = new List<int>();     // why each candidate stands, parallel to Candidates
        public List<ElectionVote> Votes = new List<ElectionVote>();
    }

    /// <summary>The yearly gathering of all the families to choose the colony's posts. Every adult may vote once, in person at the polling place.</summary>
    public sealed partial class CommunityAssembly
    {
        public int Id, StartedTick, VotingTick, CountedTick;
        public AssemblyState State;
        public List<AssemblyRace> Races = new List<AssemblyRace>();
        public List<int> Voted = new List<int>();
        public bool Convened;                         // the gathering for it has been called, so it is called only once
        public List<AssemblyEndorsement> Endorsements = new List<AssemblyEndorsement>();
        public bool Open => State == AssemblyState.Campaign || State == AssemblyState.Voting;
    }

    public sealed partial class FamilyRegistry
    {
        public const int AssemblyYearTicks = 60 * ActivityTuning.TicksPerDay;
        public const int AssemblyCampaignTicks = 2 * ActivityTuning.TicksPerDay;
        public const int AssemblyVotingTicks = ActivityTuning.TicksPerDay;
        public const int OfficeNeglectTicks = 15 * ActivityTuning.TicksPerDay;
        public const int MaxOfficeActsPerDay = 3;
        public const int CheckupWindowTicks = 14 * ActivityTuning.TicksPerDay;          // a round lasts until everyone has been seen; the doctor can be called away and come back
        public const int SessionWindowTicks = ActivityTuning.TicksPerDay;
        public const int ClassWindowTicks = SessionWindowTicks;
        public const int MaxSessionSize = 6;
        public const int MaxClassSize = 24;          // a class is the colony's children; the hall keeper's meals stay at MaxSessionSize
        public const float MinOfficeSkill = .3f;
        public const float SupportFloor = .35f;
        public List<CommunityOffice> Offices = new List<CommunityOffice>();
        public List<CommunityAssembly> Assemblies = new List<CommunityAssembly>();
        public int NextOfficeId = 1, NextAssemblyId = 1, NextAssemblyTick;
        public int HallMapId = -1, HallX, HallZ;      // the dining hall the player has named, if any; otherwise the game looks for one

        /// <summary>The player names the colony's dining hall. The hall keeper looks after exactly this room and no other.</summary>
        public void SetHall(int map, int x, int z) { HallMapId = map; HallX = x; HallZ = z; }
        public void ClearHall() { HallMapId = -1; HallX = HallZ = 0; }
        public bool HallNamed => HallMapId >= 0;

        public static bool IsAppointed(OfficeKind kind) => kind == OfficeKind.Chaplain;

        /// <summary>How much of the post's work a good year contains. The yardstick for "did they do their part".</summary>
        public int ExpectedActs(OfficeKind kind)
        {
            switch (kind)
            {
                case OfficeKind.Healer: return Math.Max(6, Math.Min(20, Persons.Count(p => p.InColony) * 2));
                case OfficeKind.Teacher: return 30;
                case OfficeKind.HallKeeper: return 30;
                default: return 4;
            }
        }

        public CommunityOffice OfficeFor(OfficeKind kind) => Offices.FirstOrDefault(o => o.Kind == kind);
        public IEnumerable<CommunityOffice> OfficesHeldBy(int person) => Offices.Where(o => o.HolderId == person && person != 0);

        /// <summary>0 to 1: how much public weight the person's post carries. A post that is actually worked weighs more than one that is only held.</summary>
        public float OfficeWeight(int person)
        {
            var best = 0f;
            foreach (var o in OfficesHeldBy(person)) best = Math.Max(best, .5f + Math.Min(.5f, o.Work / 20f));
            return best;
        }

        private CommunityOffice EnsureOffice(OfficeKind kind)
        {
            var office = OfficeFor(kind);
            if (office == null) { office = new CommunityOffice { Id = NextOfficeId++, Kind = kind }; Offices.Add(office); }
            return office;
        }

        /// <summary>
        /// How well the holder did this term, 0 to 1: half how much of the expected work they did, half how well they did it. A post whose work was
        /// needed and ignored is marked down. This is what the colony weighs when it decides whether to keep them.
        /// </summary>
        public float OfficeGrade(OfficeKind kind)
        {
            var o = OfficeFor(kind);
            if (o == null || !o.Held) return 0f;
            float participation = Math.Min(1f, o.Work / (float)ExpectedActs(kind));
            float quality = o.QualityCount == 0 ? .5f : o.QualitySum / o.QualityCount;
            float ignored = o.UnmetSinceTick != 0 ? .15f : 0f;
            return Math.Max(0f, Math.Min(1f, .5f * participation + .5f * quality - ignored));
        }

        /// <summary>
        /// The thumb on the scale for someone already in the post. A good year makes them hard to beat; a middling year makes them an even
        /// contest; a poor year makes them easy to replace. It is a tilt, not a guarantee: a much better candidate still wins.
        /// </summary>
        public static float IncumbentBias(float grade) => .05f + (grade - .4f) * .6f;

        // ---- filling a vacancy between assemblies ----

        /// <summary>
        /// Fills a post that is empty (a first filling, or after the holder left or was relieved) without waiting for the assembly. Who votes follows
        /// the colony's arrangement. A post nobody is fit for stays empty. Returns the posts whose holder changed. Terms do not end here: only the
        /// yearly assembly reviews a holder.
        /// </summary>
        public List<CommunityOffice> ElectOffices(int now, OfficeContext ctx)
        {
            var changed = new List<CommunityOffice>();
            foreach (OfficeKind kind in Enum.GetValues(typeof(OfficeKind)))
            {
                if (IsAppointed(kind)) continue;
                var office = EnsureOffice(kind);
                if (office.Held || now < office.NextVoteTick) continue;
                office.NextVoteTick = now + 2 * ActivityTuning.TicksPerDay;
                if (Elect(office, now, ctx)) changed.Add(office);
            }
            return changed;
        }

        private float SkillOf(OfficeContext ctx, int id, OfficeKind kind)
            => Math.Max(0f, Math.Min(1f, ctx.Skill != null ? ctx.Skill(id, kind) : (GetPerson(id)?.TopSkillLevel ?? 0) / 20f));

        /// <summary>
        /// What each house wants a post for. A house with a gap in medical skill, a safety worry or hurt members wants a doctor; one with children to raise
        /// wants a teacher; one that is hungry or fractured wants a hall; one seeking influence or a legacy wants any post as standing.
        /// </summary>
        public HouseWish HouseWishFor(int family, OfficeKind kind)
        {
            var h = GetLineage(family);
            if (h == null || !IsActiveHouse(family) || IsAppointed(kind)) return HouseWish.None;
            var amb = h.Ambition ?? new HouseAmbitionRecord(); var sit = h.Situation ?? new HouseSituation();
            bool Is(HouseAmbitionKind k) => amb.Primary == k || amb.Secondary == k;
            bool Gap(HouseCapability c) => Is(HouseAmbitionKind.FillSkillGap) && amb.TargetCapability == c;
            switch (kind)
            {
                case OfficeKind.Healer: if (Gap(HouseCapability.Medical) || Is(HouseAmbitionKind.ImproveSafety) || sit.Safety < 45f) return HouseWish.Care; break;
                case OfficeKind.Teacher:
                    if (Is(HouseAmbitionKind.TrainNextGeneration) || Gap(HouseCapability.Intellectual) || Gap(HouseCapability.Social) || Gap(HouseCapability.Childcare) || sit.Children >= 2) return HouseWish.Teach;
                    break;
                case OfficeKind.HallKeeper: if (Is(HouseAmbitionKind.SecureProvisions) || Is(HouseAmbitionKind.RestoreCohesion) || Gap(HouseCapability.Cooking) || sit.Sustenance < 45f) return HouseWish.Feed; break;
            }
            return Is(HouseAmbitionKind.IncreaseInfluence) || Is(HouseAmbitionKind.ExpandLegacy) ? HouseWish.Standing : HouseWish.None;
        }

        /// <summary>The person a house would put forward for a post: its most capable fit member. Null when the house wants nothing from it.</summary>
        private int SponsoredBy(int family, OfficeKind kind, IEnumerable<int> pool, OfficeContext ctx)
            => HouseWishFor(family, kind) == HouseWish.None ? 0 : pool.Where(p => FamilyOf(p) == family).OrderByDescending(p => SkillOf(ctx, p, kind)).ThenBy(p => p).FirstOrDefault();

        /// <summary>
        /// Who stands for a post, and why. A person must be fit and willing, and must also have a reason: a calling for the skill, a personal aim for
        /// recognition, a house that sponsors them, or already holding the post. If nobody has a reason, the most capable fit person is asked as a
        /// duty, so a post a colony needs does not stay empty.
        /// </summary>
        private List<PostCandidate> OfficeCandidates(OfficeKind kind, int now, OfficeContext ctx, CommunityOffice office, bool allowHolders)
        {
            ctx = ctx ?? new OfficeContext();
            var pool = Persons.Where(p => p.InColony && !p.Juvenile && (office == null || p.Id != office.BarredId || now >= office.BarredUntil) && (allowHolders || !OfficesHeldBy(p.Id).Any()) &&
                    !(ctx.Player != null && ctx.Player(p.Id)) && SkillOf(ctx, p.Id, kind) >= MinOfficeSkill && WillingnessOf(p.Id, FamilyOf(p.Id), false) >= 35f)
                .Select(p => p.Id).OrderBy(i => i).Take(24).ToList();
            var sponsored = new HashSet<int>(Lineages.Where(h => h.AbsorbedIntoId == 0).Select(h => SponsoredBy(h.Id, kind, pool, ctx)).Where(x => x != 0));
            var list = new List<PostCandidate>();
            foreach (int id in pool)
            {
                var m = PostMotive.None;
                if (ctx.Passion != null && ctx.Passion(id, kind)) m |= PostMotive.Calling;
                var ag = AgencyOf(id);
                if (ag != null && ag.Active && ag.Goal == PersonalGoal.Recognition) m |= PostMotive.Recognition;
                if (sponsored.Contains(id)) m |= PostMotive.Sponsored;
                if (office != null && office.HolderId == id) m |= PostMotive.Incumbent;
                if (m != PostMotive.None) list.Add(new PostCandidate { Id = id, Motives = m });
            }
            if (list.Count == 0 && pool.Count > 0)
            {
                int best = pool.OrderByDescending(p => SkillOf(ctx, p, kind)).ThenBy(p => p).First();
                list.Add(new PostCandidate { Id = best, Motives = PostMotive.Duty });
            }
            return list;
        }

        private List<int> OfficeElectors(OfficeContext ctx)
        {
            List<int> electors;
            if (Institution.Kind == InstitutionKind.Authoritarian && GetPerson(Institution.LeaderId)?.InColony == true) electors = new List<int> { Institution.LeaderId };
            else if (Institution.Kind == InstitutionKind.Council)
                electors = Institution.CouncilHouseIds.Where(IsActiveHouse).Select(HeadOf).Where(h => h != 0 && GetPerson(h)?.InColony == true).Distinct().ToList();
            else electors = Persons.Where(p => p.InColony && !p.Juvenile).Select(p => p.Id).OrderBy(i => i).Take(40).ToList();
            return electors.Where(e => !(ctx.Player != null && ctx.Player(e))).ToList();
        }

        private bool Elect(CommunityOffice office, int now, OfficeContext ctx)
        {
            ctx = ctx ?? new OfficeContext();
            var candidates = OfficeCandidates(office.Kind, now, ctx, office, false).Select(x => x.Id).ToList();
            if (candidates.Count == 0) return false;
            var electors = OfficeElectors(ctx);
            if (electors.Count == 0) return false;
            var tally = candidates.ToDictionary(c => c, c => 0);
            var strength = candidates.ToDictionary(c => c, c => 0f);
            int cast = 0;
            foreach (int voter in electors)
            {
                int best = 0; float bestScore = float.MinValue;
                foreach (int c in candidates)
                {
                    float score = OfficeSupport(voter, c, SkillOf(ctx, c, office.Kind));
                    if (score > bestScore || Math.Abs(score - bestScore) < 1e-5f && c < best) { bestScore = score; best = c; }
                }
                if (best == 0 || bestScore < SupportFloor) continue;
                tally[best]++; strength[best] += bestScore; cast++;
            }
            if (cast == 0) return false;
            int winner = candidates.OrderByDescending(c => tally[c]).ThenByDescending(c => strength[c]).ThenBy(c => c).First();
            if (tally[winner] == 0) return false;
            Install(office, winner, now, tally[winner], cast - tally[winner]);
            return true;
        }

        private void Install(CommunityOffice office, int winner, int now, int backers, int opponents)
        {
            office.HolderId = winner; office.HolderFamily = FamilyOf(winner); office.ElectedTick = now; office.TermEndTick = NextAssemblyTick;
            office.LastWorkTick = now; office.Work = 0; office.QualitySum = 0f; office.QualityCount = 0; office.UnmetSinceTick = 0; office.CounterReady = false;
            office.EndReason = OfficeEnd.None; office.EndedTick = 0; office.Backers = backers; office.Opponents = opponents; office.Terms++;
            office.NextRoundTick = now + ActivityTuning.TicksPerDay; office.RoundStartTick = 0; office.Examined.Clear();
            office.NextSessionTick = now; office.SessionStartTick = 0; office.SessionQueue.Clear();
            office.PendingKind = 0; office.IllnessRound = false; office.TestingStartTick = 0; office.TestIds = new List<int>(); office.TestPass = new List<int>(); office.TestTry = new List<int>();
            office.TestScore = new List<int>(); office.TestExcused = new List<int>();
            // a new holder starts their own record of rounds, refusals and how people were treated (what the children have studied belongs to the children and stays)
            office.RoundWindow = 0; office.Missed = new List<int>(); office.Refused = new List<int>(); office.RefusedTick = new List<int>(); office.KinQuality = office.OtherQuality = 0f; office.KinSeen = office.OtherSeen = 0;
            office.RoutineDay = office.RoutineUsed = office.LastVisitTick = 0; office.ContactIds = new List<int>(); office.ContactCounts = new List<int>();
            NotePublicService(winner, office.HolderFamily, 1.5f, now);
            AdvanceRecognition(winner, .25f, now);
        }

        /// <summary>One voter's regard for one candidate: how well they do the work, how far the voter trusts them, shared house, and known misconduct.</summary>
        private float OfficeSupport(int voter, int candidate, float skill)
        {
            var bond = GetBond(voter, candidate);
            float regard = Math.Max(0f, Math.Min(1f, ((bond?.Trust ?? 0f) + (bond?.Attachment ?? 0f) + 100f) / 200f));
            float house = voter == candidate || FamilyOf(voter) == FamilyOf(candidate) ? 1f : 0f;
            float conduct = Incidents.Where(i => i.ActorId == candidate && !i.Resolved && (i.Kind == IncidentKind.Theft || i.Kind == IncidentKind.Injury || i.Kind == IncidentKind.Intimidation) && KnowsIncident(voter, i))
                .Take(6).Sum(i => Math.Min(1f, i.Severity / 60f));
            return skill * .5f + regard * .3f + house * .1f + .1f - Math.Min(.5f, conduct * .4f);
        }

        private void EndOffice(CommunityOffice office, OfficeEnd why, int now)
        {
            office.LastHolderId = office.HolderId; office.HolderId = 0; office.HolderFamily = 0; office.EndReason = why; office.EndedTick = now;
            office.UnmetSinceTick = 0; office.CounterReady = false; office.RoundStartTick = 0; office.SessionStartTick = 0; office.SessionQueue.Clear();
            office.PendingKind = 0; office.IllnessRound = false; office.TestingStartTick = 0; office.TestIds = new List<int>(); office.TestPass = new List<int>(); office.TestTry = new List<int>();
            office.TestScore = new List<int>(); office.TestExcused = new List<int>();
            office.RoundWindow = 0; office.Missed = new List<int>(); office.Refused = new List<int>(); office.RefusedTick = new List<int>(); office.KinQuality = office.OtherQuality = 0f; office.KinSeen = office.OtherSeen = 0;
            office.RoutineDay = office.RoutineUsed = office.LastVisitTick = 0; office.ContactIds = new List<int>(); office.ContactCounts = new List<int>();
            if (why == OfficeEnd.Neglected) { office.BarredId = office.LastHolderId; office.BarredUntil = now + AssemblyYearTicks / 2; }
        }

        public bool ResignOffice(OfficeKind kind, int person, int now)
        {
            var office = OfficeFor(kind);
            if (office == null || office.HolderId != person) return false;
            EndOffice(office, OfficeEnd.Resigned, now);
            return true;
        }

        /// <summary>
        /// The chaplain is whoever the colony's ideoligion names as its moral guide, so the post follows the game rather than a vote. Pass 0 when
        /// there is none. A new person starts a new record; the same person carries on.
        /// </summary>
        public bool AppointOffice(OfficeKind kind, int person, int now)
        {
            if (!IsAppointed(kind)) return false;
            var office = EnsureOffice(kind);
            if (person != 0 && GetPerson(person)?.InColony != true) person = 0;
            if (office.HolderId == person) return false;
            if (office.Held) EndOffice(office, person == 0 ? OfficeEnd.Left : OfficeEnd.Replaced, now);
            if (person == 0) return true;
            foreach (var other in Offices.Where(o => o.HolderId == person && o.Kind != kind).ToList()) EndOffice(other, OfficeEnd.Replaced, now);      // one post per person
            office.HolderId = person; office.HolderFamily = FamilyOf(person); office.ElectedTick = now; office.TermEndTick = 0; office.LastWorkTick = now;
            office.Work = 0; office.QualitySum = 0f; office.QualityCount = 0; office.UnmetSinceTick = 0; office.EndReason = OfficeEnd.None; office.EndedTick = 0; office.Terms++;
            NotePublicService(person, office.HolderFamily, 1f, now);
            return true;
        }

        /// <summary>
        /// Tells the register whether the post's work is needed right now. Work that is needed and left undone for long enough costs the holder the
        /// post; a quiet stretch with nothing to do never does.
        /// </summary>
        public void NoteOfficeNeed(OfficeKind kind, bool needed, int now)
        {
            var office = OfficeFor(kind);
            if (office == null || !office.Held) return;
            if (!needed) office.UnmetSinceTick = 0;
            else if (office.UnmetSinceTick == 0) office.UnmetSinceTick = now;
        }

        /// <summary>
        /// Credits one real act of the post's work, with how well it was done (0 to 1). The caller must hold evidence the game saw: a one-time token
        /// from a finished job or a record the game wrote. The same token is never counted twice and a day's credit is capped, so easy acts cannot be farmed.
        /// </summary>
        public bool RecordOfficeWork(OfficeKind kind, int person, int token, int now, float quality = .6f)
        {
            var office = OfficeFor(kind);
            if (office == null || office.HolderId != person || token == 0 || office.Tokens.Contains(token)) return false;
            int day = now / ActivityTuning.TicksPerDay;
            if (office.DayStamp != day) { office.DayStamp = day; office.ActsToday = 0; }
            if (office.ActsToday >= MaxOfficeActsPerDay) return false;
            office.ActsToday++; office.Work++; office.LastWorkTick = now; office.UnmetSinceTick = 0;
            office.QualitySum += Math.Max(0f, Math.Min(1f, quality)); office.QualityCount++;
            office.Tokens.Add(token);
            if (office.Tokens.Count > 64) office.Tokens.RemoveAt(0);
            NotePublicService(person, office.HolderFamily, .5f, now);
            if (quality >= .6f) AdvanceRecognition(person, .03f, now);
            return true;
        }

        /// <summary>Removes holders who have left the colony, keeps their family current, and relieves anyone who ignored work that was needed.</summary>
        public void MaintainOffices(int now)
        {
            foreach (var office in Offices.Where(o => o.Held).ToList())
            {
                var holder = GetPerson(office.HolderId);
                if (holder == null || !holder.InColony) { EndOffice(office, OfficeEnd.Left, now); continue; }
                office.HolderFamily = FamilyOf(office.HolderId);
                if (!IsAppointed(office.Kind) && office.UnmetSinceTick != 0 && now - office.UnmetSinceTick > OfficeNeglectTicks && now - office.LastWorkTick > OfficeNeglectTicks)
                {
                    var house = GetLineage(office.HolderFamily);
                    if (house != null) house.Standing = Math.Max(-100f, house.Standing - 2f);
                    EndOffice(office, OfficeEnd.Neglected, now);
                }
            }
            if (NextAssemblyTick == 0 && Persons.Count(p => p.InColony && !p.Juvenile) >= 3) NextAssemblyTick = now + AssemblyYearTicks;
            foreach (var office in Offices.Where(o => o.Held && !IsAppointed(o.Kind) && o.TermEndTick == 0)) office.TermEndTick = NextAssemblyTick;
        }

        // ---- the yearly assembly ----

        public CommunityAssembly OpenAssembly => Assemblies.FirstOrDefault(a => a.Open);

        /// <summary>
        /// Once a year every elected post is put to the colony. The holder stands again if they are still fit and willing; others may stand too. The
        /// incumbent's graded year is fixed now, so it cannot be improved by a last-minute flurry. A post nobody is fit for is simply left as it is.
        /// </summary>
        public CommunityAssembly CallAssembly(int now, OfficeContext ctx)
        {
            ctx = ctx ?? new OfficeContext();
            if (NextAssemblyTick == 0 || now < NextAssemblyTick || OpenAssembly != null) return null;
            while (NextAssemblyTick <= now) NextAssemblyTick += AssemblyYearTicks;
            var assembly = new CommunityAssembly { Id = NextAssemblyId++, StartedTick = now, VotingTick = now + AssemblyCampaignTicks, State = AssemblyState.Campaign };
            var kinds = Enum.GetValues(typeof(OfficeKind)).Cast<OfficeKind>().Where(k => !IsAppointed(k)).ToList();
            var fields = kinds.ToDictionary(k => k, k => OfficeCandidates(k, now, ctx, EnsureOffice(k), true));

            // A holder who is no longer fit or willing for their own post steps down; those who remain keep standing for it.
            foreach (var kind in kinds)
            {
                var office = EnsureOffice(kind);
                if (office.Held && !fields[kind].Any(c => c.Id == office.HolderId)) EndOffice(office, OfficeEnd.TermEnded, now);
            }

            // A person stands for one post only: the one they hold, otherwise the one they are best at. The ideoligion's chaplain already has a post.
            int chaplain = OfficeFor(OfficeKind.Chaplain)?.HolderId ?? 0;
            var home = new Dictionary<int, OfficeKind>();
            foreach (var kind in kinds) { var office = EnsureOffice(kind); if (office.Held) home[office.HolderId] = kind; }
            foreach (int person in fields.Values.SelectMany(f => f.Select(c => c.Id)).Distinct().OrderBy(i => i))
            {
                if (person == chaplain || home.ContainsKey(person)) continue;
                home[person] = kinds.Where(k => fields[k].Any(c => c.Id == person)).OrderByDescending(k => SkillOf(ctx, person, k)).ThenBy(k => k).First();
            }

            foreach (var kind in kinds)
            {
                var office = EnsureOffice(kind);
                var standing = fields[kind].Where(c => c.Id != chaplain && home.TryGetValue(c.Id, out var h) && h == kind).Take(6).ToList();
                if (standing.Count == 0) continue;
                var race = new AssemblyRace { Kind = kind, Candidates = standing.Select(c => c.Id).ToList(), Motives = standing.Select(c => (int)c.Motives).ToList() };
                if (office.Held && race.Candidates.Contains(office.HolderId)) { race.IncumbentId = office.HolderId; race.IncumbentGrade = OfficeGrade(kind); }
                assembly.Races.Add(race);
            }
            foreach (var office in Offices.Where(o => o.Held && !IsAppointed(o.Kind))) office.TermEndTick = NextAssemblyTick;
            if (assembly.Races.Count == 0) return null;                      // nothing to decide this year: no gathering, and holders simply carry on
            if (Assemblies.Count >= 8) Assemblies.RemoveAt(0);
            Assemblies.Add(assembly);
            return assembly;
        }

        /// <summary>Why a candidate stands in a race, as flags. Empty if they are not in it.</summary>
        public PostMotive MotivesOf(AssemblyRace race, int person)
        {
            int i = race?.Candidates.IndexOf(person) ?? -1;
            return i < 0 || i >= race.Motives.Count ? PostMotive.None : (PostMotive)race.Motives[i];
        }

        /// <summary>The candidate a house head would back in a race: the one they themselves think best, with a lean toward their own house.</summary>
        public int HouseChoice(int head, AssemblyRace race, CommunityAssembly assembly, OfficeContext ctx, out float score)
        {
            ctx = ctx ?? new OfficeContext();
            int best = 0; score = float.MinValue;
            foreach (int c in race.Candidates)
            {
                float s = AssemblySupport(head, c, race, assembly, ctx) + (FamilyOf(c) == FamilyOf(head) ? .15f : 0f);
                if (s > score || Math.Abs(s - score) < 1e-5f && c < best) { score = s; best = c; }
            }
            return best;
        }

        /// <summary>A house head publicly backs a candidate in a race. One backing per house per post; it only nudges the voters who trust that head.</summary>
        public bool EndorseAssembly(int assemblyId, int family, int endorser, OfficeKind kind, int candidate)
        {
            var a = Assemblies.FirstOrDefault(x => x.Id == assemblyId);
            var race = a?.Races.FirstOrDefault(r => r.Kind == kind);
            if (a == null || a.State != AssemblyState.Campaign || race == null || !race.Candidates.Contains(candidate) || HeadOf(family) != endorser ||
                a.Endorsements.Any(e => e.FamilyId == family && e.Kind == kind)) return false;
            a.Endorsements.Add(new AssemblyEndorsement { FamilyId = family, EndorserId = endorser, CandidateId = candidate, Kind = kind });
            return true;
        }

        private float AssemblySupport(int voter, int candidate, AssemblyRace race, CommunityAssembly assembly, OfficeContext ctx)
        {
            float score = OfficeSupport(voter, candidate, SkillOf(ctx, candidate, race.Kind));
            if (candidate == race.IncumbentId) score += IncumbentBias(race.IncumbentGrade);
            foreach (var e in assembly.Endorsements.Where(x => x.Kind == race.Kind && x.CandidateId == candidate))
            {
                float trust = Math.Max(0f, Math.Min(1f, (GetBond(voter, e.EndorserId)?.Trust ?? 0f) / 100f));
                score += trust * .12f + (FamilyOf(voter) == e.FamilyId ? .06f : 0f);
            }
            return score;
        }

        /// <summary>Adults living in the colony who have not yet voted. The game decides who is free to walk to the polling place.</summary>
        public List<int> AssemblyVoters(CommunityAssembly a) => Persons.Where(p => p.InColony && !p.Juvenile && !a.Voted.Contains(p.Id)).OrderBy(p => p.Id).Take(160).Select(p => p.Id).ToList();

        public bool NeedsToVoteInAssembly(int voter)
        {
            var a = Assemblies.FirstOrDefault(x => x.State == AssemblyState.Voting);
            return a != null && !a.Voted.Contains(voter) && GetPerson(voter)?.InColony == true && !GetPerson(voter).Juvenile && a.Races.Count > 0;
        }

        /// <summary>One ballot, cast in person, covering every post. In each race the voter backs whom they themselves find best, or no one if none is good enough.</summary>
        public bool CastAssemblyBallot(int assemblyId, int voter, OfficeContext ctx, int now)
        {
            ctx = ctx ?? new OfficeContext();
            var a = Assemblies.FirstOrDefault(x => x.Id == assemblyId);
            var p = GetPerson(voter);
            if (a == null || a.State != AssemblyState.Voting || p?.InColony != true || p.Juvenile || a.Voted.Contains(voter)) return false;
            a.Voted.Add(voter);
            foreach (var race in a.Races)
            {
                int best = 0; float bestScore = float.MinValue;
                foreach (int c in race.Candidates)
                {
                    float s = AssemblySupport(voter, c, race, a, ctx);
                    if (s > bestScore || Math.Abs(s - bestScore) < 1e-5f && c < best) { bestScore = s; best = c; }
                }
                if (best == 0 || bestScore < SupportFloor) { race.Abstained++; continue; }
                race.Votes.Add(new ElectionVote { VoterId = voter, CandidateId = best, Reason = best == race.IncumbentId ? PoliticalFactor.Legitimacy : PoliticalFactor.Friendship });
            }
            return true;
        }

        /// <summary>Moves an assembly along: campaigning gives way to voting, and voting ends when everyone has voted or the day is up.</summary>
        public List<AssemblyRace> AdvanceAssembly(int now)
        {
            var a = OpenAssembly;
            if (a == null) return null;
            if (a.State == AssemblyState.Campaign && now >= a.VotingTick) { a.State = AssemblyState.Voting; a.VotingTick = now; }
            if (a.State != AssemblyState.Voting) return null;
            bool everyone = AssemblyVoters(a).Count == 0;
            if (!everyone && now < a.VotingTick + AssemblyVotingTicks) return null;
            return CountAssembly(a, now);
        }

        private List<AssemblyRace> CountAssembly(CommunityAssembly a, int now)
        {
            var seated = new HashSet<int>();
            foreach (var race in a.Races)
            {
                var office = EnsureOffice(race.Kind);
                var ranked = race.Candidates.Select(c => new { Id = c, Votes = race.Votes.Count(v => v.CandidateId == c) })
                    .OrderByDescending(x => x.Votes).ThenByDescending(x => x.Id == race.IncumbentId).ThenBy(x => x.Id).ToList();
                var pick = ranked.FirstOrDefault(x => !seated.Contains(x.Id) && !HoldsOtherPost(x.Id, race.Kind) && (x.Votes > 0 || race.Candidates.Count == 1));
                if (pick == null)
                {
                    // Nobody got a vote and nobody is standing alone: the incumbent, if there is one, simply carries on.
                    if (office.Held) office.TermEndTick = NextAssemblyTick;
                    continue;
                }
                int total = Math.Max(1, race.Votes.Count);
                race.WinnerId = pick.Id; race.Margin = (pick.Votes - (ranked.Count > 1 ? ranked.Where(x => x.Id != pick.Id).Max(x => x.Votes) : 0)) / (float)total;
                seated.Add(pick.Id);
                int against = race.Votes.Count - pick.Votes;
                if (office.Held && office.HolderId == pick.Id)
                {
                    office.LastGrade = race.IncumbentGrade; office.TermEndTick = NextAssemblyTick; office.Backers = pick.Votes; office.Opponents = against;
                    office.Work = 0; office.QualitySum = 0f; office.QualityCount = 0; office.Terms++; office.UnmetSinceTick = 0; office.LastWorkTick = now;
                    NotePublicService(pick.Id, office.HolderFamily, 1f, now);
                    AdvanceRecognition(pick.Id, .15f, now);
                }
                else
                {
                    if (office.Held) { office.LastGrade = race.IncumbentGrade; EndOffice(office, OfficeEnd.TermEnded, now); }
                    Install(office, pick.Id, now, pick.Votes, against);
                }
            }
            a.State = AssemblyState.Counted; a.CountedTick = now;
            return a.Races;
        }

        private bool HoldsOtherPost(int person, OfficeKind kind) => Offices.Any(o => o.HolderId == person && o.Kind != kind);

        // ---- what a post is worth ----

        public CommunityOffice PostOf(OfficeKind kind) { var o = OfficeFor(kind); return o != null && o.Held ? o : null; }

        /// <summary>Whether someone in this house holds the post. It is what the house gets from it: first call on the service, and pride.</summary>
        public bool HouseHoldsPost(int family, OfficeKind kind)
        {
            var o = PostOf(kind);
            return o != null && family != 0 && o.HolderFamily == family;
        }

        /// <summary>Whether this person belongs to the house that holds the post: the kin of the holder, with the perks of kinship.</summary>
        public bool KinOfHolder(int person, OfficeKind kind) => HouseHoldsPost(FamilyOf(person), kind);

        /// <summary>People served by a post come to feel something for the one who served them: a little trust and affection each time, which later counts in votes and in succession.</summary>
        public void ThankPost(OfficeKind kind, int holder, int served, int now)
        {
            var o = PostOf(kind);
            if (o == null || o.HolderId != holder || served == holder || GetPerson(served)?.InColony != true) return;
            ChangeBond(served, holder, new BondChange(.5f, 1f, -.5f, 0f), now);
        }

        /// <summary>A person with a personal aim of being recognised moves toward it by doing the work of a post well, and more by being chosen.</summary>
        public void AdvanceRecognition(int person, float amount, int now)
        {
            var ag = AgencyOf(person);
            if (ag == null || !ag.Active || ag.Goal != PersonalGoal.Recognition) return;
            ag.Progress = Math.Min(1f, ag.Progress + amount);
            if (ag.Progress >= 1f) ag.EndedTick = now;
        }

        // ---- the doctor's check-ups ----

        /// <summary>
        /// Opens a round of check-ups when one is due: every colonist is to be seen once. The interval is the player's choice. Nobody is booked in
        /// advance and nothing is forced.
        /// </summary>
        public bool BeginCheckupRound(int now, int intervalTicks)
        {
            var office = OfficeFor(OfficeKind.Healer);
            if (office == null || !office.Held || intervalTicks <= 0 || now < office.NextRoundTick || CheckupOpen(now)) return false;
            // Anyone the last round did not reach (and who did not decline) is first this time; nobody is marked as having failed for not being reached.
            var prior = office.Examined;
            office.Missed = RoundStartPeople(office).Where(id => !prior.Contains(id) && !office.Refused.Contains(id)).Take(96).ToList();
            office.RoundStartTick = now; office.Examined.Clear(); office.NextRoundTick = now + intervalTicks;
            office.RoundWindow = CheckupWorkload.WindowDays(Persons.Count(p => p.InColony && p.Id != office.HolderId), office.IllnessRound) * ActivityTuning.TicksPerDay;
            office.Refused.Clear(); office.RefusedTick.Clear();
            return true;
        }

        private IEnumerable<int> RoundStartPeople(CommunityOffice office) => Persons.Where(p => p.InColony && p.Id != office.HolderId).Select(p => p.Id);

        public bool CheckupOpen(int now)
        {
            var office = OfficeFor(OfficeKind.Healer);
            return office != null && office.Held && office.RoundStartTick != 0 && now - office.RoundStartTick < (office.RoundWindow > 0 ? office.RoundWindow : CheckupWindowTicks);
        }

        /// <summary>The next person still to be seen this round, chosen from those the game says are free to go. The doctor is not their own patient.</summary>
        public int NextCheckupPatient(int now, Func<int, bool> available, Func<int, int> priority = null)
        {
            var office = OfficeFor(OfficeKind.Healer);
            if (office == null || !CheckupOpen(now)) return 0;
            return Persons.Where(p => p.InColony && p.Id != office.HolderId && !office.Examined.Contains(p.Id) && (available == null || available(p.Id)))
                .OrderBy(p => priority == null ? 0 : priority(p.Id)).ThenBy(p => office.Missed.Contains(p.Id) ? 0 : 1).ThenBy(p => p.Id).Select(p => p.Id).FirstOrDefault();      // no house is seen first; those missed last time are
        }

        /// <summary>The doctor has really seen this person. Counted once per round; a round ends when everyone present has been seen.</summary>
        public bool RecordCheckup(int doctor, int patient, int now)
        {
            var office = OfficeFor(OfficeKind.Healer);
            if (office == null || office.HolderId != doctor || !CheckupOpen(now) || patient == doctor || GetPerson(patient)?.InColony != true || office.Examined.Contains(patient)) return false;
            office.Examined.Add(patient); office.Missed.Remove(patient);
            if (Persons.Where(p => p.InColony && p.Id != doctor).All(p => office.Examined.Contains(p.Id))) office.RoundStartTick = 0;
            return true;
        }

        // ---- sessions: the teacher's classes and the hall keeper's meals ----

        /// <summary>
        /// Opens a session (a class, a meal) when one is due and there is someone to see to. With nobody available nothing starts and the session
        /// is not used up, so it is simply held as soon as someone can come.
        /// </summary>
        public bool BeginSession(OfficeKind kind, int now, int intervalTicks, IEnumerable<int> people, int maxSize = MaxSessionSize)
        {
            var office = OfficeFor(kind);
            if (office == null || !office.Held || intervalTicks <= 0 || now < office.NextSessionTick || office.SessionQueue.Count > 0) return false;
            var chosen = (people ?? Enumerable.Empty<int>()).Distinct().Where(s => s != office.HolderId && GetPerson(s)?.InColony == true).OrderBy(s => FamilyOf(s) != office.HolderFamily).ThenBy(s => s).Take(maxSize).ToList();
            if (chosen.Count == 0) return false;
            office.SessionQueue = chosen; office.SessionStartTick = now; office.NextSessionTick = now + intervalTicks;
            return true;
        }

        /// <summary>The next person waiting in the current session, or 0. A session that drags on for more than a day is simply over.</summary>
        public int NextInSession(OfficeKind kind, int now)
        {
            var office = OfficeFor(kind);
            if (office == null || office.SessionQueue.Count == 0) return 0;
            if (now - office.SessionStartTick > SessionWindowTicks || !office.Held) { office.SessionQueue.Clear(); return 0; }
            return office.SessionQueue[0];
        }

        /// <summary>This person was seen to, or cannot be: they leave the queue either way.</summary>
        public void SessionDone(OfficeKind kind, int person) => OfficeFor(kind)?.SessionQueue.Remove(person);

        /// <summary>This person could not be reached just now: they move to the back so the rest are not held up.</summary>
        public void SessionDefer(OfficeKind kind, int person)
        {
            var q = OfficeFor(kind)?.SessionQueue;
            if (q == null || !q.Remove(person)) return;
            q.Add(person);
        }

        public bool BeginClass(int now, int intervalTicks, IEnumerable<int> students) => BeginSession(OfficeKind.Teacher, now, intervalTicks, students, MaxClassSize);
        public int NextStudent(int now) => NextInSession(OfficeKind.Teacher, now);
        public void ClassDone(int student) => SessionDone(OfficeKind.Teacher, student);
        public bool BeginHallMeal(int now, int intervalTicks, IEnumerable<int> diners) => BeginSession(OfficeKind.HallKeeper, now, intervalTicks, diners);
        public int NextDiner(int now) => NextInSession(OfficeKind.HallKeeper, now);
        public void DinerServed(int diner) => SessionDone(OfficeKind.HallKeeper, diner);
    }
}
