using AutoMapper;
using Explore.Application.Contracts.Persistence;
using Explore.Application.DTOs.TenantUserRoleGrant;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Queries;

public class GetTenantUserRoleGrantDetailsRequestHandler : IRequestHandler<GetTenantUserRoleGrantDetailsRequest, TenantUserRoleGrantDto?>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly IMapper _mapper;

    public GetTenantUserRoleGrantDetailsRequestHandler(ITenantUserRoleGrantRepository tenantUserRoleGrantRepository, IMapper mapper)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _mapper = mapper;
    }

    public async Task<TenantUserRoleGrantDto?> Handle(GetTenantUserRoleGrantDetailsRequest request, CancellationToken cancellationToken)
    {
        var tenantUserRoleGrant = await _tenantUserRoleGrantRepository.GetGrantWithDetails(request.Id);
        if (tenantUserRoleGrant == null)
        {
            return null;
        }

        return _mapper.Map<TenantUserRoleGrantDto>(tenantUserRoleGrant);
    }
}
