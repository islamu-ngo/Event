using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using Explore.Persistence.Database;

using Explore.Persistence.Repositories;

namespace Explore.Persistence.Database.ProviderPrimitives;

public sealed class PaymentReconciliationExecutionScopeFactory(ITenantExploreDbContextFactory tenantFactory)
    : IPaymentReconciliationExecutionScopeFactory
{
    public async Task<IPaymentReconciliationExecutionScope> CreateAsync(
        Guid tenantId, CancellationToken cancellationToken) =>
        new ExecutionScope(await tenantFactory.CreateDbContextAsync(tenantId, cancellationToken));

    private sealed class ExecutionScope(ExploreDbContext context) : IPaymentReconciliationExecutionScope
    {
        private readonly RegistrationPaymentAttemptRepository repository = new(context);

        public Task<PaymentAttempt?> GetAttemptAsync(
            PaymentReconciliationClaim claim, DateTime observedAt, CancellationToken cancellationToken) =>
            repository.GetReconciliationAttemptAsync(claim, observedAt, cancellationToken);

        public Task<bool> SettleAsync(
            PaymentReconciliationClaim claim, PaymentReconciliationDecision decision, CancellationToken cancellationToken) =>
            repository.SettleReconciliationAsync(claim, decision, cancellationToken);

        public Task<PaymentReconciliationRecoveryResult> RecoverUnknownDispatchAsync(
            PaymentReconciliationClaim claim, PaymentReconciliationDecision decision, CancellationToken cancellationToken) =>
            repository.RecoverUnknownDispatchReconciliationAsync(claim, decision, cancellationToken);

        public ValueTask DisposeAsync() => context.DisposeAsync();
    }
}
