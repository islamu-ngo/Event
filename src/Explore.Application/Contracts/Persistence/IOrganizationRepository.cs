using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

public interface IOrganizationRepository : IGenericRepository<Organization, Guid>
{
    Task<Organization?> GetOrganizationWithDetails(Guid id, CancellationToken cancellationToken = default);
    Task<Organization?> GetOrganizationWithDetailsByActorId(Guid actorId, CancellationToken cancellationToken = default);
    Task<List<Organization>> GetOrganizationsWithDetails(CancellationToken cancellationToken = default);
    Task<List<Organization>> GetMyOrganizations(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a paginated list of organizations with details.
    /// </summary>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>A tuple containing the items and total count.</returns>
    Task<(List<Organization> Items, int TotalCount)> GetOrganizationsWithDetailsPaged(
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a paginated list of organizations for the current user.
    /// </summary>
    /// <param name="userId">The user ID.</param>
    /// <param name="pageNumber">The page number (1-based).</param>
    /// <param name="pageSize">The number of items per page.</param>
    /// <returns>A tuple containing the items and total count.</returns>
    Task<(List<Organization> Items, int TotalCount)> GetMyOrganizationsPaged(
        Guid userId,
        int pageNumber,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Permanently deletes PII data for an organization (GDPR erasure).
    /// Uses ExecuteDeleteAsync for efficient bulk deletion without loading entities.
    /// </summary>
    /// <returns>Number of PII records deleted.</returns>
    Task<int> ForgetPiiAsync(Guid organizationId, CancellationToken cancellationToken = default);
}
