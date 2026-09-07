using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;
using MediatR;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Commands;

public class RevokeTenantUserRoleGrantCommandHandler : IRequestHandler<RevokeTenantUserRoleGrantCommand, bool>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly ICurrentUserService _currentUserService;

    public RevokeTenantUserRoleGrantCommandHandler(
        ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        ICurrentUserService currentUserService)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _currentUserService = currentUserService;
    }

    public async Task<bool> Handle(RevokeTenantUserRoleGrantCommand request, CancellationToken cancellationToken)
    {
        var tenantUserRoleGrant = await _tenantUserRoleGrantRepository.GetById(request.Id);
        if (tenantUserRoleGrant == null || tenantUserRoleGrant.RevokedAt is not null)
        {
            return false;
        }

        tenantUserRoleGrant.RevokedAt = DateTime.UtcNow;
        tenantUserRoleGrant.RevokedBy = _currentUserService.UserId;
        tenantUserRoleGrant.UpdatedAt = DateTime.UtcNow;
        tenantUserRoleGrant.UpdatedBy = _currentUserService.UserId;

        await _tenantUserRoleGrantRepository.Update(tenantUserRoleGrant);
        return true;
    }
}
