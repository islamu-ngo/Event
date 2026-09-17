using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class RunKeycloakRealmDoctorQueryHandler : IQueryHandler<RunKeycloakRealmDoctorQuery, KeycloakRealmDoctorResultDto>
{
    private readonly IAuthProviderConfigurationService _configurationService;
    private readonly IKeycloakBootstrapService _keycloakBootstrapService;

    public RunKeycloakRealmDoctorQueryHandler(
        IAuthProviderConfigurationService configurationService,
        IKeycloakBootstrapService keycloakBootstrapService)
    {
        _configurationService = configurationService;
        _keycloakBootstrapService = keycloakBootstrapService;
    }

    public async Task<KeycloakRealmDoctorResultDto> QueryAsync(
        RunKeycloakRealmDoctorQuery request,
        CancellationToken cancellationToken)
    {
        var configuration = await _configurationService.ReadConfigurationAsync();
        return await _keycloakBootstrapService.DiagnoseRealmAsync(configuration, request.Request, cancellationToken);
    }
}
