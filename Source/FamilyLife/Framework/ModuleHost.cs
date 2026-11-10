using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Framework
{
    /// <summary>
    /// The start-up sequence, free of any game type so it can be tested: modules announce themselves one at a time while registration is open (in the order the packages load), then the
    /// whole set is checked once and registration closes. A module that is a duplicate, arrives late, or fails part way is refused and everything it registered is ignored. Nothing is
    /// ever discovered after <see cref="Freeze"/>.
    /// </summary>
    public sealed class ModuleHost
    {
        private readonly List<string> problems = new List<string>(), order = new List<string>();
        public ModuleCatalog Catalog { get; }
        public bool IsFrozen { get; private set; }
        public IReadOnlyList<string> Problems => problems;
        /// <summary>Module ids in the order they were installed.</summary>
        public IReadOnlyList<string> InstallOrder => order;
        /// <summary>Called for every problem as it is found (the game logs it).</summary>
        public Action<string> OnProblem = _ => { };

        public ModuleHost(ModuleCatalog catalog) { Catalog = catalog; }

        public void Fail(string message) { problems.Add(message); OnProblem(message); }

        /// <summary>Records a module that was registered by the host itself (the framework).</summary>
        public void Note(string moduleId) { order.Add(moduleId); }

        /// <summary>
        /// Installs a module by running its registration. Refused (never thrown) when registration is closed, the id is already installed, or the registration itself throws; a module
        /// that fails part way is marked broken, so every contribution it did make is ignored for the whole session.
        /// </summary>
        public bool Install(string moduleId, Action register)
        {
            if (IsFrozen) { Fail("Module '" + moduleId + "' arrived after start-up finished and was not installed. A module must install from its mod constructor."); return false; }
            if (string.IsNullOrEmpty(moduleId)) { Fail("A module was installed without an id."); return false; }
            if (Catalog.IsRegistered(moduleId)) { Fail("Module id '" + moduleId + "' is already installed; the second one was ignored. Two packages must not provide the same module."); return false; }
            try { register(); order.Add(moduleId); return true; }
            catch (Exception ex)
            {
                Fail("Module '" + moduleId + "' failed while registering and was switched off: " + ex.Message);
                if (Catalog.IsRegistered(moduleId)) Catalog.MarkBroken(moduleId);
                return false;
            }
        }

        /// <summary>Closes registration and checks the whole set once. Modules built for another API, or needing something nothing offers, are switched off rather than half-loaded.</summary>
        public void Freeze(IEnumerable<string> otherProblems = null)
        {
            if (IsFrozen) return;
            IsFrozen = true;
            foreach (var p in Catalog.Validate()) Fail(p);
            foreach (var id in Catalog.UnloadableModules()) Catalog.MarkBroken(id);
            if (otherProblems != null) foreach (var p in otherProblems) Fail(p);
        }
    }
}
