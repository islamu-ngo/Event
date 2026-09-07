using Explore.Domain;
using Explore.Domain.Enums;

namespace Explore.Application.Contracts.Persistence;

public interface IExternalApiKeyRepository : IGenericRepository<ExternalApiKey, Guid>
{
    Task<ExternalApiKey?> GetByKeyIdForAuthentication(string keyId, CancellationToken cancellationToken = default);
    Task<bool> TouchUsageMetadata(Guid id, DateTime usedAtUtc, string? lastUsedIp, TimeSpan minUpdateInterval, CancellationToken cancellationToken = default);
    Task<List<ExternalApiKey>> GetByOwner(ExternalApiKeyOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default);
    Task<List<ExternalApiKey>> GetByOwners(ExternalApiKeyOwnerType ownerType, IReadOnlyCollection<Guid> ownerIds, CancellationToken cancellationToken = default);
    Task<bool> ExistsByOwnerAndName(ExternalApiKeyOwnerType ownerType, Guid ownerId, string name, CancellationToken cancellationToken = default);

    /// <summary>Lookup by ID bypassing tenant filter. Used by management handlers for platform-scoped keys.</summary>
    Task<ExternalApiKey?> GetByIdIgnoringTenantFilter(Guid id, CancellationToken cancellationToken = default);

    /// <summary>List keys by owner bypassing tenant filter. Used for InstanceAdmin platform-scoped key listing.</summary>
    Task<List<ExternalApiKey>> GetByOwnerIgnoringTenantFilter(ExternalApiKeyOwnerType ownerType, Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>Name uniqueness check bypassing tenant filter. Used for platform-scoped key creation.</summary>
    Task<bool> ExistsByOwnerAndNameIgnoringTenantFilter(ExternalApiKeyOwnerType ownerType, Guid ownerId, string name, CancellationToken cancellationToken = default);
}
