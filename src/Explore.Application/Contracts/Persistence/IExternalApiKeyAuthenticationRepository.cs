using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Resolves persisted credentials before request tenancy exists, without exposing management operations.</summary>
public interface IExternalApiKeyAuthenticationRepository
{
    Task<ExternalApiKey?> GetByKeyIdForAuthentication(
        string keyId, CancellationToken cancellationToken = default);

    Task<bool> TouchUsageMetadata(
        Guid id,
        DateTime usedAtUtc,
        string? lastUsedIp,
        TimeSpan minUpdateInterval,
        CancellationToken cancellationToken = default);
}
