using System.ComponentModel.DataAnnotations;

namespace Explore.Application.Services.Webhooks;

public sealed class IncomingWebhookProcessingSettings
{
    public const string SectionName = "Webhooks:IncomingProcessing";

    public bool Enabled { get; set; } = true;

    [Range(1, 1000)]
    public int BatchSize { get; set; } = 50;

    [Range(1, 128)]
    public int MaxConcurrentItems { get; set; } = 8;

    [Range(5, 3600)]
    public int LeaseSeconds { get; set; } = 120;

    [Range(1, 100)]
    public int MaxAttempts { get; set; } = 8;

    [Range(1, 86400)]
    public int InitialRetryDelaySeconds { get; set; } = 30;

    [Range(1, 86400)]
    public int MaxRetryDelaySeconds { get; set; } = 3600;

    [Range(1, 3600)]
    public int PollIntervalSeconds { get; set; } = 5;

    [Range(1, 1000000)]
    public int EffectBacklogWarningThreshold { get; set; } = 500;

    [Range(1, 1000000)]
    public int EffectStaleLeaseWarningThreshold { get; set; } = 1;

    [Range(1, 1000000)]
    public int IntakeBacklogWarningThreshold { get; set; } = 500;

    [Range(1, 1000000)]
    public int IntakeStaleLeaseWarningThreshold { get; set; } = 1;
}
