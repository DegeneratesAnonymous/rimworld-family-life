using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public static class EventTuning
    {
        public const int MaxLog = 300;
        public const int MaxTies = 60;
        public const int MaxCommitments = 80;
        public const float GreatAt = 0.70f;
        public const float GoodAt = 0.52f;
        public const float FlatAt = 0.34f;
        public const float Noise = 0.14f;
        public const int RepeatWindowTicks = 7 * ActivityTuning.TicksPerDay;
        public const int MaxPerPersonPerDay = 3;
        public const float RiftOpensAtTension = ConflictTuning.TensionOpens;
        public const float TieFamiliarityDecayPerDay = 0.3f;
        public const float TieGoodwillDecayPerDay = 0.4f;
        public const float TieGrievanceEasePerDay = 0.6f;
        public const float DefiningWeight = 1.3f;
        public const int PromiseGraceTicks = ActivityTuning.TicksPerDay / 2;
        public const float MinPromiseSeconds = 600f;
    }

    public sealed partial class FamilyRegistry
    {
        public List<EventRecord> EventLog = new List<EventRecord>();
        public List<Commitment> Commitments = new List<Commitment>();
        public List<FamilyTie> Ties = new List<FamilyTie>();
        public int NextCommitmentId = 1;

        private void RepairEvents()
        {
            EventLog = EventLog ?? new List<EventRecord>();
            EventLog.RemoveAll(e => e == null || string.IsNullOrEmpty(e.EventId));
            Commitments = Commitments ?? new List<Commitment>();
            Commitments.RemoveAll(c => c == null || !personById.ContainsKey(c.FromId) || !personById.ContainsKey(c.ToId));
            Ties = Ties ?? new List<FamilyTie>();
            Ties.RemoveAll(t => t == null || t.LineageA == t.LineageB || !lineageById.ContainsKey(t.LineageA) || !lineageById.ContainsKey(t.LineageB));
            foreach (var t in Ties)
            {
                if (t.LineageA > t.LineageB) { int x = t.LineageA; t.LineageA = t.LineageB; t.LineageB = x; }
                t.Familiarity = BondTuning.Clamp(t.Familiarity);
                t.Goodwill = BondTuning.Clamp(t.Goodwill);
                t.Grievance = BondTuning.Clamp(t.Grievance);
            }
        }

        // ------------------------------------------------------------------ families and each other

        public FamilyTie GetTie(int lineageA, int lineageB)
        {
            EnsureIndexes();
            if (lineageA == lineageB || lineageA == 0 || lineageB == 0) return null;
            int lo = Math.Min(lineageA, lineageB), hi = Math.Max(lineageA, lineageB);
            return Ties.FirstOrDefault(t => t.LineageA == lo && t.LineageB == hi);
        }

        private FamilyTie EnsureTie(int lineageA, int lineageB)
        {
            var tie = GetTie(lineageA, lineageB);
            if (tie != null) return tie;
            if (!lineageById.ContainsKey(lineageA) || !lineageById.ContainsKey(lineageB)) return null;
            tie = new FamilyTie { LineageA = Math.Min(lineageA, lineageB), LineageB = Math.Max(lineageA, lineageB) };
            Ties.Add(tie);
            if (Ties.Count > EventTuning.MaxTies)
            {
                var drop = Ties.Where(t => t != tie).OrderBy(t => t.Shared).ThenBy(t => t.LastTick).First();
                Ties.Remove(drop);
            }
            return tie;
        }

        public List<FamilyTie> TiesOf(int lineageId) => Ties.Where(t => t.LineageA == lineageId || t.LineageB == lineageId).OrderByDescending(t => t.LastTick).ToList();

        /// <summary>Goodwill fades without fresh good experiences and grievances ease, but only slowly: a feud cools over weeks, not hours.</summary>
        public void UpdateTies(int now)
        {
            EnsureIndexes();
            foreach (var t in Ties)
            {
                float days = t.LastTick <= 0 ? 0f : Math.Min(10f, Math.Max(0, now - t.LastTick) / (float)ActivityTuning.TicksPerDay);
                if (t.LastTick > 0 && days > 0.01f && now - t.LastTick >= ActivityTuning.TicksPerDay)
                {
                    t.Goodwill = Math.Max(0f, t.Goodwill - EventTuning.TieGoodwillDecayPerDay * days);
                    t.Familiarity = Math.Max(0f, t.Familiarity - EventTuning.TieFamiliarityDecayPerDay * days);
                    t.Grievance = Math.Max(0f, t.Grievance - EventTuning.TieGrievanceEasePerDay * days);
                    t.LastTick = now;
                }
            }
            Ties.RemoveAll(t => t.Familiarity < 1f && t.Goodwill < 1f && t.Grievance < 1f && t.Shared < 3);
        }

        // ------------------------------------------------------------------ the log and cooldowns

        public int DaysSinceEvent(string eventId, int a, int b, int now)
        {
            for (int i = EventLog.Count - 1; i >= 0; i--)
            {
                var e = EventLog[i];
                if (e.EventId == eventId && ((e.A == a && e.B == b) || (e.A == b && e.B == a)))
                    return Math.Max(0, now - e.Tick) / ActivityTuning.TicksPerDay;
            }
            return int.MaxValue;
        }

        public int EventsTodayFor(int personId, int now)
        {
            int day = now / ActivityTuning.TicksPerDay;
            int n = 0;
            for (int i = EventLog.Count - 1; i >= 0; i--)
            {
                var e = EventLog[i];
                if (e.Tick / ActivityTuning.TicksPerDay < day) break;
                if (e.Weight != EventWeight.Ambient && (e.A == personId || e.B == personId)) n++;
            }
            return n;
        }

        public int CountEvents(string eventId, int a, int b, int sinceTick, EventTier? tier = null)
        {
            return EventLog.Count(e => e.EventId == eventId && e.Tick >= sinceTick && ((e.A == a && e.B == b) || (e.A == b && e.B == a)) && (!tier.HasValue || e.Tier == tier.Value));
        }

        /// <summary>How many times this person has been turned down (a poor outcome) trying to start this event with others lately.</summary>
        public int TimesTurnedDown(string eventId, int personId, int sinceTick) =>
            EventLog.Count(e => e.EventId == eventId && e.A == personId && e.Tick >= sinceTick && e.Tier == EventTier.Poor);

        private float RepetitionFactor(string eventId, int a, int b, int now)
        {
            int n = CountEvents(eventId, a, b, now - EventTuning.RepeatWindowTicks);
            return n == 0 ? 1f : (n == 1 ? 0.6f : (n == 2 ? 0.35f : 0.2f));
        }

        // ------------------------------------------------------------------ what is possible, and why not

        public List<EligibilityLine> Explain(PairContext c, IEnumerable<EventDef> catalog, bool childrenEnabled, Func<EventDef, bool> enabled = null)
        {
            var lines = new List<EligibilityLine>();
            var scenario = ScenarioOf(c);
            bool dailyLimit = EventsTodayFor(c.Initiator.Id, c.Now) >= EventTuning.MaxPerPersonPerDay ||
                              EventsTodayFor(c.Recipient.Id, c.Now) >= EventTuning.MaxPerPersonPerDay;
            foreach (var d in catalog)
            {
                var line = new EligibilityLine { Def = d };
                lines.Add(line);
                if (enabled != null && !enabled(d)) { line.Reason = "turned off in settings"; continue; }
                if (d.Weight != EventWeight.Ambient && dailyLimit)
                { line.Reason = "daily meaningful moment allowance reached"; continue; }
                if (d.NeedsChild && !childrenEnabled) { line.Reason = "needs children and Biotech"; continue; }
                bool scenarioFits = d.Scenario == scenario || (d.ThirdIsChild && d.Scenario == EventScenario.AdultChild && c.Third != null && c.Third.Child && scenario == EventScenario.AdultAdult);
                if (d.RequiredTradition != null && c.TraditionId != d.RequiredTradition) { line.Reason = "needs its family tradition"; continue; }
                if (!d.AnyParticipants && !scenarioFits) { line.Reason = "this pair is " + scenario + ", not " + d.Scenario; continue; }
                if (!d.AnyParticipants && !BucketAllows(d.Bucket, c)) { line.Reason = d.Bucket == EventBucket.IntraFamily ? "they are not relatives" : (d.Bucket == EventBucket.InterFamily ? "they do not belong to two different families" : "they are family, not outsiders to each other"); continue; }
                if (d.ActivityOnly && !c.FromActivity) { line.Reason = "only happens as part of an activity"; continue; }
                if (d.Needs.Length > 0 && !d.Needs.Contains(c.Setting)) { line.Reason = "only happens during " + string.Join("/", d.Needs.Select(n => n.ToString())); continue; }
                if (d.NeedsThird && c.Third == null) { line.Reason = "needs a third person involved"; continue; }
                if (d.ConcernsPromise && c.Broken == null) { line.Reason = "no recorded promise was broken"; continue; }
                int since = DaysSinceEvent(d.Id, c.Initiator.Id, c.Recipient.Id, c.Now);
                if (since < d.CooldownDays) { line.Reason = "happened " + since + " day(s) ago (cooldown " + d.CooldownDays + ")"; continue; }
                var failed = d.Opportunity.FirstOrDefault(o => !o.Test(c));
                if (failed != null) { line.Reason = "needs: " + failed.Name; continue; }
                line.Motive = d.Motive.Value(c, d.Skill);
                if (d.ValueAffinity.HasValue) line.Motive *= Math.Max(0.4f, 1f + c.Initiator.Axis(d.ValueAffinity.Value) * 0.4f);
                line.Weight = d.Favored.Contains(c.Setting) ? line.Motive * 1.6f : line.Motive;
                if (line.Weight <= 0.01f) { line.Reason = "no motivation"; continue; }
                line.Eligible = true;
                line.Reason = "possible";
            }
            return lines;
        }

        public static EventScenario ScenarioOf(PairContext c)
        {
            if (c.Initiator.Child && c.Recipient.Child) return EventScenario.ChildChild;
            if (c.Initiator.Child || c.Recipient.Child) return EventScenario.AdultChild;
            return EventScenario.AdultAdult;
        }

        /// <summary>Relatives (or the same listed family) have intra-family moments; everyone else has individual ones, and people from two different families can also have inter-family ones.</summary>
        public static bool BucketAllows(EventBucket bucket, PairContext c)
        {
            bool family = c.SameFamily || c.Related;
            switch (bucket)
            {
                case EventBucket.IntraFamily: return family;
                case EventBucket.Individual: return !family;
                default: return !family && c.DifferentFamilies;
            }
        }

        /// <summary>Chooses one eligible event for the pair, more likely the more the initiator is moved to do it. Null when nothing fits.</summary>
        public EventDef PickEvent(PairContext c, IEnumerable<EventDef> catalog, Random rng, bool childrenEnabled, Func<EventDef, bool> enabled = null)
        {
            var eligible = Explain(c, catalog, childrenEnabled, enabled).Where(l => l.Eligible).ToList();
            if (eligible.Count == 0) return null;
            // A person is not moved to do something just because it is possible: the best of the lot still has to pass a motivation roll.
            double total = eligible.Sum(l => l.Weight);
            double pick = rng.NextDouble() * total;
            EligibilityLine chosen = eligible[eligible.Count - 1];
            foreach (var l in eligible)
            {
                pick -= l.Weight;
                if (pick <= 0) { chosen = l; break; }
            }
            return rng.NextDouble() < Math.Min(0.95f, 0.25f + chosen.Motive) ? chosen.Def : null;
        }

        /// <summary>
        /// Chooses across several possible circumstances for the same two people (with different third people, say) as one weighted draw,
        /// so an event that needs a third person is not favoured or starved just because it was looked at first.
        /// </summary>
        public KeyValuePair<EventDef, PairContext>? PickEventAmong(IList<PairContext> contexts, IEnumerable<EventDef> catalog, Random rng, bool childrenEnabled, Func<EventDef, bool> enabled = null)
        {
            var pool = new List<KeyValuePair<EligibilityLine, PairContext>>();
            var defs = catalog.ToList();
            var seen = new HashSet<(string, int, int, int, ActivityKind, bool)>();
            foreach (var c in contexts)
                foreach (var l in Explain(c, defs.Where(d => d.NeedsThird == (c.Third != null)), childrenEnabled, enabled).Where(l => l.Eligible))
                {
                    // Pair-only events belong to the plain context, not to every nearby witness.
                    var key = (l.Def.Id, c.Initiator.Id, c.Recipient.Id, c.Third?.Id ?? 0, c.Setting, c.FromActivity);
                    if (seen.Add(key)) pool.Add(new KeyValuePair<EligibilityLine, PairContext>(l, c));
                }
            if (pool.Count == 0) return null;
            double total = pool.Sum(p => p.Key.Weight);
            double pick = rng.NextDouble() * total;
            var chosen = pool[pool.Count - 1];
            foreach (var p in pool)
            {
                pick -= p.Key.Weight;
                if (pick <= 0) { chosen = p; break; }
            }
            if (rng.NextDouble() >= Math.Min(0.95f, 0.25f + chosen.Key.Motive)) return null;
            return new KeyValuePair<EventDef, PairContext>(chosen.Key.Def, chosen.Value);
        }

        // ------------------------------------------------------------------ doing it

        private static float Execution(EventDef d, PairContext c, Random rng)
        {
            var i = c.Initiator;
            float v;
            if (d.Skill != null) v = 0.25f + 0.6f * Math.Min(20, i.Skill(d.Skill)) / 20f + 0.08f * i.Axis(Dim.Warmth);
            else v = 0.5f + 0.22f * i.Axis(Dim.Warmth) + 0.15f * i.Axis(Dim.Steadiness) + 0.1f * ((i.Mood - 0.5f) * 2f);
            v += (float)(rng.NextDouble() * 2 - 1) * 0.12f;
            return Math.Max(0f, Math.Min(1f, v));
        }

        public EventResult ResolveEvent(EventDef d, PairContext c, Random rng)
        {
            var r = new EventResult { Def = d, Context = c };
            r.Execution = Execution(d, c, rng);
            r.Reception = d.Reception.Value(c, d.Skill);
            r.Score = d.SkillShare * r.Execution + (1f - d.SkillShare) * r.Reception + (float)(rng.NextDouble() * 2 - 1) * EventTuning.Noise;
            r.Tier = r.Score >= EventTuning.GreatAt ? EventTier.Great : (r.Score >= EventTuning.GoodAt ? EventTier.Good : (r.Score >= EventTuning.FlatAt ? EventTier.Flat : EventTier.Poor));
            switch (r.Tier)
            {
                case EventTier.Great: r.InitiatorFeeling = 1f; r.RecipientFeeling = 1f; break;
                case EventTier.Good: r.InitiatorFeeling = 0.5f; r.RecipientFeeling = 0.5f; break;
                case EventTier.Flat: r.InitiatorFeeling = d.Arc.Keen ? 0.2f : 0f; r.RecipientFeeling = d.Arc.Keen ? -0.1f : 0f; break;
                default: r.InitiatorFeeling = d.Arc.Keen ? 0.1f : -0.6f; r.RecipientFeeling = d.Arc.Keen ? -0.7f : -0.6f; break;
            }
            if (d.Outcomes != null)
            {
                r.InitiatorFeeling = d.Outcomes[(int)r.Tier].Initiator.Feeling;
                r.RecipientFeeling = d.Outcomes[(int)r.Tier].Recipient.Feeling;
            }
            return r;
        }

        private static float Room(float v) => Math.Max(0.15f, 1f - v / 100f) * 1.5f;

        private void Shift(Bond b, EventTier tier, Arc arc, bool initiator, float repeat, float pace = 1f)
        {
            if (b == null) return;
            float oldA = b.Attachment, oldT = b.Trust, oldN = b.Tension, oldF = b.Familiarity;
            float room = Room(b.Attachment);
            float trustRoom = Room(b.Trust);
            switch (tier)
            {
                case EventTier.Great:
                    b.Attachment += arc.Gain * room * repeat; b.Trust += arc.Trust * trustRoom * repeat; b.Tension -= Math.Min(b.Tension, arc.Relief); b.Familiarity += arc.Familiarity; break;
                case EventTier.Good:
                    b.Attachment += 0.5f * arc.Gain * room * repeat; b.Trust += 0.5f * arc.Trust * trustRoom * repeat; b.Tension -= Math.Min(b.Tension, arc.Relief * 0.5f); b.Familiarity += arc.Familiarity * 0.7f; break;
                case EventTier.Flat:
                    b.Attachment += (arc.Keen && initiator ? 0.25f : 0.1f) * arc.Gain * room * repeat; b.Familiarity += arc.Familiarity * 0.4f; break;
                default:
                    b.Attachment += arc.Keen && initiator ? 0.2f * arc.Gain : -0.3f * arc.Gain;
                    b.Trust -= 0.4f * arc.Trust;
                    b.Tension += arc.Cost * (initiator ? 0.7f : 1f);
                    b.Familiarity += arc.Familiarity * 0.2f;
                    break;
            }
            pace = Math.Max(0f, Math.Min(1f, pace));
            b.Attachment = oldA + (b.Attachment - oldA) * pace; b.Trust = oldT + (b.Trust - oldT) * pace;
            b.Tension = oldN + (b.Tension - oldN) * pace; b.Familiarity = oldF + (b.Familiarity - oldF) * pace;
            b.Attachment = BondTuning.Clamp(b.Attachment); b.Trust = BondTuning.Clamp(b.Trust);
            b.Tension = BondTuning.Clamp(b.Tension); b.Familiarity = BondTuning.Clamp(b.Familiarity);
        }

        /// <summary>
        /// Carries out what a resolved event does: moves both bonds (not necessarily alike), notes the cause, mends or strains a rift,
        /// moves the standing between families, records a promise, and logs it. The only place an event changes the registry.
        /// </summary>
        public void ApplyEvent(EventResult r, int now)
        {
            EnsureIndexes();
            var d = r.Def;
            var c = r.Context;
            int a = c.Initiator.Id, b = c.Recipient.Id;
            if (!personById.ContainsKey(a) || !personById.ContainsKey(b)) return;
            float repeat = RepetitionFactor(d.Id, a, b, now);

            var ab = EnsureBond(a, b, now);
            var ba = EnsureBond(b, a, now);
            var cause = CauseFor(d, r.Tier);
            if (d.Outcomes == null)
            {
                Shift(ab, r.Tier, d.Arc, true, repeat, c.RelationshipPace);
                Shift(ba, r.Tier, d.Arc, false, repeat, c.RelationshipPace);
            }
            else
            {
                ChangeBond(a, b, d.Outcomes[(int)r.Tier].Initiator, now, repeat * Math.Max(0f, Math.Min(1f, c.RelationshipPace)));
                ChangeBond(b, a, d.Outcomes[(int)r.Tier].Recipient, now, repeat * Math.Max(0f, Math.Min(1f, c.RelationshipPace)));
            }
            float noted = (r.Tier == EventTier.Great ? 1f : (r.Tier == EventTier.Good ? 0.5f : (r.Tier == EventTier.Poor ? -0.6f : 0f))) * d.Arc.Gain;
            if (d.Outcomes == null)
            {
                if (ab != null) ab.Note(cause, noted, now);
                if (ba != null) ba.Note(cause, d.Arc.Keen && r.Tier <= EventTier.Flat ? -Math.Abs(noted) - 0.3f : noted, now);
            }
            if (r.Tier >= EventTier.Good && (d.Outcomes == null || r.InitiatorFeeling > 0 && r.RecipientFeeling > 0))
            {
                float hours = r.Tier == EventTier.Great ? 0.8f : 0.4f;     // meaningful attention counts toward who has had time lately
                if (ab != null) { ab.LastQualityTick = now; ab.QualityRecent += hours; }
                if (ba != null) { ba.LastQualityTick = now; ba.QualityRecent += hours; }
            }

            if ((d.Effects & EventEffect.Confronts) != 0 && c.Third != null && personById.ContainsKey(c.Third.Id))
            {
                var toThird = EnsureBond(c.Third.Id, a, now);
                float heat = r.Tier == EventTier.Poor ? 8f : (r.Tier == EventTier.Flat ? 1f : (r.Tier == EventTier.Good ? 3f : 2f));
                if (toThird != null) { toThird.Tension = BondTuning.Clamp(toThird.Tension + heat); toThird.Note(BondCause.Friction, -heat * 0.3f, now); }
            }

            if (d.TieIsRecipientThird && c.Third != null && personById.ContainsKey(c.Third.Id))
            {
                var rt = EnsureBond(b, c.Third.Id, now);
                var tr = EnsureBond(c.Third.Id, b, now);
                Shift(rt, r.Tier, Arc.Light, false, 1f);
                Shift(tr, r.Tier, Arc.Light, false, 1f);
            }

            if (d.ConcernsPromise && c.Broken != null)
            {
                var promise = Commitments.FirstOrDefault(p => p.Id == c.Broken.Id);
                if (promise != null) promise.State = 3;
                float repair = r.Tier == EventTier.Great ? 6f : (r.Tier == EventTier.Good ? 3f : (r.Tier == EventTier.Flat ? 0f : -4f));
                if (ba != null) ba.Trust = BondTuning.Clamp(ba.Trust + repair);
            }

            if ((d.Effects & EventEffect.EasesRift) != 0 && c.Rift != null && c.Rift.IsOpen)
            {
                // Neglect and rivalry close when the attention is really restored, not because of one conversation; this only helps.
                bool bySelf = c.Rift.Cause == RiftCause.Neglect || c.Rift.Cause == RiftCause.Rivalry;
                if (r.Tier == EventTier.Great && !bySelf) { TryReconcile(c.Rift.Id, true, now); r.RiftResolved = true; }
                else if (r.Tier >= EventTier.Good && bySelf) { c.Rift.Severity = Math.Max(5f, c.Rift.Severity * 0.7f); r.RiftEased = true; }
                else if (r.Tier == EventTier.Good)
                {
                    c.Rift.Severity = Math.Max(5f, c.Rift.Severity * 0.6f);
                    c.Rift.LastTryTick = now;
                    r.RiftEased = true;
                }
                else if (!bySelf) TryReconcile(c.Rift.Id, false, now);
            }
            else if ((d.Effects & EventEffect.StrainsRift) != 0 && r.Tier == EventTier.Poor && (ab?.Tension ?? 0f) >= EventTuning.RiftOpensAtTension && OpenRiftBetween(a, b) == null)
            {
                int before = NextRiftId;
                var events = new List<FamilyEvent>();
                TryOpen(a, b, RiftCause.Tension, Math.Max(ab.Tension, ba?.Tension ?? 0f), now, events);
                foreach (var e in events) { Chronicle.Add(e); }
                if (Chronicle.Count > MaxChronicle) Chronicle.RemoveRange(0, Chronicle.Count - MaxChronicle);
                if (NextRiftId != before) r.RiftOpened = before;
            }

            // Forgiveness requires the injured family's informed participation and a successful reconciliation.
            if ((d.Id == "T6-03" || d.Id == "T8-06") && r.Tier == EventTier.Great)
            {
                var root = Incidents.LastOrDefault(i => !i.Resolved && i.ActorId == a && i.VictimFamily == FamilyOf(b) && i.KnownBy.Contains(b));
                if (root != null) ForgiveIncident(root.Id, FamilyOf(b));
            }

            if ((d.Effects & EventEffect.Promise) != 0 && r.Tier >= EventTier.Good)
                r.PromiseMade = MakePromise(a, b, d.Id, now, 1 + (r.Tier == EventTier.Great ? 1 : 0), r.Tier == EventTier.Great ? 2 : 1);

            if ((d.Effects & EventEffect.Teaches) != 0 && r.Tier >= EventTier.Flat)
            {
                // The event supplies the roles; an unrelated high skill must not reverse a lesson.
                var teacher = d.RecipientTeaches ? c.Recipient : c.Initiator;
                var learner = d.RecipientTeaches ? c.Initiator : c.Recipient;
                string skill = d.TeachSkill == null || d.TeachSkill == "best" ? BestGap(teacher, learner) : d.TeachSkill;
                if (skill != null && learner.Skills.ContainsKey(skill) && teacher.Skill(skill) > learner.Skill(skill))
                {
                    r.XpSkill = skill;
                    r.XpTo = learner.Id;
                    r.XpAmount = (r.Tier == EventTier.Great ? 1.0f : (r.Tier == EventTier.Good ? 0.7f : 0.35f)) * 120f * (0.5f + Math.Min(20, teacher.Skill("Social")) / 40f);
                }
            }

            bool tieApplies = c.DifferentFamilies || (d.TieIsRecipientThird && c.Third != null && c.Third.LineageId != 0 && c.Third.LineageId != c.Recipient.LineageId);
            if (tieApplies && (d.Effects & (EventEffect.TiesFamilies | EventEffect.Incident | EventEffect.Bridge)) != 0)
                ApplyToTie(r, now);

            bool remember = (d.Weight == EventWeight.Defining || (d.Effects & EventEffect.Defines) != 0) && (r.Tier == EventTier.Great || r.Tier == EventTier.Poor);
            if (remember)
            {
                var kind = r.Tier == EventTier.Great ? MemoryKind.Bond : MemoryKind.Rupture;
                bool kept = false;
                int la = c.Initiator.LineageId != 0 ? c.Initiator.LineageId : LineageIdFor(personById[a]);
                int lb = c.Recipient.LineageId != 0 ? c.Recipient.LineageId : LineageIdFor(personById[b]);
                if (la != 0) kept |= Remember(la, kind, now, a, b, EventTuning.DefiningWeight);
                if (lb != 0 && lb != la) kept |= Remember(lb, kind, now, b, a, EventTuning.DefiningWeight);
                r.BecameMemory = kept;
            }

            EventLog.Add(new EventRecord { EventId = d.Id, A = a, B = b, Tick = now, Tier = r.Tier, Weight = d.Weight, Setting = (byte)c.Setting, Third = c.Third?.Id ?? 0 });
            TrimLog();
        }

        private void TrimLog()
        {
            while (EventLog.Count > EventTuning.MaxLog)
            {
                int index = EventLog.FindIndex(e => e.Weight == EventWeight.Ambient);
                EventLog.RemoveAt(index >= 0 ? index : 0);
            }
        }

        private static BondCause CauseFor(EventDef d, EventTier tier)
        {
            if (tier == EventTier.Poor) return BondCause.Friction;
            if ((d.Effects & EventEffect.EasesRift) != 0) return BondCause.Peace;
            if ((d.Effects & EventEffect.Teaches) != 0) return BondCause.Teaching;
            switch (d.Bucket)
            {
                case EventBucket.IntraFamily: return d.Scenario == EventScenario.AdultChild ? BondCause.Care : BondCause.Together;
                default: return BondCause.Together;
            }
        }

        private static string BestGap(Actor teacher, Actor learner)
        {
            string best = null;
            int gap = 2;
            foreach (var kv in teacher.Skills)
            {
                if (!learner.Skills.ContainsKey(kv.Key)) continue; // absent means disabled or unavailable
                int diff = kv.Value - learner.Skill(kv.Key);
                if (diff > gap || (diff == gap && best != null && string.CompareOrdinal(kv.Key, best) < 0)) { gap = diff; best = kv.Key; }
            }
            return best;
        }

        private void ApplyToTie(EventResult r, int now)
        {
            var c = r.Context;
            var d = r.Def;
            var tie = d.TieIsRecipientThird && c.Third != null ? EnsureTie(c.Recipient.LineageId, c.Third.LineageId) : EnsureTie(c.Initiator.LineageId, c.Recipient.LineageId);
            if (tie == null) return;
            float bridge = (d.Effects & EventEffect.Bridge) != 0 ? 1.5f : 1f;
            tie.Shared++;
            tie.LastTick = now;
            tie.Familiarity = BondTuning.Clamp(tie.Familiarity + (r.Tier == EventTier.Poor ? 0.5f : 1.5f));
            switch (r.Tier)
            {
                case EventTier.Great:
                    tie.Goodwill = BondTuning.Clamp(tie.Goodwill + 4f * bridge);
                    tie.Grievance = Math.Max(0f, tie.Grievance - 5f * bridge);
                    break;
                case EventTier.Good:
                    tie.Goodwill = BondTuning.Clamp(tie.Goodwill + 2f * bridge);
                    tie.Grievance = Math.Max(0f, tie.Grievance - 2f * bridge);
                    break;
                case EventTier.Flat:
                    tie.Goodwill = BondTuning.Clamp(tie.Goodwill + 0.3f);
                    break;
                default:
                    tie.Goodwill = Math.Max(0f, tie.Goodwill - 1f);
                    if ((d.Effects & EventEffect.Incident) != 0)
                    {
                        tie.Grievance = BondTuning.Clamp(tie.Grievance + 6f);
                        tie.WrongedBy = c.Initiator.Id;
                        tie.WrongedPerson = c.Recipient.Id;
                        tie.GrievanceTick = now;
                    }
                    break;
            }
            r.FamilyTieChanged = true;
        }

        // ------------------------------------------------------------------ promises

        /// <summary>A promise to spend time together by a day. Kept when they do, broken when the day passes.</summary>
        public int MakePromise(int fromId, int toId, string eventId, int now, int days, int importance)
        {
            EnsureIndexes();
            if (fromId == toId) return 0;
            var existing = Commitments.FirstOrDefault(p => p.IsOpen && p.FromId == fromId && p.ToId == toId);
            if (existing != null) return 0;
            var promise = new Commitment { Id = NextCommitmentId++, FromId = fromId, ToId = toId, EventId = eventId, CreatedTick = now, DueTick = now + Math.Max(1, days) * ActivityTuning.TicksPerDay, Importance = Math.Max(1, Math.Min(3, importance)) };
            Commitments.Add(promise);
            while (Commitments.Count > EventTuning.MaxCommitments)
            {
                var old = Commitments.FirstOrDefault(p => !p.IsOpen) ?? Commitments[0];
                Commitments.Remove(old);
            }
            return promise.Id;
        }

        /// <summary>Time together between the two keeps a promise they have made to each other.</summary>
        public List<Commitment> KeepPromises(int a, int b, int ticks, int now)
        {
            var kept = new List<Commitment>();
            if (ticks < EventTuning.MinPromiseSeconds) return kept;
            foreach (var p in Commitments.Where(p => p.IsOpen && ((p.FromId == a && p.ToId == b) || (p.FromId == b && p.ToId == a))))
            {
                p.State = 1;
                kept.Add(p);
                var toPromiser = GetBond(p.ToId, p.FromId);
                if (toPromiser != null)
                {
                    toPromiser.Trust = BondTuning.Clamp(toPromiser.Trust + 2f * p.Importance);
                    toPromiser.Attachment = BondTuning.Clamp(toPromiser.Attachment + 0.8f * p.Importance);
                    toPromiser.Note(BondCause.Together, 0.8f * p.Importance, now);
                }
            }
            return kept;
        }

        /// <summary>Promises whose day has passed (with a little grace) are broken. The one they were made to loses a little trust in the one who made them.</summary>
        public List<Commitment> UpdateCommitments(int now)
        {
            EnsureIndexes();
            var broken = new List<Commitment>();
            foreach (var p in Commitments.Where(p => p.IsOpen && now > p.DueTick + EventTuning.PromiseGraceTicks).ToList())
            {
                p.State = 2;
                broken.Add(p);
                var toPromiser = GetBond(p.ToId, p.FromId);
                if (toPromiser != null)
                {
                    toPromiser.Trust = BondTuning.Clamp(toPromiser.Trust - 2f * p.Importance);
                    toPromiser.Tension = BondTuning.Clamp(toPromiser.Tension + 1.5f * p.Importance);
                    toPromiser.Note(BondCause.Friction, -1f * p.Importance, now);
                }
            }
            return broken;
        }

        public Commitment BrokenPromise(int fromId, int toId) =>
            Commitments.FirstOrDefault(p => p.State == 2 && p.FromId == fromId && p.ToId == toId);

        public bool HasOpenPromise(int fromId, int toId) => Commitments.Any(p => p.IsOpen && p.FromId == fromId && p.ToId == toId);

        /// <summary>Opens a rift for a reason the caller has already decided is real.</summary>
        public int OpenRift(int a, int b, RiftCause cause, float severity, int now)
        {
            EnsureIndexes();
            int id = NextRiftId;
            var events = new List<FamilyEvent>();
            TryOpen(a, b, cause, severity, now, events);
            foreach (var e in events) Chronicle.Add(e);
            if (Chronicle.Count > MaxChronicle) Chronicle.RemoveRange(0, Chronicle.Count - MaxChronicle);
            return NextRiftId != id ? id : 0;
        }
    }
}
