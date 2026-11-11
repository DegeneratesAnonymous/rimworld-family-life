using System;
using System.Linq;
using FamilyLife.Framework;
using Xunit;

namespace FamilyLife.Tests
{
    public class CommunityPageTests
    {
        private static ModuleCatalog Modules()
        {
            var m = new ModuleCatalog();
            m.Register(new ModuleInfo { Id = ModuleCatalog.FrameworkId, Name = "fw", IsFramework = true, ApiVersion = 1 });
            m.Register(new ModuleInfo { Id = "wils.care", Name = "care", ApiVersion = 1, Requires = new[] { ModuleCatalog.FrameworkId } });
            return m;
        }
        private static CommunityPage<Func<int>> Page(string id, string module, int order = 100, Func<bool> avail = null) => new CommunityPage<Func<int>> { Id = id, ModuleId = module, LabelKey = id, Order = order, Available = avail, Render = () => 1 };

        [Fact] public void Pages_are_listed_in_order_and_owned_by_their_module()
        {
            var cat = new CommunityPageCatalog<Func<int>>(Modules());
            cat.Register(Page("wils.care.care", "wils.care", 120)); cat.Register(Page("wils.framework.overview", "wils.framework", 0)); cat.Register(Page("wils.framework.settings", "wils.framework", 90));
            Assert.Equal(new[] { "wils.framework.overview", "wils.framework.settings", "wils.care.care" }, cat.Visible().Select(p => p.Id).ToArray());
        }

        [Fact] public void A_disabled_module_has_no_page_and_cannot_be_navigated_to()
        {
            var mods = Modules(); var cat = new CommunityPageCatalog<Func<int>>(mods);
            cat.Register(Page("wils.framework.overview", "wils.framework", 0)); cat.Register(Page("wils.care.care", "wils.care"));
            mods.SetEnabled("wils.care", false);
            Assert.DoesNotContain(cat.Visible(), p => p.ModuleId == "wils.care");
            Assert.Null(cat.ById("wils.care.care"));
            Assert.Equal("wils.framework.overview", cat.Resolve("wils.care.care").Id);          // a stale selection falls back to the first listed page
        }

        [Fact] public void A_page_of_an_absent_module_is_never_registered_so_the_list_is_unchanged()
        {
            var cat = new CommunityPageCatalog<Func<int>>(Modules());
            cat.Register(Page("wils.framework.overview", "wils.framework", 0));
            Assert.Single(cat.Visible());
            Assert.Equal("wils.framework.overview", cat.Resolve("wils.treasury.treasury").Id);          // an unknown id still yields the first page, never a failure
        }

        [Fact] public void A_page_whose_availability_check_throws_is_left_out_rather_than_breaking_the_window()
        {
            var cat = new CommunityPageCatalog<Func<int>>(Modules());
            cat.Register(Page("wils.framework.overview", "wils.framework", 0)); cat.Register(Page("wils.care.care", "wils.care", 100, () => throw new InvalidOperationException("boom")));
            Assert.Single(cat.Visible());
        }

        [Fact] public void A_status_that_throws_counts_as_nothing_needing_attention()
        {
            var cat = new CommunityPageCatalog<Func<int>>(Modules());
            var p = Page("wils.framework.overview", "wils.framework", 0); p.Status = () => throw new Exception("x"); cat.Register(p);
            Assert.Equal(0, cat.StatusOf(p));
        }

        [Fact] public void Registration_is_validated()
        {
            var cat = new CommunityPageCatalog<Func<int>>(Modules());
            Assert.Throws<RegistrationException>(() => cat.Register(null));
            Assert.Throws<RegistrationException>(() => cat.Register(Page("Care", "wils.care")));                          // not a stable identifier
            Assert.Throws<RegistrationException>(() => cat.Register(Page("wils.framework.x", "wils.care")));          // outside its module's namespace
            Assert.Throws<RegistrationException>(() => cat.Register(new CommunityPage<Func<int>> { Id = "wils.care.x", ModuleId = "wils.care" }));          // nothing to render
            cat.Register(Page("wils.care.care", "wils.care"));
            Assert.Throws<RegistrationException>(() => cat.Register(Page("wils.care.care", "wils.care")));          // twice
        }

