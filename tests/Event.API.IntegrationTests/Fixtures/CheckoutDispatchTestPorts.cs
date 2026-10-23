using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Persistence;
using Explore.Application.Contracts.Services;
using Explore.Application.Services.Registration;
using RegistrationPaymentCheckoutDispatchService = Explore.Infrastructure.Services.RegistrationPaymentCheckoutDispatchService;
using NSubstitute;

namespace Event.Api.IntegrationTests.Fixtures;

/// <summary>
/// Retains scheduler orchestration test inputs at the new ports. Persisted/native
/// authority is exercised separately by CheckoutDispatchNativeAuthorityTests.
/// </summary>
internal static class CheckoutDispatchTestPorts
{
    internal static RegistrationPaymentCheckoutDispatchService CreateDispatcher(
        IRegistrationPaymentAttemptRepository repository,
        IHostedCheckoutSessionCreator creator,
        IHostedCheckoutSessionRetriever retriever,
        IPaymentIntentRetriever payment,
        IRegistrationOrderLifecycleService lifecycle,
        TimeProvider time,
        IPaidCheckoutActivationService activation,
        IPaidOrderAcceptanceFreshnessService freshness,
        IPaymentProviderDescriptor descriptor)
    {
        var admission = Substitute.For<ICheckoutDispatchAdmission>();
        admission.ClaimDueDispatchEffectsAsync(
                Arg.Any<string>(), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => repository.ClaimDueDispatchEffectsAsync(
                call.ArgAt<string>(0), call.ArgAt<int>(1), call.ArgAt<DateTime>(2),
                call.ArgAt<TimeSpan>(3), call.ArgAt<CancellationToken>(4)));
        var executions = Substitute.For<ICheckoutDispatchExecutionScopeFactory>();
        executions.CreateAsync(Arg.Any<CheckoutDispatchClaim>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var scope = Substitute.For<ICheckoutDispatchExecutionScope>();
                scope.Repository.Returns(repository);
                scope.CheckoutCreator.Returns(creator);
                scope.CheckoutRetriever.Returns(retriever);
                scope.PaymentIntentRetriever.Returns(payment);
                scope.OrderLifecycle.Returns(lifecycle);
                scope.CheckoutActivation.Returns(activation);
                scope.AcceptanceFreshness.Returns(freshness);
                scope.PaymentProviderDescriptor.Returns(descriptor);
                return new ValueTask<ICheckoutDispatchExecutionScope?>(scope);
            });
        return new(admission, executions, time);
    }
}
