using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class ApplyKeycloakRealmSyncCommandHandler(
    IAuthProviderConfigurationService configurationService,
    IKeycloakBootstrapService keycloakBootstrapService)
    : IRequestHandler<ApplyKeycloakRealmSyncCommand, KeycloakRealmSyncPlanDto>
{
    private readonly IAuthProviderConfigurationService _configurationService = configurationService;
    private readonly IKeycloakBootstrapService _keycloakBootstrapService = keycloakBootstrapService;

    public async Task<KeycloakRealmSyncPlanDto> Handle(
        ApplyKeycloakRealmSyncCommand request,
        CancellationToken cancellationToken)
    {
        var configuration = await _configurationService.ReadConfigurationWithSecretsAsync();
        return await _keycloakBootstrapService.ApplyRealmSyncAsync(configuration, request.Request, cancellationToken);
    }
}
