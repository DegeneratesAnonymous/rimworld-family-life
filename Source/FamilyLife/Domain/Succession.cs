using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum SuccessionCustom : byte { Seniority, Primogeniture, Merit, Designation, Consensus }
    public enum SuccessionStage : byte { Orderly, Negotiation, Compromise, Disputed, Branched, Resolved, Returned }
    public enum SupportReason : byte { None, Kin, Custom, Service, Affection, Competence, Promise, Respect }

    [Flags]
    public enum LegitimacyFactor : ushort
    {
        None = 0, Kin = 1, Seniority = 2, Service = 4, Competence = 8, Relationships = 16, Promise = 32, Conduct = 64, Ideology = 128, Prestige = 256
    }

    public sealed class LegitimacyContext
    {
        /// <summary>0-1 fit between the person's convictions and the house's. Missing means neutral.</summary>
        public Func<int, float> Ideology;
        public Func<int, bool> Free;
    }

    /// <summary>One reason a person is or is not seen as a rightful leader, as the observer is able to know it.</summary>
    public sealed class LegitimacyPart
    {
        public LegitimacyFactor Factor;
        public float Value;                      // contribution to the score, can be negative
    }

    public sealed class LegitimacyAssessment
    {
        public int PersonId, FamilyId;
        public float Score, Willingness;
        public bool Reluctant;
        public LegitimacyFactor Factors;
        public List<LegitimacyPart> Parts = new List<LegitimacyPart>();
    }

    public sealed partial class SuccessionCase
    {
        public int Id, FamilyId, FormerHeadId, StartedTick, StageTick, ChosenId, RunnerUpId, BranchHouseId;
        public SuccessionStage Stage;
        public SuccessionCustom Custom;
        public float Dispute;
        public LegitimacyFactor DecidingFactors;
        public List<int> Declined = new List<int>();
        public bool Open => Stage <= SuccessionStage.Disputed;
    }

    public sealed partial class Lineage
    {
        public SuccessionCustom Custom;
        public bool CustomSet;
        public List<int> Precedent = new List<int> { 0, 0, 0, 0, 0 };
        public int DesignatedHeirId, DesignatedTick;
        public List<int> DesignationKnownBy = new List<int>();
    }

    public sealed partial class FamilyRegistry
    {
        public const float LegitimacyThreshold = 40f;
        public const int VacancyGraceTicks = ActivityTuning.TicksPerDay;
        public List<SuccessionCase> SuccessionCases = new List<SuccessionCase>();
        public int NextSuccessionCaseId = 1;

        public SuccessionCase OpenSuccession(int family) => SuccessionCases.FirstOrDefault(c => c.FamilyId == family && c.Open);

        /// <summary>The house's custom: what its own past successions established, else what its culture plausibly leans toward.</summary>
        public SuccessionCustom CustomOf(int family)
        {
            var house = GetLineage(family);
            if (house == null) return SuccessionCustom.Seniority;
            var counts = house.Precedent ?? (house.Precedent = new List<int> { 0, 0, 0, 0, 0 });
            while (counts.Count < 5) counts.Add(0);
            if (counts.Any(c => c > 0)) return (SuccessionCustom)counts.IndexOf(counts.Max());
            if (house.CustomSet) return house.Custom;
            float status = house.CultureOf(Dim.Status) + house.CultureOf(Dim.Martial) * .5f, hearth = house.CultureOf(Dim.Hearth) + house.CultureOf(Dim.Devotion) * .5f;
            float merit = house.CultureOf(Dim.Scholarly) + house.CultureOf(Dim.Craft), moot = house.CultureOf(Dim.Revelry) + house.CultureOf(Dim.Faith) * .5f;
            float best = Math.Max(Math.Max(status, hearth), Math.Max(merit, moot));
            if (best < 8f) return SuccessionCustom.Seniority;
            return best == status ? SuccessionCustom.Primogeniture : best == merit ? SuccessionCustom.Merit : best == moot ? SuccessionCustom.Consensus : SuccessionCustom.Seniority;
        }

        /// <summary>Records that the head named an heir. Who knows about it is part of the record, so a private choice stays private.</summary>
        public bool DesignateHeir(int family, int head, int heir, IEnumerable<int> toldPeople, int now)
        {
            var house = GetLineage(family); var h = GetPerson(heir);
            if (house == null || HeadOf(family) != head || h?.InColony != true || h.Juvenile || !BelongsToFamily(h, family) || heir == head) return false;
            house.DesignatedHeirId = heir; house.DesignatedTick = now;
            house.DesignationKnownBy = new[] { head, heir }.Concat(toldPeople ?? Enumerable.Empty<int>()).Distinct().Take(64).ToList();
            return true;
        }

        // ---- legitimacy ----

        public LegitimacyAssessment AssessLegitimacy(int person, int family, int observer, LegitimacyContext ctx = null, int formerHead = 0)
        {
            ctx = ctx ?? new LegitimacyContext();
            var house = GetLineage(family); var p = GetPerson(person);
            var result = new LegitimacyAssessment { PersonId = person, FamilyId = family };
            if (house == null || p == null) return result;
            var custom = CustomOf(family);
            var adults = MembersOf(family).Where(m => m.InColony && !m.Juvenile).ToList();
            int from = formerHead != 0 ? formerHead : house.VacantHeadId;

            float kin = KinCloseness(p, from, house);
            float seniority = adults.Count <= 1 ? 1f : 1f - (float)adults.Count(a => a.BirthAbsTick < p.BirthAbsTick) / (adults.Count - 1);
            var term = StewardRecords.Where(s => s.PersonId == person && s.FamilyId == family).OrderByDescending(s => s.AppointedTick).FirstOrDefault();
            float prestige = (PrestigeOf(person)?.Value ?? 0f) / 100f;
            float service = Math.Min(1f, (term == null ? 0f : term.Successes / 12f) + prestige * .5f + OfficeWeight(person) * .25f);
            float competence = Math.Min(1f, p.TopSkillLevel / 15f);
            var judges = adults.Where(a => a.Id != person).Take(20).ToList();
            float relations = judges.Count == 0 ? .5f : judges.Average(j => ((GetBond(j.Id, person)?.Trust ?? 0f) + (GetBond(j.Id, person)?.Attachment ?? 0f) + 100f) / 200f);
            bool promiseVisible = house.DesignatedHeirId == person && (observer == 0 || house.DesignationKnownBy.Contains(observer));
            float promise = promiseVisible ? 1f : 0f;
            float ideology = ctx.Ideology == null ? .5f : Math.Max(0f, Math.Min(1f, ctx.Ideology(person)));
            float conduct = Incidents.Where(i => i.ActorId == person && !i.Resolved && (i.Kind == IncidentKind.Theft || i.Kind == IncidentKind.Injury || i.Kind == IncidentKind.Intimidation) && KnowsIncident(observer, i))
                .Take(8).Sum(i => Math.Min(1f, i.Severity / 60f));

            // The custom decides what counts most; the rest still count, so no single measure can crown anyone.
            float[] w = Weights(custom);
            AddPart(result, LegitimacyFactor.Kin, w[0] * kin);
            AddPart(result, LegitimacyFactor.Seniority, w[1] * seniority);
            AddPart(result, LegitimacyFactor.Service, w[2] * service);
            AddPart(result, LegitimacyFactor.Competence, w[3] * competence);
            AddPart(result, LegitimacyFactor.Relationships, w[4] * relations);
            AddPart(result, LegitimacyFactor.Promise, w[5] * promise);
            AddPart(result, LegitimacyFactor.Ideology, w[6] * ideology);
            AddPart(result, LegitimacyFactor.Prestige, w[7] * prestige);
            if (conduct > 0f) AddPart(result, LegitimacyFactor.Conduct, -Math.Min(.45f, conduct * .25f));
            result.Score = Math.Max(0f, Math.Min(100f, 100f * result.Parts.Sum(x => x.Value)));
            result.Willingness = WillingnessOf(person, family, term != null);
            result.Reluctant = result.Willingness < 35f;
            return result;
        }

        private static void AddPart(LegitimacyAssessment a, LegitimacyFactor factor, float value)
        {
            if (Math.Abs(value) < .0005f) return;
            a.Parts.Add(new LegitimacyPart { Factor = factor, Value = value }); a.Factors |= factor;
        }

        // kin, seniority, service, competence, relationships, promise, ideology, prestige  (sum = 1)
        private static float[] Weights(SuccessionCustom custom)
        {
            switch (custom)
            {
                case SuccessionCustom.Primogeniture: return new[] { .32f, .10f, .10f, .06f, .12f, .12f, .06f, .12f };
                case SuccessionCustom.Merit: return new[] { .08f, .04f, .26f, .24f, .12f, .08f, .08f, .10f };
                case SuccessionCustom.Designation: return new[] { .10f, .06f, .10f, .08f, .10f, .38f, .06f, .12f };
                case SuccessionCustom.Consensus: return new[] { .08f, .06f, .12f, .08f, .36f, .08f, .10f, .12f };
                default: return new[] { .16f, .30f, .12f, .08f, .14f, .08f, .06f, .06f };
            }
        }

        private float KinCloseness(PersonRecord p, int from, Lineage house)
        {
            if (from == 0) return BelongsByDescent(p, house) ? .45f : .2f;
            var head = GetPerson(from);
            if (head == null) return .3f;
            if (p.ParentIds.Contains(from)) return 1f;
            if (head.ParentIds.Any(pr => p.ParentIds.Contains(pr))) return .72f;
            if (p.SpouseIds.Contains(from) || head.SpouseIds.Contains(p.Id)) return .55f;
            var parents = p.ParentIds.Select(GetPerson).Where(x => x != null);
            if (parents.Any(par => par.ParentIds.Contains(from))) return .78f;
            return BelongsByDescent(p, house) ? .4f : .18f;
        }

        private static bool BelongsByDescent(PersonRecord p, Lineage house) => p.PrimaryLineageId == house.Id;

        private float WillingnessOf(int person, int family, bool hasServed)
        {
            float w = 55f;
            var agency = AgencyOf(person);
            if (agency != null && agency.Active)
            {
                if (agency.Goal == PersonalGoal.Independence) w -= 22f;
                if (agency.Goal == PersonalGoal.Recognition) w += 14f;
            }
            w += Math.Min(12f, (PrestigeOf(person)?.Value ?? 0f) * .15f) + (hasServed ? 8f : 0f);
            w += AssessBelonging(person).TrustedConnections * 2f - AssessBelonging(person).StrainedConnections * 3f;
            return Math.Max(0f, Math.Min(100f, w));
        }

        // ---- support: members back candidates for their own reasons ----

        public SupportReason SupportFor(int member, IList<LegitimacyAssessment> candidates, int family, out int chosen, int observer = 0)
        {
            chosen = 0;
            SupportReason reason = SupportReason.None; float best = float.MinValue;
            foreach (var c in candidates)
            {
                // Each member weighs the same measures differently; the spread comes from the member, not from a roll.
                var bond = GetBond(member, c.PersonId);
                float own = (bond?.Attachment ?? 0f) / 100f * 1.2f + (bond?.Trust ?? 0f) / 100f * .6f;
                var parts = c.Parts.ToDictionary(x => x.Factor, x => x.Value);
                float total = c.Score / 100f + own;
                if (c.PersonId == member) total += .08f;
                if (total <= best) continue;
                best = total; chosen = c.PersonId;
                reason = own >= .45f ? SupportReason.Affection : DominantReason(parts, member);
            }
            return reason;
        }

        private static SupportReason DominantReason(Dictionary<LegitimacyFactor, float> parts, int member)
        {
            SupportReason top = SupportReason.None; float best = 0f; int n = 0;
            foreach (var pair in parts)
            {
                SupportReason r = ReasonOf(pair.Key);
                if (r == SupportReason.None || pair.Value <= 0f) continue;
                float tilt = 1f + ((member * (131 + n * 17)) % 9 - 4) / 25f;   // stable personal emphasis
                n++;
                if (pair.Value * tilt > best) { best = pair.Value * tilt; top = r; }
            }
            return top;
        }

        private static SupportReason ReasonOf(LegitimacyFactor f)
        {
            switch (f)
            {
                case LegitimacyFactor.Kin: case LegitimacyFactor.Seniority: return SupportReason.Kin;
                case LegitimacyFactor.Service: return SupportReason.Service;
                case LegitimacyFactor.Competence: return SupportReason.Competence;
                case LegitimacyFactor.Relationships: return SupportReason.Affection;
                case LegitimacyFactor.Promise: return SupportReason.Promise;
                case LegitimacyFactor.Prestige: return SupportReason.Respect;
                case LegitimacyFactor.Ideology: return SupportReason.Custom;
                default: return SupportReason.None;
            }
        }

        public List<LegitimacyAssessment> SuccessionCandidates(int family, LegitimacyContext ctx = null, int formerHead = 0)
        {
            return MembersOf(family).Where(p => p.InColony && !p.Juvenile && p.Id != formerHead).OrderBy(p => p.Id).Take(24)
                .Select(p => AssessLegitimacy(p.Id, family, 0, ctx, formerHead)).OrderByDescending(a => a.Score).ThenBy(a => a.PersonId).ToList();
        }

        /// <summary>Counts of members backing each candidate (a member who backs themselves counts too). Children do not vote.</summary>
        public Dictionary<int, int> SupportCounts(int family, IList<LegitimacyAssessment> candidates)
        {
            var counts = candidates.ToDictionary(c => c.PersonId, c => 0);
            foreach (var m in MembersOf(family).Where(x => x.InColony && !x.Juvenile))
            { SupportFor(m.Id, candidates, family, out int chosen); if (chosen != 0) counts[chosen]++; }
            return counts;
        }

        // ---- the process ----

        /// <summary>
        /// Starts and advances successions. Event-driven: nothing here does work for a house with a living, present head. Orderly succession
        /// is tried first; only after that do negotiation, compromise, branching and an open dispute follow.
        /// </summary>
        public void MaintainSuccession(int now, LegitimacyContext ctx = null)
        {
            foreach (var house in Lineages.Where(h => h.AbsorbedIntoId == 0).ToList())
            {
                int current = HeadOf(house.Id);          // refreshes the interim head and the vacancy marker
                if (house.VacantHeadId == 0 && OpenSuccession(house.Id) == null) continue;
                var open = OpenSuccession(house.Id);
                var former = GetPerson(house.VacantHeadId);
                if (open == null)
                {
                    if (house.VacantHeadId == 0) continue;
                    if (house.VacantSeenTick == 0) house.VacantSeenTick = now;
                    bool gone = former == null || !former.IsAlive;
                    if (former?.InColony == true && !former.Juvenile && BelongsToFamily(former, house.Id)) { house.HeadId = former.Id; house.VacantHeadId = 0; house.VacantSeenTick = 0; continue; }
                    if (!gone && now - house.VacantSeenTick < VacancyGraceTicks) continue;
                    if (current == 0) continue;
                    if (SuccessionCases.Count >= 64) { var old = SuccessionCases.FirstOrDefault(c => !c.Open); if (old != null) SuccessionCases.Remove(old); else continue; }
                    open = new SuccessionCase { Id = NextSuccessionCaseId++, FamilyId = house.Id, FormerHeadId = house.VacantHeadId, StartedTick = now, StageTick = now, Stage = SuccessionStage.Orderly, Custom = CustomOf(house.Id) };
                    SuccessionCases.Add(open);
                }
                else if (former?.InColony == true && !former.Juvenile && BelongsToFamily(former, house.Id) && open.Stage <= SuccessionStage.Negotiation)
                {   // The absent head came back before anyone was named.
                    house.HeadId = former.Id; house.VacantHeadId = 0; house.VacantSeenTick = 0; open.Stage = SuccessionStage.Returned; open.StageTick = now; continue;
                }
                Advance(house, open, now, ctx);
            }
        }

        private void Advance(Lineage house, SuccessionCase c, int now, LegitimacyContext ctx)
        {
            var ranked = SuccessionCandidates(house.Id, ctx, c.FormerHeadId).Where(a => !c.Declined.Contains(a.PersonId)).ToList();
            // Someone reluctant is asked once and may refuse; refusing is a real outcome, not a failure to model.
            foreach (var a in ranked.Where(a => a.Reluctant).ToList()) { c.Declined.Add(a.PersonId); ranked.Remove(a); }
            var legit = ranked.Where(a => a.Score >= LegitimacyThreshold).ToList();
            var support = SupportCounts(house.Id, ranked);
            int voters = Math.Max(1, support.Values.Sum());
            var leader = legit.FirstOrDefault(); var runner = legit.Skip(1).FirstOrDefault();
            c.RunnerUpId = runner?.PersonId ?? 0;
            float share(LegitimacyAssessment a) => a == null ? 0f : support[a.PersonId] / (float)voters;
            float margin = leader == null ? 0f : leader.Score - (runner?.Score ?? 0f);
            int age = now - c.StageTick;

            switch (c.Stage)
            {
                case SuccessionStage.Orderly:
                    if (leader != null && (runner == null || margin >= 12f) && share(leader) >= .5f) { Settle(house, c, leader, now, SuccessionStage.Resolved); return; }
                    c.Stage = SuccessionStage.Negotiation; c.StageTick = now; break;
                case SuccessionStage.Negotiation:
                    if (leader != null && (runner == null || margin >= 6f) && share(leader) >= .45f) { Settle(house, c, leader, now, SuccessionStage.Resolved); return; }
                    if (age >= 2 * ActivityTuning.TicksPerDay) { c.Stage = SuccessionStage.Compromise; c.StageTick = now; }
                    break;
                case SuccessionStage.Compromise:
                    bool rivals = leader != null && runner != null && (GetBond(runner.PersonId, leader.PersonId)?.Tension ?? 0f) >= 40f;
                    if (leader != null && runner != null && share(runner) >= .3f && !rivals)
                    {   // The rival is brought into the house's work rather than defeated.
                        if (!house.Stewards.Contains(runner.PersonId)) { house.Stewards.Add(runner.PersonId); BeginStewardship(runner.PersonId, house.Id, now); }
                        c.Dispute = 20f;
                        Settle(house, c, leader, now, SuccessionStage.Resolved); return;
                    }
                    if (leader != null && share(leader) >= .4f && !(rivals && share(runner) >= .35f)) { Settle(house, c, leader, now, SuccessionStage.Resolved); return; }
                    c.Stage = SuccessionStage.Disputed; c.StageTick = now; break;
                case SuccessionStage.Disputed:
                    c.Dispute = Math.Min(100f, 30f + 60f * (1f - Math.Min(1f, margin / 30f)));
                    if (leader == null) break;                         // nobody is legitimate; the interim head keeps acting and the question stays open
                    if (age >= 3 * ActivityTuning.TicksPerDay && runner != null && share(runner) >= .35f && share(leader) < .6f)
                    {
                        var rival = GetPerson(runner.PersonId);
                        var branch = CreateFamilyBranch(runner.PersonId, house.Id, null, now);
                        if (branch != null)
                        {
                            foreach (var m in MembersOf(house.Id).Where(m => m.InColony && !m.Juvenile && m.Id != runner.PersonId && m.Id != leader.PersonId).ToList())
                            { SupportFor(m.Id, ranked, house.Id, out int pick); if (pick == runner.PersonId) SetSocialFamily(m.Id, branch.Id, now); }
                            c.BranchHouseId = branch.Id; c.Stage = SuccessionStage.Branched; c.StageTick = now; c.ChosenId = leader.PersonId;
                            house.HeadId = leader.PersonId; house.VacantHeadId = 0; house.VacantSeenTick = 0;
                            branch.HeadId = runner.PersonId;
                            return;
                        }
                    }
                    if (age >= 6 * ActivityTuning.TicksPerDay && share(leader) >= .4f) Settle(house, c, leader, now, SuccessionStage.Resolved);
                    break;
            }
        }

        private void Settle(Lineage house, SuccessionCase c, LegitimacyAssessment winner, int now, SuccessionStage stage)
        {
            house.HeadId = winner.PersonId; house.VacantHeadId = 0; house.VacantSeenTick = 0;
            house.Stewards.Remove(winner.PersonId);
            EndStewardship(winner.PersonId, house.Id, now);       // the office of head replaces the Steward's term
            if (house.DesignatedHeirId == winner.PersonId || GetPerson(house.DesignatedHeirId)?.IsAlive != true) { house.DesignatedHeirId = 0; house.DesignationKnownBy.Clear(); }
            c.ChosenId = winner.PersonId; c.Stage = stage; c.StageTick = now; c.DecidingFactors = winner.Factors;
            var precedent = house.Precedent ?? (house.Precedent = new List<int> { 0, 0, 0, 0, 0 });
            while (precedent.Count < 5) precedent.Add(0);
            var top = winner.Parts.OrderByDescending(x => x.Value).FirstOrDefault();
            SuccessionCustom shown = top == null ? c.Custom : CustomFor(top.Factor);
            precedent[(int)shown] = Math.Min(20, precedent[(int)shown] + 1);
            // Whoever was passed over keeps the memory: the closest overlooked kin carries a little tension toward the new head.
            var overlooked = SuccessionCandidates(house.Id, null, c.FormerHeadId).Where(a => a.PersonId != winner.PersonId && KinCloseness(GetPerson(a.PersonId), c.FormerHeadId, house) >= .9f).FirstOrDefault();
            if (overlooked != null) ChangeBond(overlooked.PersonId, winner.PersonId, new BondChange(-1f, -2f, 4f, 0f), now);
            house.Standing = Math.Max(-100f, Math.Min(100f, house.Standing - c.Dispute * .05f));
        }

        private static SuccessionCustom CustomFor(LegitimacyFactor f)
        {
            switch (f)
            {
                case LegitimacyFactor.Kin: return SuccessionCustom.Primogeniture;
                case LegitimacyFactor.Service: case LegitimacyFactor.Competence: return SuccessionCustom.Merit;
                case LegitimacyFactor.Promise: return SuccessionCustom.Designation;
                case LegitimacyFactor.Relationships: return SuccessionCustom.Consensus;
                default: return SuccessionCustom.Seniority;
            }
        }

        /// <summary>The factors a given observer is entitled to see for a person, with nothing private leaking across perspectives.</summary>
        public List<LegitimacyPart> VisibleLegitimacy(int person, int family, int observer, LegitimacyContext ctx = null)
            => AssessLegitimacy(person, family, observer, ctx).Parts.OrderByDescending(x => Math.Abs(x.Value)).ToList();
    }
}
