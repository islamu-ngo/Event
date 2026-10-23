using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Commands;

public class RevokeTenantUserRoleGrantCommandHandler : ICommandHandler<RevokeTenantUserRoleGrantCommand, bool>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly ITenantUserRoleGrantMutationBoundary _boundary;

    public RevokeTenantUserRoleGrantCommandHandler(
        ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        ITenantUserRoleGrantMutationBoundary boundary)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _boundary = boundary;
    }

    public Task<bool> ExecuteAsync(RevokeTenantUserRoleGrantCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _boundary.ExecuteRevokeAsync(request.TenantId, request.Id,
            (actor, token) => RevokeAsync(request, actor, token), cancellationToken);
    }

    private async Task<bool> RevokeAsync(RevokeTenantUserRoleGrantCommand request, Guid actor, CancellationToken cancellationToken)
    {
        var tenantUserRoleGrant = await _tenantUserRoleGrantRepository.GetById(request.Id);
        if (tenantUserRoleGrant == null || tenantUserRoleGrant.TenantId != request.TenantId
            || tenantUserRoleGrant.RevokedAt is not null)
        {
            return false;
        }

        tenantUserRoleGrant.RevokedAt = DateTime.UtcNow;
        tenantUserRoleGrant.RevokedBy = actor;
        tenantUserRoleGrant.UpdatedAt = DateTime.UtcNow;
        tenantUserRoleGrant.UpdatedBy = actor;

        await _tenantUserRoleGrantRepository.Update(tenantUserRoleGrant);
        return true;
    }
}
