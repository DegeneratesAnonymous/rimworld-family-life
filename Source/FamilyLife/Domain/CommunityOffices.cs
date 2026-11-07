using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum OfficeKind : byte { Healer, Teacher }
    public enum OfficeEnd : byte { None, TermEnded, Neglected, Left, Resigned }

    /// <summary>
    /// A post the colony fills by choice: the healer (the colony's doctor) or the teacher. Holding one is public standing, so houses want it, but it is
    /// earned by being chosen and kept by doing the work. It never changes what a person can do, only how they are regarded.
    /// </summary>
    public sealed partial class CommunityOffice
    {
        public int Id, HolderId, HolderFamily, ElectedTick, TermEndTick, LastWorkTick, Work, DayStamp, ActsToday, Counter, Backers, Opponents;
        public int UnmetSinceTick, NextVoteTick, LastHolderId, BarredId, BarredUntil, EndedTick;
        public int NextRoundTick, RoundStartTick;       // the doctor's check-ups
        public int NextClassTick, ClassStartTick;       // the teacher's classes
        public bool CounterReady;
        public OfficeKind Kind;
        public OfficeEnd EndReason;
        public List<int> Tokens = new List<int>();      // evidence already counted
        public List<int> Examined = new List<int>();    // people the doctor has seen in the current round
        public List<int> ClassQueue = new List<int>();  // students still to be taught in the current class
        public bool Held => HolderId != 0;
    }

    /// <summary>What the game can say about a person's fitness for a post. Skill is 0-1; a person who cannot do the work at all must report 0.</summary>
    public sealed class OfficeContext
    {
        public Func<int, OfficeKind, float> Skill;
        public Func<int, bool> Free;
        public Func<int, bool> Player;     // a player-controlled pawn is never made a candidate or voted for
    }

    public sealed partial class FamilyRegistry
    {
        public const int OfficeTermTicks = 30 * ActivityTuning.TicksPerDay;
        public const int OfficeNeglectTicks = 15 * ActivityTuning.TicksPerDay;
        public const int MaxOfficeActsPerDay = 3;
        public const int CheckupWindowTicks = 5 * ActivityTuning.TicksPerDay;
        public const int ClassWindowTicks = ActivityTuning.TicksPerDay;
        public const int MaxClassSize = 6;
        public const float MinOfficeSkill = .3f;
        public List<CommunityOffice> Offices = new List<CommunityOffice>();
        public int NextOfficeId = 1;

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
        /// Fills empty or expired posts. Who votes follows the colony's arrangement: everyone, the council's heads, or the leader alone. Each voter
        /// backs the candidate they themselves find best; a post nobody is fit for stays empty. Returns the posts whose holder changed.
        /// </summary>
        public List<CommunityOffice> ElectOffices(int now, OfficeContext ctx)
        {
            var changed = new List<CommunityOffice>();
            foreach (OfficeKind kind in Enum.GetValues(typeof(OfficeKind)))
            {
                var office = EnsureOffice(kind);
                if (office.Held)
                {
                    if (now < office.TermEndTick) continue;
                    EndOffice(office, OfficeEnd.TermEnded, now);
                }
                if (now < office.NextVoteTick) continue;
                office.NextVoteTick = now + 2 * ActivityTuning.TicksPerDay;
                int before = office.HolderId;
                if (Elect(office, now, ctx) && office.HolderId != before) changed.Add(office);
            }
            return changed;
        }

        private bool Elect(CommunityOffice office, int now, OfficeContext ctx)
        {
            ctx = ctx ?? new OfficeContext();
            float Skill(int id) => Math.Max(0f, Math.Min(1f, ctx.Skill != null ? ctx.Skill(id, office.Kind) : (GetPerson(id)?.TopSkillLevel ?? 0) / 20f));
            var candidates = Persons.Where(p => p.InColony && !p.Juvenile && (p.Id != office.BarredId || now >= office.BarredUntil) && !OfficesHeldBy(p.Id).Any() &&
                    !(ctx.Player != null && ctx.Player(p.Id)) && (ctx.Free == null || ctx.Free(p.Id)) && Skill(p.Id) >= MinOfficeSkill && WillingnessOf(p.Id, FamilyOf(p.Id), false) >= 35f)
                .Select(p => p.Id).OrderBy(i => i).Take(24).ToList();
            if (candidates.Count == 0) return false;

            List<int> electors;
            if (Institution.Kind == InstitutionKind.Authoritarian && GetPerson(Institution.LeaderId)?.InColony == true) electors = new List<int> { Institution.LeaderId };
            else if (Institution.Kind == InstitutionKind.Council)
                electors = Institution.CouncilHouseIds.Where(IsActiveHouse).Select(HeadOf).Where(h => h != 0 && GetPerson(h)?.InColony == true).Distinct().ToList();
            else electors = Persons.Where(p => p.InColony && !p.Juvenile).Select(p => p.Id).OrderBy(i => i).Take(40).ToList();
            electors = electors.Where(e => !(ctx.Player != null && ctx.Player(e))).ToList();
            if (electors.Count == 0) return false;

            var tally = candidates.ToDictionary(c => c, c => 0);
            var strength = candidates.ToDictionary(c => c, c => 0f);
            int cast = 0;
            foreach (int voter in electors)
            {
                int best = 0; float bestScore = float.MinValue;
                foreach (int c in candidates)
                {
                    float score = OfficeSupport(voter, c, Skill(c));
                    if (score > bestScore || Math.Abs(score - bestScore) < 1e-5f && c < best) { bestScore = score; best = c; }
                }
                if (best == 0 || bestScore < .35f) continue;
                tally[best]++; strength[best] += bestScore; cast++;
            }
            if (cast == 0) return false;
            int winner = candidates.OrderByDescending(c => tally[c]).ThenByDescending(c => strength[c]).ThenBy(c => c).First();
            if (tally[winner] == 0) return false;

            office.HolderId = winner; office.HolderFamily = FamilyOf(winner); office.ElectedTick = now; office.TermEndTick = now + OfficeTermTicks;
            office.LastWorkTick = now; office.Work = 0; office.UnmetSinceTick = 0; office.CounterReady = false; office.EndReason = OfficeEnd.None; office.EndedTick = 0;
            office.Backers = tally[winner]; office.Opponents = cast - tally[winner];
            office.NextRoundTick = now + ActivityTuning.TicksPerDay; office.RoundStartTick = 0; office.Examined.Clear();
            office.NextClassTick = now; office.ClassStartTick = 0; office.ClassQueue.Clear();
            NotePublicService(winner, office.HolderFamily, 1.5f, now);
            return true;
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
            office.UnmetSinceTick = 0; office.CounterReady = false; office.RoundStartTick = 0; office.ClassStartTick = 0; office.ClassQueue.Clear();
            if (why == OfficeEnd.Neglected) { office.BarredId = office.LastHolderId; office.BarredUntil = now + OfficeTermTicks; }
        }

        public bool ResignOffice(OfficeKind kind, int person, int now)
        {
            var office = OfficeFor(kind);
            if (office == null || office.HolderId != person) return false;
            EndOffice(office, OfficeEnd.Resigned, now);
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
        /// Credits one real act of the post's work. The caller must hold evidence the game saw: a one-time token from a finished job or a record the
        /// game wrote. The same token is never counted twice and a day's credit is capped, so easy acts cannot be farmed.
        /// </summary>
        public bool RecordOfficeWork(OfficeKind kind, int person, int token, int now)
        {
            var office = OfficeFor(kind);
            if (office == null || office.HolderId != person || token == 0 || office.Tokens.Contains(token)) return false;
            int day = now / ActivityTuning.TicksPerDay;
            if (office.DayStamp != day) { office.DayStamp = day; office.ActsToday = 0; }
            if (office.ActsToday >= MaxOfficeActsPerDay) return false;
            office.ActsToday++; office.Work++; office.LastWorkTick = now; office.UnmetSinceTick = 0;
            office.Tokens.Add(token);
            if (office.Tokens.Count > 64) office.Tokens.RemoveAt(0);
            NotePublicService(person, office.HolderFamily, .5f, now);
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
                if (office.UnmetSinceTick != 0 && now - office.UnmetSinceTick > OfficeNeglectTicks && now - office.LastWorkTick > OfficeNeglectTicks)
                {
                    var house = GetLineage(office.HolderFamily);
                    if (house != null) house.Standing = Math.Max(-100f, house.Standing - 2f);
                    EndOffice(office, OfficeEnd.Neglected, now);
                }
            }
        }

        // ---- the doctor's check-ups ----

        /// <summary>
        /// Opens a round of check-ups when one is due: every colonist is to be seen once. The interval is the player's choice. A round that opens
        /// with the doctor held in office only; nobody is booked in advance and nothing is forced.
        /// </summary>
        public bool BeginCheckupRound(int now, int intervalTicks)
        {
            var office = OfficeFor(OfficeKind.Healer);
            if (office == null || !office.Held || intervalTicks <= 0 || now < office.NextRoundTick || CheckupOpen(now)) return false;
            office.RoundStartTick = now; office.Examined.Clear(); office.NextRoundTick = now + intervalTicks;
            return true;
        }

        public bool CheckupOpen(int now)
        {
            var office = OfficeFor(OfficeKind.Healer);
            return office != null && office.Held && office.RoundStartTick != 0 && now - office.RoundStartTick < CheckupWindowTicks;
        }

        /// <summary>The next person still to be seen this round, chosen from those the game says are free to go. The doctor is not their own patient.</summary>
        public int NextCheckupPatient(int now, Func<int, bool> available)
        {
            var office = OfficeFor(OfficeKind.Healer);
            if (office == null || !CheckupOpen(now)) return 0;
            return Persons.Where(p => p.InColony && p.Id != office.HolderId && !office.Examined.Contains(p.Id) && (available == null || available(p.Id)))
                .OrderBy(p => p.Id).Select(p => p.Id).FirstOrDefault();
        }

        /// <summary>The doctor has really seen this person. Counted once per round; a round ends when everyone present has been seen.</summary>
        public bool RecordCheckup(int doctor, int patient, int now)
        {
            var office = OfficeFor(OfficeKind.Healer);
            if (office == null || office.HolderId != doctor || !CheckupOpen(now) || patient == doctor || GetPerson(patient)?.InColony != true || office.Examined.Contains(patient)) return false;
            office.Examined.Add(patient);
            if (Persons.Where(p => p.InColony && p.Id != doctor).All(p => office.Examined.Contains(p.Id))) office.RoundStartTick = 0;
            return true;
        }

        // ---- the teacher's classes ----

        /// <summary>
        /// Opens a class when one is due and there is someone to teach. With no student available nothing starts and the class is not used up, so it
        /// is simply held as soon as someone can come.
        /// </summary>
        public bool BeginClass(int now, int intervalTicks, IEnumerable<int> students)
        {
            var office = OfficeFor(OfficeKind.Teacher);
            if (office == null || !office.Held || intervalTicks <= 0 || now < office.NextClassTick || office.ClassQueue.Count > 0) return false;
            var chosen = (students ?? Enumerable.Empty<int>()).Distinct().Where(s => s != office.HolderId && GetPerson(s)?.InColony == true).OrderBy(s => s).Take(MaxClassSize).ToList();
            if (chosen.Count == 0) return false;
            office.ClassQueue = chosen; office.ClassStartTick = now; office.NextClassTick = now + intervalTicks;
            return true;
        }

        /// <summary>The next student waiting in the current class, or 0. A class that drags on for more than a day is simply over.</summary>
        public int NextStudent(int now)
        {
            var office = OfficeFor(OfficeKind.Teacher);
            if (office == null || office.ClassQueue.Count == 0) return 0;
            if (now - office.ClassStartTick > ClassWindowTicks || !office.Held) { office.ClassQueue.Clear(); return 0; }
            return office.ClassQueue[0];
        }

        /// <summary>This student was taught, or cannot be: they leave the queue either way.</summary>
        public void ClassDone(int student) => OfficeFor(OfficeKind.Teacher)?.ClassQueue.Remove(student);
    }
}
