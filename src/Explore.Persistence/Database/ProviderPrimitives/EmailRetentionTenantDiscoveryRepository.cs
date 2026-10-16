using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EmailRetentionTenantDiscoveryRepository(IGlobalExploreDbContextFactory globalFactory)
    : IEmailRetentionTenantDiscoveryRepository
{
    public async Task<IReadOnlyList<Guid>> GetTenantIdsAsync(
        DateTime cutoffUtc, int maxTenants, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxTenants, 1);
        if (cutoffUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Email retention discovery requires a UTC cutoff.", nameof(cutoffUtc));
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        var provider = RelationalProviderClassifier.Classify(context.Database);
        if (context.IsTenantFilterBypassed || context.BindTenantLease() is not null)
            throw new InvalidOperationException("email_retention_discovery_requires_unbound_global");
        if (provider == RelationalProvider.PostgreSql)
            return await context.Database.SqlQueryRaw<Guid>(
                $"SELECT tenant_id AS \"Value\" FROM {PostgresEmailRetentionDiscoveryContract.FunctionSql(context)}({{0}}, {{1}})",
                cutoffUtc, maxTenants).ToArrayAsync(cancellationToken);

        return await context.EmailDispatchOutbox
            .IgnoreTenantFilter(TenantFilterBypassReasons.EmailDispatchWorkerCrossTenantQueue)
            .AsNoTracking()
            .Where(row => row.ContentRedactedAt == null
                && ((row.Status == EmailDispatchStatus.Sent && row.SentAt <= cutoffUtc)
                    || (row.Status == EmailDispatchStatus.Skipped && row.LastFailureAt <= cutoffUtc)))
            .GroupBy(row => row.TenantId)
            .Select(group => new { TenantId = group.Key, OldestCreatedAt = group.Min(row => row.CreatedAt) })
            .OrderBy(row => row.OldestCreatedAt)
            .ThenBy(row => row.TenantId)
            .Take(maxTenants)
            .Select(row => row.TenantId)
            .ToArrayAsync(cancellationToken);
    }
}
