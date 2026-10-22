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
