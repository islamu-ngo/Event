using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Reads memberships for a server-resolved global user without borrowing the request tenant.</summary>
public interface IUserTenantMembershipRepository
{
    Task<List<TenantUser>> GetActiveTenantsForUserAsync(
        Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Checks one target tenant using ordinary tenant authority, without imposing a tenant-status condition.</summary>
    Task<bool> IsActiveTenantUserAsync(
        Guid tenantId, Guid userId, CancellationToken cancellationToken = default);

}
