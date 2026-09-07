namespace Explore.Secrets.Observability;

using System.Diagnostics.Metrics;
using Explore.Application.Contracts.Secrets;
using Explore.Domain.Enums;

/// <summary>
/// Centralized metrics for the secret resolver pipeline.
/// Metric names follow OpenTelemetry semantic conventions (dot.separated, lowercase).
/// The meter is registered as a singleton and consumed by <c>SecretResolver</c>
/// Values and source coordinates are never attached as tags.
/// </summary>
public sealed class SecretResolverMetrics : IDisposable
{
    /// <summary>Meter name exposed to OpenTelemetry collectors.</summary>
    public const string MeterName = "Event.Secrets";

    private readonly Meter _meter;
    private readonly Counter<long> _resolveSuccess;
    private readonly Counter<long> _resolveMiss;
    private readonly Counter<long> _resolveError;
    private readonly Counter<long> _cacheHit;
    private readonly Counter<long> _cacheMiss;
    private readonly Histogram<double> _resolveDuration;

    public SecretResolverMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        _meter = meterFactory.Create(MeterName);

        _resolveSuccess = _meter.CreateCounter<long>(
            name: "secrets.resolve.success",
            unit: "{resolution}",
            description: "Count of successful secret resolutions (value was returned).");

        _resolveMiss = _meter.CreateCounter<long>(
            name: "secrets.resolve.miss",
            unit: "{resolution}",
            description: "Count of resolutions that found a binding but the source returned no value.");

        _resolveError = _meter.CreateCounter<long>(
            name: "secrets.resolve.error",
            unit: "{resolution}",
            description: "Count of resolutions that failed due to a source error (auth, network, etc.).");

        _cacheHit = _meter.CreateCounter<long>(
            name: "secrets.cache.hit",
            unit: "{lookup}",
            description: "Count of in-memory cache hits for resolved secrets.");

        _cacheMiss = _meter.CreateCounter<long>(
            name: "secrets.cache.miss",
            unit: "{lookup}",
            description: "Count of in-memory cache misses for resolved secrets.");

        _resolveDuration = _meter.CreateHistogram<double>(
            name: "secrets.resolve.duration_ms",
            unit: "ms",
            description: "Wall-clock time for a single ResolveAsync call, excluding decorators.");
    }

    public void RecordSuccess(SecretSourceType source) =>
        _resolveSuccess.Add(1);

    public void RecordMiss(SecretSourceType? source) =>
        _resolveMiss.Add(1);

    public void RecordError(SecretSourceType source, SecretResolutionStatus status) =>
        _resolveError.Add(1, Tag("status", status.ToString()));

    public void RecordCacheHit() => _cacheHit.Add(1);

    public void RecordCacheMiss() => _cacheMiss.Add(1);

    public void RecordDuration(SecretResolutionStatus status, double elapsedMs) =>
        _resolveDuration.Record(elapsedMs, Tag("status", status.ToString()));

    private static KeyValuePair<string, object?> Tag(string key, string? value) => new(key, value);

    public void Dispose() => _meter.Dispose();
}
