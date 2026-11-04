using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public sealed partial class FamilyRegistry
    {
        public List<Bond> Bonds = new List<Bond>();
        private readonly Dictionary<long, Bond> bondByPair = new Dictionary<long, Bond>();

        private static long Pair(int from, int to) => ((long)from << 32) | (uint)to;

        private void RebuildBondIndex()
        {
            Bonds = Bonds ?? new List<Bond>();
            Bonds.RemoveAll(b => b == null);
            bondByPair.Clear();
            var unique = new List<Bond>(Bonds.Count);
            foreach (var b in Bonds)
            {
                if (bondByPair.ContainsKey(Pair(b.From, b.To)) || b.From == b.To) continue;
                b.Attachment = BondTuning.Clamp(b.Attachment);
                b.Trust = BondTuning.Clamp(b.Trust);
                b.Tension = BondTuning.Clamp(b.Tension);
                b.Familiarity = BondTuning.Clamp(b.Familiarity);
                bondByPair[Pair(b.From, b.To)] = b;
                unique.Add(b);
            }
            Bonds = unique;
        }

        public Bond GetBond(int from, int to)
        {
            EnsureIndexes();
            return bondByPair.TryGetValue(Pair(from, to), out var b) ? b : null;
        }

        public List<Bond> BondsFrom(int from)
        {
            EnsureIndexes();
            return Bonds.Where(b => b.From == from).OrderByDescending(b => b.Attachment).ThenBy(b => b.To).ToList();
        }

        // ------------------------------------------------------------------ who is connected to whom

        /// <summary>How the holder is connected to the other person, from the records. Housemate is the weakest tie and Ex the last resort.</summary>
        public BondKind? KindOf(PersonRecord holder, PersonRecord other)
        {
            if (holder.Id == other.Id) return null;
            if (other.ParentIds.Contains(holder.Id)) return BondKind.Parent;
            if (holder.ParentIds.Contains(other.Id)) return BondKind.Child;
            if (holder.HasPartner(other.Id)) return BondKind.Partner;
            if (holder.ParentIds.Count > 0 && holder.ParentIds.Intersect(other.ParentIds).Any()) return BondKind.Sibling;
            if (holder.ExIds.Contains(other.Id)) return BondKind.Ex;
            if (holder.HouseholdId != 0 && holder.HouseholdId == other.HouseholdId) return BondKind.Housemate;
            return null;
        }

        /// <summary>The directed pairs that deserve a bond: every colonist toward their kin, partners, exes and housemates.</summary>
        public List<KeyValuePair<int, int>> RelevantPairs()
        {
            EnsureIndexes();
            var pairs = new List<KeyValuePair<int, int>>();
            var byHolder = Bonds.GroupBy(b => b.From).ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.Attachment).ThenBy(b => b.To).ToList());
            foreach (var holder in Persons.Where(p => p.InColony).OrderBy(p => p.Id))
            {
                var candidates = new SortedSet<int>();
                foreach (var id in holder.ParentIds) candidates.Add(id);
                foreach (var id in ChildrenOf(holder.Id)) candidates.Add(id);
                foreach (var id in SiblingsOf(holder.Id)) candidates.Add(id);
                foreach (var id in holder.PartnerIds()) candidates.Add(id);
                foreach (var id in holder.ExIds) candidates.Add(id);
                if (holder.HouseholdId != 0 && householdById.TryGetValue(holder.HouseholdId, out var home))
                    foreach (var id in home.ResidentIds) candidates.Add(id);
                candidates.Remove(holder.Id);

                var ranked = candidates
                    .Select(id => personById.TryGetValue(id, out var o) ? new { o, kind = KindOf(holder, o) } : null)
                    .Where(x => x != null && x.kind.HasValue)
                    .OrderBy(x => (int)x.kind.Value == 0 ? 9 : (x.kind.Value == BondKind.Ex ? 8 : (int)x.kind.Value))     // kin and partners before housemates and exes
                    .ThenBy(x => x.o.Id)
                    .Take(BondTuning.MaxBondsPerPerson);
                foreach (var x in ranked) pairs.Add(new KeyValuePair<int, int>(holder.Id, x.o.Id));

                // People the events have brought together who are not family or housemates keep a bond too, so it follows the game's opinion.
                int extra = 0;
                if (!byHolder.TryGetValue(holder.Id, out var existingBonds)) continue;
                foreach (var bond in existingBonds)
                {
                    if (extra >= BondTuning.MaxAcquaintances) break;
                    if (!personById.TryGetValue(bond.To, out var o) || !o.InColony || KindOf(holder, o).HasValue) continue;
                    pairs.Add(new KeyValuePair<int, int>(holder.Id, o.Id));
                    extra++;
                }
            }
            return pairs;
        }

        // ------------------------------------------------------------------ updating bonds

        /// <summary>
        /// Moves every relevant bond forward to <paramref name="now"/> and applies what the events mean for relationships.
        /// <paramref name="opinionOf"/> returns the game's own opinion of one person about another, or null when unknown; it is
        /// only asked about pairs that are both in the colony. Returns the griefs caused by deaths in <paramref name="events"/>.
        /// </summary>
        public List<Grief> UpdateBonds(Func<int, int, int?> opinionOf, int now, IEnumerable<FamilyEvent> events, Func<int, float[]> profileOf = null)
        {
            EnsureIndexes();
            var griefs = new List<Grief>();

            foreach (var pair in RelevantPairs())
            {
                var holder = personById[pair.Key];
                var other = personById[pair.Value];
                var kind = KindOf(holder, other) ?? BondKind.Acquaintance;
                var bond = GetBond(holder.Id, other.Id) ?? CreateBond(holder.Id, other.Id, kind, now);
                if (other.Presence == Presence.Dead) { bond.LastTick = now; continue; }       // a memory: it no longer changes by itself

                float hours = Math.Min(BondTuning.MaxHoursPerUpdate, Math.Max(0, now - bond.LastTick) / (float)BondTuning.TicksPerHour);
                bond.LastTick = now;
                if (hours <= 0f) continue;
                bond.QualityRecent *= (float)Math.Pow(ConflictTuning.QualityDecayPerHour, hours);

                bool together = other.InColony;
                bool sameHome = holder.HouseholdId != 0 && holder.HouseholdId == other.HouseholdId;
                int? opinion = together ? SafeOpinion(opinionOf, holder.Id, other.Id) : null;

                if (sameHome) bond.Familiarity += BondTuning.FamiliarityPerHourHousehold * hours;
                else if (together) bond.Familiarity += BondTuning.FamiliarityPerHourTogether * hours;
                else bond.Familiarity -= BondTuning.FamiliarityLossPerHourApart * hours;
                bond.Familiarity = BondTuning.Clamp(bond.Familiarity);

                float opinionValue = opinion ?? 0f;
                bond.LastOpinion = opinionValue;
                float attachmentTarget = BondTuning.Clamp(BondTuning.AttachmentTarget(kind, bond.Familiarity, opinionValue));
                float trustTarget = BondTuning.Clamp(25f + 0.4f * bond.Familiarity + opinionValue * 0.3f - 0.5f * bond.Tension);
                float tensionTarget = opinion.HasValue && opinion.Value < 0 ? BondTuning.Clamp(-opinion.Value * BondTuning.EmbitteredTension) : 0f;
                if (together && profileOf != null) tensionTarget = BondTuning.Clamp(tensionTarget + Friction(holder.Id, profileOf(holder.Id), profileOf(other.Id)));

                bond.Attachment = Approach(bond.Attachment, attachmentTarget, BondTuning.AttachmentRate, hours);
                bond.Trust = Approach(bond.Trust, trustTarget, BondTuning.TrustRate, hours);
                bond.Tension = Approach(bond.Tension, tensionTarget, BondTuning.TensionRate, hours);
            }

            foreach (var e in events ?? Enumerable.Empty<FamilyEvent>())
                ApplyEventToBonds(e, now, griefs);

            PruneBonds();
            return griefs;
        }

        private static int? SafeOpinion(Func<int, int, int?> opinionOf, int from, int to)
        {
            if (opinionOf == null) return null;
            var value = opinionOf(from, to);
            return value.HasValue ? Math.Max(-100, Math.Min(100, value.Value)) : (int?)null;
        }

        private static float Approach(float current, float target, float ratePerHour, float hours)
        {
            float share = 1f - (float)Math.Pow(1f - ratePerHour, hours);
            return BondTuning.Clamp(current + (target - current) * share);
        }

        private Bond CreateBond(int from, int to, BondKind kind, int now)
        {
            float baseAttachment = BondTuning.BaseAttachment(kind);
            var bond = new Bond { From = from, To = to, Attachment = baseAttachment, Trust = 25f, Tension = 0f, Familiarity = 0f, LastTick = now };
            Bonds.Add(bond);
            bondByPair[Pair(from, to)] = bond;
            return bond;
        }

        private Bond EnsureBond(int from, int to, int now)
        {
            var existing = GetBond(from, to);
            if (existing != null) return existing;
            if (!personById.TryGetValue(from, out var holder) || !personById.TryGetValue(to, out var other)) return null;
            return CreateBond(from, to, KindOf(holder, other) ?? BondKind.Acquaintance, now);
        }

        private void ApplyEventToBonds(FamilyEvent e, int now, List<Grief> griefs)
        {
            switch (e.Kind)
            {
                case FamilyEventKind.Birth:
                    if (!personById.TryGetValue(e.A, out var child)) return;
                    foreach (var parentId in child.ParentIds)
                    {
                        Bump(EnsureBond(parentId, child.Id, now), BondTuning.BirthBump, 0f, 0f);
                        NoteBond(parentId, child.Id, BondCause.Birth, BondTuning.BirthBump, now);
                        NoteBond(child.Id, parentId, BondCause.Birth, BondTuning.BirthBump * 0.5f, now);
                        Bump(EnsureBond(child.Id, parentId, now), BondTuning.BirthBump * 0.5f, BondTuning.BirthBump * 0.5f, 0f);
                    }
                    break;

                case FamilyEventKind.PartnershipFormed:
                    Bump(EnsureBond(e.A, e.B, now), BondTuning.PartnerBump, 0f, 0f);
                    Bump(EnsureBond(e.B, e.A, now), BondTuning.PartnerBump, 0f, 0f);
                    NoteBond(e.A, e.B, BondCause.Partnered, BondTuning.PartnerBump, now);
                    NoteBond(e.B, e.A, BondCause.Partnered, BondTuning.PartnerBump, now);
                    break;

                case FamilyEventKind.PartnershipEnded:
                    Bump(EnsureBond(e.A, e.B, now), -BondTuning.BreakupAttachmentLoss, -10f, BondTuning.BreakupTension);
                    Bump(EnsureBond(e.B, e.A, now), -BondTuning.BreakupAttachmentLoss, -10f, BondTuning.BreakupTension);
                    NoteBond(e.A, e.B, BondCause.Breakup, -BondTuning.BreakupAttachmentLoss, now);
                    NoteBond(e.B, e.A, BondCause.Breakup, -BondTuning.BreakupAttachmentLoss, now);
                    break;

                case FamilyEventKind.Died:
                    foreach (var bond in Bonds.Where(b => b.To == e.A).ToList())
                    {
                        if (!personById.TryGetValue(bond.From, out var survivor) || !survivor.InColony) continue;
                        if (bond.Attachment < BondTuning.GriefMinAttachment) continue;
                        griefs.Add(new Grief { PersonId = survivor.Id, LostId = e.A, Attachment = bond.Attachment });
                    }
                    break;
            }
        }

        private static void Bump(Bond bond, float attachment, float trust, float tension)
        {
            if (bond == null) return;
            bond.Attachment = BondTuning.Clamp(bond.Attachment + attachment);
            bond.Trust = BondTuning.Clamp(bond.Trust + trust);
            bond.Tension = BondTuning.Clamp(bond.Tension + tension);
        }

        /// <summary>A dead person holds no feelings; bonds toward the dead are kept as history. Everything else is bounded per person.</summary>
        private void PruneBonds()
        {
            Bonds.RemoveAll(b =>
            {
                if (!personById.TryGetValue(b.From, out var holder) || !personById.TryGetValue(b.To, out _)) return true;
                return holder.Presence == Presence.Dead;
            });
            foreach (var group in Bonds.GroupBy(b => b.From).Where(g => g.Count() > BondTuning.MaxBondsPerPerson * 2))
            {
                // Far over the cap means stale housemate/ex ties from long ago: keep the strongest.
                foreach (var drop in group.OrderByDescending(b => b.Attachment).Skip(BondTuning.MaxBondsPerPerson * 2).ToList()) Bonds.Remove(drop);
            }
            bondByPair.Clear();
            foreach (var b in Bonds) bondByPair[Pair(b.From, b.To)] = b;
        }

        // ------------------------------------------------------------------ cohesion

        /// <summary>
        /// A summary of the bonds among a household's residents (0-100), or null when there is nobody to compare. It is derived,
        /// never stored, so it can never disagree with the bonds it summarizes.
        /// </summary>
        public float? HouseholdCohesion(Household household)
        {
            EnsureIndexes();
            return CohesionAmong(household.ResidentIds);
        }

        /// <summary>The same summary across every living member of a lineage who is in the colony.</summary>
        public float? LineageCohesion(int lineageId)
        {
            EnsureIndexes();
            var lineage = GetLineage(lineageId);
            var members = Persons
                .Where(p => p.InColony && (p.PrimaryLineageId == lineageId || (lineage != null && lineage.HasAffiliate(p.Id))))
                .Select(p => p.Id).ToList();
            return CohesionAmong(members);
        }

        private float? CohesionAmong(IList<int> ids)
        {
            if (ids.Count < 2) return null;
            var set = new HashSet<int>(ids);
            var inside = Bonds.Where(b => set.Contains(b.From) && set.Contains(b.To)).ToList();
            if (inside.Count == 0) return null;
            float attachment = inside.Average(b => b.Attachment);
            float trust = inside.Average(b => b.Trust);
            float tension = inside.Average(b => b.Tension);
            return BondTuning.Clamp(attachment * 0.6f + trust * 0.4f - tension * 0.5f);
        }

        /// <summary>Residents the rest of the household is only weakly attached to. Empty unless the household is clearly lopsided.</summary>
        public List<int> LeftOutIn(Household household)
        {
            EnsureIndexes();
            var result = new List<int>();
            if (household.ResidentIds.Count < 3) return result;
            var set = new HashSet<int>(household.ResidentIds);
            foreach (var id in household.ResidentIds)
            {
                var inbound = Bonds.Where(b => b.To == id && b.From != id && set.Contains(b.From)).ToList();
                if (inbound.Count < 2) continue;
                if (inbound.Average(b => b.Attachment) < 25f) result.Add(id);
            }
            return result;
        }
    }
}
