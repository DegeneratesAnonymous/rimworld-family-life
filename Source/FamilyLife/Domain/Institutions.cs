using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum InstitutionKind : byte { Consensus, Council, Election, Authoritarian }
    public enum ElectionState : byte { Campaign, Voting, Counted, Aborted }
    public enum PoliticalFactor : byte { None, Ideology, Family, Legitimacy, Friendship, Fear, Needs, Policy, Endorsement }
    public enum CoupGoal : byte { ReplaceLeader, SeizeOffice, ReversePolicy }
    public enum CoupStage : byte { Recruiting, Preparing, Executing, Aftermath }
    public enum CoupState : byte { Active, Succeeded, Failed, Abandoned, Reconciled }

    /// <summary>
    /// The separate things a person weighs about a candidate. They are measured independently and never normalised against each other,
    /// so strong family attachment does not lower ideological conviction, or the reverse.
    /// </summary>
    public sealed class VoterDimensions
    {
        public float Ideology, Family, Legitimacy, Friendship, Fear, Needs, Policy, Endorsement;
    }

    public sealed class SupportReport
    {
        public int VoterId, CandidateId;
        public VoterDimensions Dims = new VoterDimensions();
        public float Total;
        public PoliticalFactor Dominant;
    }

    public sealed class PoliticalContext
    {
        /// <summary>-1..1: how well a candidate's convictions fit the voter's, scaled by how certain the voter is. Missing = no opinion.</summary>
        public Func<int, int, float> IdeologyFit;
        /// <summary>-1..1: does this candidate answer the voter's present needs.</summary>
        public Func<int, int, float> NeedFit;
        public Func<int, int, float> PolicyFit;
        /// <summary>0-1 of what the person could contribute to a coup (armed, able, equipped); not a head count.</summary>
        public Func<int, float> MaterialCapacity;
        /// <summary>0-1 of what the current leader could muster against a coup.</summary>
        public Func<int, float> DefenceCapacity;
        public Func<int, bool> Free;
    }

    public sealed partial class ColonyInstitution
    {
        public InstitutionKind Kind;
        public int SinceTick, LeaderId, TermEndTick;
        public List<int> CouncilHouseIds = new List<int>();
        public int PollMapId = -1, PollX, PollZ;
    }

    public sealed partial class ElectionEndorsement { public int FamilyId, EndorserId, CandidateId; public float Weight; }
    public sealed partial class ElectionVote { public int VoterId, CandidateId; public PoliticalFactor Reason; }

    public sealed partial class Election
    {
        public int Id, StartedTick, VotingTick, CountedTick, WinnerId;
        public List<int> Voted = new List<int>();
        public ElectionState State;
        public float Margin;
        public int Abstained;
        public List<int> Candidates = new List<int>();
        public List<ElectionEndorsement> Endorsements = new List<ElectionEndorsement>();
        public List<ElectionVote> Votes = new List<ElectionVote>();
        public bool Open => State == ElectionState.Campaign || State == ElectionState.Voting;
    }

    public sealed partial class CoupParticipant { public int PersonId; public CrimeStance Stance; }

    public sealed partial class CoupPlan
    {
        public int Id, OrganizerId, TargetLeaderId, PolicyId, StartedTick, StageTick, Setbacks, EndedTick, DiscoveredBy;
        public CoupGoal Goal;
        public CoupStage Stage;
        public CoupState State;
        public float Discovery;
        public List<CoupParticipant> Participants = new List<CoupParticipant>();
        public bool Open => State == CoupState.Active;
        public int WillingCount => Participants.Count(p => p.Stance == CrimeStance.Willing);
    }

    public sealed partial class FamilyRegistry
    {
        public const int TermTicks = 15 * ActivityTuning.TicksPerDay;
        public const int CampaignTicks = 2 * ActivityTuning.TicksPerDay;
        public const int VotingWindowTicks = ActivityTuning.TicksPerDay;
        public const float AbstainBelow = .05f;
        public ColonyInstitution Institution = new ColonyInstitution();
        public List<Election> Elections = new List<Election>();
        public List<CoupPlan> Coups = new List<CoupPlan>();
        public int NextElectionId = 1, NextCoupId = 1, LastCoupPlanTick = -1000000;

        // ---- how a person weighs a candidate ----

        public SupportReport Support(int voter, int candidate, PoliticalContext ctx, Election election = null)
        {
            ctx = ctx ?? new PoliticalContext();
            var report = new SupportReport { VoterId = voter, CandidateId = candidate };
            var d = report.Dims;
            var bond = GetBond(voter, candidate);
            int vh = FamilyOf(voter), ch = FamilyOf(candidate);
            d.Ideology = Clamp1(ctx.IdeologyFit == null ? 0f : ctx.IdeologyFit(voter, candidate));
            d.Family = vh != 0 && vh == ch ? Clamp1(.4f + .6f * (bond?.Attachment ?? 0f) / 100f) : 0f;
            d.Legitimacy = ch == 0 ? 0f : Clamp1((AssessLegitimacy(candidate, ch, voter).Score / 100f - .5f) * 2f);
            d.Friendship = Clamp1(((bond?.Attachment ?? 0f) + (bond?.Trust ?? 0f)) / 200f);
            d.Fear = Clamp1((bond?.Tension ?? 0f) / 100f);
            d.Needs = Clamp1(ctx.NeedFit == null ? 0f : ctx.NeedFit(voter, candidate));
            d.Policy = Clamp1(ctx.PolicyFit == null ? 0f : ctx.PolicyFit(voter, candidate));
            if (election != null)
                foreach (var e in election.Endorsements.Where(e => e.CandidateId == candidate))
                {   // An endorsement persuades through trust in whoever gave it, not through how many live in their house.
                    float trust = FamilyOf(voter) == e.FamilyId ? 1f : Math.Max(0f, (GetBond(voter, e.EndorserId)?.Trust ?? 0f) / 100f);
                    d.Endorsement = Clamp1(d.Endorsement + e.Weight * trust);
                }
            bool coercive = Institution.Kind == InstitutionKind.Authoritarian;
            float fear = coercive ? d.Fear * .35f : -d.Fear * .45f;
            var parts = new[]
            {
                new KeyValuePair<PoliticalFactor, float>(PoliticalFactor.Ideology, d.Ideology * .30f),
                new KeyValuePair<PoliticalFactor, float>(PoliticalFactor.Family, d.Family * .20f),
                new KeyValuePair<PoliticalFactor, float>(PoliticalFactor.Legitimacy, d.Legitimacy * .22f),
                new KeyValuePair<PoliticalFactor, float>(PoliticalFactor.Friendship, d.Friendship * .18f),
                new KeyValuePair<PoliticalFactor, float>(PoliticalFactor.Fear, fear),
                new KeyValuePair<PoliticalFactor, float>(PoliticalFactor.Needs, d.Needs * .20f),
                new KeyValuePair<PoliticalFactor, float>(PoliticalFactor.Policy, d.Policy * .15f),
                new KeyValuePair<PoliticalFactor, float>(PoliticalFactor.Endorsement, d.Endorsement * .15f)
            };
            report.Total = parts.Sum(p => p.Value);
            var top = parts.OrderByDescending(p => Math.Abs(p.Value)).First();
            report.Dominant = Math.Abs(top.Value) < .01f ? PoliticalFactor.None : top.Key;
            return report;
        }

        private static float Clamp1(float v) => float.IsNaN(v) ? 0f : Math.Max(-1f, Math.Min(1f, v));

        // ---- institution shape ----

        public bool SetInstitution(InstitutionKind kind, int leader, int now)
        {
            var p = GetPerson(leader);
            if ((kind == InstitutionKind.Authoritarian || kind == InstitutionKind.Election && leader != 0) && (p?.InColony != true || p.Juvenile)) return false;
            Institution.Kind = kind; Institution.SinceTick = now; Institution.LeaderId = leader;
            Institution.TermEndTick = kind == InstitutionKind.Election || kind == InstitutionKind.Council ? now + TermTicks : 0;
            Institution.CouncilHouseIds = new List<int>();
            if (kind == InstitutionKind.Council) FormCouncil(now);
            return true;
        }

        /// <summary>One seat per house, ranked by earned standing, never by headcount, so a small house still sits at the table.</summary>
        public List<int> FormCouncil(int now, int seats = 7)
        {
            var houses = Lineages.Where(h => h.AbsorbedIntoId == 0 && MembersOf(h.Id).Any(m => m.InColony && !m.Juvenile))
                .OrderByDescending(h => h.Standing).ThenBy(h => h.Id).Take(Math.Max(1, seats)).Select(h => h.Id).ToList();
            Institution.CouncilHouseIds = houses;
            return houses;
        }

        // ---- elections ----

        public Election CallElection(IEnumerable<int> candidates, int now)
        {
            if (Institution.Kind != InstitutionKind.Election || Elections.Any(e => e.Open)) return null;
            var ids = (candidates ?? Enumerable.Empty<int>()).Distinct().Where(c => { var p = GetPerson(c); return p?.InColony == true && !p.Juvenile; }).Take(6).ToList();
            if (ids.Count < 2) return null;
            if (Elections.Count >= 16) Elections.RemoveAt(0);
            var election = new Election { Id = NextElectionId++, StartedTick = now, VotingTick = now + CampaignTicks, Candidates = ids };
            Elections.Add(election);
            return election;
        }

        public bool EndorseCandidate(int electionId, int family, int endorser, int candidate)
        {
            var e = Elections.FirstOrDefault(x => x.Id == electionId);
            if (e == null || e.State != ElectionState.Campaign || !e.Candidates.Contains(candidate) || HeadOf(family) != endorser) return false;
            e.Endorsements.RemoveAll(x => x.FamilyId == family);
            var house = GetLineage(family);
            float regard = Math.Max(0f, Math.Min(100f, ((house?.Standing ?? 0f) + 100f) / 2f)) / 100f;
            float personal = (PrestigeOf(endorser)?.Value ?? 0f) / 100f;
            e.Endorsements.Add(new ElectionEndorsement { FamilyId = family, EndorserId = endorser, CandidateId = candidate, Weight = Math.Min(.8f, .1f + regard * .35f + personal * .35f) });
            return true;
        }

        /// <summary>Moves open elections along. Call from maintenance; it does nothing when no election is open.</summary>
        public void AdvanceElections(int now, PoliticalContext ctx = null, bool physicalVoting = false)
        {
            foreach (var e in Elections.Where(x => x.Open).ToList())
            {
                e.Candidates.RemoveAll(c => GetPerson(c)?.InColony != true);
                if (e.Candidates.Count < 2 && e.State == ElectionState.Campaign) { e.State = ElectionState.Aborted; continue; }
                if (e.State == ElectionState.Campaign && now >= e.VotingTick) e.State = ElectionState.Voting;
                if (e.State != ElectionState.Voting) continue;
                if (!physicalVoting) { Count(e, now, ctx); continue; }
                // Physical voting: ballots are cast by people who actually went to the polling place; the rest are absent.
                var eligible = EligibleVoters(e);
                bool everyone = eligible.All(v => e.Votes.Any(b => b.VoterId == v) || e.Voted.Contains(v));
                if (everyone || now >= e.VotingTick + VotingWindowTicks) Tally(e, now);
            }
            // A term ending is an event, not a poll: only then does a new election open on its own.
            if (Institution.Kind == InstitutionKind.Election && Institution.TermEndTick != 0 && now >= Institution.TermEndTick && !Elections.Any(e => e.Open))
            {
                var field = Persons.Where(p => p.InColony && !p.Juvenile).Select(p => AssessLegitimacy(p.Id, FamilyOf(p.Id), 0)).Where(a => a.Score >= 30f && !a.Reluctant)
                    .OrderByDescending(a => a.Score).ThenBy(a => a.PersonId).Take(4).Select(a => a.PersonId).ToList();
                if (CallElection(field, now) == null) Institution.TermEndTick = now + ActivityTuning.TicksPerDay;
            }
        }

        /// <summary>Colonists who could vote: adults living in the colony. The game decides who is free to go.</summary>
        public List<int> EligibleVoters(Election e) => Persons.Where(p => p.InColony && !p.Juvenile).OrderBy(p => p.Id).Take(160).Select(p => p.Id).ToList();

        /// <summary>
        /// Records one ballot from a voter who has physically reached the polling place. Their choice follows their own weighing of the candidates,
        /// or no ballot at all if none of them is worth it. A person can vote once.
        /// </summary>
        public bool CastBallot(int electionId, int voter, PoliticalContext ctx, int now)
        {
            var e = Elections.FirstOrDefault(x => x.Id == electionId); var p = GetPerson(voter);
            if (e == null || e.State != ElectionState.Voting || p?.InColony != true || p.Juvenile || e.Voted.Contains(voter)) return false;
            if (e.Candidates.Count == 0) return false;
            e.Voted.Add(voter);
            var best = e.Candidates.Select(c => Support(voter, c, ctx, e)).OrderByDescending(r => r.Total).ThenBy(r => r.CandidateId).First();
            if (best.Total < AbstainBelow) { e.Abstained++; return true; }
            e.Votes.Add(new ElectionVote { VoterId = voter, CandidateId = best.CandidateId, Reason = best.Dominant });
            return true;
        }

        public bool NeedsToVote(int voter)
        {
            var e = Elections.FirstOrDefault(x => x.State == ElectionState.Voting);
            return e != null && !e.Voted.Contains(voter) && GetPerson(voter)?.InColony == true && !GetPerson(voter).Juvenile;
        }

        private void Tally(Election e, int now)
        {
            var tally = e.Candidates.Select(c => new { Id = c, Votes = e.Votes.Count(v => v.CandidateId == c) }).OrderByDescending(x => x.Votes).ThenByDescending(x => AssessLegitimacy(x.Id, FamilyOf(x.Id), 0).Score).ThenBy(x => x.Id).ToList();
            int total = Math.Max(1, e.Votes.Count);
            if (tally.Count == 0 || tally[0].Votes == 0) { e.State = ElectionState.Aborted; return; }
            e.WinnerId = tally[0].Id; e.Margin = (tally[0].Votes - (tally.Count > 1 ? tally[1].Votes : 0)) / (float)total;
            e.State = ElectionState.Counted; e.CountedTick = now;
            Institution.LeaderId = e.WinnerId; Institution.TermEndTick = now + TermTicks;
        }

        private void Count(Election e, int now, PoliticalContext ctx)
        {
            e.Votes.Clear(); e.Abstained = 0;
            if (e.Candidates.Count == 0) { e.State = ElectionState.Aborted; return; }
            foreach (var voter in Persons.Where(p => p.InColony && !p.Juvenile).OrderBy(p => p.Id).Take(160))
            {
                var best = e.Candidates.Select(c => Support(voter.Id, c, ctx, e)).OrderByDescending(r => r.Total).ThenBy(r => r.CandidateId).First();
                if (best.Total < AbstainBelow) { e.Abstained++; continue; }
                e.Votes.Add(new ElectionVote { VoterId = voter.Id, CandidateId = best.CandidateId, Reason = best.Dominant });
            }
            var tally = e.Candidates.Select(c => new { Id = c, Votes = e.Votes.Count(v => v.CandidateId == c) }).OrderByDescending(x => x.Votes).ThenByDescending(x => AssessLegitimacy(x.Id, FamilyOf(x.Id), 0).Score).ThenBy(x => x.Id).ToList();
            int total = Math.Max(1, e.Votes.Count);
            if (tally[0].Votes == 0) { e.State = ElectionState.Aborted; return; }
            e.WinnerId = tally[0].Id; e.Margin = (tally[0].Votes - (tally.Count > 1 ? tally[1].Votes : 0)) / (float)total;
            e.State = ElectionState.Counted; e.CountedTick = now;
            Institution.LeaderId = e.WinnerId; Institution.TermEndTick = now + TermTicks;
        }

        /// <summary>Totals by reason are public. How an individual voted is shown only to that voter (or the omniscient view).</summary>
        public List<KeyValuePair<PoliticalFactor, int>> ExplainElection(int electionId, int observer, out ElectionVote own)
        {
            own = null;
            var e = Elections.FirstOrDefault(x => x.Id == electionId);
            if (e == null) return new List<KeyValuePair<PoliticalFactor, int>>();
            own = observer == 0 ? null : e.Votes.FirstOrDefault(v => v.VoterId == observer);
            return e.Votes.GroupBy(v => v.Reason).Select(g => new KeyValuePair<PoliticalFactor, int>(g.Key, g.Count())).OrderByDescending(k => k.Value).ToList();
        }

        // ---- coups ----

        public CoupPlan PlanCoup(int organizer, CoupGoal goal, int policyId, PoliticalContext ctx, int now)
        {
            var person = GetPerson(organizer);
            int leader = Institution.LeaderId;
            if (person?.InColony != true || person.Juvenile || leader == 0 || leader == organizer || GetPerson(leader)?.InColony != true) return null;
            if (goal == CoupGoal.ReversePolicy && policyId == 0) return null;                      // a coup needs a concrete goal
            if (Coups.Any(c => c.Open && c.OrganizerId == organizer) || Coups.Count(c => c.Open) >= 3 || now - LastCoupPlanTick < 3 * ActivityTuning.TicksPerDay) return null;
            if (ctx?.Free != null && !ctx.Free(organizer)) return null;
            if (Coups.Count >= 32) { var old = Coups.FirstOrDefault(c => !c.Open); if (old == null) return null; Coups.Remove(old); }
            var coup = new CoupPlan { Id = NextCoupId++, OrganizerId = organizer, TargetLeaderId = leader, Goal = goal, PolicyId = policyId, StartedTick = now, StageTick = now };
            coup.Participants.Add(new CoupParticipant { PersonId = organizer, Stance = CrimeStance.Willing });
            Coups.Add(coup); LastCoupPlanTick = now;
            return coup;
        }

        public bool RecruitForCoup(int coupId, int person, RecruitAnswer answer, int now)
        {
            var coup = Coups.FirstOrDefault(c => c.Id == coupId); var p = GetPerson(person);
            if (coup == null || !coup.Open || coup.Stage > CoupStage.Preparing || p?.InColony != true || p.Juvenile || person == coup.TargetLeaderId || coup.Participants.Any(x => x.PersonId == person)) return false;
            switch (answer)
            {
                case RecruitAnswer.Join: coup.Participants.Add(new CoupParticipant { PersonId = person, Stance = CrimeStance.Willing }); break;
                case RecruitAnswer.Refuse: coup.Participants.Add(new CoupParticipant { PersonId = person, Stance = CrimeStance.Refused }); ChangeBond(person, coup.OrganizerId, new BondChange(-.5f, -1f, 1f, 0f), now); break;
                default:
                    coup.Participants.Add(new CoupParticipant { PersonId = person, Stance = CrimeStance.Exposed });
                    Discover(coup, person, now); break;
            }
            return true;
        }

        public bool AskCoupMember(int coupId, int person)
        {
            var coup = Coups.FirstOrDefault(c => c.Id == coupId); var p = GetPerson(person);
            if (coup == null || !coup.Open || coup.Stage > CoupStage.Preparing || p?.InColony != true || p.Juvenile || person == coup.TargetLeaderId || coup.Participants.Any(x => x.PersonId == person)) return false;
            coup.Participants.Add(new CoupParticipant { PersonId = person, Stance = CrimeStance.Unasked });
            return true;
        }

        public bool AnswerCoup(int coupId, int person, RecruitAnswer answer, int now)
        {
            var coup = Coups.FirstOrDefault(c => c.Id == coupId);
            var entry = coup?.Participants.FirstOrDefault(x => x.PersonId == person && x.Stance == CrimeStance.Unasked);
            if (entry == null || !coup.Open) return false;
            switch (answer)
            {
                case RecruitAnswer.Join: entry.Stance = CrimeStance.Willing; break;
                case RecruitAnswer.Refuse: entry.Stance = CrimeStance.Refused; ChangeBond(person, coup.OrganizerId, new BondChange(-.5f, -1f, 1f, 0f), now); break;
                default: entry.Stance = CrimeStance.Exposed; Discover(coup, person, now); break;
            }
            return true;
        }

        /// <summary>
        /// A council names the colony's leader when the post is empty or the term has run out. Each seated house's head casts one vote by their
        /// own weighing, so a small house counts as much as a large one.
        /// </summary>
        public Election ConveneCouncil(int now, PoliticalContext ctx = null)
        {
            if (Institution.Kind != InstitutionKind.Council) return null;
            bool vacant = GetPerson(Institution.LeaderId)?.InColony != true, due = Institution.TermEndTick != 0 && now >= Institution.TermEndTick;
            if (!vacant && !due) return null;
            var seats = Institution.CouncilHouseIds.Where(IsActiveHouse).ToList();
            if (seats.Count == 0) seats = FormCouncil(now);
            var voters = seats.Select(HeadOf).Where(h => h != 0).Distinct().ToList();
            var field = Persons.Where(p => p.InColony && !p.Juvenile).Select(p => AssessLegitimacy(p.Id, FamilyOf(p.Id), 0)).Where(a => a.Score >= 30f && !a.Reluctant)
                .OrderByDescending(a => a.Score).ThenBy(a => a.PersonId).Take(5).Select(a => a.PersonId).ToList();
            foreach (int head in voters) if (field.Count < 6 && !field.Contains(head)) field.Add(head);
            if (voters.Count == 0 || field.Count == 0) { Institution.TermEndTick = now + ActivityTuning.TicksPerDay; return null; }
            if (Elections.Count >= 16) Elections.RemoveAt(0);
            var e = new Election { Id = NextElectionId++, StartedTick = now, VotingTick = now, Candidates = field, State = ElectionState.Voting };
            foreach (int voter in voters)
            {
                var best = field.Select(c => Support(voter, c, ctx, e)).OrderByDescending(r => r.Total).ThenBy(r => r.CandidateId).First();
                e.Voted.Add(voter);
                if (best.Total < AbstainBelow) { e.Abstained++; continue; }
                e.Votes.Add(new ElectionVote { VoterId = voter, CandidateId = best.CandidateId, Reason = best.Dominant });
            }
            Elections.Add(e);
            Tally(e, now);
            return e;
        }

        private void Discover(CoupPlan coup, int by, int now)
        {
            coup.DiscoveredBy = by; coup.State = CoupState.Failed; coup.Stage = CoupStage.Aftermath; coup.EndedTick = now; coup.StageTick = now;
            ChangeBond(coup.TargetLeaderId, coup.OrganizerId, new BondChange(-4f, -8f, 8f, 0f), now);
        }

        /// <summary>
        /// One bounded step. Capacity is what people can actually bring (equipped, able, free), not how many agree; discovery risk grows with each stage.
        /// Every stage can set back, stall, or end the plan, and a plan with too little support simply lapses.
        /// </summary>
        public void AdvanceCoup(int coupId, PoliticalContext ctx, int now, bool physical = false)
        {
            ctx = ctx ?? new PoliticalContext();
            var coup = Coups.FirstOrDefault(c => c.Id == coupId);
            if (coup == null || !coup.Open) return;
            if (GetPerson(coup.OrganizerId)?.InColony != true) { coup.State = CoupState.Abandoned; coup.EndedTick = now; return; }
            if (Institution.LeaderId != coup.TargetLeaderId) { coup.State = CoupState.Abandoned; coup.EndedTick = now; return; }
            var willing = coup.Participants.Where(p => p.Stance == CrimeStance.Willing && GetPerson(p.PersonId)?.InColony == true).ToList();
            float capacity = willing.Where(p => ctx.Free == null || ctx.Free(p.PersonId)).Sum(p => ctx.MaterialCapacity == null ? .5f : Math.Max(0f, Math.Min(1f, ctx.MaterialCapacity(p.PersonId))));
            float needed = Math.Max(1.2f, .9f * (ctx.DefenceCapacity == null ? 1f : Math.Max(0f, ctx.DefenceCapacity(coup.TargetLeaderId))) + .25f * (coup.Setbacks));
            switch (coup.Stage)
            {
                case CoupStage.Recruiting:
                    if (willing.Count < 3) { if (now - coup.StageTick > 6 * ActivityTuning.TicksPerDay) { coup.State = CoupState.Abandoned; coup.EndedTick = now; } return; }
                    coup.Stage = CoupStage.Preparing; coup.StageTick = now; coup.Discovery = 10f + willing.Count * 4f; return;
                case CoupStage.Preparing:
                    if (now - coup.StageTick < ActivityTuning.TicksPerDay) return;
                    coup.Discovery = Math.Min(95f, coup.Discovery + 8f + willing.Count * 2f - 10f * Secrecy(willing));
                    if (Roll(coup.Id, 1, now) < coup.Discovery * .5f) { Discover(coup, 0, now); return; }
                    if (capacity < needed) { coup.Setbacks++; coup.StageTick = now; if (coup.Setbacks >= 3) { coup.State = CoupState.Abandoned; coup.EndedTick = now; } return; }
                    coup.Stage = CoupStage.Executing; coup.StageTick = now; return;
                case CoupStage.Executing:
                    if (physical) return;                         // in the world, the confrontation itself decides, via ResolveCoup
                    if (willing.Count < 3 || capacity < needed) { coup.Setbacks++; coup.Stage = CoupStage.Preparing; coup.StageTick = now; if (coup.Setbacks >= 3) { coup.State = CoupState.Abandoned; coup.EndedTick = now; } return; }
                    bool found = Roll(coup.Id, 2, now) < coup.Discovery;
                    if (found) { Discover(coup, 0, now); return; }
                    coup.Stage = CoupStage.Aftermath; coup.StageTick = now; coup.EndedTick = now; coup.State = CoupState.Succeeded;
                    Institution.LeaderId = coup.OrganizerId;
                    if (coup.Goal == CoupGoal.SeizeOffice) { Institution.Kind = InstitutionKind.Authoritarian; Institution.TermEndTick = 0; }
                    ChangeBond(coup.TargetLeaderId, coup.OrganizerId, new BondChange(-6f, -10f, 10f, 0f), now);
                    return;
            }
        }

        /// <summary>The moment of confrontation in the world. Same checks as the abstract step: capacity, discovery, and who is free.</summary>
        public void ResolveCoup(int coupId, PoliticalContext ctx, int now)
        {
            var coup = Coups.FirstOrDefault(c => c.Id == coupId);
            if (coup == null || !coup.Open || coup.Stage != CoupStage.Executing) return;
            coup.StageTick = now - ActivityTuning.TicksPerDay;
            AdvanceCoup(coupId, ctx, now, false);
        }

        /// <summary>Silence or a refusal from a player-controlled leader is never consent to a confrontation; the scheme loses momentum instead.</summary>
        public bool AvoidCoupConfrontation(int coupId,int now)
        {
            var coup=Coups.FirstOrDefault(c=>c.Id==coupId);
            if(coup==null||!coup.Open||coup.Stage!=CoupStage.Executing)return false;
            coup.Setbacks++;coup.StageTick=now;
            if(coup.Setbacks>=3){coup.State=CoupState.Abandoned;coup.EndedTick=now;}
            else coup.Stage=CoupStage.Preparing;
            return true;
        }

        /// <summary>Both sides can step back before the plan lands; it ends cleanly and eases the strain rather than leaving a loose thread.</summary>
        public bool ReconcileCoup(int coupId, int now)
        {
            var coup = Coups.FirstOrDefault(c => c.Id == coupId);
            if (coup == null || !coup.Open || coup.Stage == CoupStage.Executing) return false;
            coup.State = CoupState.Reconciled; coup.EndedTick = now;
            ChangeBond(coup.OrganizerId, coup.TargetLeaderId, new BondChange(2f, 3f, -4f, 0f), now);
            return true;
        }

        /// <summary>The members of a plan are known to themselves; the leader learns only if it was discovered.</summary>
        public bool CoupVisibleTo(CoupPlan coup, int observer)
            => coup != null && (observer == 0 || coup.Participants.Any(p => p.PersonId == observer) || coup.DiscoveredBy != 0 && observer == coup.TargetLeaderId || coup.State == CoupState.Succeeded);

        private float Secrecy(List<CoupParticipant> willing)
        {
            if (willing.Count < 2) return 0f;
            var trusts = new List<float>();
            foreach (var a in willing.Take(6)) foreach (var b in willing.Take(6)) if (a.PersonId != b.PersonId) trusts.Add(Math.Max(0f, (GetBond(a.PersonId, b.PersonId)?.Trust ?? 0f) / 100f));
            return trusts.Count == 0 ? 0f : trusts.Average();
        }

        private static float Roll(int id, int stage, int now) => (unchecked((uint)(id * 73856093 ^ stage * 19349663 ^ (now / ActivityTuning.TicksPerDay) * 83492791)) % 100);
    }
}
