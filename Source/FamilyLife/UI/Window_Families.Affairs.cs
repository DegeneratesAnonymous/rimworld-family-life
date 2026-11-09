using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>The perspective-aware parts of House Affairs: what a private House says to an outsider, how Houses stand with each other, and community influence.</summary>
    public partial class Window_Families
    {
        /// <summary>A card saying what the viewer does not know and why, so a private section is explained instead of left blank.</summary>
        private void DrawPrivacyCard(Rect col, ref float y, string key, string titleKey, string bodyKey, FamilyRegistry registry)
        {
            var card = FamilyKit.BeginCard(col, ref y, key, titleKey.Translate());
            float cy = 0f;
            cy += FamilyKit.Label(card.View, cy, bodyKey.Translate(view.NameOfPerspective(registry), view.Name), 0f, true);
            FamilyKit.EndCard(card, cy, ref y);
        }

        /// <summary>Other Houses as cards. Houses see how they stand in numbers they can read; a visitor sees only their own House's dealings, in words.</summary>
        private void DrawDiplomacyCard(Rect col, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage house)
        {
            int viewer = component.PerspectivePawnId;
            var ties = registry.TiesOf(house.Id).Where(t => t.State != FamilyTieState.Strangers).ToList();
            if (!view.Insider)
            {
                int mine = registry.FamilyOf(viewer);
                ties = ties.Where(t => t.LineageA == mine || t.LineageB == mine).ToList();
            }
            var card = FamilyKit.BeginCard(col, ref y, "l.diplomacy", "WFL_FW_Card_Diplomacy".Translate());
            var cv = card.View;
            float cy = 0f;
            if (ties.Count == 0) FamilyKit.EmptyState(cv, ref cy, "WFL_FW_NoDiplomacy".Translate());
            foreach (var tie in ties.Take(6))
            {
                int otherId = tie.LineageA == house.Id ? tie.LineageB : tie.LineageA;
                var other = registry.GetLineage(otherId);
                if (other == null) continue;
                float cardTop = cy;
                Widgets.Label(new Rect(8f, cy, cv.width - 8f, 22f), "WFL_FW_HouseN".Translate(registry.LineageDisplayName(other)).ToString());
                cy += 22f;
                cy += FamilyKit.Label(cv, cy, ("WFL_FW_Tie_" + tie.State).Translate().ToString().CapitalizeFirst(), 14f, false);
                if (view.Insider) { cy += FamilyKit.Label(cv, cy, "WFL_FW_Goodwill".Translate(Mathf.RoundToInt(tie.Goodwill)), 14f, true); cy += FamilyKit.Label(cv, cy, "WFL_FW_Grievance".Translate(Mathf.RoundToInt(tie.Grievance)), 14f, true); }
                else
                {
                    var event0 = Knowledge.HistoryWith(registry, viewer, house).FirstOrDefault();
                    if (event0 != null) cy += FamilyKit.Label(cv, cy, "WFL_FW_RecentEvent".Translate(FamilyText.MomentTitle(event0)), 6f, true);
                }
                bool talks = registry.MarriageAlliances.Any(a => ((a.FamilyA == house.Id && a.FamilyB == otherId) || (a.FamilyB == house.Id && a.FamilyA == otherId)) && registry.KnowsMarriageAlliance(viewer, a)
                    && (a.State == MarriageAllianceState.Negotiating || a.State == MarriageAllianceState.Supported || a.State == MarriageAllianceState.Opposed || a.State == MarriageAllianceState.Consented));
                cy += FamilyKit.Label(cv, cy, (talks ? "WFL_FW_NegotiationActive" : "WFL_FW_NoNegotiation").Translate(), 6f, true);
                cy += 4f;
                Widgets.DrawBoxSolid(new Rect(0f, cardTop, 3f, cy - cardTop - 2f), tie.State == FamilyTieState.Feuding || tie.State == FamilyTieState.Strained ? FamilyKit.Tension : tie.State == FamilyTieState.Allied || tie.State == FamilyTieState.Friendly ? FamilyKit.Closeness : FamilyKit.Unknown);
            }
            FamilyKit.EndCard(card, cy, ref y);
        }

        /// <summary>Community offices held by this House's people are public, and they are what raises a House's standing.</summary>
        private void DrawCommunityInfluenceCard(Rect col, ref float y, FamilyRegistry registry)
        {
            var held = view.Roles.Where(r => !IsHouseOffice(r.Role) && !r.Hidden && r.HolderId != 0 && view.Relevant.Contains(r.HolderId)).ToList();
            var card = FamilyKit.BeginCard(col, ref y, "l.influence", "WFL_FW_Card_Influence".Translate());
            var cv = card.View;
            float cy = 0f;
            cy += FamilyKit.Label(cv, cy, "WFL_FW_InfluenceNote".Translate(), 0f, true);
            if (held.Count == 0) cy += FamilyKit.Label(cv, cy, "WFL_FW_InfluenceNone".Translate(view.Name), 6f);
            foreach (var r in held)
                cy += FamilyKit.Label(cv, cy, "WFL_FW_InfluenceHeld".Translate(FamilyUi.NameOf(registry, r.HolderId), RoleIcons.Label(r.Role)), 6f);
            var amb = view.House.Ambition;
            if (view.Insider && amb != null && amb.Primary == HouseAmbitionKind.IncreaseInfluence)
                cy += FamilyKit.Label(cv, cy + 2f, "WFL_FW_InfluenceWanted".Translate(), 0f, true);
            FamilyKit.EndCard(card, cy, ref y);
        }
    }
}
