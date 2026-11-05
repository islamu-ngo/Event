using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class IncomingWebhookEffectHealthReader(
    IGlobalExploreDbContextFactory factory,
    PostgresNativeQueueEnrollment? enrollment = null) : IIncomingWebhookEffectHealthReader
{
    public async Task<IncomingWebhookEffectHealthSnapshot> ReadAsync(
        DateTime observedAt, CancellationToken cancellationToken)
    {
        if (observedAt.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must use UTC kind.", nameof(observedAt));
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        _ = RelationalProviderClassifier.Classify(context.Database);
        context.RequireBoundTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("effect_health_original_global_required");
        var contextId = context.ContextId;
        var tenantContext = context.TenantContext;
        var connection = RelationalConnectionIdentity.Capture(context);
        await context.Database.OpenConnectionAsync(cancellationToken);
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
            if (context.ContextId != contextId || !ReferenceEquals(context.TenantContext, tenantContext)
                || context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
                || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null
                || interrupted || !connection.Matches(context)
                || connection.State != System.Data.ConnectionState.Open || connection.BackendProcessId != backend)
                throw new UnauthorizedAccessException("effect_health_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            await PostgresEffectAggregateHealthReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
            var snapshot = await context.IncomingWebhookEffectOutboxes
                .IgnoreTenantFilter(TenantFilterBypassReasons.OperationalEffectAggregateHealth)
                .AsNoTracking()
                .GroupBy(value => 1)
                .Select(group => new IncomingWebhookEffectHealthSnapshot(
                    group.Count(value =>
                        (value.Status == OutboxMessageStatus.Pending || value.Status == OutboxMessageStatus.Failed)
                        && (value.NextAttemptAt == null || value.NextAttemptAt <= observedAt)),
                    group.Count(value => value.Status == OutboxMessageStatus.Processing
                        && value.ProcessingLeaseExpiresAt != null
                        && value.ProcessingLeaseExpiresAt <= observedAt)))
                .SingleOrDefaultAsync(cancellationToken);
            RequireOriginal();
            await PostgresEffectAggregateHealthReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
            // A missing constant group means an empty queue, only after authority has been admitted.
            return snapshot ?? new IncomingWebhookEffectHealthSnapshot(0, 0);
        }
        finally
        {
            connection.StateChange -= Changed;
            if (context.ContextId == contextId && !interrupted && connection.Matches(context)
                && connection.State == System.Data.ConnectionState.Open && connection.BackendProcessId == backend)
                await context.Database.CloseConnectionAsync();
        }
    }
}
