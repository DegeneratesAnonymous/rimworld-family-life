using System.Collections.Generic;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The shared drawing vocabulary of the Families window: cards, section headers, badges, metric bars, empty states, alert rows, tab strips.
    /// Every tab is built from these so spacing, colour and emphasis are the same everywhere. Gold marks selection, importance and the
    /// primary action only. Nothing here allocates per frame beyond the strings it is handed; heights are remembered so a card's
    /// background can be drawn before its contents without a second pass.
    /// </summary>
    internal static class FamilyKit
    {
        public const float Pad = 8f, Gap = 8f, RowH = 26f, CardTitleH = 26f, TabH = 30f;

        public static readonly Color Gold = new Color(0.90f, 0.74f, 0.28f);
        public static readonly Color CardFill = new Color(0.12f, 0.12f, 0.13f, 0.92f);
        public static readonly Color CardEdge = new Color(0.30f, 0.30f, 0.31f, 1f);
        public static readonly Color Dim = new Color(0.62f, 0.62f, 0.62f);
        public static readonly Color Closeness = new Color(0.35f, 0.70f, 0.45f);
        public static readonly Color Trust = new Color(0.35f, 0.55f, 0.82f);
        public static readonly Color Tension = new Color(0.82f, 0.38f, 0.33f);
        public static readonly Color Familiarity = new Color(0.82f, 0.68f, 0.28f);
        public static readonly Color Unknown = new Color(0.45f, 0.45f, 0.47f);
        public static readonly Color Warn = new Color(0.90f, 0.62f, 0.25f);
        public static readonly Color Urgent = new Color(0.88f, 0.36f, 0.30f);
        public static readonly Color Good = new Color(0.45f, 0.75f, 0.45f);

        /// <summary>The visible slice of the scrolled content, set by the window each frame. Long lists skip drawing what is outside it.</summary>
        public static float ViewTop, ViewBottom = 100000f;
        /// <summary>True only while drawing a list directly in the scrolled content (not inside a card's group, where the coordinates are local).</summary>
        public static bool Cull;

        private static readonly Dictionary<string, float> heights = new Dictionary<string, float>();
        public static void ForgetHeights() { heights.Clear(); }

        // ------------------------------------------------------------------ cards

        /// <summary>An open card. Draw its contents in the group's own coordinates (origin at the content's top-left, width <see cref="Inner"/>.width).</summary>
        public struct Card
        {
            public string Key;
            public float Top, Width;
            public Rect Inner;
            public bool Open;
            public Rect View => new Rect(0f, 0f, Inner.width, 4000f);
        }

        public static Card BeginCard(Rect column, ref float y, string key, string title, string tip = null, bool accent = false)
        {
            heights.TryGetValue(key, out float known);
            float h = known > 0f ? known : 80f;
            var outer = new Rect(column.x, y, column.width, h);
            Widgets.DrawBoxSolid(outer, CardFill);
            GUI.color = accent ? Gold : CardEdge;
            Widgets.DrawBox(outer, 1);
            GUI.color = Color.white;
            float top = Pad;
            if (!string.IsNullOrEmpty(title))
            {
                Text.Font = GameFont.Small;
                GUI.color = Gold;
                Widgets.Label(new Rect(outer.x + Pad, outer.y + 4f, outer.width - 2f * Pad, CardTitleH), title);
                GUI.color = Color.white;
                if (!string.IsNullOrEmpty(tip)) { var t = new Rect(outer.x + Pad, outer.y + 4f, outer.width - 2f * Pad, CardTitleH); TooltipHandler.TipRegion(t, tip); }
                Widgets.DrawBoxSolid(new Rect(outer.x + Pad, outer.y + CardTitleH + 3f, outer.width - 2f * Pad, 1f), new Color(1f, 1f, 1f, 0.12f));
                top = CardTitleH + 8f;
            }
            var inner = new Rect(outer.x + Pad, outer.y + top, outer.width - 2f * Pad, 4000f);
            Widgets.BeginGroup(inner);
            return new Card { Key = key, Top = y, Width = outer.width, Inner = inner, Open = true };
        }

        public static void EndCard(Card card, float contentHeight, ref float y)
        {
            Widgets.EndGroup();
            float titled = card.Inner.y - card.Top;
            float total = titled + Mathf.Max(contentHeight, 16f) + Pad;
            heights[card.Key] = total;
            y = card.Top + total + Gap;
        }

        // ------------------------------------------------------------------ text

        public static float Label(Rect view, float y, string text, float indent = 0f, bool dim = false, GameFont font = GameFont.Small)
        {
            if (string.IsNullOrEmpty(text)) return 0f;
            var old = Text.Font; Text.Font = font;
            float h = Text.CalcHeight(text, view.width - indent);
            if (dim) GUI.color = Dim;
            Widgets.Label(new Rect(indent, y, view.width - indent, h), text);
            GUI.color = Color.white;
            Text.Font = old;
            return h + 2f;
        }

        public static void SectionHeader(Rect view, ref float y, string text, string tip = null)
        {
            y += 4f;
            Text.Font = GameFont.Small;
            GUI.color = new Color(0.82f, 0.82f, 0.82f);
            var r = new Rect(0f, y, view.width, 22f);
            Widgets.Label(r, text.ToUpperInvariant());
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
            y += 22f;
        }

        public static void EmptyState(Rect view, ref float y, string text)
        {
            y += Label(view, y, text, 4f, true) + 2f;
        }

        public static void Badge(Rect rect, string text, Color color)
        {
            Widgets.DrawBoxSolid(rect, new Color(color.r, color.g, color.b, 0.28f));
            GUI.color = color;
            Widgets.DrawBox(rect, 1);
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            GUI.color = Color.Lerp(color, Color.white, 0.5f);
            Widgets.Label(rect, text);
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
        }

        public static float BadgeWidth(string text)
        {
            Text.Font = GameFont.Tiny;
            float w = Text.CalcSize(text).x + 12f;
            Text.Font = GameFont.Small;
            return w;
        }

        /// <summary>A key and its value on one line: a dim label on the left, the value after it.</summary>
        public static float KeyValue(Rect view, float y, string key, string value, float keyWidth = 110f)
        {
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Dim;
            Widgets.Label(new Rect(0f, y, keyWidth, 22f), key);
            GUI.color = Color.white;
            float h = Text.CalcHeight(value ?? "", view.width - keyWidth);
            Widgets.Label(new Rect(keyWidth, y, view.width - keyWidth, h), value ?? "");
            return Mathf.Max(22f, h) + 1f;
        }

        // ------------------------------------------------------------------ bars

        public static void MetricBar(Rect rect, float value, Color color, string tooltip = null, bool known = true)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.08f, 0.08f, 0.09f, 0.95f));
            if (known)
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, rect.width * Mathf.Clamp01(value / 100f), rect.height), color);
            else
                Widgets.DrawBoxSolid(new Rect(rect.x, rect.y + rect.height / 2f - 1f, rect.width, 2f), Unknown);
            GUI.color = new Color(1f, 1f, 1f, 0.14f);
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(tooltip)) TooltipHandler.TipRegion(rect, tooltip);
        }

        // ------------------------------------------------------------------ alerts

        public static Color SeverityColor(int severity) => severity >= 2 ? Urgent : severity == 1 ? Warn : Dim;

        /// <summary>One concern: a coloured dot, a short line and an arrow. Returns true when clicked.</summary>
        public static bool AlertRow(Rect view, ref float y, string text, int severity, string tip = null)
        {
            var row = new Rect(0f, y, view.width, 24f);
            Widgets.DrawHighlightIfMouseover(row);
            var dot = new Rect(row.x + 4f, row.y + 8f, 8f, 8f);
            Widgets.DrawBoxSolid(dot, SeverityColor(severity));
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(row.x + 20f, row.y, row.width - 40f, row.height), text.Truncate(row.width - 40f));
            GUI.color = Dim;
            Text.Anchor = TextAnchor.MiddleRight;
            Widgets.Label(new Rect(row.xMax - 24f, row.y, 22f, row.height), ">");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(row, tip);
            y += 26f;
            return Widgets.ButtonInvisible(row);
        }

        /// <summary>
        /// A matter needing attention: a short heading, what is wrong and by how much, and what could be done about it. The whole row is a link.
        /// Returns true when clicked.
        /// </summary>
        public static bool InfoRow(Rect view, ref float y, string head, string detail, string hint, int severity, string tip = null)
        {
            const float pad = 8f;
            float inner = view.width - 2f * pad - 14f;
            Text.Font = GameFont.Small;
            float detailH = string.IsNullOrEmpty(detail) ? 0f : Text.CalcHeight(detail, inner);
            Text.Font = GameFont.Tiny;
            float hintH = string.IsNullOrEmpty(hint) ? 0f : Text.CalcHeight(hint, inner);
            Text.Font = GameFont.Small;
            float h = pad + 18f + detailH + (hintH > 0f ? hintH + 2f : 0f) + pad;
            var row = new Rect(0f, y, view.width, h);
            Widgets.DrawBoxSolid(row, new Color(0.14f, 0.14f, 0.15f));
            Widgets.DrawBoxSolid(new Rect(row.x, row.y, 3f, row.height), SeverityColor(severity));
            Widgets.DrawHighlightIfMouseover(row);
            float cy = y + pad;
            Text.Font = GameFont.Tiny;
            GUI.color = SeverityColor(severity) == Dim ? new Color(0.8f, 0.8f, 0.8f) : SeverityColor(severity);
            Widgets.Label(new Rect(pad + 4f, cy, inner, 18f), head.ToUpperInvariant());
            GUI.color = Dim;
            Text.Anchor = TextAnchor.UpperRight;
            Widgets.Label(new Rect(row.xMax - 22f, cy, 16f, 18f), ">");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            cy += 18f;
            if (detailH > 0f) { Widgets.Label(new Rect(pad + 4f, cy, inner, detailH), detail); cy += detailH; }
            if (hintH > 0f)
            {
                Text.Font = GameFont.Tiny; GUI.color = Dim;
                Widgets.Label(new Rect(pad + 4f, cy + 2f, inner, hintH), hint);
                GUI.color = Color.white; Text.Font = GameFont.Small;
            }
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(row, tip);
            y += h + 4f;
            return Widgets.ButtonInvisible(row);
        }

        // ------------------------------------------------------------------ tabs

        /// <summary>A strip of tabs. The selected one is brighter and underlined in gold. Returns the index clicked, or -1.</summary>
        public static int TabStrip(Rect strip, IList<string> labels, int selected, IList<string> tips = null, IList<int> counts = null, IList<bool> urgent = null)
        {
            int clicked = -1;
            float w = WindowLayout.TabWidth(strip.width, labels.Count);       // fixed width, left-aligned: tabs are not stretched across the window
            for (int i = 0; i < labels.Count; i++)
            {
                var r = new Rect(strip.x + i * w, strip.y, w - 2f, strip.height);
                bool on = i == selected;
                Widgets.DrawBoxSolid(r, on ? new Color(0.22f, 0.22f, 0.24f) : new Color(0.11f, 0.11f, 0.12f));
                if (!on && Mouse.IsOver(r)) Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.06f));
                if (on) Widgets.DrawBoxSolid(new Rect(r.x, r.yMax - 3f, r.width, 3f), Gold);
                Text.Anchor = TextAnchor.MiddleCenter;
                GUI.color = on ? Color.white : new Color(0.72f, 0.72f, 0.72f);
                Widgets.Label(r, labels[i]);
                GUI.color = Color.white;
                Text.Anchor = TextAnchor.UpperLeft;
                string tip = tips != null && i < tips.Count ? tips[i] : null;
                if (counts != null && i < counts.Count && counts[i] > 0)
                {
                    // A labelled count, not an unexplained square: "2" in a rounded badge, with a sentence in the tooltip.
                    bool hot = urgent != null && i < urgent.Count && urgent[i];
                    var badge = new Rect(r.xMax - 22f, r.y + 5f, 18f, 16f);
                    Widgets.DrawBoxSolid(badge, hot ? Urgent : Warn);
                    Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter; GUI.color = Color.black;
                    Widgets.Label(badge, counts[i].ToString());
                    GUI.color = Color.white; Text.Anchor = TextAnchor.UpperLeft; Text.Font = GameFont.Small;
                    tip = (string.IsNullOrEmpty(tip) ? "" : tip + "\n\n") + "WFL_FW_TabBadgeTip".Translate(counts[i]);
                }
                if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(r, tip);
                if (Widgets.ButtonInvisible(r) && !on) clicked = i;
            }
            return clicked;
        }

        /// <summary>A small row of choices (a sub-tab or filter set). The chosen one is outlined in gold.</summary>
        public static int ChipRow(Rect view, ref float y, IList<string> labels, int selected, IList<string> tips = null, float chipHeight = 24f)
        {
            int clicked = -1;
            float x = 0f;
            float rowY = y;
            for (int i = 0; i < labels.Count; i++)
            {
                float w = BadgeWidth(labels[i]) + 14f;
                if (x + w > view.width && x > 0f) { x = 0f; rowY += chipHeight + 4f; }
                var r = new Rect(x, rowY, w, chipHeight);
                bool on = i == selected;
                Widgets.DrawBoxSolid(r, on ? new Color(0.28f, 0.24f, 0.12f) : new Color(0.16f, 0.16f, 0.17f));
                GUI.color = on ? Gold : new Color(0.4f, 0.4f, 0.42f);
                Widgets.DrawBox(r, 1);
                GUI.color = on ? Color.white : new Color(0.8f, 0.8f, 0.8f);
                Text.Anchor = TextAnchor.MiddleCenter;
                Widgets.Label(r, labels[i]);
                Text.Anchor = TextAnchor.UpperLeft;
                GUI.color = Color.white;
                if (tips != null && i < tips.Count && !string.IsNullOrEmpty(tips[i])) TooltipHandler.TipRegion(r, tips[i]);
                if (Widgets.ButtonInvisible(r)) clicked = i;
                x += w + 4f;
            }
            y = rowY + chipHeight + 6f;
            return clicked;
        }

        /// <summary>A filter chip that can be on or off independently of the others. Returns true when toggled.</summary>
        public static bool ToggleChip(Rect rect, string label, bool on, string tip = null)
        {
            Widgets.DrawBoxSolid(rect, on ? new Color(0.28f, 0.24f, 0.12f) : new Color(0.16f, 0.16f, 0.17f));
            GUI.color = on ? Gold : new Color(0.4f, 0.4f, 0.42f);
            Widgets.DrawBox(rect, 1);
            GUI.color = on ? Color.white : new Color(0.8f, 0.8f, 0.8f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(rect, label);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(rect, tip);
            return Widgets.ButtonInvisible(rect);
        }

        /// <summary>The primary action of a card: gold-edged. Everything else is an ordinary button.</summary>
        public static bool PrimaryButton(Rect rect, string label, bool enabled = true, string disabledReason = null)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool clicked = Widgets.ButtonText(rect, label);
            GUI.enabled = previous;
            GUI.color = enabled ? Gold : Dim;
            Widgets.DrawBox(rect, 1);
            GUI.color = Color.white;
            if (!enabled && !string.IsNullOrEmpty(disabledReason)) TooltipHandler.TipRegion(rect, disabledReason);
            return clicked && enabled;
        }

        /// <summary>A button that is greyed out when it cannot be used and says why in its tooltip.</summary>
        public static bool Action(Rect rect, string label, bool enabled, string disabledReason = null, string tip = null)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && enabled;
            bool clicked = Widgets.ButtonText(rect, label);
            GUI.enabled = previous;
            string text = !enabled && !string.IsNullOrEmpty(disabledReason) ? disabledReason : tip;
            if (!string.IsNullOrEmpty(text)) TooltipHandler.TipRegion(rect, text);
            return clicked && enabled;
        }

        public static void Help(Rect corner, string tip)
        {
            if (string.IsNullOrEmpty(tip)) return;
            var q = new Rect(corner.xMax - 22f, corner.y + 2f, 20f, 20f);
            Widgets.DrawBoxSolid(q, new Color(0.1f, 0.1f, 0.1f, 0.85f));
            GUI.color = Mouse.IsOver(q) ? Color.white : new Color(0.75f, 0.75f, 0.75f);
            Widgets.DrawBox(q);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(q, "?");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
            TooltipHandler.TipRegion(q, tip);
        }

        /// <summary>Splits a width into one or two columns depending on how much room there is.</summary>
        public static void Columns(Rect view, out Rect left, out Rect right, out bool wide, float leftShare = 0.5f, float minWide = 640f)
        {
            wide = view.width >= minWide;
            if (!wide) { left = new Rect(0f, 0f, view.width, 0f); right = left; return; }
            float lw = Mathf.Floor((view.width - Gap) * leftShare);
            left = new Rect(0f, 0f, lw, 0f);
            right = new Rect(lw + Gap, 0f, view.width - lw - Gap, 0f);
        }

        // ------------------------------------------------------------------ cards for things that happened

        /// <summary>
        /// One event: a kind badge, a date, who it was about, what happened, and what it meant. Defining memories are marked. Heights are measured,
        /// so a long description wraps and the next card follows.
        /// </summary>
        public static void EventCard(Rect view, ref float y, string date, string type, Color typeColor, string participants, string body, string consequence, bool defining, string tip = null)
        {
            const float pad = 8f;
            float inner = view.width - 2f * pad;
            Text.Font = GameFont.Small;
            float bodyH = string.IsNullOrEmpty(body) ? 0f : Text.CalcHeight(body, inner);
            Text.Font = GameFont.Tiny;
            float consH = string.IsNullOrEmpty(consequence) ? 0f : Text.CalcHeight(consequence, inner);
            float partH = string.IsNullOrEmpty(participants) ? 0f : 16f;
            Text.Font = GameFont.Small;
            float h = pad + 20f + partH + bodyH + (consH > 0f ? consH + 2f : 0f) + pad;
            if (Cull && y + h < ViewTop - 40f || Cull && y > ViewBottom + 40f) { y += h + 4f; return; }
            var rect = new Rect(0f, y, view.width, h);
            Widgets.DrawBoxSolid(rect, new Color(0.13f, 0.13f, 0.14f));
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 3f, rect.height), defining ? Gold : typeColor);
            Widgets.DrawHighlightIfMouseover(rect);
            float cy = y + pad;
            float bw = BadgeWidth(type);
            Badge(new Rect(pad + 4f, cy, bw, 18f), type, typeColor);
            Text.Font = GameFont.Tiny;
            GUI.color = Dim;
            Widgets.Label(new Rect(pad + 4f + bw + 8f, cy + 1f, inner - bw - 8f - (defining ? 110f : 0f), 18f), date);
            GUI.color = Color.white;
            if (defining)
            {
                var star = new Rect(rect.xMax - pad - 104f, cy, 104f, 18f);
                Badge(star, "WFL_FW_Defining".Translate(), Gold);
                TooltipHandler.TipRegion(star, "WFL_FW_DefiningTip".Translate());
            }
            cy += 20f;
            if (partH > 0f)
            {
                GUI.color = Dim;
                Widgets.Label(new Rect(pad + 4f, cy, inner - 4f, 16f), participants.Truncate(inner - 4f));
                GUI.color = Color.white;
                cy += 16f;
            }
            Text.Font = GameFont.Small;
            if (bodyH > 0f) { Widgets.Label(new Rect(pad + 4f, cy, inner - 4f, bodyH), body); cy += bodyH; }
            if (consH > 0f)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.72f, 0.78f, 0.62f);
                Widgets.Label(new Rect(pad + 4f, cy + 2f, inner - 4f, consH), consequence);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            if (!string.IsNullOrEmpty(tip)) TooltipHandler.TipRegion(rect, tip);
            y += h + 4f;
        }

        public static string Truncated(string text, float width) => text.Truncate(width);
    }
}
