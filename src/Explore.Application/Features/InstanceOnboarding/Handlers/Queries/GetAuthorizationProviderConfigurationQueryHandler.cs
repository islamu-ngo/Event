using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class GetAuthorizationProviderConfigurationQueryHandler : IQueryHandler<GetAuthorizationProviderConfigurationQuery, AuthorizationProviderConfigurationDto>
{
    private readonly IAuthorizationProviderConfigurationService _configurationService;

    public GetAuthorizationProviderConfigurationQueryHandler(IAuthorizationProviderConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public async Task<AuthorizationProviderConfigurationDto> QueryAsync(GetAuthorizationProviderConfigurationQuery request, CancellationToken cancellationToken)
    {
        return await _configurationService.ReadConfigurationAsync();
    }
}
