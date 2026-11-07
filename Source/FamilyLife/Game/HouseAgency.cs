using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
namespace FamilyLife
{
    /// <summary>
    /// How a house's people shape it. Generic admission, self-promotion and trust-based house mergers are deliberately absent: membership now
    /// comes from kinship/marriage or explicit player authority, while a head may still recognize sustained service by appointing a steward.
    /// A selected pawn and the Perspective Shift avatar are left alone.
    /// </summary>
    internal static class HouseAgency
    {
        private const int Interval = 2500;      // about an in-game hour between sweeps
        private static int nextSweep;

        public static void Reset() { nextSweep = 0; }

        /// <summary>The rule an invited pawn answers by; shared with the interaction itself.</summary>
        internal static bool WouldAcceptInvitation(FamilyRegistry r, int invitee, int head, int family)
        {
            float trust = r.GetBond(invitee, head)?.Trust ?? 0, attachment = r.GetBond(invitee, head)?.Attachment ?? 0;
            return (trust >= 40 || (r.ContactOf(invitee, family)?.Respect ?? 0) >= 6) &&
                   (r.FamilyOf(invitee) == 0 || r.AssessBelonging(invitee).Attachment <= attachment + 15);
        }

        private static bool Wants(FamilyRegistry r, int person, int family, PetitionKind kind)
        {
            int head = r.HeadOf(family);
            if (head == 0 || head == person) return false;
            var q = new HousePetition { PersonId = person, FamilyId = family, Kind = kind };
            if (!r.WouldAcceptPetition(q, head)) return false;
            if (kind != PetitionKind.Admission) return true;
            float attachment = r.GetBond(person, head)?.Attachment ?? 0;
            return r.FamilyOf(person) == 0 || r.AssessBelonging(person).Attachment <= attachment + 15;
        }

        public static void Tick(FamilyComponent c, int now)
        {
            if (now < nextSweep) return;
            nextSweep = now + Interval + Rand.Range(0, 400);
            var settings = FamilyLifeMod.Settings;
            if (settings == null || !settings.familyStories) return;
            try
            {
                var r = c.Registry;
                var people = r.Persons.Where(p => p.InColony && !p.Juvenile).ToList();
                if (people.Count < 2) return;
                foreach (var person in people.InRandomOrder())
                {
                    var pawn = c.TryGetPawn(person.Id);
                    if (pawn == null || !pawn.Spawned || Find.Selector.IsSelected(pawn) || PerspectiveShiftBridge.IsAvatar(pawn)) continue;
                    if (TryAct(c, r, people, person, pawn)) return;     // one thing per sweep keeps a colony from feeling like a committee
                }
            }
            catch (System.Exception ex) { HookSafety.Report("house agency", ex); }
        }

        private static bool TryAct(FamilyComponent c, FamilyRegistry r, List<PersonRecord> people, PersonRecord me, Pawn pawn)
        {
            int mine = r.FamilyOf(me.Id);
            bool head = mine != 0 && r.HeadOf(mine) == me.Id;

            if(mine!=0&&r.CanManageHouse(me.Id,mine)&&Rand.Chance(.25f))
            {
                var alliance=r.MarriageAlliances.FirstOrDefault(a=>(a.FamilyA==mine||a.FamilyB==mine)&&a.State==MarriageAllianceState.Opportunity);
                if(alliance!=null&&MarriageAlliancePlanner.StartNegotiation(c,alliance,pawn))return true;
            }

            if (head)
            {
                // Standing is given, not asked for: a head honors a member who has earned their trust and the house's respect.
                if (Rand.Chance(.2f) && (r.GetLineage(mine)?.Stewards.Count ?? 0) < 2)
                {
                    var honored = people.Where(o => o.Id != me.Id && r.FamilyOf(o.Id) == mine && r.RoleInHouse(o.Id, mine) == HouseRole.Member &&
                            Wants(r, o.Id, mine, PetitionKind.Recognition))
                        .OrderByDescending(o => r.GetBond(o.Id, me.Id)?.Trust ?? 0).Select(o => c.TryGetPawn(o.Id)).FirstOrDefault(o => o != null && o.Spawned);
                    if (honored != null && HouseInteractions.StartPetition(pawn, mine, PetitionKind.Recognition, true, honored)) return true;
                }
                return false;
            }
            return false;
        }
    }

    /// <summary>
    /// The player's way into the same things: select a colonist, right-click another colonist, and the family actions that pawn could take toward
    /// that one appear in the menu. Nothing here is a panel; the player is playing one pawn, and the pawn does the asking.
    /// </summary>
    public class FloatMenuOptionProvider_FamilyHouse : FloatMenuOptionProvider
    {
        protected override bool Drafted => false;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override bool AppliesInt(FloatMenuContext context)
        {
            var s = FamilyLifeMod.Settings;
            return s != null && s.familyStories && FamilyComponent.Current != null;
        }

