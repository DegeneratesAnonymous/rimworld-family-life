using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Education's entry point. The game constructs every mod class in load order; this one announces Education to the framework (which is guaranteed to have loaded first, because
    /// this package requires it) and does nothing else. Registration is explicit and happens once; the framework freezes and validates after all packages have announced themselves.
    /// </summary>
    public class EducationMod : Mod
    {
        public EducationMod(ModContentPack content) : base(content)
        {
            FamilyFramework.Install(new EducationModule());
        }
    }
}
