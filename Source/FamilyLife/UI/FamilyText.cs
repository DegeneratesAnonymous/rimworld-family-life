using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>The words for what the window's view models describe. Kept out of the drawing code so every tab says the same thing the same way.</summary>
    internal static class FamilyText
    {
        private static readonly Dictionary<int, string> dates = new Dictionary<int, string>();

        public static void ForgetDates() { dates.Clear(); }

        /// <summary>A date for a tick, worked out once and remembered.</summary>
        public static string Date(int tick)
        {
            if (dates.TryGetValue(tick, out var s)) return s;
            if (dates.Count > 600) dates.Clear();
            return dates[tick] = FamilyUi.Date(tick);
        }

        public static string AlertText(FamilyRegistry r, FamilyAlert a)
        {
            string key = "WFL_FW_Alert_" + a.Kind;
            if (a.Kind == AlertKind.OpenRift)
            {
                var rift = r.OpenRiftsOf(a.PersonId).FirstOrDefault();
                if (rift != null) return key.Translate(FamilyUi.NameOf(r, rift.A), FamilyUi.NameOf(r, rift.B)).ToString();
            }
            return key.Translate(a.Count).ToString();
        }

        public static string AlertTip(FamilyAlert a) => ("WFL_FW_AlertTip_" + a.Kind).Translate();

        public static string UpcomingText(FamilyRegistry r, UpcomingItem u)
        {
            switch (u.Kind)
            {
                case UpcomingKind.Ceremony:
                    {
                        string name = ("WFL_Ceremony_" + u.Ceremony).Translate();
                        return u.PersonA != 0 ? "WFL_FW_Up_CeremonyFor".Translate(name, FamilyUi.NameOf(r, u.PersonA)).ToString() : name;
                    }
                case UpcomingKind.Tradition: return FamilyUi.TraditionNameById(u.TraditionId);
                case UpcomingKind.Promise: return "WFL_FW_Up_Promise".Translate(FamilyUi.NameOf(r, u.PersonA), FamilyUi.NameOf(r, u.PersonB)).ToString();
                case UpcomingKind.KinVisit: return "WFL_FW_Up_Kin".Translate(u.Count).ToString();
                case UpcomingKind.CampTrip: return u.PersonA != 0 ? "WFL_FW_Up_CampBy".Translate(FamilyUi.NameOf(r, u.PersonA)).ToString() : "WFL_FW_Up_Camp".Translate().ToString();
                default: return "WFL_FW_Up_Marriage".Translate(FamilyUi.NameOf(r, u.PersonA), FamilyUi.NameOf(r, u.PersonB)).ToString();
            }
        }

        public static string UpcomingWhen(UpcomingItem u) => u.Tick <= 0 ? "WFL_FW_Up_Soon".Translate().ToString() : u.Overdue ? "WFL_FW_Up_Overdue".Translate(Date(u.Tick)).ToString() : Date(u.Tick);

        public static string UpcomingType(UpcomingItem u) => ("WFL_FW_UpKind_" + u.Kind).Translate();

        public static string MomentTitle(EventRecord e) => ("WFL_Ev_" + e.EventId.Replace('-', '_') + "_Title").Translate();

        /// <summary>The words for a moment between two people, or null if the moment's definition is no longer known.</summary>
        public static string MomentBody(FamilyRegistry r, EventRecord e)
        {
            var def = EventCatalog.Get(e.EventId) ?? TraditionMoments.Get(e.EventId) ?? CeremonyMoments.Get(e.EventId);
            if (def == null) return null;
            return SocialMoments.Describe(def, e.Tier, FamilyUi.NameOf(r, e.A), FamilyUi.NameOf(r, e.B), e.Third != 0 ? FamilyUi.NameOf(r, e.Third) : "");
        }

        public static string TierWords(EventTier tier) => ("WFL_FW_Tier_" + tier).Translate();

        /// <summary>What came of a moment, told as a short sentence: how each person felt afterwards and what it set going.
        /// Moments saved before this was recorded fall back to the plain tier words.</summary>
        public static string Aftermath(FamilyRegistry r, EventRecord e)
        {
            string a = FamilyUi.NameOf(r, e.A), b = FamilyUi.NameOf(r, e.B);
            var parts = new List<string>(4);
            string fa = Feeling(e.FeelA), fb = Feeling(e.FeelB);
            if (fa != null && fb != null && fa == fb) parts.Add(("WFL_FW_Feel_Both_" + fa).Translate(a, b).ToString());
            else
            {
                if (fa != null) parts.Add(("WFL_FW_Feel_One_" + fa).Translate(a).ToString());
                if (fb != null) parts.Add(("WFL_FW_Feel_One_" + fb).Translate(b).ToString());
            }
            var f = (AftermathFlags)e.Aftermath;
            if ((f & AftermathFlags.RiftOpened) != 0) parts.Add("WFL_FW_After_RiftOpened".Translate(a, b).ToString());
            if ((f & AftermathFlags.RiftResolved) != 0) parts.Add("WFL_FW_After_RiftResolved".Translate(a, b).ToString());
            else if ((f & AftermathFlags.RiftEased) != 0) parts.Add("WFL_FW_After_RiftEased".Translate(a, b).ToString());
            if ((f & AftermathFlags.PromiseMade) != 0) parts.Add("WFL_FW_After_PromiseMade".Translate(a, b).ToString());
            if ((f & AftermathFlags.PromiseKept) != 0) parts.Add("WFL_FW_After_PromiseKept".Translate(a, b).ToString());
            if ((f & AftermathFlags.TieChanged) != 0) parts.Add("WFL_FW_After_TieChanged".Translate().ToString());
            if ((f & AftermathFlags.Memory) != 0) parts.Add("WFL_FW_After_Memory".Translate().ToString());
            return parts.Count == 0 ? TierWords(e.Tier).ToString() : string.Join(" ", parts);
        }

        private static string Feeling(int v) => v >= 60 ? "Glad" : v >= 20 ? "Warm" : v <= -60 ? "Hurt" : v <= -20 ? "Uneasy" : null;

        public static string CeremonyBody(FamilyRegistry r, CeremonyRecord c) =>
            "WFL_Ceremony_Recent".Translate(Date(c.CompletedTick), ("WFL_Ceremony_" + c.Kind).Translate(), c.Attendees, c.Moments);

        public static string IncidentBody(FamilyRegistry r, FamilyIncident i, int viewer)
        {
            string actor = i.ActorId == 0 ? "WFL_Stories_Unknown".Translate().ToString() : FamilyUi.NameOf(r, i.ActorId);
            return viewer == 0
                ? "WFL_FW_IncidentLine".Translate(("WFL_Incident_" + i.Kind).Translate(), actor, FamilyUi.NameOf(r, i.VictimId)).ToString()
                : "WFL_FW_IncidentLinePrivate".Translate(("WFL_Incident_" + i.Kind).Translate(), actor, FamilyUi.NameOf(r, i.VictimId)).ToString();
        }

        public static string PlotBody(FamilyRegistry r, RevengePlot p) =>
            "WFL_Stories_Plot".Translate(FamilyUi.NameOf(r, p.OrganizerId), FamilyUi.NameOf(r, p.TargetId), ("WFL_PlotState_" + p.State).Translate(), p.IncidentId, p.Recruits.Count);

        public static string CategoryLabel(EventCategory c) => ("WFL_FW_Cat_" + c).Translate();

        /// <summary>Who an entry is about, for the small line under its title.</summary>
        public static string Participants(FamilyRegistry r, TimelineEntry t)
        {
            switch (t.Kind)
            {
                case TimelineKind.Moment:
                    {
                        var e = t.Moment;
                        string s = FamilyUi.NameOf(r, e.A) + ", " + FamilyUi.NameOf(r, e.B);
                        return e.Third != 0 ? s + ", " + FamilyUi.NameOf(r, e.Third) : s;
                    }
                case TimelineKind.Ceremony:
                    return t.Ceremony.SubjectId != 0 ? FamilyUi.NameOf(r, t.Ceremony.SubjectId) : FamilyUi.LineageName(r, t.Ceremony.LineageId);
                case TimelineKind.Incident: return FamilyUi.NameOf(r, t.Incident.ActorId) + ", " + FamilyUi.NameOf(r, t.Incident.VictimId);
                case TimelineKind.Plot: return FamilyUi.NameOf(r, t.Plot.OrganizerId) + ", " + FamilyUi.NameOf(r, t.Plot.TargetId);
                default: return t.Chronicle.A != 0 && t.Chronicle.Kind != FamilyEventKind.TraditionAdopted && t.Chronicle.Kind != FamilyEventKind.TraditionAbandoned && t.Chronicle.Kind != FamilyEventKind.TraditionHeld
                    ? FamilyUi.NameOf(r, t.Chronicle.A) + (t.Chronicle.B != 0 && r.GetPerson(t.Chronicle.B) != null ? ", " + FamilyUi.NameOf(r, t.Chronicle.B) : "") : "";
            }
        }
    }
}
