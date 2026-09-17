using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Services;
using Explore.Application.DTOs.Onboarding;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class GetResolverConfigurationQueryHandler : IQueryHandler<GetResolverConfigurationQuery, ResolverConfigurationDto>
{
    private readonly IResolverConfigService _resolverConfigService;

    public GetResolverConfigurationQueryHandler(IResolverConfigService resolverConfigService)
    {
        _resolverConfigService = resolverConfigService;
    }

    public async Task<ResolverConfigurationDto> QueryAsync(GetResolverConfigurationQuery request, CancellationToken cancellationToken)
    {
        return await _resolverConfigService.GetConfigurationAsync(cancellationToken);
    }
}
