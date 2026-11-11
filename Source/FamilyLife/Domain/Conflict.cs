using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum RiftCause : byte
    {
        Tension = 0,    // lasting ill feeling between two people
        Neglect = 1,    // a parent giving one child far less of their time than another
        Rivalry = 2,    // a neglected child against the sibling who has had the attention
        Breakup = 3     // the end of a partnership
    }

    public enum CoupleStage : byte { Steady = 0, Strained = 1, Distant = 2, Crisis = 3 }

    /// <summary>
    /// A conflict between two people with a stated cause. There is never more than one open rift per pair, and each has a
    /// way out: time together, an apology that is accepted, or the cause going away.
    /// </summary>
    public sealed partial class Rift
    {
        public int Id;
        public int A;                 // for Neglect: the parent. For Rivalry: the neglected child.
        public int B;                 // for Neglect: the child. For Rivalry: the favoured sibling.
        public RiftCause Cause;
        public int StartTick;
        public float Severity;        // 0-100
        public int ResolvedTick;      // 0 while open
        public int CalmSince;         // when both sides last calmed down; 0 if they are not calm
        public int LastTryTick;       // the last time someone tried to make peace
        public bool IsOpen => ResolvedTick == 0;
    }

    public static class ConflictTuning
    {
        public const float TensionOpens = 45f;
        public const float TensionCalm = 30f;
        public const float RiftCalmBelow = 15f;
        public const int SustainTicks = ActivityTuning.TicksPerDay;          // a bad day is not a rift
        public const int CalmTicks = 2 * ActivityTuning.TicksPerDay;
        public const int RivalryAfterTicks = 3 * ActivityTuning.TicksPerDay;
        public const int TryAgainTicks = ActivityTuning.TicksPerDay;
        public const int FloorTicks = 10 * ActivityTuning.TicksPerDay;
        public const float TensionFloorShare = 0.6f;                         // an open rift keeps tension at least this share of its severity
        public const float NeglectMinHours = 3f;                             // the favoured child must have had this much attention recently
        public const float NeglectRatio = 0.25f;                             // and the neglected one a quarter of it or less
        public const float RestoredRatio = 0.5f;
        public const int MaxClosedRifts = 60;
        public const float QualityDecayPerHour = 0.9956f;                    // about 10% a day

        public const float ReconcileTensionRelief = 30f;
        public const float ReconcileFailureTension = 5f;

        public static CoupleStage StageOf(float score)
        {
            if (score >= 55f) return CoupleStage.Steady;
            if (score >= 40f) return CoupleStage.Strained;
            if (score >= 25f) return CoupleStage.Distant;
            return CoupleStage.Crisis;
        }

        /// <summary>
        /// How the game's own chance of a breakup is scaled. The game already weighs opinion; this makes a breakup follow the
        /// state of the relationship: rare when it is healthy, likely in crisis. In strict mode only a crisis can end one.
        /// </summary>
        public static float BreakupMultiplier(CoupleStage stage, bool strict)
        {
            if (strict && stage != CoupleStage.Crisis) return 0f;
            switch (stage)
            {
                case CoupleStage.Steady: return 0.15f;
                case CoupleStage.Strained: return 0.6f;
                case CoupleStage.Distant: return 1.0f;
                default: return 2.5f;
            }
        }

        public static float ReconcileChance(float trustFrom, float trustTo, float forgiving)
        {
            float chance = 0.25f + 0.003f * (trustFrom + trustTo) + 0.08f * forgiving;
            return Math.Max(0.1f, Math.Min(0.9f, chance));
        }

        /// <summary>
        /// An affair needs a chain, not a roll: the couple already drifting apart, the other person familiar enough to matter,
        /// and a nature that is not devoted. And the setting must be on.
        /// </summary>
        public static bool AffairPossible(bool enabled, CoupleStage stage, float initiatorDevotion, float familiarityWithOther)
        {
            return enabled && stage >= CoupleStage.Distant && initiatorDevotion < 1.5f && familiarityWithOther >= 20f;
        }

        /// <summary>
        /// Experience a younger or less skilled relative gains from being taught. Zero unless the teacher is clearly ahead.
        /// </summary>
        public static float MentorXp(int mentorLevel, int learnerLevel, float mentorSocial, float familiarity, int ticks)
        {
            int gap = mentorLevel - learnerLevel;
            if (gap < 3 || ticks <= 0) return 0f;
            float perHour = 60f + 20f * Math.Min(8, gap);
            float hours = ticks / (float)BondTuning.TicksPerHour;
            return perHour * ActivityTuning.SocialFactor(mentorSocial) * (0.5f + BondTuning.Clamp(familiarity) / 100f) * hours;
        }
    }

    public sealed partial class Bond
    {
        public int HighTensionSince;       // when tension passed the opening level; 0 if it has not
        public float QualityRecent;        // hours of purposeful time together, fading by about a tenth a day
        public float LastOpinion;          // the game's opinion of the other person at the last update; decides whether time together helps or grates
    }

    public sealed partial class FamilyRegistry
    {
        public List<Rift> Rifts = new List<Rift>();
        public int NextRiftId = 1;

        public Rift OpenRiftBetween(int x, int y)
        {
            return Rifts.FirstOrDefault(r => r.IsOpen && ((r.A == x && r.B == y) || (r.A == y && r.B == x)));
        }

        public List<Rift> OpenRiftsOf(int personId)
        {
            return Rifts.Where(r => r.IsOpen && (r.A == personId || r.B == personId)).OrderByDescending(r => r.Severity).ToList();
        }

        public List<Rift> ClosedRifts() => Rifts.Where(r => !r.IsOpen).OrderByDescending(r => r.ResolvedTick).ToList();

        // ------------------------------------------------------------------ couples

        /// <summary>A summary of how a couple is doing (0-100) and the descriptive state it falls in.</summary>
        public float CoupleScore(int a, int b, int now)
        {
            EnsureIndexes();
            var ab = GetBond(a, b);
            var ba = GetBond(b, a);
            if (ab == null || ba == null) return 50f;
            float attachment = (ab.Attachment + ba.Attachment) / 2f;
            float trust = (ab.Trust + ba.Trust) / 2f;
            float tension = (ab.Tension + ba.Tension) / 2f;
            int lastTogether = Math.Max(ab.LastQualityTick, ba.LastQualityTick);
            float hoursApart = lastTogether > 0 ? Math.Max(0, now - lastTogether) / (float)BondTuning.TicksPerHour : 24f * 10f;
            float stale = Math.Min(20f, hoursApart / 24f);
            float rift = OpenRiftBetween(a, b) != null ? 15f : 0f;
            return BondTuning.Clamp(0.4f * attachment + 0.3f * trust - 0.5f * tension - stale - rift + 20f);
        }

        public CoupleStage CoupleStageOf(int a, int b, int now) => ConflictTuning.StageOf(CoupleScore(a, b, now));

        // ------------------------------------------------------------------ rifts

        /// <summary>
        /// Opens rifts that have a visible cause, keeps their tension up while they last, and closes those whose cause has gone.
        /// </summary>
        public List<FamilyEvent> UpdateRifts(int now, bool rivalryEnabled, IEnumerable<FamilyEvent> events)
        {
            EnsureIndexes();
            var result = new List<FamilyEvent>();

            // Tension that persists.
            foreach (var bond in Bonds)
            {
                if (bond.Tension >= ConflictTuning.TensionOpens) { if (bond.HighTensionSince == 0) bond.HighTensionSince = Math.Max(1, now); }
                else if (bond.Tension < ConflictTuning.TensionCalm) bond.HighTensionSince = 0;
            }
            foreach (var bond in Bonds.Where(b => b.HighTensionSince > 0 && now - b.HighTensionSince >= ConflictTuning.SustainTicks).ToList())
                TryOpen(bond.From, bond.To, RiftCause.Tension, Math.Max(bond.Tension, GetBond(bond.To, bond.From)?.Tension ?? 0f), now, result);

            // A partnership that ended.
            foreach (var e in events ?? Enumerable.Empty<FamilyEvent>())
                if (e.Kind == FamilyEventKind.PartnershipEnded) TryOpen(e.A, e.B, RiftCause.Breakup, 40f, now, result);

            // A parent whose attention is lopsided.
            foreach (var parent in Persons.Where(p => p.InColony && !p.Juvenile))
            {
                var kids = ChildrenOf(parent.Id).Select(id => personById.TryGetValue(id, out var k) ? k : null).Where(k => k != null && k.InColony && k.Juvenile).ToList();
                if (kids.Count < 2) continue;
                var attention = kids.ToDictionary(k => k.Id, k => GetBond(parent.Id, k.Id)?.QualityRecent ?? 0f);
                var favoured = kids.OrderByDescending(k => attention[k.Id]).ThenBy(k => k.Id).First();
                if (attention[favoured.Id] < ConflictTuning.NeglectMinHours) continue;
                foreach (var kid in kids.Where(k => k.Id != favoured.Id && attention[k.Id] <= attention[favoured.Id] * ConflictTuning.NeglectRatio))
                    TryOpen(parent.Id, kid.Id, RiftCause.Neglect, 25f, now, result, favoured.Id);
            }

            // Sibling rivalry grows out of neglect that has gone unanswered.
            if (rivalryEnabled)
            {
                foreach (var neglect in Rifts.Where(r => r.IsOpen && r.Cause == RiftCause.Neglect && now - r.StartTick >= ConflictTuning.RivalryAfterTicks).ToList())
                {
                    var favourite = FavouredSibling(neglect.A, neglect.B);
                    if (favourite != 0) TryOpen(neglect.B, favourite, RiftCause.Rivalry, 20f, now, result);
                }
            }

            // Keep tension up while a rift lasts; close the ones whose cause is gone.
            foreach (var rift in Rifts.Where(r => r.IsOpen).ToList())
            {
                bool bothHere = personById.TryGetValue(rift.A, out var pa) && personById.TryGetValue(rift.B, out var pb) && pa.InColony && pb.InColony;
                if (!bothHere) { Close(rift, now, false, result); continue; }

                if (rift.Cause == RiftCause.Neglect || rift.Cause == RiftCause.Rivalry) { if (AttentionRestored(rift)) Close(rift, now, true, result); continue; }

                // For its first days a rift keeps tension up; after that it is only as strong as the ill feeling that remains.
                float floor = now - rift.StartTick < ConflictTuning.FloorTicks ? rift.Severity * ConflictTuning.TensionFloorShare : 0f;
                var ab = GetBond(rift.A, rift.B);
                var ba = GetBond(rift.B, rift.A);
                if (ab != null) ab.Tension = Math.Max(ab.Tension, Math.Min(floor, ab.Tension + 1f));
                if (ba != null) ba.Tension = Math.Max(ba.Tension, Math.Min(floor, ba.Tension + 1f));
                bool calm = (ab?.Tension ?? 0f) < ConflictTuning.RiftCalmBelow && (ba?.Tension ?? 0f) < ConflictTuning.RiftCalmBelow;
                // The floor is what keeps a rift going; once nothing has been done about it for long enough it fades on its own.
                if (calm) { if (rift.CalmSince == 0) rift.CalmSince = Math.Max(1, now); if (now - rift.CalmSince >= ConflictTuning.CalmTicks) Close(rift, now, true, result); }
                else rift.CalmSince = 0;
            }

            var closed = Rifts.Where(r => !r.IsOpen).OrderBy(r => r.ResolvedTick).ToList();
            while (closed.Count > ConflictTuning.MaxClosedRifts) { Rifts.Remove(closed[0]); closed.RemoveAt(0); }
            return result;
        }

        private int FavouredSibling(int parentId, int neglectedId)
        {
            var kids = ChildrenOf(parentId).Where(id => id != neglectedId && personById.TryGetValue(id, out var k) && k.InColony && k.Juvenile).ToList();
            if (kids.Count == 0) return 0;
            return kids.OrderByDescending(id => GetBond(parentId, id)?.QualityRecent ?? 0f).ThenBy(id => id).First();
        }

        private bool AttentionRestored(Rift rift)
        {
            int parent = rift.Cause == RiftCause.Neglect ? rift.A : 0;
            int child = rift.B;
            if (rift.Cause == RiftCause.Rivalry)
            {
                var rival = personById.TryGetValue(rift.A, out var neglected) ? neglected : null;
                if (rival == null) return true;
                parent = rival.ParentIds.FirstOrDefault(pid => personById.TryGetValue(pid, out var pp) && pp.InColony);
                child = rift.A;
            }
            if (parent == 0) return true;
            var mine = GetBond(parent, child)?.QualityRecent ?? 0f;
            var best = ChildrenOf(parent).Select(id => GetBond(parent, id)?.QualityRecent ?? 0f).DefaultIfEmpty(0f).Max();
            return mine >= best * ConflictTuning.RestoredRatio || best < ConflictTuning.NeglectMinHours;
        }

        private void TryOpen(int a, int b, RiftCause cause, float severity, int now, List<FamilyEvent> events, int favoured = 0)
        {
            if (a == b || OpenRiftBetween(a, b) != null) return;
            if (!personById.TryGetValue(a, out var pa) || !personById.TryGetValue(b, out var pb) || !pa.InColony || !pb.InColony) return;
            Rifts.Add(new Rift { Id = NextRiftId++, A = a, B = b, Cause = cause, StartTick = now, Severity = BondTuning.Clamp(severity) });
            events.Add(new FamilyEvent(FamilyEventKind.RiftOpened, now, a, b));
            NoteBond(a, b, BondCause.Rift, -8f, now);
            NoteBond(b, a, BondCause.Rift, -8f, now);
        }

        private void Close(Rift rift, int now, bool reportable, List<FamilyEvent> events)
        {
            rift.ResolvedTick = Math.Max(1, now);
            if (reportable) events.Add(new FamilyEvent(FamilyEventKind.RiftResolved, now, rift.A, rift.B));
        }

        /// <summary>An attempt at peace has been made. If it worked, the rift closes and the tension eases; if not, it tires them both a little.</summary>
        public FamilyEvent TryReconcile(int riftId, bool accepted, int now)
        {
            var rift = Rifts.FirstOrDefault(r => r.Id == riftId && r.IsOpen);
            if (rift == null) return null;
            rift.LastTryTick = now;
            var ab = GetBond(rift.A, rift.B);
            var ba = GetBond(rift.B, rift.A);
            if (!accepted)
            {
                if (ab != null) ab.Tension = BondTuning.Clamp(ab.Tension + ConflictTuning.ReconcileFailureTension);
                if (ba != null) ba.Tension = BondTuning.Clamp(ba.Tension + ConflictTuning.ReconcileFailureTension);
                return null;
            }
            if (ab != null) { ab.Tension = BondTuning.Clamp(ab.Tension - ConflictTuning.ReconcileTensionRelief); ab.Trust = BondTuning.Clamp(ab.Trust + 5f); ab.Note(BondCause.Peace, 6f, now); }
            if (ba != null) { ba.Tension = BondTuning.Clamp(ba.Tension - ConflictTuning.ReconcileTensionRelief); ba.Trust = BondTuning.Clamp(ba.Trust + 5f); ba.Note(BondCause.Peace, 6f, now); }
            rift.ResolvedTick = Math.Max(1, now);
            var e = new FamilyEvent(FamilyEventKind.RiftResolved, now, rift.A, rift.B);
            Chronicle.Add(e);
            if (Chronicle.Count > MaxChronicle) Chronicle.RemoveRange(0, Chronicle.Count - MaxChronicle);
            int lineage = personById.TryGetValue(rift.A, out var pa) ? LineageIdFor(pa) : 0;
            Remember(lineage, MemoryKind.Reconciliation, now, rift.A, rift.B, 1.0f);
            NoteStewardService(rift.A, FamilyOf(rift.A), StewardServiceKind.Reconciliation, true, now);
            NoteStewardService(rift.B, FamilyOf(rift.B), StewardServiceKind.Reconciliation, true, now);
            return e;
        }

        /// <summary>True if enough time has passed since the last attempt at peace for this rift.</summary>
        public bool MayTryReconcile(int riftId, int now)
        {
            var rift = Rifts.FirstOrDefault(r => r.Id == riftId && r.IsOpen);
            return rift != null && (rift.LastTryTick == 0 || now - rift.LastTryTick >= ConflictTuning.TryAgainTicks);
        }
    }
}
