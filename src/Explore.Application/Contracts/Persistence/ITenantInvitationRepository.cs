using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Repository for tenant invitation management.
/// </summary>
public interface ITenantInvitationRepository : IGenericRepository<TenantInvitation, Guid>
{
    /// <summary>
    /// Gets an invitation by its unique acceptance token.
    /// Returns null if no matching token exists or the invitation has already been accepted.
    /// </summary>
    Task<TenantInvitation?> GetByTokenAsync(string token);

    /// <summary>
    /// Gets all pending (unaccepted, non-expired) invitations for a given email address within a tenant.
    /// </summary>
    Task<List<TenantInvitation>> GetPendingByEmailAsync(Guid tenantId, string email);

    /// <summary>
    /// Checks whether an active (unaccepted, non-expired) invitation exists
    /// for the specified email address within a tenant.
    /// </summary>
    Task<bool> ExistsActiveAsync(Guid tenantId, string email);
}
