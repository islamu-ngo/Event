using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Components.Shell;
using Explore.Blazor.Client.Components.Shell.Workspaces;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Shell;

namespace Explore.Blazor.Client.Tests.Components.Shell;

public sealed class WorkspaceShellScenarioMatrixTests
{
    [Test]
    public async Task ImplementedScenarioMatrix_RendersServerAuthorizedShellOutcomes()
    {
        foreach (var scenario in Scenarios)
        {
            using var context = CreateContext(scenario);
            var navigation = context.Services.GetRequiredService<NavigationManager>();
            navigation.NavigateTo(scenario.RequestedRoute);

            var rail = context.Render<AppWorkspaceRail>();
            rail.WaitForState(() => rail.FindAll(".app-workspace-rail__label").Count == scenario.ExpectedRailLabels.Length);

            var labels = rail.FindAll(".app-workspace-rail__label")
                .Select(element => element.TextContent.Trim())
                .ToArray();
            await Assert.That(string.Join('|', labels))
                .IsEqualTo(string.Join('|', scenario.ExpectedRailLabels), scenario.Name);

            var scopeRoutes = rail.FindAll(".app-workspace-rail__settings-menu-content a")
                .Select(element => element.GetAttribute("href"))
                .Where(href => href is not null && href != "/settings")
                .Select(href => href!)
                .ToArray();
            await Assert.That(string.Join('|', scopeRoutes))
                .IsEqualTo(string.Join('|', scenario.ExpectedSettingsRoutes), scenario.Name);

            var eventsNavigation = context.RenderMudComponent<EventsWorkspaceNavigation>();
            eventsNavigation.WaitForState(() => eventsNavigation.Markup.Contains(scenario.ExpectedEventsNavigationText, StringComparison.Ordinal));
            await Assert.That(eventsNavigation.Markup.Contains(scenario.ExpectedEventsNavigationText, StringComparison.Ordinal))
                .IsTrue();

            var preferences = await CreatePreferencesService(scenario).LoadAsync(CreateShellContext(scenario));
            await Assert.That(preferences.LastWorkspace).IsEqualTo(scenario.ExpectedDefaultWorkspace, scenario.Name);

            var shellState = context.Services.GetRequiredService<UiShellState>();
            await Assert.That(shellState.ActiveWorkspace.Value).IsEqualTo(scenario.ExpectedActiveWorkspace, scenario.Name);
            await Assert.That(rail.FindAll("nav[aria-label='Application workspaces']").Count)
                .IsEqualTo(1, $"{scenario.Name} ({scenario.Viewport}) must keep one semantic rail DOM");
        }
    }

    private static BlazorTestContext CreateContext(Scenario scenario)
    {
        var context = new BlazorTestContext();
        context.Services.AddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        context.Services.AddScoped<WorkspaceRouteClassifier>();
        context.Services.AddScoped<UiShellState>();
        context.Services.AddSingleton(new TenantNavLinksState());
        var tenantNavigationService = Substitute.For<ITenantNavigationService>();
        tenantNavigationService.GetNavigationLinksAsync().Returns([]);
        context.Services.AddSingleton(tenantNavigationService);

        var shellContextService = Substitute.For<IUiShellContextService>();
        shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(CreateShellContext(scenario));
        context.Services.AddSingleton(shellContextService);

        var publicExperienceService = Substitute.For<IPublicExperienceService>();
        publicExperienceService.GetCachedSettingsAsync().Returns(new PublicExperienceSettingsDto());
        publicExperienceService.GetCachedShellAsync().Returns(new PublicExperienceShellDto
        {
            Mode = scenario.Profile,
            EventCatalog = scenario.Profile == PublicExperienceMode.OrganizationCentric
                ? new PublicExperienceEventCatalogDto { Label = "Programs", Url = "/events?actor=primary" }
                : null
        });
        context.Services.AddSingleton(publicExperienceService);

        if (scenario.Authenticated)
        {
            context.SetAuthenticatedUser(Guid.CreateVersion7(), scenario.Capability);
        }
        else
        {
            context.SetAnonymousUser();
        }

        return context;
    }

    private static ShellPreferencesService CreatePreferencesService(Scenario scenario)
    {
        var settings = Substitute.For<IUserSettingsService>();
        settings.GetSettingsAsync(ShellPreferencesService.PreferencesCategory, Arg.Any<CancellationToken>())
            .Returns(string.IsNullOrWhiteSpace(scenario.StoredWorkspace)
                ? new SettingGroupResponseDto { Category = ShellPreferencesService.PreferencesCategory, Settings = [] }
                : new SettingGroupResponseDto
                {
                    Category = ShellPreferencesService.PreferencesCategory,
                    Settings =
                    [
                        new EffectiveSettingDto
                        {
                            Key = ShellPreferencesService.LastWorkspaceKey,
                            Value = $"\"{scenario.StoredWorkspace}\"",
                            SettingValueTypeCode = string.Empty,
                            SettingValueTypeName = string.Empty
                        }
                    ]
                });
        settings.ResetSettingAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);

