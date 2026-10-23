using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class IdempotencyCleanupTenantDiscoveryRepository(
    IGlobalExploreDbContextFactory globalFactory,
    PostgresNativeQueueEnrollment? enrollment = null)
    : IIdempotencyCleanupTenantDiscoveryRepository
{
    public async Task<IReadOnlyList<Guid>> GetExpiredTenantIdsAsync(
        DateTime expiresBeforeUtc, int maximumRows, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumRows, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRows, IdempotencyCleanupLimits.MaximumRows);
        if (expiresBeforeUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Idempotency cleanup discovery requires a UTC cutoff.", nameof(expiresBeforeUtc));

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        var provider = RelationalProviderClassifier.Classify(context.Database);
        if (context.IsTenantFilterBypassed || context.BindTenantLease() is not null)
            throw new InvalidOperationException("idempotency_cleanup_discovery_requires_unbound_global");
        if (provider == RelationalProvider.PostgreSql)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
            return await PostgresIdempotencyReceiptContract.GetExpiredTenantIdsAsync(
                context, enrollment ?? throw new InvalidOperationException("idempotency_cleanup_enrollment_required"),
                expiresBeforeUtc, maximumRows, cancellationToken);
        }

        // Bound eligible identities before deduplicating tenants: retained fences and
        // directory state cannot enlarge or suppress this maintenance page.
        var tenantIds = await context.IdempotencyRecords
            .IgnoreTenantFilter(TenantFilterBypassReasons.IdempotencyCleanupTenantDiscovery)
            .AsNoTracking()
            .Where(record => record.ExpiresAt <= expiresBeforeUtc
                && record.StatusCode != IdempotencyRecord.ResponseUnavailableStatusCode)
            .OrderBy(record => record.ExpiresAt)
            .ThenBy(record => record.Id)
            .Take(maximumRows)
            .Select(record => record.TenantId)
            .ToArrayAsync(cancellationToken);
        return tenantIds.Distinct().ToArray();
    }
}
