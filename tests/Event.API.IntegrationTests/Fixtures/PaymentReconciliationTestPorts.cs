using Explore.Application.Contracts.Persistence;
using Explore.Domain;
using NSubstitute;

namespace Event.Api.IntegrationTests.Fixtures;

internal static class PaymentReconciliationTestPorts
{
    internal static (IPaymentReconciliationAdmission Admission, IPaymentReconciliationExecutionScopeFactory Execution)
        Create(IRegistrationPaymentAttemptRepository repository)
    {
        var admission = Substitute.For<IPaymentReconciliationAdmission>();
        admission.ClaimDueReconciliationsAsync(Arg.Any<PaymentReconciliationAcquisition>(), Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>())
            .Returns(call => repository.ClaimDueReconciliationsAsync(call.ArgAt<PaymentReconciliationAcquisition>(0),
                call.ArgAt<DateTime>(1), call.ArgAt<CancellationToken>(2)));
        admission.RecoverReconciliationAcquisitionAsync(Arg.Any<PaymentReconciliationAcquisition>(), Arg.Any<DateTime>(),
            Arg.Any<CancellationToken>())
            .Returns(call => repository.RecoverReconciliationAcquisitionAsync(call.ArgAt<PaymentReconciliationAcquisition>(0),
                call.ArgAt<DateTime>(1), call.ArgAt<CancellationToken>(2)));
        admission.GetReconciliationHealthAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(call => repository.GetReconciliationHealthAsync(call.ArgAt<DateTime>(0), call.ArgAt<CancellationToken>(1)));
        var factory = Substitute.For<IPaymentReconciliationExecutionScopeFactory>();
        factory.CreateAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            var scope = Substitute.For<IPaymentReconciliationExecutionScope>();
            scope.GetAttemptAsync(Arg.Any<PaymentReconciliationClaim>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(call => repository.GetReconciliationAttemptAsync(call.ArgAt<PaymentReconciliationClaim>(0),
                    call.ArgAt<DateTime>(1), call.ArgAt<CancellationToken>(2)));
            scope.SettleAsync(Arg.Any<PaymentReconciliationClaim>(), Arg.Any<PaymentReconciliationDecision>(), Arg.Any<CancellationToken>())
                .Returns(call => repository.SettleReconciliationAsync(call.ArgAt<PaymentReconciliationClaim>(0),
                    call.ArgAt<PaymentReconciliationDecision>(1), call.ArgAt<CancellationToken>(2)));
            scope.RecoverUnknownDispatchAsync(Arg.Any<PaymentReconciliationClaim>(),
                Arg.Any<PaymentReconciliationDecision>(), Arg.Any<CancellationToken>())
                .Returns(async call =>
                {
                    var claim = call.ArgAt<PaymentReconciliationClaim>(0);
                    var decision = call.ArgAt<PaymentReconciliationDecision>(1);
                    var token = call.ArgAt<CancellationToken>(2);
                    bool requeued = await repository.RequeueLatestUnknownDispatchAsync(
                        claim.TenantId, claim.PaymentAttemptId, decision.ObservedAt,
                        decision.ObservedAt, token);
                    bool settled = await repository.SettleReconciliationAsync(claim, decision, token);
                    return new PaymentReconciliationRecoveryResult(settled, requeued);
                });
            return Task.FromResult(scope);
        });
        return (admission, factory);
    }
}
