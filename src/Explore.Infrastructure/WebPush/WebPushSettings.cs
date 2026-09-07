namespace Explore.Infrastructure.WebPush;

public sealed record WebPushSettings
{
    public const string SectionName = "WebPush";

    public bool Enabled { get; set; }
    public string VapidSubject { get; set; } = string.Empty;
    public string VapidPublicKey { get; set; } = string.Empty;
    public string VapidPrivateKey { get; set; } = string.Empty;
    public int RequestTimeoutSeconds { get; set; } = 30;
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 50;
    public int MaxAttemptCount { get; set; } = 5;
    public int InitialRetryDelaySeconds { get; set; } = 5;
    public int MaxRetryDelaySeconds { get; set; } = 3600;
    public int ProcessingLeaseTimeoutSeconds { get; set; } = 900;
    public int HealthDueDispatchWarningThreshold { get; set; } = 1000;
    public int HealthStaleProcessingWarningThreshold { get; set; } = 1;
    public int HealthTerminalFailureWarningThreshold { get; set; } = 1;
    public string ConsumerId { get; set; } = Environment.MachineName;
    public string NotificationOpenPath { get; set; } = "/notifications";
    public string NotificationRefreshPath { get; set; } = "/api/notification/stream";
    public bool VerboseLogging { get; set; }

    public int CalculateRetryDelay(int failedAttemptCount)
    {
        var exponent = Math.Max(0, failedAttemptCount - 1);
        var delay = InitialRetryDelaySeconds * (int)Math.Pow(2, exponent);
        return Math.Min(delay, MaxRetryDelaySeconds);
    }
}
