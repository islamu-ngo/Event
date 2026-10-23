using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Persistence.Repositories;

public sealed class AtprotoBootstrapReplayRepository(
    ExploreDbContext dbContext, TimeProvider? clock = null,
    PostgresNativeQueueEnrollment? enrollment = null, IServiceProvider? services = null) : IAtprotoBootstrapReplayRepository
{
    private const string KeyPrefix = "atproto-bootstrap:";
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly ExploreDbContextExecutionLease? _lease = services?.GetService<ExploreDbContextExecutionLease>();

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
        var now = _clock.GetUtcNow().UtcDateTime;
        if (expiresAt <= now)
        {
            return false;
        }

        var context = _lease?.Context ?? dbContext;
        if (context.IsTenantFilterBypassed || context.BindTenantLease() != tenantId)
            throw new UnauthorizedAccessException("Bootstrap receipt requires its selected tenant.");
        if (AtprotoRepositoryNativeOperations.For(context) is { } native)
            return await native.ConsumeBootstrapAsync(enrollment, tenantId, jti, now, expiresAt.UtcDateTime, cancellationToken);
        var claim = await new IdempotencyRepository(context, _clock).TryClaimAsync(
            CreateRecord(key, tenantId, now, expiresAt.UtcDateTime),
            cancellationToken).ConfigureAwait(false);
        using var owner = claim.Owner;
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
