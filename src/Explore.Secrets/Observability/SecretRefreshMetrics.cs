// Uses System.Diagnostics.Metrics for OpenTelemetry integration with PLG stack.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Explore.Secrets.Abstractions;

namespace Explore.Secrets.Observability;

/// <summary>
/// Provides Prometheus-compatible metrics for secret refresh operations.
/// Integrates with OpenTelemetry via System.Diagnostics.Metrics.
/// </summary>
public sealed class SecretRefreshMetrics : IDisposable
{
    /// <summary>
    /// Meter name for OpenTelemetry registration.
    /// Register with: metrics.AddMeter(SecretRefreshMetrics.MeterName)
    /// </summary>
    public const string MeterName = "Explore.Secrets";

    private readonly Meter _meter;
    private readonly Counter<long> _refreshTotal;
    private readonly Counter<long> _refreshFailuresTotal;
    private readonly Histogram<double> _refreshDurationSeconds;
    private readonly UpDownCounter<int> _consecutiveFailures;
    private readonly TimeProvider _clock;

    private DateTimeOffset _lastSuccessfulRefresh = DateTimeOffset.MinValue;
    private int _currentConsecutiveFailures;
    private readonly object _lock = new();

    public SecretRefreshMetrics(
        IMeterFactory? meterFactory = null,
        TimeProvider? clock = null)
    {
        _clock = clock ?? TimeProvider.System;
        // Use factory if provided (for DI), otherwise create standalone meter
        _meter = meterFactory?.Create(MeterName) ?? new Meter(MeterName, "1.0.0");

        // Counter: Total refresh attempts
        _refreshTotal = _meter.CreateCounter<long>(
            name: "secrets_refresh_total",
            unit: "{refresh}",
            description: "Total number of secret refresh attempts");

        // Counter: Failed refresh attempts
        _refreshFailuresTotal = _meter.CreateCounter<long>(
            name: "secrets_refresh_failures_total",
            unit: "{failure}",
            description: "Total number of failed secret refresh attempts");

        // Histogram: Refresh duration in seconds
        _refreshDurationSeconds = _meter.CreateHistogram<double>(
            name: "secrets_refresh_duration_seconds",
            unit: "s",
            description: "Duration of secret refresh operations in seconds");

        // UpDownCounter: Current consecutive failures
        _consecutiveFailures = _meter.CreateUpDownCounter<int>(
            name: "secrets_consecutive_failures",
            unit: "{failure}",
            description: "Current number of consecutive refresh failures");

        // Observable gauge: Last successful refresh timestamp (Unix epoch seconds)
        _meter.CreateObservableGauge(
            name: "secrets_last_refresh_timestamp_seconds",
            observeValue: () => _lastSuccessfulRefresh == DateTimeOffset.MinValue
                ? 0
                : _lastSuccessfulRefresh.ToUnixTimeSeconds(),
            unit: "s",
            description: "Unix timestamp of the last successful secret refresh");
    }

    /// <summary>
    /// Gets the timestamp of the last successful refresh.
    /// </summary>
    public DateTimeOffset LastSuccessfulRefresh
    {
        get
        {
            lock (_lock)
            {
                return _lastSuccessfulRefresh;
            }
        }
    }

    /// <summary>
    /// Gets the current consecutive failure count.
    /// </summary>
    public int ConsecutiveFailures
    {
        get
        {
            lock (_lock)
            {
                return _currentConsecutiveFailures;
            }
        }
    }

    /// <summary>
    /// Starts a refresh operation and returns a disposable that records the duration.
    /// </summary>
    /// <param name="providerType">The provider type for tagging.</param>
    /// <returns>A disposable that records metrics when disposed.</returns>
    public RefreshOperation StartRefreshOperation(SecretProviderType providerType)
    {
        return new RefreshOperation(this, providerType, Stopwatch.StartNew());
    }

    /// <summary>
    /// Records a successful refresh operation.
    /// </summary>
    /// <param name="providerType">The provider type.</param>
    /// <param name="durationSeconds">Duration in seconds.</param>
    public void RecordRefreshSuccess(SecretProviderType providerType, double durationSeconds)
    {
        var tags = new TagList
        {
            { "provider", providerType.ToString().ToLowerInvariant() },
            { "status", "success" }
        };

        _refreshTotal.Add(1, tags);
        _refreshDurationSeconds.Record(durationSeconds, tags);

        lock (_lock)
        {
            // Reset consecutive failures on success
            if (_currentConsecutiveFailures > 0)
            {
                _consecutiveFailures.Add(-_currentConsecutiveFailures,
                    new TagList { { "provider", providerType.ToString().ToLowerInvariant() } });
                _currentConsecutiveFailures = 0;
            }

            _lastSuccessfulRefresh = _clock.GetUtcNow();
        }
    }

    /// <summary>
    /// Records a failed refresh operation.
    /// </summary>
    /// <param name="providerType">The provider type.</param>
    /// <param name="durationSeconds">Duration in seconds.</param>
    /// <param name="errorType">Type of error (e.g., "timeout", "auth_failure", "network").</param>
    public void RecordRefreshFailure(SecretProviderType providerType, double durationSeconds, string? errorType = null)
    {
        var tags = new TagList
        {
            { "provider", providerType.ToString().ToLowerInvariant() },
            { "status", "failure" }
        };

        if (!string.IsNullOrEmpty(errorType))
        {
            tags.Add("error_type", errorType);
        }

        _refreshTotal.Add(1, tags);
        _refreshFailuresTotal.Add(1, tags);
        _refreshDurationSeconds.Record(durationSeconds, tags);

        lock (_lock)
        {
            _currentConsecutiveFailures++;
            _consecutiveFailures.Add(1,
                new TagList { { "provider", providerType.ToString().ToLowerInvariant() } });
        }
    }

    public void Dispose()
    {
        _meter.Dispose();
    }

    /// <summary>
    /// Represents an in-progress refresh operation for timing.
    /// </summary>
    public readonly struct RefreshOperation : IDisposable
    {
        private readonly SecretRefreshMetrics _metrics;
        private readonly SecretProviderType _providerType;
        private readonly Stopwatch _stopwatch;

        internal RefreshOperation(SecretRefreshMetrics metrics, SecretProviderType providerType, Stopwatch stopwatch)
        {
            _metrics = metrics;
            _providerType = providerType;
            _stopwatch = stopwatch;
        }

        /// <summary>
        /// Completes the operation as successful and records metrics.
        /// </summary>
        public void Complete()
        {
            _stopwatch.Stop();
            _metrics.RecordRefreshSuccess(_providerType, _stopwatch.Elapsed.TotalSeconds);
        }

        /// <summary>
        /// Completes the operation as failed and records metrics.
        /// </summary>
        /// <param name="errorType">Optional error type for categorization.</param>
        public void Fail(string? errorType = null)
        {
            _stopwatch.Stop();
            _metrics.RecordRefreshFailure(_providerType, _stopwatch.Elapsed.TotalSeconds, errorType);
        }

        /// <summary>
        /// Disposes the operation. Does not record metrics - call Complete() or Fail() explicitly.
        /// </summary>
        public void Dispose()
        {
            // Stop the timer but don't record - caller should use Complete() or Fail()
            _stopwatch.Stop();
        }
    }
}
