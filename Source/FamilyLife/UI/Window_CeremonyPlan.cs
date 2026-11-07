using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using UnityEngine;
using Verse;

namespace FamilyLife
{
    /// <summary>
    /// The one neutral planning card for a wedding, funeral, reunion or coming of age. It only arranges the practical side (when, who leads,
    /// where, who is asked). Ignoring it is fine: the family goes ahead with the choices already shown here.
    /// </summary>
    public class Window_CeremonyPlan : Window
    {
        private readonly int planId;
        private Vector2 scroll;
        private float scheduleValue = -1f;
        private float listHeight;

        public Window_CeremonyPlan(int planId)
        {
            this.planId = planId;
            doCloseX = true; forcePause = false; absorbInputAroundWindow = false; draggable = true; closeOnClickedOutside = false;
        }

        public override Vector2 InitialSize => new Vector2(580f, 640f);

        public override void DoWindowContents(Rect inRect)
        {
            var component = FamilyComponent.Current;
            var registry = component?.Registry;
            var plan = registry?.CeremonyPlans.FirstOrDefault(p => p.Id == planId);
            var request = plan == null ? null : registry.GatheringRequests.FirstOrDefault(r => r.Id == plan.RequestId);
            if (plan == null || request == null) { Close(); return; }
            int viewer = component.PerspectivePawnId, now = Activities.Now;
            var ctx = Governance.PlanContextFor(component, plan.FamilyId);
            bool editable = plan.Open && HasAuthority(registry, plan, viewer);

            float y = 0f;
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, y, inRect.width, 32f), ("WFL_Gathering_Title_" + LordJob_FamilyGathering.ReportKey(request.Kind)).Translate());
            Text.Font = GameFont.Small; y += 34f;
            string note = plan.Open ? "WFL_Plan_OpenNote".Translate(FamilyUi.Date(plan.DeadlineTick)).ToString() : ("WFL_PlanState_" + plan.State).Translate().ToString();
            GUI.color = Color.gray;
            float noteHeight = Text.CalcHeight(note, inRect.width);
            Widgets.Label(new Rect(0f, y, inRect.width, noteHeight), note); GUI.color = Color.white; y += noteHeight + 6f;

            // When
            if (scheduleValue < 0f) scheduleValue = plan.ScheduledTick;
            Widgets.Label(new Rect(0f, y, 160f, 26f), "WFL_Plan_When".Translate());
            Widgets.Label(new Rect(inRect.width - 220f, y, 220f, 26f), FamilyUi.Date((int)scheduleValue));
            float low = Mathf.Max(plan.EarliestTick, now), high = Mathf.Max(low + 1f, plan.LatestTick);
            GUI.enabled = editable;
            float chosen = Widgets.HorizontalSlider(new Rect(0f, y + 26f, inRect.width, 22f), Mathf.Clamp(scheduleValue, low, high), low, high);
            GUI.enabled = true;
            if (editable && Mathf.Abs(chosen - scheduleValue) >= 50f) { scheduleValue = chosen; registry.SetPlanSchedule(plan.Id, (int)chosen, now); }
            else if (!editable) scheduleValue = plan.ScheduledTick;
            y += 56f;

            // Who leads
            Widgets.Label(new Rect(0f, y, 160f, 28f), "WFL_Plan_Organizer".Translate());
            if (ActionButtonLocal(new Rect(170f, y, inRect.width - 170f, 28f), FamilyUi.NameOf(registry, plan.OrganizerId), editable))
            {
                var options = registry.MembersOf(plan.FamilyId).Where(p => p.InColony && !p.Juvenile).Take(40).Select(p => new FloatMenuOption(FamilyUi.NameOf(registry, p.Id), () =>
                {
                    if (!registry.SetPlanOrganizer(plan.Id, p.Id, ctx)) Messages.Message("WFL_Plan_CannotLead".Translate(FamilyUi.NameOf(registry, p.Id)), MessageTypeDefOf.RejectInput, false);
                })).ToList();
                if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
            }
            y += 34f;

