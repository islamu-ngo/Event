using System.Diagnostics.Metrics;

namespace Explore.Infrastructure.Services.Federation;

public readonly record struct AtprotoJetstreamLivenessSnapshot(
    bool IsConnected,
    DateTime? ConnectedSince,
    DateTime? DisconnectedSince,
    long Cursor);

/// <summary>
/// Shared connection state between the singleton subscriber and the readiness probe.
/// <para>
/// Deliberately tracks <em>connectivity</em>, not event flow. The subscribed collections
/// (<c>community.lexicon.calendar.*</c>) are rare network-wide — sampling the public archive showed
/// roughly two calendar records per two million firehose events — so a healthy consumer can legitimately
/// sit idle for hours. Treating "no recent event" as unhealthy would alarm constantly; treating
/// "no open subscription" as unhealthy is the signal that actually distinguishes broken from quiet.
/// </para>
/// <para>
/// For the same reason there is no seq-lag gauge here: the sealed archive tip counts every collection on
/// the network, so a collection-filtered consumer trails it by billions of seq while perfectly healthy.
/// </para>
/// </summary>
public sealed class AtprotoJetstreamLiveness
{
    private static readonly Meter Meter = new("Explore.Atproto.Jetstream", "1.0.0");
    private readonly object _lock = new();
    private bool _isConnected;
    private DateTime? _connectedSince;
    private DateTime? _disconnectedSince;
    private long _cursor;

    public AtprotoJetstreamLiveness()
    {
        Meter.CreateObservableGauge(
            "atproto.jetstream.connected",
            () => Read().IsConnected ? 1 : 0,
            description: "1 when the leased Jetstream consumer holds an open subscription, otherwise 0.");
        Meter.CreateObservableGauge(
            "atproto.jetstream.cursor",
            () => Read().Cursor,
            description: "Last applied Jetstream v2 seq for the active consumer lease.");
    }

    public void MarkConnected(DateTime observedAt, long cursor)
    {
        lock (_lock)
        {
            if (!_isConnected)
            {
                _connectedSince = observedAt;
                _disconnectedSince = null;
            }

            _isConnected = true;
            _cursor = cursor;
        }
    }

    public void MarkDisconnected(DateTime observedAt, long cursor)
    {
        lock (_lock)
        {
            if (_isConnected)
            {
                _disconnectedSince = observedAt;
                _connectedSince = null;
            }

            _isConnected = false;
            _cursor = cursor;
        }
    }

    public AtprotoJetstreamLivenessSnapshot Read()
    {
        lock (_lock)
        {
            return new(_isConnected, _connectedSince, _disconnectedSince, _cursor);
        }
    }
}
