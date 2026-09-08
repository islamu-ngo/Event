using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class PreviewKeycloakRealmSyncQueryHandler : IRequestHandler<PreviewKeycloakRealmSyncQuery, KeycloakRealmSyncPlanDto>
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

    public async Task<KeycloakRealmSyncPlanDto> Handle(PreviewKeycloakRealmSyncQuery request, CancellationToken cancellationToken)
    {
        var configuration = await _configurationService.ReadConfigurationAsync();
        return await _keycloakBootstrapService.PreviewRealmSyncAsync(configuration, request.Request, cancellationToken);
    }
}
