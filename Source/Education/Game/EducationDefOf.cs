using RimWorld;
using Verse;

namespace FamilyLife
{
    /// <summary>The job defs Education ships (in its own package's Defs). Resolved by the game when Education's assembly loads, so the framework never needs to know them. Owner: Education.</summary>
    [DefOf]
    public static class EducationDefOf
    {
        public static JobDef WFL_ClassTeacher, WFL_ClassStudent, WFL_TestStudent, WFL_OfficeLesson;
        static EducationDefOf() { DefOfHelper.EnsureInitializedInCtor(typeof(EducationDefOf)); }
    }
}
