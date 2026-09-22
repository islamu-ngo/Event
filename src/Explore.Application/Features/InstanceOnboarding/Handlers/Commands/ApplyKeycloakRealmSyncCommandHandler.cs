using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Commands;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Commands;

public class ApplyKeycloakRealmSyncCommandHandler(
    IAuthProviderConfigurationService configurationService,
    IKeycloakBootstrapService keycloakBootstrapService)
    : ICommandHandler<ApplyKeycloakRealmSyncCommand, KeycloakRealmSyncPlanDto>
{
    private readonly IAuthProviderConfigurationService _configurationService = configurationService;
    private readonly IKeycloakBootstrapService _keycloakBootstrapService = keycloakBootstrapService;

    public async Task<KeycloakRealmSyncPlanDto> ExecuteAsync(
        ApplyKeycloakRealmSyncCommand request,
        CancellationToken cancellationToken)
    {
        var configuration = await _configurationService.ReadConfigurationAsync();
        return await _keycloakBootstrapService.ApplyRealmSyncAsync(configuration, request.Request, cancellationToken);
    }
}
