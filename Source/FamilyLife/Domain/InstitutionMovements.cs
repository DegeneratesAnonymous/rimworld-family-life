using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum InstitutionMovementState : byte { Organizing, Adopted, Rejected, Withdrawn }

    /// <summary>A proposal carried from house to house. A constitution changes only after enough heads have answered it.</summary>
    public sealed partial class InstitutionMovement
    {
        public int Id, ProposerId, StartedTick, DeadlineTick, EndedTick;
        public InstitutionKind From, Target;
        public InstitutionMovementState State;
        public List<int> SupportHouseIds = new List<int>();
        public List<int> OpposeHouseIds = new List<int>();
        public List<int> ContactedHouseIds = new List<int>();
        public bool Open => State == InstitutionMovementState.Organizing;
    }

    public sealed partial class FamilyRegistry
    {
        public const int InstitutionMovementCooldown = 30 * ActivityTuning.TicksPerDay;
        public const int InstitutionMovementWindow = 3 * ActivityTuning.TicksPerDay;
        public List<InstitutionMovement> InstitutionMovements = new List<InstitutionMovement>();
        public int NextInstitutionMovementId = 1, LastInstitutionMovementTick = -1000000;

        public InstitutionMovement ActiveInstitutionMovement => InstitutionMovements.FirstOrDefault(x => x.Open);

        private List<Lineage> PoliticalHouses() => Lineages.Where(h => h.AbsorbedIntoId == 0 && HeadOf(h.Id) != 0 &&
            GetPerson(HeadOf(h.Id))?.InColony == true && !GetPerson(HeadOf(h.Id)).Juvenile).OrderBy(h => h.Id).ToList();

        public InstitutionMovement BeginInstitutionMovement(InstitutionKind target, int proposer, int now)
        {
            int family = FamilyOf(proposer); var person = GetPerson(proposer);
            if (target == Institution.Kind || ActiveInstitutionMovement != null || person?.InColony != true || person.Juvenile || HeadOf(family) != proposer ||
                now - LastInstitutionMovementTick < InstitutionMovementCooldown) return null;
            if (InstitutionMovements.Count >= 12) InstitutionMovements.RemoveAt(0);
            var movement = new InstitutionMovement
            {
                Id = NextInstitutionMovementId++, ProposerId = proposer, From = Institution.Kind, Target = target,
                StartedTick = now, DeadlineTick = now + InstitutionMovementWindow
            };
            movement.SupportHouseIds.Add(family); movement.ContactedHouseIds.Add(family);
            InstitutionMovements.Add(movement); LastInstitutionMovementTick = now;
            return movement;
        }

        /// <summary>Starts only when the colony itself has outgrown or lost confidence in its present arrangement.</summary>
        public InstitutionMovement ConsiderInstitutionMovement(int now, Func<int, bool> available = null)
        {
            if (ActiveInstitutionMovement != null || now - LastInstitutionMovementTick < InstitutionMovementCooldown || now - Institution.SinceTick < InstitutionMovementCooldown) return null;
            var houses = PoliticalHouses(); int adults = Persons.Count(p => p.InColony && !p.Juvenile);
            InstitutionKind? target = null;
            if (Institution.Kind == InstitutionKind.Consensus && houses.Count >= 3 && adults >= 6) target = InstitutionKind.Council;
            else if (Institution.Kind == InstitutionKind.Council && houses.Count >= 3 && adults >= 10) target = InstitutionKind.Election;
            else if (Institution.Kind == InstitutionKind.Authoritarian && houses.Count >= 3)
            {
                var leader = GetPerson(Institution.LeaderId);
                float tension = Persons.Where(p => p.InColony && !p.Juvenile && p.Id != Institution.LeaderId)
                    .Select(p => GetBond(p.Id, Institution.LeaderId)?.Tension ?? 0f).DefaultIfEmpty().Average();
                if (leader?.InColony != true || tension >= 35f) target = InstitutionKind.Council;
            }
            if (target == null) return null;
            int proposer = houses.Where(h => available == null || available(HeadOf(h.Id))).OrderByDescending(h => h.Standing).ThenBy(h => h.Id).Select(h => HeadOf(h.Id)).FirstOrDefault();
            if (proposer == 0) return null;
            return BeginInstitutionMovement(target.Value, proposer, now);
        }

        public bool AnswerInstitutionMovement(int movementId, int family, int head, bool support, int now)
        {
            var movement = InstitutionMovements.FirstOrDefault(x => x.Id == movementId && x.Open);
            if (movement == null || now > movement.DeadlineTick || HeadOf(family) != head || movement.ContactedHouseIds.Contains(family)) return false;
            movement.ContactedHouseIds.Add(family);
            (support ? movement.SupportHouseIds : movement.OpposeHouseIds).Add(family);
            ResolveInstitutionMovement(movement, now);
            return true;
        }

        public bool WouldSupportInstitutionMovement(int movementId, int family)
        {
            var movement = InstitutionMovements.FirstOrDefault(x => x.Id == movementId && x.Open); int head = HeadOf(family);
            if (movement == null || head == 0) return false;
            int houses = PoliticalHouses().Count, adults = Persons.Count(p => p.InColony && !p.Juvenile);
            float score = movement.Target == InstitutionKind.Council ? (houses >= 3 ? 22f : -20f)
                : movement.Target == InstitutionKind.Election ? (adults >= 10 && houses >= 3 ? 20f : -25f)
                : movement.Target == InstitutionKind.Consensus ? (houses <= 2 ? 20f : -15f) : -30f;
            var proposerBond = GetBond(head, movement.ProposerId); score += (proposerBond?.Trust ?? 0f) * .18f - (proposerBond?.Tension ?? 0f) * .15f;
            if (Institution.LeaderId != 0)
            {
                var leaderBond = GetBond(head, Institution.LeaderId);
                score += (leaderBond?.Tension ?? 0f) * .12f - (leaderBond?.Attachment ?? 0f) * .10f;
            }
            var situation = GetLineage(family)?.Situation;
            if (movement.Target == InstitutionKind.Council && situation != null && Math.Min(situation.Sustenance, situation.Safety) < 40f) score += 6f;
            return score >= 0f;
        }

        public void AdvanceInstitutionMovements(int now)
        {
            var movement = ActiveInstitutionMovement;
            if (movement == null) return;
            if (Institution.Kind != movement.From) { movement.State = InstitutionMovementState.Withdrawn; movement.EndedTick = now; return; }
            ResolveInstitutionMovement(movement, now);
        }

        private void ResolveInstitutionMovement(InstitutionMovement movement, int now)
        {
            var houses = PoliticalHouses(); int needed = houses.Count / 2 + 1;
            movement.SupportHouseIds.RemoveAll(id => houses.All(h => h.Id != id));
            movement.OpposeHouseIds.RemoveAll(id => houses.All(h => h.Id != id));
            if (movement.SupportHouseIds.Count >= needed)
            {
                if (!SetInstitution(movement.Target, movement.Target == InstitutionKind.Authoritarian ? movement.ProposerId : 0, now))
                { movement.State = InstitutionMovementState.Rejected; movement.EndedTick = now; return; }
                movement.State = InstitutionMovementState.Adopted; movement.EndedTick = now;
                if (movement.Target == InstitutionKind.Election)
                {
                    var field = Persons.Where(p => p.InColony && !p.Juvenile).Select(p => AssessLegitimacy(p.Id, FamilyOf(p.Id), 0))
                        .Where(a => a.Score >= 30f && !a.Reluctant).OrderByDescending(a => a.Score).ThenBy(a => a.PersonId).Take(4).Select(a => a.PersonId).ToList();
                    CallElection(field, now);
                }
                return;
            }
            int unanswered = houses.Count - movement.SupportHouseIds.Count - movement.OpposeHouseIds.Count;
            if (now >= movement.DeadlineTick || movement.SupportHouseIds.Count + unanswered < needed)
            { movement.State = InstitutionMovementState.Rejected; movement.EndedTick = now; }
        }
    }
}
