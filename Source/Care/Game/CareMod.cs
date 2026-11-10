using Verse;

namespace FamilyLife
{
    /// <summary>
    /// Community Care's entry point. The game constructs every mod class in load order; this one announces the module to the framework (loaded first, because this package requires
    /// it) and does nothing else. Registration is explicit and happens once; the framework freezes and validates after every package has announced itself.
    /// </summary>
    public class CareMod : Mod
    {
        public CareMod(ModContentPack content) : base(content)
        {
            FamilyFramework.Install(new CareModule());
        }
    }
}
