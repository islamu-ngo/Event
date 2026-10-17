using System.Data;
using System.Data.Common;
using Explore.Domain;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class EventDiscoverySourceProviderOperations
{
    internal const string TenantEnrollmentResource = "discovery-tenant-enrollment";

    internal static async Task FenceTenantEnrollmentAsync(
        ExploreDbContext context, CancellationToken cancellationToken)
    {
        RequireCurrentGlobalRead(context);
        if (context.Database.ProviderName == RelationalNamedLock.SqlServerProvider
            && context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel == IsolationLevel.Snapshot)
            throw new InvalidOperationException("Discovery tenant enrollment requires a current-read transaction.");

        // Both global fanout and INSERT take this transaction-owned gate before
        // inspecting membership, including the first tenant. SQLite uses its native
        // writer fence below, not a process semaphore acquired behind a writer lock.
        if (context.Database.ProviderName != RelationalNamedLock.SqliteProvider)
            await RelationalNamedLock.AcquireTransactionAsync(context, TenantEnrollmentResource, cancellationToken);
        if (context.Database.IsNpgsql())
        {
            // A no-op minimum-row write rejects a stale nonempty snapshot.
            // With no row, retain the gate and probe through the same authority.
            string query = $"SELECT {PostgresDiscoveryDisclosureContract.FunctionSql(context, "enrollment")}() AS \"Value\"";
            Guid anchor = await context.Database.SqlQueryRaw<Guid>(query).SingleAsync(cancellationToken);
            if (anchor == Guid.Empty
                && context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted)
                await RequireEmptyCurrentCatalogAsync(context, cancellationToken);
        }
        else
        {
            Guid anchor = await context.Tenants
                .IgnoreAllFilters(TenantFilterBypassReasons.DiscoveryDisclosureMutation)
                .OrderBy(tenant => tenant.Id).Select(tenant => tenant.Id).FirstOrDefaultAsync(cancellationToken);
            await RelationalEntityRowFence.AcquireGlobalAsync<Tenant>(context, anchor, cancellationToken);
        }
    }

    private static async Task RequireEmptyCurrentCatalogAsync(
        ExploreDbContext context, CancellationToken cancellationToken)
    {
        // There is no row to update in an empty snapshot. While enrollment is
        // excluded by the native gate, distinguish actual bootstrap from a snapshot
        // predating the first committed tenant using the same configured authority.
        var options = (DbContextOptions<ExploreDbContext>)context.GetService<IDbContextOptions>();
        var relational = options.Extensions.OfType<RelationalOptionsExtension>().Single();
        await using var connection = DbProviderFactories.GetFactory(context.Database.GetDbConnection())?.CreateConnection()
            ?? throw new InvalidOperationException("Discovery enrollment requires a provider connection factory.");
        connection.ConnectionString = relational.ConnectionString ?? context.Database.GetDbConnection().ConnectionString;
        var builder = new DbContextOptionsBuilder<ExploreDbContext>(options);
        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(relational.WithConnection(connection, false));
        await using var current = new ExploreDbContext(builder.Options) { TenantContext = context.TenantContext };
        await current.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await current.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted, cancellationToken);
            string query = $"SELECT {PostgresDiscoveryDisclosureContract.FunctionSql(current, "catalog")}() AS \"Value\"";
            if (await current.Database.SqlQueryRaw<bool>(query).SingleAsync(cancellationToken))
                throw new DbUpdateConcurrencyException("Discovery tenant enrollment changed after the empty snapshot.");
            await transaction.CommitAsync(cancellationToken);
        });
    }

    internal static void RequireCurrentClassificationRead(ExploreDbContext context)
    {
        if (context.Database.ProviderName == RelationalNamedLock.MySqlProvider
            && context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel
                is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable))
            throw new InvalidOperationException("Discovery source classification requires a current-read transaction.");
    }

    internal static Task<PropertyValues?> ReadClassificationPlanAsync(
        ExploreDbContext context, EntityEntry entry, CancellationToken cancellationToken) =>
        ReadNonRetainingPlanAsync(context,
            planning => planning == context
                ? entry.GetDatabaseValuesAsync(cancellationToken)
                : planning.Entry(entry.Entity).GetDatabaseValuesAsync(cancellationToken), cancellationToken);

    internal static async Task<T> ReadNonRetainingPlanAsync<T>(
        ExploreDbContext context, Func<ExploreDbContext, Task<T>> read, CancellationToken cancellationToken)
    {
        if (context.Database.ProviderName is not (RelationalNamedLock.SqlServerProvider or RelationalNamedLock.MySqlProvider))
            return await read(context);

        // Serializable reads on these engines retain source-row shared locks.
        // Discover tentative keys without retaining one before Actor/Tenant anchors.
        // Dirty planning is not authority: callers must validate every ownership
        // key with a held reread before classification or any source write proceeds.
        // A separate read also must not block on this transaction's earlier saves.
        var options = (DbContextOptions<ExploreDbContext>)context.GetService<IDbContextOptions>();
        var relational = options.Extensions.OfType<RelationalOptionsExtension>().Single();
        await using var connection = DbProviderFactories.GetFactory(context.Database.GetDbConnection())?.CreateConnection()
            ?? throw new InvalidOperationException("Discovery planning requires a provider connection factory.");
        connection.ConnectionString = relational.ConnectionString ?? context.Database.GetDbConnection().ConnectionString;
        var builder = new DbContextOptionsBuilder<ExploreDbContext>(options);
        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(relational.WithConnection(connection, false));
        await using var planning = new ExploreDbContext(builder.Options) { TenantContext = context.TenantContext };
        if (context.IsTenantFilterBypassed)
            planning.EnableTenantFilterBypass(TenantFilterBypassReasons.DiscoveryDisclosureMutation);
        return await planning.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await planning.Database.BeginTransactionAsync(
                IsolationLevel.ReadUncommitted, cancellationToken);
            T values = await read(planning);
            await transaction.CommitAsync(cancellationToken);
            return values;
        });
    }

    internal static void RequireCurrentGlobalRead(ExploreDbContext context)
    {
        if (context.Database.ProviderName == RelationalNamedLock.MySqlProvider
            && context.Database.CurrentTransaction?.GetDbTransaction().IsolationLevel
                is not (IsolationLevel.ReadCommitted or IsolationLevel.Serializable))
            throw new InvalidOperationException("Discovery global fanout requires a current-read transaction.");
    }
}
