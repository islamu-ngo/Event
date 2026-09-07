using Explore.Blazor.Client.Components.Shell.Workspaces;
using MudBlazor;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class EventsWorkspaceNavigationTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IPublicExperienceService _publicExperienceService;
    private readonly TenantNavLinksState _tenantNavLinksState;

    public EventsWorkspaceNavigationTests()
    {
        _publicExperienceService = Substitute.For<IPublicExperienceService>();
        _publicExperienceService.GetCachedSettingsAsync().Returns(new PublicExperienceSettingsDto());
        _ctx.Services.AddSingleton(_publicExperienceService);

        _tenantNavLinksState = new TenantNavLinksState();
        _ctx.Services.AddSingleton(_tenantNavLinksState);
    }

    public void Dispose()
    {
        _ctx.Dispose();
    }

    [Test]
    public async Task Render_ShowsCoreNavigationLinks()
    {
        var cut = _ctx.RenderMudComponent<EventsWorkspaceNavigation>();

        await Assert.That(cut.Markup).Contains("Advanced Search");
        await Assert.That(cut.Markup).Contains("Recently Added");
        await Assert.That(cut.Markup).Contains("Random");
        await Assert.That(cut.Markup).Contains("About Us");
        await Assert.That(cut.Markup).Contains("Contact");
        await Assert.That(cut.Find("[data-testid='events-workspace-navigation']").GetAttribute("aria-label"))
            .IsEqualTo("Events workspace navigation");
    }

    [Test]
    public async Task Render_WithBrandName_ShowsBrandLabel()
    {
        _publicExperienceService.GetCachedSettingsAsync().Returns(new PublicExperienceSettingsDto
        {
            BrandDisplayName = "Community Hub"
        });

        var cut = _ctx.RenderMudComponent<EventsWorkspaceNavigation>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Community Hub"))
                throw new InvalidOperationException("Expected 'Community Hub' label");
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task Render_WhenCommunityGuidelinesDisabled_HidesCommunityGuidelinesLink()
    {
        _publicExperienceService.GetCachedSettingsAsync().Returns(new PublicExperienceSettingsDto
        {
            AllowUserSubmittedEvents = false,
            AllowOrganizationSubmittedEvents = false,
            AllowGroupSubmittedEvents = false
        });

        var cut = _ctx.RenderMudComponent<EventsWorkspaceNavigation>();

        cut.WaitForAssertion(() =>
        {
            if (cut.Markup.Contains("Community Guidelines"))
                throw new InvalidOperationException("Expected 'Community Guidelines' to be hidden");
        });

        await Task.CompletedTask;
    }

    [Test]
    public async Task Render_WithTenantLinks_OrdersLinksAndPreservesExternalAttributes()
    {
        var tenantLinks = new List<TenantNavigationLinkDto>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Label = "Second Link",
                Url = "/second",
                Order = 20
            },
            new()
            {
                Id = Guid.NewGuid(),
                Label = "First Link",
                Url = "https://example.test/first",
                Order = 10,
                OpenInNewTab = true
            }
        };

        var cut = _ctx.RenderMudComponent<EventsWorkspaceNavigation>(parameters =>
            parameters.Add(component => component.TenantLinks, tenantLinks));

        var firstIndex = cut.Markup.IndexOf("First Link", StringComparison.Ordinal);
        var secondIndex = cut.Markup.IndexOf("Second Link", StringComparison.Ordinal);

        await Assert.That(cut.Markup).Contains("Quick Links");
        await Assert.That(firstIndex).IsLessThan(secondIndex);
        await Assert.That(cut.Markup).Contains("target=\"_blank\"");
        await Assert.That(cut.Markup).Contains("rel=\"noopener noreferrer\"");
    }

    [Test]
    public async Task Render_WithOrganizationCentricShell_UsesCatalogLabelAndHidesDiscoveryShortcuts()
    {
        _publicExperienceService.GetCachedShellAsync().Returns(new PublicExperienceShellDto
        {
            Mode = PublicExperienceMode.OrganizationCentric,
            EventCatalog = new PublicExperienceEventCatalogDto
            {
                Label = "Programs",
                Url = "/events?ActorId=11111111-1111-1111-1111-111111111111"
            },
            PrimaryOrganization = new PublicExperiencePrimaryOrganizationDto
            {
                State = PublicExperiencePrimaryOrganizationState.Available
            }
        });

        var cut = _ctx.RenderMudComponent<EventsWorkspaceNavigation>();

        cut.WaitForAssertion(() =>
        {
            if (!cut.Markup.Contains("Programs", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected catalog label");
            if (cut.Markup.Contains("Advanced Search", StringComparison.Ordinal)
                || cut.Markup.Contains("Recently Added", StringComparison.Ordinal)
                || cut.Markup.Contains("Random", StringComparison.Ordinal))
                throw new InvalidOperationException("Expected discovery shortcuts to be hidden");
        });

        await Task.CompletedTask;
    }
}
