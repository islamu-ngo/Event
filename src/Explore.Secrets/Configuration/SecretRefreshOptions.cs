// Controls refresh intervals, backoff behavior, and jitter.

namespace Explore.Secrets.Configuration;

/// <summary>
/// Configuration options for the secret refresh background service.
/// </summary>
public sealed class SecretRefreshOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "SecretRefresh";

    /// <summary>
    /// Whether automatic refresh is enabled.
    /// Only applicable for providers that support refresh.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Interval between refresh attempts.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initial delay before first refresh (adds jitter automatically).
    /// Default: 10 seconds.
    /// </summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Base delay for exponential backoff on failures.
    /// Default: 5 seconds.
    /// </summary>
    public TimeSpan BaseBackoffDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum delay between retry attempts.
    /// Default: 5 minutes.
    /// </summary>
    public TimeSpan MaxBackoffDelay { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum percentage of jitter to add to intervals (0.0 to 1.0).
    /// Helps prevent thundering herd on multi-instance deployments.
    /// Default: 0.1 (10%).
    /// </summary>
    public double JitterFactor { get; set; } = 0.1;

    /// <summary>
    /// Number of consecutive failures before marking provider unhealthy.
    /// Default: 3.
    /// </summary>
    public int UnhealthyThreshold { get; set; } = 3;

    /// <summary>
    /// Calculates the backoff delay for a given failure count.
    /// Uses exponential backoff with jitter, capped at MaxBackoffDelay.
    /// </summary>
    /// <param name="consecutiveFailures">Number of consecutive failures.</param>
    /// <returns>The calculated delay.</returns>
    public TimeSpan CalculateBackoffDelay(int consecutiveFailures)
    {
        if (consecutiveFailures <= 0)
            return TimeSpan.Zero;

        // Exponential backoff: base * 2^(failures-1)
        var exponentialMs = BaseBackoffDelay.TotalMilliseconds * Math.Pow(2, consecutiveFailures - 1);
        var cappedMs = Math.Min(exponentialMs, MaxBackoffDelay.TotalMilliseconds);

        // Add jitter
        var jitter = Random.Shared.NextDouble() * JitterFactor * cappedMs;

        return TimeSpan.FromMilliseconds(cappedMs + jitter);
    }

    /// <summary>
    /// Adds jitter to a time interval.
    /// </summary>
    /// <param name="interval">The base interval.</param>
    /// <returns>The interval with added jitter.</returns>
    public TimeSpan AddJitter(TimeSpan interval)
    {
        var jitter = Random.Shared.NextDouble() * JitterFactor * interval.TotalMilliseconds;
        return interval + TimeSpan.FromMilliseconds(jitter);
    }
}
