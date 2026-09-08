using Blazouter.Models;
using Blazouter.Services;
using Explore.Blazor.Client.Components.Webhooks;
using Explore.Blazor.Client.Contracts.Services.Accessibility;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Pages.Admin.Components;
using Explore.Blazor.Client.Pages.Events;
using Explore.Blazor.Client.Pages.User.Components;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Explore.Blazor.Client.Tests.Pages.User;

public sealed class SettingsLayoutTests : IDisposable
{
    private readonly BlazorTestContext _ctx = new();
    private readonly IAccessibilityAnnouncerService _announcer = Substitute.For<IAccessibilityAnnouncerService>();

    public SettingsLayoutTests()
    {
        _ctx.AddShellStateMocks();
        _announcer.AnnouncePoliteAsync(Arg.Any<string>()).Returns(Task.CompletedTask);
        _ctx.Services.RemoveAll<IAccessibilityAnnouncerService>();
        _ctx.Services.AddSingleton(_announcer);
        _ctx.ComponentFactories.AddStub<SettingsPersonalInfo>();
        _ctx.ComponentFactories.AddStub<SettingsSecurity>();
        _ctx.ComponentFactories.AddStub<SettingsPrivacy>();
        _ctx.ComponentFactories.AddStub<SettingsNotifications>();
        _ctx.ComponentFactories.AddStub<SettingsConnectedApps>();
        _ctx.ComponentFactories.AddStub<SettingsAppearance>();
        _ctx.ComponentFactories.AddStub<SettingsAiAssistant>();
        _ctx.ComponentFactories.AddStub<ApiKeysSection>();
        _ctx.ComponentFactories.AddStub<WebhookManagementPanel>();
    }

    public void Dispose() => _ctx.Dispose();

    [Test]
    public async Task PersonalSettingsNavigation_UsesCanonicalSectionLinksAndOneCurrentItem()
    {
        var cut = RenderLayout("security");

        var links = cut.FindAll("nav[aria-label='Personal settings sections'] a");
        await Assert.That(links.Select(link => link.GetAttribute("href")))
            .Contains("/settings/personal/appearance");
        await Assert.That(links.Select(link => link.GetAttribute("href")))
            .Contains("/settings/personal/personal-info");
        await Assert.That(links.Count(link => link.GetAttribute("aria-current") == "page")).IsEqualTo(1);
        await Assert.That(cut.Markup).DoesNotContain("?section=");
        await Assert.That(cut.Markup).DoesNotContain("settings-sidebar");
    }

