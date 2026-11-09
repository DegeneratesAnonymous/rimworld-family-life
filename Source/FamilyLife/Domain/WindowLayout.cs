using System;

namespace FamilyLife.Domain
{
    /// <summary>
    /// The Families window's sizes, worked out from the room there is. Kept apart from the drawing so the rules (how many columns, how wide a card or a
    /// line of prose may be, when a side panel moves below) are the same everywhere and can be tested. Wide windows get more columns, not wider ones.
    /// </summary>
    public static class WindowLayout
    {
        public const float MinCard = 320f, MaxCard = 520f, ProseMax = 700f, Gap = 8f;
        public const float SidebarMin = 210f, SidebarMax = 240f, DrawerMin = 360f, DrawerMax = 440f;
        public const float ThreeColumnsFrom = 1040f, TwoColumnsFrom = 660f, DrawerBesideFrom = 900f, TabMax = 150f;

        /// <summary>The dashboard columns: three on a wide window, two on a moderate one, one on a narrow one.</summary>
        public static int DashboardColumns(float width) => width >= ThreeColumnsFrom ? 3 : width >= TwoColumnsFrom ? 2 : 1;

        /// <summary>How many cards fit across, each between the minimum and maximum width.</summary>
        public static int CardColumns(float width)
        {
            if (width <= MinCard) return 1;
            return Math.Max(1, (int)Math.Floor((width + Gap) / (MinCard + Gap)));
        }

        /// <summary>The width of one card in a row of <paramref name="columns"/>; never wider than the maximum, so extra room becomes extra columns or empty margin.</summary>
        public static float CardWidth(float width, int columns)
        {
            columns = Math.Max(1, columns);
            float w = (width - Gap * (columns - 1)) / columns;
            return Math.Min(MaxCard, Math.Max(Math.Min(MinCard, width), w));
        }

        public static float ProseWidth(float width) => Math.Min(ProseMax, width);

        public static float SidebarWidth(float windowWidth) => Math.Max(SidebarMin, Math.Min(SidebarMax, windowWidth * 0.2f));

        public static float DrawerWidth(float mainWidth) => Math.Max(DrawerMin, Math.Min(DrawerMax, mainWidth * 0.36f));

        /// <summary>Whether a detail drawer sits beside the list. Below this it moves under the selected item so nothing scrolls sideways.</summary>
        public static bool DrawerBeside(float mainWidth) => mainWidth >= DrawerBesideFrom;

        /// <summary>A list that stays readable: relationship rows and metric bars never run across a whole maximized window.</summary>
        public static float ListWidth(float mainWidth, bool drawerBeside) => drawerBeside ? Math.Min(620f, mainWidth - DrawerWidth(mainWidth) - Gap) : Math.Min(mainWidth, 620f);

        /// <summary>Tabs keep a fixed width and sit left; they are not stretched across the window.</summary>
        public static float TabWidth(float stripWidth, int count) => Math.Min(TabMax, stripWidth / Math.Max(1, count));

        /// <summary>Office cards in a row: four on a wide window.</summary>
        public static int OfficeColumns(float width) => Math.Max(1, Math.Min(4, (int)Math.Floor((width + Gap) / (200f + Gap))));
    }
}

namespace FamilyLife.Domain
{
    /// <summary>
    /// When a cached view of a House is still good. The window builds its story, people and home models once and keeps them; this is the one rule that decides when
    /// they must be rebuilt. The game raises the revision whenever the records change (a new event, a bond, membership, a household, a role, something disclosed);
    /// a new registry (a loaded save), a different House, a different person being played, or the passing of the refresh interval also makes a view stale.
    /// </summary>
    public static class ViewFreshness
    {
        public static bool IsFresh(object builtRegistry, object currentRegistry, int builtLineage, int lineage, int builtRevision, int revision, int builtViewer, int viewer, int builtTick, int now, int maxAge) =>
            ReferenceEquals(builtRegistry, currentRegistry) && builtLineage == lineage && builtRevision == revision && builtViewer == viewer && now - builtTick < maxAge && now >= builtTick;
    }
}
