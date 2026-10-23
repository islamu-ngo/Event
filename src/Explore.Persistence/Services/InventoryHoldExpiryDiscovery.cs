using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class InventoryHoldExpiryDiscovery(
    IGlobalExploreDbContextFactory globalFactory,
    PostgresNativeQueueEnrollment? enrollment)
    : IInventoryHoldExpiryDiscovery
{
    public const int MaximumBatchSize = 100;

    public async Task<IReadOnlyList<InventoryHoldExpiryTarget>> GetTargetsAsync(
        DateTime utcNow, int batchSize, CancellationToken cancellationToken)
    {
        if (utcNow.Kind != DateTimeKind.Utc || batchSize is < 1 or > MaximumBatchSize)
            throw new ArgumentOutOfRangeException(nameof(batchSize));
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        _ = RelationalProviderClassifier.Classify(context.Database);
        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            context.RequireBoundTenantLease();
            if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null)
                throw new InvalidOperationException("hold_expiry_unbound_global_discovery_required");
            await context.Database.OpenConnectionAsync(cancellationToken);
            var connection = RelationalConnectionIdentity.Capture(context);
            var contextId = context.ContextId;
            int? backend = connection.BackendProcessId;
            bool interrupted = false;
            void Changed(object? sender, System.Data.StateChangeEventArgs change)
            {
                if (change.CurrentState != System.Data.ConnectionState.Open) interrupted = true;
            }
            void RequireOriginal()
            {
                cancellationToken.ThrowIfCancellationRequested();
                context.RequireBoundTenantLease();
                if (context.ContextId != contextId || context.TenantFilterTenantId is not null
                    || context.IsTenantFilterBypassed || context.IsConfigurationInstanceImportOwner
                    || context.Database.CurrentTransaction is not null || interrupted || !connection.Matches(context)
                    || connection.State != System.Data.ConnectionState.Open || connection.BackendProcessId != backend)
                    throw new UnauthorizedAccessException("hold_expiry_discovery_original_owner_changed");
            }
            connection.StateChange += Changed;
            try
            {
                RequireOriginal();
                await PostgresInventoryHoldExpiryReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
                RequireOriginal();
                var targets = await InventoryHoldExpiryQuery.ReadAsync(context, utcNow, batchSize, cancellationToken);
                RequireOriginal();
                return targets;
            }
            finally
            {
                connection.StateChange -= Changed;
                await context.Database.CloseConnectionAsync();
            }
        });
    }
}
