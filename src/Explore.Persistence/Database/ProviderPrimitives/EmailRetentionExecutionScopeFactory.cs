using Explore.Application.Contracts.Persistence;
using Explore.Persistence.Database;
using Explore.Persistence.Database.ProviderPrimitives;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class EmailRetentionExecutionScopeFactory(ITenantExploreDbContextFactory tenantFactory)
    : IEmailRetentionExecutionScopeFactory
{
    public async Task<IEmailRetentionExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(tenantId, Guid.Empty);
        var context = await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken);
        try
        {
            _ = RelationalProviderClassifier.Classify(context.Database);
            if (context.IsTenantFilterBypassed || context.BindTenantLease() != tenantId)
                throw new InvalidOperationException("email_retention_runtime_tenant_not_selected");
            return new ExecutionScope(context, new EmailDispatchOutboxRepository(context),
                new EfCoreUnitOfWork(context), tenantId);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    private sealed class ExecutionScope(
        ExploreDbContext context, EmailDispatchOutboxRepository repository, EfCoreUnitOfWork unitOfWork, Guid tenantId)
        : IEmailRetentionExecutionScope
    {
        public Task<int> CountEligibleAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken) =>
            repository.CountRetentionRedactionEligible(tenantId, cutoffUtc, batchSize, cancellationToken);

        public Task<int> RedactAsync(
            DateTime cutoffUtc, DateTime redactedAt, int batchSize, CancellationToken cancellationToken) =>
            unitOfWork.ExecuteInTransactionAsync(
                ct => repository.RedactRetentionEligible(tenantId, cutoffUtc, redactedAt, batchSize, ct),
                cancellationToken);

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
