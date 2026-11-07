using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum CouncilMatterKind : byte { Union, Censure }
    public enum CouncilMatterState : byte { Pending, Approved, Rejected, Used, Expired }

    /// <summary>A question put to the council. Each seated house's head answers once, by their own weighing; a tie leaves things as they were.</summary>
    public sealed partial class CouncilMatter
    {
        public int Id, FamilyA, FamilyB, RequestedTick, DecidedTick, Yes, No;
        public CouncilMatterKind Kind;
        public CouncilMatterState State;
        public List<int> Voted = new List<int>();
        public bool Open => State == CouncilMatterState.Pending;
    }

    public sealed partial class FamilyRegistry
    {
        public const int CouncilWindowTicks = 2 * ActivityTuning.TicksPerDay;
        public List<CouncilMatter> CouncilMatters = new List<CouncilMatter>();
        public int NextCouncilMatterId = 1;

        private bool CouncilSits => Institution.Kind == InstitutionKind.Council;

        /// <summary>
        /// Whether a union of two houses may go ahead. With any other arrangement it always may. Under a council the first attempt opens a question and
        /// is refused for now; once the council has approved it, the next attempt goes through, once.
        /// </summary>
        public bool CouncilClears(int a, int b, int now)
        {
            if (!CouncilSits) return true;
            var approved = CouncilMatters.FirstOrDefault(m => m.Kind == CouncilMatterKind.Union && m.State == CouncilMatterState.Approved && Pair(m, a, b));
            if (approved != null) { approved.State = CouncilMatterState.Used; return true; }
            if (CouncilMatters.Any(m => m.Kind == CouncilMatterKind.Union && Pair(m, a, b) && (m.Open || m.State == CouncilMatterState.Rejected && now - m.DecidedTick < 10 * ActivityTuning.TicksPerDay))) return false;
            AddMatter(CouncilMatterKind.Union, a, b, now);
            return false;
        }

        /// <summary>Whether a union between these two houses would have to go to the council and has not yet been approved.</summary>
        public bool CouncilWouldBlock(int a, int b)
            => CouncilSits && !CouncilMatters.Any(m => m.Kind == CouncilMatterKind.Union && m.State == CouncilMatterState.Approved && Pair(m, a, b));

        /// <summary>Asks the council to censure a house whose conduct is known. The house loses standing only if the council agrees.</summary>
        public CouncilMatter RequestCensure(int family, int now)
        {
            if (!CouncilSits || !IsActiveHouse(family) || CouncilMatters.Any(m => m.Kind == CouncilMatterKind.Censure && m.FamilyA == family && (m.Open || now - m.RequestedTick < 20 * ActivityTuning.TicksPerDay))) return null;
            return AddMatter(CouncilMatterKind.Censure, family, 0, now);
        }

        private CouncilMatter AddMatter(CouncilMatterKind kind, int a, int b, int now)
        {
            if (CouncilMatters.Count >= 32) { var old = CouncilMatters.FirstOrDefault(m => !m.Open); if (old == null) return null; CouncilMatters.Remove(old); }
            var matter = new CouncilMatter { Id = NextCouncilMatterId++, Kind = kind, FamilyA = a, FamilyB = b, RequestedTick = now };
            CouncilMatters.Add(matter);
            return matter;
        }

        private static bool Pair(CouncilMatter m, int a, int b) => m.FamilyA == a && m.FamilyB == b || m.FamilyA == b && m.FamilyB == a;

        /// <summary>The heads whose word is still needed on a matter: seated, living, present, not the subject, and not yet heard.</summary>
        public List<int> CouncilVotersFor(CouncilMatter m)
            => Institution.CouncilHouseIds.Where(IsActiveHouse).Where(h => h != m.FamilyA && h != m.FamilyB).Select(HeadOf).Where(h => h != 0 && GetPerson(h)?.InColony == true).Distinct().Where(h => !m.Voted.Contains(h)).ToList();

        /// <summary>
        /// Moves open matters along. A head the player controls is not voted for: their ballot waits, up to the window, and is simply missing if
        /// it never comes. When everyone has spoken, or the window ends, the matter is decided.
        /// </summary>
        public void AdvanceCouncil(int now, Func<int, bool> playerControlled = null)
        {
            foreach (var m in CouncilMatters.Where(x => x.State == CouncilMatterState.Approved && now - x.DecidedTick > 10 * ActivityTuning.TicksPerDay).ToList()) m.State = CouncilMatterState.Expired;
            if (!CouncilSits) { foreach (var m in CouncilMatters.Where(x => x.Open)) m.State = CouncilMatterState.Expired; return; }
            foreach (var m in CouncilMatters.Where(x => x.Open).ToList())
            {
                var waiting = new List<int>();
                foreach (int head in CouncilVotersFor(m))
                {
                    if (playerControlled != null && playerControlled(head) && now - m.RequestedTick < CouncilWindowTicks) { waiting.Add(head); continue; }
                    if (playerControlled != null && playerControlled(head)) continue;         // never answered: counts for nothing
                    int vote = Weigh(head, m);
                    m.Voted.Add(head);
                    if (vote > 0) m.Yes++; else if (vote < 0) m.No++;
                }
                if (waiting.Count > 0) continue;
                m.DecidedTick = now;
                m.State = m.Yes > m.No ? CouncilMatterState.Approved : CouncilMatterState.Rejected;
                if (m.State == CouncilMatterState.Approved && m.Kind == CouncilMatterKind.Censure)
                {
                    var house = GetLineage(m.FamilyA);
                    if (house != null) house.Standing = Math.Max(-100f, house.Standing - 10f);
                    m.State = CouncilMatterState.Used;
                }
            }
        }

        /// <summary>
        /// When the council approves a union that a marriage alliance already agreed on, finishes it at once rather than waiting for the
        /// negotiation to be run again.
        /// </summary>
        public int CompleteApprovedUnions(int now)
        {
            int done = 0;
            foreach (var m in CouncilMatters.Where(x => x.Kind == CouncilMatterKind.Union && x.State == CouncilMatterState.Approved).ToList())
            {
                var alliance = MarriageAlliances.FirstOrDefault(a => a.UnionTargetFamilyId != 0 && (a.UnionState == HouseUnionState.Eligible || a.UnionState == HouseUnionState.Agreed) &&
                    (a.FamilyA == m.FamilyA && a.FamilyB == m.FamilyB || a.FamilyA == m.FamilyB && a.FamilyB == m.FamilyA));
                if (alliance == null) continue;
                int target = alliance.UnionTargetFamilyId, source = target == alliance.FamilyA ? alliance.FamilyB : alliance.FamilyA;
                if (UniteFamilies(source, target, now)) { alliance.UnionState = HouseUnionState.Completed; alliance.LastChangedTick = now; done++; }
            }
            // A head's accepted petition waits for the same decision. If the council refuses, or never answers, the petition ends there.
            foreach (var q in HousePetitions.Where(x => x.AwaitingCouncil && x.Kind == PetitionKind.Union).ToList())
            {
                int from = FamilyOf(q.PersonId);
                var matter = CouncilMatters.Where(m => m.Kind == CouncilMatterKind.Union && Pair(m, q.FamilyId, from)).OrderByDescending(m => m.Id).FirstOrDefault();
                if (GetPerson(q.PersonId)?.InColony != true || from != q.SourceFamilyId || HeadOf(q.SourceFamilyId) != q.PersonId || !CouncilSits) { q.AwaitingCouncil = false; q.State = PetitionState.Expired; continue; }
                if (matter == null || matter.State == CouncilMatterState.Pending) continue;
                if (matter.State == CouncilMatterState.Approved && UniteFamilies(q.FamilyId, from, now)) { q.AwaitingCouncil = false; done++; continue; }
                if (matter.State != CouncilMatterState.Approved) { q.AwaitingCouncil = false; q.State = PetitionState.Denied; }
            }
            return done;
        }

        public bool AnswerCouncil(int matterId, int head, bool yes, int now)
        {
            var m = CouncilMatters.FirstOrDefault(x => x.Id == matterId);
            if (m == null || !m.Open || !CouncilVotersFor(m).Contains(head)) return false;
            m.Voted.Add(head);
            if (yes) m.Yes++; else m.No++;
            return true;
        }

        /// <summary>+1, -1 or 0 (no view). A head only weighs what they have reason to know.</summary>
        private int Weigh(int head, CouncilMatter m)
        {
            int own = FamilyOf(head);
            if (m.Kind == CouncilMatterKind.Union)
            {
                int headA = HeadOf(m.FamilyA), headB = HeadOf(m.FamilyB);
                float trust = ((GetBond(head, headA)?.Trust ?? 0f) + (GetBond(head, headB)?.Trust ?? 0f)) / 200f;
                float grievance = Math.Max(GetTie(own, m.FamilyA)?.Grievance ?? 0f, GetTie(own, m.FamilyB)?.Grievance ?? 0f) / 100f;
                int adults = Math.Max(1, Persons.Count(p => p.InColony && !p.Juvenile));
                int joined = Persons.Count(p => p.InColony && !p.Juvenile && (BelongsToFamily(p, m.FamilyA) || BelongsToFamily(p, m.FamilyB)));
                float balance = joined * 2 > adults ? -.5f : 0f;                     // one block should not hold most of the colony
                return trust * .6f - grievance * .6f + balance >= -.05f ? 1 : -1;
            }
            var accused = GetLineage(m.FamilyA);
            bool knows = Heists.Any(h => h.FamilyId == m.FamilyA && CrimeKnowledgeOf(head, h.Id) >= CrimeKnowledgeLevel.Report) ||
                Incidents.Any(i => i.ActorFamily == m.FamilyA && i.Kind != IncidentKind.Gift && i.Kind != IncidentKind.Restitution && KnowsIncident(head, i) && head != 0);
            if (!knows) return 0;
            return (accused?.Crime?.Heat ?? 0f) >= 40f ? 1 : -1;
        }
    }
}
