using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum CrimeDirection : byte { None, Protection, Contraband, Theft, Intimidation, Favors, Laundering }
    public enum CrimeStatus : byte { None, Considering, Active, Reforming, Reformed }
    public enum CrimeStance : byte { Unasked, Willing, Refused, Exposed, Reformed, Betrayed }
    public enum RecruitAnswer : byte { Join, Refuse, Expose }
    public enum HeistState : byte { Planning, Ready, Executing, Completed, Interrupted, Aborted, Exposed, CleanedUp }
    public enum CrimeKnowledgeLevel : byte { None, Suspicion, Report, Fact }
    public enum LawfulAlternative : byte { Aid, Trade, Employment, Alliance, Restitution }

    [Flags]
    public enum CrimeBarrier : byte { None = 0, NoNeed = 1, NoOpportunity = 2, ValuesAgainst = 4, TooFewWilling = 8, NoPrecedent = 16 }

    public sealed class CrimeContext
    {
        /// <summary>How many real, currently valid targets exist. The game counts them; zero means no opportunity.</summary>
        public int Opportunities;
        /// <summary>Rechecks that the heist target still exists, is on a valid map, and is not the participants' own house.</summary>
        public Func<Heist, bool> TargetValid;
        public Func<int, bool> Free;
    }

    public sealed class CrimeAssessment
    {
        public int FamilyId, WillingMembers, Precedents;
        public bool Need, Opportunity, Values;
        public CrimeBarrier Barriers;
        public bool Eligible => Barriers == CrimeBarrier.None;
        public List<LawfulAlternative> Alternatives = new List<LawfulAlternative>();
    }

    public sealed partial class CrimeRecord
    {
        public CrimeDirection Direction;
        public CrimeStatus Status;
        public int StartedTick, LastPlanTick = -1000000, EndedTick;
        public float Heat, ReformProgress;
        public int LastAmendsIncident;
    }

    public sealed partial class HeistParticipant
    {
        public int PersonId;
        public CrimeStance Stance;
    }

    /// <summary>A planned job. Every state is a recorded step; gains and losses exist only when the matching physical evidence is presented.</summary>
    public sealed partial class Heist
    {
        public int Id, FamilyId, OrganizerId, TargetFamilyId, TargetMapId, TargetToken, PlannedTick, StateTick, GoodsMoved, IncidentId, EvidenceToken;
        public CrimeDirection Direction;
        public HeistState State;
        public float Risk;
        public List<HeistParticipant> Participants = new List<HeistParticipant>();
        public bool Active => State <= HeistState.Executing;
        public int WillingCount => Participants.Count(p => p.Stance == CrimeStance.Willing);
    }

    public sealed partial class CrimeKnowledge
    {
        public int HeistId, PersonId;
        public CrimeKnowledgeLevel Level;
    }

    /// <summary>A protection deal. It is a recorded agreement; money or goods change hands only through recorded, evidenced payments.</summary>
    public sealed partial class ProtectionArrangement
    {
        public int Id, ProtectorFamily, ProtectedFamily, AgreedTick, EndedTick, Payments, PaymentsRecorded, LastPaymentToken, TributeAmount, AgreedBy;
        public bool Agreed => AgreedTick > 0 && EndedTick == 0;
    }

    public sealed partial class Lineage
    {
        public CrimeRecord Crime = new CrimeRecord();
    }

    public sealed partial class FamilyRegistry
    {
        public List<Heist> Heists = new List<Heist>();
        public List<CrimeKnowledge> CrimeKnowledgeRecords = new List<CrimeKnowledge>();
        public List<ProtectionArrangement> ProtectionArrangements = new List<ProtectionArrangement>();
        public int NextHeistId = 1, NextProtectionId = 1;

        public const int HeistPlanningInterval = 2 * ActivityTuning.TicksPerDay;
        private static readonly IncidentKind[] CrimeKinds = { IncidentKind.Theft, IncidentKind.Intimidation };

        public static List<LawfulAlternative> AlternativesFor(HouseNeed need)
        {
            switch (need)
            {
                case HouseNeed.Sustenance: return new List<LawfulAlternative> { LawfulAlternative.Aid, LawfulAlternative.Trade, LawfulAlternative.Employment };
                case HouseNeed.Safety: return new List<LawfulAlternative> { LawfulAlternative.Alliance, LawfulAlternative.Trade };
                case HouseNeed.Influence: case HouseNeed.Legacy: return new List<LawfulAlternative> { LawfulAlternative.Alliance, LawfulAlternative.Employment };
                default: return new List<LawfulAlternative> { LawfulAlternative.Aid, LawfulAlternative.Trade, LawfulAlternative.Restitution };
            }
        }

        // ---- is a criminal direction even plausible? ----

        /// <summary>
        /// All five must hold: a real need, a real target, values that allow it, at least two willing people, and an actual precedent. Size or a single
        /// trait never qualifies a house. The same needs always come back with lawful alternatives.
        /// </summary>
        public CrimeAssessment AssessCrimeDirection(int family, CrimeContext ctx, int now)
        {
            ctx = ctx ?? new CrimeContext();
            var result = new CrimeAssessment { FamilyId = family };
            var house = GetLineage(family);
            if (house == null || !IsActiveHouse(family)) { result.Barriers = CrimeBarrier.NoNeed | CrimeBarrier.NoOpportunity; return result; }
            var s = house.Situation ?? new HouseSituation();
            HouseNeed worst = HouseNeed.Sustenance; float low = s.Sustenance;
            if (s.LivingSpace < low) { low = s.LivingSpace; worst = HouseNeed.LivingSpace; }
            if (s.Safety < low) { low = s.Safety; worst = HouseNeed.Safety; }
            if (s.Influence < low) { low = s.Influence; worst = HouseNeed.Influence; }
            result.Need = low < 35f;
            result.Opportunity = ctx.Opportunities > 0;
            float values = house.CultureOf(Dim.Martial) + house.CultureOf(Dim.Status) - Math.Max(0f, house.CultureOf(Dim.Faith)) - Math.Max(0f, house.CultureOf(Dim.Hearth)) * .5f;
            result.Values = values >= 8f;
            var precedent = Incidents.Where(i => i.ActorFamily == family && CrimeKinds.Contains(i.Kind) && (i.Context & IncidentContext.Accident) == 0 && (i.Context & IncidentContext.MentalBreak) == 0 && now - i.Tick < 60 * ActivityTuning.TicksPerDay).ToList();
            result.Precedents = precedent.Count;
            result.WillingMembers = WillingToJoin(family, precedent).Count;
            if (!result.Need) result.Barriers |= CrimeBarrier.NoNeed;
            if (!result.Opportunity) result.Barriers |= CrimeBarrier.NoOpportunity;
            if (!result.Values) result.Barriers |= CrimeBarrier.ValuesAgainst;
            if (result.WillingMembers < 2) result.Barriers |= CrimeBarrier.TooFewWilling;
            if (result.Precedents == 0) result.Barriers |= CrimeBarrier.NoPrecedent;
            result.Alternatives = AlternativesFor(worst);
            return result;
        }

        private List<int> WillingToJoin(int family, List<FamilyIncident> precedent)
        {
            var actors = precedent.Select(i => i.ActorId).Distinct().ToList();
            var members = MembersOf(family).Where(p => p.InColony && !p.Juvenile).ToList();
            var willing = new HashSet<int>(actors.Where(a => members.Any(m => m.Id == a)));
            foreach (var m in members.Take(24))
            {
                if (willing.Contains(m.Id)) continue;
                var agency = AgencyOf(m.Id);
                bool aggrieved = agency != null && agency.Excluded >= 2 && agency.Included < 3;
                bool loyal = actors.Any(a => (GetBond(m.Id, a)?.Attachment ?? 0f) >= 45f);
                if (aggrieved && loyal) willing.Add(m.Id);
            }
            return willing.OrderBy(i => i).ToList();
        }

        /// <summary>Opens a criminal direction only when the assessment is eligible. Reform and abandonment are always available afterwards.</summary>
        public bool AdoptCrimeDirection(int family, CrimeDirection direction, CrimeContext ctx, int now)
        {
            var house = GetLineage(family);
            if (house == null || direction == CrimeDirection.None || house.Crime.Status == CrimeStatus.Active || !AssessCrimeDirection(family, ctx, now).Eligible) return false;
            var crime = house.Crime;
            crime.Direction = direction; crime.Status = CrimeStatus.Active; crime.StartedTick = now; crime.EndedTick = 0; crime.ReformProgress = 0f;
            return true;
        }

        // ---- a job, step by step ----

        public Heist PlanHeist(int family, int organizer, int targetFamily, int targetMap, int targetToken, CrimeContext ctx, int now)
        {
            var house = GetLineage(family); var person = GetPerson(organizer);
            if (house == null || house.Crime.Status != CrimeStatus.Active || person?.InColony != true || person.Juvenile || !BelongsToFamily(person, family)) return null;
            if (targetFamily == family || !IsActiveHouse(targetFamily) || HeadOf(targetFamily) == 0 || targetToken == 0) return null;
            if (now - house.Crime.LastPlanTick < HeistPlanningInterval || Heists.Any(h => h.Active && h.FamilyId == family)) return null;
            var heist = new Heist { Id = NextHeistId++, FamilyId = family, OrganizerId = organizer, TargetFamilyId = targetFamily, TargetMapId = targetMap, TargetToken = targetToken, PlannedTick = now, StateTick = now, Direction = house.Crime.Direction };
            if (ctx?.TargetValid != null && !ctx.TargetValid(heist)) return null;
            heist.Participants.Add(new HeistParticipant { PersonId = organizer, Stance = CrimeStance.Willing });
            if (Heists.Count >= 64) { var old = Heists.FirstOrDefault(h => !h.Active); if (old == null) return null; Heists.Remove(old); }
            Heists.Add(heist); house.Crime.LastPlanTick = now;
            LearnCrime(heist.Id, organizer, CrimeKnowledgeLevel.Fact);
            return heist;
        }

        /// <summary>A person asked to take part may join, refuse, or expose it. Refusing is a real option and costs only what the relationship itself costs.</summary>
        public bool RecruitForHeist(int heistId, int person, RecruitAnswer answer, int now)
        {
            var heist = Heists.FirstOrDefault(h => h.Id == heistId); var p = GetPerson(person);
            if (heist == null || heist.State != HeistState.Planning || p?.InColony != true || p.Juvenile || !BelongsToFamily(p, heist.FamilyId) || heist.Participants.Any(x => x.PersonId == person)) return false;
            if (heist.Participants.Count >= 12) return false;
            LearnCrime(heistId, person, CrimeKnowledgeLevel.Fact);
            switch (answer)
            {
                case RecruitAnswer.Join: heist.Participants.Add(new HeistParticipant { PersonId = person, Stance = CrimeStance.Willing }); break;
                case RecruitAnswer.Refuse:
                    heist.Participants.Add(new HeistParticipant { PersonId = person, Stance = CrimeStance.Refused });
                    ChangeBond(person, heist.OrganizerId, new BondChange(-.5f, -1f, 1f, 0f), now); break;
                default:
                    heist.Participants.Add(new HeistParticipant { PersonId = person, Stance = CrimeStance.Exposed });
                    ExposeHeist(heist, person, now); break;
            }
            return true;
        }

        private void ExposeHeist(Heist heist, int whistleblower, int now)
        {
            heist.State = HeistState.Exposed; heist.StateTick = now;
            var house = GetLineage(heist.FamilyId);
            if (house != null) house.Crime.Heat = Math.Min(100f, house.Crime.Heat + 25f);
            int head = HeadOf(heist.TargetFamilyId);
            if (head != 0) LearnCrime(heist.Id, head, CrimeKnowledgeLevel.Report);
            LearnCrime(heist.Id, whistleblower, CrimeKnowledgeLevel.Fact);
        }

        public bool ReadyHeist(int heistId, CrimeContext ctx, int now)
        {
            var heist = Heists.FirstOrDefault(h => h.Id == heistId);
            if (heist == null || heist.State != HeistState.Planning || heist.WillingCount < 2) return false;
            if (ctx?.TargetValid != null && !ctx.TargetValid(heist)) { AbortHeist(heistId, now); return false; }
            int refused = heist.Participants.Count(p => p.Stance == CrimeStance.Refused);
            heist.Risk = Math.Max(5f, Math.Min(95f, 45f + refused * 8f - heist.WillingCount * 6f + (GetLineage(heist.TargetFamilyId)?.Situation?.Safety ?? 50f) * .2f));
            heist.State = HeistState.Ready; heist.StateTick = now;
            return true;
        }

        public bool BeginHeist(int heistId, CrimeContext ctx, int now)
        {
            var heist = Heists.FirstOrDefault(h => h.Id == heistId);
            if (heist == null || heist.State != HeistState.Ready) return false;
            if (ctx?.TargetValid != null && !ctx.TargetValid(heist)) { InterruptHeist(heistId, now); return false; }
            int ready = heist.Participants.Count(p => p.Stance == CrimeStance.Willing && GetPerson(p.PersonId)?.InColony == true && (ctx?.Free == null || ctx.Free(p.PersonId)));
            if (ready < 2) { InterruptHeist(heistId, now); return false; }
            heist.State = HeistState.Executing; heist.StateTick = now;
            return true;
        }

        public bool InterruptHeist(int heistId, int now)
        {
            var heist = Heists.FirstOrDefault(h => h.Id == heistId);
            if (heist == null || !heist.Active) return false;
            heist.State = HeistState.Interrupted; heist.StateTick = now;
            return true;
        }

        public bool AbortHeist(int heistId, int now)
        {
            var heist = Heists.FirstOrDefault(h => h.Id == heistId);
            if (heist == null || !heist.Active) return false;
            heist.State = HeistState.Aborted; heist.StateTick = now;
            return true;
        }

        /// <summary>
        /// Records the outcome of a job that was actually carried out. Goods count only with the one-time token the game issues when the items
        /// moved; with no victim to harm there is no incident; nobody learns who did it unless they were there.
        /// </summary>
        public bool ResolveHeist(int heistId, int goodsMoved, int evidenceToken, bool caught, IEnumerable<int> witnesses, int now, int existingIncidentId = 0)
        {
            var heist = Heists.FirstOrDefault(h => h.Id == heistId);
            if (heist == null || heist.State != HeistState.Executing) return false;
            if (goodsMoved > 0 && (evidenceToken == 0 || Heists.Any(h => h.Id != heistId && h.EvidenceToken == evidenceToken))) return false;
            int victim = HeadOf(heist.TargetFamilyId);
            if (victim == 0) return false;
            var present = heist.Participants.Where(p => p.Stance == CrimeStance.Willing).Select(p => p.PersonId).ToList();
            var seen = (witnesses ?? Enumerable.Empty<int>()).Concat(caught ? new[] { victim } : new int[0]).Distinct().ToList();
            var kind = heist.Direction == CrimeDirection.Intimidation || heist.Direction == CrimeDirection.Protection ? IncidentKind.Intimidation : IncidentKind.Theft;
            // An incident is recorded for the actor; its witnesses are the only people who learn who did it.
            // When the supply system already recorded the theft that really happened, that record is used rather than writing a second one.
            var incident = existingIncidentId != 0 ? Incidents.FirstOrDefault(i => i.Id == existingIncidentId) :
                RecordIncident(kind, heist.OrganizerId, victim, heist.TargetMapId, now, Math.Min(100f, 25f + goodsMoved * .5f), seen, IncidentContext.Planned, Math.Max(0, goodsMoved));
            heist.IncidentId = incident?.Id ?? 0; heist.GoodsMoved = Math.Max(0, goodsMoved); heist.EvidenceToken = goodsMoved > 0 ? evidenceToken : 0;
            heist.State = caught ? HeistState.Exposed : HeistState.Completed; heist.StateTick = now;
            foreach (int w in seen) LearnCrime(heistId, w, CrimeKnowledgeLevel.Fact);
            var house = GetLineage(heist.FamilyId);
            if (house != null) house.Crime.Heat = Math.Min(100f, house.Crime.Heat + (caught ? 25f : 6f));
            // Doing it together builds trust inside the group.
            for (int i = 0; i < present.Count; i++) for (int j = 0; j < present.Count; j++) if (i != j) ChangeBond(present[i], present[j], new BondChange(1.5f, 1.5f, -.5f, .5f), now);
            return true;
        }

        public bool CleanupHeist(int heistId, int now)
        {
            var heist = Heists.FirstOrDefault(h => h.Id == heistId);
            if (heist == null || heist.Active || heist.State == HeistState.CleanedUp) return false;
            heist.State = HeistState.CleanedUp; heist.StateTick = now;
            return true;
        }

        // ---- who knows what ----

        public CrimeKnowledgeLevel CrimeKnowledgeOf(int observer, int heistId)
            => CrimeKnowledgeRecords.FirstOrDefault(k => k.HeistId == heistId && k.PersonId == observer)?.Level ?? CrimeKnowledgeLevel.None;

        public void LearnCrime(int heistId, int person, CrimeKnowledgeLevel level)
        {
            if (level == CrimeKnowledgeLevel.None || GetPerson(person) == null) return;
            var record = CrimeKnowledgeRecords.FirstOrDefault(k => k.HeistId == heistId && k.PersonId == person);
            if (record == null)
            {
                if (CrimeKnowledgeRecords.Count >= 512) CrimeKnowledgeRecords.RemoveAt(0);
                CrimeKnowledgeRecords.Add(new CrimeKnowledge { HeistId = heistId, PersonId = person, Level = level });
            }
            else if (level > record.Level) record.Level = level;
        }

        /// <summary>Someone who knows the facts tells another. The listener gets a report, never the facts, and a report is not proof.</summary>
        public bool ReportCrime(int heistId, int from, int to)
        {
            if (CrimeKnowledgeOf(from, heistId) != CrimeKnowledgeLevel.Fact || from == to) return false;
            LearnCrime(heistId, to, CrimeKnowledgeLevel.Report);
            return true;
        }

        public void SuspectCrime(int heistId, int person) => LearnCrime(heistId, person, CrimeKnowledgeLevel.Suspicion);

        /// <summary>Only a witness may name the perpetrator as a fact. Suspicion and hearsay never become a conviction.</summary>
        public bool CanAccuse(int observer, int heistId) => CrimeKnowledgeOf(observer, heistId) == CrimeKnowledgeLevel.Fact;
        public bool PerpetratorKnownTo(int observer, int heistId) => CanAccuse(observer, heistId);

        // ---- protection arrangements ----

        public ProtectionArrangement ProposeProtection(int protector, int protectedHouse, int tribute, int now)
        {
            if (protector == protectedHouse || !IsActiveHouse(protector) || !IsActiveHouse(protectedHouse) || tribute < 0 || tribute > 500) return null;
            if (ProtectionArrangements.Any(a => a.EndedTick == 0 && a.ProtectorFamily == protector && a.ProtectedFamily == protectedHouse)) return null;
            if (ProtectionArrangements.Count >= 64) { var old = ProtectionArrangements.FirstOrDefault(a => a.EndedTick != 0); if (old == null) return null; ProtectionArrangements.Remove(old); }
            var arrangement = new ProtectionArrangement { Id = NextProtectionId++, ProtectorFamily = protector, ProtectedFamily = protectedHouse, TributeAmount = tribute };
            ProtectionArrangements.Add(arrangement);
            return arrangement;
        }

        /// <summary>Only the protected house's head can agree. An unagreed arrangement collects nothing, whatever anyone claims.</summary>
        public bool AgreeProtection(int arrangementId, int head, int now)
        {
            var a = ProtectionArrangements.FirstOrDefault(x => x.Id == arrangementId);
            if (a == null || a.AgreedTick != 0 || a.EndedTick != 0 || HeadOf(a.ProtectedFamily) != head) return false;
            a.AgreedTick = Math.Max(1, now); a.AgreedBy = head;
            return true;
        }

        public bool RecordTribute(int arrangementId, int amount, int evidenceToken, int now)
        {
            var a = ProtectionArrangements.FirstOrDefault(x => x.Id == arrangementId);
            if (a == null || !a.Agreed || amount <= 0 || evidenceToken == 0 || a.LastPaymentToken == evidenceToken) return false;
            a.PaymentsRecorded += amount; a.Payments++; a.LastPaymentToken = evidenceToken;
            return true;
        }

        public bool EndProtection(int arrangementId, int now)
        {
            var a = ProtectionArrangements.FirstOrDefault(x => x.Id == arrangementId);
            if (a == null || a.EndedTick != 0) return false;
            a.EndedTick = Math.Max(1, now);
            return true;
        }

        // ---- reform ----

        public bool BeginReform(int family, int now)
        {
            var house = GetLineage(family);
            if (house == null || house.Crime.Status != CrimeStatus.Active) return false;
            house.Crime.Status = CrimeStatus.Reforming; house.Crime.ReformProgress = Math.Max(house.Crime.ReformProgress, 10f); house.Crime.StartedTick = now;
            foreach (var h in Heists.Where(h => h.FamilyId == family && h.Active)) AbortHeist(h.Id, now);
            return true;
        }

        /// <summary>A real act of making amends counts toward reform; promises do not.</summary>
        public bool RecordAmends(int family, int actor, int victim, int amount, int now)
        {
            var house = GetLineage(family);
            if (house == null || house.Crime.Status != CrimeStatus.Reforming || amount <= 0 || FamilyOf(actor) != family || FamilyOf(victim) == family) return false;
            if (RecordIncident(IncidentKind.Restitution, actor, victim, -1, now, Math.Min(60f, 8f + amount * .4f), new[] { actor, victim }, IncidentContext.None, amount) == null) return false;
            house.Crime.ReformProgress = Math.Min(100f, house.Crime.ReformProgress + Math.Min(30f, 6f + amount * .5f));
            if (house.Crime.ReformProgress >= 100f) { house.Crime.Status = CrimeStatus.Reformed; house.Crime.Direction = CrimeDirection.None; house.Crime.EndedTick = now; house.Crime.Heat = Math.Max(0f, house.Crime.Heat - 40f); }
            return true;
        }

        /// <summary>Counts an act of restitution already recorded by the supply system toward reform.</summary>
        public bool NoteAmends(int family, int incidentId, int now)
        {
            var house = GetLineage(family);
            var incident = Incidents.FirstOrDefault(i => i.Id == incidentId);
            if (house == null || house.Crime.Status != CrimeStatus.Reforming || incident == null || incident.Kind != IncidentKind.Restitution || incident.ActorFamily != family || incident.Tick < house.Crime.StartedTick || incident.Id <= house.Crime.LastAmendsIncident) return false;
            house.Crime.LastAmendsIncident = incident.Id;
            house.Crime.ReformProgress = Math.Min(100f, house.Crime.ReformProgress + Math.Min(30f, 6f + incident.Amount * .5f));
            if (house.Crime.ReformProgress >= 100f) { house.Crime.Status = CrimeStatus.Reformed; house.Crime.Direction = CrimeDirection.None; house.Crime.EndedTick = now; house.Crime.Heat = Math.Max(0f, house.Crime.Heat - 40f); }
            return true;
        }

        /// <summary>Abandoning the direction needs no ceremony or permission.</summary>
        public bool AbandonCrimeDirection(int family, int now)
        {
            var house = GetLineage(family);
            if (house == null || house.Crime.Status == CrimeStatus.None || house.Crime.Status == CrimeStatus.Reformed) return false;
            house.Crime.Status = CrimeStatus.None; house.Crime.Direction = CrimeDirection.None; house.Crime.EndedTick = now;
            foreach (var h in Heists.Where(h => h.FamilyId == family && h.Active)) AbortHeist(h.Id, now);
            return true;
        }

        public bool RivalResponseLikely(int family) => (GetLineage(family)?.Crime?.Heat ?? 0f) >= 40f;

        /// <summary>Slow decay; a house that stops stops being hunted. Cheap: only touches houses with heat.</summary>
        public void MaintainCrime(int now)
        {
            foreach (var house in Lineages.Where(h => h.Crime != null && h.Crime.Heat > 0f)) house.Crime.Heat = Math.Max(0f, house.Crime.Heat - .6f);
            foreach (var heist in Heists.Where(h => h.Active && now - h.StateTick > 5 * ActivityTuning.TicksPerDay)) AbortHeist(heist.Id, now);
        }
    }
}
