using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IExternalApiKeyRepository
{
    Task<List<ExternalApiKey>> GetByOwner(ExternalApiKeyOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default);
    Task<List<ExternalApiKey>> GetByOwners(ExternalApiKeyOwnerType ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default);
    Task<bool> ExistsByOwnerAndName(ExternalApiKeyOwnerType ownerType, Guid ownerId, string name, CancellationToken cancellationToken = default);

    /// <summary>Fresh lookup restricted to the exact non-bypassed current tenant.</summary>
    Task<ExternalApiKey?> GetForTenantAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Fences and rereads the exact visible key in the caller-owned Serializable transaction.</summary>
    Task<ExternalApiKey?> GetForManagementAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Persists only the key's management policy, revocation status and update audit fields.</summary>
    Task Update(ExternalApiKey entity, CancellationToken cancellationToken);
}
