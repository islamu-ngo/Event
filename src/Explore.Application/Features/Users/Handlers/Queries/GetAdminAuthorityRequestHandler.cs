using Explore.Application.Contracts.Identity;
using Explore.Application.DTOs.User;
using Explore.Application.Features.Users.Requests.Queries;
using MediatR;

namespace Explore.Application.Features.Users.Handlers.Queries;

public class GetAdminAuthorityRequestHandler : IRequestHandler<GetAdminAuthorityRequest, AdminAuthorityDto>
{
    private readonly IAdminContext _adminContext;

    public GetAdminAuthorityRequestHandler(IAdminContext adminContext)
    {
        _adminContext = adminContext;
    }

    public async Task<AdminAuthorityDto> Handle(GetAdminAuthorityRequest request, CancellationToken cancellationToken)
    {
        var isInstanceAdmin = await _adminContext.IsInstanceAdminAsync(request.UserId, cancellationToken);
        var tenantIds = await _adminContext.GetAdminTenantIdsAsync(request.UserId, cancellationToken);
        var orgIds = await _adminContext.GetAdminOrganizationIdsAsync(request.UserId, cancellationToken);
        var groupIds = await _adminContext.GetAdminGroupIdsAsync(request.UserId, cancellationToken);

        return new AdminAuthorityDto
        {
            IsInstanceAdmin = isInstanceAdmin,
            AdminTenantIds = tenantIds.ToList(),
            AdminOrganizationIds = orgIds.ToList(),
            AdminGroupIds = groupIds.ToList()
        };
    }
}
