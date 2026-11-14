using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Lets the player mark which items and resources count as stored wealth, using the game's own item filter (categories, search and ticks) instead of a short fixed menu.
    /// Silver is always recognised as the liquid currency. Items from mods that are no longer loaded are left as they were.
    /// </summary>
    internal sealed class Dialog_Valuables : Window
    {
        private readonly TreasuryState state;
        private readonly ThingFilter filter = new ThingFilter();
        private readonly ThingFilterUI.UIState ui = new ThingFilterUI.UIState();
        public override Vector2 InitialSize => new Vector2(460f, 640f);

        public Dialog_Valuables(TreasuryState state)
        {
            this.state = state; forcePause = false; doCloseX = true; doCloseButton = true; closeOnClickedOutside = false; absorbInputAroundWindow = false;
            filter.SetDisallowAll();
            foreach (var v in state.Valuables.All) { var d = DefDatabase<ThingDef>.GetNamedSilentFail(v.DefName); if (d != null) filter.SetAllow(d, true); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium; Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "WFL_Tr_ChooseValuables".Translate()); Text.Font = GameFont.Small;
            GUI.color = Color.gray; Widgets.Label(new Rect(0f, 34f, inRect.width, 44f), "WFL_Tr_ValuablesHint".Translate()); GUI.color = Color.white;
            var silver = ThingDefOf.Silver; if (silver != null && !filter.Allows(silver)) filter.SetAllow(silver, true);   // the currency can never be switched off
            var area = new Rect(0f, 82f, inRect.width, inRect.height - 82f - Window.CloseButSize.y - 6f);
            ThingFilterUI.DoThingFilterConfigWindow(area, ui, filter, null, 1, silver == null ? null : new[] { silver });
            Sync();
        }

        /// <summary>Makes the recognised list match the filter: ticked items are added (as stored wealth, not spendable silver) and unticked ones removed.</summary>
        private void Sync()
        {
            var v = state.Valuables;
            foreach (var d in filter.AllowedThingDefs) if (d != null && d.category == ThingCategory.Item && !v.Recognises(d.defName)) v.Set(d.defName, false);
            foreach (var known in v.All.ToList())
            {
                if (known.DefName == "Silver") continue;
                var d = DefDatabase<ThingDef>.GetNamedSilentFail(known.DefName);
                if (d != null && !filter.Allows(d)) v.Remove(known.DefName);
            }
        }
    }
}
