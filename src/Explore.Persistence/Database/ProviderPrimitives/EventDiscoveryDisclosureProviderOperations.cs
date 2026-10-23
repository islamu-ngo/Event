using Explore.Application.Exceptions;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal sealed class EventDiscoveryDisclosureProviderOperations(ExploreDbContext context)
{
    private readonly (string Table, string Id, string Tenant, string Identity, string Disclosure) _mapping = Mapping(context);

    internal async Task<EventDiscoveryRevision> AcquireRevisionAsync(
        Guid tenantId, CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql())
            return await PostgresDiscoveryTraversalContract.AcquireRevisionAsync(context, tenantId, cancellationToken);
        var mapping = _mapping;
        // A native write also detects a stale PostgreSQL snapshot. An advisory lock
        // followed by an ordinary snapshot SELECT cannot establish current authority.
        string fenceSql =
            $"UPDATE {mapping.Table} SET {mapping.Disclosure} = {mapping.Disclosure} WHERE {mapping.Tenant} = {{0}}";
        await context.Database.ExecuteSqlRawAsync(fenceSql, [tenantId], cancellationToken);

        string query = context.Database.ProviderName switch
        {
            RelationalNamedLock.PostgreSqlProvider or RelationalNamedLock.MySqlProvider =>
                $"SELECT * FROM {mapping.Table} WHERE {mapping.Tenant} = {{0}} FOR UPDATE",
            RelationalNamedLock.SqlServerProvider =>
                $"SELECT * FROM {mapping.Table} WITH (UPDLOCK, HOLDLOCK) WHERE {mapping.Tenant} = {{0}}",
            RelationalNamedLock.SqliteProvider =>
                $"SELECT * FROM {mapping.Table} WHERE {mapping.Tenant} = {{0}}",
            _ => throw new InvalidOperationException("Unsupported discovery authority provider.")
        };
        // Materialize the locking read itself. A later plain MySQL snapshot read
        // could otherwise return a revision older than the row just locked.
        var revisions = await context.Set<EventDiscoveryRevision>().FromSqlRaw(query, tenantId)
            .IgnoreTenantFilter(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
            .AsNoTracking().ToListAsync(cancellationToken);
        // MySQL may report changed rows rather than matched rows for a no-op.
        // Existence comes from the locking read, never its affected-row count.
        return revisions.SingleOrDefault() ?? throw new EventDiscoveryUnavailableException();
    }

    internal Task<int> UpdateRevisionAsync(
        Guid tenantId, EventDiscoveryRevision revision, CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql())
            return PostgresDiscoveryTraversalContract.UpdateRevisionAsync(context, tenantId, revision, cancellationToken);
        var mapping = _mapping;
        string updateSql =
            $"UPDATE {mapping.Table} SET {mapping.Disclosure} = {{0}}, {mapping.Identity} = {{1}} WHERE {mapping.Tenant} = {{2}}";
        return context.Database.ExecuteSqlRawAsync(updateSql,
            [revision.DisclosureEpoch, revision.IdentityEpoch, tenantId], cancellationToken);
    }

    internal static Task<T> InTenantAsync<T>(
        ExploreDbContext context, Guid tenantId, Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Database.IsNpgsql() && (tenantId == Guid.Empty || context.TenantFilterTenantId != tenantId))
            throw new InvalidOperationException("Discovery tenant operation requires the exact immutable lease.");
        return operation();
    }

    private static (string Table, string Id, string Tenant, string Identity, string Disclosure) Mapping(
        ExploreDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(EventDiscoveryRevision))!;
        var table = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        string Column(string name) => sql.DelimitIdentifier(entity.FindProperty(name)!.GetColumnName(table)!);
        return (sql.DelimitIdentifier(table.Name, table.Schema), Column(nameof(EventDiscoveryRevision.Id)),
            Column(nameof(EventDiscoveryRevision.TenantId)), Column(nameof(EventDiscoveryRevision.IdentityEpoch)),
            Column(nameof(EventDiscoveryRevision.DisclosureEpoch)));
    }
}
