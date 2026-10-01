using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Infrastructure;
using Explore.Application.Contracts.Notifications;
using Explore.Application.Contracts.Services;
using Explore.Application.Telemetry;
using NSubstitute;

namespace ApplicationUnitTests.Telemetry;

[NotInParallel("BusinessMetricsMeter")]
public sealed class BusinessMetricsEmailDispatchTests
{
    [Test]
    [Arguments(EmailDispatchDrainOutcome.RetryScheduled, "retry_scheduled", "smtp_send_failed")]
    [Arguments(EmailDispatchDrainOutcome.Parked, "parked", "smtp_configuration_unavailable")]
    [Arguments(EmailDispatchDrainOutcome.Unknown, "unknown", "smtp_outcome_unknown")]
    public async Task RecordEmailDispatchAttemptRecordsExpectedSafeTags(
        EmailDispatchDrainOutcome outcome, string expectedOutcome, string expectedFailureCategory)
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEmailDispatchAttempt(outcome);

        var measurement = metricsCapture.Single("explore.email_dispatch.attempts");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo(expectedOutcome);
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo(expectedFailureCategory);
        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
    }

    [Test]
    public async Task RecordEmailDispatchAttemptDoesNotEmitSensitiveOrHighCardinalityTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEmailDispatchAttempt(EmailDispatchDrainOutcome.Sent);

        var measurement = metricsCapture.Single("explore.email_dispatch.attempts");

        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("recipient");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("recipient_email");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("subject");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("body");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("html_body");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("plain_text_body");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("secret");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("provider_message_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("error");
    }

    [Test]
    [Arguments(EmailDispatchPublishOutcome.Disabled, "disabled")]
    [Arguments(EmailDispatchPublishOutcome.Confirmed, "confirmed")]
    [Arguments(EmailDispatchPublishOutcome.Returned, "returned")]
    [Arguments(EmailDispatchPublishOutcome.Nacked, "nacked")]
    [Arguments(EmailDispatchPublishOutcome.Failed, "failed")]
    public async Task RecordEmailDispatchRabbitMqPublishPreservesOutcomeLabels(
        EmailDispatchPublishOutcome outcome, string expectedLabel)
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();
        metrics.RecordEmailDispatchRabbitMqPublish(outcome);

        var measurement = metricsCapture.Single("explore.email_dispatch.rabbitmq.publishes");
        await Assert.That(measurement.Tags["outcome"]).IsEqualTo(expectedLabel);
        await Assert.That(measurement.Tags["failure_category"]).IsEqualTo("none");
    }

    [Test]
    [Arguments(EmailDispatchConsumeOutcome.Acked, "acked")]
    [Arguments(EmailDispatchConsumeOutcome.Rejected, "rejected")]
    [Arguments(EmailDispatchConsumeOutcome.Nacked, "nacked")]
    [Arguments(EmailDispatchConsumeOutcome.Replayed, "replayed")]
    [Arguments(EmailDispatchConsumeOutcome.Parked, "parked")]
    public async Task RecordEmailDispatchRabbitMqConsumeRecordsExpectedSafeTags(
        EmailDispatchConsumeOutcome outcome, string expectedLabel)
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEmailDispatchRabbitMqConsume(outcome, "none");

        var measurement = metricsCapture.Single("explore.email_dispatch.rabbitmq.consumes");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags["outcome"]?.ToString()).IsEqualTo(expectedLabel);
        await Assert.That(measurement.Tags["failure_category"]?.ToString()).IsEqualTo("none");
    }

    [Test]
    public async Task RecordEmailDispatchRabbitMqConsumeDoesNotEmitSensitiveOrHighCardinalityTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEmailDispatchRabbitMqConsume(EmailDispatchConsumeOutcome.Rejected, "missing_outbox");

        var measurement = metricsCapture.Single("explore.email_dispatch.rabbitmq.consumes");

        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("recipient");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("recipient_email");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("subject");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("body");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("html_body");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("plain_text_body");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("secret");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("provider_message_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("publish_event_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("delivery_tag");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("error");
    }

    [Test]
    [Arguments(EmailDispatchEligibilityOutcome.Skipped, "recipient_email_unverified", "skipped", "recipient_email_unverified")]
    [Arguments(EmailDispatchEligibilityOutcome.RateDeferred, "smtp_rate_deferred", "rate_deferred", "smtp_rate_deferred")]
    [Arguments((EmailDispatchEligibilityOutcome)int.MaxValue, "unrecognized-provider-detail", "other", "other")]
    public async Task RecordEmailDispatchOperationalOutcomePreservesBoundedExportedLabels(
        EmailDispatchEligibilityOutcome outcome, string reason, string expectedOutcome, string expectedReason)
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEmailDispatchOperationalOutcome(outcome, reason);

        var measurement = metricsCapture.Single("explore.email_dispatch.operational_outcomes");
        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags.Keys).IsEquivalentTo(["outcome", "reason"]);
        await Assert.That(measurement.Tags["outcome"]).IsEqualTo(expectedOutcome);
        await Assert.That(measurement.Tags["reason"]).IsEqualTo(expectedReason);
    }

    [Test]
    public async Task RecordEmailDispatchOperationalSignalsUsesOnlyBoundedSafeTags()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEmailDispatchTenantBacklog(3, 17);
        metrics.RecordEmailDispatchOldestPendingAge(125.5);
        metrics.RecordEmailDispatchOptionalReminderDeferral(true);
        metricsCapture.Observe();

        var backlog = metricsCapture.Single("explore.email_dispatch.tenant_backlog");
        var oldest = metricsCapture.Single("explore.email_dispatch.oldest_pending_age");
        var deferral = metricsCapture.Single("explore.email_dispatch.optional_reminder_deferral");

        await Assert.That(backlog.Value).IsEqualTo(17);
        await Assert.That(backlog.Tags.Keys).IsEquivalentTo(["sample_rank"]);
        await Assert.That(backlog.Tags["sample_rank"]).IsEqualTo(3);
        await Assert.That(oldest.DoubleValue).IsEqualTo(125.5);
        await Assert.That(oldest.Tags).IsEmpty();
        await Assert.That(deferral.Value).IsEqualTo(1);
        await Assert.That(deferral.Tags).IsEmpty();
    }

    [Test]
    public async Task OptionalReminderDeferralGaugeExportsCurrentState()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();

        metrics.RecordEmailDispatchOptionalReminderDeferral(false);
        metricsCapture.Observe();
        metrics.RecordEmailDispatchOptionalReminderDeferral(true);
        metricsCapture.Observe();

        var measurement = metricsCapture.Latest("explore.email_dispatch.optional_reminder_deferral");

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags).IsEmpty();
    }

    [Test]
    public async Task EmailDispatchOutcomeTagsUseClosedVocabulariesWithOtherFallback()
    {
        using var metricsCapture = new MetricsCapture();
        using var metrics = CreateMetrics();
        metrics.RecordEmailDispatchAttempt((EmailDispatchDrainOutcome)int.MaxValue);
        metrics.RecordEmailDispatchOperationalOutcome(EmailDispatchEligibilityOutcome.Skipped, "recipient_email_unverified");
        metrics.RecordEmailDispatchRabbitMqPublish((EmailDispatchPublishOutcome)int.MaxValue, "provider-message-456");
        metrics.RecordEmailDispatchRabbitMqConsume((EmailDispatchConsumeOutcome)int.MaxValue, "delivery-456");

        var attempt = metricsCapture.Single("explore.email_dispatch.attempts");
        var operational = metricsCapture.Single("explore.email_dispatch.operational_outcomes");
        var publish = metricsCapture.Single("explore.email_dispatch.rabbitmq.publishes");
        var consume = metricsCapture.Single("explore.email_dispatch.rabbitmq.consumes");

        await Assert.That(attempt.Tags["outcome"]).IsEqualTo("other");
        await Assert.That(attempt.Tags["failure_category"]).IsEqualTo("other");
        await Assert.That(operational.Tags["outcome"]).IsEqualTo("skipped");
        await Assert.That(operational.Tags["reason"]).IsEqualTo("recipient_email_unverified");
        await Assert.That(publish.Tags["outcome"]).IsEqualTo("other");
        await Assert.That(publish.Tags["failure_category"]).IsEqualTo("other");
        await Assert.That(publish.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(consume.Tags["outcome"]).IsEqualTo("other");
        await Assert.That(consume.Tags["failure_category"]).IsEqualTo("other");
        await Assert.That(consume.Tags.Keys).DoesNotContain("tenant_id");
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
                        0,
                        tags.ToArray().ToDictionary(tag => tag.Key, tag => tag.Value),
                        measurement));
                }
            });

            _listener.Start();
        }

        public void Observe()
        {
            _listener.RecordObservableInstruments();
        }

        public Measurement Latest(string instrumentName)
        {
            lock (_measurementsLock)
            {
                return _measurements.Last(value => value.InstrumentName == instrumentName);
            }
        }

        public Measurement Single(string instrumentName)
        {
            lock (_measurementsLock)
            {
                return _measurements.Single(value => value.InstrumentName == instrumentName);
            }
        }

        public void Dispose()
        {
            _listener.Dispose();
        }
    }

    private sealed record Measurement(
        string InstrumentName,
        long Value,
        IReadOnlyDictionary<string, object?> Tags,
        double? DoubleValue = null);
}
