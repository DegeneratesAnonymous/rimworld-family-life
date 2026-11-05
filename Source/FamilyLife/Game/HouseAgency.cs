using System.Collections.Generic;
using System.Linq;
using FamilyLife.Domain;
using RimWorld;
using Verse;
namespace FamilyLife
{
    /// <summary>
    /// How a house's people shape it. Colonists act on their own: a pawn who has earned a place asks to join, a head invites someone they have
    /// come to trust, a trusted member seeks recognition, the head of a small house proposes uniting with another. Each is the same walk-over-and-talk
    /// the player can start by hand (select a pawn, right-click another: <see cref="FloatMenuOptionProvider_FamilyHouse"/>), and a pawn the player has
    /// selected is left alone, so the player is never fighting the colony for the same pawn.
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
                    if (pawn == null || !pawn.Spawned || Find.Selector.IsSelected(pawn)) continue;
                    if (TryAct(c, r, people, person, pawn)) return;     // one thing per sweep keeps a colony from feeling like a committee
                }
            }
            catch (System.Exception ex) { HookSafety.Report("house agency", ex); }
        }

        private static bool TryAct(FamilyComponent c, FamilyRegistry r, List<PersonRecord> people, PersonRecord me, Pawn pawn)
        {
            int mine = r.FamilyOf(me.Id);
            bool head = mine != 0 && r.HeadOf(mine) == me.Id;

            if (head)
            {
                // Invite someone the head has come to trust and who has no better home.
                if (Rand.Chance(.35f))
                {
                    var guest = people.Where(o => o.Id != me.Id && r.FamilyOf(o.Id) != mine && WouldAcceptInvitation(r, o.Id, me.Id, mine))
                        .OrderByDescending(o => r.GetBond(o.Id, me.Id)?.Trust ?? 0).Select(o => c.TryGetPawn(o.Id)).FirstOrDefault(o => o != null && o.Spawned);
                    if (guest != null && HouseInteractions.StartPetition(pawn, mine, PetitionKind.Admission, true, guest)) return true;
                }
                // A small house may seek to join a larger one it trusts.
                if (Rand.Chance(.15f) && r.LivingCount(mine, Presence.Colony) <= 2)
                {
                    foreach (var other in r.Lineages.Where(l => l.Id != mine && l.AbsorbedIntoId == 0 && r.LivingCount(l.Id, Presence.Colony) > r.LivingCount(mine, Presence.Colony)))
                        if (Wants(r, me.Id, other.Id, PetitionKind.Union) && HouseInteractions.StartPetition(pawn, other.Id, PetitionKind.Union)) return true;
                }
                return false;
            }

            // Ask to join a house whose head would take them in.
            if (Rand.Chance(.4f))
            {
                foreach (var house in r.Lineages.Where(l => l.AbsorbedIntoId == 0 && l.Id != mine && r.LivingCount(l.Id, Presence.Colony) > 0))
                    if (Wants(r, me.Id, house.Id, PetitionKind.Admission) && HouseInteractions.StartPetition(pawn, house.Id, PetitionKind.Admission)) return true;
            }
            // A member who has proven themselves seeks a steward's standing.
            if (mine != 0 && Rand.Chance(.2f) && r.RoleInHouse(me.Id, mine) == HouseRole.Member && (r.GetLineage(mine)?.Stewards.Count ?? 0) < 2 &&
                Wants(r, me.Id, mine, PetitionKind.Recognition) && HouseInteractions.StartPetition(pawn, mine, PetitionKind.Recognition)) return true;
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
            if (c == null || actor == null || clicked == null || !actor.IsColonistPlayerControlled || !clicked.IsColonist || clicked.HostFaction != null) yield break;
            var r = c.Registry;
            var me = r.GetPerson(actor.thingIDNumber); var them = r.GetPerson(clicked.thingIDNumber);
            if (me == null || them == null || me.Juvenile) yield break;
            string a = actor.LabelShort, b = clicked.LabelShort;
            int myFamily = r.FamilyOf(me.Id), theirFamily = r.FamilyOf(them.Id);
            bool iLead = myFamily != 0 && r.HeadOf(myFamily) == me.Id, theyLead = theirFamily != 0 && r.HeadOf(theirFamily) == them.Id;

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
                // Joining another house, or bringing someone into this one.
                if (theyLead && !r.BelongsToFamily(me, theirFamily))
                    yield return Act("WFL_Act_AskToJoin".Translate(a, r.LineageDisplayName(r.GetLineage(theirFamily))), () => HouseInteractions.StartPetition(actor, theirFamily, PetitionKind.Admission, byPlayer: true));
                if (iLead && !r.BelongsToFamily(them, myFamily))
                    yield return Act("WFL_Act_Invite".Translate(b, r.LineageDisplayName(r.GetLineage(myFamily))), () => HouseInteractions.StartPetition(actor, myFamily, PetitionKind.Admission, true, clicked, true));
                if (iLead && theyLead && myFamily != theirFamily)
                    yield return Act("WFL_Act_Unite".Translate(r.LineageDisplayName(r.GetLineage(myFamily)), r.LineageDisplayName(r.GetLineage(theirFamily))), () => HouseInteractions.StartPetition(actor, theirFamily, PetitionKind.Union, byPlayer: true));

                // Standing within a house.
                if (theyLead && myFamily == theirFamily && r.RoleInHouse(me.Id, myFamily) == HouseRole.Member)
                    yield return Act("WFL_Act_Recognition".Translate(b), () => HouseInteractions.StartPetition(actor, myFamily, PetitionKind.Recognition, byPlayer: true));
                if (iLead && myFamily == theirFamily)
                {
                    var house = r.GetLineage(myFamily); bool steward = house.Stewards.Contains(them.Id);
                    yield return new FloatMenuOption((steward ? "WFL_Act_Revoke" : "WFL_Act_Appoint").Translate(b), () => { r.DelegateHouse(me.Id, them.Id, myFamily, !steward); c.MarkDirty(); });
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
