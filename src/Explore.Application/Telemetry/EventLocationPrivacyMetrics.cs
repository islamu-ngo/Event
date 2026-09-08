using System.Diagnostics.Metrics;
using Explore.Application.Contracts.LocationPrivacy;

namespace Explore.Application.Telemetry;

/// <summary>
/// Terminal outcome of one durable location-privacy correction attempt, as observed by the outbox.
/// </summary>
public enum EventLocationCorrectionOutcome
{
    Success = 1,
    Retry = 2,
    DeadLetter = 3
}

/// <summary>
/// Owns the <c>Explore.EventLocationPrivacy</c> meter. Registered as a singleton so the review-queue
/// gauge keeps one process-wide last-observed value between health-check probes.
/// </summary>
public sealed class EventLocationPrivacyMetrics : IDisposable
{
    public const string MeterName = "Explore.EventLocationPrivacy";
    public const string DisclosuresTotalInstrument = "event_location_privacy_disclosures_total";
    public const string CorrectionsTotalInstrument = "event_location_privacy_corrections_total";
    public const string ReviewQueueDepthInstrument = "event_location_privacy_review_queue_depth";

    private const long DepthNotObserved = -1;

    private readonly Meter _meter;
    private readonly Counter<long> _disclosuresTotal;
    private readonly Counter<long> _correctionsTotal;
    private long _reviewQueueDepth = DepthNotObserved;

    public EventLocationPrivacyMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);

        _disclosuresTotal = _meter.CreateCounter<long>(
            DisclosuresTotalInstrument,
            unit: "{disclosure}",
            description: "Total EventLocation disclosure evaluations by request purpose and resulting disclosure state.");

        _correctionsTotal = _meter.CreateCounter<long>(
            CorrectionsTotalInstrument,
            unit: "{correction}",
            description: "Total durable location-privacy correction dispatches by outbox event type and terminal status.");

        _meter.CreateObservableGauge(
            ReviewQueueDepthInstrument,
            ObserveReviewQueueDepth,
            unit: "{event_location}",
            description: "Instance-wide count of live EventLocations still flagged for privacy remediation.");
    }

    /// <summary>
    /// Counts one evaluated disclosure. Called once per resolved EventLocation so redaction pressure is
    /// visible per surface without correlating any single event, venue, or requester.
    /// </summary>
    public void RecordDisclosure(
        EventLocationDisclosurePurpose purpose,
        EventLocationDisclosureState state) =>
        _disclosuresTotal.Add(
            1,
            new KeyValuePair<string, object?>("purpose", EventLocationDisclosureWireNames.ForPurpose(purpose)),
            new KeyValuePair<string, object?>("state", EventLocationDisclosureWireNames.ForState(state)));

    /// <summary>
    /// Counts one durable correction attempt. <paramref name="eventType"/> is a compile-time outbox
    /// constant, never operator or user input, so the dimension stays bounded.
    /// </summary>
    public void RecordCorrection(string eventType, EventLocationCorrectionOutcome outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        _correctionsTotal.Add(
            1,
            new KeyValuePair<string, object?>("event_type", eventType),
            new KeyValuePair<string, object?>("status", ToWireName(outcome)));
    }

    /// <summary>
    /// Publishes the latest observed remediation backlog. Until the first probe runs the gauge reports
    /// nothing at all, so an unscraped instance is never mistaken for an empty queue.
    /// </summary>
    public void RecordReviewQueueDepth(long depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(depth);
        Interlocked.Exchange(ref _reviewQueueDepth, depth);
    }

    public void Dispose() => _meter.Dispose();

    private IEnumerable<Measurement<long>> ObserveReviewQueueDepth()
    {
        long depth = Interlocked.Read(ref _reviewQueueDepth);
        return depth == DepthNotObserved
            ? []
            : [new Measurement<long>(depth)];
    }

    private static string ToWireName(EventLocationCorrectionOutcome outcome) => outcome switch
    {
        EventLocationCorrectionOutcome.Success => "success",
        EventLocationCorrectionOutcome.Retry => "retry",
        EventLocationCorrectionOutcome.DeadLetter => "dead_letter",
        _ => throw new ArgumentOutOfRangeException(
            nameof(outcome),
            outcome,
            "Unknown EventLocation privacy correction outcome.")
    };
}
