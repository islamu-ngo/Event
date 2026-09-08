using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Queries;

public class GetTenantUserRoleGrantListRequestHandler : IRequestHandler<GetTenantUserRoleGrantListRequest, List<TenantUserRoleGrantListDto>>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly IMapper _mapper;

    public GetTenantUserRoleGrantListRequestHandler(ITenantUserRoleGrantRepository tenantUserRoleGrantRepository, IMapper mapper)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _mapper = mapper;
    }

    public async Task<List<TenantUserRoleGrantListDto>> Handle(GetTenantUserRoleGrantListRequest request, CancellationToken cancellationToken)
    {
        var tenantUserRoleGrants = await _tenantUserRoleGrantRepository.GetGrantsWithDetails();
        return _mapper.Map<List<TenantUserRoleGrantListDto>>(tenantUserRoleGrants);
    }
}
