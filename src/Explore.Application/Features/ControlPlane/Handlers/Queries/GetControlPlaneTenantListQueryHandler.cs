using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.ControlPlane;
using Explore.Application.Features.ControlPlane.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.ControlPlane.Handlers.Queries;

public sealed class GetControlPlaneTenantListQueryHandler(ITenantRepository tenantRepository)
    : IRequestHandler<GetControlPlaneTenantListQuery, IReadOnlyList<ControlPlaneTenantListItemDto>>
{
    public async Task<IReadOnlyList<ControlPlaneTenantListItemDto>> Handle(
        GetControlPlaneTenantListQuery request,
        CancellationToken cancellationToken)
    {
        _ = request;
        _ = cancellationToken;

        var tenants = await tenantRepository.GetAll();

        return tenants
            .OrderBy(tenant => tenant.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(ControlPlaneTenantMapper.ToListItem)
            .ToArray();
    }
}
