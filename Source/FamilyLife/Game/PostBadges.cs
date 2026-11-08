using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// A person's roles in the colony, shown on the colonist bar beside the game's own role markers and listed on their family tab: the colony's leader,
    /// the posts they hold (healer, teacher, hall keeper, chaplain), head of a house, and steward of a house. Nothing here changes what anyone does.
    /// </summary>
    internal static class PostBadges
    {
        internal sealed class Badge { public string Glyph, Title; public Color Color; }

        private static readonly Dictionary<int, KeyValuePair<int, List<Badge>>> cache = new Dictionary<int, KeyValuePair<int, List<Badge>>>();

        private static string GlyphFor(OfficeKind kind)
        {
            switch (kind)
            {
                case OfficeKind.Healer: return "Dr";
                case OfficeKind.Teacher: return "Te";
                case OfficeKind.HallKeeper: return "Hk";
                default: return "Ch";
            }
        }

        private static Color ColorFor(OfficeKind kind)
        {
            switch (kind)
            {
                case OfficeKind.Healer: return new Color(.35f, .75f, .5f);
                case OfficeKind.Teacher: return new Color(.4f, .6f, .9f);
                case OfficeKind.HallKeeper: return new Color(.9f, .65f, .3f);
                default: return new Color(.75f, .55f, .9f);
            }
        }

        /// <summary>The pawn's roles, most senior first. Looked up at most twice a second per pawn.</summary>
        internal static List<Badge> For(Pawn pawn)
        {
            var none = new List<Badge>();
            var component = FamilyComponent.Current;
            if (pawn == null || component == null || pawn.Dead || !pawn.IsColonist) return none;
            int now = Find.TickManager.TicksGame, id = pawn.thingIDNumber;
            if (cache.TryGetValue(id, out var hit) && now - hit.Key < 30) return hit.Value;
            var r = component.Registry; var list = new List<Badge>();
            if (r.GetPerson(id) != null)
            {
                if (r.Institution.LeaderId == id && r.Institution.Kind != InstitutionKind.Consensus)
                    list.Add(new Badge { Glyph = "Ld", Color = new Color(.95f, .8f, .3f), Title = "WFL_Role_Leader".Translate().ToString() });
                foreach (var office in r.OfficesHeldBy(id))
                    list.Add(new Badge { Glyph = GlyphFor(office.Kind), Color = ColorFor(office.Kind), Title = ("WFL_OfficeKind_" + office.Kind).Translate().CapitalizeFirst().ToString() });
                int family = r.FamilyOf(id);
                var house = family == 0 ? null : r.GetLineage(family);
                if (house != null)
                {
                    string name = r.LineageDisplayName(house);
                    if (r.HeadOf(family) == id) { if (r.MembersOf(family).Count(m => m.InColony) > 1) list.Add(new Badge { Glyph = "Hd", Color = new Color(.85f, .85f, .9f), Title = "WFL_Role_Head".Translate(name).ToString() }); }
                    else if (r.StewardshipOf(id, family, true) != null) list.Add(new Badge { Glyph = "St", Color = new Color(.7f, .8f, .85f), Title = "WFL_Role_Steward".Translate(name).ToString() });
                }
            }
            if (cache.Count > 400) cache.Clear();
            cache[id] = new KeyValuePair<int, List<Badge>>(now, list);
            return list;
        }

        /// <summary>Small tags in the corner of the pawn's colonist-bar box, with the full title on hover.</summary>
        internal static void Draw(Rect box, Pawn pawn)
        {
            var badges = For(pawn);
            if (badges.Count == 0) return;
            var oldFont = Text.Font; var oldAnchor = Text.Anchor; var oldColor = GUI.color;
            Text.Font = GameFont.Tiny; Text.Anchor = TextAnchor.MiddleCenter;
            float x = box.x + 2f, y = box.y + 2f;
            foreach (var b in badges.Take(3))
            {
                var tag = new Rect(x, y, 20f, 13f);
                GUI.color = new Color(0f, 0f, 0f, .65f);
                Widgets.DrawBoxSolid(tag, new Color(0f, 0f, 0f, .65f));
                GUI.color = b.Color;
                Widgets.Label(tag, b.Glyph);
                GUI.color = oldColor;
                TooltipHandler.TipRegion(tag, b.Title);
                y += 14f;
            }
            Text.Font = oldFont; Text.Anchor = oldAnchor; GUI.color = oldColor;
        }
    }

    /// <summary>Draws the family roles on each colonist's box in the colonist bar.</summary>
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
