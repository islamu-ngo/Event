using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using Microsoft.Extensions.DependencyInjection;

namespace Explore.Infrastructure.Services;

/// <summary>Defers construction of the checkout dependency graph until the original claim has tenant authority.</summary>
public sealed class CheckoutDispatchExecutionScopeFactory(ICheckoutDispatchRuntimeScopeFactory runtime)
    : ICheckoutDispatchExecutionScopeFactory
{
    public async ValueTask<ICheckoutDispatchExecutionScope?> CreateAsync(
        CheckoutDispatchClaim claim, DateTime observedAt, CancellationToken cancellationToken)
    {
        var scope = await runtime.CreateAsync(claim, observedAt, cancellationToken);
        if (scope is null)
            return null;
        try
        {
            return new ExecutionScope(scope);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    private sealed class ExecutionScope(ICheckoutDispatchRuntimeScope scope) : ICheckoutDispatchExecutionScope
    {
        public IRegistrationPaymentAttemptRepository Repository { get; } =
            scope.Services.GetRequiredService<IRegistrationPaymentAttemptRepository>();
        public IHostedCheckoutSessionCreator CheckoutCreator { get; } = scope.Services.GetRequiredService<IHostedCheckoutSessionCreator>();
        public IHostedCheckoutSessionRetriever CheckoutRetriever { get; } = scope.Services.GetRequiredService<IHostedCheckoutSessionRetriever>();
        public IPaymentIntentRetriever PaymentIntentRetriever { get; } = scope.Services.GetRequiredService<IPaymentIntentRetriever>();
        public IRegistrationOrderLifecycleService OrderLifecycle { get; } = scope.Services.GetRequiredService<IRegistrationOrderLifecycleService>();
        public IPaidCheckoutActivationService CheckoutActivation { get; } = scope.Services.GetRequiredService<IPaidCheckoutActivationService>();
        public IPaidOrderAcceptanceFreshnessService AcceptanceFreshness { get; } = scope.Services.GetRequiredService<IPaidOrderAcceptanceFreshnessService>();
        public IPaymentProviderDescriptor PaymentProviderDescriptor { get; } = scope.Services.GetRequiredService<IPaymentProviderDescriptor>();
        public ValueTask DisposeAsync() => scope.DisposeAsync();
    }
}
