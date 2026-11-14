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
    /// <summary>Draws one career as a left-to-right tree: entry roles on the left, each qualification a step to the right. Like a family tree, the lines show who may follow whom.</summary>
    internal static class ProfessionTree
    {
        private const float NodeH = 78f, GapY = 12f, GapX = 26f;

        internal static List<ProfessionRole> ParentsOf(CareerDefinition def, List<ProfessionRole> roles, ProfessionRole role)
        {
            if (role.Level <= 1) return new List<ProfessionRole>();
            CareerRule rule = null;
            if (def != null && !string.IsNullOrEmpty(role.Specialty)) { if (!def.Level2Branches.TryGetValue(role.Specialty, out rule)) def.Specialties.TryGetValue(role.Specialty, out rule); }
            var named = rule == null ? new List<ProfessionRole>() : rule.RequiresAny.SelectMany(q => roles.Where(x => x != role && x.Specialty == q)).Distinct().ToList();
            if (named.Count > 0) return named;
            var below = roles.Where(x => x.Level == role.Level - 1).ToList();
            if (below.Count == 0) below = roles.Where(x => x.Level < role.Level).ToList();
            return below;
        }

        internal static Dictionary<ProfessionRole, int> Depths(CareerDefinition def, List<ProfessionRole> roles)
        {
            var depth = new Dictionary<ProfessionRole, int>();
            Func<ProfessionRole, int, int> find = null;
            find = (x, guard) =>
            {
                if (depth.TryGetValue(x, out var d)) return d;
                if (guard > 8) return 0;
                var ps = ParentsOf(def, roles, x); int v = ps.Count == 0 ? 0 : ps.Max(p => find(p, guard + 1)) + 1;
                depth[x] = v; return v;
            };
            foreach (var x in roles) find(x, 0);
            return depth;
        }

        public static float Draw(Rect r, float y, FamilyComponent c, string career, List<ProfessionRole> roles, string search)
        {
            var reg = c.Registry; var book = reg.Assignments(); var ledger = reg.Careers(); var def = CareerService.Career(career);
            var depth = Depths(def, roles); int cols = depth.Values.DefaultIfEmpty(0).Max() + 1;
            float nodeW = Mathf.Clamp((r.width - GapX * (cols - 1)) / cols, 110f, 210f);
            var residents = reg.Persons.Where(p => p.InColony && !p.Juvenile).OrderBy(p => CommunityCorePages.NameOf(c, p.Id)).ToList();
            var pos = new Dictionary<ProfessionRole, Rect>(); var rowsUsed = 0f;
            for (int col = 0; col < cols; col++)
            {
                var inCol = roles.Where(x => depth[x] == col).OrderBy(x => { var ps = ParentsOf(def, roles, x).Where(p => pos.ContainsKey(p)).ToList(); return ps.Count == 0 ? 1000f : ps.Average(p => pos[p].y); }).ThenBy(x => x.Id, StringComparer.Ordinal).ToList();
                float cy = y;
                foreach (var x in inCol)
                {
                    var parents = ParentsOf(def, roles, x).Where(p => pos.ContainsKey(p)).ToList();
                    if (parents.Count > 0) cy = Mathf.Max(cy, parents.Average(p => pos[p].y));
                    pos[x] = new Rect(col * (nodeW + GapX), cy, nodeW, NodeH); cy += NodeH + GapY;
                }
                rowsUsed = Mathf.Max(rowsUsed, cy);
            }
            var line = new Color(.55f, .5f, .35f, .8f);
            foreach (var x in roles) foreach (var p in ParentsOf(def, roles, x)) if (pos.ContainsKey(p) && pos.ContainsKey(x))
            {
                Vector2 a = new Vector2(pos[p].xMax, pos[p].center.y), b = new Vector2(pos[x].x, pos[x].center.y), m = new Vector2((a.x + b.x) / 2f, 0f);
                Widgets.DrawLine(a, new Vector2(m.x, a.y), line, 2f); Widgets.DrawLine(new Vector2(m.x, a.y), new Vector2(m.x, b.y), line, 2f); Widgets.DrawLine(new Vector2(m.x, b.y), b, line, 2f);
            }
            foreach (var x in roles) Node(pos[x], c, reg, book, ledger, def, x, residents, search);
            return rowsUsed + 4f;
        }

        private static void Node(Rect n, FamilyComponent c, FamilyRegistry reg, AssignmentBook book, CareerLedger ledger, CareerDefinition def, ProfessionRole role, List<PersonRecord> residents, string search)
        {
            var holders = book.Holders(role.Id).Where(id => reg.GetPerson(id)?.InColony == true).ToList();
            string title = CareerService.TitleOf(role, reg);
            bool match = search.Length == 0 || title.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
            int qualified = residents.Count(p => !book.IsAssigned(p.Id, role.Id) && AssignmentBook.Check(role, ledger, p.Id) == AssignResult.Assigned);
            Color accent = holders.Count > 0 ? FamilyKit.Gold : qualified > 0 ? new Color(.45f, .7f, .5f) : new Color(.4f, .4f, .42f);
            if (!match) GUI.color = new Color(1f, 1f, 1f, .35f);
            Widgets.DrawBoxSolid(n, new Color(.16f, .16f, .17f)); Widgets.DrawBoxSolid(new Rect(n.x, n.y, 4f, n.height), accent);
            var inner = new Rect(n.x + 10f, n.y + 4f, n.width - 14f, n.height - 8f);
            Text.Font = GameFont.Small; Text.WordWrap = false;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f), title.Truncate(inner.width));
            GUI.color = match ? Color.gray : new Color(.5f, .5f, .5f, .35f);
            string who = holders.Count == 0 ? "WFL_Hub_Vacant".Translate().ToString() : string.Join(", ", holders.Select(id => CommunityCorePages.NameOf(c, id)));
            Widgets.Label(new Rect(inner.x, inner.y + 20f, inner.width, 20f), who.Truncate(inner.width));
            Text.WordWrap = true; GUI.color = Color.white;
            string tip = who + "\n" + "WFL_Hub_Qualified".Translate(role.Level, qualified);
            if (def != null) { var rule = RuleOf(def, role); if (rule.Skills.Count > 0) tip += "\n" + string.Join(", ", rule.Skills.Select(s => s.Key + " " + s.Value)); }
            if (!string.IsNullOrEmpty(role.BonusKey)) tip += "\n" + "WFL_Career_Bonus".Translate(role.BonusKey.Translate());
            TooltipHandler.TipRegion(new Rect(n.x, n.y, n.width, 42f), tip);
            if (Widgets.ButtonText(new Rect(inner.x, inner.yMax - 26f, inner.width, 24f), "WFL_Career_Assign".Translate() + (qualified > 0 ? " (" + qualified + ")" : ""))) Find.WindowStack.Add(new FloatMenu(Options(c, book, ledger, role, residents)));
            GUI.color = Color.white;
        }

        private static CareerRule RuleOf(CareerDefinition def, ProfessionRole role)
        {
            if (!string.IsNullOrEmpty(role.Specialty)) { if (def.Level2Branches.TryGetValue(role.Specialty, out var b)) return b; if (def.Specialties.TryGetValue(role.Specialty, out var s)) return s; }
            return def.Level2;
        }

        private static List<FloatMenuOption> Options(FamilyComponent c, AssignmentBook book, CareerLedger ledger, ProfessionRole role, List<PersonRecord> residents)
        {
            var options = new List<FloatMenuOption>();
            foreach (var p in residents)
            {
                int id = p.Id; bool already = book.IsAssigned(id, role.Id); var check = AssignmentBook.Check(role, ledger, id);
                string label = CommunityCorePages.NameOf(c, id) + (already ? " (" + "WFL_Career_Assigned".Translate() + ")" : check != AssignResult.Assigned ? " (" + "WFL_Career_Needs_Short".Translate() + ")" : "");
                options.Add(new FloatMenuOption(label, already ? (Action)(() => book.Unassign(id, role.Id)) : check == AssignResult.Assigned ? () => book.Assign(id, role, ledger, Find.TickManager.TicksGame) : (Action)null));
            }
            if (options.Count == 0) options.Add(new FloatMenuOption("WFL_ZoneNoFamilies".Translate(), null));
            return options;
        }
    }
}
