using Explore.Application.Responses;

namespace Explore.Application.Contracts.Services;

/// <summary>Owns native role-write authority, serialization and qualification invalidation.</summary>
public interface ITenantUserRoleGrantMutationBoundary
{
    Task<BaseCommandResponse<Guid>> ExecuteCreateAsync(Guid tenantId, Guid tenantUserId, int roleId,
        Func<Guid, CancellationToken, Task<BaseCommandResponse<Guid>>> operation, CancellationToken cancellationToken);

    Task<bool> ExecuteRevokeAsync(Guid tenantId, Guid grantId,
        Func<Guid, CancellationToken, Task<bool>> operation, CancellationToken cancellationToken);
}