            // Where
            Widgets.Label(new Rect(0f, y, 160f, 28f), "WFL_Plan_Venue".Translate());
            var venueMap=Find.Maps.FirstOrDefault(m=>m.uniqueID==plan.VenueMapId);
            var venueArea=venueMap?.areaManager.AllAreas.FirstOrDefault(a=>a.ID==plan.VenueAreaId);
            string venue = venueMap==null ? "WFL_Plan_VenueDefault".Translate().ToString() : venueArea==null
                ? "WFL_Plan_VenueMap".Translate(plan.VenueMapId).ToString() : "WFL_Plan_VenueArea".Translate(venueArea.Label,plan.VenueMapId).ToString();
            if (ActionButtonLocal(new Rect(170f, y, inRect.width - 170f, 28f), venue, editable))
            {
                var options = new List<FloatMenuOption> { new FloatMenuOption("WFL_Plan_VenueDefault".Translate(), () => registry.SetPlanVenue(plan.Id, ctx.DefaultMapId, 0, ctx)) };
                foreach (var map in Find.Maps.Where(m => m.IsPlayerHome))
                {
                    int id = map.uniqueID; options.Add(new FloatMenuOption("WFL_Plan_VenueMap".Translate(id), () => registry.SetPlanVenue(plan.Id, id, 0, ctx)));
                    foreach(var area in FamilyZones.Areas(map).Take(20))
                    {int areaId=area.ID;string label=area.Label;options.Add(new FloatMenuOption("WFL_Plan_VenueArea".Translate(label,id),()=>registry.SetPlanVenue(plan.Id,id,areaId,ctx)));}
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            y += 34f;

            // The avatar is asked, never assumed.
            var avatar = registry.MembersOf(plan.FamilyId).FirstOrDefault(p => p.InColony && !p.Juvenile && Governance.IsAvatar(component, p.Id));
            if (avatar != null)
            {
                string who = FamilyUi.NameOf(registry, avatar.Id);
                if (plan.Ask == AvatarAsk.Asked && plan.AvatarId == avatar.Id && viewer == avatar.Id)
                {
                    Widgets.Label(new Rect(0f, y, inRect.width - 260f, 28f), "WFL_Plan_AvatarAsked".Translate(who));
                    if (Widgets.ButtonText(new Rect(inRect.width - 250f, y, 120f, 28f), "WFL_Plan_Accept".Translate())) registry.AnswerAvatar(plan.Id, avatar.Id, true, ctx);
                    if (Widgets.ButtonText(new Rect(inRect.width - 124f, y, 120f, 28f), "WFL_Plan_Decline".Translate())) registry.AnswerAvatar(plan.Id, avatar.Id, false, ctx);
                }
                else if (plan.Open && plan.Ask == AvatarAsk.None || plan.Ask == AvatarAsk.Declined || plan.Ask == AvatarAsk.Unanswered)
                {
                    if (ActionButtonLocal(new Rect(0f, y, inRect.width, 28f), "WFL_Plan_AskAvatar".Translate(who), plan.Open && viewer != avatar.Id)) registry.AskAvatarToLead(plan.Id, avatar.Id, ctx);
                }
                else Widgets.Label(new Rect(0f, y, inRect.width, 28f), ("WFL_PlanAsk_" + plan.Ask).Translate(who));
                y += 34f;
            }

            // Who is asked, with the reason for anyone who cannot be.
            Widgets.Label(new Rect(0f, y, inRect.width, 26f), "WFL_Plan_Guests".Translate()); y += 28f;
            var guestArea = new Rect(0f, y, inRect.width, inRect.height - y - 44f);
            if (!registry.PlanVisibleTo(plan, viewer)) Widgets.Label(guestArea, "WFL_Plan_NotTold".Translate());
            else
            {
                var candidates = registry.PlanCandidates(request, plan, now, ctx);
                foreach (var extra in plan.Invitees.Where(i => candidates.All(c => c.PersonId != i.PersonId))) candidates.Add(extra);
                var view = new Rect(0f, 0f, guestArea.width - 16f, Mathf.Max(listHeight, guestArea.height));
                Widgets.BeginScrollView(guestArea, ref scroll, view);
                float ly = 0f;
                foreach (var c in candidates.OrderBy(c => c.Barriers != InviteBarrier.None).ThenBy(c => c.Source).ThenBy(c => c.PersonId).Take(64))
                {
                    var current = plan.Invitees.FirstOrDefault(i => i.PersonId == c.PersonId);
                    bool on = current != null && current.Status == InviteStatus.Invited;
                    bool blocked = c.Barriers != InviteBarrier.None && !on;
                    var row = new Rect(0f, ly, view.width, 26f);
                    bool value = on;
                    GUI.enabled = editable && !blocked;
                    Widgets.Checkbox(row.x + 2f, row.y + 1f, ref value, 24f, false, true);
                    GUI.enabled = true;
                    if (value != on && editable && !blocked)
                    {
                        if (value) registry.InvitePlanPerson(plan.Id, c.PersonId, now, ctx);
                        else registry.UninvitePlanPerson(plan.Id, c.PersonId);
                    }
                    string label = FamilyUi.NameOf(registry, c.PersonId) + "  (" + ("WFL_InviteSource_" + c.Source).Translate() + ")";
                    if (c.Barriers != InviteBarrier.None) label += "  — " + BarrierText(c.Barriers);
                    else if (c.ArrivalTick > now) label += "  — " + "WFL_Plan_Arrives".Translate(FamilyUi.Date(c.ArrivalTick));
                    if (blocked) GUI.color = Color.gray;
                    Widgets.Label(new Rect(32f, ly, view.width - 32f, 26f), label);
                    GUI.color = Color.white;
                    ly += 26f;
                }
                listHeight = ly;
                Widgets.EndScrollView();
            }

            var confirm = new Rect(0f, inRect.height - 36f, 200f, 32f);
            if (plan.Open && ActionButtonLocal(confirm, "WFL_Plan_Confirm".Translate(), editable)) { registry.ConfirmPlan(plan.Id, now, ctx); Close(); }
            if (Widgets.ButtonText(new Rect(inRect.width - 160f, inRect.height - 36f, 160f, 32f), "WFL_Plan_Close".Translate())) Close();
        }

        private static bool HasAuthority(FamilyRegistry registry, CeremonyPlan plan, int viewer) => viewer == 0 || registry.CanManageHouse(viewer, plan.FamilyId);

        private static bool ActionButtonLocal(Rect rect, string label, bool allowed)
        {
            bool previous = GUI.enabled;
            GUI.enabled = previous && allowed;
            bool clicked = Widgets.ButtonText(rect, label);
            GUI.enabled = previous;
            return clicked;
        }

        private static string BarrierText(InviteBarrier barriers)
        {
            var parts = new List<string>();
            foreach (InviteBarrier flag in System.Enum.GetValues(typeof(InviteBarrier)))
                if (flag != InviteBarrier.None && barriers.HasFlag(flag)) parts.Add(("WFL_InviteBarrier_" + flag).Translate().ToString());
            return string.Join(", ", parts);
        }
    }
}
