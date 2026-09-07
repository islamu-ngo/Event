// Used by the UI to enable/disable single-tenant revert based on tenant count.

using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.InstanceOnboarding.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.InstanceOnboarding.Handlers.Queries;

public class GetActiveTenantCountQueryHandler : IRequestHandler<GetActiveTenantCountQuery, int>
{
    private readonly ITenantRepository _tenantRepository;

    public GetActiveTenantCountQueryHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    public async Task<int> Handle(GetActiveTenantCountQuery request, CancellationToken cancellationToken)
    {
        return await _tenantRepository.GetActiveTenantCountAsync();
    }
}
