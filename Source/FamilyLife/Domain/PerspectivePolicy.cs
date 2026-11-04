using System.Linq;
namespace FamilyLife.Domain
{
    public sealed partial class FamilyRegistry
    {
        public bool KnowsIncident(int observer,FamilyIncident incident) => observer==0 || incident?.KnownBy.Contains(observer)==true;
        public bool KnowsPlot(int observer,RevengePlot plot)
        {
            if(observer==0 || plot.OrganizerId==observer || plot.Recruits.Contains(observer) || plot.Asked.Contains(observer)) return true;
            return Incidents.Any(i=>i.Kind==IncidentKind.PlotExposed && i.ActorId==plot.OrganizerId && i.VictimId==plot.TargetId && i.Tick>=plot.CreatedTick && i.KnownBy.Contains(observer));
        }
    }
}
