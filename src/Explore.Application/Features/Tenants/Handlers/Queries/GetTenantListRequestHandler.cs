using System.Collections.Generic;
using Explore.Application.Mappings;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Tenants.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Tenants.Handlers.Queries;

public class GetTenantListRequestHandler : IRequestHandler<GetTenantListRequest, List<TenantListDto>>
{
    private readonly ITenantRepository _tenantRepository;

    public GetTenantListRequestHandler(ITenantRepository tenantRepository)
    {
        _tenantRepository = tenantRepository;
    }

    public async Task<List<TenantListDto>> Handle(GetTenantListRequest request, CancellationToken cancellationToken)
    {
        var tenants = await _tenantRepository.GetAll();
        return tenants.Select(TenantMapper.ToListItem).ToList();
    }
}
