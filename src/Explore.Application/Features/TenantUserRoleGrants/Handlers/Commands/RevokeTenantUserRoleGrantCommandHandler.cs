using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Commands;

public class RevokeTenantUserRoleGrantCommandHandler : ICommandHandler<RevokeTenantUserRoleGrantCommand, bool>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly ITenantUserRoleGrantMutationBoundary _boundary;
    private readonly IHumanAccountAdministrativeActionPreparation _preparation;

    public RevokeTenantUserRoleGrantCommandHandler(
        ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        ITenantUserRoleGrantMutationBoundary boundary,
        IHumanAccountAdministrativeActionPreparation preparation)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _boundary = boundary;
        _preparation = preparation;
    }

    public async Task<bool> ExecuteAsync(RevokeTenantUserRoleGrantCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        return await _boundary.ExecuteRevokeAsync(request.TenantId, request.Id, async (actor, token) =>
        {
            if (actor != prepared.PrincipalId) return false;
            await prepared.AcquireAsync(request, token);
            var result = await ExecuteBodyAsync(request, prepared);
            if (result) prepared.Complete(request.Id);
            return result;
        }, cancellationToken);
    }

    private async Task<bool> ExecuteBodyAsync(RevokeTenantUserRoleGrantCommand request,
        IPreparedHumanAccountAdministrativeAction prepared)
    {
        var tenantUserRoleGrant = await _tenantUserRoleGrantRepository.GetById(request.Id);
        if (tenantUserRoleGrant == null || tenantUserRoleGrant.TenantId != request.TenantId
            || tenantUserRoleGrant.RevokedAt is not null)
        {
            return false;
        }

        tenantUserRoleGrant.RevokedAt = prepared.ChangedAt;
        tenantUserRoleGrant.RevokedBy = prepared.PrincipalId;
        tenantUserRoleGrant.UpdatedAt = prepared.ChangedAt;
        tenantUserRoleGrant.UpdatedBy = prepared.PrincipalId;

        await _tenantUserRoleGrantRepository.Update(tenantUserRoleGrant);
        return true;
    }
}
