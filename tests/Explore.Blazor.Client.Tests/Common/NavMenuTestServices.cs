using Explore.Blazor.Client.Clients;
using Explore.Blazor.Client.Contracts.Services;
using Explore.Blazor.Client.Contracts.Services.Organizations;
using Explore.Blazor.Client.Contracts.Services.Shell;
using Explore.Blazor.Client.Services;
using Explore.Blazor.Client.Services.Docking;
using Explore.Blazor.Client.Services.Shell;
using Explore.Blazor.Client.Tests.Common.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;

namespace Explore.Blazor.Client.Tests.Common;

/// <summary>
/// Registers all services required by the NavMenu component.
/// Eliminates duplication across test files that render NavMenu or full-page layouts.
/// </summary>
public static class NavMenuTestServices
{
    /// <summary>
    /// Registers all NavMenu dependencies with sensible defaults.
    /// </summary>
    /// <param name="ctx">The test context to register services on.</param>
    /// <param name="publicExperienceSettings">
    /// Optional settings model. When null, GetSettingsAsync returns null (anonymous/default experience).
    /// Use <see cref="PublicExperienceSettingsBuilder"/> to construct complex configurations.
    /// </param>
    /// <param name="deploymentMode">
    /// Shell context deployment mode. Defaults to "MultiTenant".
    /// Common values: "MultiTenant", "SingleTenant".
    /// </param>
    /// <param name="isCurrentUserInstanceAdmin">
    /// When true, the shell context includes an Instance settings scope.
    /// </param>
    /// <param name="isCurrentUserTenantAdmin">
    /// When true, the shell context includes a Tenant settings scope.
    /// </param>
    public static void Register(
        BlazorTestContext ctx,
        PublicExperienceSettingsDto? publicExperienceSettings = null,
        string deploymentMode = "MultiTenant",
        bool isCurrentUserInstanceAdmin = false,
        bool isCurrentUserTenantAdmin = false,
        EventCreationEligibility? eventCreationEligibility = null)
    {
        var userService = Substitute.For<IUserService>();
        userService.GetCurrentUserAsync().Returns((UserDto?)null);
        ctx.Services.AddSingleton(userService);

        var userSettingsService = Substitute.For<IUserSettingsService>();
        userSettingsService.GetSettingsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<SettingGroupResponseDto?>(null));
        ctx.Services.AddSingleton(userSettingsService);

        var publicExperienceService = Substitute.For<IPublicExperienceService>();
        publicExperienceService.GetSettingsAsync()
            .Returns(Task.FromResult(publicExperienceSettings));
        publicExperienceService.ResolveHomeRoute(Arg.Any<PublicExperienceSettingsDto?>())
            .Returns(callInfo =>
            {
                var settings = callInfo.Arg<PublicExperienceSettingsDto?>();
                return settings?.PreferredHomePage?.Equals("LandingPage", StringComparison.OrdinalIgnoreCase) == true
                    ? "/home"
                    : "/events";
            });
        ctx.Services.AddSingleton(publicExperienceService);

        var shellContextService = Substitute.For<IUiShellContextService>();
        shellContextService.GetCachedContextAsync(Arg.Any<CancellationToken>())
            .Returns(BuildShellContext(deploymentMode, isCurrentUserInstanceAdmin, isCurrentUserTenantAdmin));
        ctx.Services.AddSingleton(shellContextService);

        var tenantOnboardingService = Substitute.For<ITenantOnboardingService>();
        ctx.Services.AddSingleton(tenantOnboardingService);

        var tenantNavigationService = Substitute.For<ITenantNavigationService>();
        tenantNavigationService.GetNavigationLinksAsync().Returns(new List<TenantNavigationLinkDto>());
        ctx.Services.AddSingleton(tenantNavigationService);

        var eligibilityService = Substitute.For<IEventCreationEligibilityService>();
        eligibilityService.GetEligibilityAsync().Returns(eventCreationEligibility ?? EventCreationEligibility.NotEligible);
        ctx.Services.AddSingleton(eligibilityService);

        ctx.Services.AddScoped<DockLayoutState>();
        ctx.Services.AddScoped<IDockPanelRegistry>(provider => provider.GetRequiredService<DockLayoutState>());
        ctx.Services.TryAddScoped<IWorkspaceRegistry, WorkspaceRegistry>();
        ctx.Services.TryAddScoped<WorkspaceRouteClassifier>();
        ctx.Services.TryAddScoped<UiShellState>();
        ctx.Services.TryAddScoped<IUiShellContextService, UiShellContextService>();
        ctx.Services.AddSingleton(MockServiceFactory.CreateNotificationService());
        ctx.Services.AddSingleton(MockServiceFactory.CreateTranslationService());
        ctx.Services.AddSingleton(Substitute.For<IHttpClientFactory>());
    }

    private static UiShellContextDto BuildShellContext(
        string deploymentMode,
        bool isCurrentUserInstanceAdmin,
        bool isCurrentUserTenantAdmin)
    {
        var scopes = new List<SettingsScopeDto>();
        if (isCurrentUserInstanceAdmin)
        {
            scopes.Add(new SettingsScopeDto { Scope = "Instance", ScopeId = Guid.NewGuid(), DisplayName = "Instance" });
        }

        if (isCurrentUserTenantAdmin)
        {
            scopes.Add(new SettingsScopeDto { Scope = "Tenant", ScopeId = AuthenticationTestConstants.DefaultTenantId, DisplayName = "Tenant" });
        }

        return new UiShellContextDto
        {
            DeploymentMode = deploymentMode,
            SettingsScopes = scopes,
            Workspaces = new WorkspaceAvailabilityDto
            {
                Events = true,
                Settings = true,
                Studio = false
            }
        };
    }
}
