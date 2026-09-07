using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IActorRepository : IGenericRepository<Actor, Guid>
{
    Task<Actor?> GetActorWithDetails(Guid id, CancellationToken cancellationToken = default);
    Task<Actor?> GetPublicActorProfileAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Actor?> GetPublicActorProfileByTenantAsync(
        Guid tenantId,
        Guid actorId,
        CancellationToken cancellationToken = default);
    Task<Actor?> GetLocallyDiscoverableSubscriptionTargetAsync(
        Guid tenantId,
        Guid actorId,
        CancellationToken cancellationToken = default);
    Task<Actor?> GetActorByDid(string did, CancellationToken cancellationToken = default);
    Task<Actor?> GetActorByHandle(string handle, CancellationToken cancellationToken = default);
    Task<List<Actor>> GetActorsByTenant(Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> DidExists(string did);
    Task<(List<Actor> Items, int TotalCount)> GetActorsWithDetailsPaged(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Actor>> SearchAiReferenceActorsAsync(
        string searchTerm,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>
    /// Gets the Actor associated with a specific User (personal actor).
    /// </summary>
    Task<Actor?> GetActorByUserId(Guid userId);

    /// <summary>
    /// Gets the tracked personal Actor used by transactional identity workflows.
    /// </summary>
    Task<Actor?> GetTrackedActorByUserId(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the tenant-scoped Actor associated with a specific User.
    /// </summary>
    Task<Actor?> GetActorByUserIdAndTenantId(
        Guid userId,
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the Actor associated with a specific Organization.
    /// </summary>
    Task<Actor?> GetActorByOrganizationId(Guid organizationId);

    /// <summary>
    /// Gets the Actor associated with a specific Group.
    /// </summary>
    Task<Actor?> GetActorByGroupId(Guid groupId);

    /// <summary>
    /// Permanently deletes PII data for an actor (GDPR erasure).
    /// Uses ExecuteDeleteAsync for efficient bulk deletion without loading entities.
    /// </summary>
    /// <returns>Number of PII records deleted.</returns>
    Task<int> ForgetPiiAsync(Guid actorId);
}
