using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;
using Explore.Persistence.QueryFilters;
using Explore.Persistence.Security;
using Microsoft.EntityFrameworkCore;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class RegistrationProviderSubscriptionClaimQueue(
    IGlobalExploreDbContextFactory globalFactory) : IRegistrationProviderSubscriptionClaimQueue
{
    public async Task<IReadOnlyList<RegistrationProviderSubscriptionClaim>> ClaimAsync(
        RegistrationProviderSubscriptionOperation operation, int batchSize, DateTime observedAt,
        TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(operation) || batchSize is < 1 or > 1000 || observedAt.Kind != DateTimeKind.Utc ||
            leaseDuration <= TimeSpan.Zero || leaseDuration > TimeSpan.FromMinutes(2))
            throw new ArgumentException("Invalid subscription claim bounds.");
        await using var context = await globalFactory.CreateDbContextAsync(cancellationToken);
        if (context.BindTenantLease() is not null)
            throw new InvalidOperationException("Subscription admission requires its unbound Global pool.");
        if (context.Database.IsNpgsql())
        {
            string command = $$"""
                SELECT state_id, tenant_id, registration_provider_binding_id, lease_token,
                    processing_generation, lease_expires_at
                FROM {{PostgresRegistrationProviderSubscriptionClaimContract.FunctionSql(context)}}({0}, {1}, {2}, {3})
                """;
            return await context.Database.SqlQueryRaw<RegistrationProviderSubscriptionClaim>(
                command, (int)operation, batchSize, observedAt, leaseDuration).ToListAsync(cancellationToken);
        }

        return await context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await using var claimLock = await RelationalNamedLock.AcquireTransactionAsync(
                context, "registration-provider-subscription-state-claim", cancellationToken);
            var rows = context.RegistrationProviderSubscriptionStates
                .IgnoreTenantFilter(TenantFilterBypassReasons.RegistrationProviderSubscriptionStateWorkerCrossTenantQueue)
                .Where(state => state.LeaseExpiresAt == null || state.LeaseExpiresAt <= observedAt);
            DateTime renewBefore = observedAt.AddDays(2);
            var due = operation == RegistrationProviderSubscriptionOperation.Renewal
                ? rows.Where(state => state.WatchExpiresAt <= renewBefore &&
                    state.FailureCategory != RegistrationProviderSubscriptionState.RenewalInDoubtFailureCategory &&
                    state.FailureCategory != RegistrationProviderSubscriptionState.RenewalRejectedFailureCategory &&
                    (state.NextRenewalAttemptAt == null || state.NextRenewalAttemptAt <= observedAt))
                    .OrderBy(state => state.WatchExpiresAt).ThenBy(state => state.Id)
                : rows.Where(state => state.PendingNotificationAt != null &&
                        (state.NextSweepAttemptAt == null || state.NextSweepAttemptAt <= observedAt) ||
                    state.PendingNotificationAt == null && state.NextSweepAttemptAt != null && state.NextSweepAttemptAt <= observedAt)
                    .OrderBy(state => state.PendingNotificationAt ?? state.NextSweepAttemptAt).ThenBy(state => state.Id);
            // Never materialize subscription/provider payload under Global, including portable providers.
            var candidates = await due.Select(state => new
            {
                state.Id,
                state.TenantId,
                state.RegistrationProviderBindingId,
                state.ProcessingGeneration
            }).Take(batchSize).ToListAsync(cancellationToken);
            List<RegistrationProviderSubscriptionClaim> claims = [];
            foreach (var candidate in candidates)
            {
                Guid token = Guid.CreateVersion7();
                DateTime expiry = observedAt.Add(leaseDuration);
                long generation = checked(candidate.ProcessingGeneration + 1);
                int changed = await rows.Where(state => state.Id == candidate.Id && state.TenantId == candidate.TenantId &&
                    state.RegistrationProviderBindingId == candidate.RegistrationProviderBindingId &&
                    state.ProcessingGeneration == candidate.ProcessingGeneration)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(state => state.LeaseToken, token)
                        .SetProperty(state => state.LeaseExpiresAt, expiry)
                        .SetProperty(state => state.ProcessingGeneration, generation)
                        .SetProperty(state => state.ConcurrencyStamp, Guid.CreateVersion7())
                        .SetProperty(state => state.UpdatedAt, observedAt), cancellationToken);
                if (changed == 1)
                    claims.Add(new(candidate.Id, candidate.TenantId, candidate.RegistrationProviderBindingId,
                        token, generation, expiry));
            }
            await transaction.CommitAsync(cancellationToken);
            return (IReadOnlyList<RegistrationProviderSubscriptionClaim>)claims;
        });
    }
}
