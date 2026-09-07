using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class GetAuthProviderConfigurationQueryHandler : IRequestHandler<GetAuthProviderConfigurationQuery, AuthProviderConfigurationDto>
{
    private readonly IAuthProviderConfigurationService _configurationService;

    public GetAuthProviderConfigurationQueryHandler(IAuthProviderConfigurationService configurationService)
    {
        _configurationService = configurationService;
    }

    public async Task<AuthProviderConfigurationDto> Handle(GetAuthProviderConfigurationQuery request, CancellationToken cancellationToken)
    {
        return await _configurationService.ReadConfigurationAsync();
    }
}
