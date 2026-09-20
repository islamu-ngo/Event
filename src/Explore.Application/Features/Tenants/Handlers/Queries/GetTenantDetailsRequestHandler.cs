using Explore.Application.Contracts.Operations;
using Explore.Domain.ValueObjects;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Tenants.Requests.Queries;

namespace Explore.Application.Features.Tenants.Handlers.Queries;

public class GetTenantDetailsRequestHandler : IQueryHandler<GetTenantDetailsRequest, TenantDto?>
{
    private readonly ITenantRepository _tenantRepository;

    public GetTenantDetailsRequestHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    public async Task<TenantDto?> QueryAsync(GetTenantDetailsRequest request, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.GetByIdAsNoTrackingAsync(request.Id, cancellationToken);
        if (tenant == null || !TenantLifecycleAccessPolicy.AllowsPublic(tenant.TenantStatusId))
        {
            return null;
        }

        return TenantMapper.ToDetail(tenant);
    }
}
