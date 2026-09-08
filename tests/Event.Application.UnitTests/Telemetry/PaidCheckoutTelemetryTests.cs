using System.Diagnostics.Metrics;
using Explore.Application.Services.Registration;
using Explore.Application.Telemetry;

namespace ApplicationUnitTests.Telemetry;

[NotInParallel("BusinessMetricsMeter")]
public sealed class PaidCheckoutTelemetryTests
{
    [Test]
    public async Task UnknownFailureCodeCannotBecomeMetricTag()
    {
        const string piiCanary = "buyer@example.test/order/018f-secret";
        using var signal = new ActivationMeasurementSignal();
        using var telemetry = new PaidCheckoutTelemetry();

        telemetry.RecordActivation(PaidCheckoutActivationResult.Failure(
            piiCanary,
            "Untrusted detail must not become telemetry."));

        ActivationMeasurement measurement = await signal.Measurement.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(measurement.Outcome).IsEqualTo("blocked");
        await Assert.That(measurement.ReasonCategory).IsEqualTo("unknown");
        await Assert.That(measurement.Outcome).DoesNotContain(piiCanary);
        await Assert.That(measurement.ReasonCategory).DoesNotContain(piiCanary);
    }

    [Test]
    public async Task AllowedDecisionUsesClosedSuccessCategories()
    {
        using var signal = new ActivationMeasurementSignal();
        using var telemetry = new PaidCheckoutTelemetry();

        telemetry.RecordActivation(new PaidCheckoutActivationResult(
            true,
            null,
            "Paid Checkout is active."));

        ActivationMeasurement measurement = await signal.Measurement.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(measurement.Outcome).IsEqualTo("allowed");
        await Assert.That(measurement.ReasonCategory).IsEqualTo("none");
    }

    [Test]
    public async Task DirectoryOperatorIdentityUnavailableUsesExplicitCategory()
    {
        using var signal = new ActivationMeasurementSignal();
        using var telemetry = new PaidCheckoutTelemetry();

        telemetry.RecordActivation(PaidCheckoutActivationResult.Failure(
            "tenant_directory_operator_identity_unavailable",
            "Tenant directory operator identity is unavailable for paid commerce."));

        ActivationMeasurement measurement = await signal.Measurement.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.That(measurement.Outcome).IsEqualTo("blocked");
        await Assert.That(measurement.ReasonCategory).IsEqualTo("directory_operator_identity");
    }

    private sealed class ActivationMeasurementSignal : IDisposable
    {
        private readonly TaskCompletionSource<ActivationMeasurement> _measurement =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly MeterListener _listener = new();

        public ActivationMeasurementSignal()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == PaidCheckoutTelemetry.MeterName &&
                    instrument.Name == PaidCheckoutTelemetry.InstrumentName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, _, tags, _) =>
            {
                if (instrument.Name != PaidCheckoutTelemetry.InstrumentName)
                {
                    return;
                }

                IReadOnlyDictionary<string, object?> values =
                    tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
                _measurement.TrySetResult(new ActivationMeasurement(
                    values["outcome"]?.ToString() ?? string.Empty,
                    values["reason_category"]?.ToString() ?? string.Empty));
            });
            _listener.Start();
        }

        public Task<ActivationMeasurement> Measurement => _measurement.Task;

        public void Dispose() => _listener.Dispose();
    }

    private sealed record ActivationMeasurement(string Outcome, string ReasonCategory);
}
