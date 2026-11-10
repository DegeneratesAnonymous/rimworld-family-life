using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FamilyLife
{
    // One exchange between a professional (a doctor, a teacher) and the person they are talking to. Owner: the framework; Education and Community Care both use it.

    internal struct ExchangeResult { public EventTier Tier; public float Quality, Scale; }

    /// <summary>
    /// One exchange between a doctor or teacher and the person they are talking to. It is cooperative: how it goes depends on how well the professional delivers it (their skill
    /// in the subject, their Social, the room, their tiredness), a small part from the other person being able to say what they need, and a little from the day. The
    /// relationship changes only through <see cref="FamilyRegistry.ProfessionalExchange"/>, which limits it per pair per day.
    /// </summary>
    internal static class Conversations
    {
        internal static ExchangeResult Run(Pawn leader, Pawn other, OfficeKind kind, SkillDef subject, ExchangeTopic topic, int seed, float facility = 0f)
        {
            var c = FamilyComponent.Current; var r = c?.Registry;
            float delivery = CooperativeExchange.Delivery(PawnReadiness.Level(leader, subject), PawnReadiness.Level(leader, SkillDefOf.Social), 0f, facility, PawnReadiness.Fatigue(leader));
            var tier = CooperativeExchange.Tier(delivery, CooperativeExchange.Difficulty(topic), CooperativeExchange.Engagement(PawnReadiness.Level(other, SkillDefOf.Social)), Rand.RangeInclusive(-2, 2));
            int now = Activities.Now;
            float scale = r != null ? r.ProfessionalExchange(kind, leader.thingIDNumber, other.thingIDNumber, topic, tier, now) : 1f;
            leader.skills?.Learn(SkillDefOf.Social, (3f + 4f * Math.Max(0, GuidedExchanges.TierValue(tier))) * scale, false);          // nothing once the day's contact with this person is used up
            var tint = tier == EventTier.Poor ? new Color(1f, .72f, .6f) : tier == EventTier.Great ? new Color(.72f, 1f, .72f) : Color.white;
            string stem = "WFL_Ex_" + topic + "_" + tier;
            MoteMaker.ThrowText(leader.DrawPos, leader.Map, Phrasing.Say(stem + "_Lead", seed), tint);
            MoteMaker.ThrowText(other.DrawPos + new Vector3(0f, 0f, -.35f), other.Map, Phrasing.Say(stem + "_Reply", seed + 7), tint);
            return new ExchangeResult { Tier = tier, Quality = GuidedExchanges.TierValue(tier) * GuidedExchanges.Weight(topic), Scale = scale };
        }
    }
}