    [Test]
    public async Task RootDefaultsToViewAllWithSearchAndCanonicalCurrentLink()
    {
        var cut = RenderLayout();

        var links = cut.FindAll("nav[aria-label='Personal settings sections'] a");
        await Assert.That(links[0].TextContent.Trim()).IsEqualTo("View all");
        await Assert.That(links[0].GetAttribute("href")).IsEqualTo("/settings/personal");
        await Assert.That(links[0].GetAttribute("aria-current")).IsEqualTo("page");
        await Assert.That(cut.FindAll("label").Any(label => label.TextContent.Contains("Search settings", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task ViewAllRendersNineCanonicalSectionsInOrderWithOnePageHeading()
    {
        var cut = RenderLayout();
        var sections = cut.FindAll("section[data-settings-section]");
        string[] expectedIds =
        [
            "settings-section-personal-info",
            "settings-section-security",
            "settings-section-privacy",
            "settings-section-notifications",
            "settings-section-connected-apps",
            "settings-section-appearance",
            "settings-section-ai-assistant",
            "settings-section-api-keys",
            "settings-section-webhooks"
        ];
        string[] expectedLabels =
        [
            "Personal info",
            "Security",
            "Privacy",
            "Notifications",
            "Connected apps",
            "Appearance",
            "AI Assistant",
            "API keys",
            "Webhooks"
        ];

        await Assert.That(string.Join('|', sections.Select(section => section.Id))).IsEqualTo(string.Join('|', expectedIds));
        await Assert.That(string.Join('|', sections.Select(section => section.QuerySelector("h2")?.TextContent.Trim()))).IsEqualTo(string.Join('|', expectedLabels));
        await Assert.That(sections.Select(section => section.Id).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(9);
        await Assert.That(cut.FindAll("h1").Count).IsEqualTo(1);
    }

    [Test]
    public async Task FocusedRouteRendersOnlyRequestedSectionWithoutSearch()
    {
        var cut = RenderLayout("security");

        var sections = cut.FindAll("section[data-settings-section]");
        await Assert.That(sections.Count).IsEqualTo(1);
        await Assert.That(sections[0].Id).IsEqualTo("settings-section-security");
        await Assert.That(cut.FindAll("input").Count).IsEqualTo(0);
    }

    [Test]
    public async Task SearchFiltersCaseInsensitivelyByDeclaredMetadata()
    {
        var cut = RenderLayout();

        cut.Find("input").Input("CrEdEnTiAlS");
        cut.WaitForState(() => cut.FindAll("section[data-settings-section]").Count == 1);

        var section = cut.Find("section[data-settings-section]");
        await Assert.That(section.Id).IsEqualTo("settings-section-api-keys");
    }

    [Test]
    public async Task SearchWithNoMetadataMatchShowsVisibleStatusAndAnnouncesPolitely()
    {
        var cut = RenderLayout();

        cut.Find("input").Input("no matching settings metadata");
        cut.WaitForState(() => cut.Markup.Contains("No settings sections match your search.", StringComparison.Ordinal));

        await Assert.That(cut.FindAll("section[data-settings-section]").Count).IsEqualTo(0);
        await Assert.That(cut.Markup).Contains("No settings sections match your search.");
        await _announcer.Received(1).AnnouncePoliteAsync("No settings sections match your search.");
    }

    [Test]
    public async Task InvalidOrLegacySectionSlugFallsBackToViewAllWithoutAlias()
    {
        var cut = RenderLayout("personal");

        var sections = cut.FindAll("section[data-settings-section]");
        var currentLink = cut.Find("nav[aria-label='Personal settings sections'] a[aria-current='page']");
        await Assert.That(sections.Count).IsEqualTo(9);
        await Assert.That(currentLink.GetAttribute("href")).IsEqualTo("/settings/personal");
        await Assert.That(cut.FindAll("a[href='/settings/personal/personal']").Count).IsEqualTo(0);
    }

    [Test]
    public async Task PersonalSettingsPage_RerendersWhenBlazouterSectionParameterChanges()
    {
        _ctx.Services.RemoveAll<RouterStateService>();
        _ctx.Services.AddSingleton<RouterStateService>();
        _ctx.AddMockService<IUiShellContextService>()
            .GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns((UiShellContextDto?)null);
        _ctx.AddMockService<IShellPreferencesService>();

        var routerState = _ctx.Services.GetRequiredService<RouterStateService>();
        var navigation = _ctx.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/settings/personal/security");
        routerState.SetCurrentRoute(CreateSettingsRoute("security"), "/settings/personal/security");

        var cut = RenderSettingsPage();
        cut.WaitForState(() => cut.Markup.Contains("settings-section-security", StringComparison.Ordinal));

        navigation.NavigateTo("/settings/personal/notifications");
        routerState.SetCurrentRoute(CreateSettingsRoute("notifications"), "/settings/personal/notifications");

        cut.WaitForState(() => cut.Markup.Contains("settings-section-notifications", StringComparison.Ordinal));
        await Assert.That(cut.Markup).DoesNotContain("settings-section-security");
    }

    private IRenderedComponent<SettingsLayout> RenderLayout(string? section = null) =>
        _ctx.RenderMudComponent<SettingsLayout>(parameters =>
            parameters.Add(component => component.Section, section));

    private IRenderedComponent<Explore.Blazor.Client.Pages.User.Settings> RenderSettingsPage() =>
        _ctx.RenderMudComponent<Explore.Blazor.Client.Pages.User.Settings>();

    private static RouteMatch CreateSettingsRoute(string section) => new()
    {
        Route = new RouteConfig { Path = "/settings/personal/:section" },
        MatchedPath = $"/settings/personal/{section}",
        Params = new Dictionary<string, string> { ["section"] = section }
    };

}
