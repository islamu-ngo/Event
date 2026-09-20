using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class PreviewKeycloakRealmSyncQueryHandler : IQueryHandler<PreviewKeycloakRealmSyncQuery, KeycloakRealmSyncPlanDto>
{
    private readonly IAuthProviderConfigurationService _configurationService;
    private readonly IKeycloakBootstrapService _keycloakBootstrapService;

    public PreviewKeycloakRealmSyncQueryHandler(
        IAuthProviderConfigurationService configurationService,
        IKeycloakBootstrapService keycloakBootstrapService)
    {
        _configurationService = configurationService;
        _keycloakBootstrapService = keycloakBootstrapService;
    }

    public async Task<KeycloakRealmSyncPlanDto> QueryAsync(PreviewKeycloakRealmSyncQuery request, CancellationToken cancellationToken)
    {
        var configuration = await _configurationService.ReadConfigurationAsync();
        return await _keycloakBootstrapService.PreviewRealmSyncAsync(configuration, request.Request, cancellationToken);
    }
}
