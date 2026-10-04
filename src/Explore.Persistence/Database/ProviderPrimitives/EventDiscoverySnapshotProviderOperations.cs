using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EventDiscoverySnapshotProviderOperations
{
    internal static async Task<DateTime> BoundExpiryAsync(
        ExploreDbContext context, DateTime nowUtc, CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName != RelationalNamedLock.PostgreSqlProvider)
            return nowUtc;

        return await context.Database.SqlQueryRaw<DateTime>(
            """SELECT LEAST({0}, statement_timestamp()) AS "Value" """, nowUtc)
            .SingleAsync(cancellationToken);
    }

    internal static Task BootstrapReservationAsync(
        ExploreDbContext dbContext, Guid tenantId, CancellationToken cancellationToken)
    {
        var entity = dbContext.Model.FindEntityType(typeof(EventDiscoverySnapshotReservation))!;
        string tableName = entity.GetTableName()!;
        var mapping = StoreObjectIdentifier.Table(tableName, entity.GetSchema());
        var sql = dbContext.GetService<ISqlGenerationHelper>();
        string table = sql.DelimitIdentifier(tableName, entity.GetSchema());
        string key = sql.DelimitIdentifier(entity.FindProperty(nameof(EventDiscoverySnapshotReservation.TenantId))!
            .GetColumnName(mapping)!);
        string bootstrap = dbContext.Database.ProviderName switch
        {
            RelationalNamedLock.PostgreSqlProvider or RelationalNamedLock.SqliteProvider =>
                $"INSERT INTO {table} ({key}) VALUES ({{0}}) ON CONFLICT ({key}) DO NOTHING",
            RelationalNamedLock.MySqlProvider =>
                $"INSERT INTO {table} ({key}) VALUES ({{0}}) ON DUPLICATE KEY UPDATE {key} = {key}",
            RelationalNamedLock.SqlServerProvider =>
                $"INSERT INTO {table} ({key}) SELECT {{0}} WHERE NOT EXISTS " +
                $"(SELECT 1 FROM {table} WITH (UPDLOCK, HOLDLOCK) WHERE {key} = {{0}})",
            _ => throw new InvalidOperationException("Snapshot reservation requires a supported relational provider.")
        };
        return dbContext.Database.ExecuteSqlRawAsync(bootstrap, [tenantId], cancellationToken);
    }

    internal static async Task<IReadOnlyList<EventDiscoverySnapshotReservation>> GetExpiredOwnersAsync(
        ExploreDbContext context, Guid? afterTenantId, DateTime nowUtc, int take, CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName == RelationalNamedLock.PostgreSqlProvider)
        {
            // Only bounded ownership keys cross RLS through the dedicated function.
            // The runtime role cannot assume its owner or read foreign snapshot data.
            // FunctionSql emits only the provider-delimited model schema/name.
            // Every execution value remains a separately typed parameter.
            string query = $"SELECT * FROM {PostgresDiscoverySnapshotMaintenanceContract.FunctionSql(context)}" +
                "(@after_tenant, @now_utc, @take)";
            return await context.Set<EventDiscoverySnapshotReservation>().FromSqlRaw(
                    query,
                    new NpgsqlParameter("after_tenant", NpgsqlDbType.Uuid)
                    {
                        Value = (object?)afterTenantId ?? DBNull.Value
                    },
                    new NpgsqlParameter("now_utc", nowUtc),
                    new NpgsqlParameter("take", take))
                .IgnoreAllFilters(TenantFilterBypassReasons.DiscoverySnapshotRetention)
                .AsNoTracking().ToArrayAsync(cancellationToken);
        }
        var expired = context.Set<EventDiscoverySnapshot>()
            .IgnoreAllFilters(TenantFilterBypassReasons.DiscoverySnapshotRetention)
            .Where(snapshot => snapshot.ExpiresAtUtc <= nowUtc);
        var owners = context.Set<EventDiscoverySnapshotReservation>()
            .IgnoreAllFilters(TenantFilterBypassReasons.DiscoverySnapshotRetention)
            .AsNoTracking().Where(owner => expired.Any(snapshot => snapshot.TenantId == owner.TenantId));
        if (afterTenantId is { } cursor)
            owners = owners.Where(owner => owner.TenantId.CompareTo(cursor) > 0);
        return await owners.OrderBy(owner => owner.TenantId).Take(take).ToArrayAsync(cancellationToken);
    }
}
