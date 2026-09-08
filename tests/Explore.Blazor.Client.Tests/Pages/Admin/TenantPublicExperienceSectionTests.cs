// ABOUTME: Rendered coverage for separate tenant public-display and visitor-access controls.
// ABOUTME: Verifies canonical visitor modes and platform lock presentation without conflating display mode.

using Explore.Blazor.Client.Pages.Admin.Tenant.Components;
using Explore.Blazor.Client.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Pages.Admin;

public sealed class TenantPublicExperienceSectionTests : IDisposable
{
    private readonly BlazorTestContext _context = new();

    public TenantPublicExperienceSectionTests()
    {
        var organizations = Substitute.For<IOrganizationService>();
        organizations.GetOrganizationsPagedAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(PaginatedResult<OrganizationListDto>.Empty());
        _context.Services.AddSingleton(organizations);
        _context.Services.AddSingleton(Substitute.For<ILogger<TenantPublicExperienceSection>>());
    }

    public void Dispose() => _context.Dispose();

    [Test]
    public async Task RendersVisitorAccessSeparatelyFromPublicDisplayMode()
    {
        var model = new TenantPublicExperienceAdminModel
        {
            IsAvailable = true,
            Mode = "OrganizationCentric",
            VisitorAccessMode = VisitorAccessMode.AnonymousOnly,
            CanEditMode = true,
            CanEditVisitorAccessMode = true
        };

        var cut = _context.RenderMudComponent<TenantPublicExperienceSection>(parameters => parameters
            .Add(component => component.Model, model));
        MudSelect<string> displayMode = cut.FindComponents<MudSelect<string>>().Single().Instance;
        MudSelect<VisitorAccessMode?> visitorMode = cut.FindComponents<MudSelect<VisitorAccessMode?>>().Single().Instance;

        await Assert.That(displayMode.Value).IsEqualTo("OrganizationCentric");
        await Assert.That(visitorMode.Value).IsEqualTo(VisitorAccessMode.AnonymousOnly);
        await Assert.That(cut.FindAll("[data-testid='visitor-access-mode-select']")).Count().IsGreaterThanOrEqualTo(1);
    }

    [Test]
    public async Task LockedVisitorAccessDisablesOnlyItsControl()
    {
        var model = new TenantPublicExperienceAdminModel
        {
            IsAvailable = true,
            CanEditMode = true,
            CanEditVisitorAccessMode = false
        };

        var cut = _context.RenderMudComponent<TenantPublicExperienceSection>(parameters => parameters
            .Add(component => component.Model, model));
        MudSelect<string> displayMode = cut.FindComponents<MudSelect<string>>().Single().Instance;
        MudSelect<VisitorAccessMode?> visitorMode = cut.FindComponents<MudSelect<VisitorAccessMode?>>().Single().Instance;

        await Assert.That(displayMode.Disabled).IsFalse();
        await Assert.That(visitorMode.Disabled).IsTrue();
    }

    [Test]
    public async Task UnavailableSettingsPreserveSelectionAndDisableAllEditing()
    {
        var model = new TenantPublicExperienceAdminModel
        {
            IsAvailable = false,
            Mode = "OrganizationCentric",
            VisitorAccessMode = VisitorAccessMode.DirectoryListingOnly,
            EventCatalogLabel = "Programs",
            CanEditMode = true,
            CanEditVisitorAccessMode = true
        };

        var cut = _context.RenderMudComponent<TenantPublicExperienceSection>(parameters => parameters
            .Add(component => component.Model, model));
        MudSelect<string> displayMode = cut.FindComponents<MudSelect<string>>().Single().Instance;
        MudSelect<VisitorAccessMode?> visitorMode = cut.FindComponents<MudSelect<VisitorAccessMode?>>().Single().Instance;

        await Assert.That(displayMode.Value).IsEqualTo("OrganizationCentric");
        await Assert.That(visitorMode.Value).IsEqualTo(VisitorAccessMode.DirectoryListingOnly);
        await Assert.That(displayMode.Disabled).IsTrue();
        await Assert.That(visitorMode.Disabled).IsTrue();
        await Assert.That(cut.FindAll("[data-testid='public-experience-settings-unavailable']"))
            .Count().IsGreaterThanOrEqualTo(1);
    }
}
