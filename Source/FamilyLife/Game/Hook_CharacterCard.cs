using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Makes a colonist's surname on their character card a link to their family. The card draws the whole name as one label,
    /// so the surname's position is measured from the same text and font the game uses.
    /// </summary>
    [HarmonyPatch(typeof(CharacterCardUtility), nameof(CharacterCardUtility.DrawCharacterCard))]
    internal static class Hook_CharacterCardSurname
    {
        private static void Postfix(Rect rect, Pawn pawn, Action randomizeCallback, bool showName)
        {
            if (randomizeCallback != null || !showName || pawn == null) return;     // not the pawn-creation screen
            if (!(pawn.Name is NameTriple name) || string.IsNullOrEmpty(name.Last)) return;
            var component = FamilyComponent.Current;
            var record = component?.Registry.GetPerson(pawn.thingIDNumber);
            if (record == null) return;
            int lineageId = component.Registry.FamilyToShow(record);
            if (lineageId == 0) return;

            var oldFont = Text.Font;
            Text.Font = GameFont.Medium;
            string text = pawn.Name.ToStringFull.CapitalizeFirst();
            int at = text.LastIndexOf(name.Last, StringComparison.OrdinalIgnoreCase);
            Rect hit = default;
            bool found = at >= 0;
            if (found)
                hit = new Rect(rect.x + Text.CalcSize(text.Substring(0, at)).x, rect.y, Text.CalcSize(name.Last).x, 30f);
            float nameEnd = rect.x + Text.CalcSize(text).x;
            Text.Font = oldFont;

            string family = component.Registry.LineageDisplayName(component.Registry.GetLineage(lineageId));
            if (found)
            {
                Widgets.DrawHighlightIfMouseover(hit);
                TooltipHandler.TipRegion(hit, "WFL_OpenFamilyTip".Translate(family));
                if (Widgets.ButtonInvisible(hit)) FamilyUi.OpenFamilies(lineageId);
            }

            // A Family button after the name, only where it cannot run into the game's own icons at the top right of the card.
            float guiltyGap = pawn.guilt != null && pawn.guilt.IsGuilty ? 40f : 0f;
            var button = new Rect(nameEnd + 14f + guiltyGap, rect.y + 1f, 72f, 28f);
            float limit = rect.x + CharacterCardUtility.PawnCardSize(pawn).x - 170f;
            if (button.xMax <= limit)
            {
                TooltipHandler.TipRegion(button, "WFL_OpenFamilyTip".Translate(family));
                if (Widgets.ButtonText(button, "WFL_FamilyButton".Translate())) FamilyUi.OpenFamilies(lineageId);
            }
        }
    }
}
