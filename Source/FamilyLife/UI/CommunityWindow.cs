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
        private const float NavWidth = 224f;
        private static string selected;
        private static readonly Dictionary<string, Vector2> scrolls = new Dictionary<string, Vector2>();
        private static readonly HashSet<string> failed = new HashSet<string>();
        private readonly Dictionary<string, float> heights = new Dictionary<string, float>();
        private Vector2 navScroll;

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
            var settingsId = CommunityCorePages.PageId("settings"); var pages = FamilyFramework.Pages.Visible().Where(x => x.Id != settingsId).ToList();
            var page = FamilyFramework.Pages.Resolve(selected);
            if (page == null) { Widgets.Label(inRect, "WFL_Hub_NoPages".Translate()); return; }
            selected = page.Id;

            var nav = new Rect(0f, 0f, NavWidth, inRect.height);
            Widgets.DrawBoxSolid(nav, new Color(0.10f, 0.10f, 0.11f, 0.95f));
            float y = 6f;
            Text.Font = GameFont.Small;
            var statuses = pages.Select(p => FamilyFramework.Pages.StatusOf(p)).ToList();
            var rowHeights = pages.Select((p, i) => Mathf.Max(34f, Text.CalcHeight(p.LabelKey.Translate(), NavWidth - (statuses[i] > 0 ? 92f : 66f)) + 12f)).ToList();
            var navList = new Rect(0f, 0f, nav.width, nav.height - 40f);
            Widgets.BeginScrollView(navList, ref navScroll, new Rect(0f, 0f, NavWidth - 18f, Mathf.Max(navList.height, rowHeights.Sum() + 12f)));
            int index = 0;
            foreach (var p in pages)
            {
                var row = new Rect(4f, y, NavWidth - 26f, rowHeights[index++] - 2f);
                bool on = p.Id == page.Id;
                if (on) Widgets.DrawBoxSolid(row, new Color(0.25f, 0.22f, 0.12f, 0.9f)); else if (Mouse.IsOver(row)) Widgets.DrawHighlight(row);
                var icon = string.IsNullOrEmpty(p.Icon) ? null : ContentFinder<Texture2D>.Get(p.Icon, false);
                float x = row.x + 6f;
                if (icon != null) { GUI.DrawTexture(new Rect(x, row.center.y - 10f, 20f, 20f), icon); }
                x += 26f;
                int status = statuses[index - 1];
                Text.Anchor = TextAnchor.MiddleLeft;
                Widgets.Label(new Rect(x, row.y, row.xMax - x - (status > 0 ? 30f : 4f), row.height), p.LabelKey.Translate());
                TooltipHandler.TipRegion(row, p.LabelKey.Translate());
                if (status > 0) { Text.Anchor = TextAnchor.MiddleRight; GUI.color = FamilyKit.Gold; Widgets.Label(new Rect(row.xMax - 30f, row.y, 26f, row.height), status.ToString()); GUI.color = Color.white; }
                Text.Anchor = TextAnchor.UpperLeft;
                if (Widgets.ButtonInvisible(row) && !on) { selected = p.Id; }
                y += row.height + 2f;
            }
            Widgets.EndScrollView();
            // Settings is housekeeping, not a destination: it sits as a cog at the foot of the list.
            var settings = FamilyFramework.Pages.ById(settingsId);
            if (settings != null)
            {
                var cog = new Rect(8f, nav.height - 36f, 30f, 30f); bool onSettings = page.Id == settingsId;
                if (onSettings) Widgets.DrawBoxSolid(cog, new Color(0.25f, 0.22f, 0.12f, 0.9f)); else if (Mouse.IsOver(cog)) Widgets.DrawHighlight(cog);
                var cogTex = ContentFinder<Texture2D>.Get("UI/WFL_Cog", false);
                if (cogTex != null) GUI.DrawTexture(cog.ContractedBy(4f), cogTex);
                TooltipHandler.TipRegion(cog, settings.LabelKey.Translate());
                if (Widgets.ButtonInvisible(cog) && !onSettings) selected = settingsId;
            }

            var body = new Rect(NavWidth + 8f, 0f, inRect.width - NavWidth - 8f, inRect.height);
            Text.Font = GameFont.Medium; GUI.color = FamilyKit.Gold;
            float titleHeight = Mathf.Max(32f, Text.CalcHeight(page.LabelKey.Translate(), body.width));
            Widgets.Label(new Rect(body.x, body.y, body.width, titleHeight), page.LabelKey.Translate());
            GUI.color = Color.white; Text.Font = GameFont.Small;
            var area = new Rect(body.x, body.y + titleHeight + 8f, body.width, body.height - titleHeight - 8f);
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
                heights.TryGetValue(page.Id, out float height);
                var view = new Rect(0f, 0f, area.width - 18f, Mathf.Max(height, area.height));
                Widgets.BeginScrollView(area, ref scroll, view);
                float used;
                try { used = page.Render(new Rect(0f, 0f, view.width, area.height), c); }
                finally { Widgets.EndScrollView(); }
                scrolls[page.Id] = scroll;
                heights[page.Id] = used + 20f;
            }
            catch (Exception ex)
            {
                failed.Add(page.Id);
                Log.Error("[Wil's Family Life] Community page '" + page.Id + "' failed and was disabled for this session: " + ex);
            }
            finally { GUI.color = Color.white; Text.Font = GameFont.Small; Text.Anchor = TextAnchor.UpperLeft; }
        }
    }

    [DefOf]
    public static class CommunityDefOf
    {
        public static MainButtonDef WFL_Community;
        static CommunityDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CommunityDefOf)); }
    }
}
