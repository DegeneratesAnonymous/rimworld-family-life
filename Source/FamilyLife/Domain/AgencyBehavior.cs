using System;
using System.Collections.Generic;
using System.Linq;
namespace FamilyLife.Domain
{
    public sealed partial class AttentionRecord
    {
        public int Giver, Recipient, Day, Ticks;
    }
    public sealed class AttentionComparison
    {
        public int Giver, Child, OtherChild, ChildTicks, OtherTicks, OtherDays;
        public bool Unequal => OtherDays >= 3 && OtherTicks >= 3 * BondTuning.TicksPerHour && ChildTicks * 3 < OtherTicks;
    }
    public sealed partial class FamilyRegistry
    {
        public List<AttentionRecord> Attention = new List<AttentionRecord>();
        public float AgencyCompanionFactor(int person, int other)
        {
            var a = AgencyOf(person); var b = GetBond(person, other);
            if (a?.Active != true || GetPerson(person)?.InColony != true || GetPerson(other)?.InColony != true) return 1f;
            if (a.Goal == PersonalGoal.Connection && FamilyOf(person) == FamilyOf(other) && (b?.Tension ?? 0) < 60) return 1.2f;
            if (a.Goal == PersonalGoal.Independence && FamilyOf(person) == FamilyOf(other)) return .8f;
            return 1f;
        }
        public float AgencyEventFactor(PairContext c, EventDef d)
        {
            var a = AgencyOf(c.Initiator.Id);
            if (a?.Active != true) return 1f;
            if (a.Goal == PersonalGoal.Recognition && c.Initiator.Achievement != null && d.Opportunity.Any(o => o.Name == "Achievement")) return 1.2f;
            return AgencyCompanionFactor(c.Initiator.Id,c.Recipient.Id);
        }
        public bool CanTakePersonalTime(int id, int now)
        {
            var a = AgencyOf(id); var p = GetPerson(id);
            return a?.Active == true && a.Goal == PersonalGoal.Independence && a.Progress >= .25f && p?.InColony == true && !p.Juvenile &&
                now - a.LastActionTick >= ActivityTuning.TicksPerDay / 2 && a.LastPersonalDay < now / ActivityTuning.TicksPerDay;
        }
        public bool CompletePersonalTime(int id, int ticks, int now)
        {
            var a = AgencyOf(id);
            if (ticks < 1800 || a?.Active != true || a.Goal != PersonalGoal.Independence || GetPerson(id)?.InColony != true || GetPerson(id).Juvenile || a.LastPersonalDay >= now / ActivityTuning.TicksPerDay) return false;
            a.LastPersonalDay = now / ActivityTuning.TicksPerDay; a.PersonalSessions++;
            if (a.PersonalSessions >= 3) { a.Progress = 1; a.EndedTick = now; }
            return true;
        }
        private void RecordAttention(int giver, int child, int ticks, int now)
        {
            var p = GetPerson(giver); var c = GetPerson(child);
            if (p?.InColony != true || p.Juvenile || c?.InColony != true || !c.Juvenile || !c.ParentIds.Contains(giver)) return;
            int day = now / ActivityTuning.TicksPerDay;
            Attention.RemoveAll(r => r.Day < day-6);
            var a = Attention.FirstOrDefault(r => r.Giver == giver && r.Recipient == child && r.Day == day);
            if (a == null)
            {
                if (Attention.Count >= 4096) return;
                a = new AttentionRecord{Giver=giver,Recipient=child,Day=day}; Attention.Add(a);
            }
            a.Ticks = Math.Min(ActivityTuning.DefaultDailyTargetTicks, a.Ticks + Math.Min(ticks,ActivityTuning.DefaultDailyTargetTicks));
        }
        public AttentionComparison CompareAttention(int giver, int child, int now)
        {
            var result = new AttentionComparison{Giver=giver,Child=child}; var c = GetPerson(child);
            if (c?.InColony != true || !c.Juvenile || !c.ParentIds.Contains(giver)) return result;
            int day=now/ActivityTuning.TicksPerDay;
            var recent=Attention.Where(a=>a.Giver==giver && a.Day>=day-6 && a.Day<=day).ToList();
            result.ChildTicks=recent.Where(a=>a.Recipient==child).Sum(a=>a.Ticks);
            var other=recent.Where(a=>a.Recipient!=child && GetPerson(a.Recipient)?.InColony==true && GetPerson(a.Recipient).Juvenile &&
                GetPerson(a.Recipient).ParentIds.Contains(giver) && c.HouseholdId!=0 && GetPerson(a.Recipient).HouseholdId==c.HouseholdId)
                .GroupBy(a=>a.Recipient).OrderByDescending(g=>g.Sum(a=>a.Ticks)).FirstOrDefault();
            if(other!=null){result.OtherChild=other.Key;result.OtherTicks=other.Sum(a=>a.Ticks);result.OtherDays=other.Select(a=>a.Day).Distinct().Count();}
            return result;
        }
    }
}
