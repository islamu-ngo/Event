using System.Data;
using System.Runtime.ExceptionServices;
using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class ExternalApiKeyAuthenticationQuery
{
    internal static async Task<ExternalApiKey?> ReadAsync(
        ExploreDbContext context, string keyId, CancellationToken cancellationToken,
        PostgresNativeQueueEnrollment? enrollment = null)
    {
        await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(context, enrollment, cancellationToken);
        var connection = context.Database.IsNpgsql() ? RelationalConnectionIdentity.Capture(context) : null;
        var contextId = context.ContextId;
        int? backend = connection?.BackendProcessId;
        var transaction = connection is null ? null : context.Database.CurrentTransaction?.GetDbTransaction();
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change) { if (change.CurrentState != ConnectionState.Open) interrupted = true; }
        void RequireOriginal()
        {
            if (connection is not null && (interrupted || context.ContextId != contextId || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())))
                throw new UnauthorizedAccessException("authentication_original_connector_changed");
        }
        if (connection is not null) connection.StateChange += Changed;
        try
        {
            var result = await context.ExternalApiKeys.IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
                .AsNoTracking().SingleOrDefaultAsync(key => key.KeyId == keyId, cancellationToken);
            RequireOriginal();
            await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
            return result is not null && string.Equals(result.KeyId, keyId, StringComparison.Ordinal) ? result : null;
        }
        finally { if (connection is not null) connection.StateChange -= Changed; }
    }

    internal static async Task<bool> TouchUsageAsync(
        ExploreDbContext context,
        Guid id,
        DateTime usedAtUtc,
        string? lastUsedIp,
        DateTime threshold,
        CancellationToken cancellationToken,
        PostgresNativeQueueEnrollment? enrollment = null)
    {
        await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(context, enrollment, cancellationToken);
        var connection = context.Database.IsNpgsql() ? RelationalConnectionIdentity.Capture(context) : null;
        var contextId = context.ContextId;
        int? backend = connection?.BackendProcessId;
        var transaction = connection is null ? null : context.Database.CurrentTransaction?.GetDbTransaction();
        bool interrupted = false;
        void Changed(object? sender, StateChangeEventArgs change) { if (change.CurrentState != ConnectionState.Open) interrupted = true; }
        void RequireOriginal()
        {
            if (connection is not null && (interrupted || context.ContextId != contextId || !connection.Matches(context)
                || connection.State != ConnectionState.Open || connection.BackendProcessId != backend
                || !ReferenceEquals(transaction, context.Database.CurrentTransaction?.GetDbTransaction())))
                throw new UnauthorizedAccessException("authentication_original_connector_changed");
        }
        if (connection is not null) connection.StateChange += Changed;
        try
        {
            if (context.Database.CurrentTransaction is not null)
                return await TouchInCurrentTransactionAsync();

            var prepared = await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                // The invocation retains its original connector across retries. A
                // replaced connector cannot become a new admitted usage owner.
                RequireOriginal();
                var owned = await context.Database.BeginTransactionAsync(cancellationToken);
                try
                {
                    transaction = owned.GetDbTransaction();
                    bool touched = await TouchInCurrentTransactionAsync();
                    return (Transaction: owned, Touched: touched);
                }
                catch (Exception failure)
                {
                    await DisposeOwnedTransactionAsync(owned, failure);
                    throw;
                }
            });

            // A commit acknowledgement or disposal failure must not re-enter the
            // retry delegate and turn an already durable update into a false CAS.
            Exception? commitFailure = null;
            try
            {
                RequireOriginal();
                await prepared.Transaction.CommitAsync(cancellationToken);
            }
            catch (Exception failure) { commitFailure = failure; }
            await DisposeOwnedTransactionAsync(prepared.Transaction, commitFailure);
            return prepared.Touched;
        }
        finally { if (connection is not null) connection.StateChange -= Changed; }

        async Task<bool> TouchInCurrentTransactionAsync()
        {
            RequireOriginal();
            bool touched = await context.ExternalApiKeys.IgnoreTenantFilter(TenantFilterBypassReasons.ExternalApiKeyAuthentication)
                .Where(key => key.Id == id && (key.LastUsedAt == null || key.LastUsedAt < threshold))
                .ExecuteUpdateAsync(setters => setters.SetProperty(key => key.LastUsedAt, usedAtUtc)
                    .SetProperty(key => key.LastUsedIp, lastUsedIp), cancellationToken) > 0;
            RequireOriginal();
            await PostgresExternalApiKeyAuthenticationContract.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
            return touched;
        }

        async Task DisposeOwnedTransactionAsync(IDbContextTransaction owned, Exception? primaryFailure)
        {
            Exception? cleanupFailure = null;
            try { await owned.DisposeAsync(); }
            catch (Exception failure) { cleanupFailure = failure; }
            finally { transaction = null; }

            if (primaryFailure is not null && cleanupFailure is not null)
                throw new AggregateException(primaryFailure, cleanupFailure);
            if (primaryFailure is not null)
                ExceptionDispatchInfo.Capture(primaryFailure).Throw();
            if (cleanupFailure is not null)
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }
}
