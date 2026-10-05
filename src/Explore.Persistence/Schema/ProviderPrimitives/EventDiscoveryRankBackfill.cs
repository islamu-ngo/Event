using Explore.Domain.Services.Discovery;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Schema;

/// <summary>
/// Completes the required rank columns under migration authority before readiness.
/// Native keysets bound every read; no card graphs, source counts, or runtime fallback are involved.
/// </summary>
public static class EventDiscoveryRankBackfill
{
    private const int BatchSize = 256;

    public static async Task ApplyAsync(ExploreDbContext database, CancellationToken cancellationToken)
    {
        Guid? afterTenant = null;
        while (true)
        {
            var tenants = database.Tenants.IgnoreAllFilters(TenantFilterBypassReasons.DiscoveryRankMigration)
                .AsNoTracking().AsQueryable();
            if (afterTenant is { } tenantCursor)
                tenants = tenants.Where(tenant => tenant.Id.CompareTo(tenantCursor) > 0);
            var ids = await tenants.OrderBy(tenant => tenant.Id).Select(tenant => tenant.Id)
                .Take(BatchSize).ToListAsync(cancellationToken);
            if (ids.Count == 0)
                break;
            foreach (Guid tenantId in ids)
            {
                Guid? afterEvent = null;
                while (true)
                {
                    var next = await new EfCoreUnitOfWork(database).ExecuteSerializableAsync(
                        token => EventDiscoveryDisclosureRepository.InTenantAsync(database, tenantId, async () =>
                        {
                            var events = database.Events.IgnoreAllFilters(TenantFilterBypassReasons.DiscoveryRankMigration)
                                .AsNoTracking().Where(entity => entity.TenantId == tenantId
                                    && entity.DiscoverySourceSortKey == string.Empty);
                            if (afterEvent is { } eventCursor)
                                events = events.Where(entity => entity.Id.CompareTo(eventCursor) > 0);
                            var rows = await events.OrderBy(entity => entity.Id)
                                .Select(entity => new { entity.Id, entity.Title }).Take(BatchSize).ToListAsync(token);
                            foreach (var row in rows)
                            {
                                string titleKey = EventDiscoveryRank.TitleKey(row.Title);
                                string sourceKey = EventDiscoveryRank.SourceKey(row.Id);
                                await database.Events.IgnoreAllFilters(TenantFilterBypassReasons.DiscoveryRankMigration)
                                    .Where(entity => entity.TenantId == tenantId && entity.Id == row.Id)
                                    .ExecuteUpdateAsync(setters => setters
                                        .SetProperty(entity => entity.DiscoveryTitleSortKey, titleKey)
                                        .SetProperty(entity => entity.DiscoverySourceSortKey, sourceKey), token);
                            }
                            return rows.Count == 0 ? (Guid?)null : rows[^1].Id;
                        }, token), cancellationToken);
                    if (next is null)
                        break;
                    afterEvent = next;
                }
            }
            afterTenant = ids[^1];
        }

        Guid? afterRecord = null;
        while (true)
        {
            var next = await new EfCoreUnitOfWork(database).ExecuteSerializableAsync(async token =>
            {
                var projections = database.AtprotoEventProjections
                    .IgnoreAllFilters(TenantFilterBypassReasons.DiscoveryRankMigration).AsNoTracking()
                    .Where(entity => entity.DiscoverySourceSortKey == string.Empty);
                if (afterRecord is { } recordCursor)
                    projections = projections.Where(entity => entity.AtprotoRecordId.CompareTo(recordCursor) > 0);
                var rows = await projections.OrderBy(entity => entity.AtprotoRecordId)
                    .Select(entity => new { entity.AtprotoRecordId, entity.Name }).Take(BatchSize).ToListAsync(token);
                foreach (var row in rows)
                {
                    string titleKey = EventDiscoveryRank.TitleKey(row.Name);
                    string sourceKey = EventDiscoveryRank.SourceKey(row.AtprotoRecordId);
                    await database.AtprotoEventProjections.IgnoreAllFilters(TenantFilterBypassReasons.DiscoveryRankMigration)
                        .Where(entity => entity.AtprotoRecordId == row.AtprotoRecordId)
                        .ExecuteUpdateAsync(setters => setters
                            .SetProperty(entity => entity.DiscoveryTitleSortKey, titleKey)
                            .SetProperty(entity => entity.DiscoverySourceSortKey, sourceKey), token);
                }
                return rows.Count == 0 ? (Guid?)null : rows[^1].AtprotoRecordId;
            }, cancellationToken);
            if (next is null)
                break;
            afterRecord = next;
        }
    }
}
