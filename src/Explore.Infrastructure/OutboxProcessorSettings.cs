namespace Explore.Infrastructure;

/// <summary>
/// Configuration settings for the generic outbox processor.
/// Bind from appsettings.json section "OutboxProcessor".
/// </summary>
public class OutboxProcessorSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "OutboxProcessor";

    /// <summary>
    /// Whether the outbox processor is enabled.
    /// Default: true.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Polling interval in seconds for the background processor.
    /// Default: 5 seconds.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Maximum number of outbox messages to process in a single batch.
    /// Default: 100.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of retry attempts before dead-lettering.
    /// Default: 5.
    /// </summary>
    public int MaxRetryCount { get; set; } = 5;

    /// <summary>
    /// Initial retry delay in seconds (exponential backoff base).
    /// Default: 1 second.
    /// </summary>
    public int InitialRetryDelaySeconds { get; set; } = 1;

    /// <summary>
    /// Maximum retry delay in seconds (caps exponential backoff).
    /// Default: 3600 seconds (1 hour).
    /// </summary>
    public int MaxRetryDelaySeconds { get; set; } = 3600;

    /// <summary>
    /// Whether to enable detailed logging for debugging.
    /// Default: false.
    /// </summary>
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Calculates the retry delay using exponential backoff.
    /// </summary>
    /// <param name="retryCount">Current retry count (0-based before increment).</param>
    /// <returns>Delay in seconds, capped at MaxRetryDelaySeconds.</returns>
    public int CalculateRetryDelay(int retryCount)
    {
        var delay = InitialRetryDelaySeconds * (int)Math.Pow(2, retryCount);
        return Math.Min(delay, MaxRetryDelaySeconds);
    }
}
