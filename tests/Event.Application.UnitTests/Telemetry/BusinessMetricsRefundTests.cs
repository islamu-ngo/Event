using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;
using NSubstitute;

namespace ApplicationUnitTests.Telemetry;

[NotInParallel("BusinessMetricsMeter")]
public sealed class BusinessMetricsRefundTests
{
    [Test]
    public async Task RefundMetricsUseOnlyClosedSetNonIdentifyingTags()
    {
        using var capture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordRefundOperation("buyer@example.test", "Succeeded", "secret-value");
        metrics.RecordRefundCampaignOperation("EventCancellation", "RequiresOperator", "completed");

        Measurement refund = capture.Single("explore.refunds.operations");
        Measurement campaign = capture.Single("explore.refunds.campaign_operations");

        await Assert.That(refund.Tags.Keys).IsEquivalentTo(["operation", "status", "outcome"]);
        await Assert.That(refund.Tags["operation"]?.ToString()).IsEqualTo("unknown");
        await Assert.That(refund.Tags["status"]?.ToString()).IsEqualTo("succeeded");
        await Assert.That(refund.Tags["outcome"]?.ToString()).IsEqualTo("unknown");
        await Assert.That(campaign.Tags.Keys).IsEquivalentTo(["kind", "status", "outcome"]);
        await Assert.That(campaign.Tags["kind"]?.ToString()).IsEqualTo("event_cancellation");
        await Assert.That(campaign.Tags["status"]?.ToString()).IsEqualTo("requires_operator");
    }

    private static BusinessMetrics CreateMetrics()
    {
        var factory = Substitute.For<IMeterFactory>();
        factory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(factory);
    }

    private sealed class MetricsCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        private readonly List<Measurement> _measurements = [];

        public MetricsCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BusinessMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
                _measurements.Add(new(instrument.Name, value, tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value))));
            _listener.Start();
        }

        public Measurement Single(string instrumentName) =>
            _measurements.Single(value => value.InstrumentName == instrumentName);

        public void Dispose() => _listener.Dispose();
    }

    private sealed record Measurement(string InstrumentName, long Value, IReadOnlyDictionary<string, object?> Tags);
}
