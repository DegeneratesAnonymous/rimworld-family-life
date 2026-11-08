using System.Collections.Generic;
using System.Text;
using FamilyLife.Domain;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Loads and keeps the supplied role pictures. Every window gets a role's picture, tint and words from here and from
    /// <see cref="RoleCatalog"/>, never from a path of its own. A picture is loaded once; a missing one is reported once and replaced by the
    /// generic role picture, then by the game's own placeholder, so drawing never fails.
    /// </summary>
    internal static class RoleIcons
    {
        private static readonly Texture2D[] textures = new Texture2D[16];
        private static readonly Color[] tints = new Color[16];
        private static readonly bool[] ready = new bool[16];
        private static Texture2D generic;
        private static bool genericReady;
        private static readonly HashSet<string> warned = new HashSet<string>();

        public static void Reset() { for (int i = 0; i < ready.Length; i++) { ready[i] = false; textures[i] = null; } generic = null; genericReady = false; }

        private static Texture2D Generic()
        {
            if (!genericReady)
            {
                genericReady = true;
                generic = ContentFinder<Texture2D>.Get(RoleCatalog.GenericTexture, false) ?? BaseContent.BadTex;
            }
            return generic;
        }

        private static void Load(FamilyRole role)
        {
            int i = (int)role;
            var presentation = RoleCatalog.Get(role);
            var texture = ContentFinder<Texture2D>.Get(presentation.TexturePath, false);
            if (texture == null)
            {
                if (warned.Add(presentation.TexturePath)) Log.Warning("[Wil's Family Life] Role icon Textures/" + presentation.TexturePath + ".png is missing; a generic icon is used for " + role + ".");
                texture = Generic();
            }
            textures[i] = texture;
            tints[i] = new Color(presentation.TintR, presentation.TintG, presentation.TintB, 1f);
            ready[i] = true;
        }

        public static Texture2D Texture(FamilyRole role) { int i = (int)role; if (!ready[i]) Load(role); return textures[i]; }
        public static Color Tint(FamilyRole role) { int i = (int)role; if (!ready[i]) Load(role); return tints[i]; }
        public static string Label(FamilyRole role) => RoleCatalog.Get(role).LabelKey.Translate().ToString();
        public static string Description(FamilyRole role) => RoleCatalog.Get(role).DescriptionKey.Translate().ToString();

        /// <summary>A role's picture at a size, with a dark backing so it reads on a light portrait as well as a dark one.</summary>
        public static void Draw(Rect rect, FamilyRole role, bool backing)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (backing) Widgets.DrawBoxSolid(rect.ExpandedBy(1f), BackingColor);
            var old = GUI.color;
            GUI.color = Tint(role);
            GUI.DrawTexture(rect, Texture(role));
            GUI.color = old;
        }

        private static readonly Color BackingColor = new Color(0.08f, 0.08f, 0.1f, 0.55f);
    }

    /// <summary>
    /// What each pawn's colonist-bar emblem is, worked out once and kept. Drawing only reads this. An entry is worked out again when the family
    /// records change (the component's revision), the display settings change, the person being played changes, or after a few seconds,
    /// so a new office, a new head or a changed heir appears without reloading the save. Nothing here scans pawns or houses while drawing.
    /// </summary>
    internal static class RoleBar
    {
        private sealed class Entry
        {
            public int Revision = -1, Stamp = -1, Observer = -1, Tick = -100000;
            public bool Has;
            public RoleEntry Primary;
            public string Tip;
        }

        private const int RefreshTicks = 180;
        private static readonly Dictionary<int, Entry> entries = new Dictionary<int, Entry>();
        private static readonly RoleDisplayOptions barOptions = new RoleDisplayOptions();
        private static readonly RoleDisplayOptions tabOptions = RoleDisplayOptions.Everything;

        public static void Reset() { entries.Clear(); }

        private static int StampOf(FamilySettings s) => (s.roleIconsLeadershipOnly ? 1 : 0) | (s.showAssociateIcons ? 2 : 0);

        /// <summary>The person the player is acting as, or 0. Roles only that person could know about are shown to them, and no one else's secrets are.</summary>
        internal static int Observer() => PerspectiveShiftBridge.AvatarId;

        public static bool TryGet(FamilyComponent component, Pawn pawn, FamilySettings settings, out RoleEntry primary)
        {
            int id = pawn.thingIDNumber, now = Find.TickManager.TicksGame, observer = Observer(), stamp = StampOf(settings);
            if (!entries.TryGetValue(id, out var e))
            {
                if (entries.Count > 300) entries.Clear();          // pawns that left the bar are simply forgotten
                entries[id] = e = new Entry();
            }
            if (e.Revision != component.Revision || e.Stamp != stamp || e.Observer != observer || now - e.Tick >= RefreshTicks)
            {
                e.Revision = component.Revision; e.Stamp = stamp; e.Observer = observer; e.Tick = now; e.Tip = null;
                e.Has = false;
                if (pawn.IsColonist && !pawn.Dead)
                {
                    barOptions.ShowAssociates = settings.showAssociateIcons; barOptions.ShowTrustedAssociates = !settings.roleIconsLeadershipOnly;
                    var roles = RoleDisplay.PublicRoles(component.Registry, id, observer, barOptions);
                    e.Has = RoleDisplay.TryPrimary(roles, settings.roleIconsLeadershipOnly, settings.showAssociateIcons, out e.Primary);
                }
            }
            primary = e.Primary;
            return e.Has;
        }

        /// <summary>Role, house, what it means, and anything else the pawn holds that this observer may see. Built on first hover, then kept.</summary>
        public static string Tip(FamilyComponent component, Pawn pawn, RoleEntry primary)
        {
            if (!entries.TryGetValue(pawn.thingIDNumber, out var e)) return RoleIcons.Label(primary.Role);
            if (e.Tip != null) return e.Tip;
            e.Tip = BuildTip(component.Registry, pawn.thingIDNumber, Observer(), primary);
            return e.Tip;
        }

        internal static string TitleOf(FamilyRegistry r, RoleEntry entry)
        {
            string label = RoleIcons.Label(entry.Role);
            var house = entry.FamilyId == 0 ? null : r.GetLineage(entry.FamilyId);
            return house == null ? label : "WFL_RoleTitleHouse".Translate(label, r.LineageDisplayName(house)).ToString();
        }

        internal static string BuildTip(FamilyRegistry r, int person, int observer, RoleEntry primary)
        {
            var sb = new StringBuilder();
            sb.Append(TitleOf(r, primary).CapitalizeFirst()).Append('\n').Append(RoleIcons.Description(primary.Role));
            bool others = false;
            foreach (var entry in RoleDisplay.PublicRoles(r, person, observer, tabOptions))
            {
                if (entry.Role == primary.Role && entry.FamilyId == primary.FamilyId) continue;
                if (!others) { sb.Append("\n\n").Append("WFL_RoleAlsoHolds".Translate().ToString()); others = true; }
                sb.Append("\n- ").Append(TitleOf(r, entry).CapitalizeFirst());
            }
            sb.Append("\n\n").Append("WFL_RoleClickHint".Translate().ToString());
            return sb.ToString();
        }
    }

    /// <summary>The Family tab's list of a pawn's roles: every role the person being played may see, each with its title.</summary>
    internal static class PostBadges
    {
        internal sealed class Badge { public FamilyRole Role; public string Title; }

        private static readonly Dictionary<int, KeyValuePair<int, List<Badge>>> cache = new Dictionary<int, KeyValuePair<int, List<Badge>>>();
        private static readonly RoleDisplayOptions all = RoleDisplayOptions.Everything;

        public static void Reset() { cache.Clear(); }

        internal static List<Badge> For(Pawn pawn)
        {
            var none = new List<Badge>();
            var component = FamilyComponent.Current;
            if (pawn == null || component == null || pawn.Dead || !pawn.IsColonist) return none;
            int now = Find.TickManager.TicksGame, id = pawn.thingIDNumber;
            if (cache.TryGetValue(id, out var hit) && now - hit.Key < 30) return hit.Value;
            var r = component.Registry; var list = new List<Badge>();
            foreach (var entry in RoleDisplay.PublicRoles(r, id, RoleBar.Observer(), all))
                list.Add(new Badge { Role = entry.Role, Title = RoleBar.TitleOf(r, entry).CapitalizeFirst() });
            if (cache.Count > 400) cache.Clear();
            cache[id] = new KeyValuePair<int, List<Badge>>(now, list);
            return list;
        }

        private static float SizeFraction(int size) => size <= 0 ? 0.30f : size == 1 ? 0.38f : 0.46f;

        /// <summary>One emblem in a corner of the pawn's colonist-bar box, from cached state only. Hover for the details, click to open the family.</summary>
        internal static void Draw(Rect box, Pawn pawn)
        {
            var settings = FamilyLifeMod.Settings;
            if (settings == null || !settings.showRoleIcons || pawn == null) return;
            var component = FamilyComponent.Current;
            if (component == null || !RoleBar.TryGet(component, pawn, settings, out var role)) return;

            float size = Mathf.Max(11f, box.width * SizeFraction(settings.roleIconSize)), pad = 2f;
            int corner = settings.roleIconCorner;
            var rect = new Rect((corner & 1) == 0 ? box.x + pad : box.xMax - size - pad, corner < 2 ? box.y + pad : box.yMax - size - pad, size, size);
            RoleIcons.Draw(rect, role.Role, true);
            if (!Mouse.IsOver(rect)) return;
            TooltipHandler.TipRegion(rect, RoleBar.Tip(component, pawn, role));
            var ev = Event.current;
            if (ev.type == EventType.MouseDown && ev.button == 0) { ev.Use(); Open(pawn, role); }
        }

        private static void Open(Pawn pawn, RoleEntry role)
        {
            try
            {
                if (pawn.Spawned && pawn.Map == Find.CurrentMap)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(pawn, false);
                    InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Family));
                }
                else FamilyUi.OpenFamilies(role.FamilyId);
            }
            catch (System.Exception ex) { HookSafety.Report("role emblem click", ex); }
        }
    }

    /// <summary>Draws the family role emblem on each colonist's box in the colonist bar.</summary>
    [HarmonyPatch(typeof(ColonistBarColonistDrawer), nameof(ColonistBarColonistDrawer.DrawColonist))]
    internal static class Patch_ColonistBarRoles
    {
        private static void Postfix(Rect rect, Pawn colonist)
        {
            try { PostBadges.Draw(rect, colonist); }
            catch (System.Exception ex) { HookSafety.Report("colonist bar roles", ex); }
        }
    }
}
