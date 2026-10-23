namespace Explore.Application.Contracts.Persistence;

/// <summary>
/// Reads committed administrative authority for one exact internal user independently
/// of request tenant binding. Scoped authorization and transactional writes remain on their owners.
/// </summary>
public interface IAdministrativeAuthorityRepository
{
    Task<bool> IsInstanceAdminAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetAdminTenantIdsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetAdminOrganizationIdsAsync(
        Guid userId, Guid? tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>> GetAdminGroupIdsAsync(
        Guid userId, Guid? tenantId, CancellationToken cancellationToken = default);
}
