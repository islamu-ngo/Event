using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Microsoft.EntityFrameworkCore;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class RegistrationRetentionExecutionScopeFactory(ITenantExploreDbContextFactory tenantFactory)
    : IRegistrationRetentionExecutionScopeFactory
{
    public async Task<IRegistrationRetentionExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        var context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
        try
        {
            if (!context.Database.IsRelational())
                throw new NotSupportedException("registration_retention_execution_requires_relational_provider");
            if (context.IsTenantFilterBypassed || context.BindTenantLease() != tenantId)
                throw new InvalidOperationException("registration_retention_runtime_tenant_not_selected");
            // No ambient scope is resolved: both ports own the selected ordinary
            // Runtime context, and the existing cleanup transaction remains intact.
            var cleanup = new RegistrationRetentionCleanupRepository(context, new EfCoreUnitOfWork(context));
            return new ExecutionScope(context, cleanup, tenantId);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private sealed class ExecutionScope(
        ExploreDbContext context, RegistrationRetentionCleanupRepository cleanup, Guid tenantId)
        : IRegistrationRetentionExecutionScope
    {
        public Task<RegistrationRetentionCleanupResult> CleanupAsync(
            DateTime utcNow, int batchSize, CancellationToken cancellationToken) =>
            cleanup.CleanupTenantAsync(tenantId, utcNow, batchSize, cancellationToken);

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