        [Fact] public void The_framework_never_names_an_optional_module_in_its_page_code()
        {
            foreach (var file in new[] { "UI/CommunityWindow.cs", "UI/CommunityCorePages.cs", "Framework/CommunityPages.cs" })
            {
                string text = RepoPaths.Read(file);
                Assert.DoesNotContain("wils.care", text); Assert.DoesNotContain("wils.education", text); Assert.DoesNotContain("wils.treasury", text);
            }
        }
    }
}

namespace FamilyLife.Tests
{
    public class ModulePageIntegrationTests
    {
        [Fact] public void Disabling_either_module_removes_only_its_page_and_never_breaks_the_rest_of_the_navigation()
        {
            var mods = new ModuleCatalog();
            foreach (var id in new[] { "wils.education", "wils.care", "wils.treasury" }) mods.Register(new ModuleInfo { Id = id, Name = id, ApiVersion = 1 });
            mods.Register(new ModuleInfo { Id = ModuleCatalog.FrameworkId, Name = "fw", IsFramework = true, ApiVersion = 1 });
            var cat = new CommunityPageCatalog<Func<int>>(mods);
            Func<int> render = () => 1;
            cat.Register(new CommunityPage<Func<int>> { Id = "wils.framework.overview", ModuleId = ModuleCatalog.FrameworkId, LabelKey = "o", Order = 0, Render = render });
            cat.Register(new CommunityPage<Func<int>> { Id = "wils.education.education", ModuleId = "wils.education", LabelKey = "e", Order = 110, Render = render });
            cat.Register(new CommunityPage<Func<int>> { Id = "wils.care.care", ModuleId = "wils.care", LabelKey = "c", Order = 120, Render = render });
            cat.Register(new CommunityPage<Func<int>> { Id = "wils.treasury.treasury", ModuleId = "wils.treasury", LabelKey = "t", Order = 130, Render = render });
            Assert.Equal(4, cat.Visible().Count);
            mods.SetEnabled("wils.education", false); Assert.Equal(new[] { "wils.framework.overview", "wils.care.care", "wils.treasury.treasury" }, cat.Visible().Select(p => p.Id).ToArray());
            mods.SetEnabled("wils.care", false); Assert.Equal(new[] { "wils.framework.overview", "wils.treasury.treasury" }, cat.Visible().Select(p => p.Id).ToArray());
            Assert.Equal("wils.framework.overview", cat.Resolve("wils.education.education").Id);          // a window that was on the Education page falls back cleanly
        }

        [Fact] public void Each_module_registers_its_own_page_and_the_framework_registers_none_of_theirs()
        {
            foreach (var (module, file, id) in new[] { ("Education", "Game/EducationPage.cs", "wils.education.education"), ("Care", "Game/CarePage.cs", "wils.care.care") })
            {
                string src = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.ModuleSource(module), file)); Assert.Contains("\"" + id + "\"", src); Assert.Contains("FamilyFramework.Pages.Register", src);
            }
            Assert.Contains("EducationPage.Register(Id)", System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.ModuleSource("Education"), "Game/EducationModule.cs")));
            Assert.Contains("CarePage.Register(Id)", System.IO.File.ReadAllText(System.IO.Path.Combine(RepoPaths.ModuleSource("Care"), "Game/CareModule.cs")));
        }
    }
}

namespace FamilyLife.Tests
{
    public class CommunityNavigationRegressionTests
    {
        [Xunit.Fact]
        public void Core_links_use_the_module_that_actually_registers_the_pages()
        {
            string registration = RepoPaths.Read("Game/FamilyLifeModule.cs");
            Xunit.Assert.Contains("CommunityCorePages.Register(Id)", registration);
            foreach (var file in new[] { "UI/CommunityCorePages.cs", "UI/CommunityCareerPage.cs", "UI/CommunityWindow.cs" })
            {
                string source = RepoPaths.Read(file);
                Xunit.Assert.DoesNotContain("ModuleCatalog.FrameworkId + \".", source);
                Xunit.Assert.Contains("FamilyLifeModule.Id + \".", source);
            }
        }
    }
}
