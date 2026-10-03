using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.InstanceAdmin;
using Explore.Application.Features.InstanceAdmin.Requests.Queries;
using Explore.Application.Contracts.Operations;

namespace Explore.Application.Features.InstanceAdmin.Handlers.Queries;

public sealed class GetInstanceTenantListQueryHandler(ITenantRepository tenantRepository)
    : IQueryHandler<GetInstanceTenantListQuery, IReadOnlyList<InstanceTenantListItemDto>>
{
    public async Task<IReadOnlyList<InstanceTenantListItemDto>> QueryAsync(
        GetInstanceTenantListQuery request,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;

        var tenants = await tenantRepository.GetAll();

        return tenants
            .OrderBy(tenant => tenant.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(InstanceTenantMapper.ToListItem)
            .ToArray();
    }
}
