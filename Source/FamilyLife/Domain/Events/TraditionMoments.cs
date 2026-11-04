using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>Forty-eight gathering episodes. Conditions refer to observed or recorded circumstances.</summary>
    public static class TraditionMoments
    {
        public static readonly List<EventDef> All = Build();
        public static EventDef Get(string id) => All.FirstOrDefault(d => d.Id == id);
        private static List<EventDef> Build()
        {
            var result = new List<EventDef>();
            result.Add(Scene("T1-01", "feast", Dim.Hearth, "Outside", "Warm"));
            result.Add(Scene("T1-02", "feast", Dim.Hearth, "Supplies", "Clash"));
            result.Add(Scene("T1-03", "feast", Dim.Hearth, "None", "Trust"));
            result.Add(Scene("T1-04", "feast", Dim.Hearth, "Grief", "Warm"));
            result.Add(Scene("T1-05", "feast", Dim.Hearth, "Values", "Clash"));
            result.Add(Scene("T1-06", "feast", Dim.Hearth, "Rift", "Repair"));
            result.Add(Scene("T2-01", "reading", Dim.Scholarly, "Skill", "Warm"));
            result.Add(Scene("T2-02", "reading", Dim.Scholarly, "Unknown", "Familiar"));
            result.Add(Scene("T2-03", "reading", Dim.Scholarly, "Achievement", "Warm"));
            result.Add(Scene("T2-04", "reading", Dim.Scholarly, "Values", "Clash"));
            result.Add(Scene("T2-05", "reading", Dim.Scholarly, "Memory", "Familiar"));
            result.Add(Scene("T2-06", "reading", Dim.Scholarly, "Wrongdoing", "Disclosure"));
            result.Add(Scene("T3-01", "hunt", Dim.Martial, "Skill", "Trust"));
            result.Add(Scene("T3-02", "hunt", Dim.Martial, "Harm", "Clash"));
            result.Add(Scene("T3-03", "hunt", Dim.Martial, "Values", "Clash"));
            result.Add(Scene("T3-04", "hunt", Dim.Martial, "Outside", "Warm"));
            result.Add(Scene("T3-05", "hunt", Dim.Martial, "None", "Trust"));
            result.Add(Scene("T3-06", "hunt", Dim.Martial, "Rift", "Repair"));
            result.Add(Scene("T4-01", "craftfair", Dim.Craft, "Skill", "Warm"));
            result.Add(Scene("T4-02", "craftfair", Dim.Craft, "Achievement", "Trust"));
            result.Add(Scene("T4-03", "craftfair", Dim.Craft, "Supplies", "Clash"));
            result.Add(Scene("T4-04", "craftfair", Dim.Craft, "Values", "Clash"));
            result.Add(Scene("T4-05", "craftfair", Dim.Craft, "Unknown", "Familiar"));
            result.Add(Scene("T4-06", "craftfair", Dim.Craft, "Skill", "Trust"));
            result.Add(Scene("T5-01", "party", Dim.Revelry, "Unknown", "Familiar"));
            result.Add(Scene("T5-02", "party", Dim.Revelry, "Achievement", "Warm"));
            result.Add(Scene("T5-03", "party", Dim.Revelry, "Values", "Clash"));
            result.Add(Scene("T5-04", "party", Dim.Revelry, "Outside", "Warm"));
            result.Add(Scene("T5-05", "party", Dim.Revelry, "Supplies", "Clash"));
            result.Add(Scene("T5-06", "party", Dim.Revelry, "Rift", "Repair"));
            result.Add(Scene("T6-01", "vigil", Dim.Faith, "Grief", "Warm"));
            result.Add(Scene("T6-02", "vigil", Dim.Faith, "Wrongdoing", "Disclosure"));
            result.Add(Scene("T6-03", "vigil", Dim.Faith, "Rift", "Repair"));
            result.Add(Scene("T6-04", "vigil", Dim.Faith, "Values", "Clash"));
            result.Add(Scene("T6-05", "vigil", Dim.Faith, "Outside", "Trust"));
            result.Add(Scene("T6-06", "vigil", Dim.Faith, "Memory", "Familiar"));
            result.Add(Scene("T7-01", "foundersday", Dim.Status, "Achievement", "Trust"));
            result.Add(Scene("T7-02", "foundersday", Dim.Status, "Newcomer", "Warm"));
            result.Add(Scene("T7-03", "foundersday", Dim.Status, "Memory", "Clash"));
            result.Add(Scene("T7-04", "foundersday", Dim.Status, "Values", "Clash"));
            result.Add(Scene("T7-05", "foundersday", Dim.Status, "Supplies", "Clash"));
            result.Add(Scene("T7-06", "foundersday", Dim.Status, "Rift", "Repair"));
            result.Add(Scene("T8-01", "reunion", Dim.Devotion, "Unknown", "Familiar"));
            result.Add(Scene("T8-02", "reunion", Dim.Devotion, "Outside", "Warm"));
            result.Add(Scene("T8-03", "reunion", Dim.Devotion, "Promise", "Disclosure"));
            result.Add(Scene("T8-04", "reunion", Dim.Devotion, "Values", "Clash"));
            result.Add(Scene("T8-05", "reunion", Dim.Devotion, "Memory", "Warm"));
            result.Add(Scene("T8-06", "reunion", Dim.Devotion, "Rift", "Repair"));
            return result;
        }
        private static EventDef Scene(string id, string tradition, Dim value, string need, string style)
        {
            return new EventDef
            {
                Id = id, Key = id, RequiredTradition = tradition, AnyParticipants = true,
                Bucket = EventBucket.IntraFamily, Scenario = EventScenario.AdultAdult,
                Weight = EventWeight.Meaningful, CooldownDays = 4, Skill = "Social", SkillShare = 0.3f,
                Motive = new Drive(0.45f), Reception = new Drive(0.45f, Factor.RecipientTrust, 0.25f, Factor.RecipientTension, -0.2f, Factor.RecipientWarmth, 0.1f),
                Opportunity = new[] { new Cond(need, c => Fits(need, c)) },
                Outcomes = Outcomes(style),
                ConcernsPromise = need == "Promise",
                Effects = (id == "T1-03" || id == "T3-05" || id == "T4-06") ? EventEffect.Promise : (style == "Repair" ? EventEffect.EasesRift : EventEffect.None),
                ValueAffinity = value
            };
        }
        private static bool Fits(string need, PairContext c)
        {
            switch (need)
            {
                case "Outside": return !c.SameFamily && !c.Related;
                case "Supplies": return c.ResourcePressure;
                case "Unknown": return c.FamiliarityBetween < 25;
                case "Grief": return c.Initiator.Grieving || c.Recipient.Grieving;
                case "Skill": return c.Initiator.Skills.Any(k => c.Recipient.Skills.ContainsKey(k.Key) && k.Value - c.Recipient.Skill(k.Key) >= 3);
                case "Achievement": return c.Initiator.Achievement != null || c.Recipient.Achievement != null;
                case "Values": return new[] { Dim.Warmth, Dim.Steadiness, Dim.Devotion, Dim.Status }.Any(d => Math.Abs(c.Initiator.Axis(d) - c.Recipient.Axis(d)) >= 0.6f);
                case "Memory": return c.Registry.Memories.Any(m => m.LineageId == c.Initiator.LineageId || m.LineageId == c.Recipient.LineageId);
                case "Promise": return c.Broken != null;
                case "Rift": return c.Rift != null || c.Registry.Incidents.Any(i => !i.Resolved && i.ActorId == c.Initiator.Id && i.VictimFamily == c.Recipient.LineageId && i.KnownBy.Contains(c.Recipient.Id));
                case "Wrongdoing": return c.Registry.Incidents.Any(i => !i.Resolved && i.ActorId == c.Initiator.Id && i.KnownBy.Contains(c.Recipient.Id));
                case "Harm": return c.Registry.Incidents.Any(i => (i.Kind == IncidentKind.Death || i.Kind == IncidentKind.Injury) && i.KnownBy.Contains(c.Initiator.Id) && i.KnownBy.Contains(c.Recipient.Id));
                case "Newcomer": return c.Initiator.Newcomer || c.Recipient.Newcomer;
                default: return true;
            }
        }
        private static MomentOutcome[] Outcomes(string style)
        {
            BondChange poor, flat, good, great, receiving;
            poor = new BondChange(-1, -2, 4, 1); flat = new BondChange(0, 0, 1, 1);
            good = new BondChange(1, 1, -1, 2); great = new BondChange(3, 2, -3, 2); receiving = great;
            if (style == "Trust") { good = new BondChange(0, 2, 0, 1); great = receiving = new BondChange(1, 5, -1, 1); }
            if (style == "Familiar") { good = new BondChange(0, 0, 0, 3); great = receiving = new BondChange(0, 1, 0, 5); }
            if (style == "Clash" || style == "Repair") { poor = new BondChange(-2, -3, 6, 2); flat = new BondChange(0, -1, 2, 1); great = receiving = new BondChange(1, 1, -5, 2); }
            if (style == "Disclosure") { great = new BondChange(2, 1, -3, 1); receiving = new BondChange(0, -2, -1, 3); }
            return new[] { new MomentOutcome(poor, poor), new MomentOutcome(flat, flat), new MomentOutcome(good, good), new MomentOutcome(great, receiving) };
        }
    }
}
