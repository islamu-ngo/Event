using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;
using NSubstitute;

namespace ApplicationUnitTests.Telemetry;

[NotInParallel("BusinessMetricsMeter")]
public sealed class BusinessMetricsEventModerationTests
{
    [Test]
    public async Task RecordEventModerationActionRecordsExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEventModerationAction(
            "tenant-a",
            "heavy_redacted",
            "pending_storage_deletion",
            "storage_deletion_pending",
            irreversible: true);

        var measurement = await metricsCapture.SingleAsync("explore.events.moderation_actions");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags["tenant_id"]?.ToString()).IsEqualTo("tenant-a");
        await Assert.That(measurement.Tags["action_kind"]?.ToString()).IsEqualTo("heavy_redacted");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("pending_storage_deletion");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("storage_deletion_pending");
        await Assert.That(measurement.Tags["irreversible"]?.ToString()).IsEqualTo("true");
    }

    [Test]
    public async Task EventModerationMetricsDoNotEmitUnsafeOrHighCardinalityTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();
        var rawIdentifier = Guid.NewGuid().ToString("N");

        metrics.RecordEventModerationAction(
            "tenant-a",
            $"event-title-{rawIdentifier}",
            $"tenants/{rawIdentifier}/illegal.png",
            $"case-{rawIdentifier}",
            irreversible: null);

        var measurement = await metricsCapture.SingleAsync("explore.events.moderation_actions");
        var tagKeys = measurement.Tags.Keys.ToArray();
        var tagValues = string.Join(" ", measurement.Tags.Values.Select(value => value?.ToString()));

        await Assert.That(tagKeys).DoesNotContain("event_id");
        await Assert.That(tagKeys).DoesNotContain("event_title");
        await Assert.That(tagKeys).DoesNotContain("title");
        await Assert.That(tagKeys).DoesNotContain("slug");
        await Assert.That(tagKeys).DoesNotContain("description");
        await Assert.That(tagKeys).DoesNotContain("content");
        await Assert.That(tagKeys).DoesNotContain("reason_code");
        await Assert.That(tagKeys).DoesNotContain("correlation_id");
        await Assert.That(tagKeys).DoesNotContain("storage_object_id");
        await Assert.That(tagKeys).DoesNotContain("object_key");
        await Assert.That(tagKeys).DoesNotContain("path");
        await Assert.That(tagKeys).DoesNotContain("filename");
        await Assert.That(tagKeys).DoesNotContain("exception");
        await Assert.That(tagKeys).DoesNotContain("error");

        await Assert.That(tagValues).DoesNotContain(rawIdentifier);
        await Assert.That(tagValues).DoesNotContain("illegal.png");
        await Assert.That(tagValues).DoesNotContain("event-title");
        await Assert.That(tagValues).DoesNotContain("case-");
    }

    [Test]
    public async Task RecordEventReportWorkflowActionRecordsExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEventReportWorkflowAction(
            "tenant-a",
            "execute_report_decision",
            "failed",
            "decision_execution_failed");

        var measurement = await metricsCapture.SingleAsync("explore.event_reports.workflow_actions");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags["tenant_id"]?.ToString()).IsEqualTo("tenant-a");
        await Assert.That(measurement.Tags["action"]?.ToString()).IsEqualTo("execute");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("failed");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("decision_execution_failed");
    }

    [Test]
    public async Task RecordEventReportProviderSyncRecordsExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEventReportProviderSync(
            "tenant-a",
            "coop",
            "retryable_failure",
            "coop_timeout");

        var measurement = await metricsCapture.SingleAsync("explore.event_reports.provider_syncs");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags["tenant_id"]?.ToString()).IsEqualTo("tenant-a");
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("coop");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("retryable_failure");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("provider_timeout");
    }

    [Test]
    public async Task RecordEventReportProviderCallbackRecordsExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEventReportProviderCallback(
            "osprey",
            "succeeded");

        var measurement = await metricsCapture.SingleAsync("explore.event_reports.provider_callbacks");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("osprey");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("succeeded");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("none");
    }

    [Test]
    public async Task EventReportMetricsDoNotEmitUnsafeOrHighCardinalityTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();
        var rawIdentifier = Guid.NewGuid().ToString("N");

        metrics.RecordEventReportProviderCallback(
            $"https://provider.example/{rawIdentifier}?secret=value",
            $"reporter text {rawIdentifier}",
            $"raw provider payload {rawIdentifier}");

        var measurement = await metricsCapture.SingleAsync("explore.event_reports.provider_callbacks");
        var tagKeys = measurement.Tags.Keys.ToArray();
        var tagValues = string.Join(" ", measurement.Tags.Values.Select(value => value?.ToString()));

        await Assert.That(tagKeys).DoesNotContain("event_id");
        await Assert.That(tagKeys).DoesNotContain("report_id");
        await Assert.That(tagKeys).DoesNotContain("case_id");
        await Assert.That(tagKeys).DoesNotContain("decision_id");
        await Assert.That(tagKeys).DoesNotContain("reporter_text");
        await Assert.That(tagKeys).DoesNotContain("provider_payload");
        await Assert.That(tagKeys).DoesNotContain("provider_url");
        await Assert.That(tagKeys).DoesNotContain("correlation_id");
        await Assert.That(tagKeys).DoesNotContain("error");

        await Assert.That(tagValues).DoesNotContain(rawIdentifier);
        await Assert.That(tagValues).DoesNotContain("provider.example");
        await Assert.That(tagValues).DoesNotContain("reporter text");
        await Assert.That(tagValues).DoesNotContain("provider payload");
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

        public async Task<Measurement> SingleAsync(string instrumentName)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var matches = Snapshot()
                    .Where(measurement => measurement.InstrumentName == instrumentName)
                    .ToList();

                if (matches.Count > 0)
                {
                    return matches.Single();
                }

                await Task.Delay(10);
            }

            return Snapshot()
                .Where(measurement => measurement.InstrumentName == instrumentName)
                .Single();
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        private Measurement[] Snapshot()
        {
            lock (_measurementsLock)
            {
                return [.. _measurements];
            }
        }
    }

    private sealed record Measurement(string InstrumentName, long Value, IReadOnlyDictionary<string, object?> Tags);
}
