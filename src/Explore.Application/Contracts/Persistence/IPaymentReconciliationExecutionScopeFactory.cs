using Explore.Domain;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Acquires independent ordinary authority for the persisted claim's immutable tenant.</summary>
public interface IPaymentReconciliationExecutionScopeFactory
{
    Task<IPaymentReconciliationExecutionScope> CreateAsync(Guid tenantId, CancellationToken cancellationToken);
}

public interface IPaymentReconciliationExecutionScope : IAsyncDisposable
{
    Task<PaymentAttempt?> GetAttemptAsync(
        PaymentReconciliationClaim claim, DateTime observedAt, CancellationToken cancellationToken);

    Task<bool> SettleAsync(
        PaymentReconciliationClaim claim, PaymentReconciliationDecision decision, CancellationToken cancellationToken);

    Task<PaymentReconciliationRecoveryResult> RecoverUnknownDispatchAsync(
        PaymentReconciliationClaim claim, PaymentReconciliationDecision decision, CancellationToken cancellationToken);
}

public sealed record PaymentReconciliationRecoveryResult(bool Settled, bool RequeuedDispatch);
