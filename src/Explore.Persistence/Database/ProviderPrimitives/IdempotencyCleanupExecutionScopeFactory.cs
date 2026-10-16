using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;
using Explore.Persistence.Security;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class IdempotencyCleanupExecutionScopeFactory(
    ITenantExploreDbContextFactory tenantFactory,
    TimeProvider? clock = null,
    PostgresNativeQueueEnrollment? enrollment = null)
    : IIdempotencyCleanupExecutionScopeFactory
{
    public async Task<IIdempotencyCleanupExecutionScope> CreateAsync(
        Guid tenantId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        var context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
        try
        {
            var provider = RelationalProviderClassifier.Classify(context.Database);
            if (context.IsTenantFilterBypassed || context.BindTenantLease() != tenantId)
                throw new InvalidOperationException("idempotency_cleanup_runtime_tenant_not_selected");
            if (provider == RelationalProvider.PostgreSql && enrollment is null)
                throw new InvalidOperationException("idempotency_cleanup_enrollment_required");

            return new ExecutionScope(context, new IdempotencyRepository(context, clock, enrollment));
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private sealed class ExecutionScope(ExploreDbContext context, IdempotencyRepository repository)
        : IIdempotencyCleanupExecutionScope
    {
        public Task<int> CountExpiredAsync(
            DateTime expiresBeforeUtc, int maximumRows, CancellationToken cancellationToken = default) =>
            repository.CountExpiredAsync(expiresBeforeUtc, maximumRows, cancellationToken);

        public Task<int> ScrubExpiredAsync(
            DateTime expiresBeforeUtc, int maximumRows, CancellationToken cancellationToken = default) =>
            repository.ScrubExpiredAsync(expiresBeforeUtc, maximumRows, cancellationToken);

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
