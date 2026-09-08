using System.Collections.Generic;
using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.Tenant;
using Explore.Application.Features.Tenants.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Tenants.Handlers.Queries;

public class GetTenantListRequestHandler : IRequestHandler<GetTenantListRequest, List<TenantListDto>>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IMapper _mapper;

    public GetTenantListRequestHandler(ITenantRepository tenantRepository, IMapper mapper)
    {
        _tenantRepository = tenantRepository;
        _mapper = mapper;
    }

    public async Task<List<TenantListDto>> Handle(GetTenantListRequest request, CancellationToken cancellationToken)
    {
        var tenants = await _tenantRepository.GetAll();
        return _mapper.Map<List<TenantListDto>>(tenants);
    }
}
