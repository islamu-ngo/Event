// Used by the UI to enable/disable single-tenant revert based on tenant count.

using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class GetActiveTenantCountQueryHandler : IQueryHandler<GetActiveTenantCountQuery, int>
{
    private readonly ITenantRepository _tenantRepository;

    public GetActiveTenantCountQueryHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    public async Task<int> QueryAsync(GetActiveTenantCountQuery request, CancellationToken cancellationToken = default)
    {
        return await _tenantRepository.GetActiveTenantCountAsync();
    }
}