        AuthenticationStateProvider auth =
            MockServiceFactory.CreateAuthenticationStateProvider(scenario.Authenticated);
        return new ShellPreferencesService(settings, auth, Substitute.For<ILogger<ShellPreferencesService>>());
    }

    private static UiShellContextDto CreateShellContext(Scenario scenario) => new()
    {
        DeploymentMode = scenario.DeploymentMode,
        Workspaces = new WorkspaceAvailabilityDto
        {
            Events = true,
            Studio = scenario.StudioAvailable,
            Ai = scenario.AiAvailable,
            Settings = true
        },
        SettingsScopes = scenario.SettingsScopes.ToList(),
        NavigationDefaults = new UiShellNavigationDefaultsDto
        {
            OrganizerDefaultWorkspace = scenario.OrganizerDefaultWorkspace
        }
    };

    private static readonly Guid OrganizationId = Guid.Parse("11111111-1111-7111-8111-111111111111");
    private static readonly Guid GroupId = Guid.Parse("22222222-2222-7222-8222-222222222222");
    private static string OrganizationRoute => $"/settings/organization/{OrganizationId}";
    private static string GroupRoute => $"/settings/group/{GroupId}";
    private static SettingsScopeDto OrganizationScope => new() { Scope = "Organization", ScopeId = OrganizationId, DisplayName = "Community" };
    private static SettingsScopeDto GroupScope => new() { Scope = "Group", ScopeId = GroupId, DisplayName = "Team" };
    private static SettingsScopeDto TenantScope => new() { Scope = "Tenant", DisplayName = "Tenant" };
    private static SettingsScopeDto InstanceScope => new() { Scope = "Instance", DisplayName = "Instance" };

    private static readonly Scenario[] Scenarios =
    [
        new("anonymous-discovery-mobile", "anonymous", PublicExperienceMode.DiscoveryCentric, false, false, false, "MultiTenant", [], "/", null, "Events", ["Events"], [], "Advanced Search", "events", "events", "mobile-320"),
        new("anonymous-organization-desktop", "anonymous", PublicExperienceMode.OrganizationCentric, false, false, false, "MultiTenant", [], "/", null, "Events", ["Events"], [], "Programs", "events", "events", "desktop-1280"),
        new("seeker-mobile", "seeker", PublicExperienceMode.DiscoveryCentric, true, false, true, "MultiTenant", [], "/events", "ai", "Events", ["Events", "AI", "Settings"], [], "Advanced Search", "ai", "events", "mobile-320"),
        new("seeker-desktop", "seeker", PublicExperienceMode.DiscoveryCentric, true, false, true, "MultiTenant", [], "/events", "ai", "Events", ["Events", "AI", "Settings"], [], "Advanced Search", "ai", "events", "desktop-1280"),
        new("organizer-mobile", "organizer", PublicExperienceMode.DiscoveryCentric, true, true, true, "MultiTenant", [OrganizationScope], "/studio", "studio", "Studio", ["Events", "Studio", "AI", "Settings"], [OrganizationRoute], "Advanced Search", "studio", "studio", "mobile-375"),
        new("organization-organizer-desktop", "organizer", PublicExperienceMode.OrganizationCentric, true, true, true, "MultiTenant", [OrganizationScope], "/studio", null, "Studio", ["Events", "Studio", "AI", "Settings"], [OrganizationRoute], "Programs", "studio", "studio", "desktop-1280"),
        new("tenant-admin-revoked-studio", "tenant-admin", PublicExperienceMode.DiscoveryCentric, true, false, true, "MultiTenant", [TenantScope], "/studio", "studio", "Events", ["Events", "AI", "Settings"], ["/settings/admin"], "Advanced Search", "events", "events", "tablet-768"),
        new("instance-admin-only", "instance-admin", PublicExperienceMode.DiscoveryCentric, true, false, true, "MultiTenant", [InstanceScope], "/settings/instance", "settings", "Events", ["Events", "AI", "Settings"], ["/settings/instance"], "Advanced Search", "settings", "settings", "desktop-1920"),
        new("multi-role-single-tenant", "multi-role", PublicExperienceMode.DiscoveryCentric, true, true, true, "SingleTenant", [OrganizationScope, GroupScope, TenantScope, InstanceScope], "/ai", "ai", "Studio", ["Events", "Studio", "AI", "Settings"], [OrganizationRoute, GroupRoute, "/settings/admin", "/settings/instance"], "Advanced Search", "ai", "ai", "desktop-1920")
    ];

    private sealed record Scenario(
        string Name,
        string Capability,
        PublicExperienceMode Profile,
        bool Authenticated,
        bool StudioAvailable,
        bool AiAvailable,
        string DeploymentMode,
        IReadOnlyList<SettingsScopeDto> SettingsScopes,
        string RequestedRoute,
        string? StoredWorkspace,
        string OrganizerDefaultWorkspace,
        string[] ExpectedRailLabels,
        string[] ExpectedSettingsRoutes,
        string ExpectedEventsNavigationText,
        string ExpectedDefaultWorkspace,
        string ExpectedActiveWorkspace,
        string Viewport);
}
