using System;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>Shared, responsive section layout for framework and optional module pages.</summary>
    public static class CommunityPageLayout
    {
        public const float Gap = 16f;
        public static bool TwoColumns(Rect r) => r.width >= 680f;
        public static float ColumnWidth(Rect r) => TwoColumns(r) ? (r.width - Gap) / 2f : r.width;

        // Content uses local coordinates. Returning its actual height keeps long translations and lists scrollable.
        public static float Section(Rect r, float x, float y, float width, string title, Func<Rect, float> draw)
        {
            GUI.BeginGroup(new Rect(r.x + x, r.y + y, width, 10000f));
            try
            {
                float heading = Mathf.Max(28f, Text.CalcHeight(title, width - 16f) + 10f);
                Widgets.DrawBoxSolid(new Rect(0f, 0f, width, heading), new Color(.18f, .18f, .19f));
                GUI.color = FamilyKit.Gold;
                Widgets.Label(new Rect(8f, 5f, width - 16f, heading - 5f), title);
                GUI.color = Color.white;
                GUI.BeginGroup(new Rect(8f, heading + 8f, width - 16f, 9900f));
                try { return y + heading + 8f + draw(new Rect(0f, 0f, width - 16f, r.height)) + Gap; }
                finally { GUI.EndGroup(); }
            }
            finally { GUI.EndGroup(); GUI.color = Color.white; }
        }

        public static float Line(Rect r, float y, string text, bool dim = false)
        {
            float height = Text.CalcHeight(text, r.width);
            if (dim) GUI.color = Color.gray;
            Widgets.Label(new Rect(0f, y, r.width, height), text);
            GUI.color = Color.white;
            return y + height + 6f;
        }

        public static float Button(Rect r, float y, string text, Action action)
        {
            float height = Mathf.Max(30f, Text.CalcHeight(text, r.width - 20f) + 10f);
            if (Widgets.ButtonText(new Rect(0f, y, r.width, height), text)) action();
            return y + height + 6f;
        }
    }
}
