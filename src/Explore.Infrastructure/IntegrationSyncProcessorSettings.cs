namespace Explore.Infrastructure;

public sealed class IntegrationSyncProcessorSettings
{
    public const string SectionName = "IntegrationSyncProcessor";

    public bool Enabled { get; set; } = true;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 25;
    public int MaxAttemptCount { get; set; } = 5;
    public int InitialRetryDelaySeconds { get; set; } = 5;
    public int MaxRetryDelaySeconds { get; set; } = 3600;
    public int ProcessingLeaseTimeoutSeconds { get; set; } = 300;
    public int HealthDueWarningThreshold { get; set; } = 1000;
    public int HealthStaleWarningThreshold { get; set; } = 1;
    public int HealthAmbiguousWarningThreshold { get; set; } = 1;
    public bool VerboseLogging { get; set; }

    public int CalculateRetryDelay(int failedAttemptCount)
    {
        var exponent = Math.Max(0, failedAttemptCount - 1);
        var delay = InitialRetryDelaySeconds * (int)Math.Pow(2, exponent);
        return Math.Min(delay, MaxRetryDelaySeconds);
    }
}
