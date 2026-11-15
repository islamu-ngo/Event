using Explore.Application.Contracts.Waitlist;
using Explore.Domain;
using Explore.Domain.Enums;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Schema;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Services;

public sealed class FairReturnOrchestrationHealthReader(
    IGlobalExploreDbContextFactory factory,
    PostgresNativeQueueEnrollment? enrollment = null) : IFairReturnOrchestrationHealthReader
{
    public async Task<FairReturnOrchestrationHealth> ReadAsync(
        DateTime observedAt, CancellationToken cancellationToken)
    {
        FairReturnSupplyPolicy.RequireUtc(observedAt, nameof(observedAt));
        await using var context = await factory.CreateDbContextAsync(cancellationToken);
        _ = RelationalProviderClassifier.Classify(context.Database);
        context.RequireBoundTenantLease();
        if (context.TenantFilterTenantId is not null || context.IsTenantFilterBypassed
            || context.IsConfigurationInstanceImportOwner || context.Database.CurrentTransaction is not null)
            throw new UnauthorizedAccessException("fair_return_health_original_global_required");
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
                throw new UnauthorizedAccessException("fair_return_health_original_owner_changed");
        }
        connection.StateChange += Changed;
        try
        {
            RequireOriginal();
            await PostgresEffectAggregateHealthReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
            var snapshot = await context.FairReturnOrchestrationEffects
                .IgnoreTenantFilter(TenantFilterBypassReasons.OperationalEffectAggregateHealth)
                .AsNoTracking()
                .GroupBy(value => 1)
                .Select(group => new FairReturnOrchestrationHealth(
                    group.Count(value => value.StatusId == (int)FairReturnOrchestrationEffectStatus.Pending),
                    group.Count(value => value.StatusId == (int)FairReturnOrchestrationEffectStatus.Processing),
                    group.Count(value => value.LastFailureCode == "REPLACEMENT_PAYMENT_UNKNOWN"),
                    group.Count(value => value.StatusId == (int)FairReturnOrchestrationEffectStatus.DeadLettered),
                    group.Min(value => value.StatusId == (int)FairReturnOrchestrationEffectStatus.Pending
                        ? (DateTime?)value.CreatedAt : null)))
                .SingleOrDefaultAsync(cancellationToken);
            RequireOriginal();
            await PostgresEffectAggregateHealthReadPermissions.RequireReadyAsync(context, enrollment, cancellationToken);
            RequireOriginal();
            // A missing constant group means an empty queue, only after authority has been admitted.
            return snapshot ?? new FairReturnOrchestrationHealth(0, 0, 0, 0, null);
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
