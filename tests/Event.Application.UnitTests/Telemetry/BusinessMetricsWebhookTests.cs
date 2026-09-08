using System.Diagnostics.Metrics;
using Explore.Application.Telemetry;
using NSubstitute;

namespace ApplicationUnitTests.Telemetry;

[NotInParallel("BusinessMetricsMeter")]
public sealed class BusinessMetricsWebhookTests
{
    [Test]
    public async Task RecordWebhookDeliveryFailureRecordsExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordWebhookDeliveryFailure(
            "event.published",
            "retry_scheduled",
            "http_non_success");

        var measurement = await metricsCapture.SingleAsync("explore.webhooks.delivery_failure");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags["event_type"]?.ToString()).IsEqualTo("event.published");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("retry_scheduled");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("http_non_success");
    }

    [Test]
    public async Task RecordWebhookDeliveryFailureDoesNotEmitSensitiveOrHighCardinalityTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordWebhookDeliveryFailure(
            "event.published",
            "abandoned",
            "private_network_blocked");

        var measurement = await metricsCapture.SingleAsync("explore.webhooks.delivery_failure");

        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("endpoint_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("endpoint_url");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("url");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("secret");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("secret_ref");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("message_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("payload");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("payload_json");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("response_body");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("response_body_preview");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("error");
    }

    [Test]
    public async Task RecordWebhookManualRetryRecordsBoundedUnknownsForUnsafeInput()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordWebhookManualRetry(
            "https://example.test/private",
            "operator typed this",
            "raw exception with endpoint https://example.test");

        var measurement = await metricsCapture.SingleAsync("explore.webhooks.manual_retries");

        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags["event_type"]?.ToString()).IsEqualTo("unknown");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("unknown");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("unknown");
    }

    [Test]
    public async Task RecordWebhookProviderPublishFailureRecordsExpectedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordWebhookProviderPublishFailure(
            "event.published",
            "Svix",
            "svix_provider_unavailable");

        var measurement = await metricsCapture.SingleAsync("explore.webhooks.provider_publish_failure");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags["event_type"]?.ToString()).IsEqualTo("event.published");
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("svix");
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("svix_provider_unavailable");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("message_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("endpoint_url");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("payload");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("error");
    }

    [Test]
    public async Task RecordWebhookClaimLagUsesOnlyBoundedProviderAndOperationTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordWebhookClaimLag(
            WebhookTelemetryProvider.Local,
            WebhookTelemetryOperation.Delivery,
            TimeSpan.FromSeconds(12.5));

        var measurement = await metricsCapture.SingleAsync("explore.webhooks.claim_lag");

        await Assert.That(measurement.Value).IsEqualTo(12.5);
        await Assert.That(measurement.Tags.Keys).IsEquivalentTo(["provider", "operation"]);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("local");
        await Assert.That(measurement.Tags["operation"]?.ToString()).IsEqualTo("delivery");
    }

    [Test]
    public async Task RecordWebhookProcessingOutcomeUsesClosedTelemetryVocabulary()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Svix,
            WebhookTelemetryOperation.Reconciliation,
            WebhookTelemetryOutcome.ManualReconciliation);

        var measurement = await metricsCapture.SingleAsync("explore.webhooks.processing_outcomes");

        await Assert.That(measurement.Tags.Keys).IsEquivalentTo(["provider", "operation", "outcome"]);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("svix");
        await Assert.That(measurement.Tags["operation"]?.ToString()).IsEqualTo("reconciliation");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("manual_reconciliation");
    }

    [Test]
    public async Task CoopIncomingEffectMetricsUseBoundedProviderAndOperationTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordWebhookProcessingOutcome(
            WebhookTelemetryProvider.Coop,
            WebhookTelemetryOperation.IncomingEffect,
            WebhookTelemetryOutcome.DeadLettered);

        var measurement = await metricsCapture.SingleAsync("explore.webhooks.processing_outcomes");

        await Assert.That(measurement.Tags.Keys).IsEquivalentTo(["provider", "operation", "outcome"]);
        await Assert.That(measurement.Tags["provider"]?.ToString()).IsEqualTo("coop");
        await Assert.That(measurement.Tags["operation"]?.ToString()).IsEqualTo("incoming_effect");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo("dead_lettered");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
    }

    [Test]
    public async Task SpecializedOperationalMetricsExposeOnlyBoundedDimensions()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordWebhookRetryScheduled(
            WebhookTelemetryProvider.Svix,
            WebhookTelemetryOperation.Publication);
        metrics.RecordWebhookDeadLetter(
            WebhookTelemetryProvider.Local,
            WebhookTelemetryOperation.Delivery);
        metrics.RecordWebhookManualReconciliation(WebhookTelemetryProvider.Svix);
        metrics.RecordWebhookEndpointAutoPause(WebhookTelemetryProvider.Local);
        metrics.RecordWebhookProviderHealthCheck(
            WebhookTelemetryProvider.Svix,
            WebhookTelemetryOutcome.Unhealthy);
        metrics.RecordWebhookPublicationUnknownAge(
            WebhookTelemetryProvider.Svix,
            TimeSpan.FromMinutes(3));

        var retry = await metricsCapture.SingleAsync("explore.webhooks.retries_scheduled");
        var deadLetter = await metricsCapture.SingleAsync("explore.webhooks.dead_letters");
        var reconciliation = await metricsCapture.SingleAsync("explore.webhooks.manual_reconciliations");
        var autoPause = await metricsCapture.SingleAsync("explore.webhooks.endpoint_auto_pauses");
        var providerHealth = await metricsCapture.SingleAsync("explore.webhooks.provider_health_checks");
        var unknownAge = await metricsCapture.SingleAsync("explore.webhooks.publication_unknown_age");

        await Assert.That(retry.Tags.Keys).IsEquivalentTo(["provider", "operation"]);
        await Assert.That(deadLetter.Tags.Keys).IsEquivalentTo(["provider", "operation"]);
        await Assert.That(reconciliation.Tags.Keys).IsEquivalentTo(["provider"]);
        await Assert.That(autoPause.Tags.Keys).IsEquivalentTo(["provider"]);
        await Assert.That(providerHealth.Tags.Keys).IsEquivalentTo(["provider", "outcome"]);
        await Assert.That(providerHealth.Tags["outcome"]?.ToString()).IsEqualTo("unhealthy");
        await Assert.That(unknownAge.Tags.Keys).IsEquivalentTo(["provider"]);
        await Assert.That(unknownAge.Value).IsEqualTo(180);
    }

    [Test]
    public async Task RetentionMetricsCollapseUnsafeDimensionsUnderLoad()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        for (var index = 0; index < 1_000; index++)
        {
            metrics.RecordWebhookRetentionCleanupItems(
                1,
                $"operator-mode-{index}",
                $"https://private.example/{index}");
        }

        var measurements = metricsCapture.All("explore.webhooks.retention.cleanup_items");
        var tagSeries = measurements
            .Select(measurement => string.Join(
                '|',
                measurement.Tags.OrderBy(tag => tag.Key).Select(tag => $"{tag.Key}={tag.Value}")))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        await Assert.That(measurements).Count().IsEqualTo(1_000);
        await Assert.That(tagSeries).IsEquivalentTo(["data_kind=unknown|mode=unknown"]);
    }

    [Test]
    public async Task RegistrationProviderSubscriptionLifecycleMetricsUseOnlyBoundedDimensions()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordRegistrationProviderSubscriptionOperation("renewal", "success");
        metrics.RecordRegistrationProviderSubscriptionOperation("https://tenant.example/private", "raw exception text");
        metrics.RecordRegistrationProviderSubscriptionWatchCount("expiring", 3);
        metrics.RecordRegistrationProviderSubscriptionWatchCount("expired", 1);

        IReadOnlyList<Measurement> operations = metricsCapture.All("explore.registration_providers.subscription_operations");
        IReadOnlyList<Measurement> watches = metricsCapture.All("explore.registration_providers.subscription_watches");

        await Assert.That(operations).Count().IsEqualTo(2);
        await Assert.That(operations[0].Tags.Keys).IsEquivalentTo(["operation", "outcome"]);
        await Assert.That(operations[0].Tags["operation"]?.ToString()).IsEqualTo("renewal");
        await Assert.That(operations[0].Tags["outcome"]?.ToString()).IsEqualTo("success");
        await Assert.That(operations[1].Tags["operation"]?.ToString()).IsEqualTo("unknown");
        await Assert.That(operations[1].Tags["outcome"]?.ToString()).IsEqualTo("unknown");
        await Assert.That(operations.SelectMany(measurement => measurement.Tags.Keys)).DoesNotContain("tenant_id");
        await Assert.That(operations.SelectMany(measurement => measurement.Tags.Keys)).DoesNotContain("provider");
        await Assert.That(operations.SelectMany(measurement => measurement.Tags.Keys)).DoesNotContain("error");
        await Assert.That(watches.Select(measurement => measurement.Tags["status"]?.ToString())).IsEquivalentTo(["expiring", "expired"]);
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

        public IReadOnlyList<Measurement> All(string instrumentName)
        {
            lock (_measurementsLock)
            {
                return _measurements
                    .Where(measurement => measurement.InstrumentName == instrumentName)
                    .ToArray();
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }

    private sealed record Measurement(string InstrumentName, double Value, IReadOnlyDictionary<string, object?> Tags);
}
