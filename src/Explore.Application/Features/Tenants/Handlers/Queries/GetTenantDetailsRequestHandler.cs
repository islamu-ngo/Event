using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Tenants.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Tenants.Handlers.Queries;

public class GetTenantDetailsRequestHandler : IRequestHandler<GetTenantDetailsRequest, TenantDto>
{
    private readonly ITenantRepository _tenantRepository;

    public GetTenantDetailsRequestHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    public async Task<TenantDto> Handle(GetTenantDetailsRequest request, CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetById(request.Id);
        if (tenant == null)
        {
            return null;
        }

        return TenantMapper.ToDetail(tenant);
    }
}
