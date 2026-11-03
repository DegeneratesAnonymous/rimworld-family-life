using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>Why a bond moved. Kept so the player can be told, in words, what made two people closer or more distant.</summary>
    public enum BondCause : byte
    {
        Together = 0, Meal = 1, Story = 2, Teaching = 3, Date = 4, Care = 5, Gathering = 6,
        Peace = 7, Rift = 8, Breakup = 9, Birth = 10, Partnered = 11, Friction = 12
    }

    /// <summary>One recent cause of change in a bond: what it was, when it last happened, and how much it moved the bond in total.</summary>
    public sealed partial class BondReason
    {
        public BondCause Cause;
        public int Tick;
        public float Delta;      // positive made them closer, negative drove them apart
    }

    public sealed partial class Bond
    {
        public const int MaxReasons = 4;

        public List<BondReason> Reasons = new List<BondReason>();
        public byte Tier;                    // 0 = ordinary, 1 = close, 2 = distant; changes are announced
        public bool TierSeen;                // the first look only records the tier; it announces nothing
        public int LastMilestoneTick;

        /// <summary>Remembers a cause. The same cause again adds to its total; only the few most telling causes are kept.</summary>
        public void Note(BondCause cause, float delta, int now)
        {
            if (Math.Abs(delta) < 0.05f) return;
            Reasons = Reasons ?? new List<BondReason>();
            var existing = Reasons.FirstOrDefault(r => r.Cause == cause);
            if (existing != null)
            {
                // A reason fades as it gets old, so what is shown is what has been happening lately.
                float age = Math.Max(0, now - existing.Tick) / (float)ActivityTuning.TicksPerDay;
                existing.Delta = existing.Delta * (float)Math.Pow(0.9, age) + delta;
                existing.Tick = now;
            }
            else Reasons.Add(new BondReason { Cause = cause, Tick = now, Delta = delta });
            while (Reasons.Count > MaxReasons)
            {
                var weakest = Reasons.OrderBy(r => Math.Abs(r.Delta)).ThenBy(r => r.Tick).First();
                Reasons.Remove(weakest);
            }
        }

        /// <summary>The most telling recent cause pointing the given way, or null.</summary>
        public BondReason TopReason(bool positive)
        {
            return (Reasons ?? new List<BondReason>()).Where(r => positive ? r.Delta > 0.5f : r.Delta < -0.5f)
                .OrderByDescending(r => Math.Abs(r.Delta)).FirstOrDefault();
        }
    }

    public static class InsightTuning
    {
        public const float CloseAt = 70f;
        public const float CloseResetBelow = 58f;
        public const float DistantBelow = 28f;
        public const float DistantResetAbove = 40f;
        public const int MilestoneCooldownTicks = 5 * ActivityTuning.TicksPerDay;
        public const int MaxMilestonesPerUpdate = 2;
        public const float ReasonsMinSize = 0.5f;
    }

    /// <summary>How close two people are, from the bonds recorded between them.</summary>
    public sealed class PairCloseness
    {
        public float? Value;           // 0-100: the average attachment of the directions that are recorded; null when neither is
        public Bond FromFocus;         // how the first person feels about the second
        public Bond ToFocus;           // and the other way round
    }

    public sealed partial class FamilyRegistry
    {
        /// <summary>
        /// How close <paramref name="focusId"/> and <paramref name="otherId"/> are. Each direction is a separate bond; the closeness is
        /// their average where both exist, and the one that does exist where only one does (a bond is only kept from someone in the
        /// colony, so a relative who is away has only the colonists' side).
        /// </summary>
        public PairCloseness ClosenessBetween(int focusId, int otherId)
        {
            EnsureIndexes();
            var result = new PairCloseness();
            if (focusId == otherId) return result;
            result.FromFocus = GetBond(focusId, otherId);
            result.ToFocus = GetBond(otherId, focusId);
            var values = new List<float>();
            if (result.FromFocus != null) values.Add(result.FromFocus.Attachment);
            if (result.ToFocus != null) values.Add(result.ToFocus.Attachment);
            if (values.Count > 0) result.Value = values.Average();
            return result;
        }

        /// <summary>Adds a cause to a bond if it exists.</summary>
        public void NoteBond(int from, int to, BondCause cause, float delta, int now)
        {
            GetBond(from, to)?.Note(cause, delta, now);
        }

        /// <summary>
        /// Looks for bonds that have crossed into close or distant since the last look and reports a few of them. The first look at
        /// a bond only records where it stands. A bond must also have a recorded cause pointing that way, so a bond that merely
        /// started low is never announced as having cooled.
        /// </summary>
        public List<FamilyEvent> UpdateBondMilestones(int now)
        {
            EnsureIndexes();
            var events = new List<FamilyEvent>();
            foreach (var bond in Bonds.OrderBy(b => b.From).ThenBy(b => b.To))
            {
                if (!personById.TryGetValue(bond.From, out var holder) || !holder.InColony) continue;
                if (!personById.TryGetValue(bond.To, out var other) || !other.InColony) continue;

                if (!bond.TierSeen)
                {
                    bond.TierSeen = true;
                    bond.Tier = bond.Attachment >= InsightTuning.CloseAt ? (byte)1 : (bond.Attachment < InsightTuning.DistantBelow ? (byte)2 : (byte)0);
                    continue;
                }

                if (bond.Tier == 1 && bond.Attachment < InsightTuning.CloseResetBelow) bond.Tier = 0;
                if (bond.Tier == 2 && bond.Attachment > InsightTuning.DistantResetAbove) bond.Tier = 0;
                if (events.Count >= InsightTuning.MaxMilestonesPerUpdate) continue;
                if (bond.LastMilestoneTick > 0 && now - bond.LastMilestoneTick < InsightTuning.MilestoneCooldownTicks) continue;

                if (bond.Tier != 1 && bond.Attachment >= InsightTuning.CloseAt && bond.TopReason(true) != null)
                {
                    bond.Tier = 1;
                    bond.LastMilestoneTick = Math.Max(1, now);
                    events.Add(new FamilyEvent(FamilyEventKind.BondDeepened, now, bond.From, bond.To));
                }
                else if (bond.Tier != 2 && bond.Attachment < InsightTuning.DistantBelow && KindOf(holder, other).HasValue && bond.TopReason(false) != null)
                {
                    bond.Tier = 2;
                    bond.LastMilestoneTick = Math.Max(1, now);
                    events.Add(new FamilyEvent(FamilyEventKind.BondCooled, now, bond.From, bond.To));
                }
            }
            return events;
        }

        // ------------------------------------------------------------------ how culture shapes what a family does

        private float CultureNorm(int lineageId, Dim dim)
        {
            var lineage = GetLineage(lineageId);
            if (lineage == null) return 0f;
            return Math.Max(-1.5f, Math.Min(1.5f, lineage.CultureOf(dim) / CultureTuning.ProfileScale));
        }

        /// <summary>How likely a pair with something to teach is to spend their time on it. A learned, crafting or fighting family teaches often.</summary>
        public float TeachChance(int lineageId)
        {
            float lean = CultureNorm(lineageId, Dim.Scholarly) + 0.5f * CultureNorm(lineageId, Dim.Craft) + 0.5f * CultureNorm(lineageId, Dim.Martial);
            return Math.Max(0.1f, Math.Min(0.9f, 0.35f + 0.35f * lean));
        }

        /// <summary>How likely partners with a free evening are to go out together rather than just keep company. Devoted, home-loving families do.</summary>
        public float DateChance(int lineageId)
        {
            float lean = 0.6f * CultureNorm(lineageId, Dim.Devotion) + 0.4f * CultureNorm(lineageId, Dim.Hearth) + 0.3f * CultureNorm(lineageId, Dim.Revelry);
            return Math.Max(0.15f, Math.Min(0.9f, 0.45f + 0.3f * lean));
        }

        /// <summary>
        /// How much a person wants family time compared with the usual: a family that is devoted and rooted seeks it, a restless and
        /// independent one does not, and so does a person whose own character leans that way.
        /// </summary>
        public float FamilyTimeAppetite(int lineageId, float[] personalProfile)
        {
            float family = 1f + 0.25f * CultureNorm(lineageId, Dim.Devotion) + 0.15f * CultureNorm(lineageId, Dim.Hearth) + 0.15f * CultureNorm(lineageId, Dim.Rootedness);
            float personal = personalProfile == null ? 1f
                : 1f + 0.2f * personalProfile[(int)Dim.Devotion] + 0.1f * personalProfile[(int)Dim.Warmth] + 0.1f * personalProfile[(int)Dim.Rootedness];
            return Math.Max(0.5f, Math.Min(1.5f, family * personal));
        }

        /// <summary>
        /// Friction between two people whose characters pull apart, and between a person and a family whose values they do not share.
        /// It adds to the tension they settle at; it never creates a rift by itself.
        /// </summary>
        public float Friction(int holderId, float[] holderProfile, float[] otherProfile)
        {
            if (holderProfile == null || otherProfile == null) return 0f;
            var axes = new[] { Dim.Warmth, Dim.Steadiness, Dim.Rootedness, Dim.Devotion };
            float total = 0f;
            foreach (var d in axes)
            {
                float gap = Math.Abs(holderProfile[(int)d] - otherProfile[(int)d]);
                total += Math.Max(0f, gap - 1.0f) * 6f;
            }
            int lineageId = personById.TryGetValue(holderId, out var holder) ? LineageIdFor(holder) : 0;
            if (lineageId != 0)
                foreach (var d in axes)
                {
                    float family = CultureNorm(lineageId, d);
                    float mine = holderProfile[(int)d];
                    if (Math.Abs(family) > 0.5f && Math.Abs(mine - family) > 1.2f && Math.Sign(mine) != Math.Sign(family)) total += 4f;
                }
            return Math.Min(25f, total);
        }
    }
}
