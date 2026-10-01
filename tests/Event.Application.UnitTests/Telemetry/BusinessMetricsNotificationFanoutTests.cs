using System.Diagnostics.Metrics;
using Explore.Application.Services;
using Explore.Application.Telemetry;
using NSubstitute;

namespace ApplicationUnitTests.Telemetry;

[NotInParallel("BusinessMetricsMeter")]
public sealed class BusinessMetricsNotificationFanoutTests
{
    [Test]
    public async Task RecordNotificationFanoutRunRecordsExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordNotificationFanoutRun(EventPublishedNotificationFanoutService.FanoutKind, EventPublishedNotificationFanoutService.OutcomeCompleted);

        var measurement = metricsCapture.Single("explore.notifications.fanout_runs");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags["fanout_kind"]?.ToString()).IsEqualTo(EventPublishedNotificationFanoutService.FanoutKind);
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo(EventPublishedNotificationFanoutService.OutcomeCompleted);
    }

    [Test]
    public async Task RecordNotificationFanoutSubscribersRecordsAggregatedCountWithSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordNotificationFanoutSubscribers(7, EventPublishedNotificationFanoutService.FanoutKind, EventPublishedNotificationFanoutService.OutcomeNotificationCreated);

        var measurement = metricsCapture.Single("explore.notifications.fanout_subscribers");

        await Assert.That(measurement.Value).IsEqualTo(7);
        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags["fanout_kind"]?.ToString()).IsEqualTo(EventPublishedNotificationFanoutService.FanoutKind);
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo(EventPublishedNotificationFanoutService.OutcomeNotificationCreated);
    }

    [Test]
    public async Task NotificationFanoutMetricsDoNotEmitSensitiveOrHighCardinalityTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordNotificationFanoutRun(EventPublishedNotificationFanoutService.FanoutKind, EventPublishedNotificationFanoutService.OutcomeFailed);

        var measurement = metricsCapture.Single("explore.notifications.fanout_runs");

        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("event_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("actor_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("source_actor_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("subscriber_user_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("subscriber_tenant_user_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("notification_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("deduplication_key");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("event_title");
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }

    private sealed class MetricsCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly Lock _measurementsLock = new();
        private readonly List<Measurement> _measurements = [];

        public MetricsCapture()
        {
            _listener = new MeterListener();
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == BusinessMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
            {
                lock (_measurementsLock)
                {
                    _measurements.Add(new Measurement(
                        instrument.Name,
                        measurement,
                        tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value)));
                }
            });

            _listener.Start();
        }

        public Measurement Single(string instrumentName)
        {
            lock (_measurementsLock)
            {
                return _measurements
                    .Where(measurement => measurement.InstrumentName == instrumentName)
                    .Single();
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }

    private sealed record Measurement(string InstrumentName, long Value, IReadOnlyDictionary<string, object?> Tags);
}
