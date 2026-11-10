using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>The job defs Community Care ships (in its own package's Defs). Resolved by the game when Care's assembly loads, so the framework never needs to know them. Owner: Community Care.</summary>
    [DefOf]
    public static class CareDefOf
    {
        public static JobDef WFL_Checkup, WFL_ClinicDoctor, WFL_ClinicPatient;
        static CareDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(CareDefOf)); }
    }
}
