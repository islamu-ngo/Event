using System.Data;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Explore.Persistence.Database.ProviderPrimitives;

/// <summary>Materializes fixed global catalogue rows through ordinary EF scalar mappings and filters.</summary>
internal static class GlobalLookupCatalogueProviderOperations
{
    internal static async Task<List<T>> ReadAsync<T>(ExploreDbContext context,
        PostgresNativeQueueEnrollment enrollment, CancellationToken cancellationToken) where T : class
    {
        if (!context.Database.IsNpgsql() || System.Transactions.Transaction.Current is not null
            || context.BindTenantLease() is not null || context.IsTenantFilterBypassed)
            throw new UnauthorizedAccessException("global_lookup_original_global_owner_required");
        var lookup = PostgresGlobalLookupCatalogueContract.Resolve(context, typeof(T));
        var connection = context.Database.GetDbConnection();
        if (context.Database.CurrentTransaction is null)
        {
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                if (!ReferenceEquals(connection, context.Database.GetDbConnection()))
                    throw new UnauthorizedAccessException("global_lookup_connection_substitution");
                await using var owned = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
                var result = await ReadAsync<T>(context, enrollment, cancellationToken);
                await owned.CommitAsync(cancellationToken);
                return result;
            });
        }

        var transaction = context.Database.CurrentTransaction.GetDbTransaction();
        RequireOwner();
        await PostgresGlobalLookupCatalogueContract.RequireReadyAsync(context, enrollment, cancellationToken);
        RequireOwner();
        // EF still owns entity materialization and every ordinary named query filter.
        // This composable projection reads the function result, never the source table.
        var items = await context.Set<T>().FromSqlRaw(
            PostgresGlobalLookupCatalogueContract.EntitySql(context, lookup),
            new NpgsqlParameter("lookup_type", NpgsqlDbType.Integer) { Value = lookup.Selector })
            .AsNoTracking().ToListAsync(cancellationToken);
        RequireOwner();
        return items;

        void RequireOwner()
        {
            context.ValidateTenantLease();
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || !ReferenceEquals(connection, context.Database.GetDbConnection())
                || connection.State != ConnectionState.Open
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())
                || !ReferenceEquals(connection, transaction.Connection)
                || transaction.IsolationLevel != IsolationLevel.Serializable)
                throw new UnauthorizedAccessException("global_lookup_connection_or_transaction_substitution");
        }
    }
}
