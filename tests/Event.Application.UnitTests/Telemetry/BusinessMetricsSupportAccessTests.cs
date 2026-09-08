using System.Diagnostics.Metrics;
using Explore.Application.Features.SupportAccess;
using Explore.Application.Telemetry;
using NSubstitute;

namespace ApplicationUnitTests.Telemetry;

[NotInParallel("BusinessMetricsMeter")]
public sealed class BusinessMetricsSupportAccessTests
{
    [Test]
    public async Task SupportAccessMetricsRecordExpectedBoundedTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordSupportAccessLifecycleEvent("force_stopped", "Write", "succeeded");
        metrics.RecordSupportAccessRequestAudit(
            "CommandCommitted",
            "server_error",
            "failed",
            "support_access_audit_persistence_failed");
        metrics.RecordSupportAccessSessionValidationDenial(SupportAccessFailureCodes.Disabled, "Write");
        metrics.RecordSupportAccessBoundaryDenial(
            "support_access_target_tenant_mismatch",
            "create",
            "ReadOnly");

        var measurements = await metricsCapture.AllAsync(expectedCount: 4);
        var lifecycle = measurements.Single(measurement =>
            measurement.InstrumentName == "explore.support_access.lifecycle_events");
        var requestAudit = measurements.Single(measurement =>
            measurement.InstrumentName == "explore.support_access.request_audits");
        var validation = measurements.Single(measurement =>
            measurement.InstrumentName == "explore.support_access.session_validation_denials");
        var boundary = measurements.Single(measurement =>
            measurement.InstrumentName == "explore.support_access.authorization_boundary_denials");

        await Assert.That(lifecycle.Tags["event_type"]?.ToString()).IsEqualTo("force_stopped");
        await Assert.That(lifecycle.Tags["mode"]?.ToString()).IsEqualTo("write");
        await Assert.That(lifecycle.Tags["outcome"]?.ToString()).IsEqualTo("succeeded");
        await Assert.That(lifecycle.Tags["failure_category"]?.ToString()).IsEqualTo("none");

        await Assert.That(requestAudit.Tags["event_type"]?.ToString()).IsEqualTo("command_committed");
        await Assert.That(requestAudit.Tags["outcome"]?.ToString()).IsEqualTo("server_error");
        await Assert.That(requestAudit.Tags["persistence_outcome"]?.ToString()).IsEqualTo("failed");
        await Assert.That(requestAudit.Tags["failure_category"]?.ToString()).IsEqualTo("support_access_audit_persistence_failed");

        await Assert.That(validation.Tags["reason"]?.ToString()).IsEqualTo(SupportAccessFailureCodes.Disabled);
        await Assert.That(validation.Tags["mode"]?.ToString()).IsEqualTo("write");

        await Assert.That(boundary.Tags["reason"]?.ToString()).IsEqualTo("support_access_target_tenant_mismatch");
        await Assert.That(boundary.Tags["mode"]?.ToString()).IsEqualTo("read_only");
        await Assert.That(boundary.Tags["action_class"]?.ToString()).IsEqualTo("write");
    }

    [Test]
    public async Task SupportAccessMetricsDoNotEmitSensitiveOrHighCardinalityTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();
        var rawIdentifier = Guid.NewGuid().ToString("D");

        metrics.RecordSupportAccessLifecycleEvent($"started-{rawIdentifier}", $"mode-{rawIdentifier}", $"failed-{rawIdentifier}", $"ticket-{rawIdentifier}");
        metrics.RecordSupportAccessRequestAudit($"/api/support/{rawIdentifier}", $"status-{rawIdentifier}", $"db-{rawIdentifier}", $"exception-{rawIdentifier}");
        metrics.RecordSupportAccessSessionValidationDenial($"session-{rawIdentifier}", $"actor-{rawIdentifier}");
        metrics.RecordSupportAccessBoundaryDenial($"mismatch-{rawIdentifier}", $"custom-{rawIdentifier}", $"tenant-{rawIdentifier}");

        var measurements = await metricsCapture.AllAsync(expectedCount: 4);
        var tagKeys = measurements.SelectMany(measurement => measurement.Tags.Keys).ToArray();
        var tagValues = string.Join(" ", measurements.SelectMany(measurement => measurement.Tags.Values.Select(value => value?.ToString())));

        await Assert.That(tagKeys).DoesNotContain("support_access_session_id");
        await Assert.That(tagKeys).DoesNotContain("session_id");
        await Assert.That(tagKeys).DoesNotContain("actor_user_id");
        await Assert.That(tagKeys).DoesNotContain("target_tenant_id");
        await Assert.That(tagKeys).DoesNotContain("target_tenant_user_id");
        await Assert.That(tagKeys).DoesNotContain("ticket_reference");
        await Assert.That(tagKeys).DoesNotContain("reason_text");
        await Assert.That(tagKeys).DoesNotContain("route");
        await Assert.That(tagKeys).DoesNotContain("path");
        await Assert.That(tagKeys).DoesNotContain("resource_id");
        await Assert.That(tagKeys).DoesNotContain("exception");
        await Assert.That(tagKeys).DoesNotContain("error");

        await Assert.That(tagValues).DoesNotContain(rawIdentifier);
        await Assert.That(tagValues).DoesNotContain("ticket-");
        await Assert.That(tagValues).DoesNotContain("/api/support");
        await Assert.That(tagValues).DoesNotContain("exception-");
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
