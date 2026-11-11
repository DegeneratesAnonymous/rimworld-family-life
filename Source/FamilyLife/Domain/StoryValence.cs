using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // How a moment went depends on who is looking. "Ivan stood up for Debby against Strick" brought Ivan and Debby closer, and it was a blow to Strick. The story
    // layer therefore never reads one global result: it asks for the effect on a particular person, and for the effect between the two main people separately.

    /// <summary>What one moment meant, for the person being played and between the people it was mainly about.</summary>
    public struct Valence
    {
        /// <summary>The effect on the viewer: -1 harmful, 0 neutral, +1 good. Zero for a moment the viewer was not part of.</summary>
        public int OnViewer;
        /// <summary>The effect between the initiator and the recipient.</summary>
        public int Between;
        /// <summary>The effect on how the two families stand, when the moment moves that.</summary>
        public int InterHouse;
        public bool ViewerInvolved;
        /// <summary>The effect to show for this perspective: the viewer's own when they were part of it, otherwise the pair's.</summary>
        public int ForPerspective => ViewerInvolved ? OnViewer : Between;
    }

    public static class StoryValence
    {
        private static int TierSign(EventTier t) => t == EventTier.Poor ? -1 : t == EventTier.Flat ? 0 : 1;

        /// <summary>A person's own side of a moment: how it went (poor, flat, good) nudged by how they walked away feeling.</summary>
        private static int Side(EventTier tier, int feel)
        {
            int s = TierSign(tier);
            if (s == 0) return feel >= 20 ? 1 : feel <= -20 ? -1 : 0;
            if (s > 0 && feel <= -40) return 0;
            if (s < 0 && feel >= 40) return 0;
            return s;
        }

        public static Valence Of(EventRecord e, int viewer)
        {
            var v = new Valence();
            if (e == null) return v;
            var def = EventCatalog.Get(e.EventId);
            v.Between = StoryConsolidation.Outcome(e);
            v.InterHouse = def != null && (def.Effects & EventEffect.TiesFamilies) != 0 ? TierSign(e.Tier) : 0;
            if (viewer == 0) return v;
            if (viewer == e.A) { v.ViewerInvolved = true; v.OnViewer = Side(e.Tier, e.FeelA); }
            else if (viewer == e.B) { v.ViewerInvolved = true; v.OnViewer = Side(e.Tier, e.FeelB); }
            else if (viewer == e.Third)
            {
                v.ViewerInvolved = true;
                // Someone stood against the third person, or took the other's side: that is never good for them, however well it went for the pair.
                v.OnViewer = def != null && (def.Effects & EventEffect.Confronts) != 0 ? -1 : 0;
            }
            return v;
        }

        /// <summary>The effect of a run of moments (one told scene) for this perspective: the sign of their sum.</summary>
        public static int ForBeat(IEnumerable<EventRecord> parts, int viewer) => Math.Sign(parts.Sum(p => Of(p, viewer).ForPerspective));

        /// <summary>The net effect, on the viewer, of the moments they shared with one person.</summary>
        public static int NetWith(IEnumerable<EventRecord> events, int viewer, int other) =>
            events.Where(e => e.A == other || e.B == other || e.Third == other).Sum(e => Of(e, viewer).OnViewer);
    }

    public static class RelationshipWords
    {
        /// <summary>
        /// A known relationship in a word the viewer is entitled to, taken from what they lived through with that person (and only then, when there is nothing, from the
        /// feelings they hold). One definition, so the chapter, the story and the relationship list agree.
        /// </summary>
        public static string With(FamilyRegistry r, int viewer, int other, RelRow row, IList<EventRecord> history)
        {
            var shared = history.Where(e => e.A == other || e.B == other || e.Third == other).ToList();
            if (shared.Count > 0)
            {
                int net = StoryValence.NetWith(shared, viewer, other);
                if (net <= -2) return "Strained";
                if (net < 0) return "Uneasy";
                if (net >= 2) return "Warm";
                if (net > 0) return row.Trust >= 60f ? "Trusting" : "Warm";
                return Knowledge.Describe(row) == "Unfamiliar" ? "Uncertain" : Knowledge.Describe(row);
            }
            return Knowledge.Describe(row);
        }

        /// <summary>The overall tone of a person's dealings with a House: the total of how each moment went for them.</summary>
        public static string ToneOf(IEnumerable<EventRecord> history, int viewer)
        {
            int good = 0, bad = 0;
            foreach (var e in history) { int s = StoryValence.Of(e, viewer).OnViewer; if (s > 0) good++; else if (s < 0) bad++; }
            return bad > good ? "Uneasy" : good > bad ? "Warm" : "Mixed";
        }
    }

    /// <summary>One way of writing how two people are related, as a sentence about both.</summary>
    public static class RelationPhrase
    {
        public static Narrative Of(FamilyRegistry r, int a, int b)
        {
            var pa = r.GetPerson(a); var pb = r.GetPerson(b);
            string na = HouseholdNames.Short(pa), nb = HouseholdNames.Short(pb);
            if (pa == null || pb == null) return new Narrative("WFL_FW_RelP_Known", na, nb);
            if (pb.ParentIds.Contains(a)) return new Narrative("WFL_FW_RelP_ParentOf", na, nb);
            if (pa.ParentIds.Contains(b)) return new Narrative("WFL_FW_RelP_ParentOf", nb, na);
            if (pa.PartnerIds().Contains(b) || pb.PartnerIds().Contains(a)) return new Narrative("WFL_FW_RelP_Partners", na, nb);
            if (pa.ExIds.Contains(b) || pb.ExIds.Contains(a)) return new Narrative("WFL_FW_RelP_Former", na, nb);
            if (pa.ParentIds.Intersect(pb.ParentIds).Any()) return new Narrative("WFL_FW_RelP_Siblings", na, nb);
            return new Narrative("WFL_FW_RelP_Known", na, nb);
        }
    }

    // ------------------------------------------------------------------ beds and sleeping spots

    public enum BedKind : byte { Bed, Crib, Bedroll, Medical }

    public struct BedObject
    {
        public int ThingId, Slots, OwnerId;
        public BedKind Kind;
    }

    public struct SleepCounts
    {
        /// <summary>Beds, cribs and bedrolls as objects: a double bed is one bed.</summary>
        public int Beds;
        /// <summary>Places to sleep: a double bed is two.</summary>
        public int SleepingSpots;
        public int MedicalBeds, Unassigned;

        public static SleepCounts Of(IEnumerable<BedObject> objects)
        {
            var c = new SleepCounts();
            var seen = new HashSet<int>();
            foreach (var o in objects)
            {
                if (!seen.Add(o.ThingId)) continue;       // two residents sharing one bed are one bed
                if (o.Kind == BedKind.Medical) { c.MedicalBeds++; continue; }
                c.Beds++;
                c.SleepingSpots += Math.Max(1, o.Slots);
                if (o.OwnerId == 0) c.Unassigned++;
            }
            return c;
        }
    }

    // ------------------------------------------------------------------ suggestions a pawn could plausibly act on

    /// <summary>A person's skill level for a House capability, or -1 when they cannot do that kind of work or it is not known.</summary>
    public delegate int SkillProbe(int personId, HouseCapability capability);

    public static class ActionAdvice
    {
        public const int TeachAt = 8;

        /// <summary>
        /// Keeps only the suggestions this person could plausibly attempt, and names who they would involve. Nothing is suggested merely because it might help in general.
        /// The colony overview (viewer 0) is not a person and gets the House's general suggestions unchanged.
        /// </summary>
        public static void Apply(FamilyRegistry r, Lineage house, int viewer, List<StoryThread> threads, SkillProbe probe, IList<EventRecord> history)
        {
            if (viewer == 0) return;
            var me = r.GetPerson(viewer);
            foreach (var t in threads)
            {
                var actions = new List<Narrative>();
                foreach (var a in t.Actions)
                {
                    switch (a.Key)
                    {
                        case "WFL_FW_Act_TimeTogether":
                            { var p = Target(r, me, t, false, history); if (p != null) actions.Add(new Narrative("WFL_FW_Act_TimeWith", HouseholdNames.Short(p))); break; }
                        case "WFL_FW_Act_Apologize":
                            { var p = Target(r, me, t, true, history); if (p != null) actions.Add(new Narrative("WFL_FW_Act_ApologizeTo", HouseholdNames.Short(p))); break; }
                        case "WFL_FW_Act_Aid":
                            { var p = Target(r, me, t, false, history); if (p != null) actions.Add(new Narrative("WFL_FW_Act_AidTo", HouseholdNames.Short(p))); break; }
                        case "WFL_FW_Act_Mentor":
                            {
                                var kids = r.Persons.Where(x => x.InColony && x.Juvenile && r.BelongsToFamily(x, house.Id) && x.Id != viewer).Take(3).ToList();
                                if (me != null && me.InColony && !me.Juvenile && kids.Count > 0) actions.Add(new Narrative("WFL_FW_Act_MentorNamed", string.Join(", ", kids.Select(HouseholdNames.Short))));
                                break;
                            }
                        case "WFL_FW_Th_Capability_Act1":
                        case "WFL_FW_Amb_FillSkillGap_Act1":
                        case "WFL_FW_Amb_FillSkillGap_Act2":
                            foreach (var n in SkillActions(r, house, viewer, house.Situation?.WeakestCapability ?? HouseCapability.None, probe)) if (!actions.Any(x => x.Key == n.Key && x.Args.SequenceEqual(n.Args))) actions.Add(n);
                            break;
                        default: actions.Add(a); break;
                    }
                }
                t.Actions = actions;
            }
        }

        private static PersonRecord Target(FamilyRegistry r, PersonRecord me, StoryThread t, bool needGrievance, IList<EventRecord> history)
        {
            foreach (var id in t.People)
            {
                if (id == (me?.Id ?? 0)) continue;
                var p = r.GetPerson(id);
                if (p == null || !p.InColony) continue;
                if (me != null && me.Juvenile != p.Juvenile && r.KindOf(me, p) == null) continue;       // across the age gap only between kin
                if (needGrievance && !HasGrievance(r, me, p, history)) continue;
                return p;
            }
            return null;
        }

        /// <summary>A known reason to apologise: a moment that went badly between them, or an open rift.</summary>
        public static bool HasGrievance(FamilyRegistry r, PersonRecord me, PersonRecord other, IList<EventRecord> history)
        {
            if (me == null) return false;
            if (r.OpenRiftBetween(me.Id, other.Id) != null) return true;
            return history != null && history.Any(e => (e.A == other.Id || e.B == other.Id || e.Third == other.Id) && StoryValence.Of(e, me.Id).OnViewer < 0);
        }

        private static IEnumerable<Narrative> SkillActions(FamilyRegistry r, Lineage house, int viewer, HouseCapability cap, SkillProbe probe)
        {
            if (probe == null || cap == HouseCapability.None) yield break;
            string capKey = "WFL_Capability_" + cap;
            int mine = probe(viewer, cap);
            var members = r.Persons.Where(p => p.InColony && p.Id != viewer && r.BelongsToFamily(p, house.Id)).ToList();
            if (mine >= TeachAt)
            {
                var learner = members.FirstOrDefault(p => !p.Juvenile && probe(p.Id, cap) >= 0 && probe(p.Id, cap) < TeachAt);
                if (learner != null) yield return new Narrative("WFL_FW_Act_Teach", HouseholdNames.Short(learner), capKey);
            }
            else if (mine >= 0)
            {
                var teacher = members.FirstOrDefault(p => probe(p.Id, cap) >= TeachAt);
                if (teacher != null) yield return new Narrative("WFL_FW_Act_Learn", capKey, HouseholdNames.Short(teacher));
            }
            var skilled = r.Persons.FirstOrDefault(p => p.InColony && !p.Juvenile && p.Id != viewer && !r.BelongsToFamily(p, house.Id) && probe(p.Id, cap) >= TeachAt);
            if (skilled != null) yield return new Narrative("WFL_FW_Act_Befriend", HouseholdNames.Short(skilled), capKey);
        }
    }
}
