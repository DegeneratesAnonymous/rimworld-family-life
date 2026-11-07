using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// What the game does about the life events the registry reports: small mood memories for the people concerned, a gathering
    /// request for a coming of age or a loss, a note when the family takes up a tradition. The registry decides that something
    /// happened; this only reacts to it, and only to events from this sweep.
    /// </summary>
    internal static class EventEffects
    {
        private const int DayTicks = ActivityTuning.TicksPerDay;

        public static void Apply(FamilyComponent component, List<FamilyEvent> events, List<Grief> griefs, int now)
        {
            var registry = component.Registry;
            var settings = FamilyLifeMod.Settings;
            bool gatherings = settings == null || settings.traditions;
            bool ceremonies=settings==null||settings.ceremonies;

            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case FamilyEventKind.MarriageFormed:
                        if(ceremonies)
                        {
                            int familyA=registry.FamilyOf(e.A),familyB=registry.FamilyOf(e.B);
                            int host=familyA!=0?familyA:familyB;
                            if(host!=0)registry.RequestCeremony(CeremonyKind.Wedding,host,e.A,e.B,now,DayTicks/8,4*DayTicks,
                                CeremonyInfluence.Engagement|(familyA!=0&&familyB!=0?CeremonyInfluence.StrongRelationships:CeremonyInfluence.None),
                                familyB!=0&&familyB!=host?familyB:0);
                        }
                        break;
                    case FamilyEventKind.CameOfAge:
                        Remember(component, e.A, "WFL_CameOfAge", 0);
                        if (gatherings) registry.RequestGathering("comingofage", registry.LineageIdFor(registry.GetPerson(e.A)), e.A, now, DayTicks / 4, 3 * DayTicks);
                        break;
                    case FamilyEventKind.Died:
                        if(ceremonies)
                        {
                            int family=registry.FamilyOf(e.A);
                            if(family!=0)registry.RequestCeremony(CeremonyKind.Funeral,family,e.A,0,now,DayTicks/2,4*DayTicks,CeremonyInfluence.RecentDeath);
                        }
                        break;
                    case FamilyEventKind.Anniversary:
                        Remember(component, e.A, "WFL_Anniversary", e.B);
                        Remember(component, e.B, "WFL_Anniversary", e.A);
                        break;
                    case FamilyEventKind.Reunion:
                        foreach (var bond in registry.Bonds.ToArray())
                            if (bond.To == e.A && bond.Attachment >= 40f) Remember(component, bond.From, "WFL_Reunion", e.A);
                        Remember(component, e.A, "WFL_Reunion", 0);
                        break;
                    case FamilyEventKind.RiftOpened:
                        Remember(component, e.A, "WFL_Estranged", e.B);
                        Remember(component, e.B, "WFL_Estranged", e.A);
                        break;
                    case FamilyEventKind.FamiliesJoined:
                    case FamilyEventKind.StepFamilyFormed:
                        AnnounceJoining(component, e);
                        break;
                    case FamilyEventKind.BondDeepened:
                    case FamilyEventKind.BondCooled:
                        AnnounceBond(component, e, settings);
                        break;
                    case FamilyEventKind.TraditionAdopted:
                        if (e.B >= 0 && e.B < TraditionCatalog.All.Length && registry.GetLineage(e.A) != null)
                            Messages.Message("WFL_Msg_TraditionAdopted".Translate(registry.LineageDisplayName(registry.GetLineage(e.A)),
                                ("WFL_Trad_" + TraditionCatalog.All[e.B].Id).Translate()), MessageTypeDefOf.PositiveEvent, false);
                        break;
                }
            }

            // A loss that people felt is something the family gathers about.
            if (ceremonies && griefs != null)
                foreach (var grief in griefs)
                {
                    if (grief.Attachment < 45f) continue;
                    int lineage = registry.LineageIdFor(registry.GetPerson(grief.LostId));
                    if (lineage != 0) registry.RequestCeremony(CeremonyKind.Funeral,lineage,grief.LostId,0,now,DayTicks/2,4*DayTicks,CeremonyInfluence.RecentDeath);
                }

            // Someone who is far away is missed while they are gone.
            if (settings == null || settings.familyActivities)
                foreach (var pair in registry.MissingPairs(now))
                    Remember(component, pair.Key, "WFL_Missing", pair.Value);
        }

        /// <summary>Two families have been joined by a partnership, or a step-family has formed. Said once, with the names.</summary>
        private static void AnnounceJoining(FamilyComponent component, FamilyEvent e)
        {
            var registry = component.Registry;
            var pawn = component.TryGetPawn(e.A) ?? component.TryGetPawn(e.B);
            if (pawn == null) return;
            string a = FamilyUi.NameOf(registry, e.A);
            string b = FamilyUi.NameOf(registry, e.B);
            string text = e.Kind == FamilyEventKind.FamiliesJoined
                ? "WFL_Msg_FamiliesJoined".Translate(a, b, FamilyUi.LineageName(registry, registry.GetPerson(e.A)?.PrimaryLineageId ?? 0),
                    FamilyUi.LineageName(registry, registry.GetPerson(e.B)?.PrimaryLineageId ?? 0)).ToString()
                : "WFL_Msg_StepFamily".Translate(a, b).ToString();
            Messages.Message(text, pawn, MessageTypeDefOf.PositiveEvent, false);
        }

        /// <summary>
        /// A bond has become close, or distant, because of something that can be named. Said in a sentence, once, near the people involved,
        /// instead of as a number the player would have to go and look up.
        /// </summary>
        private static void AnnounceBond(FamilyComponent component, FamilyEvent e, FamilySettings settings)
        {
            if (settings != null && !settings.bondMessages) return;
            var registry = component.Registry;
            var bond = registry.GetBond(e.A, e.B);
            var holder = component.TryGetPawn(e.A);
            if (bond == null || holder == null || holder.Dead) return;
            bool closer = e.Kind == FamilyEventKind.BondDeepened;
            var reason = bond.TopReason(closer);
            string why = reason == null ? "" : FamilyUi.WhyText(reason.Cause, closer);
            string a = FamilyUi.NameOf(registry, e.A);
            string b = FamilyUi.NameOf(registry, e.B);
            string text = closer ? "WFL_Msg_BondDeepened".Translate(a, b, why).ToString() : "WFL_Msg_BondCooled".Translate(a, b, why).ToString();
            Messages.Message(text, holder, closer ? MessageTypeDefOf.PositiveEvent : MessageTypeDefOf.NegativeEvent, false);
        }

        private static void Remember(FamilyComponent component, int personId, string thought, int otherId)
        {
            var pawn = component.TryGetPawn(personId);
            if (pawn == null || pawn.Dead) return;
            Activities.GainMemory(pawn, thought, otherId == 0 ? null : component.TryGetPawn(otherId));
        }
    }
}
