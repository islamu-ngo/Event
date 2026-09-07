using Explore.Application.Contracts.Payments;
using Explore.Application.Contracts.Scheduling;
using Explore.Application.Services.Registration;
using Quartz;

namespace Explore.API.Scheduling;

[DisallowConcurrentExecution]
public sealed class PaymentReconciliationDrainJob(
    RegistrationPaymentCheckoutDispatchService checkoutDispatch,
    RegistrationPaymentReconciliationService reconciliation,
    IConfiguration configuration,
    ILogger<PaymentReconciliationDrainJob> logger) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        Uri? origin = ResolvePublicOrigin();
        RegistrationPaymentCheckoutDispatchResult firstDispatch = origin is null
            ? await DeferDispatchAsync(context.CancellationToken)
            : await DispatchAsync(origin, context.CancellationToken);
        RegistrationPaymentReconciliationResult result = await reconciliation.ReconcileDueAsync(
            new RegistrationPaymentReconciliationRequest("payment-reconciliation-drain-job"),
            context.CancellationToken);
        RegistrationPaymentCheckoutDispatchResult secondDispatch = origin is null
            ? await DeferDispatchAsync(context.CancellationToken)
            : await DispatchAsync(origin, context.CancellationToken);
        int dispatched = firstDispatch.Completed + secondDispatch.Completed;
        if (result.Claimed > 0 || firstDispatch.Claimed > 0 || secondDispatch.Claimed > 0)
        {
            logger.LogInformation(
                "Scheduled job {JobName} drained payment work. ReconciliationClaimed={Claimed} CheckoutCompleted={CheckoutCompleted} RequeuedDispatches={RequeuedDispatches} Succeeded={Succeeded} NonTerminal={NonTerminal} Unknown={Unknown} Parked={Parked} Stale={Stale}",
                ScheduledJobNames.PaymentReconciliationDrain,
                result.Claimed,
                dispatched,
                result.RequeuedDispatches,
                result.Succeeded,
                result.NonTerminal,
                result.Unknown,
                result.Parked,
                result.Stale);
        }
    }

    private Task<RegistrationPaymentCheckoutDispatchResult> DispatchAsync(Uri origin, CancellationToken cancellationToken) =>
        checkoutDispatch.DispatchDueAsync(
            new RegistrationPaymentCheckoutDispatchRequest(
                "payment-checkout-dispatch-drain-job",
                50,
                TimeSpan.FromMinutes(2),
                origin,
                new Uri(origin, "payments/checkout/success"),
                new Uri(origin, "payments/checkout/cancel")),
            cancellationToken);

    private Task<RegistrationPaymentCheckoutDispatchResult> DeferDispatchAsync(CancellationToken cancellationToken) =>
        checkoutDispatch.DeferDueForConfigurationAsync(
            "payment-checkout-dispatch-drain-job",
            50,
            TimeSpan.FromMinutes(2),
            "checkout_return_origin_invalid",
            cancellationToken);

    private Uri? ResolvePublicOrigin()
    {
        string? value = configuration["PublicBaseUrl"]
            ?? configuration["App:PublicBaseUrl"]
            ?? configuration["Application:PublicBaseUrl"];
        return HostedCheckoutReturnUrls.TryNormalizePublicBaseUrl(value, out Uri origin) ? origin : null;
    }
}
