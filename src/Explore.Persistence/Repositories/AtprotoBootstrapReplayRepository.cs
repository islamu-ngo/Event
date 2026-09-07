using Explore.Application.Contracts.Persistence;
using Explore.Domain;

namespace Explore.Persistence.Repositories;

public sealed class AtprotoBootstrapReplayRepository(ExploreDbContext dbContext) : IAtprotoBootstrapReplayRepository
{
    private const string KeyPrefix = "atproto-bootstrap:";

    public async Task<bool> TryConsumeAsync(
        string jti,
        Guid tenantId,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(jti) || jti.Length > 64 || tenantId == Guid.Empty)
        {
            return false;
        }

        var key = KeyPrefix + jti;
        var now = DateTime.UtcNow;
        if (expiresAt <= now)
        {
            return false;
        }

        var claim = await new IdempotencyRepository(dbContext).TryClaimAsync(
            CreateRecord(key, tenantId, now, expiresAt.UtcDateTime),
            cancellationToken).ConfigureAwait(false);
        return claim.IsOwner;
    }

    private static IdempotencyRecord CreateRecord(
        string key,
        Guid tenantId,
        DateTime createdAt,
        DateTime expiresAt) => new()
        {
            Id = Guid.CreateVersion7(),
            Key = key,
            TenantId = tenantId,
            RequestMethod = HttpMethod.Post.Method,
            RequestTarget = "/api/auth/atproto/session",
            RequestBodyHash = string.Empty,
            PrincipalFingerprint = string.Empty,
            StatusCode = 0,
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
}
