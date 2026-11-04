using System;
using System.Linq;
namespace FamilyLife.Domain
{
    public sealed partial class FamilyIncident
    {
        public int RepairSteps, LastRepairTick = -1000000;
    }
    public sealed partial class FamilyRegistry
    {
        /// <summary>Only an informed victim-family participant and the actual actor can discuss this cause.</summary>
        public bool DiscussReparation(int incidentId, int injuredSpeaker, int actor, bool mutualConsent, int now)
        {
            var incident=Incidents.FirstOrDefault(i=>i.Id==incidentId);
            if(incident==null || incident.Resolved || incident.ActorId!=actor || incident.VictimFamily==0 || FamilyOf(injuredSpeaker)!=incident.VictimFamily ||
                GetPerson(injuredSpeaker)?.InColony!=true || GetPerson(actor)?.InColony!=true || GetPerson(injuredSpeaker).Juvenile || GetPerson(actor).Juvenile ||
                !incident.KnownBy.Contains(injuredSpeaker) || !incident.KnownBy.Contains(actor) || now-incident.LastRepairTick<ActivityTuning.TicksPerDay) return false;
            incident.LastRepairTick=now;
            if(!mutualConsent) {ChangeBond(injuredSpeaker,actor,new BondChange(0,-.5f,.5f,1),now);return false;}
            incident.RepairSteps=Math.Min(3,incident.RepairSteps+1);
            ChangeBond(injuredSpeaker,actor,new BondChange(.25f,1,-1,1),now);
            ChangeBond(actor,injuredSpeaker,new BondChange(.25f,.5f,-1,1),now);
            if(incident.RepairSteps>=3 && CanManageHouse(injuredSpeaker,incident.VictimFamily)) ForgiveIncident(incident.Id,incident.VictimFamily);
            return true;
        }
    }
}
