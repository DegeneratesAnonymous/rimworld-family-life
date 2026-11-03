using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>A pawn's own view of their family: lineage, household, parents, partners, children and siblings.</summary>
    public class ITab_Pawn_Family : ITab
    {
        private Vector2 scroll;
        private float contentHeight = 300f;

        public ITab_Pawn_Family()
        {
            size = new Vector2(430f, 460f);
            labelKey = "WFL_TabFamily";
        }

        private Pawn SelectedPawn => SelThing as Pawn;

        private PersonRecord SelectedRecord
        {
            get
            {
                var pawn = SelectedPawn;
                var component = FamilyComponent.Current;
                return pawn == null || component == null ? null : component.Registry.GetPerson(pawn.thingIDNumber);
            }
        }

        public override bool IsVisible
        {
            get
            {
                var record = SelectedRecord;
                return record != null && record.InColony;
            }
        }

        protected override void FillTab()
        {
            var component = FamilyComponent.Current;
            var record = SelectedRecord;
            if (component == null || record == null) return;
            var registry = component.Registry;

            var outer = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            var view = new Rect(0f, 0f, outer.width - 16f, Mathf.Max(contentHeight, outer.height));
            Widgets.BeginScrollView(outer, ref scroll, view);
            float y = 0f;

            var lineage = registry.GetLineage(registry.FamilyToShow(record));
            if (lineage != null)
            {
                Row(view, ref y, "WFL_Tab_Lineage".Translate(), "WFL_FamilyOf".Translate(registry.LineageDisplayName(lineage)));
                if (Widgets.ButtonText(new Rect(0f, y, 180f, 26f), "WFL_OpenFamilies".Translate()))
                    FamilyUi.OpenFamilies(lineage.Id);
                y += 32f;
            }

            var household = registry.GetHousehold(record.HouseholdId);
            if (household != null)
                Row(view, ref y, "WFL_Tab_Household".Translate(),
                    FamilyUi.NameList(registry, household.ResidentIds.Where(id => id != record.Id)).NullToDash());

            Row(view, ref y, "WFL_Tab_Parents".Translate(), FamilyUi.NameList(registry, record.ParentIds).NullToDash());
            Row(view, ref y, "WFL_Tab_Partners".Translate(), FamilyUi.NameList(registry, record.PartnerIds()).NullToDash());
            Row(view, ref y, "WFL_Tab_Children".Translate(), FamilyUi.NameList(registry, registry.ChildrenOf(record.Id)).NullToDash());
            Row(view, ref y, "WFL_Tab_Siblings".Translate(), FamilyUi.NameList(registry, registry.SiblingsOf(record.Id)).NullToDash());

            contentHeight = y + 10f;
            Widgets.EndScrollView();
        }

        private static void Row(Rect view, ref float y, string label, string value)
        {
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, view.width, 22f), label);
            GUI.color = Color.white;
            y += 20f;
            float h = Text.CalcHeight(value, view.width - 10f);
            Widgets.Label(new Rect(10f, y, view.width - 10f, h), value);
            y += h + 8f;
        }
    }

    internal static class StringExtras
    {
        public static string NullToDash(this string s) => string.IsNullOrEmpty(s) ? "-" : s;
    }
}
