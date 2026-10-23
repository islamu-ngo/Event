using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;

namespace Explore.Persistence.Repositories;

public sealed class ExternalApiKeyAuthenticationRepository(IGlobalExploreDbContextFactory factory,
    PostgresNativeQueueEnrollment? enrollment = null)
    : IExternalApiKeyAuthenticationRepository
{
    public async Task<ExternalApiKey?> GetByKeyIdForAuthentication(
        string keyId, CancellationToken cancellationToken = default)
    {
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await ExternalApiKeyAuthenticationQuery.ReadAsync(context, keyId, cancellationToken, enrollment);
    }

    public async Task<bool> TouchUsageMetadata(
        Guid id, DateTime usedAtUtc, string? lastUsedIp, TimeSpan minUpdateInterval,
        CancellationToken cancellationToken = default)
    {
        DateTime threshold = usedAtUtc.Subtract(minUpdateInterval);
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        return await ExternalApiKeyAuthenticationQuery.TouchUsageAsync(
            context, id, usedAtUtc, lastUsedIp, threshold, cancellationToken, enrollment);
    }
}
