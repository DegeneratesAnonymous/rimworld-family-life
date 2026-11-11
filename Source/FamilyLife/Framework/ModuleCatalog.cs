using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FamilyLife.Framework
{
    // The small set of registration contracts the optional modules plug into. Everything here is plain C# with no game types, so it can be tested outside RimWorld. Providers
    // are registered once at start-up and checked once; nothing is discovered by reflection while the game runs.

    /// <summary>Raised for a registration that cannot be accepted. The message names the module and the identifier, so the log says exactly what to fix.</summary>
    public sealed class RegistrationException : Exception
    {
        public RegistrationException(string message) : base(message) { }
    }

    public sealed class ModuleInfo
    {
        public string Id, Name;
        /// <summary>Modules this one cannot work without. An optional module may require only the framework; modules never require one another.</summary>
        public string[] Requires = new string[0];
        public bool IsFramework;
        /// <summary>The framework API the module was written against (<see cref="ModuleCatalog.CurrentApiVersion"/>). A module built for another version is refused, not half-loaded.</summary>
        public int ApiVersion;
        /// <summary>Capabilities (offered by the framework or another module) that must exist for this module to work. Optional modules should leave this empty.</summary>
        public string[] RequiresCapabilities = new string[0];
    }

    /// <summary>
    /// Which modules are present and which capabilities they offer. Another module (or the framework) asks about a capability, never about a module, so Education can offer
    /// tutoring when it is installed and nothing at all when it is not.
    /// </summary>
    public sealed class ModuleCatalog
    {
        public const string FrameworkId = "wils.framework";
        /// <summary>The version of the registration contracts in this assembly. Bumped when a contract changes in a way an older module cannot follow.</summary>
        public const int CurrentApiVersion = 1;
        // wils.<area>[.<name>] in lower case: the stable form used in saves, logs and settings
        public static readonly Regex IdPattern = new Regex(@"^wils(\.[a-z][a-z0-9]*)+$", RegexOptions.Compiled);

        private readonly Dictionary<string, ModuleInfo> modules = new Dictionary<string, ModuleInfo>();
        private readonly Dictionary<string, string> capabilities = new Dictionary<string, string>();          // capability id -> owning module
        private readonly HashSet<string> disabled = new HashSet<string>();          // switched off by its lifecycle state (preparing, prepared)
        private readonly HashSet<string> broken = new HashSet<string>();            // refused or failed during registration: off for the whole session

        public IEnumerable<ModuleInfo> Modules => modules.Values;

        public static void CheckId(string id, string what)
        {
            if (string.IsNullOrEmpty(id) || !IdPattern.IsMatch(id)) throw new RegistrationException(what + " identifier '" + id + "' is not a stable namespaced identifier (expected the form wils.area.name, lower case).");
        }

        public void Register(ModuleInfo module)
        {
            if (module == null) throw new RegistrationException("A module was registered as null.");
            CheckId(module.Id, "Module");
            if (modules.ContainsKey(module.Id)) throw new RegistrationException("Module '" + module.Id + "' is registered twice.");
            modules[module.Id] = module;
        }

        /// <summary>A module offers a capability. Each capability has exactly one owner.</summary>
        public void Provide(string moduleId, string capability)
        {
            CheckId(capability, "Capability");
            if (!modules.ContainsKey(moduleId)) throw new RegistrationException("Capability '" + capability + "' was offered by module '" + moduleId + "', which is not registered.");
            if (!capability.StartsWith(moduleId + ".", StringComparison.Ordinal)) throw new RegistrationException("Capability '" + capability + "' must be inside its module's namespace '" + moduleId + ".'.");
            if (capabilities.TryGetValue(capability, out var owner)) throw new RegistrationException("Capability '" + capability + "' is offered by both '" + owner + "' and '" + moduleId + "'.");
            capabilities[capability] = moduleId;
        }

        public bool HasModule(string id) => IsEnabled(id);

        /// <summary>True if some enabled module offers the capability. This is what other code asks before showing an interaction.</summary>
        public bool Has(string capability) => capabilities.TryGetValue(capability, out var owner) && IsEnabled(owner);

        public void SetEnabled(string moduleId, bool enabled)
        {
            if (!modules.ContainsKey(moduleId)) throw new RegistrationException("Module '" + moduleId + "' is not registered.");
            if (modules[moduleId].IsFramework && !enabled) throw new RegistrationException("The framework cannot be disabled.");
            if (enabled) disabled.Remove(moduleId); else disabled.Add(moduleId);
        }

        public bool IsEnabled(string moduleId) => modules.ContainsKey(moduleId) && !disabled.Contains(moduleId) && !broken.Contains(moduleId);

        public bool IsRegistered(string moduleId) => modules.ContainsKey(moduleId);

        /// <summary>A module that was refused or failed while registering. Everything it had registered is ignored for the rest of the session.</summary>
        public void MarkBroken(string moduleId) { if (moduleId != null) broken.Add(moduleId); }
        public bool IsBroken(string moduleId) => broken.Contains(moduleId);

        /// <summary>
        /// Checks the whole set once: exactly one framework, every required module present, optional modules requiring only the framework, and no cycles. Returns every problem
        /// found rather than the first, so one start-up log line lists them all.
        /// </summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            int frameworks = modules.Values.Count(m => m.IsFramework);
            if (frameworks != 1) problems.Add("There must be exactly one framework module, found " + frameworks + ".");
            foreach (var m in modules.Values)
                foreach (var need in m.Requires ?? new string[0])
                {
                    if (!modules.ContainsKey(need)) problems.Add("Module '" + m.Id + "' requires '" + need + "', which is not present.");
                    else if (!m.IsFramework && !modules[need].IsFramework) problems.Add("Module '" + m.Id + "' requires '" + need + "': optional modules may depend only on the framework, never on one another.");
                    else if (m.IsFramework) problems.Add("The framework requires '" + need + "': the framework must not depend on any module.");
                }
            foreach (var m in modules.Values)
            {
                if (m.ApiVersion != CurrentApiVersion) problems.Add("Module '" + m.Id + "' was built for framework API " + m.ApiVersion + " but this framework provides API " + CurrentApiVersion + "; it was not loaded.");
                foreach (var cap in m.RequiresCapabilities ?? new string[0])
                    if (!capabilities.ContainsKey(cap)) problems.Add("Module '" + m.Id + "' needs the capability '" + cap + "', which nothing offers.");
            }
            // a cycle can only appear through the rules broken above, but it is checked directly as well
            foreach (var m in modules.Values) if (Reaches(m.Id, m.Id, new HashSet<string>())) problems.Add("Module '" + m.Id + "' depends on itself through a cycle.");
            return problems;
        }

        /// <summary>The modules the checks above reject outright: a different API version, a missing required module or a missing required capability. They are switched off, not half-loaded.</summary>
        public List<string> UnloadableModules()
        {
            var bad = new List<string>();
            foreach (var m in modules.Values.Where(x => !x.IsFramework))
            {
                bool ok = m.ApiVersion == CurrentApiVersion
                    && (m.Requires ?? new string[0]).All(n => modules.ContainsKey(n) && modules[n].IsFramework)
                    && (m.RequiresCapabilities ?? new string[0]).All(c => capabilities.ContainsKey(c));
                if (!ok) bad.Add(m.Id);
            }
            return bad;
        }

        private bool Reaches(string from, string target, HashSet<string> seen)
        {
            if (!modules.TryGetValue(from, out var m)) return false;
            foreach (var need in m.Requires ?? new string[0])
            {
                if (need == target) return true;
                if (seen.Add(need) && Reaches(need, target, seen)) return true;
            }
            return false;
        }
    }
}
