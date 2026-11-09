using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    // One place that decides what a person being played may know about a House, and one place that decides what they may do to it. Every tab asks these;
    // none keeps its own rules. Knowledge and authority are separate: knowing a House has no heir does not allow naming one.

    /// <summary>How widely a kind of information is known.</summary>
    public enum InfoScope : byte
    {
        /// <summary>Crest, name, public head, declared heir, public offices, public ceremonies, public membership.</summary>
        Public,
        /// <summary>People the viewer has met, events witnessed nearby, public interactions.</summary>
        Observable,
        /// <summary>Told directly to the viewer: invitations, promises and negotiations involving them.</summary>
        Shared,
        /// <summary>Internal ambition, supply targets, private relationship values, household policy, private succession talk.</summary>
        HousePrivate,
        /// <summary>Hidden heirs, plots, undisclosed grievances, crime. Only the administrative colony overview sees these.</summary>
        Secret
    }

    /// <summary>How well the viewer knows a House: from the inside, from dealings with it, by reputation only, or not at all.</summary>
    public enum Acquaintance : byte { Insider, Acquainted, Reputation, Unknown }

    public static class Knowledge
    {
        public const int HistoryTake = 40;

        /// <summary>Whether the viewer may know this kind of information about the House. The overview (viewer 0) is the administrative view and knows everything.</summary>
        public static bool Allows(FamilyRegistry r, int viewer, int houseId, InfoScope scope, int involving = 0)
        {
            if (viewer == 0) return true;
            switch (scope)
            {
                case InfoScope.Public: return true;
                case InfoScope.Observable: return FamilyWindowFacts.IsInsider(r, houseId, viewer) || (involving != 0 && Meets(r, viewer, involving));
                case InfoScope.Shared: return FamilyWindowFacts.IsInsider(r, houseId, viewer) || involving == viewer;
                case InfoScope.HousePrivate: return FamilyWindowFacts.IsInsider(r, houseId, viewer);
                default: return false;
            }
        }

        /// <summary>The viewer has dealt with this person: a bond they hold toward them, or a moment they shared.</summary>
        public static bool Meets(FamilyRegistry r, int viewer, int other)
        {
            if (viewer == 0 || viewer == other) return true;
            foreach (var b in r.BondsFrom(viewer)) if (b.To == other) return true;
            for (int i = r.EventLog.Count - 1, seen = 0; i >= 0 && seen < 400; i--, seen++)
            {
                var e = r.EventLog[i];
                if ((e.A == viewer && (e.B == other || e.Third == other)) || (e.B == viewer && (e.A == other || e.Third == other)) || (e.Third == viewer && (e.A == other || e.B == other))) return true;
            }
            return false;
        }

        /// <summary>The moments the viewer took part in with this House's people, newest first. This is the viewer's own history: nothing here is private to the House.</summary>
        public static List<EventRecord> HistoryWith(FamilyRegistry r, int viewer, Lineage house)
        {
            var list = new List<EventRecord>();
            if (r == null || house == null || viewer == 0) return list;
            var theirs = FamilyTimeline.Relevant(r, house);
            for (int i = r.EventLog.Count - 1; i >= 0 && list.Count < HistoryTake; i--)
            {
                var e = r.EventLog[i];
                if (e.Weight == EventWeight.Ambient) continue;
                bool me = e.A == viewer || e.B == viewer || e.Third == viewer;
                if (!me) continue;
                int other = e.A == viewer ? e.B : e.A;
                if (theirs.Contains(other) || (e.Third != 0 && e.Third != viewer && theirs.Contains(e.Third)) || theirs.Contains(e.B) && e.B != viewer) list.Add(e);
            }
            return list;
        }

        public static Acquaintance AcquaintanceWith(FamilyRegistry r, int viewer, Lineage house)
        {
            if (r == null || house == null) return Acquaintance.Unknown;
            if (FamilyWindowFacts.IsInsider(r, house.Id, viewer)) return Acquaintance.Insider;
            if (HistoryWith(r, viewer, house).Count > 0) return Acquaintance.Acquainted;
            foreach (var b in r.BondsFrom(viewer)) if (FamilyTimeline.Relevant(r, house).Contains(b.To)) return Acquaintance.Acquainted;
            return r.HeadOf(house.Id) != 0 ? Acquaintance.Reputation : Acquaintance.Unknown;
        }

        /// <summary>A feeling in words, for when the exact values are not the viewer's to see.</summary>
        public static string Describe(RelRow row)
        {
            if (row.Tension >= 50f) return "Strained";
            if (row.Tension >= 30f) return "Uneasy";
            if (row.Attachment >= 60f) return row.Trust >= 60f ? "Trusting" : "Warm";
            if (row.Familiarity < 25f) return "Unfamiliar";
            if (row.Trust >= 60f) return "Trusting";
            return row.Attachment >= 35f ? "Warm" : "Uncertain";
        }

        /// <summary>Kin ties that are public: partners and parents with their children, between people of the same House.</summary>
        public static List<KeyValuePair<int, int>> PublicTies(FamilyRegistry r, Lineage house)
        {
            var ties = new List<KeyValuePair<int, int>>();
            var members = r.Persons.Where(p => p.InColony && r.BelongsToFamily(p, house.Id)).ToList();
            for (int i = 0; i < members.Count; i++)
                for (int j = i + 1; j < members.Count; j++)
                {
                    var kind = r.KindOf(members[i], members[j]);
                    if (kind == BondKind.Partner || kind == BondKind.Parent || kind == BondKind.Child) ties.Add(new KeyValuePair<int, int>(members[i].Id, members[j].Id));
                }
            return ties;
        }
    }

    // ------------------------------------------------------------------ authority

    public enum HouseAct : byte
    {
        DeclareHeir, AppointSteward, AppointOfficer, EditCrest, CreateBranch, DesignateHeirloom, ChooseInheritor,
        ChangeSuccessionCustom, ChangeHouseholdPolicy, PlanPrivateCeremony, ChangeFamilyArea, HandlePrivateRequest
    }

    public struct AuthResult
    {
        public bool Allowed;
        public Narrative Why;
        /// <summary>The control should not be drawn at all: the viewer has no standing with this House.</summary>
        public bool Hidden;
        public static AuthResult Yes => new AuthResult { Allowed = true };
    }

    public static class HouseAuthority
    {
        private static bool HeadOnly(HouseAct act) =>
            act == HouseAct.DeclareHeir || act == HouseAct.AppointSteward || act == HouseAct.AppointOfficer || act == HouseAct.ChangeSuccessionCustom;

        /// <summary>
        /// Whether <paramref name="actor"/> may do <paramref name="act"/> for the House. The colony overview (actor 0) is the administrative view and may; a person may
        /// only act for a House they belong to, and the head's acts need the head. The same check runs again inside every action below.
        /// </summary>
        public static AuthResult Check(FamilyRegistry r, int actor, int houseId, HouseAct act)
        {
            var house = r?.GetLineage(houseId);
            if (house == null) return new AuthResult { Why = new Narrative("WFL_FW_Auth_NoHouse"), Hidden = true };
            if (actor == 0) return AuthResult.Yes;
            string name = r.LineageDisplayName(house);
            var person = r.GetPerson(actor);
            if (person == null || !person.InColony || !r.BelongsToFamily(person, houseId))
                return new AuthResult { Why = new Narrative("WFL_FW_Auth_Outsider", name), Hidden = true };
            if (HeadOnly(act))
                return r.HeadOf(houseId) == actor ? AuthResult.Yes : new AuthResult { Why = new Narrative("WFL_FW_Auth_" + act, name) };
            return r.CanManageHouse(actor, houseId) ? AuthResult.Yes : new AuthResult { Why = new Narrative("WFL_FW_Auth_Manage", name) };
        }

        // ---- the actions: each revalidates, then asks the registry. The UI may hide or grey a button, but this is what decides.

        public static bool DeclareHeir(FamilyRegistry r, int actor, int houseId, int heir, IEnumerable<int> told, int now)
        {
            if (!Check(r, actor, houseId, HouseAct.DeclareHeir).Allowed) return false;
            int head = r.HeadOf(houseId);
            return head != 0 && r.DesignateHeir(houseId, head, heir, told, now);
        }

        public static bool AppointSteward(FamilyRegistry r, int actor, int houseId, int member, bool appoint, int now)
        {
            if (!Check(r, actor, houseId, HouseAct.AppointSteward).Allowed) return false;
            int head = r.HeadOf(houseId);
            return head != 0 && r.DelegateHouse(head, member, houseId, appoint, now);
        }

        public static bool EditCrest(FamilyRegistry r, int actor, int houseId, int primary, int secondary, int mark, int pattern)
        {
            if (!Check(r, actor, houseId, HouseAct.EditCrest).Allowed) return false;
            var house = r.GetLineage(houseId);
            house.CrestPrimary = primary; house.CrestSecondary = secondary; house.CrestMark = mark; house.CrestPattern = pattern;
            return true;
        }

        public static Lineage CreateBranch(FamilyRegistry r, int actor, int parentId, int founder, string name, int now)
        {
            if (!Check(r, actor, parentId, HouseAct.CreateBranch).Allowed) return null;
            return r.CreateFamilyBranch(founder, parentId, name, now);
        }

        public static bool DesignateHeirloom(FamilyRegistry r, int actor, int thingId, int now)
        {
            var item = r.PersonalItemFor(thingId);
            if (item == null) return false;
            int house = item.OwnerId == 0 ? item.HouseId : r.FamilyOf(item.OwnerId);
            if (house == 0) return false;
            if (actor != 0 && !(actor == item.OwnerId && r.BelongsToFamily(r.GetPerson(actor), house)) && !Check(r, actor, house, HouseAct.DesignateHeirloom).Allowed) return false;
            return r.DesignateHeirloom(thingId, actor == 0 ? (item.OwnerId != 0 ? item.OwnerId : r.HeadOf(house)) : actor, now);
        }
    }
}
