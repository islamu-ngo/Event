using Explore.Application.Contracts.Operations;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Features.TenantUserRoleGrants.Requests.Commands;

namespace Explore.Application.Features.TenantUserRoleGrants.Handlers.Commands;

public class RevokeTenantUserRoleGrantCommandHandler : ICommandHandler<RevokeTenantUserRoleGrantCommand, bool>
{
    private readonly ITenantUserRoleGrantRepository _tenantUserRoleGrantRepository;
    private readonly IHumanAccountAdministrativeActionPreparation _preparation;
    private readonly ISerializableAttemptOwner _owner;

    public RevokeTenantUserRoleGrantCommandHandler(
        ITenantUserRoleGrantRepository tenantUserRoleGrantRepository,
        IHumanAccountAdministrativeActionPreparation preparation,
        ISerializableAttemptOwner owner)
    {
        _tenantUserRoleGrantRepository = tenantUserRoleGrantRepository;
        _preparation = preparation;
        _owner = owner;
    }

    public async Task<bool> ExecuteAsync(RevokeTenantUserRoleGrantCommand request, CancellationToken cancellationToken)
    {
        var prepared = await _preparation.PrepareAsync(request, cancellationToken);
        return await _owner.ExecuteAsync(async token =>
        {
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
        if (tenantUserRoleGrant == null || tenantUserRoleGrant.RevokedAt is not null)
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