        public override IEnumerable<FloatMenuOption> GetOptionsFor(Pawn clicked, FloatMenuContext context)
        {
            var actor = context.FirstSelectedPawn;
            var c = FamilyComponent.Current;
            if (c == null || actor == null || clicked == null || !PerspectiveShiftBridge.CanActAs(actor) || !actor.IsColonistPlayerControlled || !clicked.IsColonist || clicked.HostFaction != null) yield break;
            var r = c.Registry;
            var me = r.GetPerson(actor.thingIDNumber); var them = r.GetPerson(clicked.thingIDNumber);
            if (me == null || them == null) yield break;
            int myFamily=r.FamilyOf(me.Id),theirFamily=r.FamilyOf(them.Id);
            string b = clicked.LabelShort;
            if(actor!=clicked)
                yield return Act("WFL_Act_SocialMoment".Translate(b),()=>SocialMoments.TryPlayerMoment(actor,clicked));
            var rift=r.OpenRiftBetween(me.Id,them.Id);
            if(rift!=null&&FamilyLifeMod.Settings.conflicts&&r.MayTryReconcile(rift.Id,Activities.Now))
                yield return Act("WFL_Act_MakePeace".Translate(b),()=>HouseInteractions.StartRiftRepair(actor,clicked));
            var repair=r.Incidents.Where(i=>!i.Resolved&&i.ActorId==them.Id&&i.KnownBy.Contains(me.Id)&&
                (i.VictimId==me.Id||i.VictimFamily==myFamily&&myFamily!=0)).OrderByDescending(i=>i.Tick).FirstOrDefault();
            if(repair!=null)
            {
                int incidentId=repair.Id;
                yield return Act("WFL_Act_DiscussIncident".Translate(b),()=>HouseInteractions.StartIncidentRepair(actor,clicked,incidentId));
            }
            if(me.Juvenile)yield break;
            bool iLead = myFamily != 0 && r.HeadOf(myFamily) == me.Id, theyLead = theirFamily != 0 && r.HeadOf(theirFamily) == them.Id;

            foreach(var alliance in r.MarriageAlliances.Where(a=>a.State==MarriageAllianceState.Opportunity&&(a.FamilyA==myFamily||a.FamilyB==myFamily)))
                if(r.CanManageHouse(me.Id,myFamily)&&actor!=clicked&&MarriageAlliancePlanner.Representative(r,alliance.FamilyA==myFamily?alliance.FamilyB:alliance.FamilyA)==them.Id)
                    yield return Act("WFL_Act_NegotiateMarriage".Translate(b),()=>MarriageAlliancePlanner.StartNegotiation(c,alliance,actor,true));
            foreach(var alliance in r.MarriageAlliances.Where(a=>a.State==MarriageAllianceState.Negotiating&&
                (System.Math.Abs(a.NegotiatorA)==me.Id||System.Math.Abs(a.NegotiatorB)==me.Id)))
            {
                var pending=alliance;
                int counterpart=System.Math.Abs(alliance.NegotiatorA)==me.Id?System.Math.Abs(alliance.NegotiatorB):System.Math.Abs(alliance.NegotiatorA);
                if(them.Id!=counterpart)continue;
                yield return Act("WFL_Act_SupportMarriage".Translate(),()=>MarriageAlliancePlanner.DecideAs(c,pending,actor,true));
                yield return Act("WFL_Act_OpposeMarriage".Translate(),()=>MarriageAlliancePlanner.DecideAs(c,pending,actor,false));
            }

            // A petition waiting on this pawn's word.
            foreach (var q in r.HousePetitions.Where(q => q.State == PetitionState.Heard && q.PersonId == them.Id && q.HeardBy == me.Id && r.HeadOf(q.FamilyId) == me.Id))
            {
                var petition = q;
                string what = ("WFL_PetitionKind_" + q.Kind).Translate();
                yield return new FloatMenuOption("WFL_Act_Accept".Translate(b, what), () => HouseInteractions.Decide(c, petition, me.Id, true, clicked, actor));
                yield return new FloatMenuOption("WFL_Act_Decline".Translate(b, what), () => HouseInteractions.Decide(c, petition, me.Id, false, clicked, actor));
            }

            if (!them.Juvenile)
            {
                // A Steward is an office granted by the head for demonstrated service; members do not ask for standing.
                if (iLead && myFamily == theirFamily)
                {
                    var house = r.GetLineage(myFamily); bool steward = house.Stewards.Contains(them.Id);
                    yield return new FloatMenuOption((steward ? "WFL_Act_Revoke" : "WFL_Act_Appoint").Translate(b), () => { r.DelegateHouse(me.Id, them.Id, myFamily, !steward, Activities.Now); c.MarkDirty(); });
                }
                if (iLead && myFamily == theirFamily && them.Id != me.Id)
                    yield return new FloatMenuOption("WFL_Act_Release".Translate(b), () => { r.SetSocialFamily(them.Id, -1, Activities.Now); c.FamilyMembershipChanged(); });

                // Help between houses.
                if (theyLead && theirFamily != myFamily && myFamily != 0)
                {
                    yield return Act("WFL_Act_AskFood".Translate(b), () => c.Economy.RequestAid(actor, theirFamily, true));
                    yield return Act("WFL_Act_OfferFood".Translate(b), () => c.Economy.OfferAid(actor, theirFamily, true));
                }
            }
        }

        private static FloatMenuOption Act(string label, System.Func<bool> run)
        {
            return new FloatMenuOption(label, () => { if (!run()) Messages.Message("WFL_House_ActionUnavailable".Translate(), MessageTypeDefOf.NeutralEvent, false); });
        }
    }
}
