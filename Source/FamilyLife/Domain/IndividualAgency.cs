using System;
using System.Linq;

namespace FamilyLife.Domain
{
    /// <summary>A current, directed assessment; never a permanent moral label or a political allegiance.</summary>
    public sealed class FamilyBelonging
    {
        public int PersonId, FamilyId, Connections, TrustedConnections, StrainedConnections;
        public float Attachment, Trust, Tension;
        public bool Isolated => Connections == 0 || TrustedConnections == 0;
        public bool Divided => TrustedConnections > 0 && StrainedConnections > 0;
    }

    public sealed partial class FamilyRegistry
    {
        public FamilyBelonging AssessBelonging(int personId)
        {
            var result = new FamilyBelonging { PersonId = personId, FamilyId = FamilyOf(personId) };
            if (result.FamilyId == 0) return result;
            // Use known outgoing bonds, not family size: a large dynasty does not manufacture loyalty.
            var bonds = Bonds.Where(b => b.From == personId && b.To != personId &&
                GetPerson(b.To)?.InColony == true && FamilyOf(b.To) == result.FamilyId).ToList();
            result.Connections = bonds.Count;
            if (bonds.Count == 0) return result;
            result.Attachment = bonds.Average(b => b.Attachment);
            result.Trust = bonds.Average(b => b.Trust);
            result.Tension = bonds.Average(b => b.Tension);
            result.TrustedConnections = bonds.Count(b => b.Attachment >= 40 && b.Trust >= 35 && b.Tension < 50);
            result.StrainedConnections = bonds.Count(b => b.Tension >= 50 || b.Trust < 20);
            return result;
        }
    }
}
