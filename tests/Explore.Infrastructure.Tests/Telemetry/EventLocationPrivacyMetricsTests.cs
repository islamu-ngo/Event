using System.Diagnostics.Metrics;
using Explore.Application.Contracts.LocationPrivacy;
using Explore.Application.Telemetry;
using NSubstitute;
using TUnit.Core;

namespace Explore.Infrastructure.Tests.Telemetry;

[Category("EventLocationPrivacy")]
[NotInParallel("EventLocationPrivacyMeter")]
public sealed class EventLocationPrivacyMetricsTests
{
    [Test]
    public async Task Meter_UsesTheCanonicalEventLocationPrivacyName()
    {
        await Assert.That(EventLocationPrivacyMetrics.MeterName)
            .IsEqualTo("Explore.EventLocationPrivacy");
    }

    [Test]
    [Arguments(EventLocationDisclosurePurpose.Public, EventLocationDisclosureState.Available, "public", "available")]
    [Arguments(EventLocationDisclosurePurpose.Attendee, EventLocationDisclosureState.PrivateVenue, "attendee", "private_venue")]
    [Arguments(EventLocationDisclosurePurpose.Management, EventLocationDisclosureState.NeedsPrivacyReview, "management", "needs_privacy_review")]
    [Arguments(EventLocationDisclosurePurpose.Public, EventLocationDisclosureState.Hidden, "public", "hidden")]
    [Arguments(EventLocationDisclosurePurpose.Public, EventLocationDisclosureState.ToBeAnnounced, "public", "to_be_announced")]
    [Arguments(EventLocationDisclosurePurpose.Attendee, EventLocationDisclosureState.Unavailable, "attendee", "unavailable")]
    public async Task RecordDisclosure_CountsPurposeAndStateWithWireNames(
        EventLocationDisclosurePurpose purpose,
        EventLocationDisclosureState state,
        string expectedPurpose,
        string expectedState)
    {
        using var capture = new MetricsCapture();
        using EventLocationPrivacyMetrics metrics = CreateMetrics();

        metrics.RecordDisclosure(purpose, state);

        Measurement measurement = await capture.SingleAsync(
            EventLocationPrivacyMetrics.DisclosuresTotalInstrument);

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags["purpose"]?.ToString()).IsEqualTo(expectedPurpose);
        await Assert.That(measurement.Tags["state"]?.ToString()).IsEqualTo(expectedState);
    }

    [Test]
    public async Task RecordDisclosure_DoesNotEmitTenantEventOrSubjectIdentifiers()
    {
        using var capture = new MetricsCapture();
        using EventLocationPrivacyMetrics metrics = CreateMetrics();

        metrics.RecordDisclosure(
            EventLocationDisclosurePurpose.Management,
            EventLocationDisclosureState.Available);

        Measurement measurement = await capture.SingleAsync(
            EventLocationPrivacyMetrics.DisclosuresTotalInstrument);

        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("event_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("event_location_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("location_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("user_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("street_address");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("postcode");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("venue_name");
    }

    [Test]
    [Arguments(EventLocationCorrectionOutcome.Success, "success")]
    [Arguments(EventLocationCorrectionOutcome.Retry, "retry")]
    [Arguments(EventLocationCorrectionOutcome.DeadLetter, "dead_letter")]
    public async Task RecordCorrection_CountsEventTypeAndOutcome(
        EventLocationCorrectionOutcome outcome,
        string expectedStatus)
    {
        using var capture = new MetricsCapture();
        using EventLocationPrivacyMetrics metrics = CreateMetrics();

        metrics.RecordCorrection("location.privacy.corrected", outcome);

        Measurement measurement = await capture.SingleAsync(
            EventLocationPrivacyMetrics.CorrectionsTotalInstrument);

        await Assert.That(measurement.Value).IsEqualTo(1);
        await Assert.That(measurement.Tags["event_type"]?.ToString())
            .IsEqualTo("location.privacy.corrected");
        await Assert.That(measurement.Tags["status"]?.ToString()).IsEqualTo(expectedStatus);
        await Assert.That(measurement.Tags.Keys).DoesNotContain("payload");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("message_id");
        await Assert.That(measurement.Tags.Keys).DoesNotContain("aggregate_id");
    }

    [Test]
    public async Task ReviewQueueDepth_IsNotObservedBeforeTheFirstMeasurement()
    {
        using var capture = new MetricsCapture();
        using EventLocationPrivacyMetrics metrics = CreateMetrics();

        capture.RecordObservableInstruments();

        await Assert.That(capture.Snapshot(EventLocationPrivacyMetrics.ReviewQueueDepthInstrument))
            .IsEmpty();
    }

    [Test]
    public async Task RecordReviewQueueDepth_PublishesTheLatestDepthAsAGauge()
    {
        using var capture = new MetricsCapture();
        using EventLocationPrivacyMetrics metrics = CreateMetrics();

        metrics.RecordReviewQueueDepth(7);
        metrics.RecordReviewQueueDepth(51);
        capture.RecordObservableInstruments();

        Measurement measurement = await capture.SingleAsync(
            EventLocationPrivacyMetrics.ReviewQueueDepthInstrument);

        await Assert.That(measurement.Value).IsEqualTo(51);
        await Assert.That(measurement.Tags.Keys).DoesNotContain("tenant_id");
    }

    [Test]
    public async Task RecordReviewQueueDepth_RejectsNegativeDepth()
    {
        using EventLocationPrivacyMetrics metrics = CreateMetrics();

        await Assert.That(() => metrics.RecordReviewQueueDepth(-1))
            .Throws<ArgumentOutOfRangeException>();
    }

    private static EventLocationPrivacyMetrics CreateMetrics()
    {
        var meterFactory = Substitute.For<IMeterFactory>();
        meterFactory.Create(Arg.Any<MeterOptions>())
            .Returns(new Meter(EventLocationPrivacyMetrics.MeterName));
        return new EventLocationPrivacyMetrics(meterFactory);
    }

    private sealed record Measurement(
        string InstrumentName,
        long Value,
        IReadOnlyDictionary<string, object?> Tags);

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
                    if (instrument.Meter.Name == EventLocationPrivacyMetrics.MeterName)
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
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

        public void RecordObservableInstruments() => _listener.RecordObservableInstruments();

        public IReadOnlyList<Measurement> Snapshot(string instrumentName)
        {
            lock (_measurementsLock)
            {
                return [.. _measurements.Where(item => item.InstrumentName == instrumentName)];
            }
        }

        public async Task<Measurement> SingleAsync(string instrumentName)
        {
            for (var attempt = 0; attempt < 20; attempt++)
            {
                IReadOnlyList<Measurement> matches = Snapshot(instrumentName);
                if (matches.Count > 0)
                {
                    return matches[^1];
                }

                await Task.Delay(10);
            }

            throw new InvalidOperationException($"No measurement captured for '{instrumentName}'.");
        }

        public void Dispose() => _listener.Dispose();
    }
}
