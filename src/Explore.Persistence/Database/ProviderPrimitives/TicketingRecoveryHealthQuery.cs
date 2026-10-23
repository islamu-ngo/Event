using Explore.Application.Contracts.Recovery;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

internal static class TicketingRecoveryHealthQuery
{
    internal static async Task<TicketingRecoveryAggregateHealth> ReadAsync(
        ExploreDbContext context, CancellationToken cancellationToken)
    {
        if (context.Database.IsNpgsql())
            return await PostgresTicketingRecoveryHealthContract.ObserveAsync(context, cancellationToken);

        // Only the separately acquired Global lease may aggregate across tenants.
        const string reason = "Fixed recovery fleet readiness counts from separate Global observation authority.";
        var checkpoints = context.TicketingRecoveryCheckpoints.IgnoreTenantFilter(reason).AsNoTracking();
        var reissues = context.TicketingRecoveryReissueIntents.IgnoreTenantFilter(reason).AsNoTracking();
        var effects = context.FairReturnOrchestrationEffects.IgnoreTenantFilter(reason).AsNoTracking();
        int recoveryOnly = await checkpoints.CountAsync(value =>
            value.Status == TicketingRecoveryStatus.RecoveryOnly, cancellationToken);
        int failed = await checkpoints.CountAsync(value =>
            value.Status == TicketingRecoveryStatus.Failed, cancellationToken);
        int pendingReissues = await reissues.CountAsync(value =>
            value.Status == TicketingRecoveryReissueStatus.Pending, cancellationToken);
        int ambiguous = await effects.CountAsync(value =>
            value.StatusId == (int)FairReturnOrchestrationEffectStatus.Unknown, cancellationToken);
        int deadLettered = await effects.CountAsync(value =>
            value.StatusId == (int)FairReturnOrchestrationEffectStatus.DeadLettered, cancellationToken);
        int poison = await effects.CountAsync(value =>
            value.LastFailureCode != null && value.LastFailureCode.StartsWith("POISON_"), cancellationToken);
        DateTime? oldestDue = await effects
            .Where(value => value.StatusId == (int)FairReturnOrchestrationEffectStatus.Pending ||
                value.StatusId == (int)FairReturnOrchestrationEffectStatus.Unknown)
            .MinAsync(value => (DateTime?)value.NextAttemptAt, cancellationToken);
        return new(recoveryOnly, failed, pendingReissues, ambiguous, deadLettered, poison, oldestDue);
    }
}
