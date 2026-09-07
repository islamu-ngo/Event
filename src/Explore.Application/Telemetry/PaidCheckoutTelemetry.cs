using System.Diagnostics.Metrics;
using Explore.Application.Services.Registration;

namespace Explore.Application.Telemetry;

public interface IPaidCheckoutTelemetry
{
    void RecordActivation(PaidCheckoutActivationResult result);
}

public sealed class PaidCheckoutTelemetry : IPaidCheckoutTelemetry, IDisposable
{
    public const string MeterName = "Explore.Business";
    public const string InstrumentName = "explore.payments.checkout_activation";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _activationCounter;

    public PaidCheckoutTelemetry()
    {
        _activationCounter = _meter.CreateCounter<long>(
            InstrumentName,
            unit: "{evaluation}",
            description: "Paid Checkout activation decisions by bounded outcome and reason category.");
    }

    public void RecordActivation(PaidCheckoutActivationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _activationCounter.Add(
            1,
            new KeyValuePair<string, object?>("outcome", result.IsActive ? "allowed" : "blocked"),
            new KeyValuePair<string, object?>("reason_category", ReasonCategory(result.FailureCode)));
    }

    public void Dispose() => _meter.Dispose();

    private static string ReasonCategory(string? failureCode) => failureCode switch
    {
        null => "none",
        "payment_operator_inactive" => "operator",
        "paid_sale_control_uninitialized" or "paid_sale_stopped" => "sale_control",
        "payment_policy_unavailable" or "payment_policy_invalid" => "policy",
        "payment_currency_unsupported" => "currency",
        "payment_ceiling_exceeded" => "ceiling",
        "payment_review_required" => "review",
        "payment_organizer_unavailable" => "organizer",
        "tenant_directory_operator_identity_unavailable" => "directory_operator_identity",
        "payment_activation_invalid" => "invalid",
        _ => "unknown"
    };
}

public sealed class TelemetryPaidCheckoutActivationService(
    PaidCheckoutActivationService inner,
    IPaidCheckoutTelemetry telemetry) : IPaidCheckoutActivationService
{
    public async Task<PaidCheckoutActivationResult> EvaluateSaleControlAsync(
        Guid tenantId,
        Guid eventId,
        CancellationToken cancellationToken)
    {
        PaidCheckoutActivationResult result =
            await inner.EvaluateSaleControlAsync(tenantId, eventId, cancellationToken);
        telemetry.RecordActivation(result);
        return result;
    }

    public async Task<PaidCheckoutActivationResult> EvaluateAsync(
        PaidCheckoutActivationRequest request,
        CancellationToken cancellationToken)
    {
        PaidCheckoutActivationResult result = await inner.EvaluateAsync(request, cancellationToken);
        telemetry.RecordActivation(result);
        return result;
    }
}
