namespace Explore.Infrastructure;

public class EmailDispatchProcessorSettings
{
    public const string SectionName = "EmailDispatchProcessor";

    public bool Enabled { get; set; } = true;
    public EmailDispatchProcessorMode Mode { get; set; } = EmailDispatchProcessorMode.Quartz;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
    public int MaxRowsPerTenantPerBatch { get; set; } = 5;
    public int MaxConcurrentDispatches { get; set; } = 8;
    public int MaxConcurrentDispatchesPerTenant { get; set; } = 2;
    public int GlobalSmtpRateLimitPerMinute { get; set; } = 120;
    public int TenantSmtpRateLimitPerMinute { get; set; } = 30;
    public int OptionalBacklogHighWatermark { get; set; } = 1000;
    public int OptionalBacklogLowWatermark { get; set; } = 500;
    public int MaxAttemptCount { get; set; } = 5;
    public int InitialRetryDelaySeconds { get; set; } = 5;
    public int MaxRetryDelaySeconds { get; set; } = 3600;
    public int ProcessingLeaseTimeoutSeconds { get; set; } = 900;
    public int HealthDueDispatchWarningThreshold { get; set; } = 1000;
    public int HealthStaleProcessingWarningThreshold { get; set; } = 1;
    public int HealthUnknownWarningThreshold { get; set; } = 1;
    public int HealthDeadLetterWarningThreshold { get; set; } = 1;
    public int HealthOldestPendingWarningSeconds { get; set; } = 900;
    public int HealthTenantBacklogWarningThreshold { get; set; } = 250;
    public int HealthTenantSampleLimit { get; set; } = 10;
    public string ConsumerId { get; set; } = Environment.MachineName;
    public bool VerboseLogging { get; set; }

    public int CalculateRetryDelay(int failedAttemptCount)
    {
        var exponent = Math.Max(0, failedAttemptCount - 1);
        var delay = InitialRetryDelaySeconds * (int)Math.Pow(2, exponent);
        return Math.Min(delay, MaxRetryDelaySeconds);
    }
}

public enum EmailDispatchProcessorMode
{
    Disabled = 0,
    Quartz = 1,
    HostedService = 2
}
