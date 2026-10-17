using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;

namespace Explore.Application.Contracts.Persistence;

/// <summary>Fleet authority may return only the persisted checkout ownership tuple, never economic data.</summary>
public interface ICheckoutDispatchAdmission
{
    Task<IReadOnlyList<CheckoutDispatchClaim>> ClaimDueDispatchEffectsAsync(
        string leaseOwner, int batchSize, DateTime claimedAt, TimeSpan leaseDuration,
        CancellationToken cancellationToken);
}

/// <summary>Validates the original live claim under a fresh immutable Runtime lease before exposing scoped services.</summary>
public interface ICheckoutDispatchRuntimeScopeFactory
{
    ValueTask<ICheckoutDispatchRuntimeScope?> CreateAsync(
        CheckoutDispatchClaim claim, DateTime observedAt, CancellationToken cancellationToken);
}

public interface ICheckoutDispatchRuntimeScope : IAsyncDisposable
{
    IServiceProvider Services { get; }
}

/// <summary>Constructs provider and routing dependencies only after Runtime claim admission succeeds.</summary>
public interface ICheckoutDispatchExecutionScopeFactory
{
    ValueTask<ICheckoutDispatchExecutionScope?> CreateAsync(
        CheckoutDispatchClaim claim, DateTime observedAt, CancellationToken cancellationToken);
}

public interface ICheckoutDispatchExecutionScope : IAsyncDisposable
{
    IRegistrationPaymentAttemptRepository Repository { get; }
    IHostedCheckoutSessionCreator CheckoutCreator { get; }
    IHostedCheckoutSessionRetriever CheckoutRetriever { get; }
    IPaymentIntentRetriever PaymentIntentRetriever { get; }
    IRegistrationOrderLifecycleService OrderLifecycle { get; }
    IPaidCheckoutActivationService CheckoutActivation { get; }
    IPaidOrderAcceptanceFreshnessService AcceptanceFreshness { get; }
    IPaymentProviderDescriptor PaymentProviderDescriptor { get; }
}
