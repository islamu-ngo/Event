using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;
using NSubstitute;

namespace ApplicationUnitTests.Telemetry;

[NotInParallel("BusinessMetricsMeter")]
public sealed class BusinessMetricsAiProviderTests
{
    private static readonly string[] SensitiveTagKeys =
    [
        "prompt",
        "response",
        "content",
        "endpoint",
        "endpoint_url",
        "api_key",
        "model_id",
        "provider_request_id",
        "tenant_id",
        "user_id",
        "error"
    ];

    private static readonly string[] SensitiveTagValues =
    [
        "secret",
        "sensitive prompt",
        "assistant response",
        "https://secret.example/gpt-test",
        "gpt-test",
        "resp_test"
    ];

    [Test]
    public async Task RecordAiProviderHealthCheckRecordsExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAiProviderHealthCheck("openai-compatible", "healthy", "configured_no_probe");

        var measurement = await metricsCapture.SingleAsync("explore.ai.provider.health_checks");

        await Assert.That(measurement.Value).IsEqualTo(1d);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("openai-compatible");
        await Assert.That(measurement.Tags["status"]?.ToString()).IsEqualTo("healthy");
        await Assert.That(measurement.Tags["reason"]?.ToString()).IsEqualTo("configured_no_probe");
        await AssertNoSensitiveTagsAsync(measurement);
    }

    [Test]
    public async Task RecordAiProviderRequestDoesNotEmitSensitiveOrHighCardinalityTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAiProviderRequest("fake", "succeeded");

        var measurement = await metricsCapture.SingleAsync("explore.ai.provider.requests");

        await AssertNoSensitiveTagsAsync(measurement);
    }

    [Test]
    public async Task RecordAiProviderRequestBoundsUnexpectedProviderOutcomeAndFailureTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAiProviderRequest(
            "https://secret.example/gpt-test",
            "raw success with sensitive prompt",
            "provider said secret prompt");

        var measurement = await metricsCapture.SingleAsync("explore.ai.provider.requests");

        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("unknown");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("unknown");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("unknown");
        await AssertNoSensitiveTagsAsync(measurement);
    }

    [Test]
    public async Task RecordAiProviderRequestDurationRecordsOnlyBoundedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAiProviderRequestDuration(
            TimeSpan.FromMilliseconds(250),
            "azure-openai",
            "failed",
            "provider said secret prompt");

        var measurement = await metricsCapture.SingleAsync("explore.ai.provider.request_duration");

        await Assert.That(measurement.Value).IsGreaterThan(0d);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("azure-openai");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("failed");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("unknown");
        await AssertNoSensitiveTagsAsync(measurement);
    }

    [Test]
    public async Task RecordAiProviderTokenUsageRecordsOnlyProviderAndTokenTypeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAiProviderTokenUsage("https://secret.example/gpt-test", inputTokens: 12, outputTokens: null, totalTokens: null);

        var measurement = await metricsCapture.SingleAsync("explore.ai.provider.token_usage");

        await Assert.That(measurement.Value).IsEqualTo(12d);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("unknown");
        await Assert.That(measurement.Tags["token_type"]?.ToString()).IsEqualTo("input");
        await AssertNoSensitiveTagsAsync(measurement);
    }

    [Test]
    public async Task RecordAiProviderProposedActionsRecordsOnlyBoundedActionKindTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAiProviderProposedActions("openai", 2, "delete_event_tech_aspect");

        var measurement = await metricsCapture.SingleAsync("explore.ai.provider.proposed_actions");

        await Assert.That(measurement.Value).IsEqualTo(2d);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("openai");
        await Assert.That(measurement.Tags["action_kind"]?.ToString()).IsEqualTo("delete_event_tech_aspect");
        await AssertNoSensitiveTagsAsync(measurement);
    }

    [Test]
    public async Task RecordAiProviderProposedActionsRejectsUnboundedActionKindTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordAiProviderProposedActions("openai", 2, "secret_action_payload");

        var measurement = await metricsCapture.SingleAsync("explore.ai.provider.proposed_actions");

        await Assert.That(measurement.Value).IsEqualTo(2d);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("openai");
        await Assert.That(measurement.Tags["action_kind"]?.ToString()).IsEqualTo("unknown");
        await AssertNoSensitiveTagsAsync(measurement);
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

            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
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
                Measurement[] snapshot;
                lock (_measurementsLock)
                {
                    snapshot = [.. _measurements];
                }

                var matches = snapshot
                    .Where(measurement => measurement.InstrumentName == instrumentName)
                    .ToList();

                if (matches.Count > 0)
                {
                    return matches.Single();
                }

                await Task.Delay(10);
            }

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

    private static async Task AssertNoSensitiveTagsAsync(Measurement measurement)
    {
        foreach (var key in SensitiveTagKeys)
        {
            await Assert.That(measurement.Tags.Keys).DoesNotContain(key);
        }

        foreach (var value in measurement.Tags.Values.Select(tag => tag?.ToString() ?? string.Empty))
        {
            foreach (var sensitiveValue in SensitiveTagValues)
            {
                await Assert.That(value).DoesNotContain(sensitiveValue);
            }
        }
    }

    private sealed record Measurement(string InstrumentName, double Value, IReadOnlyDictionary<string, object?> Tags);
}
