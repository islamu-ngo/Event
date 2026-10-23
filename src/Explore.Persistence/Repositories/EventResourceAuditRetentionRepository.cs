using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Application.Services;
using Explore.Persistence.QueryFilters;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Repositories;

public sealed class EventResourceAuditRetentionRepository(ExploreDbContext context)
    : IEventResourceAuditRetentionRepository
{
    public async Task<int> DeleteExpiredBatchAsync(
        Guid tenantId, DateTime? inclusiveCutoffUtc, int limit, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || inclusiveCutoffUtc is { Kind: not DateTimeKind.Utc })
            throw new ArgumentException("Audit deletion requires an exact tenant and a UTC cutoff.");
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(limit, EventResourceAuditRetentionService.DeleteBatchSize);
        var candidates = context.Set<EventResourceAuditEntry>()
            .IgnoreAllFilters(TenantFilterBypassReasons.EventResourceAuditRetention)
            .Where(entry => entry.TenantId == tenantId);
        if (inclusiveCutoffUtc is { } cutoff)
            candidates = candidates.Where(entry => entry.Timestamp <= cutoff);
        Guid[] ids = await candidates.AsNoTracking().OrderBy(entry => entry.Timestamp).ThenBy(entry => entry.Id)
            .Take(limit).Select(entry => entry.Id).ToArrayAsync(cancellationToken);
        if (ids.Length == 0) return 0;
        return await candidates.Where(entry => ids.Contains(entry.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
