using System;
using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using FamilyLife.Framework;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The one Community window. The framework owns the navigation, which page is selected, scrolling and the layout conventions; each page (the core ones below, and those a module registers
    /// in <see cref="FamilyFramework.Pages"/>) draws only its own content. It lists only the pages of modules that are enabled, and a page that throws shows a message instead of breaking the window.
    /// </summary>
    public class MainTabWindow_Community : MainTabWindow
    {
        private const float NavWidth = 190f, RowH = 30f;
        private static string selected;
        private static readonly Dictionary<string, Vector2> scrolls = new Dictionary<string, Vector2>();
        private static readonly HashSet<string> failed = new HashSet<string>();
        private float contentHeight = 400f;

        public override Vector2 RequestedTabSize => new Vector2(Mathf.Min(1180f, UI.screenWidth - 40f), Mathf.Min(760f, UI.screenHeight - 120f));

        /// <summary>Opens the window on a page (for links from other windows and from the world, such as a zone's "open Treasury").</summary>
        public static void Open(string pageId)
        {
            selected = pageId;
            if (Find.MainTabsRoot != null && Find.WindowStack.WindowOfType<MainTabWindow_Community>() == null) Find.MainTabsRoot.SetCurrentTab(CommunityDefOf.WFL_Community, false);
        }

        public override void DoWindowContents(Rect inRect)
        {
            var c = FamilyComponent.Current;
            if (c == null) return;
            var pages = FamilyFramework.Pages.Visible();
            var page = FamilyFramework.Pages.Resolve(selected);
            if (page == null) { Widgets.Label(inRect, "WFL_Hub_NoPages".Translate()); return; }
            selected = page.Id;

            var nav = new Rect(0f, 0f, NavWidth, inRect.height);
            Widgets.DrawBoxSolid(nav, new Color(0.10f, 0.10f, 0.11f, 0.95f));
            float y = 6f;
            Text.Font = GameFont.Small;
            foreach (var p in pages)
            {
                var row = new Rect(4f, y, NavWidth - 8f, RowH - 2f);
                bool on = p.Id == page.Id;
                if (on) Widgets.DrawBoxSolid(row, new Color(0.25f, 0.22f, 0.12f, 0.9f)); else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                var icon = string.IsNullOrEmpty(p.Icon) ? null : ContentFinder<Texture2D>.Get(p.Icon, false);
                float x = row.x + 6f;
                if (icon != null) { GUI.DrawTexture(new Rect(x, row.y + 4f, 20f, 20f), icon); x += 26f; }
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(x, row.y, row.xMax - x - 28f, row.height), p.LabelKey.Translate());
                int status = FamilyFramework.Pages.StatusOf(p);
                if (status > 0) { Text.Anchor = TextAnchor.MiddleRight; GUI.color = FamilyKit.Gold; Widgets.Label(new Rect(row.xMax - 30f, row.y, 26f, row.height), status.ToString()); GUI.color = Color.white; }
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(row) && !on) { selected = p.Id; }
                y += RowH;
            }

            var body = new Rect(NavWidth + 8f, 0f, inRect.width - NavWidth - 8f, inRect.height);
            Text.Font = GameFont.Medium; GUI.color = FamilyKit.Gold;
            Widgets.Label(new Rect(body.x, body.y, body.width, 32f), page.LabelKey.Translate());
            GUI.color = Color.white; Text.Font = GameFont.Small;
            var area = new Rect(body.x, body.y + 36f, body.width, body.height - 36f);
            DrawPage(page, area, c);
        }

        private void DrawPage(CommunityPage<Func<Rect, FamilyComponent, float>> page, Rect area, FamilyComponent c)
        {
            if (failed.Contains(page.Id)) { GUI.color = new Color(1f, .7f, .6f); Widgets.Label(area, "WFL_Hub_PageFailed".Translate()); GUI.color = Color.white; return; }
            try
            {
                if (page.Render == null) return;
                // A page that owns its scrolling is given the whole area; every other page is drawn into a scroll view sized to the height it reports.
                if (page.OwnsScrolling) { page.Render(new Rect(area.x, area.y, area.width, area.height), c); return; }
                scrolls.TryGetValue(page.Id, out var scroll);
                var view = new Rect(0f, 0f, area.width - 18f, Mathf.Max(contentHeight, area.height));
                Widgets.BeginScrollView(area, ref scroll, view);
                float used = page.Render(new Rect(0f, 0f, view.width, area.height), c);
                Widgets.EndScrollView();
                scrolls[page.Id] = scroll;
                contentHeight = used + 20f;
            }
            catch (Exception ex)
            {
                try { Widgets.EndScrollView(); } catch (Exception) { }
                failed.Add(page.Id);
                Log.Error("[Wil's Family Life] Community page '" + page.Id + "' failed and was disabled for this session: " + ex);
            }
        }
    }

    [DefOf]
    public static class CommunityDefOf
    {
        public static MainButtonDef WFL_Community;
        static CommunityDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CommunityDefOf)); }
    }
}
