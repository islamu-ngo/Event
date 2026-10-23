using Explore.Application.Contracts.Persistence;
using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class InstanceStorageAccountingProjection
{
    internal static void RequireSupportedProvider(ExploreDbContext context)
    {
        if (!context.Database.IsNpgsql() && !context.Database.IsSqlite() && !context.Database.IsSqlServer()
            && context.Database.ProviderName != RelationalNamedLock.MySqlProvider)
            throw new NotSupportedException("The selected provider has no original accounting custody protocol.");
    }

    internal static async Task<IReadOnlyList<InstanceStorageProviderTotals>> ReadAsync(
        ExploreDbContext global, CancellationToken cancellationToken, string? function = null)
    {
        if (global.Database.IsNpgsql())
            return await global.Database.SqlQueryRaw<InstanceStorageProviderTotals>(
                $"""
                SELECT "Provider" AS provider, "UsedBytes" AS used_bytes,
                    "ReservedBytes" AS reserved_bytes, "QuarantinedBytes" AS quarantined_bytes,
                    "ObjectCount" AS object_count, "LastRecalculatedAt" AS last_recalculated_at
                FROM {function ?? PostgresInstanceStorageAdministrationContract.UsageFunctionSql(global)}()
                """)
                .ToListAsync(cancellationToken);

        return await global.StorageUsageCounters.AsNoTracking()
            .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
            .GroupBy(row => row.Provider)
            .OrderBy(group => group.Key)
            .Select(group => new InstanceStorageProviderTotals(
                group.Key, group.Sum(row => row.UsedBytes), group.Sum(row => row.ReservedBytes),
                group.Sum(row => row.QuarantinedBytes), group.Sum(row => row.ObjectCount),
                group.Max(row => row.LastRecalculatedAt)))
            .ToListAsync(cancellationToken);
    }

    internal static async Task<IReadOnlyList<InstanceStorageProviderTotals>> RecalculateAsync(
        ExploreDbContext global, CancellationToken ct)
    {
        if (global.Database.IsNpgsql())
            return await ReadAsync(global, ct,
                PostgresInstanceStorageAdministrationContract.RecalculateFunctionSql(global));

        var scopes = await global.StorageUsageCounters.AsNoTracking()
                .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
                .Select(row => new { row.TenantId, row.Provider })
                .Union(global.StorageObjects.AsNoTracking()
                    .IgnoreQueryFilters([QueryFilterNames.Tenant, QueryFilterNames.SoftDelete])
                    .Where(row => row.Provider != StorageProviders.LegacyExternal)
                    .Select(row => new { row.TenantId, row.Provider }))
                .Union(global.StorageUploadSessions.AsNoTracking()
                    .IgnoreTenantFilter(TenantFilterBypassReasons.InstanceStorageAdministration)
                    .Select(row => new { row.TenantId, row.Provider }))
                .ToListAsync(ct);

        var entity = global.Model.FindEntityType(typeof(StorageUsageCounter))!;
        var store = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = global.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(entity.GetTableName()!, entity.GetSchema());
        string C(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(store)!);
        string id = C(nameof(StorageUsageCounter.Id)), tenant = C(nameof(StorageUsageCounter.TenantId));
        string provider = C(nameof(StorageUsageCounter.Provider)), stamp = C(nameof(StorageUsageCounter.ConcurrencyStamp));
        string used = C(nameof(StorageUsageCounter.UsedBytes)), reserved = C(nameof(StorageUsageCounter.ReservedBytes));
        string quarantined = C(nameof(StorageUsageCounter.QuarantinedBytes)), count = C(nameof(StorageUsageCounter.ObjectCount));
        string created = C(nameof(StorageUsageCounter.CreatedAt)), updated = C(nameof(StorageUsageCounter.UpdatedAt));
        string last = C(nameof(StorageUsageCounter.LastRecalculatedAt));
        DateTime now = DateTime.UtcNow;
        foreach (var scope in scopes.OrderBy(scope => scope.TenantId).ThenBy(scope => scope.Provider, StringComparer.Ordinal))
        {
            await global.Database.ExecuteSqlRawAsync($$"""
                INSERT INTO {{table}} ({{id}}, {{tenant}}, {{provider}}, {{stamp}},
                    {{used}}, {{reserved}}, {{quarantined}}, {{count}}, {{created}}, {{updated}})
                SELECT {0}, {1}, {2}, {3}, 0, 0, 0, 0, {4}, {4}
                WHERE NOT EXISTS (SELECT 1 FROM {{table}} WHERE {{tenant}} = {1} AND {{provider}} = {2})
                """, new object[] { Guid.CreateVersion7(), scope.TenantId, scope.Provider, Guid.CreateVersion7(), now }, ct);
            var fence = await global.Database.SqlQueryRaw<CounterFence>($$"""
                SELECT {{id}} AS id, {{stamp}} AS concurrency_stamp FROM {{table}}
                WHERE {{tenant}} = {0} AND {{provider}} = {1}
                """, scope.TenantId, scope.Provider).SingleAsync(ct);
            int changed = await global.Database.ExecuteSqlRawAsync($$"""
                UPDATE {{table}} SET {{stamp}} = {0} WHERE {{id}} = {1} AND {{stamp}} = {2}
                """, new object[] { Guid.CreateVersion7(), fence.Id, fence.ConcurrencyStamp }, ct);
            if (changed != 1)
                throw new ConcurrencyConflictException(ConcurrencyConflictException.ConcurrentUpdate,
                    "Storage usage authority changed concurrently.");
            var totals = await StorageScopeTotalsQuery.ReadAsync(global, scope.TenantId, scope.Provider, ct);
            await global.Database.ExecuteSqlRawAsync($$"""
                UPDATE {{table}} SET {{used}} = {0}, {{reserved}} = {1}, {{quarantined}} = {2},
                    {{count}} = {3}, {{last}} = {4}, {{updated}} = {4} WHERE {{id}} = {5}
                """, new object[] { totals.Used, totals.Reserved, totals.Quarantined, totals.Count, now, fence.Id }, ct);
        }
        return await ReadAsync(global, ct);
    }

    private sealed record CounterFence(Guid Id, Guid ConcurrencyStamp);
}
