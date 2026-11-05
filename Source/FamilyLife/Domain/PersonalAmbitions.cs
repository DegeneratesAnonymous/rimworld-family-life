using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Domain
{
    public enum PersonalGoal : byte { None, Connection, Recognition, Independence }
    public sealed partial class AgencyRecord
    {
        public int PersonId, FamilyId, Included, Excluded, LastEvidenceTick = -1, StartedTick, EndedTick;
        public int LastActionTick = -1000000, LastPersonalDay = -1, PersonalSessions;
        public PersonalGoal Goal;
        public float Progress;
        public string LastEvent = "";
        public bool Active => Goal != PersonalGoal.None && EndedTick == 0;
    }
    public sealed partial class FamilyRegistry
    {
        public List<AgencyRecord> Agency = new List<AgencyRecord>();
        private readonly Dictionary<int,AgencyRecord> agencyByPerson = new Dictionary<int,AgencyRecord>();
        public AgencyRecord AgencyOf(int id)
        {
            if(agencyByPerson.Count!=Agency.Count) {agencyByPerson.Clear();foreach(var a in Agency) agencyByPerson[a.PersonId]=a;}
            return agencyByPerson.TryGetValue(id,out var record)?record:null;
        }

        // Actual completed moments, not absence of interactions, establish inclusion/exclusion evidence.
        public void NoteAgencyMoment(EventResult moment, int now)
        {
            if (moment?.Context == null || moment.Def == null || moment.Def.Weight == EventWeight.Ambient) return;
            var c = moment.Context;
            NoteAgencyEvidence(c.Initiator.Id, c.Recipient.Id, moment.Def.Id, Feeling(moment, true), now, c.Initiator.Achievement != null && moment.Def.Opportunity.Any(o => o.Name == "Achievement"));
            NoteAgencyEvidence(c.Recipient.Id, c.Initiator.Id, moment.Def.Id, Feeling(moment, false), now, c.Recipient.Achievement != null && moment.Def.Opportunity.Any(o => o.Name == "Achievement"));
        }
        private static float Feeling(EventResult r, bool initiator)
        { return r.Def.Outcomes == null ? (r.Tier >= EventTier.Good ? 1 : r.Tier == EventTier.Poor ? -1 : 0) : (initiator ? r.Def.Outcomes[(int)r.Tier].Initiator : r.Def.Outcomes[(int)r.Tier].Recipient).Feeling; }
        private void NoteAgencyEvidence(int person, int other, string eventId, float feeling, int now, bool achievement)
        {
            var p = GetPerson(person);
            int family = FamilyOf(person);
            if (p?.InColony != true || p.Juvenile || family == 0 || FamilyOf(other) != family || GetPerson(other)?.InColony != true || person == other || Math.Abs(feeling) < 0.001f) return;
            var a = AgencyOf(person);
            if (a == null) { a = new AgencyRecord { PersonId = person, FamilyId = family }; Agency.Add(a); }
            if (a.FamilyId != family) ResetAgency(a, family);
            if (a.LastEvidenceTick >= 0 && now / ActivityTuning.TicksPerDay <= a.LastEvidenceTick / ActivityTuning.TicksPerDay) return;
            if (a.Active && now - a.LastEvidenceTick > 14 * ActivityTuning.TicksPerDay) a.EndedTick = now;
            if (a.LastEvidenceTick >= 0 && now - a.LastEvidenceTick > 7 * ActivityTuning.TicksPerDay) { a.Included = 0; a.Excluded = 0; }
            a.LastEvidenceTick = now; a.LastEvent = eventId;
            bool positive = feeling > 0;
            if (positive) a.Included = Math.Min(10, a.Included + 1); else a.Excluded = Math.Min(10, a.Excluded + 1);
            if (!a.Active)
            {
                if (a.EndedTick > 0 && now - a.EndedTick < 7 * ActivityTuning.TicksPerDay) return;
                if (a.Included >= 3 && positive) a.Goal = achievement ? PersonalGoal.Recognition : PersonalGoal.Connection;
                else if (a.Excluded >= 3 && !positive) a.Goal = PersonalGoal.Independence;
                else return;
                a.StartedTick = now; a.EndedTick = 0; a.Progress = 0; a.PersonalSessions = 0; a.LastPersonalDay = -1;
                return;
            }
            // Independence is an aspiration, not an automatic departure or political action.
            bool advances = a.Goal == PersonalGoal.Independence ? !positive : positive && (a.Goal != PersonalGoal.Recognition || achievement);
            a.Progress = Math.Max(0, Math.Min(1, a.Progress + (advances ? 0.125f : -0.0625f)));
            if (a.Progress >= 1 && a.Goal != PersonalGoal.Independence && (a.Goal == PersonalGoal.Recognition || AssessBelonging(person).TrustedConnections > 0)) a.EndedTick = now;
        }
        private static void ResetAgency(AgencyRecord a, int family)
        { a.FamilyId = family; a.Goal = PersonalGoal.None; a.Included = a.Excluded = a.StartedTick = a.EndedTick = 0; a.Progress = 0; a.LastActionTick = -1000000; a.LastPersonalDay = -1; a.PersonalSessions = 0; a.LastEvidenceTick = -1; a.LastEvent = ""; }
        public void MaintainAgency(int now)
        {
            MaintainHouseRelations(now);
            Attention.RemoveAll(a => a.Day < now / ActivityTuning.TicksPerDay - 6);
            foreach (var a in Agency)
            {
                if (a.FamilyId != FamilyOf(a.PersonId)) ResetAgency(a, FamilyOf(a.PersonId));
                if (a.Active && (GetPerson(a.PersonId)?.InColony != true || now - a.LastEvidenceTick > 14 * ActivityTuning.TicksPerDay)) a.EndedTick = now;
            }
        }
    }
}
