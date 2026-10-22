using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class IntegrationSyncQueueDiscovery(
    IGlobalExploreDbContextFactory globalFactory,
    PostgresNativeQueueEnrollment? enrollment)
    : IIntegrationSyncQueueDiscovery
{
    public async Task<IReadOnlyList<IntegrationSyncQueueCandidate>> GetPendingAsync(
        int batchSize, DateTime now, DateTime staleProcessingStartedBefore, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(batchSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(batchSize, IIntegrationSyncQueueDiscovery.MaximumBatchSize);
        if (now.Kind != DateTimeKind.Utc || staleProcessingStartedBefore.Kind != DateTimeKind.Utc ||
            staleProcessingStartedBefore > now)
            throw new ArgumentException("Integration sync discovery requires ordered UTC bounds.");
        if (now == DateTime.MinValue || now == DateTime.MaxValue
            || staleProcessingStartedBefore == DateTime.MinValue || staleProcessingStartedBefore == DateTime.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(now), "Integration sync discovery requires finite bounds.");

        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
        RelationalConnectionIdentity? connection = null;
        int? backend = null;
        bool monitoring = false;
        bool interrupted = false;
        bool reopened = false;
        void Changed(object? sender, System.Data.StateChangeEventArgs change)
        {
            if (change.CurrentState == System.Data.ConnectionState.Open)
                reopened |= interrupted;
            else
                interrupted = true;
        }
        void RequireOriginal()
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.RequireBoundTenantLease();
            if (context.ContextId != contextId || !ReferenceEquals(context.TenantContext, tenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
                || interrupted || connection is null || !connection.Matches(context)
                || connection.State != System.Data.ConnectionState.Open || connection.BackendProcessId != backend)
                throw new UnauthorizedAccessException("integration_sync_discovery_owner_changed");
        }
        try
        {
            _ = RelationalProviderClassifier.Classify(context.Database);
            context.RequireBoundTenantLease();
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null)
                throw new UnauthorizedAccessException("integration_sync_original_global_required");
            connection = RelationalConnectionIdentity.Capture(context);
            await context.Database.OpenConnectionAsync(cancellationToken);
            if (context.ContextId != contextId || !ReferenceEquals(context.TenantContext, tenantContext)
                || !connection.Matches(context) || connection.State != System.Data.ConnectionState.Open)
                throw new UnauthorizedAccessException("integration_sync_discovery_owner_changed");
            backend = connection.BackendProcessId;
            connection.StateChange += Changed;
            monitoring = true;
            return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                RequireOriginal();
                await PostgresIntegrationSyncReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
                RequireOriginal();
                DateTime boundedNow = await EventDiscoverySnapshotProviderOperations.BoundExpiryAsync(context, now, cancellationToken);
                RequireOriginal();
                DateTime boundedStale = staleProcessingStartedBefore < boundedNow ? staleProcessingStartedBefore : boundedNow;
                var rows = await IntegrationSyncDiscoveryQuery.ReadAsync(
                    context, batchSize, boundedNow, boundedStale, cancellationToken);
                RequireOriginal();
                return rows;
            });
        }
        finally
        {
            try
            {
                bool originalOwner = context.ContextId == contextId && connection is not null && connection.Matches(context);
                bool originalConnector = monitoring && connection is not null && !reopened
                    && (connection.State == System.Data.ConnectionState.Closed
                        || connection.State == System.Data.ConnectionState.Open && connection.BackendProcessId == backend);
                if (originalOwner && originalConnector)
                    await context.Database.CloseConnectionAsync();
            }
            finally
            {
                if (monitoring) connection!.StateChange -= Changed;
            }
        }
    }
}
