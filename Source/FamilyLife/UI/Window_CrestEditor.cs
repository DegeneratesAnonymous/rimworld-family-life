using FamilyLife.Domain;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// A dialog for a House's crest: colour swatches, a grid of symbols, the patterns, and a live preview. Nothing is changed until Confirm; Cancel leaves the crest as it was.
    /// </summary>
    internal sealed class Window_CrestEditor : Window
    {
        private readonly Lineage house;
        private readonly Lineage preview = new Lineage();
        private readonly Lineage thumb = new Lineage();
        private int primary, secondary, mark, pattern;

        public Window_CrestEditor(Lineage house)
        {
            this.house = house;
            primary = CrestStyle.Index(house.CrestPrimary, house.Id, CrestStyle.Colors);
            secondary = CrestStyle.Index(house.CrestSecondary, 7, CrestStyle.Colors);
            mark = CrestStyle.Index(house.CrestMark, house.Id / CrestStyle.Colors, CrestStyle.Marks);
            pattern = CrestStyle.Index(house.CrestPattern, 0, CrestStyle.Patterns);
            doCloseX = true; absorbInputAroundWindow = true; closeOnClickedOutside = false; forcePause = false;
        }

        public override Vector2 InitialSize => new Vector2(560f, 470f);

        private void Fill(Lineage l, int pr, int sec, int mk, int pat) { l.Id = house.Id; l.CrestPrimary = pr; l.CrestSecondary = sec; l.CrestMark = mk; l.CrestPattern = pat; }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "WFL_Crest_EditTitle".Translate());
            Text.Font = GameFont.Small;
            Fill(preview, primary, secondary, mark, pattern);
            FamilyCrest.Draw(new Rect(inRect.width - 110f, 40f, 96f, 116f), preview);
            GUI.color = FamilyKit.Dim; Widgets.Label(new Rect(inRect.width - 120f, 160f, 116f, 22f), "WFL_Crest_Preview".Translate()); GUI.color = Color.white;

            float y = 40f;
            y = Swatches(y, "WFL_Crest_PrimaryLabel".Translate(), ref primary);
            y = Swatches(y, "WFL_Crest_SecondaryLabel".Translate(), ref secondary);

            GUI.color = FamilyKit.Gold; Widgets.Label(new Rect(0f, y, 300f, 22f), "WFL_Crest_SymbolLabel".Translate()); GUI.color = Color.white;
            y += 24f;
            for (int i = 0; i < CrestStyle.Marks; i++)
            {
                var cell = new Rect(i * 56f, y, 50f, 60f);
                Fill(thumb, primary, secondary, i, pattern);
                FamilyCrest.Draw(cell, thumb);
                if (i == mark) { GUI.color = FamilyKit.Gold; Widgets.DrawBox(cell, 3); GUI.color = Color.white; }
                TooltipHandler.TipRegion(cell, ("WFL_Crest_Mark_" + i).Translate().ToString().Replace("Symbol: ", ""));
                if (Widgets.ButtonInvisible(cell)) mark = i;
            }
            y += 70f;

            GUI.color = FamilyKit.Gold; Widgets.Label(new Rect(0f, y, 300f, 22f), "WFL_Crest_PatternLabel".Translate()); GUI.color = Color.white;
            y += 24f;
            for (int i = 0; i < CrestStyle.Patterns; i++)
            {
                var cell = new Rect(i * 90f, y, 80f, 60f);
                Fill(thumb, primary, secondary, mark, i);
                FamilyCrest.Draw(cell, thumb);
                if (i == pattern) { GUI.color = FamilyKit.Gold; Widgets.DrawBox(cell, 3); GUI.color = Color.white; }
                Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.UpperCenter;
                Widgets.Label(new Rect(cell.x, cell.yMax + 1f, cell.width, 16f), ("WFL_Crest_Pattern_" + i).Translate().ToString().Replace("Pattern: ", ""));
                Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                if (Widgets.ButtonInvisible(cell)) pattern = i;
            }

            float by = inRect.height - 34f;
            if (Widgets.ButtonText(new Rect(inRect.width - 270f, by, 130f, 30f), "WFL_Crest_Cancel".Translate())) Close();
            if (FamilyKit.PrimaryButton(new Rect(inRect.width - 130f, by, 130f, 30f), "WFL_Crest_Confirm".Translate()))
            {
                house.CrestPrimary = primary; house.CrestSecondary = secondary; house.CrestMark = mark; house.CrestPattern = pattern;
                Close();
            }
        }

        private float Swatches(float y, string label, ref int chosen)
        {
            GUI.color = FamilyKit.Gold; Widgets.Label(new Rect(0f, y, 300f, 22f), label); GUI.color = Color.white;
            y += 24f;
            for (int i = 0; i < CrestStyle.Colors; i++)
            {
                var cell = new Rect(i * 36f, y, 30f, 30f);
                Widgets.DrawBoxSolid(cell, FamilyCrest.ColorOf(i));
                GUI.color = i == chosen ? FamilyKit.Gold : new Color(0.4f, 0.4f, 0.42f);
                Widgets.DrawBox(cell, i == chosen ? 3 : 1);
                GUI.color = Color.white;
                if (Widgets.ButtonInvisible(cell)) chosen = i;
            }
            return y + 38f;
        }
    }
}
