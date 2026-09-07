using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;
using NSubstitute;

namespace ApplicationUnitTests.Telemetry;

/// <summary>
/// Authorization decisions are emitted on every request, so a single unbounded dimension here multiplies
/// across the busiest path in the system. These tests pin the dimension set rather than sampling it.
/// </summary>
[NotInParallel("BusinessMetricsMeter")]
public sealed class BusinessMetricsAuthorizationTests
{
    private const string CounterName = "explore.authorization.decisions";
    private const string HistogramName = "explore.authorization.decision.duration";

    /// <summary>The complete, closed dimension set. Adding one means changing this test deliberately.</summary>
    private static readonly string[] ExpectedTagKeys =
        ["resource_kind", "action", "outcome", "reason_code", "provider"];

    [Test]
    public async Task AuthorizationDecisionRecordsCapabilityOutcomeReasonAndProvider()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAuthorizationDecision(
            resourceKind: "islamuevent_event",
            action: "update",
            outcome: "denied",
            reasonCode: "provider_unavailable",
            providerId: "cerbos",
            durationMs: 12.5);

        var measurements = await metricsCapture.AllAsync(expectedCount: 2);
        var counter = measurements.Single(measurement => measurement.InstrumentName == CounterName);

        await Assert.That(counter.Value).IsEqualTo(1d);
        await Assert.That(counter.Tags["resource_kind"]?.ToString()).IsEqualTo("islamuevent_event");
        await Assert.That(counter.Tags["action"]?.ToString()).IsEqualTo("update");
        await Assert.That(counter.Tags["outcome"]?.ToString()).IsEqualTo("denied");
        await Assert.That(counter.Tags["reason_code"]?.ToString()).IsEqualTo("provider_unavailable");
        await Assert.That(counter.Tags["provider"]?.ToString()).IsEqualTo("cerbos");
    }

    /// <summary>
    /// Duration is required by Task 3.2 and has to carry the same dimensions as the count, otherwise
    /// "which capability is slow?" cannot be answered from the histogram alone.
    /// </summary>
    [Test]
    public async Task AuthorizationDecisionRecordsDurationWithTheSameDimensionsAsTheCount()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAuthorizationDecision(
            "islamuevent_event", "view", "allowed", "allowed", "local", durationMs: 42.5);

        var measurements = await metricsCapture.AllAsync(expectedCount: 2);
        var counter = measurements.Single(measurement => measurement.InstrumentName == CounterName);
        var duration = measurements.Single(measurement => measurement.InstrumentName == HistogramName);

        await Assert.That(duration.Value).IsEqualTo(42.5);
        await Assert.That(duration.Tags.OrderBy(tag => tag.Key, StringComparer.Ordinal))
            .IsEquivalentTo(counter.Tags.OrderBy(tag => tag.Key, StringComparer.Ordinal));
    }

    /// <summary>
    /// A negative duration would come from a clock going backwards, not from a fast decision. Recording
    /// it would poison the histogram's aggregates permanently.
    /// </summary>
    [Test]
    public async Task AuthorizationDecisionClampsNegativeDurationToZero()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAuthorizationDecision(
            "islamuevent_event", "view", "allowed", "allowed", "local", durationMs: -5);

        var measurements = await metricsCapture.AllAsync(expectedCount: 2);
        var duration = measurements.Single(measurement => measurement.InstrumentName == HistogramName);

        await Assert.That(duration.Value).IsEqualTo(0d);
    }

    /// <summary>
    /// The dimension set is closed. In particular the observed policy revision must never become a tag:
    /// it is bounded at any instant but changes on every policy publish, so over a retention window it
    /// multiplies every other dimension without bound.
    /// </summary>
    [Test]
    public async Task AuthorizationDecisionEmitsNoUnboundedOrSensitiveDimensions()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAuthorizationDecision(
            "islamuevent_event", "update", "denied", "denied", "cerbos", durationMs: 1);

        var measurements = await metricsCapture.AllAsync(expectedCount: 2);

        foreach (var measurement in measurements)
        {
            await Assert.That(measurement.Tags.Keys.OrderBy(key => key, StringComparer.Ordinal))
                .IsEquivalentTo(ExpectedTagKeys.OrderBy(key => key, StringComparer.Ordinal));
        }

        var tagKeys = measurements.SelectMany(measurement => measurement.Tags.Keys).ToArray();

        await Assert.That(tagKeys).DoesNotContain("observed_revision");
        await Assert.That(tagKeys).DoesNotContain("revision");
        await Assert.That(tagKeys).DoesNotContain("resource_id");
        await Assert.That(tagKeys).DoesNotContain("tenant_id");
        await Assert.That(tagKeys).DoesNotContain("user_id");
        await Assert.That(tagKeys).DoesNotContain("subject");
        await Assert.That(tagKeys).DoesNotContain("facts");
        await Assert.That(tagKeys).DoesNotContain("token");
        await Assert.That(tagKeys).DoesNotContain("correlation_id");
    }

    private static BusinessMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>()).Returns(new Meter(BusinessMetrics.MeterName));
        return new BusinessMetrics(meterFactory);
    }

    /// <summary>
    /// Captures both <see cref="long"/> counters and <see cref="double"/> histograms, unlike the
    /// long-only captures elsewhere in this suite — the decision duration is a double.
    /// </summary>
    private sealed class MetricsCapture : IDisposable
    {
        private readonly MeterListener _listener;
        private readonly Lock _measurementsLock = new();
        private readonly List<Measurement> _measurements = [];

        public MetricsCapture()
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name == BusinessMetrics.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                }
            };

            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
                Record(instrument.Name, measurement, tags));
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
                Record(instrument.Name, measurement, tags));

            _listener.Start();
        }

        public async Task<IReadOnlyList<Measurement>> AllAsync(int expectedCount)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var snapshot = Snapshot();
                if (snapshot.Length >= expectedCount)
                {
                    return snapshot;
                }

                await Task.Delay(10);
            }

            return Snapshot();
        }

        public void Dispose() => _listener.Dispose();

        private void Record(string instrumentName, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            var captured = tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value);
            lock (_measurementsLock)
            {
                _measurements.Add(new Measurement(instrumentName, value, captured));
            }
        }

        private Measurement[] Snapshot()
        {
            lock (_measurementsLock)
            {
                return [.. _measurements];
            }
        }
    }

    private sealed record Measurement(
        string InstrumentName,
        double Value,
        IReadOnlyDictionary<string, object?> Tags);
}
