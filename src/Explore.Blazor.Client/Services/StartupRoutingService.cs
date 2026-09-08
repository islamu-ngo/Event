using Explore.Blazor.Client.Contracts.Providers;

namespace Explore.Blazor.Client.Services;

public sealed class StartupRoutingService : IStartupRoutingService
{
    private readonly IInstanceOnboardingService _instanceOnboardingService;
    private readonly IPublicExperienceService _publicExperienceService;

    public StartupRoutingService(
        IInstanceOnboardingService instanceOnboardingService,
        IPublicExperienceService publicExperienceService)
    {
        _instanceOnboardingService = instanceOnboardingService;
        _publicExperienceService = publicExperienceService;
    }

    public async Task<StartupRouteDecision> GetRootDecisionAsync()
    {
        var instanceStatus = await _instanceOnboardingService.GetStartupStatusAsync();
        switch (instanceStatus.Disposition)
        {
            case InstanceOnboardingStartupDisposition.InteractivePending:
                return StartupRouteDecision.Setup;
            case InstanceOnboardingStartupDisposition.ConfiguredAdministratorPending:
                return instanceStatus.Provider switch
                {
                    "Keycloak" => StartupRouteDecision.KeycloakChallenge,
                    "Atproto" => StartupRouteDecision.AtprotoChallenge,
                    _ => StartupRouteDecision.Unavailable
                };
            case InstanceOnboardingStartupDisposition.Unavailable:
                return StartupRouteDecision.Unavailable;
            case InstanceOnboardingStartupDisposition.Completed:
                break;
            default:
                return StartupRouteDecision.Unavailable;
        }

        var isSingleTenant = string.Equals(
            instanceStatus.SelectedDeploymentMode,
            "SingleTenant",
            StringComparison.Ordinal);
        var isMultiTenant = string.Equals(
            instanceStatus.SelectedDeploymentMode,
            "MultiTenant",
            StringComparison.Ordinal);
        if (!isSingleTenant && !isMultiTenant)
        {
            return StartupRouteDecision.Unavailable;
        }

        if (instanceStatus.IsAuthenticated &&
            instanceStatus.IsCurrentUserInstanceAdmin &&
            isMultiTenant)
        {
            return StartupRouteDecision.InstanceAdmin;
        }

        var shellTask = _publicExperienceService.GetCachedShellAsync();
        var shell = shellTask is null ? null : await shellTask;
        string homeRoute;
        if (shell is not null)
        {
            homeRoute = _publicExperienceService.ResolveHomeRoute(shell);
        }
        else
        {
            var settingsTask = _publicExperienceService.GetCachedSettingsAsync();
            var settings = settingsTask is null ? null : await settingsTask;
            homeRoute = _publicExperienceService.ResolveHomeRoute(settings);
        }

        return homeRoute.Equals("/home", StringComparison.OrdinalIgnoreCase)
            ? StartupRouteDecision.PublicLanding
            : StartupRouteDecision.PublicHome;
    }
}

public enum StartupRouteDecision
{
    PublicHome,
    PublicLanding,
    Setup,
    InstanceAdmin,
    KeycloakChallenge,
    AtprotoChallenge,
    Unavailable
}
