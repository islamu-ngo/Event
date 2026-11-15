using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface ITenantUserRepository : IGenericRepository<TenantUser, Guid>
{
    /// <summary>Creates a membership using existing principal references tracked by the committing context.</summary>
    Task<TenantUser> CreateForExistingAccountAsync(TenantUser membership, CancellationToken cancellationToken = default);
    /// <summary>Reads the exact membership including retired rows without private profile data.</summary>
    Task<TenantUser?> GetForEnrollmentAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);
    Task<TenantUser?> GetByTenantAndUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    Task<TenantUser?> GetByTenantAndActorAsync(Guid tenantId, Guid actorId, CancellationToken cancellationToken = default);
    Task<bool> IsActiveTenantUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    /// <summary>Holds the membership authority row through the caller-owned transaction before evaluating it.</summary>
    Task<bool> FenceActiveTenantUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> TryRemoveMembershipAsync(
        Guid tenantId,
        Guid userId,
        Guid removedBy,
        DateTime removedAtUtc,
        CancellationToken cancellationToken = default);
}
