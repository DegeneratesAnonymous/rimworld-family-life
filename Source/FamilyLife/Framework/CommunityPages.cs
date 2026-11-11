using System;
using System.Collections.Generic;
using System.Linq;

namespace FamilyLife.Framework
{
    /// <summary>One page of the Community window. The framework owns navigation, selection and layout; the registering module owns the content (<see cref="Render"/>).</summary>
    public sealed class CommunityPage<TRender> where TRender : class
    {
        public string Id, ModuleId, LabelKey, Icon;
        /// <summary>Lower sorts first. Core pages use 0-99, modules 100 and up.</summary>
        public int Order;
        /// <summary>Extra availability beyond the module being enabled (for example a page that needs a post to be filled). Null means always.</summary>
        public Func<bool> Available;
        /// <summary>A small count shown beside the page name (things needing attention); 0 or null shows nothing.</summary>
        public Func<int> Status;
        /// <summary>True for a page that draws its own scroll areas (it is given the whole content area and its return value is ignored).</summary>
        public bool OwnsScrolling;
        public TRender Render;
    }

    /// <summary>
    /// The pages of the Community window. Registered once at start-up like every other contribution; a page of a disabled, absent or broken module is never listed, so navigation
    /// cannot lead to an empty or failing page. Generic over the render delegate so the registry carries no game types and can be tested.
    /// </summary>
    public sealed class CommunityPageCatalog<TRender> where TRender : class
    {
        private readonly ModuleCatalog modules;
        private readonly Dictionary<string, CommunityPage<TRender>> byId = new Dictionary<string, CommunityPage<TRender>>();
        public CommunityPageCatalog(ModuleCatalog modules) { this.modules = modules; }

        public void Register(CommunityPage<TRender> page)
        {
            if (page == null) throw new RegistrationException("A community page was registered as null.");
            ModuleCatalog.CheckId(page.Id, "Community page");
            if (string.IsNullOrEmpty(page.ModuleId)) throw new RegistrationException("Community page '" + page.Id + "' has no owning module.");
            if (!page.Id.StartsWith(page.ModuleId + ".", StringComparison.Ordinal)) throw new RegistrationException("Community page '" + page.Id + "' must be inside its module's namespace '" + page.ModuleId + ".'.");
            if (page.Render == null) throw new RegistrationException("Community page '" + page.Id + "' has nothing to render.");
            if (byId.ContainsKey(page.Id)) throw new RegistrationException("Community page '" + page.Id + "' is registered twice.");
            byId[page.Id] = page;
        }

        public CommunityPage<TRender> ById(string id) => id != null && byId.TryGetValue(id, out var p) && Listed(p) ? p : null;

        /// <summary>The pages to show, in order. A page whose own availability check throws is left out rather than breaking the window.</summary>
        public List<CommunityPage<TRender>> Visible() => byId.Values.Where(Listed).OrderBy(p => p.Order).ThenBy(p => p.Id, StringComparer.Ordinal).ToList();

        /// <summary>The page to show: the wanted one if it is still listed, otherwise the first listed page (null only if nothing is registered).</summary>
        public CommunityPage<TRender> Resolve(string wantedId) => ById(wantedId) ?? Visible().FirstOrDefault();

        private bool Listed(CommunityPage<TRender> p)
        {
            try { return modules.IsEnabled(p.ModuleId) && (p.Available == null || p.Available()); }
            catch (Exception) { return false; }
        }

        public int StatusOf(CommunityPage<TRender> p) { try { return p.Status?.Invoke() ?? 0; } catch (Exception) { return 0; } }
    }
}
