using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Personal possessions, heirlooms and estates, as the person looking is able to know them. Nobody here can "assign" an inheritance: a player acts through the
    /// pawn they are playing (name an heir, declare an heirloom, answer a question), and the game never answers for them.
    /// </summary>
    public partial class Window_Families
    {
        private static string ClaimReasons(FamilyRegistry registry, EstateCase estate, EstateClaim claim, int viewer)
        {
            var parts = new List<string>();
            foreach (ClaimBasis flag in System.Enum.GetValues(typeof(ClaimBasis)))
            {
                if (flag == ClaimBasis.None || !claim.Basis.HasFlag(flag)) continue;
                // A designation counts for as much as people knew of it; only those who knew of it are shown it.
                if (flag == ClaimBasis.Designation && viewer != 0 && !registry.Beneficiaries.Any(b => b.OwnerId == estate.DeceasedId && b.HeirId == claim.ClaimantId && b.KnownBy.Contains(viewer))) continue;
                parts.Add(("WFL_ClaimBasis_" + flag).Translate().ToString());
            }
            return string.Join(", ", parts);
        }

        private void DrawInheritance(Rect view, ref float y, FamilyComponent component, FamilyRegistry registry, Lineage lineage)
        {
            int viewer = component.PerspectivePawnId;
            int now = Activities.Now;
            Heading(view, ref y, "WFL_Estate_Title".Translate());

            // What members own: heirlooms are public (they are worn and handed down in plain sight); ordinary things only to their owner and the overview.
            var owners = registry.MembersOf(lineage.Id).Where(p => p.InColony && !p.Juvenile).Select(p => p.Id).ToHashSet();
            var items = registry.PersonalItems.Where(i => (i.OwnerId != 0 && owners.Contains(i.OwnerId) && (i.Heirloom || viewer == 0 || i.OwnerId == viewer)) || (i.OwnerId == 0 && i.HouseId == lineage.Id))
                .OrderByDescending(i => i.Heirloom).ThenByDescending(i => i.Value).Take(10).ToList();
            if (items.Count == 0) Line(view, ref y, "WFL_Estate_NoItems".Translate(), 10f, true);
            bool head = viewer != 0 && registry.HeadOf(lineage.Id) == viewer;
            foreach (var item in items)
            {
                string holder = item.OwnerId == 0 ? "WFL_Estate_HouseKeeps".Translate(registry.LineageDisplayName(lineage)).ToString() : FamilyUi.NameOf(registry, item.OwnerId);
                string kind = item.Heirloom ? ("WFL_HeirloomBasis_" + item.Basis).Translate().ToString() : "WFL_Estate_Possession".Translate().ToString();
                Line(view, ref y, "WFL_Estate_ItemLine".Translate(item.Label, holder, kind), 10f, true);
                float x = 20f; bool buttons = false;
                int thing = item.ThingId;
                if (viewer != 0 && item.OwnerId == viewer)
                {
                    if (!item.Heirloom && Widgets.ButtonText(new Rect(x, y, 170f, 26f), "WFL_Estate_MakeHeirloom".Translate())) registry.DesignateHeirloom(thing, viewer, now);
                    if (!item.Heirloom) x += 176f;
                    if (Widgets.ButtonText(new Rect(x, y, 150f, 26f), "WFL_Estate_NameHeir".Translate())) OpenHeirMenu(registry, viewer, thing, now);
                    buttons = true;
                }
                else if (head && item.OwnerId != 0 && !item.Heirloom)
                {
                    if (Widgets.ButtonText(new Rect(x, y, 200f, 26f), "WFL_Estate_MakeHouseHeirloom".Translate())) registry.DesignateHeirloom(thing, viewer, now);
                    buttons = true;
                }
                if (buttons) y += 30f;
            }
            if (viewer != 0)
                foreach (var b in registry.Beneficiaries.Where(b => b.OwnerId == viewer).Take(3))
                {
                    string what = b.ThingId == 0 ? "WFL_Estate_Everything".Translate().ToString() : registry.PersonalItemFor(b.ThingId)?.Label ?? "?";
                    Line(view, ref y, "WFL_Estate_Named".Translate(FamilyUi.NameOf(registry, b.HeirId), what), 20f, true);
                }

            var cases = registry.Estates.Where(e => e.FamilyId == lineage.Id && registry.EstateVisibleTo(e, viewer)).OrderByDescending(e => e.Open).ThenByDescending(e => e.OpenedTick).Take(5).ToList();
            foreach (var estate in cases)
            {
                string mediator = estate.MediatorId > 0 ? "WFL_Estate_Mediator".Translate(FamilyUi.NameOf(registry, estate.MediatorId)).ToString() : "";
                Line(view, ref y, "WFL_Estate_CaseLine".Translate(FamilyUi.NameOf(registry, estate.DeceasedId), ("WFL_EstateStage_" + estate.Stage).Translate(), mediator), 10f, true);
                foreach (var item in estate.Items.Take(8))
                {
                    string status;
                    switch (item.Outcome)
                    {
                        case ItemOutcome.Lost: status = ("WFL_EstateLost_" + item.Lost).Translate(); break;
                        case ItemOutcome.Delivered: status = "WFL_EstateItem_Delivered".Translate(item.HeirId != 0 ? FamilyUi.NameOf(registry, item.HeirId) : registry.LineageDisplayName(registry.GetLineage(item.RecipientHouseId))); break;
                        case ItemOutcome.Assigned:
                        case ItemOutcome.Delivering:
                            status = "WFL_EstateItem_Pending".Translate(item.HeirId != 0 ? FamilyUi.NameOf(registry, item.HeirId) : registry.LineageDisplayName(registry.GetLineage(item.RecipientHouseId))); break;
                        default: status = item.Contested ? "WFL_EstateItem_Contested".Translate() : "WFL_EstateItem_Waiting".Translate(); break;
                    }
                    Line(view, ref y, "WFL_EstateItem_Line".Translate(item.Label, status), 20f, true);
                    if (item.Contested || item.Outcome == ItemOutcome.Pending)
                        foreach (var claim in item.Claims.Where(c => !c.Relinquished).Take(4))
                        {
                            string reasons = ClaimReasons(registry, estate, claim, viewer);
                            if (reasons.Length > 0) Line(view, ref y, "WFL_Estate_ClaimLine".Translate(FamilyUi.NameOf(registry, claim.ClaimantId), reasons), 30f, true);
                        }
                }
            }
        }

        /// <summary>A short history: how each finished estate came out, without the working.</summary>
        private void DrawEstateHistory(Rect view, ref float y, FamilyRegistry registry, Lineage lineage, int viewer)
        {
            var done = registry.Estates.Where(e => e.FamilyId == lineage.Id && !e.Open && registry.EstateVisibleTo(e, viewer)).OrderByDescending(e => e.ClosedTick).Take(4).ToList();
            if (done.Count == 0) return;
            Heading(view, ref y, "WFL_Estate_History".Translate());
            foreach (var e in done)
                Line(view, ref y, "WFL_Estate_HistoryLine".Translate(FamilyUi.NameOf(registry, e.DeceasedId), e.Items.Count(i => i.Outcome == ItemOutcome.Delivered), e.Items.Count(i => i.Outcome == ItemOutcome.Lost),
                    ("WFL_EstateStage_" + e.Stage).Translate()), 10f, true);
        }

        private void OpenHeirMenu(FamilyRegistry registry, int owner, int thing, int now)
        {
            var me = registry.GetPerson(owner);
            if (me == null) return;
            var kin = me.PartnerIds().Concat(registry.ChildrenOf(owner)).Concat(me.ParentIds).Concat(registry.SiblingsOf(owner)).Distinct()
                .Select(registry.GetPerson).Where(p => p != null && p.InColony && !p.Juvenile).Take(6).ToList();
            var options = new List<FloatMenuOption>();
            foreach (var p in kin)
            {
                int heir = p.Id;
                options.Add(new FloatMenuOption("WFL_Estate_NameAndTell".Translate(FamilyUi.NameOf(registry, heir)), () => registry.DesignateBeneficiary(owner, heir, thing, new[] { heir }, now)));
                options.Add(new FloatMenuOption("WFL_Estate_NameSecretly".Translate(FamilyUi.NameOf(registry, heir)), () => registry.DesignateBeneficiary(owner, heir, thing, null, now)));
            }
            if (registry.Beneficiaries.Any(b => b.OwnerId == owner && b.ThingId == thing)) options.Add(new FloatMenuOption("WFL_Estate_Revoke".Translate(), () => registry.RevokeBeneficiary(owner, thing)));
            if (options.Count == 0) options.Add(new FloatMenuOption("WFL_Estate_NoKin".Translate(), null));
            Find.WindowStack.Add(new FloatMenu(options));
        }
    }
}
